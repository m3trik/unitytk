using System;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEditor.Presets;

namespace M3trik.Unitytk.Materials
{
    /// <summary>
    /// Gives an image the materials written on import name its role on its first
    /// import (<see cref="MaterialsImporter"/> records them): a normal map imported
    /// as a normal map, a data map linear, and, where the project asks
    /// (<see cref="MaterialsSettings.fullSize"/>, off by default), each at its own size
    /// over the project's default cap it starts at. A postprocessor of its own, so a change to
    /// the material mapping (the importer's version) reimports the models alone, never
    /// every texture of the project. A texture already imported is the person's: its
    /// settings change only through <see cref="MaterialsActions.FixTextureRoles"/>.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's model importer gives an extracted normal map no normal-map
    /// type (it asks, in a dialog, after the import), and imports every extracted image
    /// as sRGB colour.
    /// </remarks>
    public class MaterialsTextureRoles : AssetPostprocessor
    {
        /// <inheritdoc/>
        public override uint GetVersion() => 1;

        void OnPreprocessTexture()
        {
            // First import only: afterwards the settings are the user's.
            if (!assetImporter.importSettingsMissing) return;
            Apply(assetImporter as TextureImporter, assetPath);
        }

        /// <summary>
        /// Give <paramref name="importer"/>, the first import of the texture at
        /// <paramref name="assetPath"/>, the role a written material recorded for it in its
        /// model's folder of its own (asked of the media provider <paramref name="media"/>
        /// resolves; default: the carrier). Returns whether it changed the importer.
        /// </summary>
        /// <param name="media">Resolves the media provider, called only for a texture a written
        /// material named: most textures ask nothing.</param>
        internal static bool Apply(TextureImporter importer, string assetPath, Func<IModelMedia> media = null)
        {
            if (importer == null || !MaterialsImporter.Named(assetPath)) return false;
            TextureRole? role = MaterialsImporter.RoleOf(assetPath, media != null ? media() : Provided.One<IModelMedia>());
            if (role == null) return false;  // named, but not a written material's map here: Unity's import stands
            // A first import starts at the project's default cap: Unity has applied its default presets.
            bool sized = MaterialsSettings.instance.fullSize && FullSize(importer, true, importer.maxTextureSize);
            if (role == TextureRole.Normal)
            {
                importer.textureType = TextureImporterType.NormalMap;
                return true;
            }
            if (role == TextureRole.Data)
            {
                importer.sRGBTexture = false;
                return true;
            }
            return sized;
        }

        /// <summary>
        /// The size cap the project gives <paramref name="importer"/>'s texture on its first import: the last of its
        /// default texture import presets (the Preset Manager's, their filters matched, applied in turn) that sets one,
        /// else Unity's own (<see cref="TextureImporterPlatformSettings.maxTextureSize"/>'s default). Read off the
        /// presets' values (<see cref="PresetCap"/>); the importer is never written.
        /// </summary>
        internal static int DefaultCap(TextureImporter importer)
        {
            int cap = new TextureImporterPlatformSettings().maxTextureSize;
            foreach (Preset preset in Preset.GetDefaultPresetsForObject(importer))
                if (PresetCap(preset.PropertyModifications) is int set) cap = set;
            return cap;
        }

        // A texture importer's per-platform settings, as a preset saves them: each entry its build target and its cap.
        const string Platforms = "m_PlatformSettings.Array.data[";
        const string BuildTarget = "m_BuildTarget";
        const string MaxSize = "m_MaxTextureSize";
        // The entry every platform without one of its own takes (TextureImporter.maxTextureSize's).
        const string DefaultPlatform = "DefaultTexturePlatform";

        /// <summary>The cap a preset's <paramref name="values"/> set for every platform (their default platform's entry); null for none.</summary>
        static int? PresetCap(PropertyModification[] values)
        {
            string entry = null;
            foreach (PropertyModification value in values)
                if (value.propertyPath.StartsWith(Platforms, StringComparison.Ordinal) && value.propertyPath.EndsWith("]." + BuildTarget, StringComparison.Ordinal)
                    && value.value == DefaultPlatform)
                    entry = value.propertyPath.Substring(0, value.propertyPath.Length - BuildTarget.Length);
            if (entry == null) return null;
            foreach (PropertyModification value in values)
                if (value.propertyPath == entry + MaxSize && int.TryParse(value.value, out int size)) return size;
            return null;
        }

        /// <summary>
        /// Whether <paramref name="importer"/>'s size cap is the project's default (<paramref name="defaultCap"/>,
        /// <see cref="DefaultCap"/>) and below its image's own size (2048 halves a delivery's 4K map); with
        /// <paramref name="apply"/>, raise it to the image's (its longer side, to a power of two, at most 16384). A cap
        /// a person chose for the texture (any other) stays, and per-platform caps are never touched.
        /// </summary>
        /// <param name="importer">A texture's importer.</param>
        /// <param name="apply">Raise the cap; false only answers.</param>
        /// <param name="defaultCap">The cap that counts as no one's choice for this texture: the project's default.</param>
        internal static bool FullSize(TextureImporter importer, bool apply, int defaultCap)
        {
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            int own = Math.Min(16384, Math.Max(32, UnityEngine.Mathf.NextPowerOfTwo(Math.Max(width, height))));
            if (importer.maxTextureSize != defaultCap || importer.maxTextureSize >= own) return false;
            if (apply) importer.maxTextureSize = own;
            return true;
        }
    }
}
