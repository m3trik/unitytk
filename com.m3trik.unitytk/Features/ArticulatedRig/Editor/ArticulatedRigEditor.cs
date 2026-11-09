using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The inspector of an articulated rig: its fields; why it does not pose, where its joints
    /// make no rig; in play mode, a way back to the animation while a pose holds; and the rig
    /// shown in the Scene view while the inspector is open, posed from its values, through the
    /// one keeper of what rigs show outside play mode (<see cref="ArticulatedRigPreview"/>),
    /// which gives the parts back as the last of the rig's inspectors closes, before a scene is
    /// saved and wherever else it does. Each joint's anchor and limits have their handles on the joint's
    /// own inspector (<see cref="ArticulatedJointEditor"/>).
    /// </summary>
    [CustomEditor(typeof(ArticulatedRig))]
    [CanEditMultipleObjects]
    sealed class ArticulatedRigEditor : UnityEditor.Editor
    {
        void OnEnable() => ArticulatedRigPreview.Open(Rigs());

        void OnDisable() => ArticulatedRigPreview.Close(Rigs());

        IEnumerable<ArticulatedRig> Rigs()
        {
            foreach (Object o in targets)
                if (o is ArticulatedRig rig) yield return rig;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            if (serializedObject.ApplyModifiedProperties()) ArticulatedRigPreview.Show(Rigs());
            foreach (ArticulatedRig rig in Rigs())
            {
                string problem = rig.Problem;
                if (problem == null) continue;
                EditorGUILayout.HelpBox($"'{rig.name}' does not pose: {problem}", MessageType.Warning);
                break;
            }
            if (!EditorApplication.isPlaying || targets.Length != 1) return;
            var only = (ArticulatedRig)target;
            using (new EditorGUI.DisabledScope(!only.IsHoldingPose))
                if (GUILayout.Button("Return to Animation")) only.ReturnToAnimation();
        }
    }
}
