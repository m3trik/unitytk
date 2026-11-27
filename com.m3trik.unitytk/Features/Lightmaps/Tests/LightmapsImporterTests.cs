using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.Lightmaps.Tests
{
    /// <summary>
    /// Real imports of a lightmapped delivery (<see cref="LightmapsFbx"/>), its maps and
    /// probe embedded in it: the model's root gets a set of references -- each object's
    /// renderer (same-named objects told apart by hierarchy) with its map, imported as a
    /// lightmap, and its rect -- which binds once placed; the probe is a native custom
    /// probe at the capture point with the record's box; a namespaced delivery joins by
    /// leaf; a tie stays unbound and is said; a v2 record is refused with its fix.
    /// </summary>
    public class LightmapsImporterTests
    {
        // A stand-in pipeline: its Lit one shader, its shader for a baked surface another, both drawn.
        static PureRenderPipelineHost Pipeline() => new PureRenderPipelineHost { LitShader = "Hidden/Internal-Colored", DrawsShader = _ => true };

        const string BakedLit = Folder + "/Baked.shader";

        // A pipeline's shader for a baked surface, at its asset path, declaring the rule's keyword as Baked Lit does.
        static Shader BakedShader
        {
            get
            {
                if (AssetDatabase.LoadAssetAtPath<Shader>(BakedLit) is Shader made && made != null) return made;
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
                File.WriteAllText(BakedLit, "Shader \"Hidden/UnitytkLightmapsTestBaked\" { SubShader { Pass {\nHLSLPROGRAM\n" +
                                            "#pragma vertex Vert\n#pragma fragment Frag\n" +
                                            "#pragma shader_feature_local_fragment " + LightmapsImporter.ReflectByBakeKeyword + "\n" +
                                            "float4 Vert(float4 p : POSITION) : SV_POSITION { return p; }\n" +
                                            "float4 Frag() : SV_Target { return 0; }\nENDHLSL\n} } }\n");
                AssetDatabase.ImportAsset(BakedLit, ImportAssetOptions.ForceSynchronousImport);
                return AssetDatabase.LoadAssetAtPath<Shader>(BakedLit);
            }
        }

        // A set whose one renderer draws with materials, each new on the stand-in's Lit unless given.
        static LightmapSet Set(GameObject node, params Material[] materials)
        {
            var renderer = node.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            var set = node.AddComponent<LightmapSet>();
            set.receivers.Add(new LightmapSet.Receiver { renderer = renderer });
            return set;
        }

        [Test]
        public void Each_material_a_map_lights_moves_onto_the_pipelines_baked_shader_once_with_the_rules_keyword()
        {
            var node = new GameObject("BAKED");
            var a = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "A" };
            var b = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "B" };
            var other = new Material(Shader.Find("Unlit/Texture")) { name = "Other" };
            try
            {
                LightmapSet set = Set(node, a, b, a);
                PureRenderPipelineHost host = Pipeline();
                Assert.That(LightmapsImporter.Baked(set, host, BakedShader, true), Is.EqualTo(2), "a material shared twice is moved once");
                Assert.That((a.shader, b.shader), Is.EqualTo((BakedShader, BakedShader)));
                Assert.That(a.IsKeywordEnabled(LightmapsImporter.ReflectByBakeKeyword) && b.IsKeywordEnabled(LightmapsImporter.ReflectByBakeKeyword),
                            Is.True, "reflecting by its bake");
                Assert.That(LightmapsImporter.Baked(set, host, BakedShader, false), Is.EqualTo(2), "on it already: the rule set again");
                Assert.That(a.IsKeywordEnabled(LightmapsImporter.ReflectByBakeKeyword), Is.False, "by its scale alone");
                set.receivers[0].renderer.sharedMaterials = new[] { other };
                Assert.That(LightmapsImporter.Baked(set, new PureRenderPipelineHost(), BakedShader, true), Is.Zero, "not the pipeline's Lit: left");
                Assert.That(LightmapsImporter.Baked(set, host, null, true), Is.Zero, "no shader for a baked surface: nothing");
                Assert.That(LightmapsImporter.Baked(null, host, BakedShader, true), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(node);
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
                Object.DestroyImmediate(other);
            }
        }

        [Test]
        public void A_project_material_remapped_onto_the_model_is_named_and_left_as_it_is()
        {
            var node = new GameObject("BAKED");
            var own = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "Own" };
            var project = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "Project" };
            try
            {
                if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
                AssetDatabase.CreateAsset(project, Folder + "/Project.mat");
                LightmapSet set = Set(node, own, project);
                var named = new List<string>();
                Assert.That(LightmapsImporter.Baked(set, Pipeline(), BakedShader, true, named), Is.EqualTo(1));
                Assert.That(own.shader, Is.SameAs(BakedShader), "the model's own material");
                Assert.That(project.shader.name, Is.EqualTo("Hidden/Internal-Colored"), "a project's material: not the import's to write");
                Assert.That(named, Is.EqualTo(new[] { Folder + "/Project.mat" }));
            }
            finally
            {
                Object.DestroyImmediate(node);
                Object.DestroyImmediate(own);
            }
        }

        [Test]
        public void The_rules_scale_is_its_probes_and_the_import_depends_on_the_pipelines_baked_shader()
        {
            var node = new GameObject("BAKED");
            var lit = new Material(Shader.Find("Hidden/Internal-Colored")) { name = "Lit" };
            Shader baked = BakedShader;
            const string bakedLit = BakedLit;
            try
            {
                LightmapSet set = Set(node, lit);
                set.probe = new GameObject("Probe").AddComponent<ReflectionProbe>();
                set.probe.transform.SetParent(node.transform, false);
                var depends = new List<string>();
                Assert.That(LightmapsImporter.Reflect(set, true, 0.25f, Model, Pipeline(), null, depends.Add), Is.Zero,
                            "a pipeline with no shader for a baked surface: its materials stay on its Lit");
                Assert.That(set.probe.intensity, Is.EqualTo(0.25f), "the scale rides the delivery's own probe");
                Assert.That((lit.shader.name, depends.Count), Is.EqualTo(("Hidden/Internal-Colored", 0)));

                Assert.That(LightmapsImporter.Reflect(set, true, 2f, Model, Pipeline(), bakedLit, depends.Add), Is.EqualTo(1));
                Assert.That(depends, Is.EqualTo(new[] { bakedLit }), "the import depends on the shader it moves its materials onto");
                Assert.That((set.probe.intensity, lit.shader), Is.EqualTo((2f, baked)));

                set.probe = null;
                LogAssert.Expect(LogType.Warning, new Regex("no reflection probe to carry it"));
                LightmapsImporter.Reflect(set, false, 0.5f, Model, Pipeline(), bakedLit, depends.Add);
                LogAssert.Expect(LogType.Warning, new Regex(@"shader for a baked surface \(Assets/None\.shader\) is not made"));
                Assert.That(LightmapsImporter.Reflect(set, false, 1f, Model, Pipeline(), "Assets/None.shader", depends.Add), Is.Zero,
                            "a shader not made: said, never a silent nothing");
                lit.shader = baked;
                var elsewhere = new PureRenderPipelineHost { LitShader = "Hidden/Internal-Colored", DrawsShader = _ => true };
                set.receivers[0].renderer.sharedMaterials = new[] { new Material(Shader.Find("Unlit/Texture")) };
                LogAssert.Expect(LogType.Warning, new Regex("no material its maps light is on Hidden/Internal-Colored"));
                Assert.That(LightmapsImporter.Reflect(set, true, 1f, Model, elsewhere, bakedLit, depends.Add), Is.Zero,
                            "a pipeline with the shader, and nothing moved onto it: said");
                Object.DestroyImmediate(set.receivers[0].renderer.sharedMaterial);
            }
            finally
            {
                Object.DestroyImmediate(node);
                Object.DestroyImmediate(lit);
            }
        }

        const string Folder = "Assets/UnitytkLightmapsImporterTests";
        const string Model = Folder + "/room.fbx";

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
        }

        static GameObject Import(string record, string ns = "")
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            LightmapsFbx.Write(Model, record, ns, LightmapsFbx.Files());
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(Model);
        }

        // Each receiver by its renderer's path under the file's room node, namespaces dropped.
        static Dictionary<string, LightmapSet.Receiver> ByPath(LightmapSet set)
            => set.receivers.ToDictionary(r => AnimationUtility.CalculateTransformPath(r.renderer.transform, set.transform)
                                                                .Replace("ns:", "").Substring("room/".Length));

        [Test]
        public void A_delivery_gives_its_root_a_set_of_references_which_binds_once_placed()
        {
            GameObject model = Import(LightmapsFbx.Record());
            var set = model.GetComponent<LightmapSet>();
            Assert.That(set, Is.Not.Null);
            Assert.That(set.mode, Is.EqualTo(LightmapsMode.NonDirectional), "a delivery's maps are direction-free");
            Assert.That(set.maps.Select(m => m.color.name), Is.EquivalentTo(new[] { "Crate_Lightmap", "Room_Lightmap" }), "one map per file: the atlas shared");
            foreach (var map in set.maps)
            {
                string path = AssetDatabase.GetAssetPath(map.color);
                Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).textureType, Is.EqualTo(TextureImporterType.Lightmap),
                            $"{path}: extracted from the FBX and imported as a lightmap");
            }
            var receivers = ByPath(set);
            Assert.That(receivers.Keys, Is.EquivalentTo(LightmapsFbx.Rects.Keys), "every object, the two BODYs by their parents");
            foreach (var pair in LightmapsFbx.Rects)
            {
                Assert.That(receivers[pair.Key].scaleOffset, Is.EqualTo(pair.Value), $"{pair.Key}: its rect");
                Assert.That(set.maps[receivers[pair.Key].map].color.name, Is.EqualTo(pair.Key == "Crate" ? "Crate_Lightmap" : "Room_Lightmap"));
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var placed = instance.GetComponent<LightmapSet>();
            Assert.That(placed.Bound, Is.True, "placed in a scene no bake owns, it binds");
            foreach (LightmapSet.Receiver receiver in placed.receivers)
            {
                Renderer r = receiver.renderer;
                Assert.That(LightmapSettings.lightmaps[r.lightmapIndex].lightmapColor, Is.SameAs(placed.maps[receiver.map].color), r.name);
                Assert.That(r.lightmapScaleOffset, Is.EqualTo(receiver.scaleOffset), r.name);
            }
            Assert.That(placed.probe.enabled, Is.True);

            placed.enabled = false;
            Assert.That(placed.receivers.Select(x => x.renderer.lightmapIndex).Distinct(), Is.EqualTo(new[] { -1 }), "disabled, the fields given back");
            Assert.That(LightmapSettings.lightmaps.Where(d => d.lightmapColor != null), Is.Empty);
        }

        [Test]
        public void The_probe_is_a_native_custom_probe_at_the_capture_point_with_the_records_box()
        {
            GameObject model = Import(LightmapsFbx.Record());
            var set = model.GetComponent<LightmapSet>();
            ReflectionProbe probe = set.probe;
            Assert.That(probe, Is.Not.Null);
            Assert.That(probe.name, Is.EqualTo(LightmapsImporter.ProbeName));
            Assert.That(probe.transform.parent, Is.SameAs(model.transform));
            Assert.That(probe.mode, Is.EqualTo(ReflectionProbeMode.Custom));
            var cube = probe.customBakedTexture as Cubemap;
            Assert.That(cube, Is.Not.Null, "the record's panorama as a cubemap");
            Assert.That(AssetDatabase.GetAssetPath(cube), Is.EqualTo(Model), "a sub-asset the import owns");
            // Record units times unit_scale (0.01 m), the FBX's X mirrored.
            AssertNear(probe.transform.localPosition, new Vector3(-1f, 1.5f, -2f), "at the capture point");
            Assert.That(probe.boxProjection, Is.True, "projected onto the room it was captured in");
            AssertNear(probe.size, new Vector3(8f, 4f, 10f), "the record's box");
            AssertNear(probe.center, new Vector3(2f, 0.5f, 3f), "its centre, from the probe");
        }

        [Test]
        public void A_namespaced_delivery_joins_each_object_by_its_leaf()
        {
            // The nodes of a referenced asset's delivery keep their namespace; the record
            // names the objects without it (a producer that strips them).
            GameObject model = Import(LightmapsFbx.Record(), "ns:");
            Assert.That(model.GetComponentsInChildren<Transform>(true).Any(t => t.name == "ns:Crate"), Is.True, "the node keeps its namespace");
            var receivers = ByPath(model.GetComponent<LightmapSet>());
            Assert.That(receivers.Keys, Is.EquivalentTo(LightmapsFbx.Rects.Keys), "every object, by its leaf and hierarchy");
            Assert.That(receivers["MACHINE_B/BODY"].scaleOffset, Is.EqualTo(LightmapsFbx.Rects["MACHINE_B/BODY"]));
        }

        [Test]
        public void A_tie_stays_unbound_and_is_said()
        {
            // The two BODY entries with no parent to tell them apart.
            string record = LightmapsFbx.Record().Replace("\"hierarchy\": [\"room\", \"MACHINE_A\", \"BODY\"]", "\"hierarchy\": [\"BODY\"]")
                                                 .Replace("\"hierarchy\": [\"room\", \"MACHINE_B\", \"BODY\"]", "\"hierarchy\": [\"BODY\"]");
            LogAssert.Expect(LogType.Warning, new Regex("'BODY' matches several objects equally \\(.*MACHINE_A/BODY, .*MACHINE_B/BODY\\)"));
            GameObject model = Import(record);
            var receivers = ByPath(model.GetComponent<LightmapSet>());
            Assert.That(receivers.Keys, Is.EquivalentTo(new[] { "Crate", "Floor", "Wall" }), "never guessed");
        }

        [Test]
        public void A_v2_record_is_refused_with_its_fix_and_no_set_is_made()
        {
            LogAssert.Expect(LogType.Warning, new Regex("lightmaps record v2; unitytk reads v3 only.*re-export"));
            GameObject model = Import(LightmapsFbx.Record(version: 2));
            Assert.That(model.GetComponent<LightmapSet>(), Is.Null);
            Assert.That(model.transform.Find(LightmapsImporter.ProbeName), Is.Null);
        }

        [Test]
        public void A_file_without_the_record_keeps_unitys_import()
        {
            GameObject model = Import(null);
            Assert.That(model.GetComponentsInChildren<LightmapSet>(true), Is.Empty);
        }

        [Test]
        public void The_records_it_reads_are_declared()
        {
            var read = ReadsRecordAttribute.Read(new[] { typeof(LightmapsImporter).Assembly });
            Assert.That(read.Keys, Is.EquivalentTo(new[] { UnitytkRecords.Lightmaps, UnitytkRecords.Handoff }), "no materials");
            Assert.That(read[UnitytkRecords.Lightmaps], Is.EqualTo(LightmapRecord.CurrentVersion));
            Assert.That(read[UnitytkRecords.Handoff], Is.EqualTo(HandoffBlock.CurrentVersion));
        }

        static HandoffBlock.Shape.Payload Handoff(string rendering)
        {
            var record = HandoffBlock.Read("{\"version\": " + HandoffBlock.CurrentVersion + ", \"instructions\": \"Read me.\", " +
                                           "\"assembly\": [], \"rendering\": " + rendering + "}", "Assets/room.fbx", out string refusal);
            Assert.That(record, Is.Not.Null, refusal);
            return record;
        }

        [Test]
        public void A_set_holds_the_scene_only_as_its_handoff_says()
        {
            var node = new GameObject("ROOM");
            try
            {
                var set = node.AddComponent<LightmapSet>();
                Assert.That((set.environment, set.lightsOff), Is.EqualTo((false, false)), "a set added by hand leaves the scene alone");
                LightmapsImporter.Handoff(set, null);
                Assert.That((set.environment, set.lightsOff), Is.EqualTo((false, false)), "a file with no handoff: the set's own");

                LightmapsImporter.Handoff(set, Handoff(
                    "{\"environment\": {\"source\": \"probe\", \"probeIntensity\": 0.5}, \"keyLight\": {\"disabledWhen\": \"lightmapped\"}, " +
                    "\"lightmappedMaterials\": {\"envMapIntensity\": 0.25, \"reflectionNormalization\": \"none\"}}"));
                Assert.That((set.environment, set.environmentIntensity, set.lightsOff), Is.EqualTo((true, 0.5f, true)));

                LightmapsImporter.Handoff(set, Handoff("{\"environment\": {\"source\": \"room\"}, \"keyLight\": {\"disabledWhen\": \"never\"}, " +
                                                       "\"lightmappedMaterials\": {}}"));
                Assert.That((set.environment, set.lightsOff), Is.EqualTo((false, false)), "another source, another rule: the scene's own");
                Assert.That(set.environmentIntensity, Is.EqualTo(HandoffRendering.Environment.ProbeIntensityDefault), "a setting left out: the policy's");

                LightmapsImporter.Handoff(set, Handoff("{\"renderer\": {}}"));
                Assert.That((set.environment, set.lightsOff), Is.EqualTo((false, false)), "sections it leaves out: no rule");
            }
            finally
            {
                Object.DestroyImmediate(node);
            }
        }

        [Test]
        public void Its_surfaces_reflection_rule_is_the_handoffs_and_none_where_it_states_none()
        {
            Assert.That(LightmapsImporter.Reflection(null, Model, out _, out _), Is.False, "no handoff: the materials stay as they are");
            Assert.That(LightmapsImporter.Reflection(Handoff("{\"renderer\": {}}"), Model, out _, out _), Is.False,
                        "no lightmapped materials stated: as they are");
            Assert.That(LightmapsImporter.Reflection(Handoff("{\"lightmappedMaterials\": {\"envMapIntensity\": 0.25, " +
                                                             "\"reflectionNormalization\": \"none\"}}"), Model,
                                                     out bool byBake, out float scale), Is.True);
            Assert.That((byBake, scale), Is.EqualTo((false, 0.25f)), "by its scale alone");
            Assert.That(LightmapsImporter.Reflection(Handoff("{\"lightmappedMaterials\": {}}"), Model, out byBake, out scale), Is.True);
            Assert.That((byBake, scale), Is.EqualTo((true, HandoffRendering.LightmappedMaterials.EnvMapIntensityDefault)),
                        "a setting left out: the policy's (lightmapLuma)");
            LogAssert.Expect(LogType.Warning, new Regex("does not draw: envMapTerms 'full'"));
            LightmapsImporter.Reflection(Handoff("{\"lightmappedMaterials\": {\"envMapTerms\": \"full\"}}"), Model, out _, out _);
        }

        [Test]
        public void An_exact_name_beats_a_leaf_where_the_hierarchy_cannot_tell_them_apart()
        {
            var root = new GameObject("root");
            try
            {
                Renderer Node(string name)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(root.transform, false);
                    return go.AddComponent<MeshRenderer>();
                }
                Renderer a = Node("A:prop"), b = Node("B:prop");
                var matches = LightmapsImporter.Receivers(root.transform, new List<RecordNames.Entry>
                {
                    new RecordNames.Entry("A:prop", new[] { "A:prop" }), new RecordNames.Entry("B:prop", new[] { "B:prop" }),
                    new RecordNames.Entry("C:prop", new[] { "C:prop" }),
                });
                Assert.That(matches[0].Item, Is.SameAs(a.transform));
                Assert.That(matches[1].Item, Is.SameAs(b.transform));
                Assert.That(matches[2].Outcome, Is.EqualTo(RecordNames.Outcome.Absent), "each candidate went to a better match");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void A_deleted_map_comes_back_on_the_reimport_its_delete_brings()
        {
            GameObject model = Import(LightmapsFbx.Record());
            string map = AssetDatabase.GetAssetPath(model.GetComponent<LightmapSet>().maps.Single(m => m.color.name == "Crate_Lightmap").color);
            Assert.That(AssetDatabase.DeleteAsset(map), Is.True);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            Assert.That(File.Exists(map), Is.True, "the model's extraction wrote it, so its delete asks for it again");
            var set = model.GetComponent<LightmapSet>();
            Assert.That(set.maps.Select(m => m.color.name), Is.EquivalentTo(new[] { "Crate_Lightmap", "Room_Lightmap" }));
            Assert.That(ByPath(set).Keys, Is.EquivalentTo(LightmapsFbx.Rects.Keys), "every object bound again");
            Assert.That(TypeOf(map), Is.EqualTo(TextureImporterType.Lightmap), "imported as a lightmap again");
        }

        [Test]
        public void A_change_to_a_bound_maps_import_settings_keeps_the_set_whole()
        {
            // Unity reuses a cached import whose dependencies still match: an import that
            // asked for the maps and depended on none of them came back from the cache --
            // its set empty, no import run -- once a map the next import bound changed.
            GameObject model = Import(LightmapsFbx.Record());
            string map = AssetDatabase.GetAssetPath(model.GetComponent<LightmapSet>().maps.Single(m => m.color.name == "Crate_Lightmap").color);
            var importer = (TextureImporter)AssetImporter.GetAtPath(map);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var set = AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponent<LightmapSet>();
            Assert.That(set.maps.Select(m => m.color.name), Is.EquivalentTo(new[] { "Crate_Lightmap", "Room_Lightmap" }), "the set kept whole");
            Assert.That(ByPath(set).Keys, Is.EquivalentTo(LightmapsFbx.Rects.Keys));
            Assert.That(set.probe, Is.Not.Null);
        }

        [Test]
        public void A_redelivered_map_replaces_the_extracted_one_in_place_its_guid_and_settings_kept()
        {
            // The project pinned a setting on the map its delivery extracted, and
            // materials and scenes bind it by GUID; a re-export with a new bake lands on
            // that same file, so neither is lost.
            GameObject model = Import(LightmapsFbx.Record());
            string map = AssetDatabase.GetAssetPath(model.GetComponent<LightmapSet>().maps.Single(m => m.color.name == "Crate_Lightmap").color);
            string guid = AssetDatabase.AssetPathToGUID(map);
            var importer = (TextureImporter)AssetImporter.GetAtPath(map);
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            var files = LightmapsFbx.Files();
            files[LightmapsFbx.CrateMap] = LightmapsFbx.Exr(8, 8, new Color(0.1f, 0.9f, 0.2f, 1f));  // the new bake
            LightmapsFbx.Write(Model, LightmapsFbx.Record(), "", files);
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Assert.That(File.ReadAllBytes(map), Is.EqualTo(files[LightmapsFbx.CrateMap]), "the new bake, in place");
            Assert.That(AssetDatabase.AssetPathToGUID(map), Is.EqualTo(guid), "its GUID kept");
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(map)).textureCompression,
                        Is.EqualTo(TextureImporterCompression.Uncompressed), "the setting the project pinned kept");
            var set = AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponent<LightmapSet>();
            Assert.That(AssetDatabase.GetAssetPath(set.maps.Single(m => m.color.name == "Crate_Lightmap").color), Is.EqualTo(map),
                        "the set binds the same file");
        }

        [Test]
        public void A_scaled_instance_projects_onto_a_box_of_its_own_size()
        {
            GameObject model = Import(LightmapsFbx.Record());
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            var set = instance.GetComponent<LightmapSet>();
            instance.transform.localScale = new Vector3(2f, 3f, 4f);
            set.enabled = false;
            set.enabled = true;
            AssertNear(set.probe.size, new Vector3(16f, 12f, 40f), "the record's box, scaled with the instance");
            AssertNear(set.probe.center, new Vector3(4f, 1.5f, 12f), "its centre from the probe, scaled too");
            AssertNear(set.probe.transform.position, new Vector3(-2f, 4.5f, -8f), "the capture point moves with the scale");
        }

        [Test]
        public void Apply_imports_a_probes_panorama_whose_import_was_changed_as_its_cubemap_needs()
        {
            GameObject model = Import(LightmapsFbx.Record());
            string panorama = AssetDatabase.GUIDToAssetPath(model.GetComponent<LightmapSet>().probeSource);
            Assert.That(Path.GetFileName(panorama), Is.EqualTo(LightmapsFbx.Probe), "the set names the probe's panorama");
            var importer = (TextureImporter)AssetImporter.GetAtPath(panorama);
            importer.isReadable = false;
            importer.SaveAndReimport();  // the model's import reads it: it reimports
            model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            Assert.That(model.GetComponent<LightmapSet>().probe, Is.Null, "premise: no cubemap, no probe");
            Assert.That(model.GetComponent<LightmapSet>().probeSource, Is.EqualTo(AssetDatabase.AssetPathToGUID(panorama)),
                        "the set still names the panorama it could not read");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            Assert.That(instance.scene.IsValid() && instance.scene.isLoaded, Is.True, "premise: placed in an open scene");

            var plan = new OpContext(dryRun: true);
            LightmapsActions.Apply(plan);
            Assert.That(plan.Changes.Select(c => (c.kind, c.target)),
                        Does.Contain(("write", AssetDatabase.GetTextMetaFilePathFromAssetPath(panorama))),
                        "its import, asked as a write; issues: " + string.Join("; ", plan.Issues.Select(i => i.message)));

            LightmapsActions.Apply(new OpContext());
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(panorama)).isReadable, Is.True);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            Assert.That(model.GetComponent<LightmapSet>().probe, Is.Not.Null, "the model reimported, and made its probe");
            var placed = UnityEngine.Object.FindObjectsByType<LightmapSet>(FindObjectsSortMode.None).Single();
            Assert.That(placed.probe, Is.Not.Null, "...on the placed instance too");
            var again = new OpContext(dryRun: true);
            LightmapsActions.Apply(again);
            Assert.That(again.Changes, Is.Empty, "a second run plans nothing");
        }

        // -- the import's own rules, over a model built in memory ---------------------------

        /// <summary>The record's nodes under a root, each a unit quad (with a second UV set where <paramref name="uv2"/>).</summary>
        static GameObject Room(bool uv2 = true)
        {
            var root = new GameObject("model");
            Transform Node(Transform parent, string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                return go.transform;
            }
            void Quad(Transform parent, string name)
            {
                Transform t = Node(parent, name);
                var corners = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
                var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1f, 1f, 0f), Vector3.up }, uv = corners };
                if (uv2) mesh.uv2 = corners;
                mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
                t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                t.gameObject.AddComponent<MeshRenderer>();
            }
            Transform room = Node(root.transform, "room");
            Quad(room, "Crate");
            Quad(room, "Floor");
            Quad(room, "Wall");
            Quad(Node(room, "MACHINE_A"), "BODY");
            Quad(Node(room, "MACHINE_B"), "BODY");
            return root;
        }

        /// <summary>The delivery's files in the model's folder, imported as a file copied in by hand is (the panorama readable where asked).</summary>
        static void Files(bool readablePanorama)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            foreach (var file in LightmapsFbx.Files())
            {
                string path = Folder + "/" + file.Key;
                File.WriteAllBytes(path, file.Value);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            if (!readablePanorama) return;
            var panorama = (TextureImporter)AssetImporter.GetAtPath(Folder + "/" + LightmapsFbx.Probe);
            LightmapsTextureRoles.Pin(panorama, LightmapsTextureRoles.Role.Panorama, true);
            panorama.SaveAndReimport();
        }

        // The import's work on root, its media the model's folder (the stand-in's), its cubes kept in added.
        static LightmapSet Assemble(GameObject root, string record, ColorSpace space, List<Object> added)
            => LightmapsImporter.Assemble(root, record, Model, new PureRecordSource(), new PureModelMedia(),
                                          (id, made) => added.Add(made), _ => { }, space);

        static void Each(List<Object> added, GameObject root)
        {
            foreach (Object made in added) Object.DestroyImmediate(made);
            Object.DestroyImmediate(root);
        }

        static TextureImporterType TypeOf(string path) => ((TextureImporter)AssetImporter.GetAtPath(path)).textureType;

        [Test]
        public void Each_loss_is_said_with_its_fix()
        {
            Files(readablePanorama: false);
            var added = new List<Object>();
            GameObject root = Room(uv2: false);
            try
            {
                string record = LightmapsFbx.Record().Replace($"\"map\": \"{LightmapsFbx.CrateMap}\"", "\"map\": \"Gone_Lightmap.exr\"");
                LogAssert.Expect(LogType.Warning, new Regex("makes no cubemap: it is not imported readable"));
                LogAssert.Expect(LogType.Warning, new Regex("5 object\\(s\\) have no second UV set \\(uv2\\).*left unbound: Crate, Floor, Wall, BODY, BODY\\..*second set"));
                LogAssert.Expect(LogType.Warning, new Regex("colour space is Gamma.*Switch to Linear"));
                LightmapSet set = Assemble(root, record, ColorSpace.Gamma, added);
                Assert.That(set.receivers, Is.Empty, "no object without lightmap UVs is bound");
                Assert.That(set.probe, Is.Null, "an unreadable panorama makes no probe");
                Assert.That(set.probeSource, Is.EqualTo(AssetDatabase.AssetPathToGUID(Folder + "/" + LightmapsFbx.Probe)),
                            "...but the set names it, for Apply's repair");
                Assert.That(added, Is.Empty, "no cube registered with the import");
            }
            finally
            {
                Each(added, root);
            }

            added.Clear();
            root = Room();
            try
            {
                string record = LightmapsFbx.Record().Replace($"\"map\": \"{LightmapsFbx.CrateMap}\"", "\"map\": \"Gone_Lightmap.exr\"");
                LogAssert.Expect(LogType.Warning, new Regex("makes no cubemap"));
                LogAssert.Expect(LogType.Warning, new Regex("1 map\\(s\\) the record names are not in the model's media: Gone_Lightmap.exr.*Re-export"));
                LogAssert.Expect(LogType.Warning, new Regex("map\\(s\\) are not imported as lightmaps.*: [^:]*Room_Lightmap.exr\\. Set each one's Texture Type to Lightmap in its import settings\\.$"));
                LightmapSet set = Assemble(root, record, ColorSpace.Linear, added);
                Assert.That(set.receivers.Select(r => r.renderer.name), Is.EquivalentTo(new[] { "Floor", "Wall", "BODY", "BODY" }),
                            "the crate, whose map is missing, is left unbound");
            }
            finally
            {
                Each(added, root);
            }
        }

        [Test]
        public void A_record_whose_unit_scale_or_box_cannot_place_the_probe_makes_none_and_registers_no_cube()
        {
            Files(readablePanorama: true);
            foreach (var (record, said) in new[]
            {
                (LightmapsFbx.Record().Replace("\"unit_scale\": 0.01", "\"unit_scale\": 0"), "no positive unit_scale"),
                (LightmapsFbx.Record().Replace("\"min\": [-500, 0, -400]", "\"min\": [NaN, 0, -400]"), "box has a corner that is no finite point"),
            })
            {
                var added = new List<Object>();
                GameObject root = Room();
                try
                {
                    LogAssert.Expect(LogType.Warning, new Regex(said + ".*no reflection probe is made"));
                    LightmapSet set = Assemble(root, record, ColorSpace.Linear, added);
                    Assert.That(set.probe, Is.Null, said);
                    Assert.That(root.transform.Find(LightmapsImporter.ProbeName), Is.Null, said);
                    Assert.That(added, Is.Empty, $"{said}: no cube registered with the import, then dropped");
                }
                finally
                {
                    Each(added, root);
                }
            }
        }

        [Test]
        public void An_open_scenes_probe_is_distant_and_reaches_the_models_bounds()
        {
            Files(readablePanorama: true);
            var added = new List<Object>();
            GameObject root = Room();
            try
            {
                string record = Regex.Replace(LightmapsFbx.Record(), "\"box\": \\{[^}]*\\}", "\"box\": null");
                LightmapSet set = Assemble(root, record, ColorSpace.Linear, added);
                ReflectionProbe probe = set.probe;
                Assert.That(probe, Is.Not.Null);
                Assert.That(probe.boxProjection, Is.False, "an open scene's probe is read as distant");
                // Every quad spans (0, 0, 0) to (1, 1, 0) in the root's frame; the probe sits at (-1, 1.5, -2).
                AssertNear(probe.size, new Vector3(1f, 1f, 0f), "the model's bounds");
                AssertNear(probe.center, new Vector3(1.5f, -1f, 2f), "their centre, from the probe");
                Assert.That(added.OfType<Cubemap>().Single().name, Is.EqualTo(Path.GetFileNameWithoutExtension(LightmapsFbx.Probe)));
            }
            finally
            {
                Each(added, root);
            }
        }

        [Test]
        public void The_cube_is_the_panorama_turned_a_quarter_and_linear()
        {
            // An 8 x 2 panorama, each column its own red: Unity's column c is the record's c + 2.
            const int w = 8, h = 2;
            var source = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) source[y * w + x] = new Color(x / 8f, y, 0.5f, 1f);
            Color[] turned = ProbeCube.Turned(source, w, h, ColorSpace.Linear);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    Assert.That(turned[y * w + x], Is.EqualTo(source[y * w + (x + w / 4) % w]), $"texel ({x}, {y})");

            // A width no multiple of 4 blends the two columns the quarter falls between.
            var odd = new[] { Color.black, Color.white, Color.black, Color.black, Color.black, Color.black };
            Assert.That(ProbeCube.Turned(odd, 6, 1, ColorSpace.Linear)[0], Is.EqualTo(Color.LerpUnclamped(Color.white, Color.black, 0.5f)),
                        "column 0 reads 1.5 of the record's");

            // A Gamma project stores the EXR gamma-encoded: the bake's linear values back.
            Color stored = new Color(0.5f, 0.25f, 1f, 1f);
            Assert.That(ProbeCube.Turned(new[] { stored, stored, stored, stored }, 4, 1, ColorSpace.Gamma)[0], Is.EqualTo(stored.linear));
            Assert.That(ProbeCube.Turned(new[] { stored, stored, stored, stored }, 4, 1, ColorSpace.Linear)[0], Is.EqualTo(stored),
                        "a Linear project's are read as they are");
            Assert.That(source[0], Is.EqualTo(new Color(0f, 0f, 0.5f, 1f)), "the source is left as it was");
        }

        [Test]
        public void The_probes_ambient_light_is_its_panorama_as_a_surface_sees_it()
        {
            const int w = 64, h = 32;
            var even = new Color[w * h];
            for (int i = 0; i < even.Length; i++) even[i] = new Color(2f, 1f, 0.5f, 1f);
            Color[] lit = Lit(ProbeCube.Ambient(even, w, h), Vector3.up, Vector3.down, Vector3.forward);
            foreach (Color way in lit)
            {
                Assert.That(way.r, Is.EqualTo(2f).Within(0.02f), "an even room lights every way alike, at its own level");
                Assert.That(way.g, Is.EqualTo(1f).Within(0.01f));
                Assert.That(way.b, Is.EqualTo(0.5f).Within(0.005f));
            }

            // A ceiling of light over a dark floor: the top rows are up (Unity's layout).
            var ceiling = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) ceiling[y * w + x] = y >= h / 2 ? Color.white : Color.black;
            lit = Lit(ProbeCube.Ambient(ceiling, w, h), Vector3.up, Vector3.down, Vector3.forward);
            Assert.That(lit[0].r, Is.EqualTo(1f).Within(0.08f), "facing the ceiling: all of it");
            Assert.That(lit[1].r, Is.EqualTo(0f).Within(0.08f), "facing the floor: none");
            Assert.That(lit[2].r, Is.EqualTo(0.5f).Within(0.05f), "facing a wall: half");

            // One bright quarter at the image's three quarters: +X in Unity's layout.
            var side = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = w * 5 / 8; x < w * 7 / 8; x++) side[y * w + x] = Color.white;
            lit = Lit(ProbeCube.Ambient(side, w, h), Vector3.right, Vector3.left);
            Assert.That(lit[0].r, Is.GreaterThan(lit[1].r + 0.3f), "the light is where the layout puts it");
        }

        static Color[] Lit(float[] ambient, params Vector3[] facing)
        {
            Assert.That(ambient.Length, Is.EqualTo(27));
            var light = new UnityEngine.Rendering.SphericalHarmonicsL2();
            for (int channel = 0; channel < 3; channel++)
                for (int i = 0; i < 9; i++) light[channel, i] = ambient[channel * 9 + i];
            var lit = new Color[facing.Length];
            light.Evaluate(facing, lit);
            return lit;
        }

        [Test]
        public void Each_object_resolves_as_the_reference_resolves_it()
        {
            // pythontk's own case table for _Lightmaps._resolve_lightmap_entries (the one
            // its viewer parity test holds the GLB build and the page to): exact before
            // leaf, namespaces tolerated, same-named objects told apart by hierarchy, a stale
            // same-named entry absent, a tie ambiguous. The reference's "exact" and "leaf"
            // are Bound here, to the same node.
            var scene = new GameObject("scene");
            try
            {
                Transform Node(Transform parent, string name, bool mesh)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(parent, false);
                    if (mesh) go.AddComponent<MeshRenderer>();
                    return go.transform;
                }
                Transform grp = Node(scene.transform, "GRP", false);
                Transform bodyA = Node(Node(grp, "MACHINE_A", false), "BODY", true);
                Transform bodyB = Node(Node(grp, "MACHINE_B", false), "BODY", true);
                Transform prop = Node(scene.transform, "NS:prop", true);
                Transform room = Node(scene.transform, "room", true);
                Node(scene.transform, "NS_A:room", true);
                Node(scene.transform, "twin", true);
                Node(scene.transform, "twin", true);
                var entries = new List<RecordNames.Entry>
                {
                    new RecordNames.Entry("BODY", new[] { "GRP", "MACHINE_A", "BODY" }),
                    new RecordNames.Entry("BODY", new[] { "GRP", "MACHINE_B", "BODY" }),
                    new RecordNames.Entry("BODY", new[] { "OLD", "BODY" }),
                    new RecordNames.Entry("prop", new string[0]),
                    new RecordNames.Entry("room", new string[0]),
                    new RecordNames.Entry("twin", new string[0]),
                    new RecordNames.Entry("gone"),
                    new RecordNames.Entry(""),
                };
                RecordNames.Match<Transform>[] matches = LightmapsImporter.Receivers(scene.transform, entries);
                var expected = new (RecordNames.Outcome outcome, Transform node)[]
                {
                    (RecordNames.Outcome.Bound, bodyA), (RecordNames.Outcome.Bound, bodyB), (RecordNames.Outcome.Absent, null),
                    (RecordNames.Outcome.Bound, prop), (RecordNames.Outcome.Bound, room), (RecordNames.Outcome.Ambiguous, null),
                    (RecordNames.Outcome.Absent, null), (RecordNames.Outcome.Absent, null),
                };
                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.That(matches[i].Outcome, Is.EqualTo(expected[i].outcome), $"entry {i} ({entries[i].Name})");
                    Assert.That(matches[i].Item, Is.EqualTo(expected[i].node), $"entry {i}'s node");
                }
                Assert.That(matches[5].Candidates, Is.EqualTo(new[] { "twin", "twin" }), "a tie names where its candidates sit");
            }
            finally
            {
                Object.DestroyImmediate(scene);
            }
        }

        static void AssertNear(Vector3 actual, Vector3 expected, string why)
            => Assert.That((actual - expected).magnitude, Is.LessThan(1e-4f), $"{why}: {actual} for {expected}");
    }
}
