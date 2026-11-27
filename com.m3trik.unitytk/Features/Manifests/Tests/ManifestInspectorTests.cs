using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Manifests.Tests
{
    /// <summary>
    /// <see cref="ManifestInspector"/>: what a person sets by hand is edited in the inspector; what only the
    /// feature's actions change (locks, behaviors off, edits, own values, the build's record, the adjustments
    /// and moved helpers it keeps, the kits' settings list) is shown, never edited there.
    /// </summary>
    public class ManifestInspectorTests
    {
        [Test]
        public void What_only_the_actions_change_is_read_only_in_the_inspector()
        {
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            Editor editor = Editor.CreateEditor(manifest);
            try
            {
                Assert.That(editor, Is.InstanceOf<ManifestInspector>());
                var fields = editor.CreateInspectorGUI().Query<PropertyField>().ToList().ToDictionary(f => f.bindingPath, f => f.enabledSelf);
                Assert.That(fields.Keys, Is.SupersetOf(new[] { "plan", "delivery", "scene", "off", "locked", "edits", "tunes", "built", "settings", "adjusted", "moved" }));
                Assert.That(new[] { "plan", "delivery", "extras", "scene", "assetFolder", "sheet" }.All(k => fields[k]), "set by hand");
                Assert.That(new[] { "off", "locked", "edits", "tunes", "built", "settings", "adjusted", "moved" }.Any(k => fields[k]), Is.False, "the actions' alone");
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(manifest);
            }
        }
    }
}
