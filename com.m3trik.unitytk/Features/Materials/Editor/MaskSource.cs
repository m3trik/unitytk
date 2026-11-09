using System;
using System.Globalization;

namespace M3trik.Unitytk.Materials
{
    /// <summary>
    /// What one Unity mask (<see cref="M3trik.Unitytk.Kernel.MaskMap"/>) is packed from: a
    /// glTF material's metallicRoughness and occlusion images, by file name, and the
    /// factors baked into it. Two materials stating the same share one mask: two
    /// sources are the same exactly when their <see cref="Name"/>s are (the import
    /// adds a mask under its name, so a name two different masks shared would collide),
    /// and a factor is read to the name's 3 decimals (<see cref="Quantized"/>).
    /// </summary>
    internal sealed class MaskSource : IEquatable<MaskSource>
    {
        /// <summary>The metallicRoughness image's file name (roughness G, metal B), or null.</summary>
        public string metallicRoughness;

        /// <summary>The occlusion image's file name (read from R), or null; the metallicRoughness one where packed with it.</summary>
        public string occlusion;

        /// <summary>glTF's metallicFactor.</summary>
        public float metallic = 1f;

        /// <summary>glTF's roughnessFactor.</summary>
        public float roughness = 1f;

        /// <summary>
        /// The mask's name: its sources' stem and, where they are not 1, its factors --
        /// stable across imports, so an object referencing it keeps it.
        /// </summary>
        public string Name
        {
            get
            {
                string name = Stem(metallicRoughness ?? occlusion ?? "mask") + "_MetallicSmoothness";
                if (occlusion != null && occlusion != metallicRoughness && metallicRoughness != null)
                    name += "_" + Stem(occlusion);
                float m = Quantized(metallic), r = Quantized(roughness);
                if (m != 1f || r != 1f)
                    name += string.Format(CultureInfo.InvariantCulture, "_m{0:0.###}_r{1:0.###}", m, r);
                return name;
            }
        }

        /// <summary><paramref name="factor"/> to the 3 decimals a <see cref="Name"/> states it by: what a mask bakes in.</summary>
        public static float Quantized(float factor) => (float)Math.Round(factor, 3, MidpointRounding.AwayFromZero);

        // A file name without a PNG's extension; another image's keeps it, so a.png and a.jpg name two masks.
        static string Stem(string file)
        {
            int dot = file.LastIndexOf('.');
            if (dot <= 0) return file;
            string extension = file.Substring(dot + 1);
            return string.Equals(extension, "png", StringComparison.OrdinalIgnoreCase)
                ? file.Substring(0, dot)
                : file.Substring(0, dot) + "_" + extension;
        }

        /// <inheritdoc/>
        public bool Equals(MaskSource other) => other != null && string.Equals(Name, other.Name, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => Equals(obj as MaskSource);

        /// <inheritdoc/>
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Name);
    }
}
