using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// <see cref="TaskSettings"/>: the defaults assume no one task. A task built by them ships no debug tools,
    /// leaves a scene's own rig as it is (one that travels), hides none of the delivery's meshes, and gets a placard only
    /// where a step shows a page on it; a task's own settings ask for each, and name what the build makes.
    /// </summary>
    public class TaskSettingsTests
    {
        GameObject _root;
        Manifest _manifest;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _root = new GameObject("cabinet_2026");
            var cabinet = new GameObject("CABINET").transform;
            cabinet.SetParent(_root.transform);
            new GameObject("DOOR_GRP").transform.SetParent(cabinet);
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);  // a project's own screen, named as anchors often are
            screen.name = "UI_SCREEN";
            screen.transform.SetParent(cabinet);
            _manifest = ScriptableObject.CreateInstance<Manifest>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_manifest);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static SheetPlan.Shape.Payload Plan(string steps)
        {
            var plan = SheetPlan.Read("{\"version\": 2, \"steps\": [" + steps + "]}", "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            return plan;
        }

        const string Highlight = "{\"id\": \"A01\", \"fields\": {\"voice\": \"Look at the door.\"}, \"actions\": [" +
                                 "{\"behavior\": \"highlight\", \"text\": \"Highlight the door\", \"targets\": [\"CABINET|DOOR_GRP\"]}]}";

        [Test]
        public void The_defaults_ship_no_debug_tools_and_leave_a_rig_that_travels_and_the_deliverys_meshes_alone()
        {
            var defaults = ScriptableObject.CreateInstance<TaskSettings>();
            Assert.That((defaults.debugTools, defaults.rig, defaults.anchorPattern, defaults.pages),
                        Is.EqualTo((false, TaskRig.Desktop, "", PlacardPages.Needed)));
            Object.DestroyImmediate(defaults);

            // A headset's rig, which the scene declares by its travelling (a Traveler on its origin): its camera a child of it.
            var origin = new GameObject("XR Origin", typeof(Viewpoints.Traveler));
            var offset = new GameObject("Camera Offset").transform;
            offset.SetParent(origin.transform);
            var camera = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" }.GetComponent<Camera>();
            camera.transform.SetParent(offset);
            float near = camera.nearClipPlane;

            var live = new OpContext();
            ManifestsActions.Build(Plan(Highlight), new ManifestScene(_root, new AnimationClip[0]) { Manifest = _manifest }, live);
            Assert.That(Object.FindObjectsByType<Viewpoints.Traveler>(FindObjectsSortMode.None).Single().gameObject, Is.SameAs(origin), "no rig given");
            Assert.That(camera.transform.childCount == 0 && Mathf.Approximately(camera.nearClipPlane, near), "the headset's camera as it was");
            Assert.That(Object.FindAnyObjectByType<Steps.SequenceDebugCommands>(), Is.Null, "no debug tools");
            Assert.That(_root.transform.Find("CABINET/UI_SCREEN").GetComponent<Renderer>().enabled, Is.True, "no mesh hidden");
            Assert.That(GameObject.Find("Placard"), Is.Null, "no step shows a page: no placard");
        }

        [Test]
        public void A_main_camera_that_does_not_travel_is_made_the_user_wherever_it_sits()
        {
            // A camera under a grouping root (a dolly, the scene's Cameras): no rig of its own, whatever its parent.
            var group = new GameObject("Cameras");
            var camera = new GameObject("Main Camera", typeof(Camera)) { tag = "MainCamera" }.GetComponent<Camera>();
            camera.transform.SetParent(group.transform);
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            try
            {
                var live = new OpContext();
                ManifestsActions.Build(Plan(Highlight), new ManifestScene(_root, new AnimationClip[0]).Use(settings), live);
                Assert.That(camera.GetComponent<Viewpoints.Traveler>(), Is.Not.Null, "the steps' viewpoints move it");
                Assert.That(Mathf.Approximately(camera.nearClipPlane, settings.nearClip), "and it sees what the hand holds");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void A_pattern_setting_that_does_not_parse_is_said_once_naming_it_and_the_build_goes_on()
        {
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            try
            {
                Assert.That(settings.picture.anchor, Is.Empty, "no screen named by default: a task names its own");
                settings.anchorPattern = "UI_(";
                settings.picture.anchor = "UI_(";  // the same text in two settings: each is said
                settings.rig = TaskRig.None;
                var scene = new ManifestScene(_root, new AnimationClip[0]).Use(settings);
                var live = new OpContext();
                ManifestsActions.Build(Plan(Highlight), scene, live);
                Assert.That(TaskKit.PictureAnchor(scene, live), Is.Null, "a pattern that does not parse matches nothing");
                Assert.That(live.Issues.Where(i => i.code == TaskKit.PatternCode).Select(i => i.message.Split(' ')[2]),
                            Is.EqualTo(new[] { "anchorPattern", "picture.anchor" }), "each said once, by its name");
                Assert.That(_root.transform.Find("CABINET/UI_SCREEN").GetComponent<Renderer>().enabled, Is.True, "no mesh hidden by it");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void What_the_task_sets_once_is_offered_on_no_row_and_what_a_row_reads_is()
        {
            // The window offered these on every row and object (Settings for 'X'), and the build read them for the whole task
            // alone: a value set on one row did nothing. The task's own, one for every row, are set in its Setup.
            string[] Offered(string behavior) => ManifestBehaviors.Mark(behavior)?.Settings ?? new string[0];
            Assert.That(Offered("travel"), Is.EquivalentTo(new[] { "travel.when", "travel.ring" }),
                        "its word's own; where a spot is searched for (standOff, viewAngle) is the task's, and a person moves the spot itself");
            Assert.That(Offered("advance"), Is.Empty, "how long a step runs: the task's pace");
            Assert.That(ManifestBehaviors.SettingsOf("advance"), Is.Null, "so it offers no settings at all");
            Assert.That(Offered("answer"), Is.Empty, "the key pad's keys: the task's");
            Assert.That(Offered("picture"), Is.EquivalentTo(new[] { "picture.until", "picture.fieldOfView" }),
                        "where the picture comes up (its style, its anchor) is the task's one screen; its span and its own camera's angle are its row's");
            Assert.That(Offered("highlight"), Is.EqualTo(new[] { "highlight.style", "highlight.color", "highlight.loop", "highlight.cue" }),
                        "a word with a group of its own offers each of its fields, in their order");
            Assert.That(ManifestBehaviors.Mark("use").RowSettings, Is.EquivalentTo(new[] { "use.speed", "use.start", "use.stop" }),
                        "its take's speed and stretch, its row's as a whole, named by their paths");
            Assert.That(ManifestBehaviors.Mark("button").Suffix, Is.EqualTo(new[] { "button.when" }), "and what its chip says");
        }

        [Test]
        public void A_rows_settings_by_the_sheets_names_go_over_its_words_own_and_an_objects_own_over_those()
        {
            // A sheet's row says "size=0.3" on an indicate row: the kit reads indicate.size; the window keeps an object's own under that name.
            var row = new SheetPlan.Shape.PlanAction
            {
                behavior = "indicate", text = "Arrow points at the door", targets = new List<string> { "CABINET|DOOR_GRP" },
                settings = new Dictionary<string, object> { ["size"] = "0.3", ["style"] = "solid" },
            };
            var scene = new ManifestScene(_root, new AnimationClip[0]) { Manifest = _manifest };
            TaskSettings own = scene.Settings<TaskSettings>("A01", "CABINET|DOOR_GRP", row);
            Assert.That((own.indicate.size, own.indicate.style), Is.EqualTo((0.3f, IndicateStyle.Solid)), "the row's, under its word's own settings");
            Assert.That(own.highlight.color, Is.EqualTo(scene.Settings<TaskSettings>().highlight.color), "another word's stay the task's");
            Assert.That(ManifestBehaviors.SettingName("indicate", "size"), Is.EqualTo("indicate.size"), "the name the row's setting has");
            Assert.That(ManifestBehaviors.SettingName("take", "size"), Is.EqualTo("size"), "a word with no group of its own: as it is");

            Assert.That(ManifestsActions.SetTune(_manifest, "A01", "CABINET|DOOR_GRP", "indicate", "indicate.size", "0.5"), Is.True);
            Assert.That(ManifestsActions.SetTune(_manifest, "A01", "CABINET|DOOR_GRP", "indicate", "size", "0.5"), Is.False,
                        "an object's own value is kept by its setting's whole name");
            var tuned = new ManifestScene(_root, new AnimationClip[0]) { Manifest = _manifest };
            Assert.That(tuned.Settings<TaskSettings>("A01", "CABINET|DOOR_GRP", row).indicate.size, Is.EqualTo(0.5f), "an object's own over its row's");
        }

        [Test]
        public void A_rows_words_read_by_the_tasks_settings()
        {
            var defaults = ScriptableObject.CreateInstance<TaskSettings>();
            try
            {
                Assert.That(Refusals.Readable("KEY_GRP", defaults.nameSuffixes), Is.EqualTo("key"), "a group's suffix is not said");
                Assert.That(Refusals.Readable("KEY_PART", defaults.nameSuffixes), Is.EqualTo("key part"));
                Assert.That(Refusals.Readable("KEY_PART", new[] { "part" }), Is.EqualTo("key"), "a project's own suffix");
                Assert.That(Lists.Items("Door open\nBolt out"), Is.EqualTo(new[] { "Door open", "Bolt out" }), "a list's Label: an item a line");
                Assert.That(Lists.Items("'Door open', Bolt out; Lid on"), Is.EqualTo(new[] { "Door open", "Bolt out", "Lid on" }), "or split at commas and semicolons");
            }
            finally
            {
                Object.DestroyImmediate(defaults);
            }
        }

        [Test]
        public void A_step_gets_a_page_where_it_has_something_to_show_or_the_task_asks_for_every_step()
        {
            string steps = Highlight + "," +
                           "{\"id\": \"A02\", \"fields\": {\"voice\": \"Now open it.\", \"placard\": \"Open the door\"}, \"actions\": []}";
            ManifestsActions.Build(Plan(steps), new ManifestScene(_root, new AnimationClip[0]), new OpContext());
            Transform placard = GameObject.Find("Placard").transform;
            Assert.That(placard.Find("A02").GetComponent<Panels.PlacardPage>().text, Is.EqualTo("Open the door"), "its placard text");
            Assert.That(placard.Find("A01"), Is.Null, "a voice line alone shows no page");

            var every = ScriptableObject.CreateInstance<TaskSettings>();
            every.pages = PlacardPages.EveryStep;
            every.names.placard = "Board";
            try
            {
                var plan = Plan(steps);
                ManifestsActions.Build(plan, new ManifestScene(_root, new AnimationClip[0]).Use(every), new OpContext());
                Transform board = GameObject.Find(TaskKit.PlacardName(plan, every)).transform;
                Assert.That(board.name, Is.EqualTo("Board"), "named as the task says");
                Assert.That(board.Find("A01").GetComponent<Panels.PlacardPage>().text, Is.EqualTo("Look at the door."), "every step a page, its voice line");
            }
            finally
            {
                Object.DestroyImmediate(every);
            }
        }
    }
}
