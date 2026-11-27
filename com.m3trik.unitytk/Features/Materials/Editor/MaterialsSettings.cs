using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Materials
{
    /// <summary>
    /// The Materials feature's project settings, shared with the project (<c>ProjectSettings/UnitytkMaterials.asset</c>)
    /// and edited in Project Settings ▸ unitytk ▸ Materials: whether each map a delivery's materials name is imported
    /// at its own size on its first import (<see cref="fullSize"/>, off: the project's cap stands, as for any texture).
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>ScriptableSingleton</c> with a <c>FilePath</c> under <c>ProjectSettings/</c> and its
    /// <c>SettingsProvider</c>, the documented home and page of a package's project settings. An editor preference
    /// was weighed and not taken: it is one person's, so a map's first import would differ by who imported it.
    /// </remarks>
    [FilePath("ProjectSettings/UnitytkMaterials.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class MaterialsSettings : ScriptableSingleton<MaterialsSettings>
    {
        /// <summary>Where the settings show in Project Settings.</summary>
        public const string Page = "Project/unitytk/Materials";

        [Tooltip("Import each map a delivery's materials name at its own size (its longer side, to a power of two, at most " +
                 "16384) on its first import, where the project's default cap (its default texture import preset's, else " +
                 "Unity's) would shrink it. Off: the project's cap stands, as for every other texture.")]
        public bool fullSize;

        /// <summary>Write the settings to the project.</summary>
        public void Save() => Save(true);

        [SettingsProvider]
        static SettingsProvider Provider() => new SettingsProvider(Page, SettingsScope.Project)
        {
            guiHandler = search =>
            {
                var settings = new SerializedObject(instance);
                EditorGUILayout.PropertyField(settings.FindProperty(nameof(fullSize)));
                if (settings.ApplyModifiedPropertiesWithoutUndo()) instance.Save();
            },
            keywords = new[] { "texture", "size", "cap", "import" },
        };
    }
}
