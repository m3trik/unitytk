using System;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Kernel;
using UnityEditor;

namespace M3trik.Unitytk.Materials
{
    /// <summary>
    /// Gives an image the materials written on import name its role on its first
    /// import (<see cref="MaterialsImporter"/> records them): a normal map imported
    /// as a normal map, a data map linear. A postprocessor of its own, so a change to
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
            var role = MaterialsImporter.RoleOf(assetPath, media != null ? media() : Provided.One<IModelMedia>());
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
            return false;
        }
    }
}
