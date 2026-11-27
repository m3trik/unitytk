using System;
using M3trik.Unitytk.Contracts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Interaction.Tests
{
    /// <summary>
    /// The step actor's contract (<see cref="IStepActor"/>), one fixture run against its stand-in
    /// (<see cref="PureStepActor"/>) and each provider (<see cref="InteractionActor"/>): it acts on the parts it knows and
    /// no other; a plain part acted on is selected (not held), one it presses is held down until let go, each with a line
    /// for the log naming it; with nothing playing the walk has nothing to wait for or say, and with something playing it
    /// waits, or a fast walk plays it to its end and says so.
    /// </summary>
    public class StepActorContractTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("delivery (contract test)");

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

        // What a provider's fixture hands the contract: its parts, and how to read back what acting on them did.
        sealed class Rig
        {
            public IStepActor actor;
            public Behaviour part, pressable, other;  // a plain part it knows, one it presses, one it does not know
            public Func<bool> selected, pressed;      // the plain part taken as selected; the pressable held down now
            public Action play;                       // something the steps started plays
        }

        [Test]
        public void Its_stand_in_keeps_the_contract()
        {
            Behaviour part = Child("PART").AddComponent<Light>(), pressable = Child("BUTTON").AddComponent<Light>();
            var actor = new PureStepActor { ActsOn = acted => acted == part || acted == pressable };
            actor.Holds.Add(pressable);
            Keeps(new Rig
            {
                actor = actor, part = part, pressable = pressable, other = Child("OTHER").AddComponent<Light>(),
                selected = () => actor.Acted.Contains(part),
                pressed = () => actor.Acted.Contains(pressable) && !actor.Released.Contains(pressable),
                play = () => actor.Playing = 1,
            });
        }

        [Test]
        public void An_interaction_actor_keeps_the_contract()
        {
            Target latch = Interactions.MakeTarget(Child("LATCH"));
            bool selected = false;
            latch.onSelected.AddListener(by => selected = true);
            GameObject node = Child("BUTTON");
            Target button = Interactions.MakeTarget(node);
            var open = new AnimationClip { name = "Open" };
            open.SetCurve("BUTTON", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0, 0, 1, 1));
            ClipPlayer player = Interactions.AddClip(_root, open);
            Interactions.AddClipTrigger(node, _root, "Open", whileHeld: true, startEnabled: true);
            foreach (UnityEventBase when in new UnityEventBase[] { button.onPressed, button.onReleased, button.onSelected })
                for (int i = 0; i < when.GetPersistentEventCount(); i++)  // its wiring called in the editor too (Unity's: in play alone)
                    when.SetPersistentListenerState(i, UnityEventCallState.EditorAndRuntime);
            var actor = Child("PILOT").AddComponent<InteractionActor>();
            actor.hand = Child("HAND").AddComponent<Hand>();
            Keeps(new Rig
            {
                actor = actor, part = latch, pressable = button, other = Child("LAMP").AddComponent<Light>(),
                selected = () => selected,
                pressed = () => button.IsPressed,
                play = () => player.Play("Open"),
            });
        }

        // The contract, read back through what acting did.
        static void Keeps(Rig rig)
        {
            IStepActor actor = rig.actor;
            Assert.That(actor.Acts(rig.other), Is.False, "a part it does not know");
            Assert.That(actor.Acts(rig.part) && actor.Acts(rig.pressable), Is.True, "the parts it knows");

            Assert.That(actor.Act(rig.part, out bool held), Does.Contain(rig.part.name), "a line for the log, naming the part");
            Assert.That(held, Is.False, "a plain part is selected, not held");
            Assert.That(rig.selected(), Is.True, "acted on: selected, not refused");
            Assert.That(actor.Release(rig.part), Does.Contain(rig.part.name));

            Assert.That(actor.Busy(false, out string note), Is.False, "nothing plays");
            Assert.That(note, Is.Null, "nothing to say");

            Assert.That(actor.Act(rig.pressable, out held), Does.Contain(rig.pressable.name));
            Assert.That(held && rig.pressed(), Is.True, "a part it presses is held down: the player lets go on its next move");
            Assert.That(actor.Release(rig.pressable), Does.Contain(rig.pressable.name));
            Assert.That(rig.pressed(), Is.False, "let go");

            rig.play();
            Assert.That(actor.Busy(false, out note), Is.True, "something plays: the walk waits");
            Assert.That(note, Is.Null, "waiting is nothing to say");
            Assert.That(actor.Busy(true, out note), Is.True, "a fast walk spends its move playing it to its end");
            Assert.That(note, Is.Not.Null.And.Not.Empty, "and says so");
            Assert.That(actor.Busy(false, out _), Is.False, "played to its end: nothing to wait for");
        }
    }
}
