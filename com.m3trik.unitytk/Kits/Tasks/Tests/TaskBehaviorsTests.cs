using System.Linq;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// <see cref="TaskBehaviors"/>: the task knows a behavior by its traits and the interfaces its builder
    /// implements, never by its name, so a project's own verb takes each role the kit's do (<see cref="TestRolesBehavior"/>,
    /// <see cref="TestGateBehavior"/>): its viewpoint placed and marked, its gate's button held back, its step going on,
    /// its tick listed, its question gone to, its part sent home unfaded.
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
            string json = "{\"version\": 2, \"steps\": [{\"id\": \"A01\", \"actions\": [" +
                "{\"behavior\": \"teleport\", \"text\": \"The user is brought to VIEW_FRONT\", \"targets\": [\"VIEW_FRONT\"], " +
                "\"fields\": {\"faces\": \"CABINET|DOOR_GRP\"}}]}]}";
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
            Assert.That(TaskBehaviors.Traits("teleport").Teleports && TaskBehaviors.Traits("test_walk").Teleports, "the kit's teleport and a project's own");
            Assert.That(TaskBehaviors.Traits("advance").GoesOn && TaskBehaviors.Traits("return").Returns);
            Assert.That(TaskBehaviors.With(t => t.GoesOn), Does.Contain("advance").And.Contain("test_go_on"));
            Assert.That(TaskBehaviors.Pairs("check", "checklist") && TaskBehaviors.Pairs("checklist", "check"), "a pair goes both ways");
            Assert.That(TaskBehaviors.Pairs("test_tick", "checklist") && TaskBehaviors.Pairs("quiz", "test_question"), "a project's own pairs by naming its partner");
            Assert.That(TaskBehaviors.Pairs("check", "test_tick") || TaskBehaviors.Pairs("check", "check"), Is.False, "two ticks pair with no list");
            Assert.That(TaskBehaviors.Gate("pause") != null && TaskBehaviors.Gate("test_gate") != null, "the gates");
            Assert.That(TaskBehaviors.Gate("select"), Is.Null);
        }

        [Test]
        public void A_projects_own_verbs_build_in_the_roles_of_the_kits()
        {
            string json = "{\"version\": 2, \"steps\": [" +
                "{\"id\": \"A01\", \"actions\": [" +
                "{\"behavior\": \"marker\", \"text\": \"Destination circle fades in\"}," +
                "{\"behavior\": \"test_walk\", \"text\": \"The user walks up to the door\", \"fields\": {\"facing\": \"CABINET|DOOR_GRP\"}}," +
                "{\"behavior\": \"checklist\", \"text\": \"Checklist appears on placard with blank check boxes\"}]}," +
                "{\"id\": \"A02\", \"actions\": [" +
                "{\"behavior\": \"select\", \"text\": \"Selecting the door completes the step\", \"targets\": [\"CABINET|DOOR_GRP\"]}," +
                "{\"behavior\": \"test_gate\", \"text\": \"Selecting 'Done' goes on\", \"fields\": {\"label\": \"Done\"}}]}," +
                "{\"id\": \"A03\", \"actions\": [" +
                "{\"behavior\": \"goto\", \"text\": \"Selecting 'Again' triggers step A01\", \"fields\": {\"label\": \"Again\"}, \"to\": \"A01\"}," +
                "{\"behavior\": \"test_go_on\", \"text\": \"Go on by itself\"}]}," +
                "{\"id\": \"A04\", \"actions\": [" +
                "{\"behavior\": \"test_tick\", \"text\": \"Tick 'Door checked'\", \"fields\": {\"label\": \"Door checked\"}}," +
                "{\"behavior\": \"quiz\", \"text\": \"Selecting 'Take Quiz' triggers advancement to the quiz\", \"fields\": {\"label\": \"Take Quiz\"}}]}," +
                "{\"id\": \"A05\", \"actions\": [" +
                "{\"behavior\": \"test_question\", \"text\": \"Is the door shut?\", \"fields\": {\"label\": \"Yes\"}}," +
                "{\"behavior\": \"fade_out\", \"text\": \"The lid fades away\", \"targets\": [\"CABINET|LID\"]}," +
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
            Assert.That(view.GetComponent<Placement.PlacedSpot>().key, Is.EqualTo("teleport/A01"), "its viewpoint placed by the kit, before any row");
            Assert.That(view.GetComponentInChildren<Indicators.Marker>(true), Is.Not.Null, "a marker naming no place marks where it brings the user");
            var sequence = GameObject.Find("Task").GetComponent<Steps.Sequence>();
            Assert.That(sequence.Find("A02").Choice("Done").waits, Is.True, "its gate's button offered once the door is selected");
            Assert.That(sequence.Find("A03").autoAdvance, Is.True, "its row says the step goes on, though it offers a choice");
            Transform placard = GameObject.Find("Placard").transform;
            Assert.That(placard.Find(TaskKit.ChecklistName("A01")).GetComponent<Panels.PlacardList>().items, Is.EqualTo(new[] { "Door checked" }),
                        "a blank checklist lists its ticks");
            Assert.That(sequence.Find("A04").Choice("Take Quiz").to, Is.EqualTo("A05"), "the quiz: the first step holding its partner's row");
            Assert.That(_root.transform.Find("CABINET/LID").GetComponent<RenderEffects.FadeTo>(), Is.Null, "sent home, it goes back shown");
            Assert.That(live.Issues.Select(i => i.code), Does.Contain(TaskKit.FadeReturnedCode));
        }
    }
}
