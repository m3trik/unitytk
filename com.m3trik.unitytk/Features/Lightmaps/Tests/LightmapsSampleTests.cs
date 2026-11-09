using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

using M3trik.Unitytk.Testing;
namespace M3trik.Unitytk.Lightmaps.Tests
{
    /// <summary>
    /// The Lightmaps sample, imported as the Package Manager imports it: a floor and
    /// three boxes lit by a bake the DCC committed, the floor by its own map and the
    /// boxes by one atlas, each box its own quarter of it, and the bake's probe, captured
    /// at (40, 100, -30) cm in the box (-250, 0, -200) to (200, 300, 220) cm, whose
    /// delivered panorama holds the bake's lamp. The model's set holds every renderer with
    /// its map and rect, binds them placed and gives them back disabled; the probe is a
    /// native box-projected one at the record's point, reading the delivered panorama and
    /// nothing of the record's optional extensions; a scene with a Lighting Data Asset makes the
    /// set stand down; and the demo recipe builds a scene the set owns, Validate clean.
    /// The project these run in imports every sample of the package, so a sample that is
    /// not there fails: it is never skipped.
    /// </summary>
    public class LightmapsSampleTests
    {
        const string Package = "com.m3trik.unitytk";
        const string SampleName = "Lightmaps";
        const string Recipe = "M3trik.Unitytk.Samples.Lightmaps.LightmapsDemoRecipe, M3trik.Unitytk.Samples.Lightmaps.Editor";
        const string Scratch = "Assets/UnitytkLightmapsSampleTests";
        const string FloorMap = "Floor_Lightmap";
        const string Atlas = "Boxes_Lightmap";
        const string Panorama = "Lightmaps_Probe_Lights";

        /// <summary>Each object's map and rect, as the recipe committed them (uv2 * (x, y) + (z, w)).</summary>
        static readonly Dictionary<string, (string map, Vector4 rect)> Lit = new Dictionary<string, (string, Vector4)>
        {
            ["Floor"] = (FloorMap, new Vector4(1f, 1f, 0f, 0f)),
            ["Box_A"] = (Atlas, new Vector4(0.5f, 0.5f, 0f, 0f)),
            ["Box_B"] = (Atlas, new Vector4(0.5f, 0.5f, 0.5f, 0f)),
            ["Box_C"] = (Atlas, new Vector4(0.5f, 0.5f, 0f, 0.5f)),
        };

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Scratch);
        }

        static string Folder()
        {
            Sample sample = Sample.FindByPackage(Package, null).FirstOrDefault(s => s.displayName == SampleName);
            Assert.That(sample.displayName, Is.EqualTo(SampleName),
                        "the package lists no Lightmaps sample: its package.json's samples are written by the workspace's sample sync");
            Assert.That(sample.isImported, Is.True,
                        $"the Lightmaps sample is not imported at {sample.importPath}: import it from the Package Manager's " +
                        "Samples tab (the owned project's builder copies every sample in)");
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return "Assets" + full.Substring(assets.Length);
        }

        static string Model() => Folder() + "/Lightmaps.fbx";

        static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>(Model());

        static LightmapSet Set(GameObject root)
        {
            var set = root.GetComponent<LightmapSet>();
            Assert.That(set, Is.Not.Null, "the lightmaps record gives the model's root a Lightmap Set");
            return set;
        }

        static Dictionary<string, LightmapSet.Receiver> ByName(LightmapSet set) => set.receivers.ToDictionary(r => r.renderer.name);

        /// <summary>The sample's model placed in a new scene no bake owns.</summary>
        static LightmapSet Placed()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            return Set((GameObject)PrefabUtility.InstantiatePrefab(Prefab()));
        }

        static Texture2D Slot(Renderer r) => LightmapSettings.lightmaps[r.lightmapIndex].lightmapColor;

        [Test]
        public void The_set_holds_every_renderer_with_its_map_and_rect()
        {
            LightmapSet set = Set(Prefab());
            Assert.That(set.mode, Is.EqualTo(LightmapsMode.NonDirectional), "a delivery's maps are direction-free");
            Assert.That(set.maps.Select(m => m.color.name), Is.EquivalentTo(new[] { FloorMap, Atlas }), "one map per file: the atlas shared");
            foreach (LightmapSet.Map map in set.maps)
            {
                string path = AssetDatabase.GetAssetPath(map.color);
                Assert.That(((TextureImporter)AssetImporter.GetAtPath(path)).textureType, Is.EqualTo(TextureImporterType.Lightmap),
                            $"{path}: extracted from the FBX and imported as a lightmap");
                Assert.That(map.direction, Is.Null, $"{path}: no directions, non-directional");
            }
            var receivers = ByName(set);
            Assert.That(receivers.Keys, Is.EquivalentTo(Lit.Keys), "the floor and every box");
            foreach (var pair in Lit)
            {
                LightmapSet.Receiver receiver = receivers[pair.Key];
                Assert.That(set.maps[receiver.map].color.name, Is.EqualTo(pair.Value.map), $"{pair.Key}: its map");
                Assert.That(receiver.scaleOffset, Is.EqualTo(pair.Value.rect), $"{pair.Key}: its rect");
                Mesh mesh = receiver.renderer.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.HasVertexAttribute(VertexAttribute.TexCoord1), Is.True, $"{pair.Key}: read through its second UV set");
            }
        }

        [Test]
        public void Placed_it_binds_them_and_disabled_it_gives_them_back()
        {
            LightmapSet set = Placed();
            Assert.That(set.Bound, Is.True, "placed in a scene no bake owns, it binds");
            Assert.That(LightmapSettings.lightmapsMode, Is.EqualTo(LightmapsMode.NonDirectional));
            foreach (var pair in ByName(set))
            {
                Renderer r = pair.Value.renderer;
                Assert.That(Slot(r), Is.SameAs(set.maps[pair.Value.map].color), $"{pair.Key}: its map's slot");
                Assert.That(r.lightmapScaleOffset, Is.EqualTo(Lit[pair.Key].rect), $"{pair.Key}: its rect");
            }
            int[] slots = set.receivers.Select(x => x.renderer.lightmapIndex).Distinct().ToArray();
            Assert.That(slots, Has.Length.EqualTo(2), "two slots: the floor's map and the boxes' atlas");
            Assert.That(set.probe.enabled, Is.True, "its probe on with its maps");

            set.enabled = false;
            Assert.That(set.Bound, Is.False);
            Assert.That(set.receivers.Select(x => x.renderer.lightmapIndex).Distinct(), Is.EqualTo(new[] { -1 }), "each index given back");
            Assert.That(set.receivers.Select(x => x.renderer.lightmapScaleOffset).Distinct(), Is.EqualTo(new[] { new Vector4(1f, 1f, 0f, 0f) }),
                        "each rect given back");
            Assert.That((LightmapSettings.lightmaps ?? Array.Empty<LightmapData>()).Where(d => d.lightmapColor != null), Is.Empty,
                        "its slots freed");
            Assert.That(set.probe.enabled, Is.False, "its probe off");

            set.enabled = true;
            Assert.That(set.Bound, Is.True, "enabled again, it binds again");
            Assert.That(Slot(ByName(set)["Box_C"].renderer).name, Is.EqualTo(Atlas));
        }

        [Test]
        public void The_probe_lands_at_the_records_point_with_its_box_reading_the_delivered_panorama()
        {
            GameObject model = Prefab();
            ReflectionProbe probe = Set(model).probe;
            Assert.That(probe, Is.Not.Null, "the record's probe");
            Assert.That(probe.name, Is.EqualTo(LightmapsImporter.ProbeName));
            Assert.That(probe.transform.parent, Is.SameAs(model.transform));
            Assert.That(probe.mode, Is.EqualTo(ReflectionProbeMode.Custom));
            // Centimetres times unit_scale (0.01 m), the FBX's X mirrored as Unity's import mirrors it.
            AssertNear(probe.transform.localPosition, new Vector3(-0.4f, 1f, -0.3f), "at the capture point");
            Assert.That(probe.boxProjection, Is.True, "projected onto the room it was captured in");
            AssertNear(probe.size, new Vector3(4.5f, 3f, 4.2f), "the record's box");
            AssertNear(probe.center, new Vector3(0.65f, 0.5f, 0.4f), "its centre, from the probe");

            var cube = probe.customBakedTexture as Cubemap;
            Assert.That(cube, Is.Not.Null, "a cubemap made of the record's panorama");
            Assert.That(cube.name, Is.EqualTo(Panorama), "the delivered panorama: the bake's lamp drawn in");
            Assert.That(AssetDatabase.GetAssetPath(cube), Is.EqualTo(Model()), "a sub-asset the import owns");
            // The record's highlight copies ride the FBX too (an optional extension): Unity picks a
            // probe by volume, never per renderer, so the import makes no probe of them.
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(Model()).OfType<Cubemap>().Select(c => c.name), Is.EqualTo(new[] { Panorama }),
                        "one cubemap: the delivered panorama's, none of a highlight copy");
            Assert.That(Prefab().GetComponentsInChildren<ReflectionProbe>(true), Has.Length.EqualTo(1), "one probe");
        }

        [Test]
        public void A_scene_with_a_lighting_data_asset_makes_it_stand_down()
        {
            LightmapSet set = Placed();
            Assert.That(set.Bound, Is.True, "no bake: the set owns the scene");
            if (!AssetDatabase.IsValidFolder(Scratch)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Scratch));
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, Scratch + "/Baked.unity");
            var bake = new LightingDataAsset(scene);
            AssetDatabase.CreateAsset(bake, Scratch + "/LightingData.asset");
            Lightmapping.SetLightingDataAssetForScene(scene, bake);
            LightmapOwner.RefreshAll();

            Assert.That(set.StandsDown, Is.True, "Unity's bake owns the scene");
            Assert.That(set.Bound, Is.False, "it binds nothing");
            Assert.That(set.receivers.Select(x => x.renderer.lightmapIndex).Distinct(), Is.EqualTo(new[] { -1 }), "its fields given back");
            Assert.That(set.probe.enabled, Is.False, "its probe off");
            var ctx = new OpContext(dryRun: true);
            Assert.That(LightmapsActions.Validate(ctx), Is.EqualTo(1));
            Assert.That(ctx.Issues.Single().code, Is.EqualTo(LightmapsActions.BakeOwnsSetsCode), "a baked scene that holds sets is named");
        }

        [Test]
        public void The_demo_recipe_builds_a_scene_its_set_owns_and_validate_is_clean()
        {
            string folder = Folder();
            Type recipe = Type.GetType(Recipe);
            Assert.That(recipe, Is.Not.Null, "the sample's editor assembly is missing: it did not compile, or the sample is not imported");
            var scene = (Scene)recipe.GetMethod("Build").Invoke(null, new object[] { folder });
            Assert.That(scene.path, Is.EqualTo(folder + "/LightmapsDemo.unity"), "saved beside the model");
            GameObject model = scene.GetRootGameObjects().Single(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == Prefab());
            LightmapSet set = Set(model);
            Assert.That(set.Bound, Is.True, "the set owns the demo scene");
            Assert.That(LightmapOwner.Baked(scene), Is.False, "no bake of Unity's");
            Assert.That(UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None), Is.Empty,
                        "the maps hold the light: no realtime light lights the boxes again");
            Assert.That(set.probe.enabled, Is.True);
            var ctx = new OpContext(dryRun: true);
            Assert.That(LightmapsActions.Validate(ctx), Is.EqualTo(0), string.Join("; ", ctx.Issues.Select(i => i.message)));
            Assert.That(ctx.Issues, Is.Empty, "Validate is clean on the demo scene");
            // The camera sees the boxes: each one's centre in front of it, inside the view.
            Camera camera = Camera.main;
            foreach (string box in new[] { "Box_A", "Box_B", "Box_C" })
            {
                Vector3 view = camera.WorldToViewportPoint(model.transform.Find(box).GetComponent<Renderer>().bounds.center);
                Assert.That(view.z > 0f && view.x > 0f && view.x < 1f && view.y > 0f && view.y < 1f, Is.True, $"{box} in view: {view}");
            }
            // An error logged fails the test (the Test Framework's own rule).
        }

        // The sample's model copied in and imported, then reimported with set applied to its
        // importer: the probe's point, box and centre scale as the meshes do.
        static void ProbeKeepsToTheMeshes(Action<ModelImporter> set, float expected)
        {
            (Vector3 at, Vector3 size, Vector3 centre) Read(GameObject model)
            {
                ReflectionProbe probe = Set(model).probe;
                Assert.That(probe, Is.Not.Null, "the copy's probe");
                return (probe.transform.localPosition, probe.size, probe.center);
            }
            var (before, after, scale) = ImportScaleCheck.Rescaled(Model(), Scratch, set, Read, expected);
            ImportScaleCheck.Near(after.at, before.at * scale, $"the capture point, x{scale}");
            ImportScaleCheck.Near(after.size, before.size * scale, "the box");
            ImportScaleCheck.Near(after.centre, before.centre * scale, "its centre");
        }

        [Test]
        public void At_scale_factor_2_the_probe_scales_with_the_meshes()
            => ProbeKeepsToTheMeshes(importer => importer.globalScale = 2f, 2f);

        [Test]
        public void With_convert_units_off_the_probe_scales_with_the_meshes()
            => ProbeKeepsToTheMeshes(importer => importer.useFileScale = false, 0f);

        static void AssertNear(Vector3 actual, Vector3 expected, string why) => ImportScaleCheck.Near(actual, expected, why);
    }
}
