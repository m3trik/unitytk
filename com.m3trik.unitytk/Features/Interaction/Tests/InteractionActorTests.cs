using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Interaction.Tests
{
    /// <summary>
    /// <see cref="InteractionActor"/>: how a player of steps acts on the feature's targets and waits for its clips, and
    /// the action that adds it.
    /// </summary>
    public class InteractionActorTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("delivery");

        [TearDown]
        public void TearDown()
        {
            if (_root.TryGetComponent(out ClipPlayer player)) player.Clear();
            Object.DestroyImmediate(_root);
        }

        GameObject Child(string name)
        {
            var node = new GameObject(name);
            node.transform.SetParent(_root.transform, false);
            return node;
        }

        [Test]
        public void An_actor_planned_on_a_planned_object_is_made_once_as_planned()
        {
            var dry = new OpContext(dryRun: true);
            Assert.That(Interactions.AddStepActor(new PlannedObject("PILOT", _root.transform), dry), Is.Null);
            GameObject pilot = Child("PILOT");
            Assert.That(Interactions.AddStepActor(pilot, dry), Is.Null, "planned once");
            var live = new OpContext();
            InteractionActor made = Interactions.AddStepActor(pilot, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(dry.Changes.Single().target, Is.EqualTo("delivery/PILOT/InteractionActor"));
            Assert.That(Interactions.AddStepActor(pilot, new OpContext(dryRun: true)), Is.SameAs(made), "one there: nothing");
        }

        [Test]
        public void It_takes_what_a_target_requires_then_selects_it_and_waits_for_a_clip_or_plays_it_to_its_end()
        {
            Hand hand = Child("HAND").AddComponent<Hand>();
            GameObject key = Child("KEY"), latch = Child("LATCH");
            Holdable holdable = Interactions.MakeHoldable(key);
            Target target = Interactions.MakeTarget(latch, key);
            var open = new AnimationClip { name = "Open" };
            open.SetCurve("LATCH", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
            ClipPlayer player = Interactions.AddClip(_root, open);
            InteractionActor actor = Child("PILOT").AddComponent<InteractionActor>();
            actor.hand = hand;

            Assert.That(actor.Acts(target) && !actor.Acts(hand), Is.True, "it acts on targets alone");
            Assert.That(actor.Act(target, out bool held), Is.EqualTo("took KEY in hand; selected LATCH"));
            Assert.That(hand.Holds(holdable) && !held, Is.True);

            Assert.That(actor.Busy(false, out string note) || note != null, Is.False, "nothing plays");
            player.Play("Open");
            Assert.That(actor.Busy(false, out note) && note == null, Is.True, "a clip plays: the walk waits");
            Assert.That(actor.Busy(true, out note), Is.True);
            Assert.That((note, player.Busy), Is.EqualTo(("played its clips to their end", false)), "a fast walk plays it to its end");
            player.Clear();
            Object.DestroyImmediate(player);
            Assert.That(actor.Busy(false, out _), Is.False, "a player gone is no longer waited for");
        }

        // A delivery with a button whose press plays Open (its trigger as Interactions wires it), and an actor.
        (Target button, ClipTrigger trigger, ClipPlayer player, InteractionActor actor) Button(bool whileHeld, float pressSeconds)
        {
            GameObject node = Child("BUTTON");
            Target button = Interactions.MakeTarget(node);
            var open = new AnimationClip { name = "Open" };
            open.SetCurve("BUTTON", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
            ClipPlayer player = Interactions.AddClip(_root, open);
            ClipTrigger trigger = Interactions.AddClipTrigger(node, _root, "Open", whileHeld: whileHeld, pressSeconds: pressSeconds, startEnabled: true);
            InteractionActor actor = Child("PILOT").AddComponent<InteractionActor>();
            actor.hand = Child("HAND").AddComponent<Hand>();
            foreach (UnityEngine.Events.UnityEventBase when in new UnityEngine.Events.UnityEventBase[] { button.onPressed, button.onReleased, button.onSelected })
                for (int i = 0; i < when.GetPersistentEventCount(); i++)  // its wiring called in the editor too (Unity's: in play alone)
                    when.SetPersistentListenerState(i, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            return (button, trigger, player, actor);
        }

        [Test]
        public void A_press_held_plays_while_it_is_held_and_is_let_go_on_the_next_move()
        {
            var (button, _, player, actor) = Button(whileHeld: true, pressSeconds: 0f);
            Assert.That(actor.Act(button, out bool held), Is.EqualTo("pressed BUTTON"));
            Assert.That((held, button.IsPressed, player.IsPlaying("Open")), Is.EqualTo((true, true, true)), "held down, it plays");
            Assert.That(actor.Release(button), Is.EqualTo("let go of BUTTON"));
            Assert.That((button.IsPressed, player.IsPaused("Open")), Is.EqualTo((false, true)), "let go, it stops where it stands");
        }

        [Test]
        public void A_timed_press_is_held_its_time_before_it_is_let_go_or_at_once_on_a_fast_walk()
        {
            // The actor selected a timed button (nothing listens to a selection there), and the walk let go of it at once.
            var (button, trigger, player, actor) = Button(whileHeld: false, pressSeconds: 0.5f);
            Assert.That(actor.Act(button, out bool held), Is.EqualTo("pressed BUTTON"));
            Assert.That((held, trigger.Counting, player.IsPlaying("Open")), Is.EqualTo((true, true, false)), "pressed, counting its time");
            Assert.That(actor.Busy(false, out string note) && note == null, Is.True, "the walk waits while it counts");
            trigger.Tick(0.6f);  // a player's frames
            Assert.That(player.IsPlaying("Open") && !trigger.Counting, Is.True, "held its time, it plays");
            Assert.That(actor.Busy(false, out _), Is.True, "and the walk waits for the clip");
            actor.Release(button);
            Assert.That(player.IsPlaying("Open"), Is.True, "let go after its time: it plays on");
            player.Finish();

            Assert.That(actor.Act(button, out held) == "pressed BUTTON" && held, Is.True);
            Assert.That(actor.Busy(true, out note), Is.True);
            Assert.That((note, player.IsPlaying("Open")), Is.EqualTo(("held BUTTON pressed its 0.5 s", true)), "a fast walk holds it its time at once");
        }
    }
}
