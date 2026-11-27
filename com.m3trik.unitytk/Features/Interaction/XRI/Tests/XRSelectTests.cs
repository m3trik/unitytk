using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace M3trik.Unitytk.Interaction.XRI.Tests
{
    /// <summary>
    /// The XRI door: every target made is selectable by a headset's controllers (its meshes'
    /// colliders, an interactable wired to an <see cref="XRSelect"/>), held to the kernel's
    /// three checks; a controller's select selects it for the hand on that controller.
    /// </summary>
    public class XRSelectTests
    {
        GameObject _door, _geo, _left, _right;

        [SetUp]
        public void SetUp()
        {
            _door = new GameObject("DOOR");
            _geo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(_geo.GetComponent<Collider>());
            _geo.name = "DOOR_GEO";
            _geo.transform.SetParent(_door.transform, false);
            _left = new GameObject("LEFT", typeof(Hand));
            _right = new GameObject("RIGHT", typeof(Hand));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_door);
            Object.DestroyImmediate(_left);
            Object.DestroyImmediate(_right);
        }

        [Test]
        public void A_target_inside_another_keeps_its_colliders_to_itself()
        {
            _geo.AddComponent<BoxCollider>();
            var lockNode = GameObject.CreatePrimitive(PrimitiveType.Cube);  // its own collider
            lockNode.name = "LOCK";
            lockNode.transform.SetParent(_door.transform, false);
            var door = _door.AddComponent<XRSelect>();
            var inner = lockNode.AddComponent<XRSelect>();
            door.ClaimColliders();
            inner.ClaimColliders();
            Assert.That(_door.GetComponent<XRSimpleInteractable>().colliders, Is.EqualTo(new[] { _geo.GetComponent<Collider>() }),
                        "the door's own mesh, not the lock's");
            Assert.That(lockNode.GetComponent<XRSimpleInteractable>().colliders, Is.EqualTo(new[] { lockNode.GetComponent<Collider>() }));
        }

        [Test]
        public void A_target_made_is_selectable_by_a_controller_as_planned_once_and_undone()
        {
            var dry = new OpContext(dryRun: true);
            Interactions.MakeTarget(_door, null, dry);
            Assert.That(dry.Changes.Select(c => c.target), Is.SupersetOf(new[] { "DOOR/XRSimpleInteractable", "DOOR/XRSelect", "DOOR" }));
            OpContext.RunMenu("Make target", ctx =>
            {
                Interactions.MakeTarget(_door, null, ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            var interactable = _door.GetComponent<XRSimpleInteractable>();
            Assert.That(interactable.selectEntered.GetPersistentTarget(0), Is.SameAs(_door.GetComponent<XRSelect>()));
            Assert.That(interactable.selectExited.GetPersistentMethodName(0), Is.EqualTo(nameof(XRSelect.OnSelectExited)), "its end lets go");
            var again = new OpContext(dryRun: true);
            Interactions.MakeTarget(_door, null, again);
            Assert.That(again.Changes, Is.Empty);
            Undo.PerformUndo();
            Assert.That(_door.GetComponent<Target>() == null && _door.GetComponent<XRSimpleInteractable>() == null,
                        "one Ctrl+Z took it all away");
        }

        [Test]
        public void A_controllers_select_selects_it_for_the_hand_on_that_controller()
        {
            Target target = Interactions.MakeTarget(_door);
            var interactable = _door.GetComponent<XRSimpleInteractable>();
            for (int i = 0; i < interactable.selectEntered.GetPersistentEventCount(); i++)
                interactable.selectEntered.SetPersistentListenerState(i, UnityEventCallState.EditorAndRuntime);
            Hand selectedBy = null;
            target.onSelected.AddListener(by => selectedBy = by);

            var controller = new GameObject("RIGHT_CONTROLLER");
            controller.transform.SetParent(_right.transform, false);
            var interactor = controller.AddComponent<XRDirectInteractor>();
            interactable.selectEntered.Invoke(new SelectEnterEventArgs { interactorObject = interactor, interactableObject = interactable });
            Assert.That(selectedBy, Is.SameAs(_right.GetComponent<Hand>()), "the hand on the controller that selected");
            Assert.That(target.IsPressed, Is.True, "held while the controller selects");
            for (int i = 0; i < interactable.selectExited.GetPersistentEventCount(); i++)
                interactable.selectExited.SetPersistentListenerState(i, UnityEventCallState.EditorAndRuntime);
            interactable.selectExited.Invoke(new SelectExitEventArgs { interactorObject = interactor, interactableObject = interactable });
            Assert.That(target.IsPressed, Is.False);
        }

        [Test]
        public void A_controllers_select_refused_says_so_once()
        {
            Target target = Interactions.MakeTarget(_door);
            var tool = new GameObject("TOOL").AddComponent<Holdable>();
            try
            {
                target.requires = tool;  // the hand holds nothing
                var interactable = _door.GetComponent<XRSimpleInteractable>();
                for (int i = 0; i < interactable.selectEntered.GetPersistentEventCount(); i++)
                    interactable.selectEntered.SetPersistentListenerState(i, UnityEventCallState.EditorAndRuntime);
                int refused = 0;
                target.onRefused.AddListener(_ => refused++);
                var controller = new GameObject("RIGHT_CONTROLLER");
                controller.transform.SetParent(_right.transform, false);
                var interactor = controller.AddComponent<XRDirectInteractor>();
                interactable.selectEntered.Invoke(new SelectEnterEventArgs { interactorObject = interactor, interactableObject = interactable });
                Assert.That(refused, Is.EqualTo(1), "one select, one refusal");
                Assert.That(target.IsPressed, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(tool.gameObject);
            }
        }

        [Test]
        public void Two_targets_of_one_name_are_made_selectable_apart_as_planned()
        {
            var parent = new GameObject("CABINET");
            var twin = new GameObject("DOOR");
            twin.transform.SetParent(parent.transform, false);
            try
            {
                var dry = new OpContext(dryRun: true);
                Interactions.MakeTarget(_door, null, dry);
                Interactions.MakeTarget(twin, null, dry);
                var live = new OpContext();
                Interactions.MakeTarget(_door, null, live);
                Interactions.MakeTarget(twin, null, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(dry.Changes.Count(c => c.target.EndsWith("/XRSelect")), Is.EqualTo(2), "each planned, by its path");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
