using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Steps.Tests
{
    /// <summary>
    /// The Steps actions, held to the kernel's three checks as a chain a builder runs (a
    /// sequence made, two steps added, awaits, a choice, a component enabled during a step,
    /// events wired to signal and to choose, and an exit): one Ctrl+Z restores the scene, the dry run's plan names every
    /// change the run makes, and a second run plans nothing.
    /// </summary>
    public class StepsActionsTests
    {
        const string Name = "Steps (actions test)";
        GameObject _door;
        Light _light;
        TestEvents _events;

        [SetUp]
        public void SetUp()
        {
            _door = new GameObject("DOOR");
            _light = _door.AddComponent<Light>();
            _events = ScriptableObject.CreateInstance<TestEvents>();
            _events.name = _door.name;  // as a component's name is its object's
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_door);
            Object.DestroyImmediate(_events);
            var made = GameObject.Find(Name);
            if (made != null) Object.DestroyImmediate(made);
        }

        void Chain(OpContext ctx)
        {
            StepsActions.CreateSequence(Name, ctx);
            StepsActions.AddStep(Name, "A01", "Open the door", ctx);
            StepsActions.AddStep(Name, "A02", "Check it", ctx);
            StepsActions.Await(Name, "A01", "A01", ctx);
            StepsActions.SetAutoAdvance(Name, "A01", true, ctx);
            StepsActions.AddChoice(Name, "A02", "Next", null, ctx);
            StepsActions.EnableDuring(Name, "A01", _door, _light, ctx);
            StepsActions.EnableAtEnd(Name, "A02", _door, _light, ctx);
            StepsActions.SignalOn(_door, _events, _events.clicked, Name, "clicked", ctx);
            StepsActions.SignalOn(_door, _events, _events.ended, Name, ctx);
            StepsActions.AddExit(Name, "A02", "Exit", ctx);
            StepsActions.ChooseOn(_door, _events, _events.chosen, Name, ctx);
            StepsActions.AddOtherwise(Name, "A02", "A01", ctx);
            StepsActions.EnableFrom(Name, "A01", _door, _light, ctx);
            StepsActions.DisableFrom(Name, "A02", _door, _light, ctx);
        }

        [Test]
        public void The_chain_plans_what_it_then_does_and_a_second_run_plans_nothing()
        {
            var dry = new OpContext(dryRun: true);
            Chain(dry);
            Assert.That(GameObject.Find(Name), Is.Null, "a dry run makes nothing");
            var live = new OpContext();
            Chain(live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(live.Issues, Is.Empty);
            var again = new OpContext(dryRun: true);
            Chain(again);
            Assert.That(again.Changes, Is.Empty);

            var sequence = GameObject.Find(Name).GetComponent<Sequence>();
            Assert.That(sequence.steps.Select(s => (s.id, s.title)), Is.EqualTo(new[] { ("A01", "Open the door"), ("A02", "Check it") }));
            Step a01 = sequence.Find("A01");
            Assert.That((a01.awaits.Single(), a01.autoAdvance, a01.activates.Single()), Is.EqualTo(("A01", true, (Behaviour)_light)));
            Assert.That(sequence.Find("A02").Choice("next").to, Is.Empty);
            Assert.That(sequence.Find("A02").ends.Single(), Is.SameAs(_light));
            Assert.That(_events.clicked.GetPersistentMethodName(0), Is.EqualTo(nameof(Sequence.Signal)));
            Assert.That(_events.ended.GetPersistentTarget(0), Is.SameAs(sequence));
            Assert.That(sequence.Find("A02").Choice("exit").Finishes, Is.True, "an exit finishes the sequence");
            Assert.That(_events.chosen.GetPersistentMethodName(0), Is.EqualTo(nameof(Sequence.Pick)));
            StepChoice other = sequence.Find("A02").ChoiceFor("anything");
            Assert.That((other.otherwise, other.to), Is.EqualTo((true, "A01")), "any other answer goes back");
            Assert.That((a01.turnsOn.Single(), sequence.Find("A02").turnsOff.Single()), Is.EqualTo(((Behaviour)_light, (Behaviour)_light)));
        }

        [Test]
        public void Two_components_on_one_object_in_one_step_are_planned_apart()
        {
            void Run(OpContext ctx, Light light, AudioSource sound)
            {
                StepsActions.CreateSequence(Name, ctx);
                StepsActions.AddStep(Name, "A01", "Open", ctx);
                StepsActions.EnableDuring(Name, "A01", _door, light, ctx);
                StepsActions.EnableDuring(Name, "A01", _door, sound, ctx);
            }
            var dry = new OpContext(dryRun: true);
            Run(dry, null, null);  // components a dry run only planned
            var live = new OpContext();
            Run(live, _light, _door.AddComponent<AudioSource>());
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(GameObject.Find(Name).GetComponent<Sequence>().Find("A01").activates.Count, Is.EqualTo(2));
        }

        [Test]
        public void Two_objects_of_one_name_in_one_step_are_planned_apart()
        {
            // Two lamps of one name under two parents: a plan naming each by its name took the second for the first.
            var left = new GameObject("LEFT");
            var right = new GameObject("RIGHT");
            try
            {
                GameObject leftLamp = new GameObject("LAMP"), rightLamp = new GameObject("LAMP");
                leftLamp.transform.SetParent(left.transform);
                rightLamp.transform.SetParent(right.transform);
                void Run(OpContext ctx, Light one, Light other)
                {
                    StepsActions.CreateSequence(Name, ctx);
                    StepsActions.AddStep(Name, "A01", "Light them", ctx);
                    StepsActions.EnableDuring(Name, "A01", leftLamp, one, ctx);
                    StepsActions.EnableDuring(Name, "A01", rightLamp, other, ctx);
                }
                var dry = new OpContext(dryRun: true);
                Run(dry, null, null);  // lights a dry run only planned
                var live = new OpContext();
                Run(live, leftLamp.AddComponent<Light>(), rightLamp.AddComponent<Light>());
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(GameObject.Find(Name).GetComponent<Sequence>().Find("A01").activates.Count, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(left);
                Object.DestroyImmediate(right);
            }
        }

        [Test]
        public void Parts_of_two_planned_objects_of_one_name_are_planned_as_their_live_run_switches_them()
        {
            // A part's object was named by the string its caller built: two of one name were planned as one.
            string[] parents = { "LEFT", "RIGHT" };
            void Plan(OpContext ctx)
            {
                StepsActions.CreateSequence(Name, ctx);
                StepsActions.AddStep(Name, "A01", "Light them", ctx);
                StepsActions.AddStep(Name, "A02", "Done", ctx);
                foreach (string parent in parents)
                {
                    var lamp = new PlannedObject("LAMP", new PlannedObject(parent));
                    StepsActions.EnableFrom<Light>(Name, "A01", lamp, ctx);
                    StepsActions.EnableDuring<Light>(Name, "A01", lamp, ctx);
                    StepsActions.EnableAtEnd<Light>(Name, "A01", lamp, ctx);
                    StepsActions.DisableFrom<Light>(Name, "A02", lamp, ctx);
                }
            }
            var made = new List<GameObject>();
            try
            {
                var dry = new OpContext(dryRun: true);
                Plan(dry);
                var live = new OpContext();
                StepsActions.CreateSequence(Name, live);
                StepsActions.AddStep(Name, "A01", "Light them", live);
                StepsActions.AddStep(Name, "A02", "Done", live);
                foreach (string parent in parents)
                {
                    var holder = new GameObject(parent);
                    made.Add(holder);
                    var lamp = new GameObject("LAMP");
                    lamp.transform.SetParent(holder.transform);
                    Light light = lamp.AddComponent<Light>();
                    StepsActions.EnableFrom(Name, "A01", lamp, light, live);
                    StepsActions.EnableDuring(Name, "A01", lamp, light, live);
                    StepsActions.EnableAtEnd(Name, "A01", lamp, light, live);
                    StepsActions.DisableFrom(Name, "A02", lamp, light, live);
                }
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Step a01 = GameObject.Find(Name).GetComponent<Sequence>().Find("A01");
                Assert.That((a01.turnsOn.Count, a01.activates.Count, a01.ends.Count), Is.EqualTo((2, 2, 2)), "each lamp its own");
                Assert.That(StepsActions.EnableDuring<Light>(Name, "A01", new PlannedObject("LAMP", new PlannedObject("LEFT")), new OpContext()),
                            Is.False, "a live run has nothing to switch on a planned object");
            }
            finally
            {
                foreach (GameObject each in made) Object.DestroyImmediate(each);
            }
        }

        [Test]
        public void A_rebuilt_steps_dry_run_plans_the_wiring_its_live_run_makes_anew()
        {
            // A dry run planned the rewire, then read the old wiring and planned none of what the live run wires again.
            Chain(new OpContext());
            StepsActions.BuiltFrom(Name, "A01", "select DOOR", new OpContext());
            void Rebuild(OpContext ctx)
            {
                StepsActions.BuiltFrom(Name, "A01", "select LAMP", ctx);
                StepsActions.Await(Name, "A01", "A01", ctx);
                StepsActions.SetAutoAdvance(Name, "A01", true, ctx);
                StepsActions.SetAutoAdvance(Name, "A02", true, ctx);
                StepsActions.SetStarts(Name, "A01", "Begin", false, ctx);  // an emptied step starts on nothing
                StepsActions.AddChoice(Name, "A01", "Next", null, ctx);
                StepsActions.EnableDuring(Name, "A01", _door, _light, ctx);
            }
            var dry = new OpContext(dryRun: true);
            Rebuild(dry);
            Assert.That(dry.Changes.Select(c => c.action), Has.Member("await 'A01'"), "what the emptied step awaits again");
            var live = new OpContext();
            Rebuild(live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Step a01 = GameObject.Find(Name).GetComponent<Sequence>().Find("A01");
            Assert.That((a01.awaits.Single(), a01.autoAdvance, a01.choices.Single().label, a01.activates.Single()),
                        Is.EqualTo(("A01", true, "Next", (Behaviour)_light)));
        }

        [Test]
        public void An_event_its_owner_does_not_hold_is_raised_and_not_wired()
        {
            StepsActions.CreateSequence(Name, new OpContext());
            var elsewhere = new UnityEngine.Events.UnityEvent();  // no field of the owner holds it
            var ctx = new OpContext();
            Assert.That(StepsActions.SignalOn(_door, _events, elsewhere, Name, "clicked", ctx), Is.False);
            Assert.That(ctx.Issues.Select(i => i.code), Is.EqualTo(new[] { PersistentCalls.UnreadableCode }));
            Assert.That(elsewhere.GetPersistentEventCount(), Is.Zero, "nothing wired on what it cannot read");
        }

        [Test]
        public void A_choice_event_of_an_object_a_dry_run_only_planned_is_planned_as_its_live_run_wires_it()
        {
            const string Panel = "Panel (actions test)";
            StepsActions.CreateSequence(Name, new OpContext());
            GameObject panel = null;
            try
            {
                var dry = new OpContext(dryRun: true);
                Assert.That(StepsActions.ChooseOn(new PlannedObject(Panel), Name, dry), Is.True);
                Assert.That(StepsActions.ChooseOn(new PlannedObject(Panel), Name, dry), Is.False, "planned once");
                panel = new GameObject(Panel);
                _events.name = Panel;  // the asset holding its event, named as its object
                var live = new OpContext();
                Assert.That(StepsActions.ChooseOn(panel, _events, _events.chosen, Name, live), Is.True);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(StepsActions.ChooseOn(new PlannedObject(Panel), Name, new OpContext()), Is.False, "a live run has nothing to wire on a planned object");
            }
            finally
            {
                if (panel != null) Object.DestroyImmediate(panel);
            }
        }

        [Test]
        public void The_chain_is_one_undo_step()
        {
            OpContext.RunMenu("Make the steps", Chain);
            Assert.That(GameObject.Find(Name), Is.Not.Null);
            Undo.PerformUndo();
            Assert.That(GameObject.Find(Name) == null && _events.clicked.GetPersistentEventCount() == 0, "one Ctrl+Z took it all away");
        }

        [Test]
        public void A_step_built_from_other_rows_is_wired_anew_and_one_built_from_the_same_is_left()
        {
            Chain(new OpContext());
            Step step = GameObject.Find(Name).GetComponent<Sequence>().Find("A01");
            Assert.That(step.awaits, Is.Not.Empty, "premise: the chain wired it");
            var first = new OpContext(dryRun: true);
            Assert.That(StepsActions.BuiltFrom(Name, "A01", "select DOOR", first), Is.False, "no mark yet: it only takes one");
            Assert.That(first.Changes.Single().action, Is.EqualTo("keep what it is built from"));
            StepsActions.BuiltFrom(Name, "A01", "select DOOR", new OpContext());
            Assert.That((step.source, step.awaits.Count > 0), Is.EqualTo(("select DOOR", true)), "marked, its wiring as it was");
            var same = new OpContext(dryRun: true);
            StepsActions.BuiltFrom(Name, "A01", "select DOOR", same);
            Assert.That(same.Changes, Is.Empty, "built from the same: nothing to do");

            var other = new OpContext(dryRun: true);
            Assert.That(StepsActions.BuiltFrom(Name, "A01", "", other), Is.True, "its rows changed: it would be wired anew");
            Assert.That(step.awaits, Is.Not.Empty, "a dry run changes nothing");
            OpContext.RunMenu("Rewire", ctx => StepsActions.BuiltFrom(Name, "A01", "", ctx));
            Assert.That(step.source, Is.EqualTo(""));
            Assert.That(step.awaits.Count + step.choices.Count + step.activates.Count + step.ends.Count + step.turnsOn.Count + step.turnsOff.Count,
                        Is.EqualTo(0), "what it awaited, offered and switched is gone: the build that follows wires what its rows now say");
            Assert.That(step.autoAdvance, Is.False);
            Undo.PerformUndo();
            Assert.That((step.source, step.awaits.Count > 0), Is.EqualTo(("select DOOR", true)), "one Ctrl+Z gives it back");
        }

        [Test]
        public void One_event_signals_two_things_once_each()
        {
            // Wired once to signal "opened", the same event's second signal was taken for wired already.
            StepsActions.CreateSequence(Name, new OpContext());
            Assert.That(StepsActions.SignalOn(_door, _events, _events.clicked, Name, "opened"), Is.True);
            Assert.That(StepsActions.SignalOn(_door, _events, _events.clicked, Name, "lit"), Is.True, "another signal: wired too");
            var again = new OpContext(dryRun: true);
            Assert.That(StepsActions.SignalOn(_door, _events, _events.clicked, Name, "opened", again), Is.False);
            Assert.That(StepsActions.SignalOn(_door, _events, _events.clicked, Name, "lit", again), Is.False);
            Assert.That(again.Changes, Is.Empty, "each wired once");
            Assert.That(_events.clicked.GetPersistentEventCount(), Is.EqualTo(2));
        }

        [Test]
        public void A_sequences_debug_commands_and_autopilot_are_planned_made_once_and_undone()
        {
            const string Panel = "Debug (actions test)";
            void Run(OpContext ctx)
            {
                StepsActions.CreateSequence(Name, ctx);
                GameObject node = GameObject.Find(Panel);
                if (node != null) StepsActions.AddDebugCommands(node, Name, ctx);
                else StepsActions.AddDebugCommands(new PlannedObject(Panel), ctx);
            }
            try
            {
                var dry = new OpContext(dryRun: true);
                Run(dry);
                new GameObject(Panel);
                OpContext.RunMenu("Debug commands", ctx =>
                {
                    Run(ctx);
                    Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                });
                var commands = GameObject.Find(Panel).GetComponent<SequenceDebugCommands>();
                var sequence = GameObject.Find(Name).GetComponent<Sequence>();
                Assert.That((commands.sequence, commands.autopilot.sequence, commands.autopilot.enabled), Is.EqualTo((sequence, sequence, false)),
                            "over the sequence, its autopilot off");
                var again = new OpContext(dryRun: true);
                Run(again);
                Assert.That(again.Changes, Is.Empty);
                Undo.PerformUndo();
                Assert.That(GameObject.Find(Panel).GetComponent<SequenceDebugCommands>() == null
                            && GameObject.Find(Panel).GetComponent<SequenceAutopilot>() == null, "one Ctrl+Z took them away");
            }
            finally
            {
                var panel = GameObject.Find(Panel);
                if (panel != null) Object.DestroyImmediate(panel);
            }
        }

        [Test]
        public void A_step_of_a_missing_sequence_is_raised_and_makes_nothing()
        {
            var ctx = new OpContext();
            Assert.That(StepsActions.AddStep("No Such Sequence", "A01", "x", ctx), Is.Null);
            Assert.That(StepsActions.Await("No Such Sequence", "A01", "x", ctx), Is.False);
            Assert.That(ctx.Issues.Select(i => i.code), Is.All.EqualTo(StepsActions.MissingCode));
            Assert.That(ctx.Changes, Is.Empty);
        }
    }
}
