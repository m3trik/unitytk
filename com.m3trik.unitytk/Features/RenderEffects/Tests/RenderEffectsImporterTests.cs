using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// Real imports of a two-take delivery (<see cref="EffectsFbx"/>): each node the
    /// render_effects record names gets its components, each material entry its track;
    /// in each clip every proxy curve is rebound onto its component's own field (a
    /// visibility onto the node's own active state, stepped) and the proxies, their
    /// curves and Unity's own renderer-enabled curves under such a node are gone; one
    /// twin per material and state is made into the model; the curves import as keyed;
    /// the instances the runtime wears are the model's templates'; a v1 record is refused
    /// with its fix and nothing is added; a node Unity folds into the model's root is said,
    /// its proxies deleted; a namespaced delivery joins each target to its curve by its leaf;
    /// and a file without a record keeps Unity's import.
    /// </summary>
    public class RenderEffectsImporterTests
    {
        const string Folder = "Assets/UnitytkRenderEffectsTests";
        const string Model = Folder + "/effects.fbx";

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Folder);

        static GameObject Import(string record = EffectsFbx.Record, string ns = "") => ImportFile(EffectsFbx.Write(record, ns));

        static GameObject ImportFile(string fbx)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            File.WriteAllText(Model, fbx, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        }

        static AnimationClip Clip(string name)
            => AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Single(c => c.name == name);

        static AnimationCurve Curve(AnimationClip clip, string path, System.Type type, string property)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, type, property));
            Assert.That(curve, Is.Not.Null, $"{clip.name}: {path} {type.Name}.{property}");
            return curve;
        }

        [Test]
        public void Each_clip_carries_each_proxy_curve_on_its_components_own_field_and_the_proxies_are_gone()
        {
            GameObject model = Import();
            Assert.That(model.transform.Find("box").GetComponent<Fade>(), Is.Not.Null);
            Assert.That(model.transform.Find("twoslot").GetComponent<Fade>(), Is.Not.Null);
            var highlight = model.transform.Find("glow").GetComponent<Highlight>();
            Stage.AssertRgb(highlight.color, MaterialColor.Encode(new Color(1f, 0.5f, 0f)), "the record's colour, linear, as the field stores it");
            var track = model.transform.Find("data_export/hullMat_signal").GetComponent<EmissionTrack>();
            Assert.That(track.material.name, Is.EqualTo("hullMat_signal"));
            Assert.That(track.strength, Is.EqualTo(2f));
            Stage.AssertRgb(track.color, MaterialColor.Encode(new Color(1f, 0.5f, 0.25f)), "the record's colour");
            Assert.That(model.transform.Find("crowd").GetComponent<RenderEffect>(), Is.Null, "a visibility is the node's own state");
            Assert.That(model.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("__")), Is.Empty, "no proxy reaches the prefab");

            AnimationClip a = Clip("TakeA"), b = Clip("TakeB");
            Assert.That(Curve(a, "box", typeof(Fade), "alpha").Evaluate(0f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Curve(a, "box", typeof(Fade), "alpha").Evaluate(1f), Is.EqualTo(0f).Within(1e-4f), "TakeA fades it out");
            Assert.That(Curve(b, "box", typeof(Fade), "alpha").Evaluate(0f), Is.EqualTo(0f).Within(1e-4f), "from its own clip's start");
            Assert.That(Curve(b, "box", typeof(Fade), "alpha").Evaluate(1f), Is.EqualTo(1f).Within(1e-4f), "TakeB fades it in");
            Assert.That(Curve(a, "glow", typeof(Highlight), "intensity").Evaluate(1f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(Curve(a, "twoslot", typeof(Fade), "alpha").Evaluate(1f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(Curve(a, "data_export/hullMat_signal", typeof(EmissionTrack), "weight").Evaluate(1f), Is.EqualTo(1f).Within(1e-4f));
            AnimationCurve shown = Curve(a, "crowd", typeof(GameObject), "m_IsActive");
            Assert.That(shown.Evaluate(0.49f), Is.EqualTo(1f), "on until its step");
            Assert.That(shown.Evaluate(0.51f), Is.EqualTo(0f), "off from it, stepped, never ramped");
            Assert.That(Curve(b, "crowd", typeof(GameObject), "m_IsActive").Evaluate(0.51f), Is.EqualTo(1f));

            foreach (AnimationClip clip in new[] { a, b })
            {
                var bindings = AnimationUtility.GetCurveBindings(clip);
                Assert.That(bindings.Where(x => x.path.Contains("__")), Is.Empty, $"{clip.name}: no proxy curve left");
                Assert.That(bindings.Where(x => x.propertyName == "m_Enabled" && x.path.StartsWith("crowd")), Is.Empty,
                            $"{clip.name}: the node's active state owns its presence, not one renderer curve per child");
            }
        }

        [Test]
        public void The_import_makes_one_twin_per_material_and_state_into_the_model()
        {
            GameObject model = Import();
            var effects = model.GetComponentsInChildren<RenderEffect>(true);
            MaterialTwins table = effects[0].twins;
            Assert.That(table, Is.Not.Null);
            Assert.That(effects.Select(e => e.twins).Distinct(), Is.EqualTo(new[] { table }), "one table for the model's effects");
            Assert.That(AssetDatabase.GetAssetPath(table), Is.EqualTo(Model), "the model's own, rebuilt with it");
            var made = table.twins.Select(t => (t.original.name, t.state)).ToArray();
            Assert.That(made, Is.EquivalentTo(new[]
            {
                ("boxMat", TwinState.Blended), ("slotA", TwinState.Blended), ("slotB", TwinState.Blended),
                ("glowMat", TwinState.Glowing), ("hullMat_signal", TwinState.Emitting),
            }), "per material, each state its effects can ask for");
            foreach (var twin in table.twins)
                Assert.That(AssetDatabase.GetAssetPath(twin.template), Is.EqualTo(Model), $"{twin.template.name}: a sub-asset");
            var kid = model.transform.Find("crowd/kidA").GetComponent<Renderer>();
            Assert.That(table.twins.Select(t => t.original), Has.None.SameAs(kid.sharedMaterial), "nothing for a renderer no effect covers");
        }

        [Test]
        public void An_instance_the_runtime_wears_is_its_templates_and_the_model_rests_on_its_originals()
        {
            GameObject model = Import();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                var box = instance.transform.Find("box");
                var renderer = box.GetComponent<Renderer>();
                Material original = renderer.sharedMaterial;
                var fade = box.GetComponent<Fade>();
                fade.alpha = 0.5f;
                fade.Apply();
                Material template = fade.twins.Template(original, TwinState.Blended);
                Assert.That(renderer.sharedMaterial, Is.Not.SameAs(template), "instanced, never worn itself");
                Assert.That(renderer.sharedMaterial.shaderKeywords, Is.EquivalentTo(template.shaderKeywords), "the template's variant");
                fade.alpha = 1f;
                fade.Apply();
                Assert.That(renderer.sharedMaterial, Is.SameAs(original));
                fade.Release();
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Over_the_imported_model_each_effect_shows_on_its_own_renderers_and_rests_on_the_originals()
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(Import());
            var made = new System.Collections.Generic.List<Object> { instance };
            try
            {
                Renderer Part(string path) => instance.transform.Find(path).GetComponent<Renderer>();
                Material[] slots = Part("twoslot").sharedMaterials;
                var twoslot = instance.transform.Find("twoslot").GetComponent<Fade>();
                twoslot.alpha = 0.5f;
                twoslot.Apply();
                Material[] faded = Part("twoslot").sharedMaterials;
                for (int i = 0; i < 2; i++)
                {
                    Assert.That(faded[i].name, Is.EqualTo(slots[i].name + " (Blended)"), $"slot {i}: its own material's twin");
                    Stage.AssertRgb(faded[i].color, slots[i].color, $"slot {i} keeps its own colour");
                    Assert.That(faded[i].color.a, Is.EqualTo(slots[i].color.a * 0.5f).Within(1e-4f));
                }
                twoslot.alpha = 0f;
                twoslot.Apply();
                Assert.That(Part("twoslot").forceRenderingOff, Is.True, "hidden at 0");
                twoslot.alpha = 1f;
                twoslot.Apply();
                Assert.That(Part("twoslot").sharedMaterials, Is.EqualTo(slots), "the shared originals at rest");
                Assert.That(Part("twoslot").forceRenderingOff, Is.False);

                Material glowMat = Part("glow").sharedMaterial;
                var glow = instance.transform.Find("glow").GetComponent<Highlight>();
                glow.intensity = 1f;
                glow.Apply();
                Assert.That(Part("glow").sharedMaterial.name, Is.EqualTo(glowMat.name + " (Glowing)"));
                glow.intensity = 0f;
                glow.Apply();
                Assert.That(Part("glow").sharedMaterial, Is.SameAs(glowMat));

                Material signal = Part("hull").sharedMaterials[0], hullMat = Part("hull").sharedMaterials[1];
                var track = instance.transform.Find("data_export/hullMat_signal").GetComponent<EmissionTrack>();
                track.weight = 0.5f;
                track.Apply();
                Material lit = Part("hull").sharedMaterials[0];
                Assert.That(lit.name, Is.EqualTo(signal.name + " (Emitting)"));
                var host = RenderPipelineHosts.Current;
                Stage.AssertRgb(MaterialColor.Decode(lit.GetColor(host.Lit.EmissionColor)), new Color(1f, 0.5f, 0.25f),
                                "the record's colour x strength 2 x weight 0.5");
                Assert.That(Part("hull").sharedMaterials[1], Is.SameAs(hullMat), "the other slot keeps its material");

                // A Fade a person adds over the delivered group: its two renderers share one twin.
                var crowd = instance.transform.Find("crowd").gameObject.AddComponent<Fade>();
                crowd.alpha = 0.5f;
                crowd.Apply();
                Assert.That(Part("crowd/kidA").sharedMaterial, Is.SameAs(Part("crowd/kidB").sharedMaterial),
                            "one twin per material, shared by every renderer the root covers");
                Assert.That(crowd.writer.InstanceCount, Is.EqualTo(1));
                foreach (RenderEffect effect in instance.GetComponentsInChildren<RenderEffect>(true)) effect.Release();
                Assert.That(Part("crowd/kidA").HasPropertyBlock(), Is.False);
            }
            finally
            {
                foreach (Object o in made) Object.DestroyImmediate(o);
            }
        }

        [Test]
        public void The_importer_writes_after_every_material_writer()
            => Assert.That(new RenderEffectsImporter().GetPostprocessOrder(), Is.EqualTo(Core.ImportOrder.AfterMaterialDescriptions));

        [Test]
        public void A_delivery_carrying_the_record_imports_its_curves_as_keyed()
        {
            Import();
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            Assert.That(importer.resampleCurves, Is.False, "asked of the carrier: a resampled step is a one-frame ramp");
            AnimationCurve shown = Curve(Clip("TakeA"), "crowd", typeof(GameObject), "m_IsActive");
            Assert.That(shown.keys.All(k => float.IsInfinity(k.outTangent)), Is.True, "every key a step");
        }

        [Test]
        public void A_v1_record_is_refused_with_its_fix_and_nothing_is_added()
        {
            LogAssert.Expect(LogType.Warning, new Regex("render_effects record v1; unitytk reads v2 only.*re-export"));
            GameObject model = Import("{\"version\": 1, \"tracks\": [{\"node\": \"box\", \"channels\": [\"opacity\"]}]}");
            Assert.That(model.GetComponentsInChildren<RenderEffect>(true), Is.Empty);
        }

        [Test]
        public void A_node_unity_folds_into_the_models_root_is_said_and_leaves_no_proxy()
        {
            // Measured: a file whose one top-level node holds the carrier imports with that node as the root.
            LogAssert.Expect(LogType.Warning, new Regex(
                "names the node 'lamp', the file's one top-level node, which Unity's import folds into the model's root.*" +
                "opacity, visibility are left out and their curve proxies deleted"));
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            File.WriteAllText(Model, EffectsFbx.WriteSingle(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);

            Assert.That(model.GetComponent<MeshRenderer>(), Is.Not.Null, "the node is the root, named after the file");
            Assert.That(model.GetComponentsInChildren<RenderEffect>(true), Is.Empty, "no effect on the model's root");
            Assert.That(model.GetComponentsInChildren<Transform>(true).Where(t => t.name.Contains("__")), Is.Empty,
                        "no proxy left behind");
            var bindings = AnimationUtility.GetCurveBindings(Clip("TakeA"));
            Assert.That(bindings.Where(b => b.path.Contains("__")), Is.Empty, "no proxy curve left");
            Assert.That(bindings.Where(b => b.path.Length == 0 && b.propertyName == "m_Enabled"), Is.Not.Empty,
                        "Unity's own curve of the node's Visibility kept");
        }

        [Test]
        public void A_namespaced_delivery_joins_each_target_to_its_curve_by_its_leaf()
        {
            // A namespaced asset's delivery: its nodes and materials named with the namespace, the
            // record naming them so, each proxy by its target's leaf (the transport's name).
            GameObject model = Import(EffectsFbx.NamespacedRecord, "ns:");
            Transform box = model.GetComponentsInChildren<Transform>(true).Single(t => M3trik.Unitytk.Core.RecordNames.Leaf(t.name) == "box");
            Assert.That(box.GetComponent<Fade>(), Is.Not.Null, $"the node's track, on '{box.name}'");
            Transform holder = model.transform.Find("data_export/hullMat_signal");
            Assert.That(holder, Is.Not.Null, "the material's track, named by its proxy's leaf");
            Assert.That(holder.GetComponent<EmissionTrack>().material.name, Is.EqualTo("ns_hullMat_signal"),
                        "on the material Unity's import names 'ns:hullMat_signal' (measured)");

            AnimationClip a = Clip("TakeA");
            string boxPath = AnimationUtility.CalculateTransformPath(box, model.transform);
            Assert.That(Curve(a, boxPath, typeof(Fade), "alpha").Evaluate(1f), Is.EqualTo(0f).Within(1e-4f), "the box's proxy curve");
            Assert.That(Curve(a, "data_export/hullMat_signal", typeof(EmissionTrack), "weight").Evaluate(1f),
                        Is.EqualTo(1f).Within(1e-4f), "the material's proxy curve");
            Assert.That(model.GetComponentsInChildren<Transform>(true).Select(t => t.name),
                        Has.None.EqualTo("box__opacity").And.None.EqualTo("hullMat_signal__emission"), "neither proxy left");
        }

        [Test]
        public void Two_tracks_whose_nodes_share_a_leaf_are_a_tie_left_out_and_said()
        {
            // Two namespaces' 'box' join the one node 'box' by its leaf: neither is a guess to keep.
            const string tied =
                "{\"version\": 2, \"tracks\": [" +
                "{\"node\": \"a:box\", \"channels\": [\"opacity\"]}, " +
                "{\"node\": \"b:box\", \"channels\": [\"highlight\"]}, " +
                "{\"node\": \"glow\", \"channels\": [\"highlight\"]}], \"materials\": []}";
            LogAssert.Expect(LogType.Warning, new Regex(
                "render_effects record names several nodes 'box' after their namespaces \\(a:box, b:box\\).*left out"));
            GameObject model = Import(tied);
            Transform box = model.transform.Find("box");
            Assert.That(box.GetComponent<Fade>(), Is.Null, "the first track is not kept");
            Assert.That(box.GetComponent<Highlight>(), Is.Null, "nor the last");
            Assert.That(model.transform.Find("glow").GetComponent<Highlight>(), Is.Not.Null, "an untied track still joins");
        }

        [Test]
        public void A_track_goes_on_its_proxys_parent_alone_not_every_node_sharing_its_leaf()
        {
            // a:box carries the proxy box__opacity; b:box shares the leaf and carries none.
            const string record =
                "{\"version\": 2, \"tracks\": [{\"node\": \"a:box\", \"channels\": [\"opacity\"]}], \"materials\": []}";
            GameObject model = ImportFile(EffectsFbx.WriteSharedLeaves(record));
            Transform a = model.transform.Find("a:box"), b = model.transform.Find("b:box");
            Assert.That(a.GetComponent<Fade>(), Is.Not.Null, "the node the curve was written under");
            Assert.That(b.GetComponent<Fade>(), Is.Null, "a namesake with no proxy gets no inert fade");

            AnimationClip take = Clip("TakeA");
            Assert.That(Curve(take, "a:box", typeof(Fade), "alpha").Evaluate(1f), Is.EqualTo(0f).Within(1e-4f));
            var enabled = AnimationUtility.GetCurveBindings(take)
                                          .Where(c => c.path == "b:box" && c.propertyName == "m_Enabled").ToList();
            Assert.That(enabled, Is.Not.Empty, "the namesake keeps Unity's own renderer-enabled curve");
        }

        [Test]
        public void A_track_with_no_proxy_joins_its_exact_name_else_a_unique_leaf_and_a_tie_is_said()
        {
            const string exact =
                "{\"version\": 2, \"tracks\": [{\"node\": \"c:lid\", \"channels\": [\"highlight\"]}], \"materials\": []}";
            GameObject model = ImportFile(EffectsFbx.WriteSharedLeaves(exact));
            Assert.That(model.transform.Find("c:lid").GetComponent<Highlight>(), Is.Not.Null, "the exact name");
            Assert.That(model.transform.Find("d:lid").GetComponent<Highlight>(), Is.Null, "not its namesake");

            const string tied =
                "{\"version\": 2, \"tracks\": [{\"node\": \"lid\", \"channels\": [\"highlight\"]}], \"materials\": []}";
            LogAssert.Expect(LogType.Warning, new Regex(
                "render_effects record's node 'lid' \\(no curve proxy says which\\) matches several objects equally " +
                "\\(c:lid, d:lid\\): its effects are left out"));
            model = ImportFile(EffectsFbx.WriteSharedLeaves(tied));
            Assert.That(model.transform.Find("c:lid").GetComponent<Highlight>(), Is.Null, "never guessed");
            Assert.That(model.transform.Find("d:lid").GetComponent<Highlight>(), Is.Null);
        }

        [Test]
        public void A_file_without_the_record_keeps_unitys_import()
        {
            GameObject model = Import(null);
            Assert.That(model.GetComponentsInChildren<RenderEffect>(true), Is.Empty);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            Assert.That(importer.resampleCurves, Is.True, "nothing asked of a file that carries no effects");
            Assert.That(model.transform.Find("box/box__opacity"), Is.Not.Null, "no record names it: not read as a proxy");
        }
    }
}
