using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// <see cref="TaskKit"/>: a plan assessed against a scene action by action, and built
    /// through each behavior's builder, held to the kernel's three checks (one Ctrl+Z restores
    /// the scene, the dry run names every change, a second build plans nothing).
    /// </summary>
    public class TaskKitTests
    {
        GameObject _root;
        ManifestScene _scene;

        // A cabinet as an FBX instance holds it: its top level under the instance root.
        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("cabinet_2026");
            var cabinet = new GameObject("CABINET").transform;
            cabinet.SetParent(_root.transform);
            new GameObject("DOOR_GRP").transform.SetParent(cabinet);
            new GameObject("LID").transform.SetParent(cabinet);
            _scene = new ManifestScene(_root, new[] { new AnimationClip { name = "A01" } });
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
            foreach (string made in new[] { "Task", "Placard", M3trik.Unitytk.Panels.PanelsActions.InputName, M3trik.Unitytk.Viewpoints.ViewpointsActions.EyesName })
            {
                var node = GameObject.Find(made);
                if (node != null) Object.DestroyImmediate(node);
            }
        }

        static SheetPlan.Shape.Payload Plan(string actions)
        {
            string json = "{\"version\": 2, \"steps\": [{\"id\": \"A01\", \"actions\": [" + actions + "]}], " +
                          "\"issues\": [\"no step plays the take Z99\"]}";
            var plan = SheetPlan.Read(json, "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            return plan;
        }

        const string Collider = "{\"behavior\": \"test_collider\", \"text\": \"Selecting the door triggers Door open Animation\", " +
                                "\"targets\": [\"CABINET|DOOR_GRP\"], \"clip\": \"A01\"}";

        [Test]
        public void A_tasks_objects_are_named_for_its_settings_so_a_renamed_sheet_is_the_same_task()
        {
            // A sheet a project manager saved under a new name made a second task in the scene (a real task:
            // two sequences, each with its placard): a build finds what it made by these names.
            var source = new SheetPlan.Shape.PlanSource { sheet = "Lamp - Updated.csv", settings = "../tasks/lamp.json" };
            var plan = new SheetPlan.Shape.Payload { source = source };
            Assert.That(TaskKit.SequenceName(plan), Is.EqualTo("Task (lamp)"));
            source.sheet = "Lamp - PM v2.csv";
            Assert.That(TaskKit.SequenceName(plan), Is.EqualTo("Task (lamp)"), "renamed, the sheet is still the task's");
            Assert.That(TaskKit.PlacardName(plan), Is.EqualTo("Placard (lamp)"));
            source.settings = "";
            Assert.That(TaskKit.SequenceName(plan), Is.EqualTo("Task (Lamp - PM v2)"), "a plan staged with no settings file goes by its sheet");
        }

        [Test]
        public void Assess_says_which_actions_are_ready_unresolved_or_have_no_builder()
        {
            var plan = Plan(Collider + "," +
                            "{\"behavior\": \"test_collider\", \"text\": \"Selecting the hinge\", \"targets\": [\"CABINET|HINGE\"], \"clip\": \"B07\"}," +
                            "{\"behavior\": \"test_collider\", \"text\": \"Selecting the lid\", \"targets\": [\"CABINET|LID\"], " +
                            "\"issues\": [\"read \\\"lidd\\\" as \\\"lid\\\" (a typo in the sheet?)\"]}," +
                            "{\"behavior\": \"no_builder_yet\", \"text\": \"Number pad appears\"}," +
                            "{\"behavior\": \"test_collider\", \"text\": \"Selecting the door again\", \"targets\": [\"CABINET|DOOR_GRP\"], " +
                            "\"clip\": \"A01\", \"warnings\": [\"row 1 (Animation Clip Names): the take A01 moves none of this row's DOOR: re-export it\"]}");
            ManifestAssessment assessment = ManifestsActions.Assess(plan, _scene);

            Assert.That(assessment.rows.Select(r => r.state), Is.EqualTo(new[]
            {
                ManifestAssessment.State.Ready,
                ManifestAssessment.State.Unresolved,
                ManifestAssessment.State.Unresolved,
                ManifestAssessment.State.Unsupported,
                ManifestAssessment.State.Ready,
            }));
            Assert.That(assessment.rows[4].warnings.Single(), Does.Contain("moves none"), "a warning is carried, never a block");
            Assert.That(assessment.Warnings(), Is.EqualTo(1));
            Assert.That(assessment.rows[1].problems, Is.EqualTo(new[]
            {
                "the target 'CABINET|HINGE' is no node under 'cabinet_2026'",
                "the delivery carries no take 'B07'",
            }));
            Assert.That(assessment.rows[2].problems.Single(), Does.StartWith("read \"lidd\""), "the plan's own issue holds it back");
            Assert.That(assessment.Unsupported(), Is.EqualTo(new System.Collections.Generic.SortedDictionary<string, int> { ["no_builder_yet"] = 1 }));
            Assert.That(assessment.planIssues, Is.EqualTo(new[] { "no step plays the take Z99" }));
            Assert.That(_root.GetComponentsInChildren<BoxCollider>(), Is.Empty, "an assessment changes nothing");
        }

        [Test]
        public void What_the_kit_cannot_fit_place_or_follow_is_raised_by_its_code()
        {
            string json = "{\"version\": 2, \"extensions\": {\"task\": {\"tools\": {\"LID\": {\"tip\": [0, 1]}}, \"pace\": 2}}, " +
                          "\"steps\": [{\"id\": \"A01\", \"actions\": [" +
                          "{\"behavior\": \"play\", \"text\": \"Door open Animation plays\", \"clip\": \"A01\"}," +
                          "{\"behavior\": \"teleport\", \"text\": \"The user is teleported facing the lid\", \"fields\": {\"facing\": \"CABINET|LID\"}}]}]}";
            var plan = SheetPlan.Read(json, "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            settings.rig = TaskRig.None;
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);  // the lid shut in a crate: nothing outside it sees it
            crate.name = "CRATE";
            crate.transform.SetParent(_root.transform.Find("CABINET"));
            crate.transform.SetPositionAndRotation(new Vector3(0, 1f, 0), Quaternion.identity);
            crate.transform.localScale = Vector3.one * 3f;
            try
            {
                var ctx = new OpContext();
                ManifestsActions.Build(plan, _scene.Use(settings), ctx);
                bool Said(string code, string words) => ctx.Issues.Any(i => i.code == code && i.message.Contains(words));
                Assert.That(Said(TaskKit.ExtensionCode, "task tools are not read: a tool rigged off its work is adjusted on the tool"),
                            "a settings file's tool table, which the kit no longer reads, is said with where its adjustment goes");
                Assert.That(Said(TaskKit.ExtensionCode, "holds 'pace', which the kit does not read"), "and anything else it holds");
                Assert.That(Said(TaskKit.FollowsNothingCode, "plays after no clip of its step"), "a play row with nothing to follow");
                Assert.That(Said(TaskKit.SpotCode, "no spot on the floor sees 'LID'"), "no spot sees what the teleport faces: said");
            }
            finally
            {
                Object.DestroyImmediate(settings);
                var viewpoints = GameObject.Find(TaskKit.ViewpointsName(plan));
                if (viewpoints != null) Object.DestroyImmediate(viewpoints);
            }
        }

        [Test]
        public void A_step_whose_acting_row_is_held_back_plans_its_next_button_as_its_run_builds_it()
        {
            // The select row names a node the scene lacks: held back, it builds nothing for its step to wait on.
            var plan = Plan("{\"behavior\": \"select\", \"text\": \"Selecting the hatch\", \"targets\": [\"CABINET|HATCH\"]}," +
                            "{\"behavior\": \"pause\", \"text\": \"Selecting 'Next' triggers step advancement\", \"fields\": {\"label\": \"Next\"}}");
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            settings.rig = TaskRig.None;
            try
            {
                _scene.Use(settings);
                var dry = new OpContext(dryRun: true);
                ManifestAssessment assessed = ManifestsActions.Build(plan, _scene, dry);
                Assert.That(assessed.rows[0].state, Is.EqualTo(ManifestAssessment.State.Unresolved));
                var live = new OpContext();
                ManifestsActions.Build(plan, _scene, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))),
                            "the plan is the run: Next is not held back for a row that built nothing");
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, _scene, again);
                Assert.That(again.Changes, Is.Empty, "a second build plans nothing");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void Build_builds_the_ready_actions_and_raises_the_rest()
        {
            var plan = Plan(Collider + ",{\"behavior\": \"no_builder_yet\", \"text\": \"Number pad appears\"}");
            var ctx = new OpContext();
            ManifestsActions.Build(plan, _scene, ctx);
            Assert.That(_scene.Node("CABINET|DOOR_GRP").GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(ctx.Issues.Select(i => i.code), Is.EqualTo(new[] { ManifestsActions.UnsupportedCode }));
        }

        [Test]
        public void An_object_is_offered_only_behaviors_that_act_on_an_object()
        {
            Assert.That(ManifestBehaviors.Mark("advance").NamesObjects, Is.False, "a step's own");
            Assert.That(ManifestBehaviors.Mark("highlight").NamesObjects, Is.True);
            var offered = ManifestsActions.Assignable(_scene, "CABINET|DOOR_GRP");
            Assert.That(offered, Does.Contain("indicate").And.Contain("select"));
            Assert.That(offered, Does.Not.Contain("advance").And.Not.Contain("note").And.Not.Contain("number_pad"),
                        "going on, a note, a number pad: nothing an object is given");
        }

        [Test]
        public void Build_is_one_undo_step()
        {
            var plan = Plan(Collider);
            OpContext.RunMenu("Build Tasks", ctx => ManifestsActions.Build(plan, _scene, ctx));
            Assert.That(_scene.Node("CABINET|DOOR_GRP").GetComponent<BoxCollider>(), Is.Not.Null);
            Undo.PerformUndo();
            Assert.That(_scene.Node("CABINET|DOOR_GRP").GetComponent<BoxCollider>() == null, "one Ctrl+Z took the build away");
        }

        [Test]
        public void Build_plans_what_it_then_does_and_a_second_build_plans_nothing()
        {
            var plan = Plan(Collider);
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, _scene, dry);
            Assert.That(_scene.Node("CABINET|DOOR_GRP").GetComponent<BoxCollider>() == null, "a dry run builds nothing");
            var live = new OpContext();
            ManifestsActions.Build(plan, _scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(live.Changes, Is.Not.Empty);
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, _scene, again);
            Assert.That(again.Changes, Is.Empty);
        }

        // A task of every placard-, marker- and press-built behavior, each step its own.
        const string Task = "{\"version\": 2, \"steps\": [" +
            "{\"id\": \"A01\", \"actions\": [" +
            "{\"behavior\": \"use\", \"text\": \"Pressing and holding the door plays Door open animation\", \"targets\": [\"CABINET|DOOR_GRP\"], \"clip\": \"A01\", \"settings\": {\"useTrigger\": \"HoldToPlay\"}}," +
            "{\"behavior\": \"marker\", \"text\": \"Destination circle fades in\"}," +
            "{\"behavior\": \"checklist\", \"text\": \"A checklist appears on the placard: Door open, Lid checked\", \"fields\": {\"label\": \"Door open, Lid checked\"}}]}," +
            "{\"id\": \"A015\", \"actions\": [" +
            "{\"behavior\": \"show\", \"text\": \"Highlighted lid appears in place\", \"targets\": [\"CABINET|LID\"]}," +
            "{\"behavior\": \"quiz\", \"text\": \"Selecting 'Take Quiz' triggers advancement to the quiz\", \"fields\": {\"label\": \"Take Quiz\"}}]}," +
            "{\"id\": \"A02\", \"actions\": [" +
            "{\"behavior\": \"teleport\", \"text\": \"The user is teleported to VIEW_FRONT\", \"targets\": [\"CABINET|VIEW_FRONT\"]}," +
            "{\"behavior\": \"marker\", \"text\": \"Destination circle fades away\", \"settings\": {\"markerShow\": \"Hide\"}}," +
            "{\"behavior\": \"check\", \"text\": \"Checklist item 'Lid checked' gets a check mark\", \"fields\": {\"label\": \"Lid checked\"}}]}," +
            "{\"id\": \"Q01\", \"actions\": [" +
            "{\"behavior\": \"option\", \"text\": \"*Wrench\", \"fields\": {\"label\": \"Wrench\", \"value\": \"correct\"}}," +
            "{\"behavior\": \"option\", \"text\": \"Hammer\", \"fields\": {\"label\": \"Hammer\"}}," +
            "{\"behavior\": \"option\", \"text\": \"Pliers\", \"fields\": {\"label\": \"Pliers\"}}," +
            "{\"behavior\": \"goto\", \"text\": \"Selecting 'Pliers' triggers step A01\", \"fields\": {\"label\": \"Pliers\"}, \"to\": \"A01\"}]}," +
            "{\"id\": \"D08\", \"actions\": [" +
            "{\"behavior\": \"number_pad\", \"text\": \"A number pad appears on the placard\"}," +
            "{\"behavior\": \"answer\", \"text\": \"Entering '1337' triggers step D10\", \"fields\": {\"value\": \"1337\"}, \"to\": \"D10\"}," +
            "{\"behavior\": \"answer\", \"text\": \"Entering any other number triggers step Q01\", \"to\": \"Q01\", \"settings\": {\"answerKind\": \"Otherwise\"}}]}," +
            "{\"id\": \"D10\", \"actions\": [" +
            "{\"behavior\": \"checklist\", \"text\": \"Checklist appears on placard with blank check boxes\"}]}," +
            "{\"id\": \"D11\", \"actions\": [" +
            "{\"behavior\": \"check\", \"text\": \"Checklist item 'Bolts out' gets a check mark\", \"fields\": {\"label\": \"Bolts out\"}}]}]}";

        [Test]
        public void Press_marker_question_key_pad_and_checklist_rows_build_as_planned_once()
        {
            var view = new GameObject("VIEW_FRONT").transform;
            view.SetParent(_root.transform.Find("CABINET"));
            _scene = new ManifestScene(_root, new[] { new AnimationClip { name = "A01" } });  // a scene finds its nodes as it is made
            var plan = SheetPlan.Read(Task, "task", out string refusal);
            Assert.That(refusal, Is.Null);
            var dry = new OpContext(dryRun: true);
            ManifestAssessment assessment = ManifestsActions.Build(plan, _scene, dry);
            Assert.That(assessment.rows.Select(r => r.state), Is.All.EqualTo(ManifestAssessment.State.Ready),
                        string.Join("; ", assessment.rows.SelectMany(r => r.problems)));
            Assert.That(dry.Issues, Is.Empty, string.Join("; ", dry.Issues.Select(i => i.message)));
            var live = new OpContext();
            ManifestsActions.Build(plan, _scene, live);
            Assert.That(live.Issues, Is.Empty, string.Join("; ", live.Issues.Select(i => i.message)));
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, _scene, again);
            Assert.That(again.Changes, Is.Empty);

            var sequence = GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>();
            var door = _root.transform.Find("CABINET/DOOR_GRP").GetComponent<M3trik.Unitytk.Interaction.Target>();
            Assert.That(door.onPressed.GetPersistentTarget(0), Is.InstanceOf<M3trik.Unitytk.Interaction.ClipTrigger>(), "through its step's trigger");
            Assert.That(door.onPressed.GetPersistentMethodName(0), Is.EqualTo(nameof(M3trik.Unitytk.Interaction.ClipTrigger.Hold)));
            Assert.That(door.onReleased.GetPersistentMethodName(0), Is.EqualTo(nameof(M3trik.Unitytk.Interaction.ClipTrigger.Release)));

            var marker = view.Find(M3trik.Unitytk.Indicators.IndicatorsActions.MarkerName).GetComponent<M3trik.Unitytk.Indicators.Marker>();
            Assert.That(sequence.Find("A01").turnsOn, Does.Contain(marker), "at the next teleport's viewpoint, from A01 on");
            Assert.That(sequence.Find("A02").turnsOff.Single(), Is.SameAs(marker));

            Transform placard = GameObject.Find("Placard").transform;
            var list = placard.Find(TaskKit.ChecklistName("A01")).GetComponent<M3trik.Unitytk.Panels.PlacardList>();
            Assert.That(list.items, Is.EqualTo(new[] { "Door open", "Lid checked" }));
            Assert.That(sequence.Find("A01").turnsOn, Does.Contain(list));
            var tick = list.GetComponentInChildren<M3trik.Unitytk.Panels.PlacardTick>(true);
            Assert.That((tick.item, sequence.Find("A02").ends.Single()), Is.EqualTo(("Lid checked", (Behaviour)tick)), "ticked as A02 ends");

            var q01 = placard.Find("Q01").GetComponent<M3trik.Unitytk.Panels.PlacardPage>();
            Assert.That(q01.choices, Is.EqualTo(new[] { "Wrench", "Hammer", "Pliers" }));
            Assert.That(q01.replies.Select(r => (r.choice, r.text)), Is.EqualTo(new[] { ("Hammer", ScriptableObject.CreateInstance<TaskSettings>().wrongChoiceReply) }),
                        "a wrong answer says try again; one a row sends somewhere does not");
            Assert.That(sequence.Find("Q01").Choice("Wrench").to, Is.Empty, "the right answer goes on");
            Assert.That(sequence.Find("Q01").Choice("Hammer"), Is.Null, "a wrong one stays");

            Assert.That(placard.Find("D08").GetComponent<M3trik.Unitytk.Panels.PlacardPage>().keys, Is.EqualTo(ScriptableObject.CreateInstance<TaskSettings>().numberKeys));
            var blank = placard.Find(TaskKit.ChecklistName("D10")).GetComponent<M3trik.Unitytk.Panels.PlacardList>();
            Assert.That(blank.items, Is.EqualTo(new[] { "Bolts out" }), "a blank checklist lists the checks that follow it");
            Assert.That(blank.GetComponentInChildren<M3trik.Unitytk.Panels.PlacardTick>(true).item, Is.EqualTo("Bolts out"));
            Assert.That(sequence.Find("A015").Choice("Take Quiz").to, Is.EqualTo("Q01"), "the quiz: the first step asking a question");
            Assert.That(placard.Find("A015").GetComponent<M3trik.Unitytk.Panels.PlacardPage>().choices, Is.EqualTo(new[] { "Take Quiz" }));
            Assert.That(_root.transform.Find("CABINET/LID").GetComponent<M3trik.Unitytk.RenderEffects.FadeTo>(), Is.Not.Null, "shown by a fade in");
            sequence.Begin();
            sequence.GoTo("D08");
            sequence.Choose("42");
            Assert.That(sequence.Current.id, Is.EqualTo("Q01"), "any other number");
            sequence.GoTo("D08");
            sequence.Choose("1337");
            Assert.That(sequence.Current.id, Is.EqualTo("D10"));
        }

        [Test]
        public void A_marker_with_no_place_and_a_tick_with_no_list_are_unresolved()
        {
            var plan = Plan("{\"behavior\": \"marker\", \"text\": \"Destination circle fades in\"}," +
                            "{\"behavior\": \"check\", \"text\": \"Checklist item 'Lid' gets a check mark\", \"fields\": {\"label\": \"Lid\"}}," +
                            "{\"behavior\": \"marker\", \"text\": \"Destination circle fades away\", \"settings\": {\"markerShow\": \"Hide\"}}");
            ManifestAssessment assessment = ManifestsActions.Assess(plan, _scene);
            Assert.That(assessment.rows.Select(r => r.state), Is.All.EqualTo(ManifestAssessment.State.Unresolved));
            Assert.That(assessment.rows[0].problems.Single(), Does.Contain("no teleport follows it"));
            Assert.That(assessment.rows[1].problems.Single(), Does.Contain("no checklist before it lists 'Lid'"));
        }

        [Test]
        public void A_lamp_turned_on_glows_in_its_rows_colour()
        {
            // A turn_on row's colour (the sheet's color, a highlight's) was read by no one: the glow kept the kit's.
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lamp.name = "LAMP";
            lamp.transform.SetParent(_root.transform.Find("CABINET"));
            var plan = Plan("{\"behavior\": \"turn_on\", \"text\": \"The lamp turns on\", \"targets\": [\"CABINET|LAMP\"], " +
                            "\"settings\": {\"highlightColor\": \"#3399FF\"}}");
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            settings.rig = TaskRig.None;
            try
            {
                var scene = new ManifestScene(_root, new AnimationClip[0]).Use(settings);
                ManifestsActions.Build(plan, scene, new OpContext());
                var glow = lamp.GetComponent<M3trik.Unitytk.RenderEffects.Highlight>();
                Assert.That(glow.pulse, Is.False, "steady");
                Assert.That(ColorUtility.ToHtmlStringRGB(glow.color), Is.EqualTo("3399FF"), "in its row's colour");
                Assert.That(glow.colorDim, Is.EqualTo(M3trik.Unitytk.RenderEffects.Highlight.DimOf(glow.color)), "its dim colour with it");
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, new ManifestScene(_root, new AnimationClip[0]).Use(settings), again);
                Assert.That(again.Changes, Is.Empty, "a second build changes nothing");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void A_marker_of_a_teleport_that_cannot_be_built_is_unresolved_with_it_and_the_build_goes_on()
        {
            // A marker took the next teleport's viewpoint whether or not that row could be built: one naming a viewpoint the
            // delivery lacks threw as the marker was built, and the whole build stopped there.
            string json = "{\"version\": 2, \"steps\": [" +
                          "{\"id\": \"A01\", \"actions\": [{\"behavior\": \"marker\", \"text\": \"Destination circle fades in\"}]}," +
                          "{\"id\": \"A02\", \"actions\": [{\"behavior\": \"teleport\", \"text\": \"The user is teleported to VIEW_GONE\", " +
                          "\"targets\": [\"CABINET|VIEW_GONE\"]}]}]}";
            var plan = SheetPlan.Read(json, "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            ManifestAssessment assessment = ManifestsActions.Assess(plan, _scene);
            Assert.That(assessment.rows.Select(r => r.state), Is.EqualTo(new[] { ManifestAssessment.State.Unresolved, ManifestAssessment.State.Unresolved }));
            Assert.That(assessment.rows[0].problems.Single(), Does.Contain("the teleport it marks (A02:").And.Contain("cannot be built")
                                                                 .And.Contain("'CABINET|VIEW_GONE' is no node"));
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            settings.rig = TaskRig.None;
            try
            {
                var dry = new OpContext(dryRun: true);
                Assert.DoesNotThrow(() => ManifestsActions.Build(plan, _scene.Use(settings), dry));
                var live = new OpContext();
                Assert.DoesNotThrow(() => ManifestsActions.Build(plan, _scene, live), "each row it cannot build is raised and left out");
                Assert.That(live.Issues.Count(i => i.code == ManifestsActions.UnresolvedCode), Is.EqualTo(2));
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void A_place_whose_spot_is_part_of_what_it_places_is_unresolved()
        {
            // Measured on a real delivery: the sheet's slot was the placed part's own body, so it rode into the hand.
            new GameObject("BODY").transform.SetParent(_root.transform.Find("CABINET/LID"));
            var plan = Plan("{\"behavior\": \"place\", \"text\": \"Moving the lid close to its slot plays its install\", " +
                            "\"targets\": [\"CABINET|LID|BODY\"], \"fields\": {\"tool\": \"CABINET|LID\"}, \"clip\": \"A01\"}," +
                            "{\"behavior\": \"place\", \"text\": \"Moving the lid close to the door plays its install\", " +
                            "\"targets\": [\"CABINET|DOOR_GRP\"], \"fields\": {\"tool\": \"CABINET|LID\"}, \"clip\": \"A01\"}");
            ManifestAssessment assessment = ManifestsActions.Assess(plan, new ManifestScene(_root, new[] { new AnimationClip { name = "A01" } }));
            Assert.That(assessment.rows.Select(r => r.state), Is.EqualTo(new[] { ManifestAssessment.State.Unresolved, ManifestAssessment.State.Ready }));
            Assert.That(assessment.rows[0].problems.Single(), Does.Contain("'BODY' is part of 'LID'").And.Contain("Repair the sheet").And.Contain("set its Take and Tool"));
        }

        [Test]
        public void A_part_a_place_row_brings_to_its_slot_stays_where_its_take_puts_it_unless_its_settings_say_otherwise()
        {
            // A filter fitted into its slot came back to the hand as its take ended, and was carried to the end of the task.
            var install = new AnimationClip { name = "A01" };
            install.SetCurve("CABINET/LID", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            var plan = Plan("{\"behavior\": \"take\", \"text\": \"Selecting the lid places it in hand\", \"targets\": [\"CABINET|LID\"]}," +
                            "{\"behavior\": \"place\", \"text\": \"Moving the lid close to the door plays its install\", " +
                            "\"targets\": [\"CABINET|DOOR_GRP\"], \"fields\": {\"tool\": \"CABINET|LID\"}, \"clip\": \"A01\"}");
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            settings.rig = TaskRig.None;
            try
            {
                var scene = new ManifestScene(_root, new[] { install }).Use(settings);
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, scene, dry);
                var live = new OpContext();
                ManifestsActions.Build(plan, scene, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))),
                            "the plan is the run");
                var lid = _root.transform.Find("CABINET/LID").GetComponent<M3trik.Unitytk.Interaction.Holdable>();
                var entry = _root.GetComponent<M3trik.Unitytk.Interaction.ClipPlayer>().Find("A01");
                Assert.That(entry.leaves, Is.EqualTo(new[] { lid }), "placed, it stays where its take puts it");
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, scene, again);
                Assert.That(again.Changes, Is.Empty, "a second build plans nothing");
                settings.placedStays = false;  // a thing brought near that the hand keeps (a scanner held up to a tag)
                ManifestsActions.Build(plan, scene, new OpContext());
                Assert.That(entry.leaves, Is.Empty, "the setting off: it comes back to the hand as its take ends");
            }
            finally
            {
                Object.DestroyImmediate(settings);
                Object.DestroyImmediate(install);
            }
        }

        [Test]
        public void Each_task_builds_by_its_own_settings_and_a_projects_behavior_can_act_on_parts()
        {
            var step = new SheetPlan.Shape.PlanStep
            {
                id = "A01", fields = new Dictionary<string, object> { ["voice"] = "one two three four five six seven eight nine ten" },
            };
            var defaults = ScriptableObject.CreateInstance<TaskSettings>();
            Assert.That(TaskKit.DwellOf(step, defaults), Is.EqualTo(10f / defaults.wordsPerSecond).Within(1e-4f), "the defaults");
            var brisk = ScriptableObject.CreateInstance<TaskSettings>();
            brisk.wordsPerSecond = 5f;
            brisk.minimumDwell = 0f;
            Assert.That(TaskKit.DwellOf(step, brisk), Is.EqualTo(2f).Within(1e-4f), "a task read faster runs shorter");
            var scene = new ManifestScene(_root, new AnimationClip[0]).Use(brisk);
            Assert.That(TaskKit.DwellOf(step, scene), Is.EqualTo(2f).Within(1e-4f), "by the scene's own settings");

            Assert.That(TaskBehaviors.Traits("select").ActsOn && TaskBehaviors.Traits("take").ActsOn, "the built-in behaviors that act on parts");
            Assert.That(TaskBehaviors.Traits("highlight").ActsOn, Is.False, "pointing a part out is not acting on it");
            Assert.That(TaskBehaviors.Traits("test_acting").ActsOn, Is.True, "a project's own behavior, by its declaration");
        }

        [Test]
        public void A_target_of_two_things_apart_gets_an_arrow_at_each_and_a_dry_run_plans_both()
        {
            // "Arrows point at both door locks": one group, two locks half a metre apart. Added: 2026-11-22
            Transform cabinet = _root.transform.Find("CABINET");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "FLOOR";
            floor.transform.SetParent(cabinet);
            floor.transform.SetPositionAndRotation(new Vector3(0, -0.05f, 0), Quaternion.identity);
            floor.transform.localScale = new Vector3(20, 0.1f, 20);
            var locks = new GameObject("LOCKS_GRP").transform;
            locks.SetParent(cabinet);
            foreach (var (name, y) in new[] { ("LOCK_UPPER", 1.5f), ("LOCK_LOWER", 0.9f) })
            {
                var lockBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lockBox.name = name;
                lockBox.transform.SetParent(locks);
                lockBox.transform.SetPositionAndRotation(new Vector3(0, y, 0), Quaternion.identity);
                lockBox.transform.localScale = Vector3.one * 0.04f;
            }
            var scene = new ManifestScene(_root, new AnimationClip[0]);
            var plan = Plan("{\"behavior\": \"indicate\", \"text\": \"Arrows point at both locks\", \"targets\": [\"CABINET|LOCKS_GRP\"]}");
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));

            var arrows = locks.GetComponentsInChildren<Indicators.Indicator>().ToDictionary(a => a.name);
            Assert.That(arrows.Keys, Is.EquivalentTo(new[] { "Indicator A01 LOCK_UPPER", "Indicator A01 LOCK_LOWER" }), "an arrow at each lock");
            Vector3 upper = locks.Find("LOCK_UPPER").position, lower = locks.Find("LOCK_LOWER").position;
            Vector3 at = arrows["Indicator A01 LOCK_UPPER"].transform.position;
            Assert.That(Vector3.Distance(at, upper), Is.LessThan(Vector3.Distance(at, lower)), "each beside its own lock, not between them");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, again);
            Assert.That(again.Changes, Is.Empty, "a second build changes nothing");
        }

        [Test]
        public void An_object_its_step_returns_goes_back_shown_unless_the_task_fades_it_too()
        {
            // "Keys fade away from hand", then "Return keys to table": the keys went home hidden. Added: 2026-11-23
            const string rows = "{\"behavior\": \"fade_out\", \"text\": \"The lid fades away from hand\", \"targets\": [\"CABINET|LID\"]}," +
                                "{\"behavior\": \"return\", \"text\": \"Return the lid\", \"targets\": [\"CABINET|LID\"]}," +
                                "{\"behavior\": \"fade_out\", \"text\": \"The door fades away\", \"targets\": [\"CABINET|DOOR_GRP\"]}";
            var live = new OpContext();
            ManifestsActions.Build(Plan(rows), _scene, live);
            Transform lid = _root.transform.Find("CABINET/LID"), door = _root.transform.Find("CABINET/DOOR_GRP");
            Assert.That(lid.GetComponent<M3trik.Unitytk.RenderEffects.FadeTo>(), Is.Null, "sent home shown");
            Assert.That(door.GetComponent<M3trik.Unitytk.RenderEffects.FadeTo>(), Is.Not.Null, "what its step does not return fades as before");
            Assert.That(live.Issues.Single(i => i.code == TaskKit.FadeReturnedCode).message, Does.Contain("'LID' goes back shown"));

            var fades = ScriptableObject.CreateInstance<TaskSettings>();
            fades.fadeReturned = true;
            ManifestsActions.Build(Plan(rows), new ManifestScene(_root, new[] { new AnimationClip { name = "A01" } }).Use(fades), new OpContext());
            Assert.That(lid.GetComponent<M3trik.Unitytk.RenderEffects.FadeTo>(), Is.Not.Null, "the task's choice: it fades, as the sheet says");
        }

        [Test]
        public void A_use_is_a_click_unless_an_object_of_its_own_is_held_to_play_or_a_timed_press()
        {
            // The user: press and hold one button (release stops, optionally plays back), per object, never by default; a timed press a setting.
            var plan = Plan("{\"behavior\": \"use\", \"text\": \"Selecting the button lowers the door\", \"targets\": [\"CABINET|DOOR_GRP\", \"CABINET|LID\"], \"clip\": \"A01\"}");
            var quiet = ScriptableObject.CreateInstance<TaskSettings>();
            quiet.rig = TaskRig.None;
            quiet.debugTools = false;
            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                ManifestScene Scene() => new ManifestScene(_root, new[] { new AnimationClip { name = "A01" } }) { Manifest = manifest }.Use(quiet);
                ManifestsActions.Build(plan, Scene(), new OpContext());
                GameObject door = _root.transform.Find("CABINET/DOOR_GRP").gameObject, lid = _root.transform.Find("CABINET/LID").gameObject;
                Assert.That(door.GetComponents<M3trik.Unitytk.Interaction.ClipTrigger>().Single().whileHeld, Is.False, "by default a click plays it");

                ManifestsActions.SetTune(manifest, "A01", "CABINET|DOOR_GRP", "use", nameof(TaskSettings.useTrigger), nameof(UseTrigger.HoldToPlay));
                ManifestsActions.SetTune(manifest, "A01", "CABINET|DOOR_GRP", "use", nameof(TaskSettings.releaseRewinds), "true");
                ManifestsActions.SetTune(manifest, "A01", "CABINET|LID", "use", nameof(TaskSettings.useTrigger), nameof(UseTrigger.TimedPress));
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, Scene(), dry);
                var live = new OpContext();
                ManifestsActions.Build(plan, Scene(), live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                var held = door.GetComponents<M3trik.Unitytk.Interaction.ClipTrigger>().Single();
                Assert.That((held.whileHeld, held.reverseOnRelease), Is.EqualTo((true, true)), "its own: held to play, played back as let go");
                var timed = lid.GetComponents<M3trik.Unitytk.Interaction.ClipTrigger>().Single();
                Assert.That((timed.whileHeld, timed.pressSeconds), Is.EqualTo((false, quiet.pressSeconds)), "its own: a timed press, the task's seconds");
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, Scene(), again);
                Assert.That(again.Changes, Is.Empty, "a second build plans nothing");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void A_part_selected_without_its_tool_tells_the_user_to_take_it()
        {
            // A lock clicked before the keys are taken was silent. Added: 2026-11-22
            var plan = Plan("{\"behavior\": \"use\", \"text\": \"Selecting the door with the lid opens it\", \"targets\": [\"CABINET|DOOR_GRP\"], " +
                            "\"fields\": {\"tool\": \"CABINET|LID\"}, \"clip\": \"A01\"}");
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, _scene, dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, _scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var target = _root.transform.Find("CABINET/DOOR_GRP").GetComponent<M3trik.Unitytk.Interaction.Target>();
            var placard = GameObject.Find("Placard").GetComponent<M3trik.Unitytk.Panels.Placard>();
            Assert.That((target.onRefused.GetPersistentTarget(0), target.onRefused.GetPersistentMethodName(0)),
                        Is.EqualTo(((Object)placard, nameof(M3trik.Unitytk.Panels.Placard.Tell))));
            var said = new UnityEditor.SerializedObject(target).FindProperty("onRefused.m_PersistentCalls.m_Calls.Array.data[0].m_Arguments.m_StringArgument");
            Assert.That(said.stringValue, Is.EqualTo("Take the lid first."), "the task's line, the tool by its readable name");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, _scene, again);
            Assert.That(again.Changes, Is.Empty);
        }

        // The real task showed every arrow of a step at once (at the keys on the table and at both locks), each lock's
        // arrow stayed over the key as it turned, and a tool glowed in the hand.
        [Test]
        public void A_cue_at_what_a_step_has_the_user_act_on_waits_on_the_hand_and_one_at_what_they_only_look_at_shows_throughout()
        {
            const string Rows =
                "{\"behavior\": \"indicate\", \"text\": \"Arrow points at the lid\", \"targets\": [\"CABINET|LID\"]}," +
                "{\"behavior\": \"highlight\", \"text\": \"The door is highlighted\", \"targets\": [\"CABINET|DOOR_GRP\"]SET}," +
                "{\"behavior\": \"take\", \"text\": \"Selecting the lid takes it\", \"targets\": [\"CABINET|LID\"]}," +
                "{\"behavior\": \"use\", \"text\": \"Selecting the door with the lid opens it\", \"targets\": [\"CABINET|DOOR_GRP\"], " +
                "\"fields\": {\"tool\": \"CABINET|LID\"}, \"clip\": \"A01\"}";
            SheetPlan.Shape.Payload Two(string set)
            {
                string json = "{\"version\": 2, \"steps\": [{\"id\": \"A01\", \"actions\": [" + Rows.Replace("SET", set) + "]}, " +
                              "{\"id\": \"A02\", \"actions\": [{\"behavior\": \"highlight\", \"text\": \"The door is highlighted\", " +
                              "\"targets\": [\"CABINET|DOOR_GRP\"]}]}]}";
                var read = SheetPlan.Read(json, "test plan", out string refusal);
                Assert.That(refusal, Is.Null);
                return read;
            }
            var plan = Two("");
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, _scene, dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, _scene, live);
            Assert.That(live.Issues, Is.Empty);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));

            Transform door = _root.transform.Find("CABINET/DOOR_GRP"), lid = _root.transform.Find("CABINET/LID");
            var glow = door.GetComponent<M3trik.Unitytk.RenderEffects.Highlight>();
            var arrow = lid.GetComponentInChildren<M3trik.Unitytk.Indicators.Indicator>(true);
            var glows = door.GetComponent<M3trik.Unitytk.Interaction.TargetCue>();
            var points = arrow.GetComponent<M3trik.Unitytk.Interaction.TargetCue>();
            Assert.That((glows.cue, glows.targets.Single(), glows.waitsForHand, glows.goesOnceActed), Is.EqualTo(((Behaviour)glow, door.gameObject, true, true)),
                        "the door's glow waits for the lid in hand and goes once the door is used");
            Assert.That((points.cue, points.targets.Single()), Is.EqualTo(((Behaviour)arrow, lid.gameObject)), "the lid's arrow goes as it is taken");
            var sequence = GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>();
            var first = sequence.Find("A01");
            Assert.That(first.activates.Contains(glows) && first.activates.Contains(points), "its step switches what shows them");
            Assert.That(first.activates.Contains(glow) || first.activates.Contains(arrow), Is.False,
                        "never the cues themselves: both would be on from the step's start");
            Assert.That(sequence.Find("A02").activates.Contains(glow), "a step that only shows the door glows it throughout");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, _scene, again);
            Assert.That(again.Changes, Is.Empty, "a second build changes nothing");

            // By its setting a cue shows for its whole step: what switches it stays, set neither to wait nor to go.
            ManifestsActions.Build(Two(", \"settings\": {\"cueWhen\": \"Step\"}"), _scene, new OpContext());
            Assert.That(glows.waitsForHand || glows.goesOnceActed, Is.False);
            Assert.That(points.waitsForHand && points.goesOnceActed, "the arrow's row says nothing: it keeps the task's");
        }

        [Test]
        public void A_checklist_goes_from_the_placard_as_the_step_its_span_names_starts()
        {
            // The real task's checklist, done with at its section's end, ran over the buttons of every page after it.
            string json = "{\"version\": 2, \"steps\": [" +
                          "{\"id\": \"A01\", \"actions\": [{\"behavior\": \"checklist\", \"text\": \"A checklist is shown\", \"fields\": {\"label\": \"Door, Lid\"}}]}, " +
                          "{\"id\": \"A02\", \"actions\": [{\"behavior\": \"checklist\", \"text\": \"A checklist is shown, until A02\", " +
                          "\"settings\": {\"checklistShow\": \"Hide\"}}]}]}";
            var plan = SheetPlan.Read(json, "test plan", out string refusal);
            Assert.That(refusal, Is.Null);
            Assert.That(ManifestsActions.Assess(plan, _scene).rows.Select(r => r.state), Is.All.EqualTo(ManifestAssessment.State.Ready));
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, _scene, dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, _scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var sequence = GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>();
            var list = GameObject.Find("Placard").GetComponentInChildren<M3trik.Unitytk.Panels.PlacardList>(true);
            Assert.That(sequence.Find("A01").turnsOn.Contains(list), "up from its step on");
            Assert.That(sequence.Find("A02").turnsOff.Contains(list), "and off as the step its span names starts");
            Assert.That(GameObject.Find("Placard").GetComponentsInChildren<M3trik.Unitytk.Panels.PlacardList>(true).Length, Is.EqualTo(1), "the row that takes it away makes no list");

            var alone = SheetPlan.Read("{\"version\": 2, \"steps\": [{\"id\": \"A01\", \"actions\": [{\"behavior\": \"checklist\", " +
                                       "\"text\": \"The checklist goes away\", \"settings\": {\"checklistShow\": \"Hide\"}}]}]}", "test plan", out refusal);
            Assert.That(ManifestsActions.Assess(alone, _scene).rows.Single().problems.Single(), Is.EqualTo("no row before it shows a checklist"));
        }

        [Test]
        public void A_behavior_says_what_kind_of_row_it_is_and_a_projects_behavior_fills_its_page()
        {
            Assert.That(TaskBehaviors.Traits("pause").Offers && TaskBehaviors.Traits("pause").EndsStep, "pause: a button that goes on");
            Assert.That(TaskBehaviors.Traits("option").Offers && !TaskBehaviors.Traits("option").EndsStep, "an answer: a button that may not");
            Assert.That(TaskBehaviors.Traits("hold").Holds && TaskBehaviors.Traits("take").Holds, "the hand holds what they name");
            Assert.That(TaskBehaviors.Traits("teleport").Places && !TaskBehaviors.Traits("highlight").Places, "a place, not a part to see");
            var none = TaskBehaviors.Traits("no_such_behavior");
            Assert.That(none.ActsOn || none.Offers || none.EndsStep || none.Holds || none.Places, Is.False, "no builder: no traits");

            var plan = Plan("{\"behavior\": \"test_acting\", \"text\": \"Look the door over\", \"fields\": {\"label\": \"Look the door over\"}, " +
                            "\"targets\": [\"CABINET|DOOR_GRP\"]}");
            ManifestsActions.Build(plan, _scene);
            var page = GameObject.Find("Placard").transform.Find("A01").GetComponent<M3trik.Unitytk.Panels.PlacardPage>();
            Assert.That(page.text, Is.EqualTo("Look the door over"), "its row, on its step's page (ITaskPageContent)");
        }

        [Test]
        public void A_step_runs_its_reading_time_a_goto_to_the_start_restarts_and_its_page_keeps_clear_of_its_targets()
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);  // a viewpoint carrying its destination disc
            disc.name = "VIEW_FRONT";
            disc.transform.SetParent(_root.transform.Find("CABINET"));
            var scene = new ManifestScene(_root, new AnimationClip[0]);
            string json = "{\"version\": 2, \"steps\": [" +
                "{\"id\": \"A01\", \"fields\": {\"voice\": \"one two three four five six seven eight nine ten\", \"placard\": \"Look at the door\"}, \"actions\": [" +
                "{\"behavior\": \"highlight\", \"text\": \"Highlight the door\", \"targets\": [\"CABINET|DOOR_GRP\"]}," +
                "{\"behavior\": \"teleport\", \"text\": \"The user is teleported to VIEW_FRONT\", \"targets\": [\"CABINET|VIEW_FRONT\"]}," +
                "{\"behavior\": \"advance\", \"text\": \"Auto Advance\"}]}," +
                "{\"id\": \"A02\", \"actions\": [" +
                "{\"behavior\": \"goto\", \"text\": \"Selecting 'Restart Tasks' triggers step A01\", \"fields\": {\"label\": \"Restart Tasks\"}, \"to\": \"A01\", \"restart\": true}]}]}";  // the reading marks a jump to the first step
            var plan = SheetPlan.Read(json, "task", out string refusal);
            Assert.That(refusal, Is.Null);
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, again);
            Assert.That(again.Changes, Is.Empty);

            var sequence = GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>();
            Assert.That(sequence.Find("A01").dwell, Is.EqualTo(10f / ScriptableObject.CreateInstance<TaskSettings>().wordsPerSecond).Within(1e-4f), "its voice line, spoken");
            Assert.That(sequence.Find("A02").Choice("Restart Tasks").Restarts, Is.True, "back to the first step: start over");
            var page = GameObject.Find("Placard").transform.Find("A01").GetComponent<M3trik.Unitytk.Panels.PlacardPage>();
            Assert.That(page.keepClear, Is.EqualTo(new[] { _root.transform.Find("CABINET/DOOR_GRP") }), "what it shows, not where it goes");
            var marker = disc.transform.Find(M3trik.Unitytk.Indicators.IndicatorsActions.MarkerName).GetComponent<M3trik.Unitytk.Indicators.Marker>();
            Assert.That(marker.enabled, Is.False, "the viewpoint's disc is a marker, off until a row shows it");
        }

        [Test]
        public void A_step_that_offers_no_choice_goes_on_by_itself_unless_its_advance_is_turned_off()
        {
            // "Do we really need an advance behavior, isn't that just the default?" Added: 2026-11-23
            string json = "{\"version\": 2, \"steps\": [" +
                "{\"id\": \"A01\", \"fields\": {\"voice\": \"Look at the door.\"}, \"actions\": [" +
                "{\"behavior\": \"highlight\", \"text\": \"Highlight the door\", \"targets\": [\"CABINET|DOOR_GRP\"]}]}," +
                "{\"id\": \"A02\", \"actions\": [" +
                "{\"behavior\": \"goto\", \"text\": \"Selecting 'Again' triggers step A01\", \"fields\": {\"label\": \"Again\"}, \"to\": \"A01\"}]}," +
                "{\"id\": \"A03\", \"actions\": [{\"behavior\": \"advance\", \"text\": \"Auto Advance\"}]}]}";
            var plan = SheetPlan.Read(json, "task", out string refusal);
            Assert.That(refusal, Is.Null);
            var scene = new ManifestScene(_root, new AnimationClip[0]);
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var sequence = GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>();
            Assert.That(sequence.Find("A01").autoAdvance && sequence.Find("A01").dwell > 0f, Is.True, "no row says so: it goes on once heard");
            Assert.That(sequence.Find("A02").autoAdvance, Is.False, "it waits on its choice");
            Assert.That(sequence.Find("A03").autoAdvance, Is.True, "the sheet's own advance says the same");

            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                manifest.edits.Add(new Manifest.Edit { step = "A03", target = "", behavior = "advance" });
                foreach (var step in sequence.steps) step.autoAdvance = false;  // back to waiting: a build is seen to set what it sets
                var quiet = ScriptableObject.CreateInstance<TaskSettings>();
                quiet.rig = TaskRig.None;
                quiet.debugTools = false;
                ManifestsActions.Build(plan, new ManifestScene(_root, new AnimationClip[0]) { Manifest = manifest }.Use(quiet), new OpContext());
                Assert.That((sequence.Find("A01").autoAdvance, sequence.Find("A03").autoAdvance), Is.EqualTo((true, false)), "its advance turned off: it waits");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void A_steps_voice_line_delivered_in_its_take_plays_once_from_the_step()
        {
            // 2026-11-25: the Shot Manifest keys each step's narration onto its first take, so a delivery carries it as
            // that take's audio cue; the task plays the line as the step starts, and the take must not play it again.
            var voice = AudioClip.Create("grab_the_keys", 24000 * 2, 1, 24000, false);
            var player = M3trik.Unitytk.AnimationAudio.AnimationAudioActions.AddPlayer(_root) ? _root.GetComponent<M3trik.Unitytk.AnimationAudio.AnimationAudioPlayer>() : null;
            var scene = new ManifestScene(_root, new AnimationClip[0]) { FindAsset = (path, type) => path == "narration/grab_the_keys.wav" ? voice : null };
            var plan = SheetPlan.Read("{\"version\": 2, \"steps\": [{\"id\": \"A01\", \"fields\": {\"voice\": \"Grab the keys.\", " +
                                      "\"sound\": \"narration/grab_the_keys.wav\"}, \"actions\": [{\"behavior\": \"advance\", \"text\": \"Auto Advance\"}]}]}",
                                      "task", out string refusal);
            Assert.That(refusal, Is.Null);
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            settings.rig = TaskRig.None;
            try
            {
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, scene.Use(settings), dry);
                ManifestsActions.Build(plan, scene, new OpContext());
                Assert.That(player.leave, Is.EqualTo(new[] { "grab_the_keys" }), "the delivery's copy is left to the step");
                Assert.That(dry.Changes.Any(c => (c.action ?? "").Contains("to what plays them elsewhere")), "planned, as it is made");
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, scene, again);
                Assert.That(again.Changes, Is.Empty, "a second build changes nothing");
            }
            finally
            {
                Object.DestroyImmediate(settings);
                var made = GameObject.Find(TaskKit.VoiceName(plan));
                if (made != null) Object.DestroyImmediate(made);
                Object.DestroyImmediate(voice);
            }
        }

        [Test]
        public void A_build_of_some_steps_leaves_every_built_steps_voice_line_to_its_step()
        {
            // A build of one step (or one past a locked step) set the take's list of sounds to leave to that step's alone: the
            // other step kept its cue, and its take played the line again over it.
            const string Advance = "{\"behavior\": \"advance\", \"text\": \"Auto Advance\"}";
            var keys = AudioClip.Create("grab_the_keys", 24000, 1, 24000, false);
            var door = AudioClip.Create("open_the_door", 24000, 1, 24000, false);
            var player = M3trik.Unitytk.AnimationAudio.AnimationAudioActions.AddPlayer(_root) ? _root.GetComponent<M3trik.Unitytk.AnimationAudio.AnimationAudioPlayer>() : null;
            var scene = new ManifestScene(_root, new AnimationClip[0])
            {
                FindAsset = (path, type) => path == "narration/a01.wav" ? keys : path == "narration/a02.wav" ? door : null,
            };
            var plan = SheetPlan.Read("{\"version\": 2, \"steps\": [" +
                                      "{\"id\": \"A01\", \"fields\": {\"voice\": \"Grab the keys.\", \"sound\": \"narration/a01.wav\"}, \"actions\": [" + Advance + "]}," +
                                      "{\"id\": \"A02\", \"fields\": {\"voice\": \"Open the door.\", \"sound\": \"narration/a02.wav\"}, \"actions\": [" + Advance + "]}]}",
                                      "task", out string refusal);
            Assert.That(refusal, Is.Null);
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            settings.rig = TaskRig.None;
            try
            {
                ManifestsActions.Build(plan, scene.Use(settings), new OpContext());
                Assert.That(player.leave, Is.EqualTo(new[] { "grab_the_keys", "open_the_door" }), "premise: both left to their steps");
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, scene, dry, only: new[] { "A02" });
                Assert.That(dry.Changes.Where(c => (c.action ?? "").Contains("to what plays them elsewhere")), Is.Empty, "the list stands as it is");
                ManifestsActions.Build(plan, scene, new OpContext(), only: new[] { "A02" });
                Assert.That(player.leave, Is.EqualTo(new[] { "grab_the_keys", "open_the_door" }), "A01's cue still plays its line: the take leaves it");
            }
            finally
            {
                Object.DestroyImmediate(settings);
                var made = GameObject.Find(TaskKit.VoiceName(plan));
                if (made != null) Object.DestroyImmediate(made);
                Object.DestroyImmediate(keys);
                Object.DestroyImmediate(door);
            }
        }

        [Test]
        public void A_steps_sound_plays_while_it_runs_and_its_step_waits_for_it()
        {
            var voice = AudioClip.Create("open_the_door", 24000 * 6, 1, 24000, false);  // six seconds
            var scene = new ManifestScene(_root, new AnimationClip[0]) { FindAsset = (path, type) => path == "narration/a01.wav" ? voice : null };
            string json = "{\"version\": 2, \"steps\": [" +
                "{\"id\": \"A01\", \"fields\": {\"voice\": \"Open the door.\", \"sound\": \"narration/a01.wav\"}, \"actions\": [" +
                "{\"behavior\": \"advance\", \"text\": \"Auto Advance\"}]}," +
                "{\"id\": \"A02\", \"fields\": {\"voice\": \"Close it.\", \"sound\": \"narration/missing.wav\"}, \"actions\": [" +
                "{\"behavior\": \"advance\", \"text\": \"Auto Advance\"}]}]}";
            var plan = SheetPlan.Read(json, "task", out string refusal);
            Assert.That(refusal, Is.Null);
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, again);
            Assert.That(again.Changes, Is.Empty);
            try
            {
                var cue = GameObject.Find(TaskKit.VoiceName(plan)).transform.Find("A01").GetComponent<M3trik.Unitytk.AnimationAudio.SoundCue>();
                var sequence = GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>();
                Assert.That(sequence.Find("A01").activates, Does.Contain(cue), "it plays while its step runs");
                Assert.That(sequence.Find("A01").dwell, Is.EqualTo(6f + ScriptableObject.CreateInstance<TaskSettings>().voiceTail).Within(1e-3f), "the step waits for its voice");
                Assert.That(sequence.Find("A02").dwell, Is.EqualTo(ScriptableObject.CreateInstance<TaskSettings>().minimumDwell).Within(1e-3f), "no sound held: its words read");
                Assert.That(live.Issues.Select(i => i.code), Does.Contain(TaskKit.SoundCode), "a sound the scene does not hold is raised");
            }
            finally
            {
                var made = GameObject.Find(TaskKit.VoiceName(plan));
                if (made != null) Object.DestroyImmediate(made);
            }
        }

        // The real task's last viewpoint, left to a build, stood the user 2 m from the plugs they were to pull: far
        // enough to see the whole door those are on. Its pictures looked at whatever the placard's card, moved beside
        // the user, carried their cameras to.
        [Test]
        public void A_spot_stands_the_user_within_reach_of_what_they_act_on_and_a_row_takes_its_picture_from_the_camera_it_names()
        {
            Transform cabinet = _root.transform.Find("CABINET");
            GameObject Box(string name, Transform under, Vector3 at, Vector3 size)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = name;
                box.transform.SetParent(under);
                box.transform.SetPositionAndRotation(at, Quaternion.identity);
                box.transform.localScale = size;
                return box;
            }
            Box("FLOOR", cabinet, new Vector3(0, -0.05f, 0), new Vector3(20, 0.1f, 20));
            Transform door = cabinet.Find("DOOR_GRP");
            Box("DOOR_GEO", door, new Vector3(0, 1.1f, 0), new Vector3(1f, 2.2f, 0.06f));           // a door 2.2 m tall
            GameObject bolt = Box("BOLT", door, new Vector3(0.3f, 1.4f, 0.05f), Vector3.one * 0.03f);  // a bolt on its +Z face
            var lens = new GameObject("CAM_BOLT");
            lens.transform.SetParent(cabinet);
            lens.transform.SetPositionAndRotation(new Vector3(0.3f, 1.5f, 0.6f), Quaternion.LookRotation(Vector3.back));
            lens.AddComponent<Camera>().fieldOfView = 25f;
            var scene = new ManifestScene(_root, new[] { new AnimationClip { name = "A01" } });
            const string Teleport = "{\"behavior\": \"teleport\", \"text\": \"User is teleported facing the door\", \"fields\": {\"facing\": \"CABINET|DOOR_GRP\"}}";
            const string Picture = "{\"behavior\": \"pip\", \"text\": \"A picture shows the bolt\", \"targets\": [\"CABINET|DOOR_GRP|BOLT\"], " +
                                   "\"fields\": {\"from\": \"CABINET|CAM_BOLT\"}}";
            const string Act = "{\"behavior\": \"select\", \"text\": \"Selecting the bolt goes on\", \"targets\": [\"CABINET|DOOR_GRP|BOLT\"]}";
            try
            {
                var looks = Plan(Teleport);
                ManifestsActions.Build(looks, scene, new OpContext());
                Transform view = GameObject.Find(TaskKit.ViewpointsName(looks)).transform.Find(TaskKit.ViewpointName("A01"));
                float seeing = Vector3.Distance(new Vector3(view.position.x, 0, view.position.z), new Vector3(0.3f, 0, 0.05f));
                Assert.That(seeing, Is.GreaterThan(1.4f), "with nothing to act on there, they stand back to see the door");

                var acts = Plan(Teleport + "," + Act + "," + Picture);
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(acts, scene, dry);
                var live = new OpContext();
                ManifestsActions.Build(acts, scene, live);
                Assert.That(live.Issues, Is.Empty);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                float reach = Vector3.Distance(new Vector3(view.position.x, 0, view.position.z), new Vector3(0.3f, 0, 0.05f));
                Assert.That(reach, Is.LessThan(1.2f), "acting on the bolt, they stand by it");
                // Which side of the door they stand on is the row's to say (side) or the viewpoint's to be moved to: the
                // search promises only that what they act on is itself seen from there, so never from behind its door.
                Assert.That(Placement.Placer.Seen(view.position + Vector3.up * Placement.Placer.DefaultEyeHeight,
                                                  bolt.GetComponent<Renderer>().bounds, bolt.transform), Is.EqualTo(1f),
                            "where the bolt itself is seen, never behind the door it is on");
                Assert.That(Vector3.Dot(view.forward, Vector3.ProjectOnPlane(bolt.transform.position - view.position, Vector3.up).normalized), Is.GreaterThan(0.9f), "facing it");
                Assert.That(view.GetComponent<Viewpoints.TravelTo>(), Is.Not.Null, "and are stood on the spot as it stands");

                var shot = GameObject.Find(TaskKit.PipName(acts)).transform.Find("A01").GetComponent<Panels.PipShot>();
                Assert.That(shot.from, Is.SameAs(lens.transform), "the picture is taken from the camera its row names");
                Assert.That(lens.GetComponent<Camera>().enabled, Is.False, "which never draws to the screen itself");
                Assert.That(bolt.transform.Find(Panels.PanelsActions.SpotName("A01")) == null, "no spot is searched for beside it");
                Assert.That(lens.GetComponent<Placement.PlacedSpot>().Adjusted, Is.False, "marked as its maker placed it");
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(acts, scene, again);
                Assert.That(again.Changes, Is.Empty, "a second build changes nothing");
            }
            finally
            {
                foreach (string made in new[] { TaskKit.ViewpointsName(Plan(Teleport)), TaskKit.PipName(Plan(Teleport)) })
                {
                    var node = GameObject.Find(made);
                    if (node != null) Object.DestroyImmediate(node);
                }
            }
        }

        [Test]
        public void Spots_the_sheet_names_none_of_are_placed_where_the_part_is_seen_planned_first_and_kept_when_adjusted()
        {
            // A room: a floor, the door (a box on show), and a wall hiding it from its -Z side.
            Transform cabinet = _root.transform.Find("CABINET");
            GameObject Box(string name, Transform under, Vector3 at, Vector3 size)
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = name;
                box.transform.SetParent(under);
                box.transform.SetPositionAndRotation(at, Quaternion.identity);
                box.transform.localScale = size;
                return box;
            }
            Box("FLOOR", cabinet, new Vector3(0, -0.05f, 0), new Vector3(20, 0.1f, 20));
            Box("WALL", cabinet, new Vector3(0, 1f, -0.6f), new Vector3(6, 3, 0.1f));
            Box("DOOR_GEO", cabinet.Find("DOOR_GRP"), new Vector3(0, 1f, 0), Vector3.one * 0.4f);
            var scene = new ManifestScene(_root, new AnimationClip[0]);
            var plan = Plan("{\"behavior\": \"teleport\", \"text\": \"User is teleported facing the door\", \"fields\": {\"facing\": \"CABINET|DOOR_GRP\"}}," +
                            "{\"behavior\": \"indicate\", \"text\": \"Arrow points at the door\", \"targets\": [\"CABINET|DOOR_GRP\"]}," +
                            "{\"behavior\": \"pip\", \"text\": \"PiP screen shows the door\", \"targets\": [\"CABINET|DOOR_GRP\"]}");
            try
            {
                Assert.That(ManifestsActions.Assess(plan, scene).rows.Select(r => r.state), Is.All.EqualTo(ManifestAssessment.State.Ready));
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, scene, dry);
                Assert.That(GameObject.Find(TaskKit.ViewpointsName(plan)) == null, "a dry run builds nothing");
                var live = new OpContext();
                ManifestsActions.Build(plan, scene, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));

                Transform view = GameObject.Find(TaskKit.ViewpointsName(plan)).transform.Find(TaskKit.ViewpointName("A01"));
                Assert.That(view.position.z, Is.GreaterThan(-0.5f), "where the door is seen, not behind the wall");
                Assert.That(view.position.y, Is.EqualTo(0f).Within(1e-3f), "on the floor");
                Assert.That(view.GetComponent<Placement.PlacedSpot>().key, Is.EqualTo("teleport/A01"));
                Assert.That(view.GetComponent<Viewpoints.TravelTo>(), Is.Not.Null, "the step brings the user there, standing on it as it stands");
                Assert.That(view.Find(Viewpoints.ViewpointsActions.FacingName(view.gameObject, cabinet.Find("DOOR_GRP"))) == null,
                            "no second place turned toward the door: the viewpoint itself faces it, and a person turns that");
                Assert.That(Vector3.Dot(view.forward, Vector3.ProjectOnPlane(cabinet.Find("DOOR_GRP/DOOR_GEO").GetComponent<Renderer>().bounds.center - view.position, Vector3.up).normalized),
                            Is.GreaterThan(0.9f), "placed facing what its row faces");
                Assert.That(view.GetComponentInChildren<Indicators.Marker>(true), Is.Not.Null, "its ring, for a marker row");

                Transform arrow = cabinet.Find("DOOR_GRP/Indicator A01");
                Assert.That(arrow.GetComponent<Indicators.Indicator>().follow, Is.False, "placed, riding the door");
                Assert.That(arrow.position.y, Is.GreaterThan(1.2f), "over the door: the usual arrow, seen from the spot");
                var shot = GameObject.Find(TaskKit.PipName(plan)).transform.Find("A01").GetComponent<Panels.PipShot>();
                Assert.That(shot.from, Is.Not.Null.And.EqualTo(cabinet.Find("DOOR_GRP/" + Panels.PanelsActions.SpotName("A01"))),
                            "its picture taken from a spot that sees the door, the door's own: it rides it");
                Assert.That(Placement.Placer.Seen(shot.from.position, cabinet.Find("DOOR_GRP/DOOR_GEO").GetComponent<Renderer>().bounds,
                                                  cabinet.Find("DOOR_GRP")), Is.GreaterThan(0.5f));
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, scene, again);
                Assert.That(again.Changes, Is.Empty);

                // A person moves the viewpoint; the capture keeps it, and the next build puts it there.
                view.position += Vector3.right * 0.5f;
                Vector3 moved = view.position;
                var kept = new Placement.Placements.Tuning();
                Placement.Placements.Capture(kept, null);
                view.position -= Vector3.right * 0.5f;
                var tuned = new ManifestScene(_root, new AnimationClip[0]);
                tuned.State<TaskState>().Tuning = kept;
                ManifestsActions.Build(plan, tuned, new OpContext());
                Assert.That(Vector3.Distance(view.position, moved), Is.LessThan(1e-4f));
            }
            finally
            {
                foreach (string made in new[] { TaskKit.ViewpointsName(plan), TaskKit.PipName(plan) })
                {
                    var node = GameObject.Find(made);
                    if (node != null) Object.DestroyImmediate(node);
                }
            }
        }

        [Test]
        public void A_pictures_own_camera_frames_with_its_rows_field_of_view_which_a_person_sets_on_it()
        {
            // A Camera is no MonoBehaviour: a field of view set on a picture's own camera was listed nowhere, and a scene
            // built anew gave it the view's again (2026-11-26). It is the row's setting, bound to the camera the kit made.
            Transform cabinet = _root.transform.Find("CABINET");
            foreach (var (name, parent, at, size) in new[] { ("FLOOR", cabinet, new Vector3(0, -0.05f, 0), new Vector3(20, 0.1f, 20)),
                                                             ("DOOR_GEO", cabinet.Find("DOOR_GRP"), new Vector3(0, 1f, 0), Vector3.one * 0.4f) })
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = name;
                box.transform.SetParent(parent);
                box.transform.SetPositionAndRotation(at, Quaternion.identity);
                box.transform.localScale = size;
            }
            SheetPlan.Shape.Payload Picture(string settings) => Plan(
                "{\"behavior\": \"pip\", \"text\": \"PiP screen shows the door\", \"targets\": [\"CABINET|DOOR_GRP\"]" + settings + "}");
            var plan = Picture("");
            try
            {
                var scene = new ManifestScene(_root, new AnimationClip[0]);
                ManifestsActions.Build(plan, scene, new OpContext());
                Camera Lens() => GameObject.Find(TaskKit.PipName(plan)).transform.Find("A01").GetComponent<Panels.PipShot>().from.GetComponent<Camera>();
                Assert.That(Lens().fieldOfView, Is.EqualTo(Panels.PipView.DefaultFieldOfView).Within(1e-4f), "the view's, by default");
                var bound = ((IManifestBound)ManifestBehaviors.Find("pip")).Bound(plan.steps[0].actions[0], plan.steps[0], scene, "CABINET|DOOR_GRP").ToList();
                Assert.That(bound.Select(b => (b.setting, b.part, b.property)),
                            Is.EqualTo(new[] { (nameof(TaskSettings.pipFieldOfView), (Object)Lens(), "field of view") }), "bound to the camera the kit made");
                using (var held = new SerializedObject(Lens()))
                    Assert.That(held.FindProperty("field of view")?.floatValue, Is.EqualTo(Lens().fieldOfView), "the property an edit is kept by");

                var framed = Picture(", \"settings\": {\"pipFieldOfView\": 40}");
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(framed, new ManifestScene(_root, new AnimationClip[0]), dry);
                var live = new OpContext();
                ManifestsActions.Build(framed, new ManifestScene(_root, new AnimationClip[0]), live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(Lens().fieldOfView, Is.EqualTo(40f).Within(1e-4f), "its row's");

                // 0.5 degrees was placed for as it was written while the camera framed with 1: one angle for both, and said.
                var narrow = Picture(", \"settings\": {\"pipFieldOfView\": 0.5}");
                var said = new OpContext();
                ManifestsActions.Build(narrow, new ManifestScene(_root, new AnimationClip[0]), said);
                Assert.That(Lens().fieldOfView, Is.EqualTo(PipBehavior.LeastAngle).Within(1e-4f), "brought within what a camera takes");
                Assert.That(said.Issues.Any(i => i.code == ManifestsActions.NoteCode && i.message.Contains("is no camera's")), "and said on its row");
            }
            finally
            {
                foreach (string made in new[] { TaskKit.ViewpointsName(plan), TaskKit.PipName(plan) })
                {
                    var node = GameObject.Find(made);
                    if (node != null) Object.DestroyImmediate(node);
                }
            }
        }

        [Test]
        public void A_rows_own_helpers_are_named_as_every_tool_names_them_and_the_names_a_build_gave_before_go()
        {
            // The Shot Manifest names a row's own helper VIEW_/ARROW_/CAM_<step>_<target>; Unity named what it made for the
            // same row "VIEW_A01", "Indicator A01", "PiP A01". One standard: the plan's helpers (the reader's, by the same rule).
            Transform cabinet = _root.transform.Find("CABINET");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "FLOOR";
            floor.transform.SetParent(cabinet);
            floor.transform.SetPositionAndRotation(new Vector3(0, -0.05f, 0), Quaternion.identity);
            floor.transform.localScale = new Vector3(20, 0.1f, 20);
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "DOOR_GEO";
            door.transform.SetParent(cabinet.Find("DOOR_GRP"));
            door.transform.SetPositionAndRotation(new Vector3(0, 1f, 0), Quaternion.identity);
            door.transform.localScale = Vector3.one * 0.4f;
            string Rows(bool named) =>
                "{\"behavior\": \"teleport\", \"text\": \"User is teleported facing the door\", \"fields\": {\"facing\": \"CABINET|DOOR_GRP\"" +
                (named ? ", \"helpers\": [\"VIEW_A01_DOOR_GRP\"]" : "") + "}}," +
                "{\"behavior\": \"indicate\", \"text\": \"Arrow points at the door\", \"targets\": [\"CABINET|DOOR_GRP\"]" +
                (named ? ", \"fields\": {\"helpers\": [\"ARROW_A01_DOOR_GRP\"]}" : "") + "}," +
                "{\"behavior\": \"pip\", \"text\": \"PiP screen shows the door\", \"targets\": [\"CABINET|DOOR_GRP\"]" +
                (named ? ", \"fields\": {\"helpers\": [\"CAM_A01_DOOR_GRP\"]}" : "") + "}";
            var before = Plan(Rows(false));
            var plan = Plan(Rows(true));
            try
            {
                ManifestsActions.Build(before, new ManifestScene(_root, new AnimationClip[0]), new OpContext());  // a plan from before the standard
                Transform views = GameObject.Find(TaskKit.ViewpointsName(plan)).transform;
                Assert.That(views.Find(TaskKit.ViewpointName("A01")), Is.Not.Null, "premise: the old names");
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, new ManifestScene(_root, new AnimationClip[0]), dry);
                var live = new OpContext();
                ManifestsActions.Build(plan, new ManifestScene(_root, new AnimationClip[0]), live);
                Assert.That(live.Changes.Select(c => $"{c.kind} {c.target}: {c.action}").ToList(),
                            Is.EqualTo(dry.Changes.Select(c => $"{c.kind} {c.target}: {c.action}").ToList()), "the plan is the run");

                Transform doorGroup = cabinet.Find("DOOR_GRP");
                Assert.That(views.Find("VIEW_A01_DOOR_GRP"), Is.Not.Null, "the viewpoint, as the Shot Manifest names it");
                Assert.That(views.Find(TaskKit.ViewpointName("A01")), Is.Null, "the old one gone");
                Assert.That(doorGroup.Find("ARROW_A01_DOOR_GRP").GetComponent<Indicators.Indicator>(), Is.Not.Null, "the arrow");
                Assert.That(doorGroup.Find(IndicateBehavior.ArrowName("A01")), Is.Null, "the old one gone");
                Assert.That(doorGroup.Find("CAM_A01_DOOR_GRP").GetComponent<Camera>(), Is.Not.Null, "the picture's camera");
                var shot = GameObject.Find(TaskKit.PipName(plan)).transform.Find("A01").GetComponent<Panels.PipShot>();
                Assert.That(shot.from, Is.EqualTo(doorGroup.Find("CAM_A01_DOOR_GRP")), "its picture taken from it");
                var again = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, new ManifestScene(_root, new AnimationClip[0]), again);
                Assert.That(again.Changes, Is.Empty, "a second build changes nothing");
            }
            finally
            {
                foreach (string made in new[] { TaskKit.ViewpointsName(plan), TaskKit.PipName(plan) })
                {
                    var node = GameObject.Find(made);
                    if (node != null) Object.DestroyImmediate(node);
                }
            }
        }

        [Test]
        public void Moving_a_viewpoint_leaves_the_arrows_found_from_it_where_they_were_first_placed()
        {
            // A real task (2026-11-26): a viewpoint moved 0.1 m and turned, and the next build placed again the arrow found
            // from it. The search runs once; a person's Place Again runs it anew.
            Transform cabinet = _root.transform.Find("CABINET");
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "FLOOR";
            floor.transform.SetParent(cabinet);
            floor.transform.SetPositionAndRotation(new Vector3(0, -0.05f, 0), Quaternion.identity);
            floor.transform.localScale = new Vector3(20, 0.1f, 20);
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "DOOR_GEO";
            door.transform.SetParent(cabinet.Find("DOOR_GRP"));
            door.transform.SetPositionAndRotation(new Vector3(0, 1f, 0), Quaternion.identity);
            door.transform.localScale = Vector3.one * 0.4f;
            var plan = Plan("{\"behavior\": \"teleport\", \"text\": \"User is teleported facing the door\", \"fields\": {\"facing\": \"CABINET|DOOR_GRP\"}}," +
                            "{\"behavior\": \"indicate\", \"text\": \"Arrow points at the door\", \"targets\": [\"CABINET|DOOR_GRP\"]}");
            try
            {
                ManifestsActions.Build(plan, new ManifestScene(_root, new AnimationClip[0]), new OpContext());
                Transform view = GameObject.Find(TaskKit.ViewpointsName(plan)).transform.Find(TaskKit.ViewpointName("A01"));
                Transform arrow = cabinet.Find("DOOR_GRP/" + IndicateBehavior.ArrowName("A01"));
                Vector3 first = arrow.position;

                view.position += new Vector3(1.2f, 0f, 0.4f);  // a person moves the viewpoint; the build keeps it
                view.rotation = Quaternion.Euler(0f, 30f, 0f) * view.rotation;
                var kept = new Placement.Placements.Tuning();
                Placement.Placements.Capture(kept, null);
                ManifestScene Scene()
                {
                    var scene = new ManifestScene(_root, new AnimationClip[0]);
                    scene.State<TaskState>().Tuning = kept;
                    return scene;
                }
                var dry = new OpContext(dryRun: true);
                ManifestsActions.Build(plan, Scene(), dry);
                Assert.That(dry.Changes.Where(c => c.target.Contains(IndicateBehavior.ArrowName("A01"))).Select(c => $"{c.kind} {c.target}: {c.action}"),
                            Is.Empty, "the arrow's first placement holds");
                ManifestsActions.Build(plan, Scene(), new OpContext());
                Assert.That(Vector3.Distance(arrow.position, first), Is.LessThan(1e-4f));

                Placement.PlacedSpot was = arrow.GetComponent<Placement.PlacedSpot>();
                Placement.Placements.PlaceAgain(was, new OpContext());
                ManifestScene last = Scene();
                ManifestsActions.Build(plan, last, new OpContext());
                Placement.PlacedSpot placed = arrow.GetComponent<Placement.PlacedSpot>();
                Assert.That(placed != null && placed != was && !placed.Adjusted, "placed anew, where the search put it");
                Vector3? eyes = Spots.Eyes(last, last.Plan.steps[0]);
                Assert.That(eyes.HasValue && Vector3.Distance(eyes.Value, view.position) < 2.5f, "premise: the user's eyes over the moved viewpoint");
                Assert.That(Placement.Placer.Seen(eyes.Value, new Bounds(arrow.position, Vector3.one * 0.02f), arrow), Is.GreaterThan(0f),
                            "seen from where the user now stands, the moved viewpoint");
            }
            finally
            {
                foreach (string made in new[] { TaskKit.ViewpointsName(plan) })
                {
                    var node = GameObject.Find(made);
                    if (node != null) Object.DestroyImmediate(node);
                }
            }
        }

        [Test]
        public void Two_viewpoints_a_step_places_are_two_spots_each_keeping_its_own_adjustment()
        {
            // A step that brings the user to one spot as it starts and another as it ends kept both under one key (and, from a
            // plan with no helper names, one viewpoint): a move of one was dropped as the other was kept, or put on both.
            Transform cabinet = _root.transform.Find("CABINET");
            foreach (var (name, parent, at, size) in new[] { ("FLOOR", cabinet, new Vector3(0, -0.05f, 0), new Vector3(20, 0.1f, 20)),
                                                             ("DOOR_GEO", cabinet.Find("DOOR_GRP"), new Vector3(0, 1f, 0), Vector3.one * 0.4f),
                                                             ("LID_GEO", cabinet.Find("LID"), new Vector3(3f, 1f, 0), Vector3.one * 0.3f) })
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = name;
                box.transform.SetParent(parent);
                box.transform.SetPositionAndRotation(at, Quaternion.identity);
                box.transform.localScale = size;
            }
            var plan = Plan("{\"behavior\": \"teleport\", \"text\": \"User is teleported facing the door\", \"fields\": {\"facing\": \"CABINET|DOOR_GRP\"}}," +
                            "{\"behavior\": \"pause\", \"text\": \"Selecting 'Next' triggers step advancement\", \"fields\": {\"label\": \"Next\"}}," +
                            "{\"behavior\": \"teleport\", \"text\": \"User is teleported facing the lid\", \"fields\": {\"facing\": \"CABINET|LID\"}}");
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            settings.rig = TaskRig.None;
            try
            {
                ManifestsActions.Build(plan, new ManifestScene(_root, new AnimationClip[0]).Use(settings), new OpContext());
                Transform views = GameObject.Find(TaskKit.ViewpointsName(plan)).transform;
                Transform first = views.Find(TaskKit.ViewpointName("A01")), second = views.Find(TaskKit.ViewpointName("A01") + "_2");
                Assert.That(first != null && second != null, "a viewpoint each");
                Assert.That((first.GetComponent<Placement.PlacedSpot>().key, second.GetComponent<Placement.PlacedSpot>().key),
                            Is.EqualTo(("teleport/A01", "teleport/A01/2")), "each under its own key");

                first.position += Vector3.left * 0.5f;  // a person moves the first, and leaves the second
                Vector3 moved = first.position, there = second.position;
                var kept = new Placement.Placements.Tuning();
                Placement.Placements.Capture(kept, null);
                Assert.That(kept.spots.Select(s => s.key), Is.EqualTo(new[] { "teleport/A01" }), "the move kept, the other spot's no adjustment");
                var tuned = new ManifestScene(_root, new AnimationClip[0]).Use(settings);
                tuned.State<TaskState>().Tuning = kept;
                ManifestsActions.Build(plan, tuned, new OpContext());
                Assert.That(Vector3.Distance(first.position, moved), Is.LessThan(1e-4f), "the first where it was moved");
                Assert.That(Vector3.Distance(second.position, there), Is.LessThan(1e-4f), "the second where it was placed");
            }
            finally
            {
                Object.DestroyImmediate(settings);
                var views = GameObject.Find(TaskKit.ViewpointsName(plan));
                if (views != null) Object.DestroyImmediate(views);
            }
        }

        [Test]
        public void An_arrow_with_no_free_spot_is_said_until_a_person_places_it()
        {
            // Measured 2026-11-25 on a production task: an arrow at a nut boxed in by its door said "no free spot" on every
            // build, after a person had moved it and the build had kept it too, so following its fix never cleared it.
            Transform cabinet = _root.transform.Find("CABINET");
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "DOOR_GEO";
            door.transform.SetParent(cabinet.Find("DOOR_GRP"));
            door.transform.SetPositionAndRotation(new Vector3(0, 1f, 0), Quaternion.identity);
            door.transform.localScale = Vector3.one * 0.2f;
            var crate = GameObject.CreatePrimitive(PrimitiveType.Cube);  // all round it: no way out is free
            crate.name = "CRATE";
            crate.transform.SetParent(cabinet);
            crate.transform.SetPositionAndRotation(new Vector3(0, 1f, 0), Quaternion.identity);
            crate.transform.localScale = Vector3.one * 3f;
            var plan = Plan("{\"behavior\": \"indicate\", \"text\": \"Arrow points at the door\", \"targets\": [\"CABINET|DOOR_GRP\"]}");
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            settings.rig = TaskRig.None;
            try
            {
                var first = new OpContext();
                ManifestsActions.Build(plan, new ManifestScene(_root, new AnimationClip[0]).Use(settings), first);
                var said = first.Issues.Single(i => i.code == TaskKit.SpotCode);
                Assert.That(said.message, Does.Contain("no spot beside 'DOOR_GRP' is both free and seen").And.Contain("its manifest keeps it"),
                            "says what it did, and that a move is kept");

                // A person moves it; the capture keeps it, and the next build puts it there and has nothing to say.
                Transform arrow = cabinet.Find("DOOR_GRP/Indicator A01");
                arrow.position += Vector3.forward * 2f;
                var kept = new Placement.Placements.Tuning();
                Placement.Placements.Capture(kept, null);
                var tuned = new ManifestScene(_root, new AnimationClip[0]).Use(settings);
                tuned.State<TaskState>().Tuning = kept;
                var again = new OpContext();
                ManifestsActions.Build(plan, tuned, again);
                Assert.That(again.Issues.Where(i => i.code == TaskKit.SpotCode), Is.Empty, "a person placed it");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void An_arrow_a_person_moved_stays_where_they_put_it_when_the_delivery_is_re_exported_under_a_new_name()
        {
            // A task's deliveries are dated: swapped over to the next export, the placed instance takes its name, and an
            // arrow's adjustment was kept under a key holding the old name, so the next build put the arrow back.
            var plan = Plan("{\"behavior\": \"indicate\", \"text\": \"Arrow points at the door\", \"targets\": [\"CABINET|DOOR_GRP\"]}");
            var settings = ScriptableObject.CreateInstance<TaskSettings>();
            settings.rig = TaskRig.None;
            try
            {
                var kept = new Placement.Placements.Tuning();
                ManifestScene Scene()
                {
                    var scene = new ManifestScene(_root, new AnimationClip[0]).Use(settings);
                    scene.State<TaskState>().Tuning = kept;
                    return scene;
                }
                ManifestsActions.Build(plan, Scene(), new OpContext());
                Transform arrow = _root.transform.Find("CABINET/DOOR_GRP/Indicator A01");
                arrow.position += Vector3.up * 0.5f;  // a person's move
                Vector3 put = arrow.position;
                Placement.Placements.Capture(kept, null);

                _root.name = "cabinet_2027";  // the next export, swapped in
                ManifestsActions.Build(plan, Scene(), new OpContext());
                Assert.That(Vector3.Distance(arrow.position, put), Is.LessThan(1e-4f), "where the person put it");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void A_pip_row_shows_its_part_until_a_hide_row_and_the_deliverys_anchors_are_hidden()
        {
            var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);  // an authoring anchor: a screen placed for a panel
            screen.name = "UI_PIP_01";
            screen.transform.SetParent(_root.transform.Find("CABINET"));
            var anchored = ScriptableObject.CreateInstance<TaskSettings>();
            anchored.anchorPattern = "^UI_";  // the task names its anchors (none by default)
            anchored.pipAnchorPattern = "^UI_PIP";  // and its picture's screen
            var scene = new ManifestScene(_root, new AnimationClip[0]).Use(anchored);
            string json = "{\"version\": 2, \"steps\": [" +
                "{\"id\": \"A01\", \"actions\": [" +
                "{\"behavior\": \"pip\", \"text\": \"PiP screen shows the door behind the lid.\", \"targets\": [\"CABINET|DOOR_GRP\"]}]}," +
                "{\"id\": \"A02\", \"actions\": [" +
                "{\"behavior\": \"pip\", \"text\": \"PiP screen with the lid appears\", \"targets\": [\"CABINET|LID\"]}]}," +
                "{\"id\": \"A03\", \"actions\": [" +
                "{\"behavior\": \"pip\", \"text\": \"PiP screen fades away\", \"settings\": {\"pipShow\": \"Hide\"}}]}]}";
            var plan = SheetPlan.Read(json, "task", out string refusal);
            Assert.That(refusal, Is.Null);
            Assert.That(ManifestsActions.Assess(plan, scene).rows.Select(r => r.state), Is.All.EqualTo(ManifestAssessment.State.Ready));
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, again);
            Assert.That(again.Changes, Is.Empty);
            try
            {
                var pip = GameObject.Find(TaskKit.PipName(plan)).GetComponent<M3trik.Unitytk.Panels.PipView>();
                Assert.That(pip.style, Is.EqualTo(ScriptableObject.CreateInstance<TaskSettings>().pipStyle));
                var first = pip.transform.Find("A01").GetComponent<M3trik.Unitytk.Panels.PipShot>();
                var second = pip.transform.Find("A02").GetComponent<M3trik.Unitytk.Panels.PipShot>();
                Assert.That((first.target, first.caption), Is.EqualTo((_root.transform.Find("CABINET/DOOR_GRP"), "The door behind the lid")));
                Assert.That(second.caption, Is.EqualTo("The lid"));
                var sequence = GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>();
                Assert.That(sequence.Find("A01").turnsOn, Does.Contain(first));
                Assert.That(sequence.Find("A02").turnsOn, Does.Contain(second));
                Assert.That(sequence.Find("A02").turnsOff, Does.Contain(first), "the next picture replaces it");
                Assert.That(sequence.Find("A03").turnsOff, Does.Contain(second), "a hide row takes it away");
                Assert.That(screen.GetComponent<MeshRenderer>().enabled, Is.False, "an authoring anchor is a place, not a thing to see");
                Assert.That(TaskKit.PipAnchor(scene), Is.SameAs(screen.transform));
            }
            finally
            {
                var made = GameObject.Find(TaskKit.PipName(plan));
                if (made != null) Object.DestroyImmediate(made);
                Object.DestroyImmediate(anchored);
            }
        }

        [Test]
        public void Rows_after_the_advance_row_teleport_and_hide_the_marker_as_the_step_ends()
        {
            var view = new GameObject("VIEW_FRONT").transform;
            view.SetParent(_root.transform.Find("CABINET"));
            var scene = new ManifestScene(_root, new AnimationClip[0]);
            var plan = Plan("{\"behavior\": \"marker\", \"text\": \"Destination circle fades in\"}," +
                            "{\"behavior\": \"pause\", \"text\": \"Selecting 'Next' triggers step advancement\", \"fields\": {\"label\": \"Next\"}}," +
                            "{\"behavior\": \"marker\", \"text\": \"Destination circle fades away\", \"settings\": {\"markerShow\": \"Hide\"}}," +
                            "{\"behavior\": \"teleport\", \"text\": \"The user is teleported to VIEW_FRONT\", \"targets\": [\"CABINET|VIEW_FRONT\"]}");
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, dry);
            var live = new OpContext();
            ManifestsActions.Build(plan, scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, again);
            Assert.That(again.Changes, Is.Empty);

            var step = GameObject.Find("Task").GetComponent<M3trik.Unitytk.Steps.Sequence>().Find("A01");
            var travel = view.GetComponent<M3trik.Unitytk.Viewpoints.TravelTo>();
            var marker = view.Find(M3trik.Unitytk.Indicators.IndicatorsActions.MarkerName).GetComponent<M3trik.Unitytk.Indicators.Marker>();
            Assert.That(step.ends, Does.Contain(travel), "the user is brought there once they choose Next");
            Assert.That(step.activates, Has.No.Member(travel));
            Assert.That(step.activates, Does.Contain(marker), "the destination shows while the step runs");
            Assert.That(step.turnsOn.Concat(step.turnsOff), Has.No.Member(marker), "and is gone after it");
        }

        [Test]
        public void A_teleport_facing_an_object_brings_the_user_to_a_viewpoint_turned_toward_it_planned_first()
        {
            var view = new GameObject("VIEW_FRONT").transform;
            view.SetParent(_root.transform.Find("CABINET"));
            var scene = new ManifestScene(_root, new AnimationClip[0]);
            var plan = Plan("{\"behavior\": \"teleport\", \"text\": \"User is teleported to VIEW_FRONT, facing the door\", " +
                            "\"targets\": [\"CABINET|VIEW_FRONT\"], \"fields\": {\"facing\": \"CABINET|DOOR_GRP\"}}");
            var dry = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, dry);
            Assert.That(view.childCount, Is.Zero, "a dry run builds nothing");
            var live = new OpContext();
            ManifestsActions.Build(plan, scene, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Transform facing = view.Find(M3trik.Unitytk.Viewpoints.ViewpointsActions.FacingName(view.gameObject, _root.transform.Find("CABINET/DOOR_GRP")));
            Assert.That(facing, Is.Not.Null);
            Assert.That(facing.GetComponent<M3trik.Unitytk.Viewpoints.TravelTo>(), Is.Not.Null, "the step brings the user there");
            Assert.That(view.GetComponent<M3trik.Unitytk.Viewpoints.TravelTo>() == null, "not to the viewpoint's own forward");
            var again = new OpContext(dryRun: true);
            ManifestsActions.Build(plan, scene, again);
            Assert.That(again.Changes, Is.Empty);
        }
    }
}
