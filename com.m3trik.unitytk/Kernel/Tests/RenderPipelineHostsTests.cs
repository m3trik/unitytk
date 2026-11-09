using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>The render-pipeline host registry and its Built-in default.</summary>
    public class RenderPipelineHostsTests
    {
        /// <summary>A pipeline asset no real pipeline serves.</summary>
        sealed class FakePipelineAsset : RenderPipelineAsset
        {
            protected override RenderPipeline CreatePipeline() => null;
        }

        [Test]
        public void No_pipeline_asset_is_the_built_in_pipeline()
        {
            Assert.That(RenderPipelineHosts.For(null), Is.SameAs(RenderPipelineHosts.BuiltIn));
            Assert.That(RenderPipelineHosts.BuiltIn.Name, Is.EqualTo("Built-in"));
            Assert.That(RenderPipelineHosts.BuiltIn.BaseColorProperty, Is.EqualTo("_Color"));
            Assert.That(RenderPipelineHosts.BuiltIn.BaseMapProperty, Is.EqualTo("_MainTex"));
            Assert.That(Shader.Find(RenderPipelineHosts.BuiltIn.UnlitShader(false)), Is.Not.Null);
            Assert.That(RenderPipelineHosts.BuiltIn.LitShader, Is.EqualTo("Standard"));
            var lit = Shader.Find(RenderPipelineHosts.BuiltIn.LitShader);
            Assert.That(lit, Is.Not.Null);
            Assert.That(RenderPipelineHosts.BuiltIn.Draws(lit), Is.True, "its lit shader is its own");
            var blended = new Material(Shader.Find(RenderPipelineHosts.BuiltIn.UnlitShader(true)));
            try
            {
                Assert.That(blended.HasProperty(RenderPipelineHosts.BuiltIn.BaseMapProperty), Is.True,
                    "the unlit shader holds its map in the base map property");
            }
            finally
            {
                Object.DestroyImmediate(blended);
            }
        }

        /// <summary>A shader of <paramref name="passes"/> (ShaderLab pass blocks) in one subshader tagged for no pipeline.</summary>
        static Shader Untagged(string name, string passes)
            => ShaderUtil.CreateShaderAsset($"Shader \"Hidden/UnitytkTests/{name}\" {{ SubShader {{ {passes} }} }}", false);

        const string Program = "CGPROGRAM\n#pragma vertex vert\n#pragma fragment frag\n" +
                               "float4 vert(float4 v : POSITION) : SV_POSITION { return v; }\n" +
                               "fixed4 frag() : SV_Target { return 1; }\nENDCG";

        static string Pass(string lightMode)
            => lightMode == null ? $"Pass {{ {Program} }}" : $"Pass {{ Tags {{ \"LightMode\" = \"{lightMode}\" }} {Program} }}";

        [Test]
        public void An_untagged_pass_is_found_by_its_light_mode()
        {
            // Unlit/Transparent's one pass states no LightMode, which URP draws as SRPDefaultUnlit;
            // Standard's are Built-in's lit passes (ForwardBase, ForwardAdd, ...), which disqualify it
            // even asked for by their own mode: a scriptable pipeline draws them as an error.
            Assert.That(RenderPipelineHosts.DrawnThroughUntaggedSubshader(Shader.Find("Unlit/Transparent"), "", "SRPDefaultUnlit"), Is.True);
            Assert.That(RenderPipelineHosts.DrawnThroughUntaggedSubshader(Shader.Find("Standard"), "", "SRPDefaultUnlit"), Is.False);
            Assert.That(RenderPipelineHosts.DrawnThroughUntaggedSubshader(Shader.Find("Standard"), "ForwardBase"), Is.False);
            Assert.That(RenderPipelineHosts.DrawnThroughUntaggedSubshader(null, ""), Is.False);
        }

        [Test]
        public void Each_built_in_lighting_mode_disqualifies_a_subshader_in_any_case()
        {
            string[] modes =
            {
                "Always", "ForwardBase", "FORWARDBASE", "ForwardAdd", "Deferred", "PrepassBase", "PrepassFinal",
                "Vertex", "VertexLM", "VertexLMRGBM",
            };
            for (int i = 0; i < modes.Length; i++)
            {
                var shader = Untagged("Mode" + i, Pass(modes[i]) + Pass(null));
                try
                {
                    Assert.That(shader, Is.Not.Null, modes[i]);
                    Assert.That(RenderPipelineHosts.DrawnThroughUntaggedSubshader(shader, "", "SRPDefaultUnlit"), Is.False,
                        $"{modes[i]}: a Built-in lighting pass beside the unlit one");
                    Assert.That(RenderPipelineHosts.DrawnThroughUntaggedSubshader(shader, modes[i]), Is.False,
                        $"{modes[i]}: asked for its own mode too");
                }
                finally
                {
                    Object.DestroyImmediate(shader);
                }
            }
        }

        [Test]
        public void The_stand_in_host_is_set_up_as_the_pipeline_it_plays_and_keeps_what_it_was_asked()
        {
            var host = new PureRenderPipelineHost();
            Assert.That(host.Serves(null), Is.False, "no pipeline unless given one");
            Assert.That(host.UnlitShader(true), Is.EqualTo(host.UnlitShader(false)));
            host.BlendedUnlitShaderName = "Unlit/Transparent";
            Assert.That(host.UnlitShader(true), Is.EqualTo("Unlit/Transparent"));
            Assert.That(host.Draws(Shader.Find("Standard")), Is.False, "URP's tag by default");
            host.DrawsShader = shader => true;
            Assert.That(host.Draws(null), Is.False, "no shader is drawn");
            var material = new Material(Shader.Find("Standard"));
            try
            {
                host.Blends = false;
                Assert.That(host.SetBlended(material, true), Is.False);
                Assert.That(host.Blended, Is.EqualTo(new[] { (material, true) }));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void A_built_in_lighting_pass_disqualifies_a_subshader_a_helper_pass_beside_it_notwithstanding()
        {
            var lit = Untagged("LitWithHelper", Pass("ForwardBase") + Pass(null));
            var unlit = Untagged("UnlitWithUtilities", Pass(null) + Pass("ShadowCaster") + Pass("UnitytkTestsCache"));
            try
            {
                Assert.That(lit, Is.Not.Null);
                Assert.That(RenderPipelineHosts.DrawnThroughUntaggedSubshader(lit, "", "SRPDefaultUnlit"), Is.False,
                    "its colour is a ForwardBase pass, which a scriptable pipeline draws as an error");
                // A shadow caster and a LightMode no camera draws (a command buffer's) neither count nor disqualify.
                Assert.That(RenderPipelineHosts.DrawnThroughUntaggedSubshader(unlit, "", "SRPDefaultUnlit"), Is.True);
                Assert.That(RenderPipelineHosts.DrawnThroughUntaggedSubshader(unlit, "UniversalForward"), Is.False, "no pass of the modes asked");
            }
            finally
            {
                Object.DestroyImmediate(lit);
                Object.DestroyImmediate(unlit);
            }
        }

        [Test]
        public void A_registered_host_serves_its_pipeline_and_one_no_host_serves_has_none()
        {
            var asset = ScriptableObject.CreateInstance<FakePipelineAsset>();
            var host = new PureRenderPipelineHost("Fake") { ServedPipeline = asset };
            var again = new PureRenderPipelineHost("Fake") { ServedPipeline = asset };
            try
            {
                Assert.That(RenderPipelineHosts.For(asset), Is.Null, "a pipeline whose door is absent has no host");
                RenderPipelineHosts.Register(host);
                Assert.That(RenderPipelineHosts.For(asset), Is.SameAs(host));
                RenderPipelineHosts.Register(again);
                Assert.That(RenderPipelineHosts.For(asset), Is.SameAs(again), "a door registering again replaces its host");
                Assert.That(RenderPipelineHosts.Unregister(host), Is.False, "...so the first is no longer registered");
                Assert.That(RenderPipelineHosts.For(null), Is.SameAs(RenderPipelineHosts.BuiltIn));
                Assert.That(RenderPipelineHosts.Unregister(again), Is.True);
                Assert.That(RenderPipelineHosts.For(asset), Is.Null);
            }
            finally
            {
                // Whichever is registered when an assertion fails: never leak a fake into the next test.
                RenderPipelineHosts.Unregister(host);
                RenderPipelineHosts.Unregister(again);
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void The_built_in_blend_switch_is_the_standard_inspectors_fade()
        {
            var material = new Material(Shader.Find("Standard"));
            try
            {
                Assert.That(RenderPipelineHosts.BuiltIn.SetBlended(material, true), Is.True);
                Assert.That(material.GetFloat("_Mode"), Is.EqualTo(2f));
                Assert.That(material.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
                Assert.That(material.IsKeywordEnabled("_ALPHABLEND_ON"), Is.True);
                Assert.That(material.GetTag("RenderType", false), Is.EqualTo("Transparent"));
                Assert.That(RenderPipelineHosts.BuiltIn.SetBlended(material, false), Is.True);
                Assert.That(material.GetFloat("_Mode"), Is.EqualTo(0f));
                Assert.That(material.IsKeywordEnabled("_ALPHABLEND_ON"), Is.False);
                Assert.That(material.renderQueue, Is.EqualTo(material.shader.renderQueue), "back on the shader's own queue");
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
            var unlit = new Material(Shader.Find(RenderPipelineHosts.BuiltIn.UnlitShader(false)));
            try
            {
                Assert.That(RenderPipelineHosts.BuiltIn.SetBlended(unlit, true), Is.False, "an unlit shader has no blend switch");
            }
            finally
            {
                Object.DestroyImmediate(unlit);
            }
        }

        [Test]
        public void A_shader_with_a_mode_but_its_blending_written_in_its_passes_has_no_built_in_blend_switch()
        {
            // A custom shader that exposes _Mode alone, its blend hard-coded: the switch is
            // the four properties the Standard inspector writes, so this one is left as it is.
            var shader = ShaderUtil.CreateShaderAsset(
                "Shader \"Hidden/UnitytkTests/ModeOnly\" { Properties { _Mode (\"Mode\", Float) = 0 } " +
                $"SubShader {{ Blend One Zero {Pass(null)} }} }}", false);
            var material = new Material(shader);
            try
            {
                Assert.That(material.HasProperty("_Mode"), Is.True);
                Assert.That(material.HasProperty("_SrcBlend"), Is.False);
                int queue = material.renderQueue;
                Assert.That(RenderPipelineHosts.BuiltIn.SetBlended(material, true), Is.False,
                    "no blend switch: the caller warns that the shader cannot fade");
                Assert.That(material.GetFloat("_Mode"), Is.EqualTo(0f), "the material is unchanged");
                Assert.That(material.IsKeywordEnabled("_ALPHABLEND_ON"), Is.False);
                Assert.That(material.GetTag("RenderType", false), Is.Not.EqualTo("Transparent"));
                Assert.That(material.renderQueue, Is.EqualTo(queue));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Test]
        public void A_message_names_the_hosts_pipeline_or_the_one_drawing_now()
        {
            Assert.That(RenderPipelineHosts.NameOf(RenderPipelineHosts.BuiltIn), Is.EqualTo("Built-in"));
            Assert.That(RenderPipelineHosts.NameOf(new PureRenderPipelineHost("Fake")), Is.EqualTo("Fake"));
            // No host: the pipeline drawing now, whichever this project draws with.
            RenderPipelineAsset drawing = GraphicsSettings.currentRenderPipeline;
            if (drawing == null)
                Assert.That(RenderPipelineHosts.NameOf(null), Is.EqualTo("active"), "Built-in has no asset to name");
            else
                Assert.That(RenderPipelineHosts.NameOf(null), Does.Contain(drawing.GetType().Name).And.Contain(drawing.name),
                    "a scriptable pipeline by its asset's type and name");
        }

        [Test]
        public void The_built_in_pipeline_draws_its_own_shaders()
        {
            Assert.That(RenderPipelineHosts.BuiltIn.Draws(Shader.Find("Standard")), Is.True);
            Assert.That(RenderPipelineHosts.BuiltIn.Draws(null), Is.False);
        }
    }
}
