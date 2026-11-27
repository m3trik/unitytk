using M3trik.Unitytk.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Interaction.Tests
{
    /// <summary>
    /// The step actor's contract (<see cref="IStepActor"/>), one fixture run against its stand-in
    /// (<see cref="PureStepActor"/>) and each provider (<see cref="InteractionActor"/>): it acts on the parts it knows and
    /// no other, a plain part acted on is selected (not held) and let go of, each with a line for the log, and with nothing
    /// playing the walk has nothing to wait for or say.
    /// </summary>
    public class StepActorContractTests
    {
        GameObject _root;

        [SetUp]
        public void SetUp() => _root = new GameObject("delivery (contract test)");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        GameObject Child(string name)
        {
            var node = new GameObject(name);
            node.transform.SetParent(_root.transform, false);
            return node;
        }

        [Test]
        public void Its_stand_in_keeps_the_contract()
        {
            var part = Child("PART").AddComponent<Light>();
            var actor = new PureStepActor { ActsOn = acted => acted == part };
            Keeps(actor, part, Child("OTHER").AddComponent<Light>());
            Assert.That(actor.Acted, Is.EqualTo(new Behaviour[] { part }));
            Assert.That(actor.Released, Is.EqualTo(new Behaviour[] { part }));
            actor.Playing = 1;
            Assert.That(actor.Busy(false, out string note) && note == null, Is.True, "something plays: the walk waits");
            Assert.That(actor.Busy(true, out note) && note != null && actor.Playing == 0, Is.True, "a fast walk plays it to its end");
        }

        [Test]
        public void An_interaction_actor_keeps_the_contract()
        {
            Target part = Interactions.MakeTarget(Child("LATCH"));
            var actor = Child("PILOT").AddComponent<InteractionActor>();
            Keeps(actor, part, Child("LAMP").AddComponent<Light>());
        }

        // The contract: acting on part, a plain part it knows, and not on other.
        static void Keeps(IStepActor actor, Behaviour part, Behaviour other)
        {
            Assert.That(actor.Acts(other), Is.False, "a part it does not know");
            Assert.That(actor.Acts(part), Is.True);
            Assert.That(actor.Act(part, out bool held), Is.Not.Empty, "a line for the log");
            Assert.That(held, Is.False, "a plain part is selected, not held");
            Assert.That(actor.Release(part), Is.Not.Empty);
            Assert.That(actor.Busy(false, out string note), Is.False, "nothing plays");
            Assert.That(note, Is.Null, "nothing to say");
        }
    }
}
