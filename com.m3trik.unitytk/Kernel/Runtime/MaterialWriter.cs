using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The one material writer: puts a glTF 2.0 material (<see cref="LitSurface"/>) on
    /// the render pipeline's own Lit, or its Unlit for an unlit one, through the
    /// pipeline's row of the declared table (<see cref="IRenderPipelineHost.Lit"/>,
    /// <see cref="LitTable"/>) and its host's blend switch
    /// (<see cref="IRenderPipelineHost.SetBlended"/>). It writes a complete state --
    /// the shader, every map slot (cleared first), the values, keywords, alpha mode,
    /// faces and the maps' tiling -- so nothing an earlier mapping left behind survives,
    /// and it never assigns a shader of its own or a property block. What the target
    /// cannot show is said, one sentence each, in the losses a caller passes. It also
    /// makes a material's twins (<see cref="Twin"/>: its copy in a state an effect asks
    /// for -- blended, glowing or emitting -- by the pipeline's own switches) and writes
    /// their values (<see cref="SetTwinValues"/>), so an effect sets the pipeline's own
    /// Lit's properties, as a material's own inspector would.
    /// </summary>
    /// <remarks>
    /// Prior art: glTFast's material generators (<c>BuiltInMaterialGenerator</c>,
    /// <c>UniversalRPMaterialGenerator</c>, <c>HighDefinitionRPMaterialGenerator</c>)
    /// and UnityGLTF's importer map the same glTF model per pipeline, but onto shader
    /// graphs of their own (glTFast's glTF graphs, UnityGLTF's PBRGraph), which give a
    /// Unity user materials they would not recognise, a package dependency, and a
    /// shader every other writer (fade, highlight) would have to learn; so neither is
    /// adopted now, and glTFast stays an opt-in door for reading GLB files, not built.
    /// This maps onto the pipeline's own Lit by the names its own inspectors write
    /// (Built-in's <c>StandardShaderGUI</c>, URP's <c>BaseShaderGUI</c> and
    /// <c>LitShader</c>, HDRP's <c>LitGUI</c>), as Unity's model importer does for the
    /// materials it maps itself, so SRP batching and the pipeline's tools keep working.
    /// </remarks>
    public static class MaterialWriter
    {
        /// <summary>
        /// Write <paramref name="surface"/> onto <paramref name="material"/>, on the lit
        /// or unlit shader <paramref name="host"/>'s pipeline names. Returns false, the
        /// material untouched, where there is no host (a pipeline no door serves), the
        /// host names no table, or the project lacks the shader.
        /// </summary>
        /// <param name="material">The material to write.</param>
        /// <param name="surface">What to write.</param>
        /// <param name="host">The render pipeline's host (<see cref="RenderPipelineHosts.Current"/>), or null.</param>
        /// <param name="losses">Where each thing the target cannot show is said (null: not said).</param>
        /// <exception cref="ArgumentNullException"><paramref name="material"/> or <paramref name="surface"/> is null.</exception>
        public static bool Write(Material material, LitSurface surface, IRenderPipelineHost host, ICollection<string> losses = null)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (surface == null) throw new ArgumentNullException(nameof(surface));
            LitTable table = host?.Lit;
            if (table == null) return false;
            string shaderName = surface.unlit ? UnlitShader(table, surface) : table.Lit;
            Shader shader = Shader.Find(shaderName);
            if (shader == null) return false;

            material.shader = shader;
            material.shaderKeywords = new string[0];
            ClearMaps(material);
            var mapped = new List<string>();

            Color baseColor = surface.baseColor;
            if (surface.alpha == LitAlpha.Opaque) baseColor.a = 1f;
            if (surface.unlit)
            {
                SetColor(material, table.UnlitColor ?? table.BaseColor, baseColor);
                SetMap(material, table.UnlitMap ?? table.BaseMap, surface.baseMap, mapped);
                bool tinted = baseColor.r < 0.999f || baseColor.g < 0.999f || baseColor.b < 0.999f;
                if (!table.UnlitTints && surface.baseMap != null && tinted)
                    losses?.Add($"{shader.name} takes no tint over its map: its colour factor is not shown");
                if (shaderName == table.UnlitUntextured && surface.alpha != LitAlpha.Opaque)
                    losses?.Add($"{shader.name} draws opaque: its opacity is not shown");
            }
            else
            {
                foreach (var pair in table.Fixed) SetFloat(material, pair.Key, pair.Value);
                SetColor(material, table.BaseColor, baseColor);
                SetMap(material, table.BaseMap, surface.baseMap, mapped);
                SetColor(material, table.MetaColor, baseColor);
                SetMap(material, table.MetaMap, surface.baseMap, mapped);
                Scalar(material, table.Metallic, surface.metallic);
                SetFloat(material, table.Smoothness, 1f - surface.roughness);
                if (surface.maskMap != null) Mask(material, table, surface.maskMap, surface.maskHasOcclusion, mapped);
                if (surface.normalMap != null)
                {
                    SetMap(material, table.NormalMap, surface.normalMap, mapped);
                    SetFloat(material, table.NormalScale, surface.normalScale);
                    foreach (string keyword in table.NormalKeywords) material.EnableKeyword(keyword);
                }
                Emission(material, table, surface, mapped);
            }
            Alpha(material, table, host, surface, shaderName, losses);
            Faces(material, table, surface, losses);
            // The pipelines' Lit reads ONE transform for every map, the base map's
            // (_BaseMap_ST, _MainTex_ST): it is set whether a base map is bound or
            // not, or a material whose maps are all others loses its tiling.
            foreach (string property in new[] { table.BaseMap, table.MetaMap, table.UnlitMap })
                if (Has(material, property) && !mapped.Contains(property)) mapped.Add(property);
            foreach (string property in mapped)
            {
                material.SetTextureScale(property, surface.tiling);
                material.SetTextureOffset(property, surface.offset);
            }
            return true;
        }

        /// <summary>
        /// Bind <paramref name="mask"/>, a mask in Unity's layout (<see cref="MaskMap"/>),
        /// on <paramref name="material"/> as its pipeline's Lit reads it -- the metal and
        /// smoothness map, and its occlusion where <paramref name="occlusion"/> -- leaving
        /// the rest of the material as it is. Returns false, the material untouched, where
        /// there is no table or the material is not on the pipeline's Lit.
        /// </summary>
        /// <param name="host">The render pipeline's host (<see cref="RenderPipelineHosts.Current"/>), or null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="material"/> or <paramref name="mask"/> is null.</exception>
        public static bool BindMask(Material material, Texture mask, bool occlusion, IRenderPipelineHost host)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            if (mask == null) throw new ArgumentNullException(nameof(mask));
            LitTable table = host?.Lit;
            if (table == null || material.shader == null || material.shader.name != table.Lit) return false;
            var mapped = new List<string>();
            Mask(material, table, mask, occlusion, mapped);
            // The mask follows the base map's tiling, as every main map does.
            Vector2 tiling = Has(material, table.BaseMap) ? material.GetTextureScale(table.BaseMap) : Vector2.one;
            Vector2 offset = Has(material, table.BaseMap) ? material.GetTextureOffset(table.BaseMap) : Vector2.zero;
            foreach (string property in mapped)
            {
                material.SetTextureScale(property, tiling);
                material.SetTextureOffset(property, offset);
            }
            return true;
        }

        // -- the effects' twins ---------------------------------------------------------

        /// <summary>
        /// A twin of <paramref name="original"/> for <paramref name="state"/>: a copy that
        /// differs only by what the state switches, through <paramref name="host"/>'s table
        /// and blend switch -- blended for <see cref="TwinState.Blended"/> (a material
        /// already drawn in a transparent queue stays as it is), its emission on and its
        /// emission map set aside for <see cref="TwinState.Glowing"/>, its emission on and
        /// its map kept for <see cref="TwinState.Emitting"/> (<see cref="Normalized"/>: a
        /// glow takes the place of an emission). An effect instances it while the effect
        /// is active and puts the original back at rest; one made in the editor is the
        /// template whose shader variant a player build keeps. Returns null for
        /// <see cref="TwinState.None"/> (the original is its own), or where there is no
        /// host or the host names no table (a pipeline no door serves).
        /// </summary>
        /// <param name="original">The material as authored.</param>
        /// <param name="state">What the effect asks of it.</param>
        /// <param name="host">The render pipeline's host (<see cref="RenderPipelineHosts.Current"/>), or null.</param>
        /// <param name="losses">Where each thing the twin cannot show is said (null: not said).</param>
        /// <exception cref="ArgumentNullException"><paramref name="original"/> is null.</exception>
        public static Material Twin(Material original, TwinState state, IRenderPipelineHost host, ICollection<string> losses = null)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            LitTable table = host?.Lit;
            state = Normalized(state);
            if (table == null || state == TwinState.None) return null;
            var twin = new Material(original) { name = $"{original.name} ({TwinLabel(state)})" };
            string shader = original.shader != null ? original.shader.name : "its shader";
            if ((state & TwinState.Blended) != 0 && twin.renderQueue <= (int)RenderQueue.GeometryLast
                && !host.SetBlended(twin, true))
                losses?.Add($"{shader} has no blend switch the {host.Name} pipeline's host knows: an opacity below 1 is not shown");
            if ((state & (TwinState.Glowing | TwinState.Emitting)) != 0)
            {
                bool glowing = (state & TwinState.Glowing) != 0;
                if (glowing && Has(twin, table.EmissionMap)) twin.SetTexture(table.EmissionMap, null);
                if (table.EmissionKeyword != null)
                {
                    // Built-in's and URP's turns emission on; HDRP's samples the map, set aside by a glow.
                    if (!table.EmissionKeywordForMapOnly) twin.EnableKeyword(table.EmissionKeyword);
                    else if (glowing) twin.DisableKeyword(table.EmissionKeyword);
                }
                twin.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                if (!Has(twin, table.EmissionColor))
                    losses?.Add($"{shader} has no emission colour the {host.Name} pipeline's table names: an added glow or emission is not shown");
            }
            return twin;
        }

        /// <summary>
        /// <paramref name="state"/> as a twin is made for it: a glow takes the place of an
        /// emission (both turn emission on; the glow also sets the map aside), so an effect
        /// keys its twins by this.
        /// </summary>
        public static TwinState Normalized(TwinState state)
            => (state & TwinState.Glowing) != 0 ? state & ~TwinState.Emitting : state;

        /// <summary>
        /// Write an effect's values onto <paramref name="twin"/>, an instance of
        /// <paramref name="original"/>'s twin: its opacity, the original's times
        /// <paramref name="alpha"/>, and, when given, its <paramref name="emission"/> (linear
        /// RGB, above 1 where brighter), each where the shader has the slot. Values alone,
        /// never a keyword, pass or queue, so the instance keeps its template's variant. For
        /// a shader the table names it allocates nothing (an effect writes it every frame its
        /// value moves); another's main colour is looked up by name.
        /// </summary>
        /// <param name="host">The render pipeline's host, or null: nothing is written.</param>
        /// <exception cref="ArgumentNullException"><paramref name="twin"/> or <paramref name="original"/> is null.</exception>
        public static void SetTwinValues(Material twin, Material original, IRenderPipelineHost host, float alpha, Color? emission)
        {
            if (twin == null) throw new ArgumentNullException(nameof(twin));
            if (original == null) throw new ArgumentNullException(nameof(original));
            LitTable table = host?.Lit;
            if (table == null) return;
            alpha = Mathf.Clamp01(alpha);
            bool slot = Opacity(twin, original, table.BaseColor, alpha);
            slot |= Opacity(twin, original, table.UnlitColor, alpha);
            Opacity(twin, original, table.MetaColor, alpha);
            // A shader the table does not name (a feature's own): its main colour, as Unity's
            // Material.color reads it ([MainColor], else _Color).
            if (!slot) Opacity(twin, original, MainColor(original.shader), alpha);
            if (!emission.HasValue || !Has(twin, table.EmissionColor)) return;
            Color e = emission.Value;
            e.a = 1f;
            twin.SetColor(table.EmissionColor, MaterialColor.Encode(e));
        }

        /// <summary>
        /// The uniform emission <paramref name="material"/> shows, linear RGB: its emission
        /// colour, decoded (<see cref="MaterialColor.Decode"/>), its map not counted; black
        /// where its emission is off or <paramref name="host"/>'s table names no slot for it.
        /// </summary>
        public static Color EmissionOf(Material material, IRenderPipelineHost host)
        {
            LitTable table = host?.Lit;
            if (material == null || table == null || !Has(material, table.EmissionColor)) return Color.black;
            if (table.EmissionKeyword != null && !table.EmissionKeywordForMapOnly && !material.IsKeywordEnabled(table.EmissionKeyword))
                return Color.black;
            Color e = MaterialColor.Decode(material.GetColor(table.EmissionColor));
            e.a = 1f;
            return e;
        }

        /// <summary>Whether <paramref name="material"/>'s emission is mapped: a texture in its table's emission map slot.</summary>
        public static bool EmissionMapped(Material material, IRenderPipelineHost host)
        {
            LitTable table = host?.Lit;
            return material != null && table != null && Has(material, table.EmissionMap)
                   && material.GetTexture(table.EmissionMap) != null;
        }

        static bool Opacity(Material twin, Material original, string property, float alpha)
        {
            if (!Has(twin, property) || !Has(original, property)) return false;
            Color c = original.GetColor(property);
            c.a *= alpha;
            twin.SetColor(property, c);
            return true;
        }

        // The colour property a shader marks [MainColor], else _Color, else null.
        static string MainColor(Shader shader)
        {
            if (shader == null) return null;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
                if ((shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainColor) != 0) return shader.GetPropertyName(i);
            return shader.FindPropertyIndex("_Color") >= 0 ? "_Color" : null;
        }

        // The twin's name after its state: Blended, Glowing, Emitting, or Blended+ either.
        static string TwinLabel(TwinState state)
        {
            string glow = (state & TwinState.Glowing) != 0 ? "Glowing" : (state & TwinState.Emitting) != 0 ? "Emitting" : null;
            if ((state & TwinState.Blended) == 0) return glow;
            return glow == null ? "Blended" : "Blended+" + glow;
        }

        /// <summary>
        /// Whether <paramref name="a"/> renders as <paramref name="b"/> does, by what a
        /// write sets: the shader, its keywords, render queue, <c>RenderType</c> tag, the
        /// passes the blend switch turns, the emission's global illumination, double-sided
        /// global illumination and every property of the shader, each map's tiling too.
        /// An action writes into a copy and changes the material only where they differ,
        /// so a second run plans nothing.
        /// </summary>
        public static bool Equivalent(Material a, Material b)
        {
            if (a == null || b == null) return a == b;
            if (a.shader != b.shader || a.renderQueue != b.renderQueue
                || a.GetTag("RenderType", false) != b.GetTag("RenderType", false)
                || a.globalIlluminationFlags != b.globalIlluminationFlags || a.doubleSidedGI != b.doubleSidedGI)
                return false;
            var keywords = new HashSet<string>(a.shaderKeywords);
            if (!keywords.SetEquals(b.shaderKeywords)) return false;
            foreach (string pass in Passes)
                if (a.GetShaderPassEnabled(pass) != b.GetShaderPassEnabled(pass)) return false;
            Shader shader = b.shader;
            if (shader == null) return true;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string name = shader.GetPropertyName(i);
                switch (shader.GetPropertyType(i))
                {
                    case ShaderPropertyType.Color:
                    case ShaderPropertyType.Vector:
                        if (a.GetVector(name) != b.GetVector(name)) return false;
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        if (!Mathf.Approximately(a.GetFloat(name), b.GetFloat(name))) return false;
                        break;
                    case ShaderPropertyType.Int:
                        if (a.GetInteger(name) != b.GetInteger(name)) return false;
                        break;
                    case ShaderPropertyType.Texture:
                        if (a.GetTexture(name) != b.GetTexture(name) || a.GetTextureScale(name) != b.GetTextureScale(name)
                            || a.GetTextureOffset(name) != b.GetTextureOffset(name))
                            return false;
                        break;
                }
            }
            return true;
        }

        /// <summary>The passes a blend switch turns on or off (URP's depth prepass), compared by <see cref="Equivalent"/>.</summary>
        static readonly string[] Passes = { "DepthOnly", "ShadowCaster", "MotionVectors" };

        static string UnlitShader(LitTable table, LitSurface surface)
        {
            if (surface.baseMap == null && table.UnlitUntextured != null) return table.UnlitUntextured;
            if (surface.alpha == LitAlpha.Blend && table.UnlitBlended != null) return table.UnlitBlended;
            if (surface.alpha == LitAlpha.Mask && table.UnlitCutout != null) return table.UnlitCutout;
            return table.Unlit;
        }

        static void Mask(Material material, LitTable table, Texture mask, bool occlusion, List<string> mapped)
        {
            SetMap(material, table.MaskMap, mask, mapped);
            if (table.MaskKeyword != null) material.EnableKeyword(table.MaskKeyword);
            foreach (var pair in table.Masked) SetFloat(material, pair.Key, pair.Value);
            if (!occlusion || table.OcclusionMap == null) return;
            SetMap(material, table.OcclusionMap, mask, mapped);
            SetFloat(material, table.OcclusionStrength, 1f);
            if (table.OcclusionKeyword != null) material.EnableKeyword(table.OcclusionKeyword);
        }

        static void Emission(Material material, LitTable table, LitSurface surface, List<string> mapped)
        {
            bool emits = surface.emission.maxColorComponent > 0f;
            Color emission = emits ? surface.emission : Color.black;
            emission.a = 1f;
            SetColor(material, table.EmissionColor, emission);
            if (!emits)
            {
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                return;
            }
            SetMap(material, table.EmissionMap, surface.emissionMap, mapped);
            if (table.EmissionKeyword != null && (!table.EmissionKeywordForMapOnly || surface.emissionMap != null))
                material.EnableKeyword(table.EmissionKeyword);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        // The clip toggle first: URP's blend switch reads it to queue a cut surface.
        static void Alpha(Material material, LitTable table, IRenderPipelineHost host, LitSurface surface,
                          string shaderName, ICollection<string> losses)
        {
            bool clip = surface.alpha == LitAlpha.Mask, blend = surface.alpha == LitAlpha.Blend;
            SetFloat(material, table.AlphaClip, clip ? 1f : 0f);
            // Built-in's unlit shaders blend or cut by being the one that does.
            bool chosen = surface.unlit && (shaderName == table.UnlitBlended || shaderName == table.UnlitCutout
                                            || shaderName == table.UnlitUntextured);
            if (!chosen && !host.SetBlended(material, blend) && blend)
                losses?.Add($"{material.shader.name} has no blend switch the {host.Name} pipeline's host knows: its opacity is not shown");
            if (!clip) return;
            SetFloat(material, table.Cutoff, surface.alphaCutoff);
            if (table.AlphaTestKeyword != null) material.EnableKeyword(table.AlphaTestKeyword);
            foreach (var pair in table.Clipped) SetFloat(material, pair.Key, pair.Value);
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
        }

        static void Faces(Material material, LitTable table, LitSurface surface, ICollection<string> losses)
        {
            bool both = surface.doubleSided;
            if (Has(material, table.Cull))
                material.SetFloat(table.Cull, (float)(both ? CullMode.Off : CullMode.Back));
            else if (both)
                losses?.Add($"it is double-sided, and {material.shader.name} draws its front faces alone");
            SetFloat(material, table.DoubleSided, both ? 1f : 0f);
            if (table.DoubleSidedKeyword != null)
            {
                if (both) material.EnableKeyword(table.DoubleSidedKeyword);
                else material.DisableKeyword(table.DoubleSidedKeyword);
            }
            material.doubleSidedGI = both;
        }

        // Every map slot empty and untiled: a write states all of them.
        static void ClearMaps(Material material)
        {
            Shader shader = material.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture) continue;
                string name = shader.GetPropertyName(i);
                material.SetTexture(name, null);
                material.SetTextureScale(name, Vector2.one);
                material.SetTextureOffset(name, Vector2.zero);
            }
        }

        // Whether the pipeline's table names the property and the material's shader has it.
        static bool Has(Material material, string property) => property != null && material.HasProperty(property);

        static void SetMap(Material material, string property, Texture texture, List<string> mapped)
        {
            if (texture == null || !Has(material, property)) return;
            material.SetTexture(property, texture);
            if (!mapped.Contains(property)) mapped.Add(property);
        }

        static void SetColor(Material material, string property, Color linear)
        {
            if (Has(material, property)) material.SetColor(property, MaterialColor.Encode(linear));
        }

        static void SetFloat(Material material, string property, float value)
        {
            if (Has(material, property)) material.SetFloat(property, value);
        }

        // A float as the shader means it: a [Gamma] property (Standard's _Metallic) is
        // converted to linear on upload in a Linear project, so it is written encoded there.
        static void Scalar(Material material, string property, float value)
        {
            if (property == null) return;
            int index = material.shader.FindPropertyIndex(property);
            if (index < 0) return;
            if ((material.shader.GetPropertyFlags(index) & ShaderPropertyFlags.Gamma) != 0
                && QualitySettings.activeColorSpace == ColorSpace.Linear)
                value = Mathf.LinearToGammaSpace(value);
            material.SetFloat(property, value);
        }
    }
}
