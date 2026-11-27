using System.Collections.Generic;
using M3trik.Unitytk.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Steps.Tests
{
    /// <summary>
    /// <see cref="SequenceAutopilot"/>: a sequence walked through as a person would, its parts acted on by the actor it
    /// is given, a choice taken forward, a press let go on the next move, what plays waited for or finished, a step
    /// nothing completes noted stuck; a walk to a step stops as it starts.
    /// </summary>
    public class SequenceAutopilotTests
    {
        GameObject _root;
        Sequence _sequence;
        SequenceAutopilot _pilot;
        Actor _actor;

        // A stand-in for the part that knows the scene's parts: each part it acts on signals the step, as a selection does.
        sealed class Actor : IStepActor
        {
            internal Sequence Sequence;
            internal readonly List<string> Acted = new List<string>();
            internal readonly HashSet<string> Held = new HashSet<string>();
            internal int Playing;

            public bool Acts(Behaviour part) => part is Light;

            public string Act(Behaviour part, out bool held)
            {
                Acted.Add(part.name);
                held = Held.Contains(part.name);
                if (!held) Sequence.Signal(part.name);
                return (held ? "pressed " : "selected ") + part.name;
            }

            public string Release(Behaviour part)
            {
                Sequence.Signal(part.name);
                return "let go of " + part.name;
            }

            public bool Busy(bool finish, out string note)
            {
                note = null;
                if (Playing == 0) return false;
                if (!finish) return true;
                Playing = 0;
                note = "played its clips to their end";
                return true;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Steps");
            _sequence = _root.AddComponent<Sequence>();
            _pilot = _root.AddComponent<SequenceAutopilot>();
            _pilot.sequence = _sequence;
            _actor = new Actor { Sequence = _sequence };
            _pilot.Actor = _actor;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        Step Add(string id, bool auto, params string[] parts)
        {
            var node = new GameObject(id);
            node.transform.SetParent(_root.transform);
            var step = node.AddComponent<Step>();
            step.id = id;
            step.autoAdvance = auto;
            foreach (string part in parts)
            {
                var target = new GameObject(part).AddComponent<Light>();  // any Behaviour: a part the actor acts on
                target.transform.SetParent(node.transform);
                step.awaits.Add(part);
                step.activates.Add(target);
            }
            _sequence.steps.Add(step);
            return step;
        }

        [Test]
        public void It_walks_the_steps_acting_on_their_parts_and_choosing_forward()
        {
            Add("A01", true, "DOOR");
            Step ask = Add("A02", false);
            ask.choices.Add(new StepChoice { label = "Back", to = "A01" });
            ask.choices.Add(new StepChoice { label = "Next" });
            Add("A03", true, "LOCK");
            _actor.Held.Add("LOCK");
            Assert.That(_pilot.Run(), Is.True);
            Assert.That(_sequence.Finished, Is.True);
            Assert.That(_pilot.Stuck, Is.Empty, string.Join("\n", _pilot.Log));
            Assert.That(_actor.Acted, Is.EqualTo(new[] { "DOOR", "LOCK" }));
            Assert.That(_pilot.Log, Has.Some.EqualTo("A02: chose 'Next'"), "forward, not back");
            Assert.That(_pilot.Log, Has.Some.EqualTo("A03: let go of LOCK"), "a press let go on the next move");
        }

        [Test]
        public void It_waits_for_what_plays_or_finishes_it_on_a_fast_walk()
        {
            Add("A01", true, "DOOR");
            _actor.Playing = 1;
            _pilot.Tick(0.1f);  // begins
            _pilot.Tick(0.1f);
            Assert.That(_actor.Acted, Is.Empty, "something plays: it waits");
            _pilot.fastForward = true;
            _pilot.Tick(0.1f);
            Assert.That(_pilot.Log, Has.Some.EqualTo("A01: played its clips to their end"));
            Assert.That(_pilot.Run(), Is.True);
            Assert.That(_actor.Acted, Is.EqualTo(new[] { "DOOR" }));
        }

        [Test]
        public void A_walk_to_a_step_stops_as_it_starts_and_a_step_with_no_actor_is_stuck()
        {
            Add("A01", true, "DOOR");
            Add("A02", true, "LOCK");
            Add("A03", false);
            _pilot.stopAt = "A02";
            Assert.That(_pilot.Run(), Is.True);
            Assert.That(_sequence.Current.id, Is.EqualTo("A02"), "stopped as it starts");
            Assert.That(_actor.Acted, Is.EqualTo(new[] { "DOOR" }));

            _pilot.stopAt = "";
            _pilot.Actor = null;  // none set, none beside it: no part is acted on
            _pilot.fastForward = true;
            Assert.That(_pilot.Run(), Is.True);
            Assert.That(_pilot.Stuck, Is.EqualTo(new[] { "A02" }), "nothing completes it");
        }

        [Test]
        public void The_scenes_sequence_is_kept_once_found_and_found_again_once_gone()
        {
            // Each frame searched the scene for it where none was named.
            _pilot.sequence = null;
            Assert.That(_pilot.Sequence, Is.SameAs(_sequence), "the scene's");
            var later = new GameObject("Later");
            try
            {
                Sequence other = later.AddComponent<Sequence>();
                Assert.That(_pilot.Sequence, Is.SameAs(_sequence), "the one found is kept");
                Object.DestroyImmediate(_sequence);
                Assert.That(_pilot.Sequence, Is.SameAs(other), "once gone, the scene's again");
            }
            finally
            {
                Object.DestroyImmediate(later);
            }
        }
    }
}
