using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The inspector of an articulated joint: its fields; a word where no rig lists it; Scene
    /// handles, in its rest frame, for its anchor (a position handle for the point, a rotation
    /// handle for the axes) and its limits (an arc about the anchor's axis for each turn, from
    /// its min to its max -- a full circle where it is unbounded -- and a span along the axis
    /// for each slide), each with the value it stands at; and its rig shown in the Scene view
    /// while the inspector is open, posed from the values, through the one keeper of what rigs
    /// show outside play mode (<see cref="ArticulatedRigPreview"/>), which gives the parts back
    /// as the last of its rig's inspectors closes, before a scene is saved and wherever else it
    /// does; and, where the part stands off where its rest and values pose it (a move the
    /// preview yielded to), a way to make that its rest (Capture Rest From Transform).
    /// </summary>
    [CustomEditor(typeof(ArticulatedJoint))]
    [CanEditMultipleObjects]
    sealed class ArticulatedJointEditor : UnityEditor.Editor
    {
        void OnEnable() => ArticulatedRigPreview.Open(RigsOf(targets));

        void OnDisable() => ArticulatedRigPreview.Close(RigsOf(targets));

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            if (serializedObject.ApplyModifiedProperties()) ArticulatedRigPreview.Show(RigsOf(targets));
            foreach (Object o in targets)
            {
                if (!(o is ArticulatedJoint joint) || RigOf(joint) != null) continue;
                EditorGUILayout.HelpBox($"No Articulated Rig above '{joint.name}' lists it, so nothing poses it: add it to its " +
                                        "rig's Joints (Tools > unitytk > Articulated Rig > Add Joint).", MessageType.Warning);
                break;
            }
            var off = new List<ArticulatedJoint>();
            foreach (Object o in targets)
                if (o is ArticulatedJoint joint && RigOf(joint) != null && ArticulatedRigActions.RestDiffers(joint)) off.Add(joint);
            if (off.Count == 0) return;
            EditorGUILayout.HelpBox(off.Count == 1
                ? $"'{off[0].name}' stands off where its rest and values pose it, so it moves there in play."
                : $"{off.Count} joints stand off where their rests and values pose them, so they move there in play.",
                MessageType.Info);
            if (GUILayout.Button("Capture Rest From Transform"))
                OpContext.RunMenu("Capture Rest From Transform", ctx =>
                {
                    foreach (ArticulatedJoint joint in off) ArticulatedRigActions.CaptureRest(joint, ctx);
                });
        }

        /// <summary>The rig above <paramref name="joint"/> that lists it, or null.</summary>
        internal static ArticulatedRig RigOf(ArticulatedJoint joint)
        {
            if (joint == null) return null;
            foreach (ArticulatedRig rig in joint.GetComponentsInParent<ArticulatedRig>(true))
                if (rig.IndexOf(joint) >= 0) return rig;
            return null;
        }

        static IEnumerable<ArticulatedRig> RigsOf(Object[] objects)
        {
            foreach (Object o in objects)
            {
                ArticulatedRig rig = RigOf(o as ArticulatedJoint);
                if (rig != null) yield return rig;
            }
        }

        void OnSceneGUI()
        {
            var joint = (ArticulatedJoint)target;
            if (joint == null) return;
            ArticulatedRig rig = RigOf(joint);
            Matrix4x4 rest;
            if (rig == null || !rig.TryGetRestFrame(joint, out rest))
            {
                // No rig poses it: its rest under its scene parent.
                Matrix4x4 parent = joint.transform.parent != null ? joint.transform.parent.localToWorldMatrix : Matrix4x4.identity;
                rest = parent * Matrix4x4.TRS(joint.restPosition, Normalized(joint.restRotation), Vector3.one);
            }
            using (new Handles.DrawingScope(rest))
            {
                Vector3 point = joint.anchorPosition;
                Quaternion axes = Normalized(joint.anchorRotation);
                float radius = HandleUtility.GetHandleSize(point) * 0.9f;
                foreach (ArticulatedJoint.Channel channel in joint.channels)
                    if (channel != null) Limit(joint, channel, point, axes, radius);

                EditorGUI.BeginChangeCheck();
                Vector3 moved = Handles.PositionHandle(point, axes);
                Quaternion turned = Handles.RotationHandle(axes, point);
                if (!EditorGUI.EndChangeCheck()) return;
                serializedObject.Update();
                serializedObject.FindProperty(nameof(ArticulatedJoint.anchorPosition)).vector3Value = moved;
                serializedObject.FindProperty(nameof(ArticulatedJoint.anchorRotation)).quaternionValue = turned;
                if (serializedObject.ApplyModifiedProperties() && rig != null) ArticulatedRigPreview.Show(new[] { rig });
            }
        }

        // One channel's limits about (a turn) or along (a slide) its anchor axis, with its value.
        static void Limit(ArticulatedJoint joint, ArticulatedJoint.Channel channel, Vector3 point, Quaternion axes, float radius)
        {
            int index = (int)channel.axis % 3;
            Vector3 axis = axes * Unit(index);
            float value = joint.GetValue(channel.axis);
            Color colour = index == 0 ? Handles.xAxisColor : index == 1 ? Handles.yAxisColor : Handles.zAxisColor;
            using (new Handles.DrawingScope(colour))
            {
                if (channel.axis <= ArticulatedJoint.Axis.Rz)
                {
                    Vector3 from = axes * Unit((index + 1) % 3);
                    if (channel.hasMin && channel.hasMax)
                        Handles.DrawWireArc(point, axis, Quaternion.AngleAxis(channel.min, axis) * from, channel.max - channel.min, radius);
                    else
                        Handles.DrawWireDisc(point, axis, radius);
                    if (channel.hasMin) Handles.DrawLine(point, point + Quaternion.AngleAxis(channel.min, axis) * from * radius);
                    if (channel.hasMax) Handles.DrawLine(point, point + Quaternion.AngleAxis(channel.max, axis) * from * radius);
                    Handles.DrawDottedLine(point, point + Quaternion.AngleAxis(value, axis) * from * radius * 1.15f, 3f);
                }
                else
                {
                    float lo = channel.hasMin ? channel.min : -radius;
                    float hi = channel.hasMax ? channel.max : radius;
                    if (channel.hasMin && channel.hasMax) Handles.DrawLine(point + axis * lo, point + axis * hi);
                    else Handles.DrawDottedLine(point + axis * lo, point + axis * hi, 3f);
                    float tick = radius * 0.08f;
                    if (channel.hasMin) Handles.DotHandleCap(0, point + axis * lo, Quaternion.identity, tick, EventType.Repaint);
                    if (channel.hasMax) Handles.DotHandleCap(0, point + axis * hi, Quaternion.identity, tick, EventType.Repaint);
                    Handles.SphereHandleCap(0, point + axis * value, Quaternion.identity, tick * 1.5f, EventType.Repaint);
                }
            }
        }

        static Vector3 Unit(int index) => index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;

        // A rotation the Inspector left all zeros is the identity, as Unity treats it.
        static Quaternion Normalized(Quaternion q)
            => q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f ? Quaternion.identity : q.normalized;
    }
}
