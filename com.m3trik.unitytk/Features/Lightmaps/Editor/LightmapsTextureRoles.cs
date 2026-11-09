using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Lightmaps
{
    /// <summary>
    /// Gives each file a delivery's lightmaps record names its import settings on its
    /// first import (<see cref="LightmapsImporter"/> notes them): a lightmap imported as a
    /// <see cref="TextureImporterType.Lightmap"/>, which Unity encodes for the project's
    /// lightmap encoding, and the reflection probe's panorama as a readable linear source,
    /// which the model's import makes its cubemap from. A postprocessor of its own, so a
    /// change to the model's import reimports the models alone. A file imported before
    /// is the person's: the Apply action imports a set's maps as lightmaps where they are not.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's model import imports every extracted image as an sRGB colour
    /// texture; a lightmap Unity did not bake gets no Lightmap type, and so no lightmap
    /// encoding, unless something sets it.
    /// </remarks>
    public class LightmapsTextureRoles : AssetPostprocessor
    {
        /// <summary>What a file of the record holds, which decides its import settings.</summary>
        internal enum Role
        {
            /// <summary>A lightmap: linear light, read through the second UV set.</summary>
            Lightmap,
            /// <summary>The probe's equirectangular panorama: a source the model's import reads.</summary>
            Panorama,
        }

        /// <inheritdoc/>
        public override uint GetVersion() => 1;

        // A file name -> per model, what it holds: noted by the model's import, read on the
        // file's FIRST import alone, in the model's folder of its own (IModelMedia.InOwnMediaFolder),
        // where every extraction imports its files first.
        static readonly Dictionary<string, Dictionary<string, Role>> _roles =
            new Dictionary<string, Dictionary<string, Role>>(StringComparer.OrdinalIgnoreCase);

        void OnPreprocessTexture()
        {
            // First import only: afterwards the settings are the person's.
            if (!assetImporter.importSettingsMissing) return;
            Apply(assetImporter as TextureImporter, assetPath);
        }

        /// <summary>Note that the model at <paramref name="modelPath"/>'s record names <paramref name="file"/> as <paramref name="role"/>.</summary>
        internal static void Note(string file, Role role, string modelPath)
        {
            if (string.IsNullOrEmpty(file)) return;
            if (!_roles.TryGetValue(file, out var byModel))
                _roles[file] = byModel = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
            byModel[modelPath] = role;
        }

        /// <summary>
        /// Give <paramref name="importer"/>, the first import of the texture at
        /// <paramref name="assetPath"/>, the role a model's record noted for it, where the
        /// file lies in that model's folder of its own (asked of the media provider
        /// <paramref name="media"/> resolves; default: the carrier). Returns whether it did.
        /// </summary>
        internal static bool Apply(TextureImporter importer, string assetPath, Func<IModelMedia> media = null)
        {
            if (importer == null || !_roles.TryGetValue(Path.GetFileName(assetPath), out var byModel)) return false;
            IModelMedia provider = media != null ? media() : Provided.One<IModelMedia>();
            foreach (var pair in byModel)
            {
                if (!provider.InOwnMediaFolder(pair.Key, assetPath)) continue;
                Pin(importer, pair.Value, true);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Whether <paramref name="importer"/>'s settings differ from what <paramref name="role"/>
        /// asks; set them where <paramref name="apply"/> (a check never touches an importer).
        /// </summary>
        internal static bool Pin(TextureImporter importer, Role role, bool apply)
        {
            if (role == Role.Lightmap)
            {
                // The format stays Automatic: Unity encodes a Lightmap for the project's
                // lightmap encoding, which its shaders decode by.
                bool changed = Set(importer.textureType, TextureImporterType.Lightmap, v => importer.textureType = v, apply);
                changed |= Set(importer.mipmapEnabled, true, v => importer.mipmapEnabled = v, apply);
                return changed;
            }
            bool differs = Set(importer.textureType, TextureImporterType.Default, v => importer.textureType = v, apply);
            differs |= Set(importer.textureShape, TextureImporterShape.Texture2D, v => importer.textureShape = v, apply);
            differs |= Set(importer.isReadable, true, v => importer.isReadable = v, apply);
            differs |= Set(importer.sRGBTexture, false, v => importer.sRGBTexture = v, apply);
            differs |= Set(importer.mipmapEnabled, false, v => importer.mipmapEnabled = v, apply);
            differs |= Set(importer.npotScale, TextureImporterNPOTScale.None, v => importer.npotScale = v, apply);
            differs |= Set(importer.maxTextureSize, 16384, v => importer.maxTextureSize = v, apply);
            differs |= Set(importer.textureCompression, TextureImporterCompression.Uncompressed, v => importer.textureCompression = v, apply);
            return differs;
        }

        static bool Set<T>(T current, T wanted, Action<T> set, bool apply)
        {
            if (EqualityComparer<T>.Default.Equals(current, wanted)) return false;
            if (apply) set(wanted);
            return true;
        }
    }
}
