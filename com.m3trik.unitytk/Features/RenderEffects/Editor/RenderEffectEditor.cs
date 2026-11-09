using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// The inspector of every render effect: its fields; a word, and the action to fix
    /// it, where its materials' twins are not prepared; and each edit shown in the Scene
    /// view on the renderers it covers, through the one keeper of what effects show outside
    /// play mode (<see cref="RenderEffectsPreview"/>), which gives them back as the inspector
    /// closes, before a scene is saved and wherever else it does.
    /// </summary>
    [CustomEditor(typeof(RenderEffect), true)]
    [CanEditMultipleObjects]
    sealed class RenderEffectEditor : UnityEditor.Editor
    {
        string _unprepared;

        void OnEnable() => _unprepared = targets.Length == 1 ? Unprepared(target as RenderEffect) : null;

        void OnDisable() => RenderEffectsPreview.End(targets);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject);
            if (serializedObject.ApplyModifiedProperties()) RenderEffectsPreview.Show(targets);
            if (_unprepared != null)
            {
                EditorGUILayout.HelpBox(_unprepared, MessageType.Warning);
                if (targets.Length == 1 && GUILayout.Button("Prepare Materials"))
                {
                    var effect = (RenderEffect)target;
                    OpContext.RunMenu("Prepare Materials", ctx => ctx.Log($"Prepare Materials: {Prepare(effect, ctx)} change(s)."));
                    _unprepared = Unprepared(effect);
                }
            }
            foreach (Object o in targets)
            {
                if (!RenderEffectsPreview.Showing(o as RenderEffect)) continue;
                EditorGUILayout.HelpBox("Showing in the Scene view: the materials as authored are back when this " +
                                        "inspector closes, and before the scene is saved.", MessageType.None);
                break;
            }
        }

        /// <summary>
        /// Where <paramref name="effect"/>'s twins are measured and prepared from: a track's
        /// scope (its renderers are anywhere in it: an imported track sits under the
        /// carrier), else the effect's own object, the root of what it covers.
        /// </summary>
        internal static Transform PreparedFrom(RenderEffect effect)
            => effect is EmissionTrack ? RenderEffect.ScopeOf(effect) : effect.transform;

        /// <summary>The inspector's Prepare Materials: <see cref="RenderEffectsActions.PrepareMaterials"/> over <see cref="PreparedFrom"/>.</summary>
        internal static int Prepare(RenderEffect effect, OpContext ctx = null, string assetPath = null)
            => RenderEffectsActions.PrepareMaterials(PreparedFrom(effect).gameObject, assetPath, ctx);

        /// <summary>
        /// What <paramref name="effect"/>'s twins lack, in a sentence, measured from
        /// <see cref="PreparedFrom"/>; null when every twin the effects there can ask for is made.
        /// </summary>
        internal static string Unprepared(RenderEffect effect)
        {
            if (effect == null) return null;
            Transform root = PreparedFrom(effect);
            var effects = new List<RenderEffect>(root.GetComponentsInChildren<RenderEffect>(true));
            foreach (EmissionTrack track in TwinPlan.Tracks(root))
                if (!effects.Contains(track)) effects.Add(track);
            int missing = 0;
            foreach (var (material, state) in TwinPlan.Needed(root))
            {
                bool found = false;
                foreach (RenderEffect e in effects)
                    if (e.twins != null && e.twins.Template(material, state) != null) { found = true; break; }
                if (!found) missing++;
            }
            return missing == 0
                ? null
                : $"{missing} twin(s) of the materials this effect covers are not prepared: they are made at runtime, " +
                  "and a player build may lack their shader variants.";
        }
    }
}
