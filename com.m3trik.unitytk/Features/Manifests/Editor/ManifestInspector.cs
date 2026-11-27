using System;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Manifests.Tests")]

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// A manifest's inspector: what a person sets by hand (its plan, delivery, extra files, scene, asset folder and
    /// sheet) editable; what only the feature's actions change (the steps locked, each kit's settings, the behaviors
    /// off, its edits, each object's own values, the build's record, what a person adjusted and moved in the scene) shown read-only, so each such change is a
    /// <see cref="ManifestsActions"/> call: asked of its context, one undo step, and gone by the next build.
    /// </summary>
    [CustomEditor(typeof(Manifest))]
    internal sealed class ManifestInspector : Editor
    {
        // The fields only the feature's actions change.
        internal static readonly string[] ActionsOnly =
        {
            nameof(Manifest.locked), nameof(Manifest.settings), nameof(Manifest.off), nameof(Manifest.edits), nameof(Manifest.tunes), nameof(Manifest.built),
            nameof(Manifest.adjusted), nameof(Manifest.moved),
        };

        /// <inheritdoc/>
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            SerializedProperty each = serializedObject.GetIterator();
            for (bool enter = true; each.NextVisible(enter); enter = false)
            {
                if (each.propertyPath == "m_Script") continue;
                var field = new PropertyField(each.Copy());
                if (Array.IndexOf(ActionsOnly, each.propertyPath) >= 0) field.SetEnabled(false);
                root.Add(field);
            }
            root.Add(new HelpBox("The steps locked, the behaviors off, the edits and each object's own values change in the Manifest " +
                                 "window (Tools > unitytk > Manifest), or through ManifestsActions: each one undo step a build goes by.",
                                 HelpBoxMessageType.Info));
            return root;
        }
    }
}
