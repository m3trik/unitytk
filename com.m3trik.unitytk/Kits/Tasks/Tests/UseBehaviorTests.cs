using M3trik.Unitytk.Manifests;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Testing;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// The <c>take</c> and <c>use</c> builders on an imported delivery (a box keyed
    /// over two takes, Intro and Outro): a plan built into its instance, then played by a hand
    /// as a person would, and held to the kernel's three checks.
    /// </summary>
    public class UseBehaviorTests
    {
        const string Folder = "Assets/UnitytkTaskKitTests";
        const string Model = Folder + "/two_takes.fbx";
        GameObject _instance;
        GameObject _wrench;

        [OneTimeSetUp]
        public void Import()
        {
            AssetDatabase.CreateFolder("Assets", "UnitytkTaskKitTests");
            File.WriteAllText(Model, TwoTakeFbx.Write("audio", "{\"version\": 3, \"cues\": []}"));
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        [OneTimeTearDown]
        public void Remove() => AssetDatabase.DeleteAsset(Folder);

        [SetUp]
        public void SetUp()
        {
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Model));
            // A tool on the table beside the box (the delivery's own would be a node of it).
            _wrench = new GameObject("WRENCH_GRP");
            _wrench.transform.SetParent(_instance.transform);
            _wrench.transform.localPosition = new Vector3(0, 0, 1);
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance.TryGetComponent(out ClipPlayer player)) player.Clear();
            Object.DestroyImmediate(_instance);
            foreach (string made in new[] { "Task", "Placard", M3trik.Unitytk.Panels.PanelsActions.InputName, M3trik.Unitytk.Viewpoints.ViewpointsActions.EyesName })
            {
                var node = GameObject.Find(made);
                if (node != null) Object.DestroyImmediate(node);
            }
        }

        static SheetPlan.Shape.Payload Plan(string actions)
        {
            var plan = SheetPlan.Read("{\"version\": 2, \"steps\": [{\"id\": \"A01\", \"actions\": [" + actions + "]}]}",
                                         "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            return plan;
        }

        const string TakeWrench = "{\"behavior\": \"take\", \"text\": \"Selecting the wrench takes it in hand\", \"targets\": [\"WRENCH_GRP\"]}";
        const string UseBox = "{\"behavior\": \"use\", \"text\": \"Selecting the box with wrench in hand triggers Box Intro animation\", " +
                              "\"targets\": [\"Box\"], \"clip\": \"Intro\", \"fields\": {\"with\": \"WRENCH_GRP\"}}";
        // What a sentence sheet said of a tool after its use: implied by the use's With, a note.
        const string BackInHand = "{\"behavior\": \"note\", \"text\": \"Wrench returns to hand\"}";

        // A persistent call runs in a player only (Unity's default); an Edit Mode test runs it here.
        static void Live(UnityEngine.Events.UnityEventBase when)
        {
            for (int i = 0; i < when.GetPersistentEventCount(); i++)
                when.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
        }

        static void Live(GameObject node) => Live(node.GetComponent<Target>().onSelected);

        [Test]
        public void The_plan_is_ready_against_the_imported_delivery()
        {
            var scene = new ManifestScene(_instance);
            Assert.That(scene.ClipNames, Is.EqualTo(new[] { "Intro", "Outro" }), "the takes, found on the model asset");
            var assessment = ManifestsActions.Assess(Plan(TakeWrench + "," + UseBox + "," + BackInHand), scene);
            Assert.That(assessment.rows.Select(r => r.state), Is.All.EqualTo(ManifestAssessment.State.Ready));
        }

        [Test]
        public void A_row_that_asks_less_of_a_target_than_another_step_does_is_said()
        {
            // One step selects the box with nothing in hand, a later one with the wrench: the box asks for one thing in
            // every step, so the first would ask for the wrench too (it could never be done).
            const string selectBox = "{\"behavior\": \"use\", \"text\": \"Selecting the box triggers Box Intro animation\", " +
                                     "\"targets\": [\"Box\"], \"clip\": \"Intro\"}";
            const string wrenchBox = "{\"behavior\": \"use\", \"text\": \"Selecting the box with wrench in hand triggers Box Outro animation\", " +
                                     "\"targets\": [\"Box\"], \"clip\": \"Outro\", \"fields\": {\"with\": \"WRENCH_GRP\"}}";
            var plan = SheetPlan.Read("{\"version\": 2, \"steps\": [{\"id\": \"A01\", \"actions\": [" + selectBox + "]}, " +
                                      "{\"id\": \"A02\", \"actions\": [" + TakeWrench + "," + wrenchBox + "]}]}", "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            var ctx = new OpContext();
            ManifestsActions.Build(plan, new ManifestScene(_instance), ctx);
            string said = ctx.Issues.Single(i => i.code == TaskKit.OneToolCode).message;
            Assert.That(said, Does.StartWith("A01: 'Box' asks for 'WRENCH_GRP' in hand in this step too, where its row names nothing: A02 acts on it"),
                        "at the row that asks less than the target will");
            Assert.That(said, Does.Contain("Name another object"), "with its fix");
            Assert.That(_instance.transform.Find("Box").GetComponent<Target>().requires, Is.SameAs(_wrench.GetComponent<Holdable>()), "what it warns of");
        }

        [Test]
        public void Rows_that_ask_the_same_of_a_target_say_nothing()
        {
            const string wrenchBox = "{\"behavior\": \"use\", \"text\": \"Selecting the box with wrench in hand triggers Box Outro animation\", " +
                                     "\"targets\": [\"Box\"], \"clip\": \"Outro\", \"fields\": {\"with\": \"WRENCH_GRP\"}}";
            var ctx = new OpContext();
            ManifestsActions.Build(Plan(TakeWrench + "," + UseBox + "," + wrenchBox), new ManifestScene(_instance), ctx);
            Assert.That(ctx.Issues, Is.Empty);
        }

        [Test]
        public void A_built_plan_plays_as_its_sheet_says()
        {
            var plan = Plan(TakeWrench + "," + UseBox + "," + BackInHand);
            ManifestsActions.Build(plan, new ManifestScene(_instance), new OpContext());
            // A selection's signal is named by its node's path under the delivery, never by the delivery's own name: a
            // re-export under a new (dated) file name renamed every signal, and each step then awaited the old one too.
            Assert.That(GameObject.Find("Task").transform.Find("A01").GetComponent<M3trik.Unitytk.Steps.Step>().awaits,
                        Has.Some.EqualTo("select:WRENCH_GRP").And.None.Contains(_instance.name));
            GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>().Begin();  // its step switches its triggers on
            Transform box = _instance.transform.Find("Box");
            Live(box.gameObject);
            Live(_wrench);
            var hand = new GameObject("HAND").AddComponent<Hand>();
            hand.transform.SetParent(_instance.transform);
            var player = _instance.GetComponent<ClipPlayer>();

            Assert.That(box.GetComponent<Target>().Select(hand), Is.False, "the box wants the wrench in hand");
            Assert.That(_wrench.GetComponent<Target>().Select(hand) && hand.Holds(_wrench.GetComponent<Holdable>()));
            float start = box.localPosition.x;
            Assert.That(box.GetComponent<Target>().Select(hand), Is.True);
            Assert.That(player.IsPlaying("Intro"));
            player.Finish();
            Assert.That(!player.Busy);
            Assert.That(box.localPosition.x, Is.Not.EqualTo(start).Within(1e-6), "Intro moved the box and left it at its end");
        }

        [Test]
        public void A_tool_the_clip_does_not_move_works_at_what_it_moves_by_its_rig_and_from_its_own_tip_offset()
        {
            // The ideal setup is the rig's: the tool's pivot at its tip, its axes the part's. A tool rigged otherwise (the
            // real task's screwdriver) is adjusted by hand after a build, on the tool, and the next build keeps it.
            var plan = Plan(TakeWrench + "," + UseBox);
            var ctx = new OpContext();
            ManifestsActions.Build(plan, new ManifestScene(_instance), ctx);
            Assert.That(ctx.Issues, Is.Empty);
            var wrench = _wrench.GetComponent<Holdable>();
            Assert.That((wrench.tipPosition, wrench.tipRotation), Is.EqualTo((Vector3.zero, Vector3.zero)), "as rigged: no offset is given it");
            Transform box = _instance.transform.Find("Box");
            ClipTrigger trigger = Interactions.TriggerOf(box.gameObject, "Intro");
            Assert.That(trigger.tool == wrench && trigger.workAt == box.Find(Interactions.WorkSpotName("WRENCH_GRP")),
                        "Intro moves the box, not the wrench: the wrench works at the box, at a spot of its own there");
            Assert.That((trigger.workAt.localPosition, trigger.workAt.localRotation), Is.EqualTo((Vector3.zero, Quaternion.identity)),
                        "its spot is the box's own pivot and axes: the rig's place for it");

            // A person's tip offset, on the tool: its tip, wherever the spot is, rests on the spot.
            wrench.tipPosition = new Vector3(0f, 0f, 0.1f);
            wrench.tipRotation = new Vector3(90f, 0f, 0f);
            trigger.workAt.localPosition = new Vector3(0f, 0.2f, 0f);  // and the spot, moved for this part alone
            wrench.WorkPose(trigger.WorkPoint, out Vector3 worksAt, out _);
            Assert.That(Vector3.Distance(worksAt, trigger.workAt.position - trigger.workAt.rotation * Quaternion.Inverse(Quaternion.Euler(wrench.tipRotation)) * wrench.tipPosition),
                        Is.LessThan(1e-4f), "the tool's tip on the spot");
            var again = new OpContext();
            ManifestsActions.Build(plan, new ManifestScene(_instance), again);
            Assert.That((wrench.tipPosition, wrench.tipRotation), Is.EqualTo((new Vector3(0f, 0f, 0.1f), new Vector3(90f, 0f, 0f))),
                        "a build gives the tool no offset of its own: the person's stays");
        }

        [Test]
        public void A_task_extension_the_kit_does_not_read_is_said_once_to_the_run_however_many_rows_use_the_tool()
        {
            var plan = SheetPlan.Read(
                "{\"version\": 2, \"steps\": [{\"id\": \"A01\", \"actions\": [" + TakeWrench + "," + UseBox + "," + UseBox + "]}], " +
                "\"extensions\": {\"task\": {\"tools\": {\"WRENCH_GRP\": {\"tip\": [0, 0, 0.1]}}}}}", "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            var ctx = new OpContext();
            ManifestsActions.Build(plan, new ManifestScene(_instance), ctx);
            Assert.That(ctx.Issues.Count(i => i.code == TaskKit.ExtensionCode), Is.EqualTo(1), "said once, as the run begins");
            Assert.That(_wrench.GetComponent<Holdable>().tipPosition, Is.EqualTo(Vector3.zero), "and nothing of it is applied");
        }

        [Test]
        public void A_working_spot_two_rows_share_is_held_by_both()
        {
            // The real task's screwdriver works at each screw in two steps (out, then back in), at one spot on the screw:
            // the row that placed it held it, and the other showed nothing to select, edit or revert.
            var plan = Plan(TakeWrench + "," + UseBox + "," + UseBox);
            ManifestAssessment built = ManifestsActions.Build(plan, new ManifestScene(_instance), new OpContext());
            Transform spot = _instance.transform.Find("Box").Find(Interactions.WorkSpotName("WRENCH_GRP"));
            Assert.That(spot, Is.Not.Null);
            var placed = spot.GetComponent<Placement.PlacedSpot>();
            Assert.That(built.steps.SelectMany(s => s.rows).Count(r => r.parts.Contains(placed)), Is.EqualTo(2),
                        "each row that has the tool work there holds the spot");
        }

        [Test]
        public void A_next_after_a_take_waits_for_it_and_a_part_only_pointed_out_glows_steady()
        {
            const string Next = "{\"behavior\": \"button\", \"text\": \"Selecting 'Next' goes on\", \"fields\": {\"label\": \"Next\"}, \"to\": \"next\"}";
            const string ShowBox = "{\"behavior\": \"highlight\", \"text\": \"Highlight the box\", \"targets\": [\"Box\"]}";
            const string ShowSign = "{\"behavior\": \"highlight\", \"text\": \"Highlight the sign\", \"targets\": [\"SIGN\"]}";
            new GameObject("SIGN").transform.SetParent(_instance.transform);  // a part the task only points out
            var plan = SheetPlan.Read(
                "{\"version\": 2, \"steps\": [" +
                "{\"id\": \"A01\", \"actions\": [" + ShowSign + "," + ShowBox + "," + Next + "]}," +
                "{\"id\": \"A02\", \"actions\": [" + TakeWrench + "," + Next + "]}," +
                "{\"id\": \"A03\", \"actions\": [" + ShowBox + "," + UseBox + "]}]}",
                "task", out string refusal);
            Assert.That(refusal, Is.Null);
            var ctx = new OpContext();
            ManifestsActions.Build(plan, new ManifestScene(_instance), ctx);
            Assert.That(ctx.Issues, Is.Empty);
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, new ManifestScene(_instance), dry);
            Assert.That(dry.Changes.Select(c => c.target), Is.Empty, "a second build changes nothing");

            Transform box = _instance.transform.Find("Box");
            Assert.That(_instance.transform.Find("SIGN").GetComponent<M3trik.Unitytk.RenderEffects.Highlight>().pulse, Is.False,
                        "the task only points the sign out: a steady glow, never the pulse that means act on it");
            Assert.That(box.GetComponent<M3trik.Unitytk.RenderEffects.Highlight>().pulse, Is.True, "A03 has the user act on the box: it pulses");
            var sequence = GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>();
            var page = GameObject.Find("Placard").transform.Find("A02").GetComponent<M3trik.Unitytk.Panels.PlacardPage>();
            Assert.That(page.waiting, Is.EqualTo(new[] { "Next" }));
            Assert.That(GameObject.Find("Placard").transform.Find("A01").GetComponent<M3trik.Unitytk.Panels.PlacardPage>().waiting, Is.Empty,
                        "A01 asks for nothing: its Next is there at once");
            Live(_wrench);
            var step = sequence.steps.Find(s => s.id == "A02");
            Live(step.onComplete);
            var hand = new GameObject("HAND").AddComponent<Hand>();
            hand.transform.SetParent(_instance.transform);

            sequence.Begin();
            Assert.That(sequence.Choose("Next"), Is.True);
            Assert.That(sequence.Current.id, Is.EqualTo("A02"));
            Assert.That(sequence.Choose("Next"), Is.False, "the wrench not taken: no Next yet");
            Assert.That(page.Offered, Is.Empty);
            Assert.That(_wrench.GetComponent<Target>().Select(hand), Is.True);
            Assert.That(page.Offered, Is.EqualTo(new[] { "Next" }), "taken: Next shows");
            Assert.That(sequence.Choose("Next"), Is.True);
            Assert.That(sequence.Current.id, Is.EqualTo("A03"));
        }

        [Test]
        public void A_built_task_runs_step_by_step_as_its_sheet_says()
        {
            var plan = SheetPlan.Read(
                "{\"version\": 2, \"steps\": [" +
                "{\"id\": \"A01\", \"name\": \"Wrench the box\", \"actions\": [" + TakeWrench + "," + UseBox + "," + BackInHand + "," +
                "{\"behavior\": \"advance\", \"text\": \"Auto Advance\"}]}," +
                "{\"id\": \"A02\", \"name\": \"Send it back\", \"actions\": [" +
                "{\"behavior\": \"use\", \"text\": \"Selecting the box with wrench in hand triggers Box Outro animation\", \"targets\": [\"Box\"], " +
                "\"clip\": \"Outro\", \"fields\": {\"with\": \"WRENCH_GRP\"}}," +
                "{\"behavior\": \"button\", \"text\": \"Selecting 'Next' goes on\", \"fields\": {\"label\": \"Next\"}, \"to\": \"next\"}]}]}",
                "task", out string refusal);
            Assert.That(refusal, Is.Null);
            var ctx = new OpContext();
            ManifestsActions.Build(plan, new ManifestScene(_instance), ctx);
            Assert.That(ctx.Issues, Is.Empty);
            Transform box = _instance.transform.Find("Box");
            var player = _instance.GetComponent<ClipPlayer>();
            var sequence = GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>();
            Live(box.gameObject);
            Live(_wrench);
            Live(player.onFinished);
            var hand = new GameObject("HAND").AddComponent<Hand>();
            hand.transform.SetParent(_instance.transform);

            sequence.Begin();
            Assert.That(sequence.Current.id, Is.EqualTo("A01"));
            Assert.That(_wrench.GetComponent<Target>().Select(hand), Is.True);
            Assert.That(box.GetComponent<Target>().Select(hand), Is.True);
            player.Finish();
            Assert.That(sequence.Current.id, Is.EqualTo("A01"), "complete, it runs long enough to be read");
            sequence.Tick(60f);
            Assert.That(sequence.Current.id, Is.EqualTo("A02"), "Intro's end completed A01, which went on by itself");
            Assert.That(_wrench.GetComponent<Target>().enabled, Is.False, "the wrench is A01's alone");
            Assert.That(box.GetComponent<Target>().Select(hand), Is.True);
            player.Finish();
            Assert.That(sequence.Current.id, Is.EqualTo("A02"), "A02 waits for its Next");
            Assert.That(sequence.Choose("Next") && sequence.Finished);
        }

        [Test]
        public void A_build_plans_what_it_then_does_is_one_undo_step_and_converges()
        {
            var plan = Plan(TakeWrench + "," + UseBox + "," + BackInHand);
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, new ManifestScene(_instance), dry);
            Assert.That(_instance.GetComponent<ClipPlayer>() == null && _wrench.GetComponent<Holdable>() == null, "a dry run builds nothing");

            OpContext.RunMenu("Build Tasks", ctx =>
            {
                ManifestsActions.Build(plan, new ManifestScene(_instance), ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That(_instance.GetComponent<ClipPlayer>().Find("Intro"), Is.Not.Null);

            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, new ManifestScene(_instance), again);
            Assert.That(again.Changes, Is.Empty);

            Undo.PerformUndo();
            Assert.That(_instance.GetComponent<ClipPlayer>() == null && _wrench.GetComponent<Holdable>() == null,
                        "one Ctrl+Z took the build away");
        }
    }
}
