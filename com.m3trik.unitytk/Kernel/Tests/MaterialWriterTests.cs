using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// The one material writer, row by row of the declared table: Built-in's
    /// Standard and Unlit shaders (in any project: they are always there), URP's Lit
    /// and Unlit (in the URP project; inconclusive in a Built-in one), and HDRP's row
    /// against stand-in shaders holding HDRP Lit's and Unlit's property names (no
    /// project here has HDRP). Each row: the base colour, the mask with its occlusion,
    /// the normal map, emission above 1, the three alpha modes, double-sided, unlit,
    /// the UV transform, a complete state (nothing an earlier mapping left survives),
    /// and no write where there is no host or shader; and per row the effects' twins
    /// (blended, glowing with the emission map set aside, emitting with it kept), the
    /// original untouched, and a twin's values (alpha times the fade, the emission given).
    /// </summary>
    public class MaterialWriterTests
    {
        const string Folder = "Assets/UnitytkMaterialWriterTests";

        readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var made in _made) Object.DestroyImmediate(made);
            _made.Clear();
            AssetDatabase.DeleteAsset(Folder);
        }

        T Made<T>(T made) where T : Object
        {
            _made.Add(made);
            return made;
        }

        Material Blank() => Made(new Material(Shader.Find("Unlit/Color")) { name = "Written" });

        Texture2D Map(string name) => Made(new Texture2D(4, 4) { name = name });

        static void AssertColour(Color stored, Color linear, string why)
        {
            var encoded = MaterialColor.Encode(linear);
            Assert.That(new[] { stored.r, stored.g, stored.b, stored.a },
                        Is.EqualTo(new[] { encoded.r, encoded.g, encoded.b, encoded.a }).Within(1e-4f), why);
        }

        // -- Built-in --------------------------------------------------------------------

        [Test]
        public void Built_in_writes_standard_from_the_factors()
        {
            var m = Blank();
            var losses = new List<string>();
            var surface = new LitSurface { baseColor = new Color(0.25f, 0.5f, 0.75f, 0.6f), metallic = 0.3f, roughness = 0.4f, alpha = LitAlpha.Blend };
            Assert.That(MaterialWriter.Write(m, surface, RenderPipelineHosts.BuiltIn, losses), Is.True);
            Assert.That(m.shader.name, Is.EqualTo("Standard"));
            AssertColour(m.GetColor("_Color"), surface.baseColor, "the linear factor, encoded, its alpha the opacity");
            Assert.That(m.GetFloat("_Glossiness"), Is.EqualTo(0.6f).Within(1e-5f), "1 - roughness");
            float metal = QualitySettings.activeColorSpace == ColorSpace.Linear ? Mathf.LinearToGammaSpace(0.3f) : 0.3f;
            Assert.That(m.GetFloat("_Metallic"), Is.EqualTo(metal).Within(1e-5f), "Standard's [Gamma] metal, encoded");
            Assert.That(m.GetFloat("_Mode"), Is.EqualTo(2f), "BLEND: Standard's Fade");
            Assert.That(m.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            Assert.That(m.IsKeywordEnabled("_METALLICGLOSSMAP"), Is.False, "no mask: the factors alone");
            Assert.That(losses, Is.Empty);
        }

        [Test]
        public void Built_in_binds_the_mask_as_its_metallic_and_occlusion_map()
        {
            var m = Blank();
            var mask = Map("Ball_ORM_MetallicSmoothness");
            var surface = new LitSurface { maskMap = mask, maskHasOcclusion = true };
            MaterialWriter.Write(m, surface, RenderPipelineHosts.BuiltIn);
            Assert.That(m.GetTexture("_MetallicGlossMap"), Is.SameAs(mask));
            Assert.That(m.IsKeywordEnabled("_METALLICGLOSSMAP"), Is.True);
            Assert.That(m.GetFloat("_GlossMapScale"), Is.EqualTo(1f), "smoothness is the map's alpha as packed");
            Assert.That(m.GetFloat("_SmoothnessTextureChannel"), Is.EqualTo(0f), "smoothness from the metallic map's alpha");
            Assert.That(m.GetTexture("_OcclusionMap"), Is.SameAs(mask), "occlusion read from the mask's G");
            Assert.That(m.GetFloat("_OcclusionStrength"), Is.EqualTo(1f));

            var noOcclusion = Blank();
            MaterialWriter.Write(noOcclusion, new LitSurface { maskMap = mask }, RenderPipelineHosts.BuiltIn);
            Assert.That(noOcclusion.GetTexture("_OcclusionMap"), Is.Null, "a mask with a white G binds no occlusion");
        }

        [Test]
        public void Built_in_cuts_out_a_mask_and_says_double_sided_is_lost()
        {
            var m = Blank();
            var losses = new List<string>();
            MaterialWriter.Write(m, new LitSurface { alpha = LitAlpha.Mask, alphaCutoff = 0.3f, doubleSided = true },
                                 RenderPipelineHosts.BuiltIn, losses);
            Assert.That(m.GetFloat("_Mode"), Is.EqualTo(1f), "Standard's Cutout");
            Assert.That(m.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
            Assert.That(m.GetFloat("_Cutoff"), Is.EqualTo(0.3f).Within(1e-6f));
            Assert.That(m.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
            Assert.That(m.GetTag("RenderType", false), Is.EqualTo("TransparentCutout"));
            Assert.That(m.doubleSidedGI, Is.True);
            Assert.That(losses, Has.Some.Contains("double-sided"), "Standard draws its front faces alone");
        }

        [Test]
        public void Emission_above_one_is_kept_and_turned_on_and_none_turns_it_off()
        {
            var m = Blank();
            var glow = new Color(4f, 2f, 0.5f, 1f);
            var map = Map("Glow_Emissive");
            MaterialWriter.Write(m, new LitSurface { emission = glow, emissionMap = map }, RenderPipelineHosts.BuiltIn);
            AssertColour(m.GetColor("_EmissionColor"), glow, "the HDR colour, factor x strength");
            Assert.That(m.GetColor("_EmissionColor").r, Is.GreaterThan(1f), "above 1 kept");
            Assert.That(m.GetTexture("_EmissionMap"), Is.SameAs(map));
            Assert.That(m.IsKeywordEnabled("_EMISSION"), Is.True);
            Assert.That(m.globalIlluminationFlags, Is.EqualTo(MaterialGlobalIlluminationFlags.RealtimeEmissive));

            MaterialWriter.Write(m, new LitSurface(), RenderPipelineHosts.BuiltIn);
            Assert.That(m.IsKeywordEnabled("_EMISSION"), Is.False);
            Assert.That(m.GetTexture("_EmissionMap"), Is.Null, "the earlier map does not survive");
            Assert.That(m.globalIlluminationFlags, Is.EqualTo(MaterialGlobalIlluminationFlags.EmissiveIsBlack));
        }

        [Test]
        public void Built_in_unlit_picks_its_shader_by_map_and_alpha_and_says_what_it_loses()
        {
            var map = Map("Sign_BaseColor");
            var m = Blank();
            var losses = new List<string>();
            MaterialWriter.Write(m, new LitSurface { unlit = true, baseMap = map, alpha = LitAlpha.Blend }, RenderPipelineHosts.BuiltIn, losses);
            Assert.That(m.shader.name, Is.EqualTo("Unlit/Transparent"));
            Assert.That(m.GetTexture("_MainTex"), Is.SameAs(map));
            Assert.That(losses, Is.Empty);

            MaterialWriter.Write(m, new LitSurface { unlit = true, baseMap = map, alpha = LitAlpha.Mask, alphaCutoff = 0.4f },
                                 RenderPipelineHosts.BuiltIn, losses);
            Assert.That(m.shader.name, Is.EqualTo("Unlit/Transparent Cutout"));
            Assert.That(m.GetFloat("_Cutoff"), Is.EqualTo(0.4f).Within(1e-6f));

            MaterialWriter.Write(m, new LitSurface { unlit = true, baseMap = map, baseColor = Color.red }, RenderPipelineHosts.BuiltIn, losses);
            Assert.That(m.shader.name, Is.EqualTo("Unlit/Texture"));
            Assert.That(losses, Has.Some.Contains("no tint"));

            losses.Clear();
            MaterialWriter.Write(m, new LitSurface { unlit = true, baseColor = new Color(0f, 1f, 0f, 0.5f), alpha = LitAlpha.Blend },
                                 RenderPipelineHosts.BuiltIn, losses);
            Assert.That(m.shader.name, Is.EqualTo("Unlit/Color"));
            AssertColour(m.GetColor("_Color"), new Color(0f, 1f, 0f, 0.5f), "its colour");
            Assert.That(losses, Has.Some.Contains("draws opaque"));
        }

        [Test]
        public void The_uv_transform_tiles_every_map_it_binds_and_a_write_is_a_complete_state()
        {
            var m = Blank();
            var normal = Map("Ball_Normal");
            MaterialWriter.Write(m, new LitSurface { normalMap = normal, normalScale = 0.5f }, RenderPipelineHosts.BuiltIn);
            Assert.That(m.GetTexture("_BumpMap"), Is.SameAs(normal));
            Assert.That(m.GetFloat("_BumpScale"), Is.EqualTo(0.5f));
            Assert.That(m.IsKeywordEnabled("_NORMALMAP"), Is.True);

            var colour = Map("Tile_BaseColor");
            MaterialWriter.Write(m, new LitSurface { baseMap = colour, tiling = new Vector2(3f, 2f), offset = new Vector2(0.25f, -1.5f) },
                                 RenderPipelineHosts.BuiltIn);
            Assert.That(m.GetTextureScale("_MainTex"), Is.EqualTo(new Vector2(3f, 2f)));
            Assert.That(m.GetTextureOffset("_MainTex"), Is.EqualTo(new Vector2(0.25f, -1.5f)));
            Assert.That(m.GetTexture("_BumpMap"), Is.Null, "the earlier normal map does not survive");
            Assert.That(m.IsKeywordEnabled("_NORMALMAP"), Is.False);
        }

        [Test]
        public void With_no_base_map_the_base_property_still_carries_the_uv_transform()
        {
            // The pipeline's Lit reads one transform for every map, the base map's
            // (_MainTex_ST in Built-in, _BaseMap_ST in URP): a material with a normal map
            // alone tiles by it too.
            var m = Blank();
            var surface = new LitSurface { normalMap = Map("Tile_Normal"), tiling = new Vector2(3f, 2f), offset = new Vector2(0.5f, 0.25f) };
            Assert.That(MaterialWriter.Write(m, surface, RenderPipelineHosts.Current ?? RenderPipelineHosts.BuiltIn), Is.True);
            var table = (RenderPipelineHosts.Current ?? RenderPipelineHosts.BuiltIn).Lit;
            Assert.That(m.GetTexture(table.BaseMap), Is.Null, "no base map is bound");
            Assert.That(m.GetTextureScale(table.BaseMap), Is.EqualTo(new Vector2(3f, 2f)));
            Assert.That(m.GetTextureOffset(table.BaseMap), Is.EqualTo(new Vector2(0.5f, 0.25f)));
            Assert.That(m.GetTextureScale(table.NormalMap), Is.EqualTo(new Vector2(3f, 2f)));
        }

        [Test]
        public void No_host_or_no_shader_writes_nothing()
        {
            var m = Blank();
            Assert.That(MaterialWriter.Write(m, new LitSurface(), null), Is.False, "a pipeline no door serves");
            var missing = new PureRenderPipelineHost { Lit = LitTable.HighDefinition };
            Assert.That(MaterialWriter.Write(m, new LitSurface(), missing), Is.False, "HDRP's shaders are not in this project");
            Assert.That(m.shader.name, Is.EqualTo("Unlit/Color"), "untouched");
        }

        [Test]
        public void Equivalent_holds_a_written_material_to_what_a_second_write_makes()
        {
            var surface = new LitSurface { baseColor = Color.gray, emission = Color.red, alpha = LitAlpha.Mask };
            var a = Blank();
            var b = Blank();
            MaterialWriter.Write(a, surface, RenderPipelineHosts.BuiltIn);
            MaterialWriter.Write(b, surface, RenderPipelineHosts.BuiltIn);
            Assert.That(MaterialWriter.Equivalent(a, b), Is.True);
            surface.alphaCutoff = 0.9f;
            MaterialWriter.Write(b, surface, RenderPipelineHosts.BuiltIn);
            Assert.That(MaterialWriter.Equivalent(a, b), Is.False);
        }

        // -- URP ------------------------------------------------------------------------

        static IRenderPipelineHost Universal()
        {
            var host = RenderPipelineHosts.Current;
            Assume.That(host != null && host.Lit == LitTable.Universal, "URP's own host: the URP project's test");
            return host;
        }

        [Test]
        public void Urp_writes_its_lit_with_the_mask_occlusion_and_meta_pair()
        {
            var host = Universal();
            var m = Blank();
            var colour = Map("Ball_BaseColor");
            var mask = Map("Ball_ORM_MetallicSmoothness");
            var normal = Map("Ball_Normal");
            var surface = new LitSurface
            {
                baseColor = new Color(0.8f, 0.2f, 0.1f, 1f), baseMap = colour, maskMap = mask, maskHasOcclusion = true,
                normalMap = normal, emission = new Color(3f, 3f, 3f, 1f),
            };
            Assert.That(MaterialWriter.Write(m, surface, host), Is.True);
            Assert.That(m.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"));
            Assert.That(m.GetFloat("_WorkflowMode"), Is.EqualTo(1f), "metallic");
            AssertColour(m.GetColor("_BaseColor"), surface.baseColor, "its base colour");
            Assert.That(m.GetTexture("_BaseMap"), Is.SameAs(colour));
            Assert.That(m.GetTexture("_MainTex"), Is.SameAs(colour), "the hidden pair URP bakes lighting from");
            AssertColour(m.GetColor("_Color"), surface.baseColor, "the hidden pair's colour");
            Assert.That(m.GetTexture("_MetallicGlossMap"), Is.SameAs(mask));
            Assert.That(m.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
            Assert.That(m.GetFloat("_Smoothness"), Is.EqualTo(1f), "scales the mask's alpha");
            Assert.That(m.GetTexture("_OcclusionMap"), Is.SameAs(mask));
            Assert.That(m.IsKeywordEnabled("_OCCLUSIONMAP"), Is.True);
            Assert.That(m.GetTexture("_BumpMap"), Is.SameAs(normal));
            Assert.That(m.IsKeywordEnabled("_NORMALMAP"), Is.True);
            Assert.That(m.IsKeywordEnabled("_EMISSION"), Is.True);
            Assert.That(m.GetColor("_EmissionColor").r, Is.GreaterThan(1f), "emission above 1 kept");
            Assert.That(m.renderQueue, Is.EqualTo((int)RenderQueue.Geometry));
        }

        [Test]
        public void Urp_blends_cuts_and_draws_both_faces_by_its_own_switches()
        {
            var host = Universal();
            var m = Blank();
            MaterialWriter.Write(m, new LitSurface { alpha = LitAlpha.Blend, doubleSided = true }, host);
            Assert.That(m.GetFloat("_Surface"), Is.EqualTo(1f), "Transparent");
            Assert.That(m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
            Assert.That(m.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            Assert.That(m.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Off));

            var losses = new List<string>();
            MaterialWriter.Write(m, new LitSurface { alpha = LitAlpha.Mask, alphaCutoff = 0.25f }, host, losses);
            Assert.That(m.GetFloat("_Surface"), Is.EqualTo(0f));
            Assert.That(m.GetFloat("_AlphaClip"), Is.EqualTo(1f));
            Assert.That(m.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
            Assert.That(m.GetFloat("_Cutoff"), Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(m.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
            Assert.That(m.GetFloat("_Cull"), Is.EqualTo((float)CullMode.Back));
            Assert.That(losses, Is.Empty);
        }

        [Test]
        public void Urp_unlit_is_its_unlit_shader_tinted_and_blended()
        {
            var host = Universal();
            var m = Blank();
            var map = Map("Sign_BaseColor");
            var losses = new List<string>();
            MaterialWriter.Write(m, new LitSurface { unlit = true, baseMap = map, baseColor = new Color(1f, 0f, 0f, 0.5f), alpha = LitAlpha.Blend },
                                 host, losses);
            Assert.That(m.shader.name, Is.EqualTo("Universal Render Pipeline/Unlit"));
            Assert.That(m.GetTexture("_BaseMap"), Is.SameAs(map));
            Assert.That(m.GetFloat("_Surface"), Is.EqualTo(1f));
            Assert.That(losses, Is.Empty, "URP's Unlit tints and blends");
        }

        // -- HDRP: its row, against stand-ins holding HDRP's property names -----------------

        const string HdrpLit = "Hidden/UnitytkTests/HDRP Lit Stand-in";
        const string HdrpUnlit = "Hidden/UnitytkTests/HDRP Unlit Stand-in";

        static void StandIn(string name, string properties)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string file = $"{Folder}/{Path.GetFileName(name).Replace(' ', '_')}.shader";
            File.WriteAllText(file, $"Shader \"{name}\" {{ Properties {{ {properties} }} SubShader {{ Pass {{ CGPROGRAM\n" +
                                    "#pragma vertex vert\n#pragma fragment frag\n" +
                                    // Its keywords declared, as HDRP's are: a material holds no keyword its shader lacks.
                                    "#pragma shader_feature_local _MASKMAP\n#pragma shader_feature_local _NORMALMAP\n" +
                                    "#pragma shader_feature_local _NORMALMAP_TANGENT_SPACE\n#pragma shader_feature_local _EMISSIVE_COLOR_MAP\n" +
                                    "#pragma shader_feature_local _ALPHATEST_ON\n#pragma shader_feature_local _DOUBLESIDED_ON\n" +
                                    "float4 vert(float4 v : POSITION) : SV_POSITION { return v; }\n" +
                                    "fixed4 frag() : SV_Target { return 1; }\nENDCG } } }");
            AssetDatabase.ImportAsset(file, ImportAssetOptions.ForceSynchronousImport);
        }

        static PureRenderPipelineHost Hdrp()
        {
            StandIn(HdrpLit,
                "_BaseColor (\"c\", Color) = (1,1,1,1) _BaseColorMap (\"t\", 2D) = \"white\" {} _Metallic (\"f\", Float) = 0 " +
                "_Smoothness (\"f\", Float) = 0.5 _MaskMap (\"t\", 2D) = \"white\" {} _MetallicRemapMin (\"f\", Float) = 0.2 " +
                "_MetallicRemapMax (\"f\", Float) = 0.8 _SmoothnessRemapMin (\"f\", Float) = 0.2 _SmoothnessRemapMax (\"f\", Float) = 0.8 " +
                "_AORemapMin (\"f\", Float) = 0.2 _AORemapMax (\"f\", Float) = 0.8 _NormalMap (\"t\", 2D) = \"bump\" {} " +
                "_NormalScale (\"f\", Float) = 1 [HDR] _EmissiveColor (\"c\", Color) = (0,0,0,1) _EmissiveColorMap (\"t\", 2D) = \"white\" {} " +
                "_UseEmissiveIntensity (\"f\", Float) = 1 _AlphaCutoffEnable (\"f\", Float) = 0 _AlphaCutoff (\"f\", Float) = 0.5 " +
                "_CullMode (\"f\", Float) = 2 _DoubleSidedEnable (\"f\", Float) = 0");
            StandIn(HdrpUnlit,
                "_UnlitColor (\"c\", Color) = (1,1,1,1) _UnlitColorMap (\"t\", 2D) = \"white\" {} _AlphaCutoffEnable (\"f\", Float) = 0 " +
                "_AlphaCutoff (\"f\", Float) = 0.5 _CullMode (\"f\", Float) = 2 _DoubleSidedEnable (\"f\", Float) = 0");
            Assert.That(Shader.Find(HdrpLit), Is.Not.Null, "the stand-in imported");
            return new PureRenderPipelineHost("HDRP-like") { Lit = LitTable.HighDefinition.WithShaders(HdrpLit, HdrpUnlit) };
        }

        [Test]
        public void Hdrps_row_binds_its_mask_map_normal_emission_clip_and_faces()
        {
            var host = Hdrp();
            var m = Blank();
            var colour = Map("Ball_BaseColor");
            var mask = Map("Ball_ORM_MetallicSmoothness");
            var normal = Map("Ball_Normal");
            var surface = new LitSurface
            {
                baseColor = new Color(0.5f, 0.5f, 0.5f, 1f), baseMap = colour, maskMap = mask, maskHasOcclusion = true,
                normalMap = normal, normalScale = 0.7f, emission = new Color(5f, 0f, 0f, 1f), alpha = LitAlpha.Mask,
                alphaCutoff = 0.35f, doubleSided = true,
            };
            Assert.That(MaterialWriter.Write(m, surface, host), Is.True);
            Assert.That(m.shader.name, Is.EqualTo(HdrpLit));
            Assert.That(m.GetTexture("_BaseColorMap"), Is.SameAs(colour));
            Assert.That(m.GetTexture("_MaskMap"), Is.SameAs(mask), "HDRP's mask map is Unity's layout exactly");
            Assert.That(m.IsKeywordEnabled("_MASKMAP"), Is.True);
            foreach (string remap in new[] { "_MetallicRemap", "_SmoothnessRemap", "_AORemap" })
            {
                Assert.That(m.GetFloat(remap + "Min"), Is.EqualTo(0f), remap + " at identity");
                Assert.That(m.GetFloat(remap + "Max"), Is.EqualTo(1f), remap + " at identity");
            }
            Assert.That(m.GetTexture("_NormalMap"), Is.SameAs(normal));
            Assert.That(m.GetFloat("_NormalScale"), Is.EqualTo(0.7f).Within(1e-6f));
            Assert.That(m.IsKeywordEnabled("_NORMALMAP") && m.IsKeywordEnabled("_NORMALMAP_TANGENT_SPACE"), Is.True);
            Assert.That(m.GetColor("_EmissiveColor").r, Is.GreaterThan(1f), "emission above 1 kept");
            Assert.That(m.GetFloat("_UseEmissiveIntensity"), Is.EqualTo(0f), "the colour carries the intensity");
            Assert.That(m.IsKeywordEnabled("_EMISSIVE_COLOR_MAP"), Is.False, "a keyword for the map alone, and it has none");
            Assert.That(m.GetFloat("_AlphaCutoffEnable"), Is.EqualTo(1f));
            Assert.That(m.GetFloat("_AlphaCutoff"), Is.EqualTo(0.35f).Within(1e-6f));
            Assert.That(m.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
            Assert.That(m.GetFloat("_CullMode"), Is.EqualTo((float)CullMode.Off));
            Assert.That(m.GetFloat("_DoubleSidedEnable"), Is.EqualTo(1f));
            Assert.That(m.IsKeywordEnabled("_DOUBLESIDED_ON"), Is.True);
            Assert.That(host.Blended, Has.Count.EqualTo(1).And.Contains((m, false)), "the host's switch, opaque");
            Assert.That(m.renderQueue, Is.EqualTo((int)RenderQueue.AlphaTest));
        }

        [Test]
        public void Hdrps_row_writes_unlit_to_its_own_colour_and_map()
        {
            var host = Hdrp();
            var m = Blank();
            var map = Map("Sign_BaseColor");
            MaterialWriter.Write(m, new LitSurface { unlit = true, baseMap = map, baseColor = Color.green, alpha = LitAlpha.Blend }, host);
            Assert.That(m.shader.name, Is.EqualTo(HdrpUnlit));
            Assert.That(m.GetTexture("_UnlitColorMap"), Is.SameAs(map));
            AssertColour(m.GetColor("_UnlitColor"), Color.green, "its colour");
            Assert.That(host.Blended, Has.Some.EqualTo((m, true)), "blended through the host's switch");
        }

        // -- the effects' twins ------------------------------------------------------------

        Material Written(LitSurface surface, IRenderPipelineHost host)
        {
            var m = Blank();
            Assert.That(MaterialWriter.Write(m, surface, host), Is.True);
            return m;
        }

        [Test]
        public void Built_in_twins_blend_glow_and_emit_by_their_own_switches_the_original_untouched()
        {
            var host = RenderPipelineHosts.BuiltIn;
            var map = Map("Glow_Emissive");
            var original = Written(new LitSurface { emission = Color.white, emissionMap = map }, host);
            Assert.That(MaterialWriter.Twin(original, TwinState.None, host), Is.Null, "at rest the original is its own");

            var faded = Made(MaterialWriter.Twin(original, TwinState.Blended, host));
            Assert.That(faded.name, Is.EqualTo("Written (Blended)"));
            Assert.That(faded.GetFloat("_Mode"), Is.EqualTo(2f), "the Standard inspector's Fade");
            Assert.That(faded.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            Assert.That(faded.GetFloat("_ZWrite"), Is.EqualTo(0f));
            Assert.That(faded.GetTexture("_EmissionMap"), Is.SameAs(map), "a fade keeps the map");

            var glowing = Made(MaterialWriter.Twin(original, TwinState.Glowing | TwinState.Emitting, host));
            Assert.That(glowing.name, Is.EqualTo("Written (Glowing)"), "a glow takes an emission's place");
            Assert.That(glowing.GetTexture("_EmissionMap"), Is.Null, "the map set aside");
            Assert.That(glowing.IsKeywordEnabled("_EMISSION"), Is.True);
            Assert.That(glowing.renderQueue, Is.EqualTo(original.renderQueue), "opaque still");

            var emitting = Made(MaterialWriter.Twin(original, TwinState.Emitting | TwinState.Blended, host));
            Assert.That(emitting.name, Is.EqualTo("Written (Blended+Emitting)"));
            Assert.That(emitting.GetTexture("_EmissionMap"), Is.SameAs(map), "an emission keeps its map");

            Assert.That(original.GetFloat("_Mode"), Is.EqualTo(0f), "the original is never changed");
            Assert.That(original.GetTexture("_EmissionMap"), Is.SameAs(map));
            Assert.That(original.renderQueue, Is.LessThan((int)RenderQueue.Transparent));
            Assert.That(MaterialWriter.EmissionMapped(original, host), Is.True);
            Assert.That(MaterialWriter.EmissionOf(original, host).r, Is.EqualTo(1f).Within(1e-4f), "its uniform emission, linear");
        }

        [Test]
        public void A_twins_values_are_its_originals_alpha_times_the_fade_and_the_emission_given()
        {
            var host = RenderPipelineHosts.BuiltIn;
            var original = Written(new LitSurface { baseColor = new Color(0.2f, 0.4f, 0.6f, 0.8f), alpha = LitAlpha.Opaque }, host);
            var twin = Made(MaterialWriter.Twin(original, TwinState.Blended | TwinState.Glowing, host));
            MaterialWriter.SetTwinValues(twin, original, host, 0.5f, new Color(2f, 1f, 0f, 1f));
            Color stored = original.GetColor("_Color");
            Assert.That(twin.GetColor("_Color").a, Is.EqualTo(stored.a * 0.5f).Within(1e-5f), "the original's alpha times the fade");
            Assert.That(twin.GetColor("_Color").r, Is.EqualTo(stored.r).Within(1e-5f), "its colour kept");
            AssertColour(twin.GetColor("_EmissionColor"), new Color(2f, 1f, 0f, 1f), "the emission, linear, encoded");
            Assert.That(original.GetColor("_Color").a, Is.EqualTo(stored.a), "the original untouched");
        }

        [Test]
        public void No_host_makes_no_twin()
        {
            var original = Blank();
            Assert.That(MaterialWriter.Twin(original, TwinState.Blended, null), Is.Null);
            Assert.That(MaterialWriter.Twin(original, TwinState.Blended, new PureRenderPipelineHost { Lit = null }), Is.Null);
        }

        [Test]
        public void Urp_twins_switch_its_surface_and_its_depth_prepass()
        {
            var host = Universal();
            var original = Written(new LitSurface(), host);
            var faded = Made(MaterialWriter.Twin(original, TwinState.Blended, host));
            Assert.That(faded.GetFloat("_Surface"), Is.EqualTo(1f), "Transparent");
            Assert.That(faded.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
            Assert.That(faded.GetShaderPassEnabled("DepthOnly"), Is.False, "no depth while blended");
            Assert.That(faded.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            Assert.That(original.GetShaderPassEnabled("DepthOnly"), Is.True, "the original writes depth");
            var glowing = Made(MaterialWriter.Twin(original, TwinState.Glowing, host));
            Assert.That(glowing.IsKeywordEnabled("_EMISSION"), Is.True);
        }

        [Test]
        public void Hdrps_row_twins_set_its_emissive_map_aside_by_its_map_keyword()
        {
            var host = Hdrp();
            var map = Map("Glow_Emissive");
            var original = Written(new LitSurface { emission = Color.white, emissionMap = map }, host);
            Assert.That(original.IsKeywordEnabled("_EMISSIVE_COLOR_MAP"), Is.True);
            var glowing = Made(MaterialWriter.Twin(original, TwinState.Glowing, host));
            Assert.That(glowing.GetTexture("_EmissiveColorMap"), Is.Null, "the map set aside");
            Assert.That(glowing.IsKeywordEnabled("_EMISSIVE_COLOR_MAP"), Is.False, "HDRP's keyword samples the map alone");
            var emitting = Made(MaterialWriter.Twin(original, TwinState.Emitting, host));
            Assert.That(emitting.IsKeywordEnabled("_EMISSIVE_COLOR_MAP"), Is.True, "an emission keeps its map");
            var faded = Made(MaterialWriter.Twin(original, TwinState.Blended, host));
            Assert.That(host.Blended, Has.Some.EqualTo((faded, true)), "blended through the host's switch");
            MaterialWriter.SetTwinValues(glowing, original, host, 1f, new Color(0f, 3f, 0f, 1f));
            Assert.That(glowing.GetColor("_EmissiveColor").g, Is.GreaterThan(1f), "the emission colour slot, above 1 kept");
        }

        // -- binding a mask alone ------------------------------------------------------------

        [Test]
        public void Bind_mask_binds_on_the_pipelines_lit_alone()
        {
            var m = Made(new Material(Shader.Find("Standard")));
            m.SetTextureScale("_MainTex", new Vector2(2f, 2f));
            var mask = Map("Ball_ORM_MetallicSmoothness");
            Assert.That(MaterialWriter.BindMask(m, mask, true, RenderPipelineHosts.BuiltIn), Is.True);
            Assert.That(m.GetTexture("_MetallicGlossMap"), Is.SameAs(mask));
            Assert.That(m.GetTexture("_OcclusionMap"), Is.SameAs(mask));
            Assert.That(m.GetTextureScale("_MetallicGlossMap"), Is.EqualTo(new Vector2(2f, 2f)), "the base map's tiling");
            Assert.That(MaterialWriter.BindMask(Blank(), mask, true, RenderPipelineHosts.BuiltIn), Is.False, "not on Standard");
        }
    }
}
