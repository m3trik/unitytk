using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace M3trik.Unitytk.Lightmaps.URP.Tests
{
    /// <summary>
    /// Baked Lit, made on import from the project's own URP Lit: Lit renamed, every property and pass Lit's (a
    /// material keeps its values moving onto it), every pass reading Lit's one material buffer (so the SRP Batcher
    /// draws it as Lit), its forward pass rerouted under the rule's keyword; a Lit laid out otherwise refuses,
    /// naming its file and URP's version. URP's door names it, a lightmapped Lit material takes it, and two
    /// deliveries in one scene keep each its own rule: each its probe's scale, each its materials' keyword.
    /// </summary>
    public class BakedLitImporterTests
    {
        static Shader Baked => AssetDatabase.LoadAssetAtPath<Shader>(BakedLitImporter.Stub);

        static Shader Lit => AssetDatabase.LoadAssetAtPath<Shader>(BakedLitImporter.Lit);

        static IEnumerable<string> Properties(Shader shader)
            => Enumerable.Range(0, shader.GetPropertyCount()).Select(i => $"{shader.GetPropertyName(i)}:{shader.GetPropertyType(i)}");

        static IEnumerable<ShaderData.Pass> Passes(Shader shader)
        {
            ShaderData.Subshader subshader = ShaderUtil.GetShaderData(shader).GetSubshader(0);
            return Enumerable.Range(0, subshader.PassCount).Select(subshader.GetPass);
        }

        // The material buffer one stage of a pass reads, compiled with keywords: its size, and the offset of each field
        // the stage reads (a compiler reports those alone).
        static (int size, Dictionary<string, int> offsets) PerMaterial(Shader shader, string pass, ShaderType stage, params string[] keywords)
        {
            ShaderData.Pass found = Passes(shader).Single(p => p.Name == pass);
            ShaderData.VariantCompileInfo compiled = found.CompileVariant(stage, keywords, ShaderCompilerPlatform.D3D, BuildTarget.StandaloneWindows64);
            Assert.That(compiled.Success, Is.True, $"{shader.name}'s {pass} compiles: " + string.Join("; ", compiled.Messages.Select(m => m.message)));
            ShaderData.ConstantBufferInfo[] buffers = compiled.ConstantBuffers.Where(b => b.Name == "UnityPerMaterial").ToArray();
            Assert.That(buffers, Has.Length.EqualTo(1), $"{shader.name}'s {pass} reads its material buffer");
            ShaderData.ConstantBufferInfo buffer = buffers[0];
            return (buffer.Size, buffer.Fields.ToDictionary(f => f.Name, f => f.Index));
        }

        // The SRP Batcher's rule: one layout of the material buffer -- one size, each field at one offset -- wherever it is read.
        static void SameLayout((int size, Dictionary<string, int> offsets) one, (int size, Dictionary<string, int> offsets) other, string what)
        {
            Assert.That(one.size, Is.EqualTo(other.size), what + ": one size");
            foreach (var field in one.offsets.Where(f => other.offsets.ContainsKey(f.Key)))
                Assert.That(other.offsets[field.Key], Is.EqualTo(field.Value), $"{what}: {field.Key} at one offset");
        }

        [Test]
        public void It_is_made_of_the_installed_Lit_its_properties_and_passes_Lits()
        {
            Shader baked = Baked;
            Assert.That(baked, Is.Not.Null, "made on import");
            Assert.That(baked.name, Is.EqualTo(BakedLitImporter.ShaderName));
            Assert.That(Shader.Find(BakedLitImporter.ShaderName), Is.SameAs(baked));
            Assert.That(ShaderUtil.ShaderHasError(baked), Is.False);
            Assert.That(Properties(baked), Is.EqualTo(Properties(Lit)), "every property Lit's: a material keeps its values");
            Assert.That(Passes(baked).Select(p => p.Name), Is.EqualTo(Passes(Lit).Select(p => p.Name)));
            Assert.That(new LocalKeyword(baked, LightmapsImporter.ReflectByBakeKeyword).isValid, Is.True, "the rule's keyword");
        }

        [Test]
        public void Every_pass_reads_Lits_one_material_buffer_so_the_SRP_Batcher_draws_it()
        {
            var forward = PerMaterial(Baked, "ForwardLit", ShaderType.Fragment, "LIGHTMAP_ON", LightmapsImporter.ReflectByBakeKeyword);
            SameLayout(forward, PerMaterial(Lit, "ForwardLit", ShaderType.Fragment, "LIGHTMAP_ON"), "Lit's own: the rule adds no property of the material's");
            SameLayout(forward, PerMaterial(Baked, "ShadowCaster", ShaderType.Fragment, "_ALPHATEST_ON"), "its shadow caster: one layout in every pass");
        }

        [Test]
        public void A_Lit_laid_out_otherwise_is_refused_naming_its_file_and_URPs_version()
        {
            string lit = File.ReadAllText(Path.GetFullPath(BakedLitImporter.Lit));
            string made = BakedLitImporter.Derive(lit, out string missing);
            Assert.That(missing, Is.Null);
            int pass = made.IndexOf("Name \"ForwardLit\"", System.StringComparison.Ordinal);
            int reroute = made.IndexOf(BakedLitImporter.Reroute, System.StringComparison.Ordinal);
            Assert.That(reroute, Is.GreaterThan(pass), "the reroute in the forward pass");
            Assert.That(reroute, Is.LessThan(made.IndexOf("LitForwardPass.hlsl", pass, System.StringComparison.Ordinal)), "...before its lighting");
            Assert.That(made.Replace("\r\n", "\n").Split('\n').Count(l => l.Contains(LightmapsImporter.ReflectByBakeKeyword)), Is.EqualTo(1));

            Assert.That(BakedLitImporter.Derive("Shader \"Other\" { }", out missing), Is.Null);
            Assert.That(missing, Does.Contain("Universal Render Pipeline/Lit"));
            Assert.That(BakedLitImporter.Derive(lit.Replace("LitForwardPass.hlsl", "Elsewhere.hlsl"), out missing), Is.Null);
            Assert.That(missing, Does.Contain("LitForwardPass.hlsl"));
            string version = PackageInfo.FindForAssetPath(BakedLitImporter.Lit).version;
            Assert.That(BakedLitImporter.Refusal(missing, version), Does.Contain(BakedLitImporter.Lit).And.Contain("URP " + version));
        }

        [Test]
        public void URPs_door_names_it_and_a_lightmapped_Lit_material_takes_it_with_its_values()
        {
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            Assume.That(host != null && host.Name == "URP", "this project draws with URP");
            Assert.That(LightmapsImporter.BakedLit(GraphicsSettings.currentRenderPipeline), Is.EqualTo(BakedLitImporter.Stub));
            Assert.That(LightmapsImporter.BakedLit(null), Is.Null, "no pipeline (Built-in): none");
            var lit = new Material(Shader.Find(host.LitShader));
            var other = new Material(Shader.Find(host.LitShader));
            var node = new GameObject("Room");
            try
            {
                lit.SetColor("_BaseColor", new Color(0.2f, 0.4f, 0.6f, 1f));
                var set = node.AddComponent<LightmapSet>();
                var renderer = node.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = lit;
                set.receivers.Add(new LightmapSet.Receiver { renderer = renderer });
                Assert.That(LightmapsImporter.Baked(set, host, Baked, true), Is.EqualTo(1));
                Assert.That(lit.shader, Is.SameAs(Baked));
                Assert.That(Vector4.Distance(lit.GetColor("_BaseColor"), new Color(0.2f, 0.4f, 0.6f, 1f)), Is.LessThan(1e-4f),
                            "its values kept: its properties are Lit's");
                Assert.That(lit.IsKeywordEnabled(new LocalKeyword(Baked, LightmapsImporter.ReflectByBakeKeyword)), Is.True);
                Assert.That(RenderPipelineHosts.SwapLit(RenderPipelineHosts.BuiltIn, other, Baked), Is.False, "the Built-in pipeline does not draw it");
            }
            finally
            {
                Object.DestroyImmediate(node);
                Object.DestroyImmediate(lit);
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void Two_deliveries_in_one_scene_keep_each_its_own_rule()
        {
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            Assume.That(host != null && host.Name == "URP", "this project draws with URP");
            var made = new List<Object>();
            // A delivery: a renderer on a Lit material, its set and its own probe.
            (LightmapSet set, Material material) Delivery(string name)
            {
                var material = new Material(Shader.Find(host.LitShader)) { name = name };
                var node = new GameObject(name);
                node.SetActive(false);
                var renderer = node.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                var set = node.AddComponent<LightmapSet>();
                set.maps.Add(new LightmapSet.Map { color = new Texture2D(4, 4) });
                set.receivers.Add(new LightmapSet.Receiver { renderer = renderer });
                set.probe = new GameObject(name + " Probe").AddComponent<ReflectionProbe>();
                set.probe.transform.SetParent(node.transform, false);
                made.Add(material);
                made.Add(node);
                made.Add(set.maps[0].color);
                return (set, material);
            }
            try
            {
                var (one, first) = Delivery("One");
                var (two, second) = Delivery("Two");
                Assert.That(LightmapsImporter.Reflect(one, true, 0.25f, "One.fbx", host, BakedLitImporter.Stub, _ => { }), Is.EqualTo(1));
                Assert.That(LightmapsImporter.Reflect(two, false, 2f, "Two.fbx", host, BakedLitImporter.Stub, _ => { }), Is.EqualTo(1));
                one.gameObject.SetActive(true);
                two.gameObject.SetActive(true);
                Assert.That((first.shader, second.shader), Is.EqualTo((Baked, Baked)));
                Assert.That((first.IsKeywordEnabled(LightmapsImporter.ReflectByBakeKeyword), second.IsKeywordEnabled(LightmapsImporter.ReflectByBakeKeyword)),
                            Is.EqualTo((true, false)), "both bound in one scene: each its own");
                Assert.That((one.probe.intensity, two.probe.intensity), Is.EqualTo((0.25f, 2f)), "each its own probe's scale");
                one.enabled = false;
                Assert.That((second.IsKeywordEnabled(LightmapsImporter.ReflectByBakeKeyword), two.probe.intensity), Is.EqualTo((false, 2f)),
                            "a set releasing touches neither the other's materials nor its probe");
            }
            finally
            {
                foreach (Object o in made) Object.DestroyImmediate(o);
            }
        }
    }
}
