using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Steps.Tests
{
    /// <summary>
    /// <see cref="Sequence"/> and <see cref="Step"/>: steps run in order, completed by their
    /// signals, going on by themselves or by a choice, each enabling its components while it runs.
    /// </summary>
    public class SequenceTests
    {
        GameObject _root;
        Sequence _sequence;
        List<string> _entered;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Steps");
            _sequence = _root.AddComponent<Sequence>();
            _entered = new List<string>();
            _sequence.onStepEntered.AddListener(s => _entered.Add(s.id));
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        Step Add(string id, bool auto, params string[] awaits)
        {
            var node = new GameObject(id);
            node.transform.SetParent(_root.transform);
            var step = node.AddComponent<Step>();
            step.id = id;
            step.autoAdvance = auto;
            step.awaits.AddRange(awaits);
            _sequence.steps.Add(step);
            return step;
        }

        [Test]
        public void A_step_run_again_ends_anew()
        {
            Step first = Add("A01", false);
            Add("A02", false);
            var end = first.gameObject.AddComponent<Light>();  // any Behaviour: a fade, a viewpoint
            first.ends.Add(end);
            _sequence.Begin();
            Assert.That(end.enabled, Is.False);
            _sequence.Next();
            Assert.That(end.enabled, Is.True, "enabled as A01 ends");
            _sequence.GoTo("A01");
            Assert.That(end.enabled, Is.False, "run again: off until it ends again, so enabling it acts again");
            _sequence.Next();
            Assert.That(end.enabled, Is.True);
        }

        [Test]
        public void A_step_going_on_by_itself_runs_its_dwell_first()
        {
            Add("IN_01", true).dwell = 3f;
            Add("A01", true, "A01").dwell = 1f;
            Add("A02", false);
            _sequence.Begin();
            Assert.That(_sequence.Current.id, Is.EqualTo("IN_01"), "complete as it starts, it still runs its dwell");
            _sequence.Tick(2f);
            Assert.That(_sequence.Current.id, Is.EqualTo("IN_01"));
            _sequence.Tick(1f);
            Assert.That(_sequence.Current.id, Is.EqualTo("A01"));
            _sequence.Tick(5f);
            Assert.That(_sequence.Current.id, Is.EqualTo("A01"), "run long enough, it still awaits its signal");
            _sequence.Signal("A01");
            Assert.That(_sequence.Current.id, Is.EqualTo("A02"), "its dwell run, its signal sends it on");
        }

        [Test]
        public void A_choice_that_restarts_begins_again_outside_a_player()
        {
            Add("A01", false);
            Add("A02", false).choices.Add(new StepChoice { label = "Start Over", outcome = ChoiceOutcome.Restart });
            _sequence.Begin();
            _sequence.Next();
            Assert.That(_sequence.Choose("start over"), Is.True);
            Assert.That(_sequence.Current.id, Is.EqualTo("A01"));
            Assert.That(_entered, Is.EqualTo(new[] { "A01", "A02", "A01" }));
        }

        [Test]
        public void A_choice_that_starts_its_step_holds_its_targets_and_its_going_on_until_taken()
        {
            // Held at its start: read first, then the step's actions come on, and it goes on by itself once done.
            Step read = Add("B01", true, "select:DOOR");
            read.choices.Add(new StepChoice { label = "Begin", outcome = ChoiceOutcome.Start });
            var target = read.gameObject.AddComponent<Light>();  // any Behaviour: the step's target
            read.activates.Add(target);
            Add("B02", false);
            _sequence.Begin();
            Assert.That((read.Started, target.enabled), Is.EqualTo((false, false)), "held at its start: its target off");
            _sequence.Signal("select:DOOR");
            Assert.That(_sequence.Current.id, Is.EqualTo("B01"), "not started: it does not go on");
            Assert.That(_sequence.Choose("Begin"), Is.True);
            Assert.That((read.Started, target.enabled, _sequence.Current.id), Is.EqualTo((true, true, "B01")), "started, and still the step");
            Assert.That(_sequence.Choose("Begin"), Is.False, "started once");
            _sequence.Tick(0.1f);
            Assert.That(_sequence.Current.id, Is.EqualTo("B02"), "complete and started: it goes on by itself");
        }

        [Test]
        public void A_step_has_no_method_unity_calls_by_name_as_its_scene_starts()
        {
            // A step's own "Start" was called by Unity on every step as the scene started, and each whose call came after
            // the sequence's start stayed switched on (its arrows shown from the first step).
            const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            foreach (string message in new[] { "Awake", "Start", "OnEnable", "Update", "LateUpdate" })
                Assert.That(typeof(Step).GetMethod(message, all, null, System.Type.EmptyTypes, null), Is.Null, $"Step.{message} would run on every step by itself");
        }

        [Test]
        public void A_choice_that_waits_is_taken_only_once_its_step_is_complete()
        {
            Step take = Add("C03", false, "select:SCREWDRIVER_GRP");
            take.choices.Add(new StepChoice { label = "Next", waits = true });
            Add("C04", false);
            int completed = 0;
            take.onComplete.AddListener(() => completed++);
            _sequence.Begin();
            Assert.That(_sequence.Choose("Next"), Is.False, "the screwdriver not taken yet: Next is not offered");
            Assert.That(_sequence.Current.id, Is.EqualTo("C03"));
            _sequence.Signal("select:SCREWDRIVER_GRP");
            Assert.That(completed, Is.EqualTo(1), "complete: a page waiting on it shows Next now");
            Assert.That(_sequence.Choose("Next"), Is.True);
            Assert.That(_sequence.Current.id, Is.EqualTo("C04"));
        }

        [Test]
        public void A_step_completes_on_all_its_signals_and_goes_on_by_itself()
        {
            Add("A01", true, "A01_1", "A01_2");
            Add("A02", true, "A02");
            _sequence.Begin();
            Assert.That(_sequence.Current.id, Is.EqualTo("A01"));
            _sequence.Signal("A01_2");
            _sequence.Signal("B99");
            Assert.That(_sequence.Current.id, Is.EqualTo("A01"), "one signal of two, and one it does not await");
            _sequence.Signal("A01_1");
            Assert.That(_sequence.Current.id, Is.EqualTo("A02"));
            _sequence.Signal("A02");
            Assert.That(_sequence.Finished && _sequence.Current == null);
        }

        [Test]
        public void A_step_awaiting_nothing_that_goes_on_by_itself_is_passed_through()
        {
            Add("IN_01", true);
            Add("IN_02", false);
            _sequence.Begin();
            Assert.That(_entered, Is.EqualTo(new[] { "IN_01", "IN_02" }));
            Assert.That(_sequence.Current.id, Is.EqualTo("IN_02"), "a step waiting for a choice stays");
        }

        [Test]
        public void A_choice_goes_to_its_step_or_on_to_the_next()
        {
            Step b03 = Add("B03", false);
            b03.choices.Add(new StepChoice { label = "YES", to = "B03_5" });
            b03.choices.Add(new StepChoice { label = "NO", to = "B04" });
            Step wrong = Add("B03_5", false);
            wrong.choices.Add(new StepChoice { label = "Try Again", to = "B03" });
            Add("B04", false).choices.Add(new StepChoice { label = "Next" });
            Add("B05", false);
            _sequence.Begin();
            Assert.That(_sequence.Choose("yes"), Is.True);
            Assert.That(_sequence.Current.id, Is.EqualTo("B03_5"));
            _sequence.Choose("Try Again");
            _sequence.Choose("NO");
            Assert.That(_sequence.Current.id, Is.EqualTo("B04"));
            Assert.That(_sequence.Choose("Maybe"), Is.False);
            _sequence.Choose("Next");
            Assert.That(_sequence.Current.id, Is.EqualTo("B05"));
        }

        [Test]
        public void An_exit_finishes_the_sequence_wherever_its_step_stands_and_a_pick_chooses_for_an_event()
        {
            Add("B04", false).choices.Add(new StepChoice { label = "Exit", outcome = ChoiceOutcome.Finish });
            Add("B05", false);
            bool finished = false;
            _sequence.onFinished.AddListener(() => finished = true);
            _sequence.Begin();
            _sequence.Pick("Maybe");
            Assert.That(_sequence.Current.id, Is.EqualTo("B04"), "a label the step does not offer changes nothing");
            _sequence.Pick("exit");
            Assert.That(_sequence.Finished && finished && _sequence.Current == null, "the exit finished it, though a step follows");
        }

        [Test]
        public void What_a_step_ends_with_is_enabled_as_it_ends_and_rewound_as_the_sequence_begins()
        {
            var lamp = new GameObject("LAMP").AddComponent<Light>();
            lamp.transform.SetParent(_root.transform);
            Add("A01", false).ends.Add(lamp);
            Add("A02", false);
            lamp.enabled = true;
            _sequence.Begin();
            Assert.That(lamp.enabled, Is.False, "not until its step ends");
            _sequence.Next();
            Assert.That(lamp.enabled, Is.True);
            _sequence.Begin();
            Assert.That(lamp.enabled, Is.False);
        }

        [Test]
        public void What_a_step_turns_on_stays_on_until_a_later_step_turns_it_off_and_is_rewound()
        {
            var marker = new GameObject("MARKER").AddComponent<Light>();
            marker.transform.SetParent(_root.transform);
            marker.enabled = true;
            Add("A01", false);
            Add("A02", false).turnsOn.Add(marker);
            Add("A03", false);
            Add("A04", false).turnsOff.Add(marker);
            _sequence.Begin();
            Assert.That(marker.enabled, Is.False, "not until its step starts");
            _sequence.Next();
            Assert.That(marker.enabled, Is.True);
            _sequence.Next();
            Assert.That(marker.enabled, Is.True, "left on after its step");
            _sequence.Next();
            Assert.That(marker.enabled, Is.False);
            _sequence.GoTo("A03");
            _sequence.Begin();
            Assert.That(marker.enabled, Is.False);
        }

        [Test]
        public void A_choice_for_any_other_label_takes_what_no_named_choice_does()
        {
            Step pad = Add("D08", false);
            pad.choices.Add(new StepChoice { label = "1337", to = "D10" });
            pad.choices.Add(new StepChoice { label = "any other", to = "D09", otherwise = true });
            Add("D09", false).choices.Add(new StepChoice { label = "Try Again", to = "D08" });
            Add("D10", false);
            _sequence.Begin();
            Assert.That(_sequence.Choose("42"), Is.True);
            Assert.That(_sequence.Current.id, Is.EqualTo("D09"));
            Assert.That(_sequence.Choose("42"), Is.False, "a step without one takes only its named choices");
            _sequence.Choose("Try Again");
            _sequence.Choose("1337");
            Assert.That(_sequence.Current.id, Is.EqualTo("D10"), "the named choice wins");
        }

        [Test]
        public void A_step_awaiting_nothing_raises_its_completion_as_it_starts_once_though_it_waits_for_a_choice()
        {
            // Its page's Next waits on onComplete: a step awaiting nothing never raised it, so its Next never showed.
            Step read = Add("A01", false);
            read.choices.Add(new StepChoice { label = "Next", waits = true });
            Add("A02", false);
            int completed = 0;
            read.onComplete.AddListener(() => completed++);
            _sequence.Begin();
            Assert.That(completed, Is.EqualTo(1), "complete as it starts");
            _sequence.Tick(1f);
            Assert.That(completed, Is.EqualTo(1), "once a run");
            Assert.That(_sequence.Choose("Next"), Is.True);
            _sequence.GoTo("A01");
            Assert.That(completed, Is.EqualTo(2), "run again: complete again");
        }

        [Test]
        public void A_held_step_awaiting_nothing_completes_as_its_start_is_taken()
        {
            Step read = Add("A01", false);
            read.choices.Add(new StepChoice { label = "Begin", outcome = ChoiceOutcome.Start });
            int completed = 0;
            read.onComplete.AddListener(() => completed++);
            _sequence.Begin();
            Assert.That(completed, Is.Zero, "held at its start: not begun");
            Assert.That(_sequence.Choose("Begin"), Is.True);
            Assert.That(completed, Is.EqualTo(1));
        }

        [Test]
        public void What_a_steps_completion_does_to_the_sequence_stands()
        {
            // A listener on the last step's completion finishes the sequence: Signal then read a step that was gone.
            Step last = Add("A01", true, "done");
            last.onComplete.AddListener(_sequence.Next);
            _sequence.Begin();
            Assert.DoesNotThrow(() => _sequence.Signal("done"));
            Assert.That(_sequence.Finished, Is.True);

            // A listener that goes elsewhere: that step stands, not skipped past as the step it left goes on.
            _sequence.steps.Clear();
            Step first = Add("B01", true);
            first.dwell = 1f;
            Add("B02", false);
            Add("B03", false);
            Add("B04", false);
            first.onComplete.AddListener(() => _sequence.GoTo("B03"));
            _entered.Clear();
            _sequence.Begin();
            _sequence.Tick(1f);
            Assert.That(_sequence.Current.id, Is.EqualTo("B03"), "where its completion sent it, not the step after");
            Assert.That(_entered, Is.EqualTo(new[] { "B01", "B03" }));
        }

        [Test]
        public void What_a_steps_exit_does_to_the_sequence_stands_and_the_step_it_was_left_for_is_never_entered()
        {
            // A listener on the step left went elsewhere: the move it interrupted then entered its own step over it, and two
            // steps ran at once.
            Step first = Add("A01", false);
            Step second = Add("A02", false);
            Add("A03", false);
            var running = second.gameObject.AddComponent<Light>();
            running.enabled = false;
            second.turnsOn.Add(running);
            int exits = 0;
            void Once()
            {
                exits++;
                first.onExit.RemoveListener(Once);
                _sequence.GoTo("A03");
            }
            first.onExit.AddListener(Once);
            _sequence.Begin();
            _sequence.Next();
            Assert.That(_sequence.Current.id, Is.EqualTo("A03"), "where its exit sent it");
            Assert.That(_entered, Is.EqualTo(new[] { "A01", "A03" }), "the step it was left for is never entered");
            Assert.That(running.enabled, Is.False, "so nothing of it runs");
            Assert.That(exits, Is.EqualTo(1), "and the step left is left once");
        }

        [Test]
        public void What_a_steps_entry_does_to_the_sequence_stands_and_the_step_left_is_not_said_entered_after_it()
        {
            // A listener on a step's entry went elsewhere: the step it left was then said entered, after the one it went to.
            Add("B01", false);
            Step second = Add("B02", false);
            Add("B03", false);
            Add("B04", false);
            void Once()
            {
                second.onEnter.RemoveListener(Once);
                _sequence.GoTo("B04");
            }
            second.onEnter.AddListener(Once);
            _sequence.Begin();
            _sequence.Next();
            Assert.That(_sequence.Current.id, Is.EqualTo("B04"));
            Assert.That(_entered, Is.EqualTo(new[] { "B01", "B04" }), "the last said entered is the one running");
        }

        [Test]
        public void A_restart_with_its_reload_off_begins_again_and_says_it_will_not_reload()
        {
            Add("A01", false);
            Add("A02", false);
            _sequence.reloadOnRestart = false;
            _sequence.Begin();
            _sequence.Next();
            Assert.That(_sequence.Reloads, Is.False);
            _sequence.Restart();
            Assert.That(_sequence.Current.id, Is.EqualTo("A01"));
        }

        [Test]
        public void A_steps_components_are_enabled_only_while_it_runs()
        {
            var door = new GameObject("DOOR").AddComponent<Light>();
            door.transform.SetParent(_root.transform);
            Add("A01", false);
            Add("A02", false).activates.Add(door);
            _sequence.Begin();
            Assert.That(door.enabled, Is.False, "disabled until its step runs");
            _sequence.Next();
            Assert.That(door.enabled, Is.True);
            _sequence.Next();
            Assert.That(door.enabled, Is.False);
        }
    }
}
