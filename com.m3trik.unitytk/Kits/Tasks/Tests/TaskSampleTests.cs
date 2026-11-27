using M3trik.Unitytk.Manifests;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Panels;
using M3trik.Unitytk.Steps;
using M3trik.Unitytk.Viewpoints;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools.Utils;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// The Tasks sample, imported as the Package Manager imports it: a cabinet task (a
    /// sheet in a studio's layout, the life-size delivery its steps play and the plan read
    /// from them, written by the workspace's sample sync). It assesses against its delivery,
    /// every node and take found and every row built; its build plans as it runs, is one undo
    /// step, and a second build changes nothing; and the demo recipe's scene, the one a person
    /// plays, stands the user at the front of the cabinet and plays through as its sheet says,
    /// step by step, its choices pressed on the placard.
    /// The project these run in imports every sample of the package, so a sample that is not
    /// there fails: it is never skipped.
    /// </summary>
    public class TaskSampleTests
    {
        const string Package = "com.m3trik.unitytk";
        const string SampleName = "Tasks";

        GameObject _instance;
        SheetPlan.Shape.Payload _plan;
        Sequence _sequence;
        ClipPlayer _player;
        Hand _hand;
        Traveler _traveler;
        Placard _placard;

        [SetUp]
        public void SetUp()
        {
            _plan = SheetPlan.Read(AssetDatabase.LoadAssetAtPath<TextAsset>(Folder() + "/cabinet.plan.json").text,
                                      "cabinet.plan.json", out string refusal);
            Assert.That(refusal, Is.Null);
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null) _player.Clear();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        // ------------------------------------------------------------ the sample
        static string Folder()
        {
            Sample sample = Sample.FindByPackage(Package, null).FirstOrDefault(s => s.displayName == SampleName);
            Assert.That(sample.displayName, Is.EqualTo(SampleName),
                        "the package lists no Tasks sample: its package.json's samples are written by the workspace's sample sync");
            Assert.That(sample.isImported, Is.True,
                        $"the Tasks sample is not imported at {sample.importPath}: import it from the Package Manager's " +
                        "Samples tab (the owned project's builder copies every sample in)");
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return "Assets" + full.Substring(assets.Length);
        }

        // The sample's demo recipe, run on its folder: the public static method of the sample's own editor assembly (named
        // by the assembly definition its import holds) that builds a scene from a folder and a context. Found by what it
        // is, never by a type's name in a string: the sample's assembly is optional, so no test assembly references it.
        static Scene Demo(string folder, OpContext ctx = null)
        {
            string[] found = AssetDatabase.FindAssets("t:AssemblyDefinitionAsset", new[] { folder });
            Assert.That(found.Length, Is.EqualTo(1), $"the Tasks sample at {folder} holds one editor assembly");
            var asmdef = AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(AssetDatabase.GUIDToAssetPath(found[0]));
            string name = JsonUtility.FromJson<AsmdefName>(asmdef.text).name;
            Assembly editor = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);
            Assert.That(editor, Is.Not.Null, $"the sample's editor assembly {name} is missing: it did not compile");
            MethodInfo build = editor.GetTypes().SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
                                     .SingleOrDefault(m => m.ReturnType == typeof(Scene)
                                                           && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(string), typeof(OpContext) }));
            Assert.That(build, Is.Not.Null, $"{name} has no recipe building a scene from a folder (Build(string folder, OpContext ctx))");
            return (Scene)build.Invoke(null, new object[] { folder, ctx ?? new OpContext() });
        }

        [Serializable]
        sealed class AsmdefName
        {
            public string name = "";
        }

        static GameObject Model()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Folder() + "/cabinet.fbx");
            Assert.That(model, Is.Not.Null, "the task's delivery imported");
            return model;
        }

        GameObject Placed() => _instance = (GameObject)PrefabUtility.InstantiatePrefab(Model());

        static void Live(UnityEventBase when)
        {
            for (int i = 0; i < when.GetPersistentEventCount(); i++)
                when.SetPersistentListenerState(i, UnityEventCallState.EditorAndRuntime);
        }

        Transform Node(string name) => _instance.GetComponentsInChildren<Transform>(true).Single(t => t.name == name);

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z).normalized;

        // The user's eyes stand over the viewpoint and face the cabinet over the ground.
        void Standing(string name)
        {
            Transform view = Node(name), eyes = _traveler.Head;
            Assert.That(new Vector2(eyes.position.x, eyes.position.z), Is.EqualTo(new Vector2(view.position.x, view.position.z)).Using(Vector2EqualityComparer.Instance),
                        $"the eyes over {name}");
            Assert.That(Vector3.Dot(Flat(eyes.forward), Flat(Node("CABINET").position - view.position)), Is.GreaterThan(0.99f), $"{name} faces the cabinet");
        }

        // The clips play on for seconds, the tools they wait for there already (a player moves them each frame).
        void Play(float seconds)
        {
            foreach (Holdable tool in _instance.GetComponentsInChildren<Holdable>(true)) tool.Arrive();
            _player.Advance(seconds);
        }

        // The step brings the user to the viewpoint as it starts: its TravelTo enabled (it acts on
        // enable, in a player, so its travel is run here).
        void Brought(string name)
        {
            var travel = Node(name).GetComponent<TravelTo>();
            Assert.That(travel.enabled, $"{_sequence.Current.id} brings the user to {name}");
            Assert.That(travel.TryTravel(), Is.True);
            Standing(name);
        }

        void Select(string name) => Assert.That(Node(name).GetComponent<Target>().Select(_hand), Is.True, $"select {name}");

        // Whether cue shows now. One at what the user acts on is switched by its TargetCue, which in a player comes on
        // with its step and looks each frame: run here.
        readonly System.Collections.Generic.HashSet<TargetCue> _watching = new System.Collections.Generic.HashSet<TargetCue>();

        bool Shows(Behaviour cue)
        {
            foreach (TargetCue each in _instance.GetComponentsInChildren<TargetCue>(true))
            {
                if (each.cue != cue) continue;
                if (each.enabled && _watching.Add(each)) each.Watch();
                else if (each.enabled) each.Refresh();
                else if (_watching.Remove(each)) each.Rest();
            }
            return cue.enabled;
        }

        // The step's page is on the placard as it runs (it shows itself on enable, in a player, so
        // it is shown here), and its button is pressed.
        PlacardPage Page() => _placard.transform.Find(_sequence.Current.id).GetComponent<PlacardPage>();

        void Press(string label)
        {
            string step = _sequence.Current.id;
            PlacardPage page = Page();
            Assert.That(page.enabled, $"{step} shows its page while it runs");
            Assert.That(page.Show(), Is.True);
            Assert.That(_placard.Choose(label), Is.True, $"{step}'s page offers '{label}'");
        }

        // ------------------------------------------------------------ the tests
        [Test]
        public void The_task_assesses_against_its_delivery_every_node_and_take_found()
        {
            ManifestAssessment assessment = ManifestsActions.Assess(_plan, new ManifestScene(Placed()));
            Assert.That(assessment.Count(ManifestAssessment.State.Unresolved), Is.Zero,
                        string.Join("\n", assessment.rows.Where(r => r.problems.Count > 0).Select(r => $"{r.step}: {string.Join("; ", r.problems)}")));
            Assert.That(assessment.Unsupported().Keys, Is.Empty,
                        "every row is built: the intro's description is a note, which builds nothing");
        }

        [Test]
        public void The_task_builds_as_planned_in_one_undo_step_and_a_second_build_changes_nothing()
        {
            GameObject instance = Placed();
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(_plan, new ManifestScene(instance), dry);
            OpContext.RunMenu("Build Tasks", ctx =>
            {
                ManifestsActions.Build(_plan, new ManifestScene(instance), ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(_plan, new ManifestScene(instance), again);
            Assert.That(again.Changes, Is.Empty);
            Undo.PerformUndo();
            Assert.That(GameObject.Find(TaskKit.SequenceName(_plan)) == null && instance.GetComponent<ClipPlayer>() == null);
        }

        [Test]
        public void The_demo_recipe_plans_what_it_builds_and_a_second_run_is_one_undo_step()
        {
            string folder = Folder();
            Demo(folder);  // a first run: the task's manifest made
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);  // what a live run builds in
            var dry = new OpContext(dryRun: true);
            Assert.That(Demo(folder, dry).IsValid(), Is.False, "a dry run opens and saves no scene");
            Assert.That(SceneRoots.Find(TaskKit.SequenceName(_plan)) == null && SceneRoots.Find("FLOOR") == null, "and makes nothing");
            OpContext live = null;
            OpContext.RunMenu("Task Demo", ctx =>
            {
                live = ctx;
                Demo(folder, ctx);
            });
            // Each change, by its object; not every word: a spot solved against the floor the recipe makes (a placard's
            // place) is solved in the dry run without it, the floor only planned there (BACKLOG: a dry run's solved spots).
            Assert.That(live.Changes.Select(c => (c.kind, c.target)).ToList(), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target)).ToList()),
                        "the dry run plans each step the live run takes");
            Undo.PerformUndo();
            Assert.That(SceneRoots.Find(TaskKit.SequenceName(_plan)) == null && SceneRoots.Find("FLOOR") == null, "one undo takes the build back");
        }

        [Test]
        public void The_demo_scene_stands_the_user_at_the_cabinet_and_plays_through_as_its_sheet_says()
        {
            string folder = Folder();
            Scene scene = Demo(folder);
            Assert.That(scene.path, Is.EqualTo(folder + "/TaskDemo.unity"), "saved beside the task");

            // The scene: the task, its task, and the desktop rig.
            GameObject model = Model();
            _instance = scene.GetRootGameObjects().Single(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == model);
            _sequence = scene.GetRootGameObjects().Single(go => go.name == TaskKit.SequenceName(_plan)).GetComponent<Sequence>();
            _player = _instance.GetComponent<ClipPlayer>();
            _traveler = Object.FindObjectsByType<Traveler>(FindObjectsSortMode.None).Single();
            Camera camera = _traveler.Head.GetComponent<Camera>();
            Assert.That(camera, Is.SameAs(Camera.main), "the user's eyes are the scene's camera");
            Assert.That(_traveler.transform, Is.SameAs(camera.transform), "which the build made the user's rig");
            Assert.That(camera.transform.position.y - Node("VIEW_FRONT").position.y, Is.EqualTo(1.6f).Within(1e-4f), "a standing user's eyes");
            _hand = Object.FindObjectsByType<Hand>(FindObjectsSortMode.None).Single();
            Assert.That(_hand.transform.parent, Is.SameAs(camera.transform), "the hand in the camera's view");
            var debug = Object.FindObjectsByType<SequenceDebugCommands>(FindObjectsSortMode.None).Single();
            Assert.That(debug.GetComponent<M3trik.Unitytk.Panels.DebugPanel>(), Is.Not.Null, "the task's debug tools, on a panel");
            Assert.That((debug.Sequence, debug.Autopilot.enabled), Is.EqualTo((_sequence, false)), "skipping in the task's sequence; the autopilot off");
            // Before the front viewpoint, facing the way it does: where the task first brings the user.
            Transform front = Node("VIEW_FRONT");
            Vector3 start = front.position - front.forward * 2f;
            Assert.That(new Vector2(camera.transform.position.x, camera.transform.position.z),
                        Is.EqualTo(new Vector2(start.x, start.z)).Using(Vector2EqualityComparer.Instance), "the user starts before the front view");
            Assert.That(Vector3.Dot(Flat(camera.transform.forward), Flat(front.forward)), Is.GreaterThan(0.99f));
            // The tools rest on the table, as the delivery holds them: Unity poses a model from
            // its first take's start, and no take starts a part away from its rest.
            Bounds table = Node("TABLE").GetComponent<Renderer>().bounds;
            foreach (string tool in new[] { "KEY_GEO", "WRENCH_GEO" })
            {
                Bounds at = Node(tool).GetComponent<Renderer>().bounds;
                Assert.That(at.min.y - table.max.y, Is.InRange(0f, 0.01f), $"{tool} lies on the table");
                Assert.That(table.Contains(new Vector3(at.center.x, table.center.y, at.center.z)), $"{tool} over the table");
            }

            // Played as a user plays it, the persistent calls running here as in a player.
            foreach (Target target in _instance.GetComponentsInChildren<Target>(true)) Live(target.onSelected);
            Live(_player.onFinished);
            _placard = scene.GetRootGameObjects().Single(go => go.name == TaskKit.PlacardName(_plan)).GetComponent<Placard>();
            Live(_placard.onChosen);
            Transform key = Node("KEY_GRP");

            _sequence.Begin();
            Assert.That(_sequence.Current.id, Is.EqualTo("IN_01"), "IN_01 runs long enough to be read");
            _sequence.Tick(60f);  // a step going on by itself runs long enough to be read
            Assert.That(_sequence.Current.id, Is.EqualTo("IN_02"), "IN_01 goes on by itself; IN_02 waits for Begin");
            Assert.That((Page().heading, Page().text), Is.EqualTo(("Begin", "Select 'Begin' to start.")), "its title and its voice line");
            Press("Begin");
            Assert.That(_placard.Heading, Is.EqualTo("Begin"));

            Assert.That(_sequence.Current.id, Is.EqualTo("A01"));
            // The key first, then the latches: a cue shows while the user can act on what it points out.
            var glow = Node("CABINET_LATCHES_GRP").GetComponent<M3trik.Unitytk.RenderEffects.Highlight>();
            var arrow = key.GetComponentInChildren<M3trik.Unitytk.Indicators.Indicator>(true);
            Assert.That(Shows(arrow), "A01 points at the key");
            Assert.That(Shows(glow), Is.False, "and the latches wait: nothing can be done at them before the key is in hand");
            Brought("VIEW_FRONT");
            Assert.That(Node("LATCH_UPPER_GRP").GetComponent<Target>().Select(_hand), Is.False, "the latch wants the key in hand");
            Select("KEY_GRP");
            Assert.That(Shows(arrow), Is.False, "taken: no arrow rides the key into the hand");
            Assert.That(Shows(glow), "the key is in hand: A01 highlights the latches");
            Select("LATCH_UPPER_GRP");
            Play(2f);
            Assert.That(Shows(glow), "one latch is still to do");
            Select("LATCH_LOWER_GRP");
            Assert.That(Shows(glow), Is.False, "both done: the glow goes as the last one is worked");
            Play(2f);
            _sequence.Tick(60f);  // a step going on by itself runs long enough to be read
            Assert.That(_sequence.Current.id, Is.EqualTo("A02"), "both latch clips ended: A01 went on");
            // A01's end sends the key home: its SendHome enabled (it acts on enable, in a player).
            Assert.That(key.GetComponent<SendHome>().enabled, Is.True, "A01's end sends the key back to the table");
            Assert.That(Node("DOOR_GRP").GetComponent<Target>().enabled, Is.True);
            Assert.That(Node("LATCH_UPPER_GRP").GetComponent<Target>().enabled, Is.False, "A01's targets are A01's alone");

            Assert.That(Page().text, Is.EqualTo("Select the door to open it"), "A02's placard row says it, in place of its voice line");
            Select("DOOR_GRP");
            Play(2f);
            _sequence.Tick(60f);  // a step going on by itself runs long enough to be read
            Assert.That(_sequence.Current.id, Is.EqualTo("B01"));
            Brought("VIEW_BACK");
            Assert.That(Node("VIEW_FRONT").GetComponent<TravelTo>().enabled, Is.False, "A01's viewpoint is A01's alone");
            Select("WRENCH_GRP");
            Select("BOLT_GRP");
            Play(2f);
            Assert.That(_player.IsPlaying("B01_2"), "the lid's clip follows the bolt's");
            Play(2f);
            _sequence.Tick(60f);  // a step going on by itself runs long enough to be read
            Assert.That(_sequence.Current.id, Is.EqualTo("B02"));
            Assert.That(Node("BOLT_GRP").GetComponents<M3trik.Unitytk.RenderEffects.FadeTo>().Single().enabled, "the bolt fades away as B01 ends");
            Assert.That(Shows(Node("CABINET_LATCHES_GRP").GetComponent<M3trik.Unitytk.RenderEffects.Highlight>()), Is.False, "A01's glow is A01's alone");

            Select("BUTTON_GRP");
            _sequence.Tick(60f);  // a step going on by itself runs long enough to be read
            Assert.That(_sequence.Current.id, Is.EqualTo("B03"), "the button's selection completed B02");
            Assert.That(Page().choices, Is.EqualTo(new[] { "YES", "NO" }), "the buttons its sheet names, a row saying what each does");
            Press("NO");
            Assert.That(_sequence.Current.id, Is.EqualTo("B03_5"));
            Press("Try Again");
            Press("YES");
            Assert.That(_sequence.Current.id, Is.EqualTo("B04"));
            Press("Exit");
            Assert.That(_sequence.Finished, Is.True, "Exit ends the task");
        }

        [Test]
        public void The_autopilot_walks_the_demo_task_to_its_end_and_to_a_step()
        {
            string folder = Folder();
            Scene scene = Demo(folder);
            GameObject model = Model();
            _instance = scene.GetRootGameObjects().Single(go => PrefabUtility.GetCorrespondingObjectFromSource(go) == model);
            _player = _instance.GetComponent<ClipPlayer>();
            _placard = scene.GetRootGameObjects().Single(go => go.name == TaskKit.PlacardName(_plan)).GetComponent<Placard>();
            foreach (Target target in _instance.GetComponentsInChildren<Target>(true)) Live(target.onSelected);
            Live(_player.onFinished);
            Live(_placard.onChosen);

            var pilot = new GameObject("Autopilot").AddComponent<SequenceAutopilot>();
            pilot.gameObject.AddComponent<InteractionActor>();  // how it acts on the task's targets
            try
            {
                pilot.fastForward = true;
                pilot.stopAt = "B01";
                Assert.That(pilot.Run(), Is.True);
                Assert.That(pilot.Sequence.Current.id, Is.EqualTo("B01"), "a walk to a step stops as it starts");
                Assert.That(Node("DOOR_GRP").localRotation, Is.Not.EqualTo(Quaternion.identity), "the steps before it left the door open");
                pilot.stopAt = "";
                Assert.That(pilot.Run(), Is.True, string.Join("\n", pilot.Log));
                Assert.That(pilot.Sequence.Finished, Is.True);
                Assert.That(pilot.Stuck, Is.Empty, string.Join("\n", pilot.Log));
                Assert.That(pilot.Log, Has.Some.Contains("took KEY_GRP in hand").Or.Some.Contains("selected KEY_GRP"));
                Assert.That(pilot.Log, Has.Some.Contains("chose 'YES'"), "the forward answer at B03");
            }
            finally
            {
                Object.DestroyImmediate(pilot.gameObject);
            }
        }
    }
}
