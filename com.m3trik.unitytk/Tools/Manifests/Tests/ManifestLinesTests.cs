using System.Linq;
using M3trik.Unitytk.Manifests;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>
    /// <see cref="ManifestLines"/>: a plan as the window's lines (a step, and under it each object its
    /// rows act on with its behaviors), with no status before an assessment; a behavior's name as a
    /// person reads it and its state as a glyph in its colour; what this project turned off or added
    /// beside what the sheet says; and the report of what waits on the sheet's owner. Its behaviors are this
    /// assembly's own (<see cref="ToolTestKit"/>), or names no builder has: no kit's but the test's.
    /// </summary>
    public class ManifestLinesTests
    {
        const string Json =
            "{\"version\": 2, \"source\": {\"sheet\": \"test.csv\"}, \"issues\": [\"no step plays the take Z99\"], \"steps\": [" +
            "{\"id\": \"S01\", \"actions\": [" +
            "{\"behavior\": \"test_fade\", \"text\": \"The lid fades in\", \"targets\": [\"BOX|LID\", \"BOX|HINGE\"]}," +
            "{\"behavior\": \"unread\", \"text\": \"Something else\", \"issues\": [\"row 2 (Step Contents): no phrase reads it: write an Action\"]}]}]}";

        [Test]
        public void Two_rows_of_one_behavior_say_what_tells_them_apart_and_every_field_the_sheet_gives()
        {
            const string choices =
                "{\"version\": 2, \"steps\": [{\"id\": \"B03\", \"actions\": [" +
                "{\"behavior\": \"test_choice\", \"text\": \"Selecting YES goes to B03.5\", \"to\": \"B03_5\", \"fields\": {\"label\": \"YES\"}}," +
                "{\"behavior\": \"test_choice\", \"text\": \"Selecting NO goes to B04\", \"to\": \"B04\", \"fields\": {\"label\": \"NO\"}}," +
                "{\"behavior\": \"no_builder\", \"text\": \"A row no builder has\", \"fields\": {\"side_of\": \"LID\"}}]}]}";
            ManifestLines.Line step = ManifestLines.Of(SheetPlan.Read(choices, "test", out _), null).Single();
            var chips = ManifestWindow.Chips(step);
            Assert.That(chips.Select(c => c.label), Is.EqualTo(new[] { "Test Choice 'YES' → B03_5", "Test Choice 'NO' → B04", "No Builder" }),
                        "what tells a row apart is its behavior's own declaration (ChipFields)");
            Assert.That(chips[0].tooltip, Does.Contain("Choice: YES").And.Contain("Goes to: B03_5"), "every field the sheet's row gives it, as its behavior labels it");
            Assert.That(chips[2].tooltip, Does.Contain("Side of: LID"), "a field no behavior labels: its name in words");
            Assert.That(step.behaviors, Is.EqualTo("Test Choice 'YES' → B03_5, Test Choice 'NO' → B04, No Builder"), "what a search and an export read");
        }

        [Test]
        public void One_behavior_read_several_ways_says_which_beside_its_name()
        {
            // The user: consolidate, and keep chips readable with a suffix when a behavior is set a certain way.
            const string rows =
                "{\"version\": 2, \"steps\": [{\"id\": \"C01\", \"actions\": [" +
                "{\"behavior\": \"test_marker\", \"text\": \"Destination circle fades in\"}," +
                "{\"behavior\": \"test_marker\", \"text\": \"Destination circle fades away\", \"settings\": {\"show\": \"Hide\"}}," +
                "{\"behavior\": \"test_press\", \"text\": \"Pressing and holding the button plays its take\", \"targets\": [\"BOX|BUTTON\"], \"clip\": \"C01\", " +
                "\"fields\": {\"tool\": \"BOX|PLIERS\"}, \"settings\": {\"press\": \"HoldToPlay\"}}]}]}";
            ManifestLines.Line step = ManifestLines.Of(SheetPlan.Read(rows, "test", out _), null).Single();
            Assert.That(ManifestWindow.Chips(step).Select(c => c.label), Is.EqualTo(new[] { "Test Marker", "Test Marker · hide" }), "the usual way says nothing");
            var press = ManifestWindow.Chips(step.rows.Single()).Single();
            Assert.That(press.label, Is.EqualTo("Test Press · hold to play"));
            Assert.That(press.tooltip, Does.Contain("Take: C01").And.Contain("In hand: PLIERS"), "a field naming a node, by the node's own name");

            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                Assert.That(ManifestsActions.SetTune(manifest, "C01", "BOX|BUTTON", "test_press", "press", "TimedPress"), Is.True);
                ManifestLines.Line own = ManifestLines.Of(SheetPlan.Read(rows, "test", out _), null, manifest).Single().rows.Single();
                var chip = ManifestWindow.Chips(own).Single();
                Assert.That((chip.label, chip.level), Is.EqualTo(("Test Press · timed press", "edit")), "the object's own way, marked as an edit");
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        [Test]
        public void A_plan_is_a_line_a_step_and_a_line_each_object_with_no_status_until_it_is_assessed()
        {
            var plan = SheetPlan.Read(Json, "test", out _);
            ManifestLines.Line step = ManifestLines.Of(plan, null).Single();
            Assert.That((step.step, step.description, step.acts, step.status, step.label), Is.EqualTo(("S01", "S01", "2 objects", "", "")));
            Assert.That(step.behaviors, Is.EqualTo("Unread"), "a step's own line holds the behaviors that name no object");
            Assert.That(step.rows.Select(r => (r.description, r.target, r.behaviors, r.row)), Is.EqualTo(new[]
            {
                ("LID", "BOX|LID", "Test Fade", 0), ("HINGE", "BOX|HINGE", "Test Fade", 1),
            }), "a row that names two objects stands on each one's line");
            Assert.That(step.Matches("hinge") && step.Matches("FADES") && !step.Matches("door"), "a search reads a step's objects and the sheet's words too");
            Assert.That(ManifestLines.BehaviorLabel("press_hold"), Is.EqualTo("Press Hold"));
            // A family reads as one behavior, a suffix saying how it is set (the Shot Manifest's Fade · in). 2026-11-24
            Assert.That(ManifestLines.BehaviorLabel("test_swing_open"), Is.EqualTo("Test Swing · open"));
            Assert.That(ManifestBehaviors.FamilyOf("test_swing_shut"), Is.EqualTo("test_swing"));
            Assert.That(ManifestBehaviors.Family("test_swing_shut"), Is.EqualTo(new[] { "test_swing_open", "test_swing_shut" }));
            Assert.That(ManifestBehaviors.Family("test_fade"), Is.EqualTo(new[] { "test_fade" }), "no family: itself alone");
            Assert.That(ManifestLines.Of(null, null), Is.Empty);
            Assert.That(ManifestWindow.Chips(step.rows[0]).Select(c => (c.label, c.level)), Is.EqualTo(new[] { ("Test Fade", "") }), "no state yet: a plain chip");

            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                manifest.off.Add("test_fade");
                ManifestLines.Line off = ManifestLines.Of(plan, null, manifest).Single().rows[0];
                Assert.That(off.tokens.Single().off && off.tokens.Single().offEverywhere, "what the manifest turned off shows before an assessment too");
                Assert.That(ManifestWindow.Chips(off).Select(c => c.level), Is.EqualTo(new[] { "off" }), "a grey chip");
                Assert.That(off.Quiet, Is.False, "an edit of this project's is something to say");
                Assert.That((off.status, off.label), Is.EqualTo((ManifestStatus.Off, "off")), "every behavior of an object off: its line says so");

                // What it takes from one object and gives another shows before an assessment too.
                manifest.off.Clear();
                manifest.edits.Add(new Manifest.Edit { step = "S01", target = "BOX|HINGE", behavior = "test_fade" });
                manifest.edits.Add(new Manifest.Edit { step = "S01", target = "BOX|LID", behavior = "test_pick", add = true });
                ManifestLines.Line edited = ManifestLines.Of(plan, null, manifest).Single();
                Assert.That(edited.rows.Single(r => r.target == "BOX|HINGE").tokens.Select(k => (k.label, k.off, k.offEverywhere)),
                            Is.EqualTo(new[] { ("Test Fade", true, false) }));
                Assert.That(edited.rows.Single(r => r.target == "BOX|LID").tokens.Select(k => (k.label, k.off, k.added)),
                            Is.EqualTo(new[] { ("Test Fade", false, false), ("Test Pick", false, true) }));
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }

        static ManifestAssessment Assessed(SheetPlan.Shape.Payload plan, params ManifestAssessment.Row[] rows)
        {
            var assessed = new ManifestAssessment();
            assessed.planIssues.AddRange(plan.issues);
            var step = new ManifestAssessment.Step { id = "S01" };
            step.rows.AddRange(rows);
            assessed.steps.Add(step);
            assessed.rows.AddRange(rows);
            return assessed;
        }

        static ManifestAssessment.Row Row(string behavior, string text, int source, params string[] targets)
        {
            var row = new ManifestAssessment.Row { step = "S01", index = source, source = source, behavior = behavior, text = text };
            row.targets.AddRange(targets);
            return row;
        }

        [Test]
        public void What_waits_on_a_person_is_a_report_for_the_sheets_owner()
        {
            var plan = SheetPlan.Read(Json, "test", out _);
            ManifestAssessment.Row unread = Row("unread", "Something else", 1);
            unread.state = ManifestAssessment.State.Unresolved;
            unread.cause = ManifestStatus.Sheet;
            unread.problems.Add("row 2 (Step Contents): no phrase reads it: write an Action");
            ManifestAssessment assessed = Assessed(plan, Row("test_fade", "The lid fades in", 0, "BOX|LID", "BOX|HINGE"), unread);

            string report = ManifestLines.IssuesReport(plan, assessed);
            Assert.That(report, Does.StartWith("# Manifest issues: test.csv"));
            Assert.That(report, Does.Contain("- no step plays the take Z99"), "the sheet's own issues first");
            Assert.That(report, Does.Contain("- **S01** row 2 (Unread): \"Something else\"\n  - row 2 (Step Contents): no phrase reads it: write an Action"));
            Assert.That(report, Does.Not.Contain("The lid fades in"), "a row in order is not in it");

            ManifestLines.Line line = ManifestLines.Of(plan, assessed).Single();
            Assert.That(line.label, Is.EqualTo("sheet issue"), "a step says its worst row");
            Assert.That(line.tooltip, Is.EqualTo("✖ Something else: sheet issue"));
            ManifestLines.Token token = line.tokens.Single();
            Assert.That(token.tooltip, Does.StartWith("✖ Unread  (sheet issue)\n✖ row 2 (Step Contents): no phrase reads it: write an Action\nSheet: Something else"),
                        "a behavior's tooltip: its state, its issues, the sheet's words");
            Assert.That(ManifestWindow.Chips(line).Select(c => (c.label, c.level)), Is.EqualTo(new[] { ("Unread", "error") }), "a chip in its state's colour");
            Assert.That(ManifestWindow.Chips(line)[0].tooltip, Does.StartWith("✖ Unread  (sheet issue)").And.Contain("Click:"), "its own tooltip");
            Assert.That(line.rows[0].Quiet && !line.Quiet);
            Assert.That(ManifestWindow.Chips(line.rows[0]).Select(c => c.level), Is.EqualTo(new[] { "valid" }), "built: a green chip");
        }

        [Test]
        public void What_this_project_turned_off_or_added_stands_on_its_objects_line_and_in_the_report()
        {
            var plan = SheetPlan.Read(Json, "test", out _);
            // The fade is left out for the lid alone; a glow of the lid is off; a pick of it is added here.
            ManifestAssessment.Row fade = Row("test_fade", "The lid fades in", 0, "BOX|HINGE");
            fade.dropped.Add("BOX|LID");
            ManifestAssessment.Row select = Row("test_pick", "test_pick LID (added in this project)", -1, "BOX|LID");
            select.added = true;
            ManifestAssessment assessed = Assessed(plan, fade, select);
            ManifestAssessment.Row highlight = Row("test_glow", "The lid glows", 1, "BOX|LID");
            highlight.off = true;
            assessed.steps[0].off.Add(highlight);

            ManifestLines.Line step = ManifestLines.Of(plan, assessed).Single();
            ManifestLines.Line lid = step.rows.Single(r => r.description == "LID"), hinge = step.rows.Single(r => r.description == "HINGE");
            Assert.That(hinge.tokens.Select(k => (k.label, k.off, k.added)), Is.EqualTo(new[] { ("Test Fade", false, false) }));
            Assert.That(lid.tokens.Select(k => (k.label, k.off, k.added)), Is.EqualTo(new[]
            {
                ("Test Fade", true, false), ("Test Glow", true, false), ("Test Pick", false, true),
            }), "in the sheet's order, what was added last");
            Assert.That(lid.tokens[0].tooltip, Does.Contain("off for this object"));
            Assert.That(ManifestWindow.Chips(lid).Select(c => (c.label, c.level)), Is.EqualTo(new[] { ("Test Fade", "off"), ("Test Glow", "off"), ("Test Pick", "edit") }),
                        "what this project left out grey, what it added amber");
            Assert.That(lid.Quiet, Is.False);
            Assert.That(assessed.steps[0].off[0].Status, Is.EqualTo(ManifestStatus.Off));

            var manifest = ScriptableObject.CreateInstance<Manifest>();
            try
            {
                manifest.off.Add("test_glow");
                manifest.edits.Add(new Manifest.Edit { step = "S01", target = "BOX|LID", behavior = "test_fade" });
                manifest.edits.Add(new Manifest.Edit { step = "S01", target = "BOX|LID", behavior = "test_pick", add = true });
                select.warnings.Add("the pick's own warning");  // the project's row: nothing the sheet's owner sees as one of its rows
                string report = ManifestLines.IssuesReport(plan, assessed, manifest);
                Assert.That(report, Does.Not.Contain("row 0").And.Not.Contain("the pick's own warning"), "a row added here is no row of the sheet");
                Assert.That(report, Does.Contain("## Built otherwise than the sheet says"));
                Assert.That(report, Does.Contain("- Test Glow: off for every row").And.Contain("- **S01** LID: Test Fade left out").And.Contain("- **S01** LID: Test Pick added"));
            }
            finally
            {
                Object.DestroyImmediate(manifest);
            }
        }
    }
}
