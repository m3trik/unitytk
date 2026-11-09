using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace M3trik.Unitytk.Kernel.URP.Tests
{
    /// <summary>
    /// URP's host as a feature asks it, in a project with URP installed (whichever
    /// pipeline it draws with): which pipeline asset it serves, which shaders it
    /// draws, the names of its base properties and its blend switch, on URP's Lit
    /// as on its Unlit.
    /// </summary>
    public class UniversalRenderPipelineHostTests
    {
        readonly IRenderPipelineHost _host = new UniversalRenderPipelineHost();

        [Test]
        public void It_draws_its_own_shaders_and_a_legacy_unlit_pass_but_not_built_ins_lit_ones()
        {
            Assert.That(_host.Draws(Shader.Find(_host.UnlitShader(true))), Is.True, "tagged UniversalPipeline");
            // Untagged, its one pass states no LightMode: URP draws it as SRPDefaultUnlit.
            Assert.That(_host.Draws(Shader.Find("Unlit/Transparent")), Is.True);
            // Untagged, its passes are Built-in's (ForwardBase...): URP draws it as an error.
            Assert.That(_host.Draws(Shader.Find("Standard")), Is.False);
            Assert.That(_host.Draws(null), Is.False);
        }

        [Test]
        public void It_draws_the_shadow_planes_shader_through_its_unlit_pass_beside_the_cache_pass()
        {
            // Legacy CG with no LightMode on its drawing pass, and a pass of its own LightMode
            // that a command buffer draws (ShadowPlaneCache): URP draws it as SRPDefaultUnlit.
            Shader shader = Shader.Find("m3trik/ShadowPlane");
            Assume.That(shader, Is.Not.Null, "the ShadowPlane feature's shader imported");
            Assert.That(_host.Draws(shader), Is.True);
        }

        [Test]
        public void It_does_not_draw_a_built_in_lit_shader_for_a_helper_pass_beside_it()
        {
            const string program = "CGPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n" +
                                   "float4 vert(float4 v : POSITION) : SV_POSITION { return v; }\n" +
                                   "fixed4 frag() : SV_Target { return 1; }\nENDCG";
            var shader = UnityEditor.ShaderUtil.CreateShaderAsset(
                "Shader \"Hidden/UnitytkTests/LitWithHelper\" { SubShader { " +
                $"Pass {{ Tags {{ \"LightMode\" = \"ForwardBase\" }} {program} }} Pass {{ {program} }} }} }}", false);
            try
            {
                Assert.That(_host.Draws(shader), Is.False, "its colour is a ForwardBase pass: URP's error shader");
            }
            finally
            {
                Object.DestroyImmediate(shader);
            }
        }

        [Test]
        public void It_serves_a_urp_asset_alone()
        {
            var urp = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();
            try
            {
                Assert.That(_host.Serves(urp), Is.True);
                Assert.That(_host.Serves(null), Is.False, "no asset is the Built-in pipeline's");
                Assert.That(RenderPipelineHosts.For(urp), Is.InstanceOf<UniversalRenderPipelineHost>(), "the door registered it");
            }
            finally
            {
                Object.DestroyImmediate(urp);
            }
        }

        [Test]
        public void Its_lit_shader_blends_and_goes_back_to_opaque_as_urps_inspector_sets_it()
        {
            Assert.That(_host.LitShader, Is.EqualTo("Universal Render Pipeline/Lit"));
            var lit = Shader.Find(_host.LitShader);
            Assert.That(lit, Is.Not.Null, "URP's lit shader, by the name the host gives");
            Assert.That(_host.Draws(lit), Is.True);
            Assert.That(lit.FindPropertyIndex(_host.BaseColorProperty), Is.GreaterThanOrEqualTo(0));
            Assert.That(lit.FindPropertyIndex(_host.BaseMapProperty), Is.GreaterThanOrEqualTo(0));
            var material = new Material(lit);
            try
            {
                Assert.That(_host.SetBlended(material, true), Is.True);
                Assert.That(material.GetFloat("_Surface"), Is.EqualTo(1f), "Transparent");
                Assert.That(material.GetFloat("_Blend"), Is.EqualTo(0f), "Alpha");
                Assert.That(material.GetFloat("_BlendModePreserveSpecular"), Is.EqualTo(0f), "specular fades with the rest");
                Assert.That(material.GetFloat("_SrcBlend"), Is.EqualTo((float)BlendMode.SrcAlpha));
                Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
                Assert.That(material.GetFloat("_SrcBlendAlpha"), Is.EqualTo((float)BlendMode.One));
                Assert.That(material.GetFloat("_DstBlendAlpha"), Is.EqualTo((float)BlendMode.OneMinusSrcAlpha));
                Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(0f));
                Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
                Assert.That(material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"), Is.False);
                Assert.That(material.IsKeywordEnabled("_ALPHAMODULATE_ON"), Is.False);
                Assert.That(material.GetTag("RenderType", false), Is.EqualTo("Transparent"));
                Assert.That(material.GetShaderPassEnabled("DepthOnly"), Is.False, "a blended surface writes no depth prepass");
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent + (int)material.GetFloat("_QueueOffset")));

                Assert.That(_host.SetBlended(material, false), Is.True);
                Assert.That(material.GetFloat("_Surface"), Is.EqualTo(0f));
                Assert.That(material.GetFloat("_DstBlend"), Is.EqualTo((float)BlendMode.Zero));
                Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(1f));
                Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.False);
                Assert.That(material.GetTag("RenderType", false), Is.EqualTo("Opaque"));
                Assert.That(material.GetShaderPassEnabled("DepthOnly"), Is.True);
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Geometry));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void A_shader_with_no_surface_switch_is_left_alone()
        {
            var material = new Material(Shader.Find("Standard"));
            try
            {
                int queue = material.renderQueue;
                Assert.That(_host.SetBlended(material, true), Is.False);
                Assert.That(material.renderQueue, Is.EqualTo(queue), "unchanged");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void Its_unlit_shader_holds_its_base_map_and_colour_and_blends()
        {
            var material = new Material(Shader.Find(_host.UnlitShader(true)));
            try
            {
                Assert.That(_host.BaseMapProperty, Is.EqualTo("_BaseMap"));
                Assert.That(material.HasProperty(_host.BaseMapProperty), Is.True);
                Assert.That(material.HasProperty(_host.BaseColorProperty), Is.True);
                Assert.That(_host.SetBlended(material, true), Is.True);
                Assert.That(material.GetFloat("_Surface"), Is.EqualTo(1f));
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }
    }
}
