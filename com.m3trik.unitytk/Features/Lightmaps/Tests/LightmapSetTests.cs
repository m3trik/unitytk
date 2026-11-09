using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Lightmaps.Tests
{
    /// <summary>
    /// The set's binding through Unity's native fields: enabled, its maps take slots of
    /// <see cref="LightmapSettings.lightmaps"/> (one per texture, shared between sets) and
    /// its renderers their slot and rect, its probe on; disabled, every field is given
    /// back as found (unless rebound since) and its probe off; slots freed are reused and
    /// the free tail dropped. One owner per scene: in a scene with a Lighting Data Asset
    /// the set stands down, and a build marks such a scene for the player, where a set
    /// spawned later reads the mark. An undone edit binds what the set holds now, a scene
    /// change that rebuilt the lightmaps binds it again, and a scaled instance sizes its
    /// probe's box.
    /// </summary>
    public class LightmapSetTests
    {
        const string Folder = "Assets/UnitytkLightmapSetTests";

        Texture2D _a, _b;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _a = new Texture2D(4, 4) { name = "A_Lightmap" };
            _b = new Texture2D(4, 4) { name = "B_Lightmap" };
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Object.DestroyImmediate(_a);
            Object.DestroyImmediate(_b);
            AssetDatabase.DeleteAsset(Folder);
        }

        static Renderer Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform, false);
            return child.AddComponent<MeshRenderer>();
        }

        // A disabled room of two renderers lit by the set: the first by A, the second by B at a rect.
        LightmapSet Room(string name, out Renderer first, out Renderer second, out ReflectionProbe probe)
        {
            var room = new GameObject(name);
            room.SetActive(false);
            first = Child(room, "First");
            second = Child(room, "Second");
            probe = new GameObject("Probe").AddComponent<ReflectionProbe>();
            probe.transform.SetParent(room.transform, false);
            var set = room.AddComponent<LightmapSet>();
            set.maps.Add(new LightmapSet.Map { color = _a });
            set.maps.Add(new LightmapSet.Map { color = _b });
            set.receivers.Add(new LightmapSet.Receiver { renderer = first, map = 0 });
            set.receivers.Add(new LightmapSet.Receiver { renderer = second, map = 1, scaleOffset = new Vector4(0.5f, 0.5f, 0.25f, 0f) });
            set.probe = probe;
            return set;
        }

        static Texture2D Slot(Renderer r) => LightmapSettings.lightmaps[r.lightmapIndex].lightmapColor;

        [Test]
        public void Enabled_it_binds_its_maps_and_rects_and_disabled_it_gives_every_field_back()
        {
            LightmapSet set = Room("Room", out Renderer first, out Renderer second, out ReflectionProbe probe);
            LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
            set.gameObject.SetActive(true);
            Assert.That(set.Bound, Is.True);
            Assert.That(Slot(first), Is.SameAs(_a));
            Assert.That(Slot(second), Is.SameAs(_b));
            Assert.That(first.lightmapScaleOffset, Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
            Assert.That(second.lightmapScaleOffset, Is.EqualTo(new Vector4(0.5f, 0.5f, 0.25f, 0f)));
            Assert.That(LightmapSettings.lightmapsMode, Is.EqualTo(LightmapsMode.NonDirectional), "the mode the set states");
            Assert.That(probe.enabled, Is.True, "its probe on with its maps");

            set.enabled = false;
            Assert.That(set.Bound, Is.False);
            Assert.That(new[] { first.lightmapIndex, second.lightmapIndex }, Is.EqualTo(new[] { -1, -1 }), "each index as it was");
            Assert.That(second.lightmapScaleOffset, Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)), "each rect as it was");
            Assert.That(LightmapSettings.lightmaps.Where(d => d.lightmapColor != null), Is.Empty, "its slots given back");
            Assert.That(probe.enabled, Is.False, "its probe off");
        }

        [Test]
        public void Two_sets_share_a_maps_slot_and_the_last_to_go_frees_it()
        {
            LightmapSet one = Room("One", out Renderer a, out _, out _);
            LightmapSet two = Room("Two", out Renderer b, out _, out _);
            one.gameObject.SetActive(true);
            two.gameObject.SetActive(true);
            Assert.That(b.lightmapIndex, Is.EqualTo(a.lightmapIndex), "one slot per texture");
            Assert.That(LightmapSettings.lightmaps.Length, Is.EqualTo(2));
            one.enabled = false;
            Assert.That(Slot(b), Is.SameAs(_a), "the other set still reads it");
            two.enabled = false;
            Assert.That(LightmapSettings.lightmaps.Where(d => d.lightmapColor != null), Is.Empty);
        }

        [Test]
        public void A_scene_with_a_lighting_data_asset_makes_its_sets_stand_down()
        {
            LightmapSet bound = Room("Bound", out Renderer first, out _, out ReflectionProbe probe);
            bound.gameObject.SetActive(true);
            Assert.That(bound.Bound, Is.True, "no bake: the set owns the scene");

            Scene scene = Baked();
            LightmapOwner.RefreshAll();
            Assert.That(bound.StandsDown, Is.True);
            Assert.That(bound.Bound, Is.False, "Unity's bake owns it now");
            Assert.That(first.lightmapIndex, Is.EqualTo(-1), "its fields given back");
            Assert.That(probe.enabled, Is.False);

            LightmapSet late = Room("Late", out Renderer other, out _, out ReflectionProbe lateProbe);
            late.gameObject.SetActive(true);
            Assert.That(late.Bound, Is.False, "a set enabled in a baked scene binds nothing");
            Assert.That(other.lightmapIndex, Is.EqualTo(-1));
            Assert.That(lateProbe.enabled, Is.False);
            Assert.That(scene.IsValid(), Is.True);
        }

        [Test]
        public void A_build_marks_a_scene_its_bake_owns_and_a_player_reads_the_mark()
        {
            Scene scene = SceneManager.GetActiveScene();
            LightmapOwner.Stamp(scene);
            Assert.That(LightmapBakeMark.In(scene), Is.False, "no Lighting Data Asset: no mark, the sets own it in the player");
            Baked();
            LightmapOwner.Stamp(scene);
            Assert.That(LightmapBakeMark.In(scene), Is.True, "Unity's bake owns it: marked");
            LightmapOwner.Stamp(scene);
            Assert.That(scene.GetRootGameObjects().Count(go => go.GetComponent<LightmapBakeMark>() != null), Is.EqualTo(1), "one mark");

            System.Func<Scene, bool> editor = LightmapSet.BakeOwns;
            LightmapSet.BakeOwns = null;  // a player: no editor part to ask
            try
            {
                // A prefab instantiated into the scene at runtime: never processed by the build.
                LightmapSet spawned = Room("Spawned", out Renderer first, out _, out ReflectionProbe probe);
                spawned.gameObject.SetActive(true);
                Assert.That(spawned.StandsDown, Is.True, "it reads its own scene's mark");
                Assert.That(spawned.Bound, Is.False);
                Assert.That(first.lightmapIndex, Is.EqualTo(-1));
                Assert.That(probe.enabled, Is.False);

                Lightmapping.SetLightingDataAssetForScene(scene, null);
                LightmapOwner.Stamp(scene);
                Assert.That(LightmapBakeMark.In(scene), Is.False, "no bake any more: the mark goes");
                spawned.enabled = false;
                spawned.enabled = true;
                Assert.That(spawned.Bound, Is.True, "an unmarked scene is the sets' in a player");
            }
            finally
            {
                LightmapSet.BakeOwns = editor;
            }
        }

        [Test]
        public void Play_mode_is_no_build_and_leaves_the_scene_unmarked()
        {
            Scene scene = Baked();
            new LightmapOwner().OnProcessScene(scene, null);
            Assert.That(LightmapBakeMark.In(scene), Is.False, "no report: Play Mode, where the editor answers");
        }

        [Test]
        public void Release_gives_each_renderer_back_as_found_unless_something_rebound_it()
        {
            LightmapSet set = Room("Room", out Renderer first, out Renderer second, out _);
            var foundRect = new Vector4(0.25f, 0.25f, 0.5f, 0.5f);
            first.lightmapIndex = 5;
            first.lightmapScaleOffset = foundRect;
            set.gameObject.SetActive(true);
            Assert.That(Slot(first), Is.SameAs(_a));
            second.lightmapIndex = 7;  // something else rebinds it after the set did

            set.enabled = false;
            Assert.That(first.lightmapIndex, Is.EqualTo(5), "the index it found");
            Assert.That(first.lightmapScaleOffset, Is.EqualTo(foundRect), "the rect it found");
            Assert.That(second.lightmapIndex, Is.EqualTo(7), "a renderer rebound since is left as it is");
        }

        [Test]
        public void A_freed_slot_is_reused_the_free_tail_is_dropped_and_a_slot_not_the_sets_is_kept()
        {
            var foreign = new Texture2D(4, 4) { name = "Foreign" };
            try
            {
                LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = foreign } };
                LightmapSet one = Room("One", out Renderer oneFirst, out _, out _);
                one.gameObject.SetActive(true);
                Assert.That(LightmapSettings.lightmaps.Select(d => d.lightmapColor), Is.EqualTo(new[] { foreign, _a, _b }), "after the slot it found");

                var c = new Texture2D(4, 4) { name = "C_Lightmap" };
                try
                {
                    var room = new GameObject("Three");
                    room.SetActive(false);
                    Renderer only = Child(room, "Only");
                    var three = room.AddComponent<LightmapSet>();
                    three.maps.Add(new LightmapSet.Map { color = c });
                    three.receivers.Add(new LightmapSet.Receiver { renderer = only, map = 0 });
                    room.SetActive(true);
                    Assert.That(only.lightmapIndex, Is.EqualTo(3));

                    one.enabled = false;
                    Assert.That(LightmapSettings.lightmaps.Select(d => d.lightmapColor), Is.EqualTo(new Texture2D[] { foreign, null, null, c }),
                                "its slots freed, the others' kept where they are");
                    one.enabled = true;
                    Assert.That(LightmapSettings.lightmaps.Select(d => d.lightmapColor), Is.EqualTo(new[] { foreign, _a, _b, c }),
                                "the holes reused");
                    Assert.That(oneFirst.lightmapIndex, Is.EqualTo(1));

                    three.enabled = false;
                    Assert.That(LightmapSettings.lightmaps.Select(d => d.lightmapColor), Is.EqualTo(new[] { foreign, _a, _b }), "the free tail dropped");
                    one.enabled = false;
                    Assert.That(LightmapSettings.lightmaps.Select(d => d.lightmapColor), Is.EqualTo(new[] { foreign }),
                                "the slot that was never a set's stays");
                }
                finally
                {
                    Object.DestroyImmediate(c);
                }
            }
            finally
            {
                LightmapSettings.lightmaps = new LightmapData[0];
                Object.DestroyImmediate(foreign);
            }
        }

        [Test]
        public void Sets_sharing_a_map_release_in_either_order()
        {
            foreach (bool firstGoesFirst in new[] { true, false })
            {
                LightmapSet one = Room("One", out Renderer a, out _, out _);
                LightmapSet two = Room("Two", out Renderer b, out _, out _);
                one.gameObject.SetActive(true);
                two.gameObject.SetActive(true);
                LightmapSet early = firstGoesFirst ? one : two, late = firstGoesFirst ? two : one;
                Renderer kept = firstGoesFirst ? b : a;
                early.enabled = false;
                Assert.That(Slot(kept), Is.SameAs(_a), $"{late.name} still reads the shared map");
                late.enabled = false;
                Assert.That(LightmapSettings.lightmaps, Is.Empty, $"{early.name} then {late.name}: every slot given back");
                Object.DestroyImmediate(one.gameObject);
                Object.DestroyImmediate(two.gameObject);
            }
        }

        [Test]
        public void An_undone_edit_of_a_bound_sets_rect_binds_the_rect_it_had()
        {
            LightmapSet set = Room("Room", out _, out Renderer second, out _);
            set.gameObject.SetActive(true);
            var was = second.lightmapScaleOffset;
            var edited = new Vector4(0.1f, 0.1f, 0.9f, 0.9f);
            Undo.IncrementCurrentGroup();
            var so = new SerializedObject(set);  // an edit in the inspector
            so.FindProperty("receivers.Array.data[1].scaleOffset").vector4Value = edited;
            so.ApplyModifiedProperties();
            Assert.That(second.lightmapScaleOffset, Is.EqualTo(edited), "premise: the edit is bound (OnValidate)");
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
            Assert.That(set.receivers[1].scaleOffset, Is.EqualTo(was), "the field undone");
            Assert.That(second.lightmapScaleOffset, Is.EqualTo(was), "...and bound as it is now (OnValidate)");
            Assert.That(set.Bound, Is.True);
        }

        [Test]
        public void A_scene_change_that_rebuilt_the_lightmaps_binds_the_set_again()
        {
            // What a Single load does under a set kept by DontDestroyOnLoad (measured,
            // 6000.3.10f1): the array emptied, then filled with the new scene's bake.
            var bake = new Texture2D(4, 4) { name = "Loaded_Bake" };
            try
            {
                LightmapSet set = Room("Room", out Renderer first, out _, out _);
                set.gameObject.SetActive(true);
                LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = bake } };
                LightmapSettings.lightmapsMode = LightmapsMode.CombinedDirectional;
                Assert.That(Slot(first), Is.SameAs(bake), "premise: the set's renderer reads another bake's map");

                set.ScenesChanged();
                Assert.That(Slot(first), Is.SameAs(_a), "its own map again");
                Assert.That(LightmapSettings.lightmaps[0].lightmapColor, Is.SameAs(bake), "the loaded scene's slot kept");
                Assert.That(LightmapSettings.lightmapsMode, Is.EqualTo(LightmapsMode.NonDirectional), "its mode again");
            }
            finally
            {
                LightmapSettings.lightmaps = new LightmapData[0];
                Object.DestroyImmediate(bake);
            }
        }

        [Test]
        public void A_scaled_instance_sizes_its_probes_box_by_its_scale()
        {
            LightmapSet set = Room("Room", out _, out _, out ReflectionProbe probe);
            set.probeCenter = new Vector3(1f, 0.5f, -2f);
            set.probeSize = new Vector3(4f, 3f, 6f);
            set.transform.localScale = new Vector3(2f, 3f, -4f);
            set.gameObject.SetActive(true);
            Assert.That(probe.size, Is.EqualTo(new Vector3(8f, 9f, 24f)), "the box's size through the scale");
            Assert.That(probe.center, Is.EqualTo(new Vector3(2f, 1.5f, 8f)), "its centre too, a mirrored axis mirrored");

            set.enabled = false;
            set.transform.localScale = Vector3.one;
            set.enabled = true;
            Assert.That(probe.size, Is.EqualTo(set.probeSize), "unscaled: the box as imported");
            Assert.That(probe.center, Is.EqualTo(set.probeCenter));
        }

        // The active scene saved with a Lighting Data Asset of its own: a scene Unity's bake owns.
        static Scene Baked()
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, Folder + "/Baked.unity");
            var bake = new LightingDataAsset(scene);
            AssetDatabase.CreateAsset(bake, Folder + "/LightingData.asset");
            Lightmapping.SetLightingDataAssetForScene(scene, bake);
            Assert.That(Lightmapping.GetLightingDataAssetForScene(scene), Is.SameAs(bake));
            return scene;
        }
    }
}
