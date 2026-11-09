using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using Entry = MaterialRecord.Shape.Entry;
using Payload = MaterialRecord.Shape.Payload;
using TextureTransform = MaterialRecord.Shape.TextureTransform;

namespace M3trik.Unitytk.Materials
{
    /// <summary>
    /// A glTF 2.0 material (an entry of the <c>materials</c> record, or of any glTF
    /// document) read into the kernel's <see cref="LitSurface"/>, which
    /// <see cref="MaterialWriter"/> puts on the pipeline's own Lit: its factors, its
    /// maps found by file name, its metal / roughness / occlusion as one Unity mask,
    /// emission times <c>KHR_materials_emissive_strength</c>, <c>KHR_materials_unlit</c>,
    /// and <c>KHR_texture_transform</c> turned into Unity's UV space.
    /// </summary>
    /// <remarks>
    /// The rules are glTF's own (the 2.0 specification and the three extensions), plus
    /// one the record states for every reader: a map it names that cannot be found
    /// reads as absent (<see cref="MetallicWithoutMap"/>: a metal factor
    /// of 1 dielectric, a constant metal and the roughness factor kept; no emission
    /// over a missing emission map; a colour its factor alone; a flat normal; no
    /// occlusion), never as the factor 1 that stood in for it. Samplers are the texture importer's (wrap and filter are a
    /// texture's settings in Unity, not a material's): they are not read.
    /// </remarks>
    internal static class GltfSurfaces
    {
        /// <summary>
        /// The surface <paramref name="e"/> of <paramref name="document"/> states, each
        /// image found by <paramref name="image"/> (file name -> texture, or null where it
        /// cannot be found: then named in <paramref name="missing"/>) and its mask made by
        /// <paramref name="mask"/> (null where it cannot be: then said in
        /// <paramref name="losses"/> and read as absent).
        /// </summary>
        internal static LitSurface Surface(Payload document, Entry e, Func<string, Texture> image,
                                           Func<MaskSource, Texture> mask, ICollection<string> missing,
                                           ICollection<string> losses)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            Texture Find(int? texture)
            {
                string name = MaterialRecord.ImageOf(document, texture);
                if (name == null) return null;
                Texture found = image(name);
                if (found == null && missing != null && !Contains(missing, name)) missing.Add(name);
                return found;
            }

            var pbr = e.pbrMetallicRoughness;
            var surface = new LitSurface
            {
                baseColor = Linear(pbr?.baseColorFactor, 1f),
                baseMap = Find(pbr?.baseColorTexture?.index),
                metallic = (float)(pbr?.metallicFactor ?? 1.0),
                roughness = (float)(pbr?.roughnessFactor ?? 1.0),
                normalMap = Find(e.normalTexture?.index),
                normalScale = (float)(e.normalTexture?.scale ?? 1.0),
                emissionMap = Find(e.emissiveTexture?.index),
                alpha = e.alphaMode == "BLEND" ? LitAlpha.Blend : e.alphaMode == "MASK" ? LitAlpha.Mask : LitAlpha.Opaque,
                alphaCutoff = (float)(e.alphaCutoff ?? 0.5),
                doubleSided = e.doubleSided == true,
                unlit = e.extensions?.KHR_materials_unlit != null,
            };

            // Metal, roughness and occlusion: one Unity mask, the factors baked in.
            string mr = MaterialRecord.ImageOf(document, pbr?.metallicRoughnessTexture?.index);
            string occlusion = MaterialRecord.ImageOf(document, e.occlusionTexture?.index);
            bool mrFound = mr != null && Find(pbr.metallicRoughnessTexture.index) != null;
            bool occlusionFound = occlusion != null && Find(e.occlusionTexture.index) != null;
            if (mr != null && !mrFound) surface.metallic = MetallicWithoutMap(pbr.metallicFactor);
            if (mrFound || occlusionFound)
            {
                // Without a metallicRoughness image the mask carries the factors as constants.
                var source = new MaskSource
                {
                    metallicRoughness = mrFound ? mr : null,
                    occlusion = occlusionFound ? occlusion : null,
                    metallic = MaskSource.Quantized(surface.metallic),
                    roughness = MaskSource.Quantized(surface.roughness),
                };
                surface.maskMap = mask(source);
                surface.maskHasOcclusion = surface.maskMap != null && occlusionFound;
                if (surface.maskMap == null)
                {
                    losses?.Add($"its metal / roughness / occlusion images ({string.Join(", ", Named(source))}) could " +
                                "not be read for repacking: they read as absent");
                    if (mrFound) surface.metallic = MetallicWithoutMap(pbr.metallicFactor);
                }
            }

            // Emission: the factor (each 0-1) times its strength; nothing over a map that is not there.
            var emission = Linear(e.emissiveFactor, 0f);
            float strength = (float)(e.extensions?.KHR_materials_emissive_strength?.emissiveStrength ?? 1.0);
            emission = new Color(emission.r * strength, emission.g * strength, emission.b * strength, 1f);
            bool emissionMissing = e.emissiveTexture != null && surface.emissionMap == null
                                   && MaterialRecord.ImageOf(document, e.emissiveTexture.index) != null;
            surface.emission = emissionMissing ? Color.black : emission;

            Transform(e, surface, losses);
            return surface;
        }

        static bool Contains(ICollection<string> names, string name)
        {
            foreach (string n in names)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        static IEnumerable<string> Named(MaskSource source)
        {
            if (source.metallicRoughness != null) yield return source.metallicRoughness;
            if (source.occlusion != null && source.occlusion != source.metallicRoughness) yield return source.occlusion;
        }

        static Color Linear(double[] values, float fallback)
        {
            float At(int i, float otherwise) => values != null && i < values.Length ? (float)values[i] : otherwise;
            return new Color(At(0, fallback), At(1, fallback), At(2, fallback), At(3, 1f));
        }

        /// <summary>
        /// The maps' UV transform, in Unity's UV space: glTF's (origin top left) offset
        /// <c>(u, v)</c> and scale <c>(su, sv)</c> are Unity's offset <c>(u, 1 - sv - v)</c>
        /// and the same scale. A pipeline's Lit applies one transform to every map, the
        /// base map's: the one <see cref="ChosenTransform"/> chooses (the base
        /// colour map's, else the first stated), the difference said.
        /// </summary>
        static void Transform(Entry e, LitSurface surface, ICollection<string> losses)
        {
            TextureTransform chosen = ChosenTransform(e, out bool differs);
            if (differs)
                losses?.Add("its maps state different UV transforms, and the pipeline's Lit applies one to every map: " +
                            $"the {(e.pbrMetallicRoughness?.baseColorTexture != null ? "base colour map's" : "first map's")} is applied");
            (surface.tiling, surface.offset) = Unity(chosen);
        }

        /// <summary>
        /// The metal factor a material reads where its metallicRoughness image is named
        /// and cannot be found: its absent value (<see cref="MapAbsence.Metallic"/>,
        /// dielectric) where the factor is 1 or more -- the map supplied it -- else the
        /// factor, a constant of the material's own. Its roughness factor is read as it
        /// is (a constant is the material's; 1 is matte, the absent value itself), and an
        /// emissive image that cannot be found reads as no emission. The one rule every
        /// reader of a missing map applies, mirrored from pythontk's
        /// <c>MaterialFamilies.absent_map</c>.
        /// </summary>
        /// <param name="factor">The material's <c>metallicFactor</c> (glTF's default 1 when null).</param>
        internal static float MetallicWithoutMap(double? factor)
        {
            double value = factor ?? 1.0;
            return value >= 1.0 ? MapAbsence.Metallic : (float)value;
        }

        /// <summary>
        /// The UV transform a reader that applies ONE transform to every map applies (a
        /// render pipeline's Lit: the base map's): the base colour map's, else the first
        /// bound map's that states one; null where none does. <paramref name="differs"/>
        /// is whether another bound map states a different one (which that reader cannot
        /// show). In glTF's UV space, as the record states it.
        /// </summary>
        internal static TextureTransform ChosenTransform(Entry e, out bool differs)
        {
            differs = false;
            if (e == null) return null;
            var pbr = e.pbrMetallicRoughness;
            var stated = new List<TextureTransform>();
            void Add(MaterialRecord.Shape.TextureExtensions ext, bool bound)
            {
                if (bound) stated.Add(ext?.KHR_texture_transform);
            }
            Add(pbr?.baseColorTexture?.extensions, pbr?.baseColorTexture != null);
            Add(pbr?.metallicRoughnessTexture?.extensions, pbr?.metallicRoughnessTexture != null);
            Add(e.normalTexture?.extensions, e.normalTexture != null);
            Add(e.occlusionTexture?.extensions, e.occlusionTexture != null);
            Add(e.emissiveTexture?.extensions, e.emissiveTexture != null);
            if (stated.Count == 0) return null;
            TextureTransform chosen = stated[0];
            if (pbr?.baseColorTexture == null)
                foreach (var t in stated)
                    if (t != null) { chosen = t; break; }
            foreach (var t in stated)
                if (!Same(t, chosen)) { differs = true; break; }
            return chosen;
        }

        static bool Same(TextureTransform a, TextureTransform b)
        {
            double At(double[] v, int i, double otherwise) => v != null && i < v.Length ? v[i] : otherwise;
            for (int i = 0; i < 2; i++)
                if (At(a?.scale, i, 1.0) != At(b?.scale, i, 1.0) || At(a?.offset, i, 0.0) != At(b?.offset, i, 0.0))
                    return false;
            return true;
        }

        static (Vector2 tiling, Vector2 offset) Unity(TextureTransform t)
        {
            float At(double[] v, int i, float otherwise) => v != null && i < v.Length ? (float)v[i] : otherwise;
            var scale = new Vector2(At(t?.scale, 0, 1f), At(t?.scale, 1, 1f));
            var (u, v) = M3trik.Unitytk.Core.GltfFrame.TextureOffset(At(t?.offset, 0, 0f), At(t?.offset, 1, 0f), scale.y);
            return (scale, new Vector2((float)u, (float)v));
        }
    }
}
