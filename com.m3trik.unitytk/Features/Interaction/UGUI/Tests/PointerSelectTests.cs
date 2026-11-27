using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace M3trik.Unitytk.Interaction.UGUI.Tests
{
    /// <summary>
    /// The uGUI door: every target made is clickable (its meshes' colliders, a
    /// <see cref="PointerSelect"/>), held to the kernel's three checks; a click under it selects
    /// it for the scene's hand; a camera set up sends its clicks into the scene, an EventSystem
    /// made where the scene has none.
    /// </summary>
    public class PointerSelectTests
    {
        GameObject _door, _geo, _camera;
        Hand _hand;

        [SetUp]
        public void SetUp()
        {
            _door = new GameObject("DOOR");
            _geo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(_geo.GetComponent<Collider>());
            _geo.name = "DOOR_GEO";
            _geo.transform.SetParent(_door.transform, false);
            _hand = new GameObject("HAND").AddComponent<Hand>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_door);
            Object.DestroyImmediate(_hand.gameObject);
            if (_camera != null) Object.DestroyImmediate(_camera);
            EventSystem made = Object.FindAnyObjectByType<EventSystem>();
            if (made != null) Object.DestroyImmediate(made.gameObject);
        }

        [Test]
        public void A_target_made_is_clickable_as_planned_once_and_undone()
        {
            var dry = new OpContext(dryRun: true);
            Interactions.MakeTarget(_door, null, dry);
            Assert.That(dry.Changes.Select(c => c.target), Is.SupersetOf(new[] { "DOOR/Target", "DOOR/DOOR_GEO/MeshCollider", "DOOR/PointerSelect" }));
            OpContext.RunMenu("Make target", ctx =>
            {
                Interactions.MakeTarget(_door, null, ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That(_door.GetComponent<PointerSelect>() != null && _geo.GetComponent<MeshCollider>() != null);
            var again = new OpContext(dryRun: true);
            Interactions.MakeTarget(_door, null, again);
            Assert.That(again.Changes, Is.Empty);
            Undo.PerformUndo();
            Assert.That(_door.GetComponent<Target>() == null && _door.GetComponent<PointerSelect>() == null && _geo.GetComponent<Collider>() == null,
                        "one Ctrl+Z took it all away");
        }

        [Test]
        public void A_click_on_a_mesh_under_it_selects_it_for_the_scenes_hand()
        {
            Target target = Interactions.MakeTarget(_door);
            Hand selectedBy = null;
            int selections = 0;
            target.onSelected.AddListener(by =>
            {
                selectedBy = by;
                selections++;
            });

            var click = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.ExecuteHierarchy(_geo, click, ExecuteEvents.pointerClickHandler);
            Assert.That(selections, Is.EqualTo(1), "the click on its mesh reached the target above it");
            Assert.That(selectedBy, Is.SameAs(_hand), "for the scene's hand");

            var other = new PointerEventData(null) { button = PointerEventData.InputButton.Right };
            ExecuteEvents.ExecuteHierarchy(_geo, other, ExecuteEvents.pointerClickHandler);
            Assert.That(selections, Is.EqualTo(1), "the primary button alone selects");
        }

        [Test]
        public void The_button_held_down_presses_it_until_let_go()
        {
            Target target = Interactions.MakeTarget(_door);
            var down = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.ExecuteHierarchy(_geo, down, ExecuteEvents.pointerDownHandler);
            Assert.That(target.IsPressed, Is.True);
            ExecuteEvents.ExecuteHierarchy(_geo, down, ExecuteEvents.pointerUpHandler);
            Assert.That(target.IsPressed, Is.False);
        }

        [Test]
        public void A_camera_set_up_sends_its_clicks_into_the_scene_through_an_event_system_made_as_planned_and_undone()
        {
            Assume.That(Object.FindAnyObjectByType<EventSystem>() == null, "the test scene starts with no EventSystem");
            _camera = new GameObject("EYES", typeof(Camera));
            Camera camera = _camera.GetComponent<Camera>();
            var dry = new OpContext(dryRun: true);
            PointerActions.SetUpPointer(camera, ctx: dry);
            Assert.That(dry.Changes.Select(c => c.target), Is.EqualTo(new[] { "EYES/PhysicsRaycaster", "EYES/PointerAssist", PointerActions.EventSystemName }));
            Assert.That(Object.FindAnyObjectByType<EventSystem>(), Is.Null, "a dry run makes nothing");
            OpContext.RunMenu("Set up pointer", ctx =>
            {
                PointerActions.SetUpPointer(camera, ctx: ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            EventSystem events = Object.FindAnyObjectByType<EventSystem>();
            Assert.That(events, Is.Not.Null, "an EventSystem made");
            Assert.That(events.GetComponent<BaseInputModule>(), Is.Not.Null, "with the input module the project's input handling uses");
            Assert.That(camera.GetComponent<PhysicsRaycaster>(), Is.Not.Null);
            var again = new OpContext(dryRun: true);
            PointerActions.SetUpPointer(camera, ctx: again);
            Assert.That(again.Changes, Is.Empty, "a second run changes nothing");
            Undo.PerformUndo();
            Assert.That(Object.FindAnyObjectByType<EventSystem>() == null && camera.GetComponent<PhysicsRaycaster>() == null
                        && camera.GetComponent<PointerAssist>() == null, "one Ctrl+Z took it all away");
        }

        [Test]
        public void A_camera_only_planned_is_set_up_to_click_and_look_as_its_live_run_sets_up_the_one_made()
        {
            Assume.That(Object.FindAnyObjectByType<EventSystem>() == null, "the test scene starts with no EventSystem");
            var planned = new PlannedObject("EYES");
            var dry = new OpContext(dryRun: true);
            PointerActions.SetUpPointer(planned, ctx: dry);
            Assert.That(PointerActions.SetUpLook(planned, dry), Is.Null);
            PointerActions.SetUpPointer(planned, ctx: dry);  // planned once
            Assert.That(Object.FindAnyObjectByType<EventSystem>(), Is.Null, "a dry run makes nothing");
            _camera = new GameObject("EYES", typeof(Camera));
            Camera camera = _camera.GetComponent<Camera>();
            var live = new OpContext();
            PointerActions.SetUpPointer(camera, ctx: live);
            PointerActions.SetUpLook(camera, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            var none = new OpContext();
            PointerActions.SetUpPointer(planned, ctx: none);
            Assert.That(none.Changes, Is.Empty, "nothing in a live run");
        }

        [Test]
        public void A_pointer_just_off_a_small_target_in_play_picks_it_unless_something_hides_it_or_it_is_off()
        {
            // A screw head 2 cm across, 1 m ahead; the pointer 3 cm beside it (about 1.7 degrees).
            Object.DestroyImmediate(_geo);
            var screw = GameObject.CreatePrimitive(PrimitiveType.Cube);
            screw.name = "SCREW";
            screw.transform.SetParent(_door.transform, false);
            screw.transform.localScale = Vector3.one * 0.02f;
            screw.transform.position = new Vector3(0, 0, 1);
            Target target = _door.AddComponent<Target>();
            var among = new[] { target };
            var ray = new Ray(Vector3.zero, (new Vector3(0.03f, 0, 1)).normalized);
            Physics.SyncTransforms();

            Assert.That(PointerAssist.Pick(ray, among, PointerAssist.DefaultAngle, 100f, Physics.DefaultRaycastLayers, out _, out float d), Is.SameAs(target));
            Assert.That(d, Is.EqualTo(1f).Within(0.05f));
            Assert.That(PointerAssist.Pick(ray, among, 1f, 100f, Physics.DefaultRaycastLayers, out _, out _), Is.Null, "past the angle: no help");
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                wall.transform.position = new Vector3(0, 0, 0.5f);
                wall.transform.localScale = new Vector3(1f, 1f, 0.01f);
                Physics.SyncTransforms();
                Assert.That(PointerAssist.Pick(ray, among, PointerAssist.DefaultAngle, 100f, Physics.DefaultRaycastLayers, out _, out _), Is.Null,
                            "a wall in front hides it");
            }
            finally
            {
                Object.DestroyImmediate(wall);
            }
            target.enabled = false;
            Assert.That(PointerAssist.Pick(ray, among, PointerAssist.DefaultAngle, 100f, Physics.DefaultRaycastLayers, out _, out _), Is.Null,
                        "a target out of play takes no help");
        }

        [Test]
        public void A_click_refused_says_so_once()
        {
            Target target = Interactions.MakeTarget(_door);
            var tool = new GameObject("TOOL").AddComponent<Holdable>();
            try
            {
                target.requires = tool;  // the hand holds nothing
                int refused = 0;
                target.onRefused.AddListener(_ => refused++);
                var click = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
                ExecuteEvents.ExecuteHierarchy(_geo, click, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.ExecuteHierarchy(_geo, click, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.ExecuteHierarchy(_geo, click, ExecuteEvents.pointerClickHandler);
                Assert.That(refused, Is.EqualTo(1), "one click, one refusal: the press and the click are one gesture");
                ExecuteEvents.ExecuteHierarchy(_geo, click, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.ExecuteHierarchy(_geo, click, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.ExecuteHierarchy(_geo, click, ExecuteEvents.pointerClickHandler);
                Assert.That(refused, Is.EqualTo(2), "and the next click its own");
            }
            finally
            {
                Object.DestroyImmediate(tool.gameObject);
            }
        }

        [Test]
        public void Two_targets_of_one_name_are_made_clickable_apart_as_planned()
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
                Assert.That(twin.GetComponent<PointerSelect>() != null && _door.GetComponent<PointerSelect>() != null);
                Assert.That(dry.Changes.Count(c => c.target.EndsWith("/PointerSelect")), Is.EqualTo(2), "each planned, by its path");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void A_target_answers_the_button_it_is_given()
        {
            Target target = Interactions.MakeTarget(_door);
            _door.GetComponent<PointerSelect>().button = PointerEventData.InputButton.Middle;
            int selections = 0;
            target.onSelected.AddListener(_ => selections++);
            var left = new PointerEventData(null) { button = PointerEventData.InputButton.Left };
            ExecuteEvents.ExecuteHierarchy(_geo, left, ExecuteEvents.pointerClickHandler);
            Assert.That(selections, Is.Zero, "the left button no longer");
            var middle = new PointerEventData(null) { button = PointerEventData.InputButton.Middle };
            ExecuteEvents.ExecuteHierarchy(_geo, middle, ExecuteEvents.pointerDownHandler);
            Assert.That(target.IsPressed, Is.True);
            ExecuteEvents.ExecuteHierarchy(_geo, middle, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.ExecuteHierarchy(_geo, middle, ExecuteEvents.pointerClickHandler);
            Assert.That(selections, Is.EqualTo(1), "its own button selects and presses");
        }

        [Test]
        public void A_camera_set_up_without_an_assist_gets_none()
        {
            _camera = new GameObject("EYES", typeof(Camera));
            Camera camera = _camera.GetComponent<Camera>();
            var dry = new OpContext(dryRun: true);
            PointerActions.SetUpPointer(camera, assist: false, ctx: dry);
            Assert.That(dry.Changes.Select(c => c.target), Does.Not.Contain("EYES/PointerAssist"));
            PointerActions.SetUpPointer(camera, assist: false);
            Assert.That(camera.GetComponent<PointerAssist>() == null && camera.GetComponent<PhysicsRaycaster>() != null, Is.True);
        }
    }
}
