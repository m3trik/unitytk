using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// <see cref="TaskBehaviors"/>: the task knows a behavior by its traits, refined per row, and the interfaces its builder
    /// implements, never by its name, so a project's own verb takes each role the kit's do (<see cref="TestRolesBehavior"/>,
    /// <see cref="TestGateBehavior"/>): its viewpoint placed, its gate's button held back, its step going on, its tick
    /// listed, its part sent home shown.
    /// </summary>
    public class TaskBehaviorsTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("cabinet_2026");
            var cabinet = new GameObject("CABINET").transform;
            cabinet.SetParent(_root.transform);
            GameObject Box(string name, Vector3 at, Vector3 size)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = name;
                box.transform.SetParent(cabinet);
                box.transform.SetPositionAndRotation(at, Quaternion.identity);
                box.transform.localScale = size;
                return box;
            }
            Box("FLOOR", new Vector3(0, -0.05f, 0), new Vector3(20, 0.1f, 20));
            Box("DOOR_GRP", new Vector3(0, 1f, 0), Vector3.one * 0.4f);
            Box("LID", new Vector3(0, 1.5f, 0), new Vector3(0.4f, 0.02f, 0.4f));
        }

        [Test]
        public void A_viewpoint_that_faces_away_from_what_its_row_says_the_user_faces_is_said()
        {
            // A locator shows no forward where it is made: a task was built whose user stood with the unit behind them
            // through every step, and the build said nothing.
            var view = new GameObject("VIEW_FRONT").transform;
            view.SetParent(_root.transform);
            view.SetPositionAndRotation(new Vector3(0f, 0f, 2f), Quaternion.identity);  // in front of the door, looking on past it
            string json = "{\"version\": 3, \"steps\": [{\"id\": \"A01\", \"actions\": [" +
                "{\"behavior\": \"travel\", \"text\": \"The user is brought to VIEW_FRONT\", \"targets\": [\"CABINET|DOOR_GRP\"], " +
                "\"fields\": {\"where\": [\"VIEW_FRONT\"]}}]}]}";
            var plan = SheetPlan.Read(json, "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            var scene = new ManifestScene(_root, new AnimationClip[0]);
            var away = new OpContext();
            ManifestsActions.Build(plan, scene, away);
            Assert.That(away.Issues.Select(i => i.code), Is.EqualTo(new[] { TaskKit.FacesAwayCode }));
            Assert.That(away.Issues[0].message, Does.Contain("'VIEW_FRONT' faces away from 'DOOR_GRP'"));
            Assert.That(view.rotation, Is.EqualTo(Quaternion.identity), "said, never turned for them: the helper is the truth");
            view.rotation = Quaternion.Euler(0f, 180f, 0f);  // turned to face it
            var toward = new OpContext();
            ManifestsActions.Build(plan, scene, toward);
            Assert.That(toward.Issues, Is.Empty);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            foreach (string made in new[] { "Task", "Placard", "Viewpoints", Panels.PanelsActions.InputName, Viewpoints.ViewpointsActions.EyesName })
            {
                var node = GameObject.Find(made);
                if (node != null) Object.DestroyImmediate(node);
            }
        }

        [Test]
        public void A_behavior_is_known_by_its_traits_its_partner_and_its_gate()
        {
            Assert.That(TaskBehaviors.Traits("travel").Travels && TaskBehaviors.Traits("test_walk").Travels, "the kit's travel and a project's own");
            Assert.That(TaskBehaviors.Traits("advance").GoesOn && TaskBehaviors.Traits("return").Returns);
            Assert.That(TaskBehaviors.With(t => t.GoesOn), Does.Contain("advance").And.Contain("test_go_on"));
            Assert.That(TaskBehaviors.Pairs("tick", "list") && TaskBehaviors.Pairs("list", "tick"), "a pair goes both ways");
            Assert.That(TaskBehaviors.Pairs("test_tick", "list"), "a project's own pairs by naming its partner");
            Assert.That(TaskBehaviors.Pairs("tick", "test_tick") || TaskBehaviors.Pairs("tick", "tick"), Is.False, "two ticks pair with no list");
            var gate = Row("test_gate", "Done", null);
            var select = new SheetPlan.Shape.PlanAction { behavior = "select", text = "Selecting the door", targets = new List<string> { "CABINET|DOOR_GRP" } };
            var step = Step(gate, select);
            Assert.That(TaskBehaviors.Gate(gate, step), Is.Not.Null, "a project's own gate");
            Assert.That(TaskBehaviors.Gate(select, step), Is.Null);
        }

        [Test]
        public void A_buttons_traits_and_gate_are_its_rows_by_its_cells()
        {
            // One word for every choice a page offers: what a button is, its To says.
            var next = Row("button", "Next", "next");
            var on = Step(next);
            Assert.That(TaskBehaviors.Traits(next, on).EndsStep && TaskBehaviors.Traits(next, on).Offers && TaskBehaviors.Traits(next, on).Chooses,
                        "a button that goes on: offered, chosen, the step ended");
            Assert.That(TaskBehaviors.Gate(next, on), Is.Not.Null, "and its step waits on it");
            var elsewhere = Row("button", "Again", "A01");
            Assert.That(TaskBehaviors.Traits(elsewhere, Step(elsewhere)).EndsStep, Is.True, "one that goes to a step ends its own");
            Assert.That(TaskBehaviors.Gate(elsewhere, Step(elsewhere)), Is.Null, "and holds it for nothing");

            var right = Row("button", "Wrench", "next");
            var wrong = Row("button", "Hammer", "retry");
            var question = Step(right, wrong);
            Assert.That(TaskBehaviors.Traits(right, question).EndsStep || TaskBehaviors.Traits(wrong, question).EndsStep, Is.False,
                        "a question's answers end nothing: a wrong one stays");
            Assert.That(TaskBehaviors.Traits(right, question).Offers && TaskBehaviors.Traits(wrong, question).Offers, "both on the page");
            Assert.That(TaskBehaviors.Gate(right, question), Is.Null, "the right answer is no gate");
            Assert.That(TaskBehaviors.Traits("button").EndsStep, Is.True, "what the word declares, before a row refines it");
        }

        static SheetPlan.Shape.PlanAction Row(string behavior, string label, string to)
            => new SheetPlan.Shape.PlanAction
            {
                behavior = behavior, text = $"Selecting '{label}'", to = to, fields = new Dictionary<string, object> { ["label"] = label },
            };

        static SheetPlan.Shape.PlanStep Step(params SheetPlan.Shape.PlanAction[] rows)
            => new SheetPlan.Shape.PlanStep { id = "A02", actions = rows.ToList() };

        [Test]
        public void A_projects_own_verbs_build_in_the_roles_of_the_kits()
        {
            string json = "{\"version\": 3, \"steps\": [" +
                "{\"id\": \"A01\", \"actions\": [" +
                "{\"behavior\": \"test_walk\", \"text\": \"The user walks up to the door\", \"targets\": [\"CABINET|DOOR_GRP\"]}," +
                "{\"behavior\": \"list\", \"text\": \"A list is shown\"}]}," +
                "{\"id\": \"A02\", \"actions\": [" +
                "{\"behavior\": \"select\", \"text\": \"Selecting the door completes the step\", \"targets\": [\"CABINET|DOOR_GRP\"]}," +
                "{\"behavior\": \"test_gate\", \"text\": \"Selecting 'Done' goes on\", \"fields\": {\"label\": \"Done\"}}]}," +
                "{\"id\": \"A03\", \"actions\": [" +
                "{\"behavior\": \"button\", \"text\": \"Selecting 'Again' goes to step A01\", \"fields\": {\"label\": \"Again\"}, \"to\": \"A01\"}," +
                "{\"behavior\": \"test_go_on\", \"text\": \"Go on by itself\"}]}," +
                "{\"id\": \"A04\", \"actions\": [" +
                "{\"behavior\": \"test_tick\", \"text\": \"Tick 'Door checked'\", \"fields\": {\"label\": \"Door checked\"}}]}," +
                "{\"id\": \"A05\", \"actions\": [" +
                "{\"behavior\": \"hide\", \"text\": \"The lid disappears\", \"targets\": [\"CABINET|LID\"]}," +
                "{\"behavior\": \"test_send_home\", \"text\": \"The lid goes home\", \"targets\": [\"CABINET|LID\"]}]}]}";
            var plan = SheetPlan.Read(json, "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            var scene = new ManifestScene(_root, new AnimationClip[0]);
            ManifestAssessment assessed = ManifestsActions.Assess(plan, scene);
            Assert.That(assessed.rows.Select(r => r.state), Is.All.EqualTo(ManifestAssessment.State.Ready),
                        string.Join("; ", assessed.rows.SelectMany(r => r.problems)));
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)).ToList(), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action)).ToList()));
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, again);
            Assert.That(again.Changes, Is.Empty, "a second build plans nothing");

            Transform view = GameObject.Find(TaskKit.ViewpointsName(plan)).transform.Find(TaskKit.ViewpointName("A01"));
            Assert.That(view.GetComponent<Placement.PlacedSpot>().key, Is.EqualTo("travel/A01"), "its viewpoint placed by the kit, before any row");
            var sequence = GameObject.Find("Task").GetComponent<Steps.Sequence>();
            Assert.That(sequence.Find("A02").Choice("Done").waits, Is.True, "its gate's button offered once the door is selected");
            Assert.That(sequence.Find("A03").autoAdvance, Is.True, "its row says the step goes on, though it offers a choice");
            Transform placard = GameObject.Find("Placard").transform;
            Assert.That(placard.Find(TaskKit.ListName("A01")).GetComponent<Panels.PlacardList>().items, Is.EqualTo(new[] { "Door checked" }),
                        "a list with no items lists its ticks");
            Assert.That(_root.transform.Find("CABINET/LID").GetComponent<RenderEffects.FadeTo>(), Is.Null, "sent home, it goes back shown");
            Assert.That(live.Issues.Select(i => i.code), Does.Contain(TaskKit.FadeReturnedCode));
        }
    }
}
