using System.IO;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

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
    /// probe's box. Bound, and only where told (both off by default), it also switches off the
    /// realtime lights that light its renderers and nothing else and makes its probe's light the
    /// scene's ambient light, each given back as it releases, as its switch is turned off in place,
    /// and around every save, so a saved scene holds none of it.
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

        [Test]
        public void A_set_added_by_hand_leaves_the_scenes_lights_and_environment_alone()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            RenderSettings.ambientMode = AmbientMode.Flat;
            LightmapSet set = Room("Room", out _, out _, out _);
            set.ambient = new float[27];
            set.ambient[0] = 1f;
            Assert.That((set.lightsOff, set.environment), Is.EqualTo((false, false)), "both rules off by default");
            set.gameObject.SetActive(true);
            Assert.That(set.Bound, Is.True);
            Assert.That(sun.enabled, Is.True);
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat));
        }

        [Test]
        public void Bound_it_switches_off_only_the_lights_that_light_its_renderers_alone_and_gives_them_back()
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            var lamp = new GameObject("Lamp").AddComponent<Light>();  // kept off the mapped renderers' layer
            LightmapSet set = Room("Room", out Renderer first, out _, out _);
            set.lightsOff = true;
            lamp.cullingMask = ~(1 << first.gameObject.layer);
            var panel = new GameObject("Panel").AddComponent<Light>();  // an area light is baked only
            panel.type = LightType.Rectangle;

            set.gameObject.SetActive(true);
            Assert.That(sun.enabled, Is.False, "the maps hold its light, and it lights nothing else");
            Assert.That(set.SwitchedOff, Is.EqualTo(new[] { sun }), "what it switched off, for the action to name");
            Assert.That(lamp.enabled, Is.True, "it never reached them");
            Assert.That(panel.enabled, Is.True);

            set.enabled = false;
            Assert.That(sun.enabled, Is.True, "given back");

            // Something no map lights, in the sun's reach: the sun lights it, so it is kept; a point light whose
            // range reaches the mapped renderers alone is still switched off.
            GameObject.CreatePrimitive(PrimitiveType.Cube).transform.position = new Vector3(50f, 0f, 0f);
            var near = new GameObject("Near").AddComponent<Light>();
            near.type = LightType.Point;
            near.range = 2f;
            set.enabled = true;
            Assert.That(sun.enabled, Is.True, "it lights the cube too: kept");
            Assert.That(near.enabled, Is.False, "its range reaches the mapped renderers alone");
            set.enabled = false;
            Assert.That(near.enabled, Is.True, "given back");

            set.lightsOff = false;
            set.enabled = true;
            Assert.That(near.enabled, Is.True, "told to keep the scene's lights");
            LogAssert.NoUnexpectedReceived();  // a bind says nothing: the action that binds names what it switched off
        }

        [Test]
        public void A_saved_scene_holds_nothing_its_binding_changed_and_it_binds_again_once_saved()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            var lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 2f;
            LightmapSet set = Room("Room", out _, out _, out _);
            set.lightsOff = true;
            set.environment = true;
            set.ambient = new float[27];
            set.ambient[0] = 1f;
            set.gameObject.SetActive(true);
            Assert.That(lamp.enabled, Is.False);
            Scene scene = SceneManager.GetActiveScene();
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string path = Folder + "/Saved.unity";

            Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
            Assert.That(Saved(path, 108, "m_Enabled"), Is.EqualTo("1"), "the lamp saved as the scene had it");
            Assert.That(Saved(path, 104, "m_AmbientMode"), Is.EqualTo(((int)AmbientMode.Flat).ToString()), "the scene's own environment saved");
            Assert.That(set.Bound, Is.True, "bound again once saved");
            Assert.That(lamp.enabled, Is.False, "its rule holds again");
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Custom));
        }

        // The value of field in the first object of Unity class id classId in the scene file at path.
        static string Saved(string path, int classId, string field)
        {
            string text = File.ReadAllText(path).Replace("\r\n", "\n");
            foreach (string document in text.Split(new[] { "--- !u!" }, System.StringSplitOptions.None))
            {
                if (!document.StartsWith(classId + " ", System.StringComparison.Ordinal)) continue;
                var match = System.Text.RegularExpressions.Regex.Match(document, $@"\n\s*{field}: (\S+)");
                return match.Success ? match.Groups[1].Value : null;
            }
            return null;
        }

        [Test]
        public void A_rule_switched_off_on_a_bound_set_gives_back_what_it_holds_at_once()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            var lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 2f;
            LightmapSet set = Room("Room", out _, out _, out _);
            set.lightsOff = true;
            set.environment = true;
            set.ambient = new float[27];
            set.ambient[0] = 1f;
            set.gameObject.SetActive(true);
            Assert.That((lamp.enabled, RenderSettings.ambientMode), Is.EqualTo((false, AmbientMode.Custom)), "premise: both rules hold");

            var so = new SerializedObject(set);  // an edit in the inspector, the set still bound
            so.FindProperty(nameof(LightmapSet.lightsOff)).boolValue = false;
            so.ApplyModifiedProperties();
            Assert.That(set.Bound, Is.True);
            Assert.That(lamp.enabled, Is.True, "Lights Off cleared: the lamp is back at once");
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Custom), "the other rule still holds");

            so.Update();
            so.FindProperty(nameof(LightmapSet.environment)).boolValue = false;
            so.ApplyModifiedProperties();
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat), "Environment cleared: the scene's own, at once");

            so.Update();
            so.FindProperty(nameof(LightmapSet.lightsOff)).boolValue = true;
            so.ApplyModifiedProperties();
            Assert.That(lamp.enabled, Is.False, "ticked again: it holds again");
        }

        [Test]
        public void A_set_that_stands_down_in_place_gives_its_rules_back()
        {
            var lamp = new GameObject("Lamp").AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.range = 2f;
            LightmapSet set = Room("Room", out _, out _, out _);
            set.lightsOff = true;
            set.gameObject.SetActive(true);
            Assert.That(lamp.enabled, Is.False, "premise");
            Baked();        // Unity's bake owns the scene now
            set.Refresh();  // what the editor runs as an owner changes
            Assert.That(set.StandsDown && !set.Bound, Is.True);
            Assert.That(lamp.enabled, Is.True, "standing down, it holds nothing of the scene's");
        }

        [Test]
        public void Bound_it_lights_the_scene_by_its_probe_and_gives_the_environment_back()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientIntensity = 0.5f;
            LightmapSet set = Room("Room", out _, out _, out _);
            var room = new SphericalHarmonicsL2();
            room.AddAmbientLight(new Color(2f, 1.5f, 1f));
            set.ambient = new float[27];
            for (int channel = 0; channel < 3; channel++)
                for (int i = 0; i < 9; i++) set.ambient[channel * 9 + i] = room[channel, i];
            set.environment = true;

            set.gameObject.SetActive(true);
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Custom));
            Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(1f));
            var lit = new Color[1];
            RenderSettings.ambientProbe.Evaluate(new[] { Vector3.up }, lit);
            Assert.That(lit[0].r, Is.EqualTo(2f).Within(1e-3f), "what no map lights is lit by the room");
            Assert.That(lit[0].b, Is.EqualTo(1f).Within(1e-3f));

            set.enabled = false;
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat), "given back");
            Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(0.5f));

            set.environment = false;
            set.enabled = true;
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat), "told to leave the scene's environment");

            set.enabled = false;
            set.environment = true;
            set.enabled = true;
            RenderSettings.ambientMode = AmbientMode.Trilight;  // a person's change while it is bound
            set.enabled = false;
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight), "theirs stands");
        }

        // set told to light the scene by a room of colour, at intensity.
        static LightmapSet Lights(LightmapSet set, Color colour, float intensity)
        {
            var room = new SphericalHarmonicsL2();
            room.AddAmbientLight(colour);
            set.ambient = new float[27];
            for (int channel = 0; channel < 3; channel++)
                for (int i = 0; i < 9; i++) set.ambient[channel * 9 + i] = room[channel, i];
            set.environment = true;
            set.environmentIntensity = intensity;
            return set;
        }

        // Two sets lit one scene's environment and each took what stood there as it bound: the second the first's light,
        // so the scene kept the first's whenever the second gave back last.
        [Test]
        public void Two_sets_lighting_one_scene_give_it_its_own_back_whichever_lets_go_first()
        {
            foreach (bool firstGoesFirst in new[] { true, false })
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientIntensity = 0.5f;
                LightmapSet one = Lights(Room("One", out _, out _, out _), new Color(2f, 1f, 1f), 1f);
                LightmapSet two = Lights(Room("Two", out _, out _, out _), new Color(1f, 2f, 1f), 0.8f);
                one.gameObject.SetActive(true);
                two.gameObject.SetActive(true);
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Custom), "premise: lit by a set");
                (firstGoesFirst ? one : two).enabled = false;
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Custom), "the other still lights it");
                (firstGoesFirst ? two : one).enabled = false;
                Assert.That((RenderSettings.ambientMode, RenderSettings.ambientIntensity), Is.EqualTo((AmbientMode.Flat, 0.5f)),
                            $"the scene's own back, the {(firstGoesFirst ? "first" : "second")} set gone first");
                Object.DestroyImmediate(one.gameObject);
                Object.DestroyImmediate(two.gameObject);
            }
        }

        // A save asks each set to give back in turn: the scene saved the first set's light, the second's "found".
        [Test]
        public void A_save_with_two_sets_lighting_one_scene_holds_its_own_environment_and_the_same_light_shows_after()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            LightmapSet one = Lights(Room("One", out _, out _, out _), new Color(2f, 1f, 1f), 1f);
            LightmapSet two = Lights(Room("Two", out _, out _, out _), new Color(1f, 2f, 1f), 0.8f);
            one.gameObject.SetActive(true);
            two.gameObject.SetActive(true);
            SphericalHarmonicsL2 shown = RenderSettings.ambientProbe;
            float level = RenderSettings.ambientIntensity;

            // The kernel's save hook, in the order the sets bound (and taken again the last first).
            ((ISaveSuspended)one).Suspend();
            ((ISaveSuspended)two).Suspend();
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat), "what the save writes: the scene's own");
            ((ISaveSuspended)two).Resume();
            ((ISaveSuspended)one).Resume();
            Assert.That(RenderSettings.ambientProbe == shown && Mathf.Approximately(RenderSettings.ambientIntensity, level), Is.True,
                        "the light that showed before the save shows after it");

            Scene scene = SceneManager.GetActiveScene();
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            string path = Folder + "/TwoSets.unity";
            Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
            Assert.That(Saved(path, 104, "m_AmbientMode"), Is.EqualTo(((int)AmbientMode.Flat).ToString()), "the saved file holds the scene's own");
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Custom), "lit again once saved");
        }

        // A set lit the scene active as it bound, and on an active-scene change wrote its light into the new one, the
        // first keeping it and the new one given the first's own as the set let go.
        [Test]
        public void An_active_scene_change_gives_the_scene_left_its_own_back_and_lights_the_one_drawn()
        {
            Scene first = SceneManager.GetActiveScene();
            RenderSettings.ambientMode = AmbientMode.Flat;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            Assert.That(EditorSceneManager.SaveScene(first, Folder + "/First.unity"), Is.True, "saved: a scene is added beside a saved one alone");
            Scene second = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(second);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            SceneManager.SetActiveScene(first);
            LightmapSet set = Lights(Room("Room", out _, out _, out _), new Color(2f, 1f, 1f), 1f);
            set.gameObject.SetActive(true);
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Custom), "premise: it lights the active scene");

            SceneManager.SetActiveScene(second);
            set.ScenesChanged();  // what a player's activeSceneChanged runs
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Custom), "the scene drawn now is lit by it");
            SceneManager.SetActiveScene(first);
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Flat), "the scene it left has its own back");
            SceneManager.SetActiveScene(second);

            set.enabled = false;
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight), "letting go, the new scene's own, never the first's");
            Assert.That(SceneManager.GetActiveScene() == second, Is.True, "the active scene as it was");
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
