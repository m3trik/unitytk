using System.Collections.Generic;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.Indicators.Tests
{
    /// <summary>The material a part this feature draws wears: a new one on the pipeline's Lit, coloured and glowing as asked.</summary>
    public class PlainMaterialTests
    {
        readonly List<Object> _made = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object made in _made) Object.DestroyImmediate(made);
            _made.Clear();
        }

        T Made<T>(T made) where T : Object
        {
            _made.Add(made);
            return made;
        }

        [Test]
        public void A_plain_material_is_a_new_one_on_the_pipelines_Lit_coloured_and_glowing_as_asked()
        {
            // It was read off a primitive made for it (in a player too, where a primitive's is not the pipeline's).
            IRenderPipelineHost host = RenderPipelineHosts.Current ?? RenderPipelineHosts.BuiltIn;
            Material plain = Made(PlainMaterial.Of("Ring", new Color(1f, 0.5f, 0f, 1f), 2f, host: host));
            Assert.That(plain.shader, Is.SameAs(Shader.Find(host.LitShader)), "the pipeline's own Lit, by its host's name");
            Assert.That(plain.name, Is.EqualTo("Ring"));
            Assert.That(plain.GetColor(host.Lit.BaseColor), Is.EqualTo(new Color(1f, 0.5f, 0f, 1f)));
            Assert.That(plain.GetColor(host.Lit.EmissionColor), Is.EqualTo(new Color(2f, 1f, 0f, 2f)), "glowing twice its colour");
            Assert.That(plain.IsKeywordEnabled(host.Lit.EmissionKeyword), Is.True);
            Assert.That(plain.globalIlluminationFlags, Is.EqualTo(MaterialGlobalIlluminationFlags.None), "none of it baked");

            Material dull = Made(PlainMaterial.Of("Dull", Color.grey, 0f, host));
            Assert.That(dull, Is.Not.SameAs(plain), "each its own");
            Assert.That(dull.IsKeywordEnabled(host.Lit.EmissionKeyword), Is.False, "no glow: its emission as the shader has it");
            PlainMaterial.Paint(null, Color.red);  // nothing to paint: no throw
        }

        // Where the pipeline's Lit is not in the project or build, the arrow and the ring were made with no material
        // (drawn magenta, or not at all) and nothing said why.
        [Test]
        public void A_plain_material_with_no_shader_to_be_on_is_none_and_said_once()
        {
            var host = new PureRenderPipelineHost("Lit-less") { LitShader = "Hidden/UnitytkTests/No Such Lit" };
            LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("the Lit-less pipeline's Lit ('Hidden/UnitytkTests/No Such Lit')") +
                                                        ".*" + Regex.Escape("'Ring (no shader)'")));
            Assert.That(PlainMaterial.Of("Ring (no shader)", Color.white, 0f, host), Is.Null);
            Assert.That(PlainMaterial.Of("Ring (no shader)", Color.white, 0f, host), Is.Null, "made again: none again");
            LogAssert.NoUnexpectedReceived();  // said once
        }
    }
}
