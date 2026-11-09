using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// The inspector of a shadow plane: its fields; a word where it has no map to march;
    /// Scene-view handles for its radius (a ring on the ground round the contact) and its
    /// ground (a height under the contact), both in the plane's space; and the plane shown
    /// in the Scene view while the inspector is open, placed and faded from where its
    /// source and contact stand, through the one keeper of what planes show outside play
    /// mode (<see cref="ShadowPlanePreview"/>), which gives the placement back as the
    /// inspector closes, before a scene is saved and wherever else it does.
    /// </summary>
    [CustomEditor(typeof(ShadowPlane))]
    [CanEditMultipleObjects]
    sealed class ShadowPlaneEditor : UnityEditor.Editor
    {
        void OnEnable() => ShadowPlanePreview.Show(targets);

        void OnDisable() => ShadowPlanePreview.End(targets);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            if (serializedObject.ApplyModifiedProperties()) ShadowPlanePreview.Show(targets);
            foreach (Object o in targets)
            {
                if (!(o is ShadowPlane plane) || plane.CanMarch) continue;
                EditorGUILayout.HelpBox(Unmarched(plane), MessageType.Warning);
                break;
            }
            foreach (Object o in targets)
            {
                if (!(o is ShadowPlane shown) || !ShadowPlanePreview.Showing(shown)) continue;
                EditorGUILayout.HelpBox(shown.Places
                                            ? "Showing in the Scene view, placed from its source and contact: its transform " +
                                              "as it was is back when this inspector closes, and before the scene is saved."
                                            : "Showing in the Scene view, faded from its source and contact; its transform " +
                                              "is its own (Follow Source is off, or its contact or source is missing).",
                                        MessageType.None);
                break;
            }
        }

        /// <summary>Why <paramref name="plane"/> draws nothing, and what makes its map.</summary>
        internal static string Unmarched(ShadowPlane plane)
        {
            string why = plane.horizonMap == null
                ? "It has no horizon map"
                : $"Its horizon map's layout is not one the shader marches " +
                  $"({ShadowPlane.HorizonRefusal(plane.horizonSize, plane.horizonSpans, plane.horizonHeightScale, plane.horizonBounds)})";
            return why + ", so it draws nothing: bake one from its target (Tools > unitytk > Shadow Plane > Bake Horizon).";
        }

        void OnSceneGUI()
        {
            var plane = (ShadowPlane)target;
            if (plane == null || plane.contact == null) return;
            Matrix4x4 toWorld = plane.SpaceToWorld();
            Vector3 c = toWorld.inverse.MultiplyPoint3x4(plane.contact.position);
            var centre = new Vector3(c.x, plane.ground, c.z);
            using (new Handles.DrawingScope(new Color(1f, 0.6f, 0.1f, 1f), toWorld))
            {
                Handles.DrawWireDisc(centre, Vector3.up, plane.radius);
                float size = HandleUtility.GetHandleSize(centre) * 0.08f;
                EditorGUI.BeginChangeCheck();
                Vector3 rim = Handles.Slider(centre + Vector3.right * plane.radius, Vector3.right, size,
                                             Handles.DotHandleCap, 0f);
                Vector3 floor = Handles.Slider(centre, Vector3.up, size * 1.5f, Handles.CubeHandleCap, 0f);
                if (!EditorGUI.EndChangeCheck()) return;
                serializedObject.Update();
                serializedObject.FindProperty(nameof(ShadowPlane.radius)).floatValue = Mathf.Max(0f, rim.x - centre.x);
                serializedObject.FindProperty(nameof(ShadowPlane.ground)).floatValue = floor.y;
                if (serializedObject.ApplyModifiedProperties()) ShadowPlanePreview.Show(targets);
            }
        }
    }
}
