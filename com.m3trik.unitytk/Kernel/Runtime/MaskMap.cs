using System;
using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// Unity's packed mask: glTF's metal, roughness and occlusion repacked into the one
    /// layout every pipeline's Lit reads (<see cref="LitTable.MaskMap"/>): R metal, G
    /// occlusion, B 1, A smoothness (1 - roughness). glTF packs them otherwise (R
    /// occlusion, G roughness, B metal) and multiplies each by a factor; the factors are
    /// baked into the mask, so the material reads it at scale 1.
    /// </summary>
    /// <remarks>
    /// Standard and URP Lit read metal from their metallic map's R and smoothness from
    /// its A, and occlusion from G of their occlusion map, which binds the same mask;
    /// HDRP Lit's mask map is this layout exactly, B its detail mask (white: the whole
    /// detail, of which a glTF material has none). Prior art: glTFast's and UnityGLTF's
    /// importers need no repack, since they sample glTF's layout in shaders of their own
    /// (glTFast's glTF shader graphs, UnityGLTF's PBRGraph), which unitytk does not put
    /// on a user's material (<see cref="MaterialWriter"/>).
    /// </remarks>
    public static class MaskMap
    {
        /// <summary>
        /// The mask for a glTF material: its metallicRoughness image
        /// <paramref name="metallicRoughness"/> (roughness G, metal B; null for none) and
        /// its occlusion image <paramref name="occlusion"/> (read from R; null for none,
        /// or the metallicRoughness image itself where occlusion is packed with it), each
        /// factor multiplied in. The mask is the metallicRoughness image's size, else the
        /// occlusion's; the other is sampled to it (nearest texel). Texels are in Unity's
        /// order (row 0 at the bottom), as <c>Texture2D.GetPixels32</c> gives them.
        /// </summary>
        /// <param name="metallicRoughness">The metallicRoughness texels, or null.</param>
        /// <param name="mrWidth">Its width.</param>
        /// <param name="mrHeight">Its height.</param>
        /// <param name="occlusion">The occlusion texels, or null.</param>
        /// <param name="occlusionWidth">Its width.</param>
        /// <param name="occlusionHeight">Its height.</param>
        /// <param name="metallicFactor">glTF's <c>metallicFactor</c>.</param>
        /// <param name="roughnessFactor">glTF's <c>roughnessFactor</c>.</param>
        /// <param name="width">The mask's width.</param>
        /// <param name="height">The mask's height.</param>
        /// <returns>The mask's texels.</returns>
        /// <exception cref="ArgumentException">Neither image is given, or one's texels do not fill its size.</exception>
        public static Color32[] Pack(Color32[] metallicRoughness, int mrWidth, int mrHeight,
                                     Color32[] occlusion, int occlusionWidth, int occlusionHeight,
                                     float metallicFactor, float roughnessFactor, out int width, out int height)
        {
            if (metallicRoughness == null && occlusion == null)
                throw new ArgumentException("a mask packs a metallicRoughness image, an occlusion image or both: neither was given");
            Check(metallicRoughness, mrWidth, mrHeight, "metallicRoughness");
            Check(occlusion, occlusionWidth, occlusionHeight, "occlusion");
            width = metallicRoughness != null ? mrWidth : occlusionWidth;
            height = metallicRoughness != null ? mrHeight : occlusionHeight;
            float metal = Mathf.Clamp01(metallicFactor), rough = Mathf.Clamp01(roughnessFactor);
            var mask = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    byte r = Byte(metal), a = Byte(1f - rough), g = 255;
                    if (metallicRoughness != null)
                    {
                        Color32 mr = metallicRoughness[y * width + x];
                        r = Byte(metal * mr.b / 255f);
                        a = Byte(1f - rough * mr.g / 255f);
                    }
                    if (occlusion != null)
                        g = Sample(occlusion, occlusionWidth, occlusionHeight, x, y, width, height).r;
                    mask[y * width + x] = new Color32(r, g, 255, a);
                }
            }
            return mask;
        }

        static void Check(Color32[] texels, int width, int height, string what)
        {
            if (texels != null && (width <= 0 || height <= 0 || texels.Length != width * height))
                throw new ArgumentException($"the {what} image holds {texels.Length} texels, not {width} x {height}");
        }

        // The texel of an image of another size nearest the mask texel (x, y).
        static Color32 Sample(Color32[] texels, int w, int h, int x, int y, int width, int height)
        {
            if (w == width && h == height) return texels[y * w + x];
            int sx = Mathf.Min(w - 1, (int)((x + 0.5f) * w / width));
            int sy = Mathf.Min(h - 1, (int)((y + 0.5f) * h / height));
            return texels[sy * w + sx];
        }

        // Half up, as an 8-bit image is written (Mathf.RoundToInt rounds a half to even).
        static byte Byte(float value) => (byte)(Mathf.Clamp01(value) * 255f + 0.5f);
    }
}
