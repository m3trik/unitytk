using System;
using System.IO;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Materials
{
    /// <summary>
    /// A <see cref="MaskSource"/> made into a texture: its images read off their files
    /// (PNG or JPEG, glTF's two image formats, whatever their import settings) and
    /// packed into Unity's mask (<see cref="MaskMap.Pack"/>), linear, with mipmaps.
    /// </summary>
    internal static class MaskTextures
    {
        /// <summary>
        /// The mask <paramref name="source"/> states, each image read off the project
        /// file <paramref name="pathOf"/> names for it (an asset path); null, with why in
        /// <paramref name="why"/>, where an image cannot be read.
        /// </summary>
        internal static Texture2D Make(MaskSource source, Func<string, string> pathOf, out string why)
        {
            why = null;
            Color32[] mr = null, occlusion = null;
            int mw = 0, mh = 0, ow = 0, oh = 0;
            if (source.metallicRoughness != null && !Read(pathOf(source.metallicRoughness), out mr, out mw, out mh, out why))
                return null;
            if (source.occlusion != null)
            {
                if (source.occlusion == source.metallicRoughness)
                {
                    occlusion = mr;
                    ow = mw;
                    oh = mh;
                }
                else if (!Read(pathOf(source.occlusion), out occlusion, out ow, out oh, out why))
                    return null;
            }
            Color32[] mask = MaskMap.Pack(mr, mw, mh, occlusion, ow, oh, MaskSource.Quantized(source.metallic),
                                          MaskSource.Quantized(source.roughness), out int width, out int height);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, true, true) { name = source.Name };
            texture.SetPixels32(mask);
            texture.Apply(true, false);
            return texture;
        }

        /// <summary>
        /// Compress <paramref name="mask"/> (made by <see cref="Make"/>) as Unity's texture import
        /// compresses its source image <paramref name="like"/> for <paramref name="platform"/> (a
        /// texture importer's platform name: <c>Standalone</c>, <c>Android</c>, <c>WebGL</c>,
        /// <c>iPhone</c>): the platform's override where the source states one, else Unity's own
        /// pick for it, in that format's family with alpha (which the mask's smoothness needs), at
        /// the source's compression quality. An uncompressed source leaves the mask as it is; with
        /// no source to follow, the editor's own compression at high quality
        /// (<see cref="Texture2D.Compress"/>).
        /// </summary>
        /// <remarks>
        /// Prior art: a texture a script makes is no importer's, so Unity's platform settings
        /// never recompress it for a build, and <see cref="Texture2D.Compress"/> picks the
        /// editor's own (measured: DXT5 with the project on WebGL, whose images imported ASTC
        /// 6x6). <see cref="EditorUtility.CompressTexture(Texture2D, TextureFormat, TextureCompressionQuality)"/>
        /// takes the format; the source image's importer says which one the platform gets.
        /// </remarks>
        internal static void Compress(Texture2D mask, string platform, TextureImporter like)
        {
            if (like == null)
            {
                mask.Compress(true);
                return;
            }
            // The platform's override where the source states one, else what Unity picks for it;
            // its quality likewise (0-100, as the importer keeps it).
            TextureImporterPlatformSettings own = like.GetPlatformTextureSettings(platform);
            bool overridden = own != null && own.overridden && own.format != TextureImporterFormat.Automatic;
            TextureFormat? format = WithAlpha(overridden ? own.format : like.GetAutomaticFormat(platform));
            int quality = own != null && own.overridden ? own.compressionQuality : like.compressionQuality;
            if (format.HasValue) EditorUtility.CompressTexture(mask, format.Value, Mathf.Clamp(quality, 0, 100));
        }

        /// <summary>
        /// The compressed format of <paramref name="format"/>'s family that keeps an alpha: ASTC as
        /// it is, ETC as ETC2 RGBA8, DXT as DXT5, BC7 as it is; null for an uncompressed one, or
        /// one no family here names.
        /// </summary>
        internal static TextureFormat? WithAlpha(TextureImporterFormat format)
        {
            string name = format.ToString();
            if (name.StartsWith("ASTC_", StringComparison.Ordinal) && !name.Contains("HDR")
                && Enum.TryParse(name, out TextureFormat astc))
                return astc;
            if (name.StartsWith("ETC", StringComparison.Ordinal)) return TextureFormat.ETC2_RGBA8;
            if (name.StartsWith("DXT", StringComparison.Ordinal)) return TextureFormat.DXT5;
            if (format == TextureImporterFormat.BC7) return TextureFormat.BC7;
            return null;
        }

        /// <summary>The PNG text key a mask file this package wrote states its sources under: what marks it ours.</summary>
        internal const string StampKey = "unitytk metallic smoothness from";

        /// <summary>
        /// <paramref name="png"/> with a <c>tEXt</c> chunk stating <see cref="StampKey"/> =
        /// <paramref name="value"/> after its header: the mark a mask file this package
        /// writes carries, as pythontk stamps the packed image it writes.
        /// </summary>
        internal static byte[] Stamped(byte[] png, string value)
        {
            byte[] text = Latin1(StampKey + "\0" + value);
            const int afterHeader = 8 + 4 + 4 + 13 + 4;  // signature, IHDR length, type, data, CRC
            var chunk = new byte[12 + text.Length];
            WriteInt(chunk, 0, text.Length);
            chunk[4] = (byte)'t'; chunk[5] = (byte)'E'; chunk[6] = (byte)'X'; chunk[7] = (byte)'t';
            Array.Copy(text, 0, chunk, 8, text.Length);
            WriteInt(chunk, 8 + text.Length, (int)Crc32(chunk, 4, 4 + text.Length));
            var stamped = new byte[png.Length + chunk.Length];
            Array.Copy(png, stamped, afterHeader);
            Array.Copy(chunk, 0, stamped, afterHeader, chunk.Length);
            Array.Copy(png, afterHeader, stamped, afterHeader + chunk.Length, png.Length - afterHeader);
            return stamped;
        }

        /// <summary>What a PNG's <see cref="StampKey"/> text states, or null: a file this package did not write.</summary>
        internal static string StampOf(byte[] png)
        {
            int at = 8;
            while (png != null && at + 12 <= png.Length)
            {
                int length = (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
                if (length < 0 || at + 12 + length > png.Length) return null;
                string type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
                if (type == "tEXt")
                {
                    var chars = new char[length];
                    for (int i = 0; i < length; i++) chars[i] = (char)png[at + 8 + i];
                    string text = new string(chars);
                    int nul = text.IndexOf('\0');
                    if (nul > 0 && text.Substring(0, nul) == StampKey) return text.Substring(nul + 1);
                }
                if (type == "IDAT" || type == "IEND") return null;
                at += 12 + length;
            }
            return null;
        }

        // tEXt's encoding, Latin-1: a character outside it reads '?'.
        static byte[] Latin1(string text)
        {
            var bytes = new byte[text.Length];
            for (int i = 0; i < text.Length; i++) bytes[i] = text[i] < 256 ? (byte)text[i] : (byte)'?';
            return bytes;
        }

        static void WriteInt(byte[] buffer, int at, int value)
        {
            buffer[at] = (byte)(value >> 24);
            buffer[at + 1] = (byte)(value >> 16);
            buffer[at + 2] = (byte)(value >> 8);
            buffer[at + 3] = (byte)value;
        }

        // PNG's CRC-32 (ISO 3309), over the chunk's type and data.
        static uint Crc32(byte[] data, int offset, int count)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++)
            {
                crc ^= data[i];
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>The texels of the image file at <paramref name="assetPath"/>, Unity's order (row 0 at the bottom).</summary>
        static bool Read(string assetPath, out Color32[] texels, out int width, out int height, out string why)
        {
            texels = null;
            width = height = 0;
            why = null;
            if (string.IsNullOrEmpty(assetPath))
            {
                why = "it is not in the project";
                return false;
            }
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(Path.GetFullPath(FileUtil.GetPhysicalPath(assetPath)));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                why = $"'{assetPath}' could not be read ({e.Message})";
                return false;
            }
            var image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            try
            {
                if (!image.LoadImage(bytes, false))
                {
                    why = $"'{assetPath}' is no PNG or JPEG";
                    return false;
                }
                texels = image.GetPixels32();
                width = image.width;
                height = image.height;
                return true;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
