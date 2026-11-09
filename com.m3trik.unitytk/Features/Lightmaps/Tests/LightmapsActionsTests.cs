using System;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Lightmaps.Tests
{
    /// <summary>
    /// The Lightmaps actions held to the one-owner rule and the kernel's checks: Apply
    /// clears Unity's bake, asked first (its dry run names everything a real bake's clear
    /// takes, light probes included, and changes nothing), never a bake whose maps a set
    /// holds, imports the sets' maps as lightmaps and binds them, and a second run plans
    /// nothing; Capture fills a set from a scene Unity baked, holding copies of the bake's
    /// maps, which outlive Apply's clear; a cancelled bake gives the sets back their
    /// bindings; Assign fills one by hand, one Ctrl+Z taking it back; Validate refuses sets
    /// of disagreeing modes in one scene and names a scene a bake owns that holds sets and
    /// a rotated probed model.
    /// </summary>
    public class LightmapsActionsTests
    {
        const string Folder = "Assets/UnitytkLightmapsActionsTests";
        const string MapPath = Folder + "/Room_Lightmap.exr";

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
        }

        /// <summary>A quad with a second UV set, the one Unity reads a lightmap through.</summary>
        static Mesh Quad()
        {
            var uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, new Vector3(1f, 1f, 0f), Vector3.up },
                uv = uv,
                uv2 = uv,
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            mesh.RecalculateNormals();
            return mesh;
        }

        static Renderer Part(GameObject parent, string name, Vector3 at)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = at;
            go.AddComponent<MeshFilter>().sharedMesh = Quad();
            var r = go.AddComponent<MeshRenderer>();
            // The pipeline's own default material: what the lightmapper reads the surface from.
            r.sharedMaterial = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultMaterial
                : AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            return r;
        }

        // A map written as a plain texture, as a file copied in by hand imports.
        static Texture2D Map()
        {
            File.WriteAllBytes(MapPath, LightmapsFbx.Exr(8, 8, new Color(0.5f, 0.4f, 0.3f, 1f)));
            AssetDatabase.ImportAsset(MapPath, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);
        }

        static LightmapSet Room(Texture2D map, out Renderer part)
        {
            var room = new GameObject("Room");
            part = Part(room, "Part", Vector3.zero);
            var set = room.AddComponent<LightmapSet>();
            set.maps.Add(new LightmapSet.Map { color = map });
            set.receivers.Add(new LightmapSet.Receiver { renderer = part, map = 0 });
            return set;
        }

        // The active scene saved with a Lighting Data Asset of its own: a scene Unity's bake owns.
        static string Bake()
        {
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, Folder + "/Scene.unity");
            var bake = new LightingDataAsset(scene);
            string path = Folder + "/LightingData.asset";
            AssetDatabase.CreateAsset(bake, path);
            Lightmapping.SetLightingDataAssetForScene(scene, bake);
            LightmapOwner.RefreshAll();
            return path;
        }

        static TextureImporterType TypeOf(string path) => ((TextureImporter)AssetImporter.GetAtPath(path)).textureType;

        [Test]
        public void A_set_in_a_preview_scene_such_as_prefab_modes_is_left_alone()
        {
            // Apply and Validate work in the open scenes alone: a prefab's stage, and any other
            // preview scene, is no scene a bake owns, and binding there writes into the prefab.
            var map = new Texture2D(4, 4);
            LightmapSet room = Room(map, out _);
            string prefab = Folder + "/Room.prefab";
            PrefabUtility.SaveAsPrefabAsset(room.gameObject, prefab);
            UnityEngine.Object.DestroyImmediate(room.gameObject);
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.OpenPrefab(prefab);
                Assert.That(stage, Is.Not.Null, "Prefab Mode");
                LightmapSet other = Room(map, out _);
                SceneManager.MoveGameObjectToScene(other.gameObject, preview);
                UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex("No Lightmap Set in the open scene"));
                Assert.That(LightmapsActions.Apply(), Is.EqualTo(0), "no set of the open scenes: the stage's and the preview's are left alone");
                Assert.That(LightmapsActions.Validate(), Is.EqualTo(0), "nor validated");
            }
            finally
            {
                UnityEditor.SceneManagement.StageUtility.GoToMainStage();
                EditorSceneManager.ClosePreviewScene(preview);
                UnityEngine.Object.DestroyImmediate(map);
            }
        }

        [Test]
        public void Apply_clears_the_bake_asking_first_and_its_dry_run_changes_nothing()
        {
            LightmapSet set = Room(Map(), out Renderer part);
            string bake = Bake();
            Assert.That(set.StandsDown, Is.True, "Unity's bake owns the scene");

            var plan = new OpContext(dryRun: true);
            int planned = LightmapsActions.Apply(plan);
            Assert.That(planned, Is.EqualTo(1));
            Assert.That(plan.Changes.Select(c => (c.kind, c.target)), Does.Contain(("delete", bake)), "the bake, named");
            Assert.That(plan.Changes.Select(c => (c.kind, c.target)),
                        Does.Contain(("write", AssetDatabase.GetTextMetaFilePathFromAssetPath(MapPath))), "the map's import");
            Assert.That(LightmapOwner.Baked(SceneManager.GetActiveScene()), Is.True, "a dry run clears nothing");
            Assert.That(File.Exists(bake), Is.True);
            Assert.That(TypeOf(MapPath), Is.EqualTo(TextureImporterType.Default), "...imports nothing");
            Assert.That(part.lightmapIndex, Is.EqualTo(-1), "...and binds nothing");

            var live = new OpContext();
            Assert.That(LightmapsActions.Apply(live), Is.EqualTo(planned));
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target, c.action))),
                        "the plan named every change the run made");
            Assert.That(LightmapOwner.Baked(SceneManager.GetActiveScene()), Is.False, "the bake cleared");
            Assert.That(File.Exists(bake), Is.False, "its Lighting Data Asset with it");
            Assert.That(TypeOf(MapPath), Is.EqualTo(TextureImporterType.Lightmap));
            set = UnityEngine.Object.FindObjectsByType<LightmapSet>(FindObjectsSortMode.None).Single();
            Assert.That(set.Bound, Is.True, "the set owns the scene now");
            Assert.That(LightmapSettings.lightmaps[set.receivers[0].renderer.lightmapIndex].lightmapColor.name, Is.EqualTo("Room_Lightmap"));

            var again = new OpContext(dryRun: true);
            Assert.That(LightmapsActions.Apply(again), Is.EqualTo(1));
            Assert.That(again.Changes.Select(c => $"{c.kind} {c.target}: {c.action}"), Is.Empty, "a second run plans nothing");
        }

        [Test]
        public void Apply_with_no_set_says_so()
        {
            var plan = new OpContext(dryRun: true);
            Assert.That(LightmapsActions.Apply(plan), Is.EqualTo(0));
            Assert.That(plan.Issues.Select(i => i.code), Is.EqualTo(new[] { LightmapsActions.NothingToApplyCode }));
            Assert.That(plan.Changes, Is.Empty);
        }

        /// <summary>
        /// A room Unity can bake, saved: a floor and a wall that contribute GI, a baked sun,
        /// a light-probe group, and the smallest lighting settings that still bake.
        /// </summary>
        static GameObject Bakeable(out Renderer floor, out Renderer wall)
        {
            var room = new GameObject("Room");
            floor = Part(room, "Floor", Vector3.zero);
            wall = Part(room, "Wall", new Vector3(2f, 0f, 0f));
            foreach (Renderer r in new[] { floor, wall })
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.ContributeGI);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.lightmapBakeType = LightmapBakeType.Baked;
            sun.transform.rotation = Quaternion.LookRotation(Vector3.forward);
            new GameObject("Probes").AddComponent<LightProbeGroup>().probePositions = new[] { Vector3.zero, Vector3.one };
            var settings = new LightingSettings
            {
                bakedGI = true,
                realtimeGI = false,
                lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
                lightmapResolution = 4,
                lightmapMaxSize = 32,
                lightmapPadding = 2,
                directSampleCount = 1,
                indirectSampleCount = 1,
                environmentSampleCount = 1,
                maxBounces = 0,
                directionalityMode = LightmapsMode.NonDirectional,
                filteringMode = LightingSettings.FilterMode.None,
            };
            AssetDatabase.CreateAsset(settings, Folder + "/Lighting.lighting");
            Lightmapping.lightingSettings = settings;
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Baked.unity");
            return room;
        }

        /// <summary><see cref="Bakeable"/>, baked by Unity.</summary>
        static GameObject Baked(out Renderer floor, out Renderer wall)
        {
            GameObject room = Bakeable(out floor, out wall);
            Assert.That(Lightmapping.Bake(), Is.True, "Unity baked the scene");
            Assert.That(floor.lightmapIndex, Is.GreaterThanOrEqualTo(0), "the bake lit the floor");
            return room;
        }

        // Where a set under the room keeps its copies of a bake's maps.
        const string Copies = Folder + "/Baked_Lightmaps/Room";

        [Test]
        public void Capture_fills_a_set_from_a_scene_unity_baked_holding_copies_of_its_maps()
        {
            GameObject room = Baked(out Renderer floor, out Renderer wall);
            string[] bakeMaps = new[] { floor, wall }.Select(r => AssetDatabase.GetAssetPath(LightmapSettings.lightmaps[r.lightmapIndex].lightmapColor))
                                                    .Distinct().ToArray();

            var plan = new OpContext(dryRun: true);
            Assert.That(LightmapsActions.Capture(room, plan), Is.EqualTo(2));
            Assert.That(plan.Changes.Select(c => (c.kind, c.target)),
                        Is.EqualTo(bakeMaps.Select(m => ("write", Copies + "/" + Path.GetFileName(m)))
                                           .Concat(new[] { ("create", "Room/LightmapSet"), ("change", "Room") })),
                        "each of the bake's maps copied out, asked as a write, then the set");
            Assert.That(room.GetComponent<LightmapSet>(), Is.Null, "a dry run adds nothing");
            Assert.That(AssetDatabase.IsValidFolder(Copies), Is.False, "...and writes nothing");

            OpContext.RunMenu("Capture Scene Lightmaps", ctx => LightmapsActions.Capture(room, ctx));
            var set = room.GetComponent<LightmapSet>();
            Assert.That(set, Is.Not.Null);
            Assert.That(set.mode, Is.EqualTo(LightmapSettings.lightmapsMode), "the scene's mode");
            foreach (Renderer r in new[] { floor, wall })
            {
                LightmapSet.Receiver receiver = set.receivers.Single(x => x.renderer == r);
                string shown = AssetDatabase.GetAssetPath(LightmapSettings.lightmaps[r.lightmapIndex].lightmapColor);
                Assert.That(AssetDatabase.GetAssetPath(set.maps[receiver.map].color), Is.EqualTo(Copies + "/" + Path.GetFileName(shown)),
                            $"{r.name}: a copy of the bake's map");
                Assert.That(File.ReadAllBytes(AssetDatabase.GetAssetPath(set.maps[receiver.map].color)), Is.EqualTo(File.ReadAllBytes(shown)));
                Assert.That(TypeOf(AssetDatabase.GetAssetPath(set.maps[receiver.map].color)), Is.EqualTo(TextureImporterType.Lightmap),
                            "its import settings with it");
                Assert.That(receiver.scaleOffset, Is.EqualTo(r.lightmapScaleOffset), r.name);
            }
            Assert.That(set.StandsDown, Is.True, "Unity's bake still owns the scene: the set stands down until Apply");
            Assert.That(LightmapsActions.Capture(room, new OpContext(dryRun: true)), Is.EqualTo(2));
            var again = new OpContext(dryRun: true);
            LightmapsActions.Capture(room, again);
            Assert.That(again.Changes, Is.Empty, "a second capture plans nothing: the copies are current");
        }

        [Test]
        public void Capture_then_apply_keeps_the_maps_the_set_holds()
        {
            GameObject room = Baked(out Renderer floor, out _);
            string bakeMap = AssetDatabase.GetAssetPath(LightmapSettings.lightmaps[floor.lightmapIndex].lightmapColor);
            OpContext.RunMenu("Capture Scene Lightmaps", ctx => LightmapsActions.Capture(room, ctx));

            var live = new OpContext();
            Assert.That(LightmapsActions.Apply(live), Is.EqualTo(2));
            Assert.That(live.Issues, Is.Empty, string.Join("; ", live.Issues.Select(i => i.message)));
            Assert.That(LightmapOwner.Baked(SceneManager.GetActiveScene()), Is.False, "the bake cleared");
            Assert.That(File.Exists(bakeMap), Is.False, "...its maps with it");
            var set = UnityEngine.Object.FindObjectsByType<LightmapSet>(FindObjectsSortMode.None).Single();
            Assert.That(set.maps.Select(m => m.color), Has.All.Not.Null, "the set's maps outlive the bake");
            Assert.That(set.Bound, Is.True, "the set owns the scene now");
            Assert.That(LightmapSettings.lightmaps[floor.lightmapIndex].lightmapColor, Is.SameAs(set.maps[set.receivers[0].map].color));
        }

        [Test]
        public void Apply_never_clears_a_bake_whose_maps_a_set_holds()
        {
            GameObject room = Baked(out Renderer floor, out _);
            string bakeMap = AssetDatabase.GetAssetPath(LightmapSettings.lightmaps[floor.lightmapIndex].lightmapColor);
            var other = new GameObject("Other");
            Renderer part = Part(other, "Part", new Vector3(0f, 3f, 0f));
            LightmapSet.Map held = null;
            LightmapsActions.Assign(other, part, LightmapSettings.lightmaps[floor.lightmapIndex].lightmapColor, Vector4.one);
            held = other.GetComponent<LightmapSet>().maps.Single();

            var plan = new OpContext(dryRun: true);
            LightmapsActions.Apply(plan);
            Assert.That(plan.Issues.Select(i => (i.code, i.severity)), Is.EqualTo(new[] { (LightmapsActions.BakeMapsHeldCode, "error") }));
            Assert.That(plan.Issues[0].message, Does.Contain(bakeMap).And.Contain("Other"), "the map, and the set holding it");
            Assert.That(plan.Changes.Where(c => c.kind == "delete"), Is.Empty, "nothing of the bake planned");

            LightmapsActions.Apply(new OpContext());
            Assert.That(LightmapOwner.Baked(SceneManager.GetActiveScene()), Is.True, "the bake left as it is");
            Assert.That(File.Exists(bakeMap), Is.True);
            Assert.That(held.color, Is.Not.Null, "the set keeps its map");
            Assert.That(room, Is.Not.Null);
        }

        [Test]
        public void Apply_plans_everything_the_clear_of_a_real_bake_takes()
        {
            Room(Map(), out _);
            Baked(out _, out _);
            string data = AssetDatabase.GetAssetPath(LightmapOwner.Bake(SceneManager.GetActiveScene()));
            string[] textures = AssetDatabase.GetDependencies(data, false)
                                             .Where(f => typeof(Texture).IsAssignableFrom(AssetDatabase.GetMainAssetTypeAtPath(f))).ToArray();
            Assert.That(textures, Is.Not.Empty, "premise: the bake wrote textures");
            Assert.That(LightmapSettings.lightProbes != null && LightmapSettings.lightProbes.count > 0, Is.True, "premise: the bake's light probes");

            var plan = new OpContext(dryRun: true);
            LightmapsActions.Apply(plan);
            var deletes = plan.Changes.Where(c => c.kind == "delete").ToList();
            Assert.That(deletes.Select(c => c.target), Is.EquivalentTo(new[] { data }.Concat(textures)), "its Lighting Data Asset and every texture");
            Assert.That(deletes.Single(c => c.target == data).action, Does.EndWith("its Lighting Data Asset"));
            var named = deletes.Where(c => c.target != data).Select(c => c.action).ToList();
            Assert.That(named, Has.Some.EndsWith("a lightmap"), "the bake's lightmap, named for what it is");
            foreach (string action in named)
                Assert.That(action, Does.Match("(a lightmap|a lightmap's directions|a shadow mask|a baked reflection probe|a texture)$"));
            Assert.That(plan.Changes.Where(c => c.target == "light probes").Select(c => c.action),
                        Is.EqualTo(new[] { $"clear the bake's {LightmapSettings.lightProbes.count} light probe(s)" }));
            Assert.That(LightmapOwner.Baked(SceneManager.GetActiveScene()), Is.True, "a dry run clears nothing");
            Assert.That(textures.All(File.Exists), Is.True);
        }

        [Test]
        public void A_cancelled_bake_gives_the_sets_their_bindings_back()
        {
            LightmapSet set = Room(Map(), out Renderer part);
            Bakeable(out _, out _);
            Assert.That(set.Bound, Is.True, "no bake: the set owns the scene");
            Assert.That(Lightmapping.BakeAsync(), Is.True, "a bake started");
            try
            {
                Assert.That(set.Bound, Is.False, "a starting bake takes the sets' bindings down");
            }
            finally
            {
                Lightmapping.Cancel();
            }
            Assert.That(Lightmapping.isRunning, Is.False);
            Assert.That(set.Bound, Is.True, "cancelled, the scene's last bake stands, and it is none: the set binds again");
            Assert.That(LightmapSettings.lightmaps[part.lightmapIndex].lightmapColor.name, Is.EqualTo("Room_Lightmap"));
        }

        [Test]
        public void Validate_names_a_rotated_probed_model()
        {
            LightmapSet set = Room(Map(), out _);
            var probe = new GameObject(LightmapsImporter.ProbeName).AddComponent<ReflectionProbe>();
            probe.transform.SetParent(set.transform, false);
            probe.boxProjection = true;
            set.probe = probe;
            Assert.That(LightmapsActions.Validate(new OpContext(dryRun: true)), Is.EqualTo(0), "unrotated: nothing to say");
            set.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            var ctx = new OpContext(dryRun: true);
            Assert.That(LightmapsActions.Validate(ctx), Is.EqualTo(1));
            Assert.That(ctx.Issues.Select(i => (i.code, i.severity)), Is.EqualTo(new[] { (LightmapsActions.ProbeRotatedCode, "warning") }));
            Assert.That(ctx.Issues[0].message, Does.Contain("Room"));
            probe.boxProjection = false;
            Assert.That(LightmapsActions.Validate(new OpContext(dryRun: true)), Is.EqualTo(0), "a distant probe has no box to turn");
        }

        [Test]
        public void Assign_fills_a_set_by_hand_and_one_ctrl_z_takes_it_back()
        {
            Texture2D map = Map();
            var room = new GameObject("Room");
            Renderer part = Part(room, "Part", Vector3.zero);
            var rect = new Vector4(0.5f, 0.5f, 0.5f, 0f);

            var plan = new OpContext(dryRun: true);
            Assert.That(LightmapsActions.Assign(room, part, map, rect, plan), Is.True);
            Assert.That(plan.Changes.Select(c => c.kind), Is.EqualTo(new[] { "create", "change" }));
            Assert.That(room.GetComponent<LightmapSet>(), Is.Null);

            Assert.That(OpContext.RunMenu("Assign", ctx => LightmapsActions.Assign(room, part, map, rect, ctx)), Is.True);
            var set = room.GetComponent<LightmapSet>();
            Assert.That(set.Bound, Is.True);
            Assert.That(LightmapSettings.lightmaps[part.lightmapIndex].lightmapColor, Is.SameAs(map));
            Assert.That(part.lightmapScaleOffset, Is.EqualTo(rect));
            Assert.That(LightmapsActions.Assign(room, part, map, rect, new OpContext(dryRun: true)), Is.False, "a second run changes nothing");

            Undo.PerformUndo();
            Assert.That(room.GetComponent<LightmapSet>() == null, "one Ctrl+Z took the set away");
            Assert.That(part.lightmapIndex, Is.EqualTo(-1), "...and its binding with it");
        }

        [Test]
        public void Assign_refuses_a_renderer_of_another_scene_and_a_prefab_asset()
        {
            var room = new GameObject("Room");
            Renderer part = Part(room, "Part", Vector3.zero);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/First.unity");
            Scene other = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var elsewhere = new GameObject("Elsewhere");
            SceneManager.MoveGameObjectToScene(elsewhere, other);
            Assert.Throws<ArgumentException>(() => LightmapsActions.Assign(elsewhere, part, Map(), Vector4.one));
            GameObject asset = PrefabUtility.SaveAsPrefabAsset(room, Folder + "/Room.prefab");
            Assert.Throws<ArgumentException>(() => LightmapsActions.Capture(asset), "a prefab asset lights no scene");
            Assert.Throws<ArgumentNullException>(() => LightmapsActions.Capture(null));
        }

        [Test]
        public void Validate_refuses_sets_in_one_scene_whose_modes_disagree()
        {
            Texture2D map = Map();
            LightmapSet one = Room(map, out _);
            LightmapSet two = Room(map, out _);
            two.mode = LightmapsMode.CombinedDirectional;
            var ctx = new OpContext(dryRun: true);
            Assert.That(LightmapsActions.Validate(ctx), Is.EqualTo(1));
            var issue = ctx.Issues.Single();
            Assert.That(issue.code, Is.EqualTo(LightmapsActions.ModesDisagreeCode));
            Assert.That(issue.severity, Is.EqualTo("error"));
            two.mode = one.mode;
            Assert.That(LightmapsActions.Validate(new OpContext(dryRun: true)), Is.EqualTo(0), "one mode: nothing to say");
        }

        [Test]
        public void Validate_names_a_scene_a_bake_owns_that_holds_sets()
        {
            Room(Map(), out _);
            string bake = Bake();
            var ctx = new OpContext(dryRun: true);
            Assert.That(LightmapsActions.Validate(ctx), Is.EqualTo(1));
            var issue = ctx.Issues.Single();
            Assert.That(issue.code, Is.EqualTo(LightmapsActions.BakeOwnsSetsCode));
            Assert.That(issue.message, Does.Contain(bake).And.Contain("Room"));
            Assert.That(ctx.Changes, Is.Empty, "it checks, never changes");
        }
    }
}
