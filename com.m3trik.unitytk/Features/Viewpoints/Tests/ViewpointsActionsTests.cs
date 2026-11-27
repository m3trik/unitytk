using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools.Utils;

namespace M3trik.Unitytk.Viewpoints.Tests
{
    /// <summary>
    /// The Viewpoints actions, each held to the kernel's three checks: one Ctrl+Z after
    /// a run restores the scene, the dry run's plan names every change the run makes,
    /// and a second run plans nothing.
    /// </summary>
    public class ViewpointsActionsTests
    {
        GameObject _rig, _view;

        [SetUp]
        public void SetUp()
        {
            _rig = new GameObject("RIG");
            _view = new GameObject("FRONT_VIEW");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rig);
            Object.DestroyImmediate(_view);
        }

        [Test]
        public void A_traveler_and_a_travel_to_are_planned_made_once_and_undone()
        {
            var dry = new OpContext(dryRun: true);
            Assert.That(ViewpointsActions.AddTraveler(_rig, dry), Is.Null);
            Assert.That(ViewpointsActions.AddTravelTo(_view, ctx: dry), Is.Null);
            Assert.That(ViewpointsActions.AddTravelTo(_view, ctx: dry), Is.Null, "a part the dry run planned counts as made");
            Assert.That(_rig.GetComponent<Traveler>() == null && _view.GetComponent<TravelTo>() == null, "a dry run makes nothing");

            Traveler traveler = null;
            TravelTo travel = null;
            OpContext.RunMenu("Add viewpoints", ctx =>
            {
                traveler = ViewpointsActions.AddTraveler(_rig, ctx);
                travel = ViewpointsActions.AddTravelTo(_view, ctx: ctx);
                ViewpointsActions.AddTravelTo(_view, ctx: ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That(traveler != null && traveler.gameObject == _rig);
            Assert.That(travel != null && travel.gameObject == _view && !travel.enabled, "made disabled: a step enables it as it runs");

            var again = new OpContext(dryRun: true);
            Assert.That(ViewpointsActions.AddTraveler(_rig, again), Is.SameAs(traveler));
            Assert.That(ViewpointsActions.AddTravelTo(_view, ctx: again), Is.SameAs(travel));
            Assert.That(again.Changes, Is.Empty);

            Undo.PerformUndo();
            Assert.That(_rig.GetComponent<Traveler>() == null && _view.GetComponent<TravelTo>() == null, "one Ctrl+Z took both away");
        }

        [Test]
        public void A_facing_viewpoint_stands_at_the_viewpoint_turned_level_toward_the_object_made_once_and_undone()
        {
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                door.name = "DOOR";
                door.transform.position = new Vector3(3f, 2f, 0f);  // its middle: to the viewpoint's right, above
                _view.transform.SetPositionAndRotation(new Vector3(0f, 0f, 0f), Quaternion.identity);

                var dry = new OpContext(dryRun: true);
                Assert.That(ViewpointsActions.AddFacing(_view, door.transform, dry), Is.Null);
                Assert.That(ViewpointsActions.AddFacing(_view, door.transform, dry), Is.Null, "a part the dry run planned counts as made");
                Assert.That(_view.transform.childCount, Is.Zero, "a dry run makes nothing");

                GameObject facing = null;
                OpContext.RunMenu("Add facing", ctx =>
                {
                    facing = ViewpointsActions.AddFacing(_view, door.transform, ctx);
                    Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                });
                Assert.That(facing.name, Is.EqualTo("FRONT_VIEW_FACING_DOOR"));
                Assert.That(facing.transform.parent, Is.SameAs(_view.transform));
                Assert.That(facing.transform.position, Is.EqualTo(_view.transform.position).Using(Vector3EqualityComparer.Instance));
                Assert.That(Vector3.Dot(facing.transform.forward, Vector3.right), Is.EqualTo(1f).Within(1e-5f), "level, toward the door's middle");

                facing.transform.rotation = Quaternion.Euler(0f, 45f, 0f);  // turned by hand
                var again = new OpContext(dryRun: true);
                Assert.That(ViewpointsActions.AddFacing(_view, door.transform, again), Is.SameAs(facing));
                Assert.That(again.Changes, Is.Empty);
                Assert.That(facing.transform.eulerAngles.y, Is.EqualTo(45f).Within(1e-3f), "kept as it stands");

                Undo.PerformUndo();
                Assert.That(_view.transform.childCount, Is.Zero, "one Ctrl+Z took it away");
            }
            finally
            {
                Object.DestroyImmediate(door);
            }
        }

        [Test]
        public void Two_faced_objects_of_one_name_get_a_facing_viewpoint_each_as_planned()
        {
            // Both were faced from one child named for the bare name: the second took the first's place.
            var a = new GameObject("A");
            var b = new GameObject("B");
            Transform screwA = GameObject.CreatePrimitive(PrimitiveType.Cube).transform, screwB = GameObject.CreatePrimitive(PrimitiveType.Cube).transform;
            screwA.name = screwB.name = "SCREW";
            screwA.SetParent(a.transform, false);
            screwB.SetParent(b.transform, false);
            screwA.position = new Vector3(3f, 0f, 0f);
            screwB.position = new Vector3(0f, 0f, 3f);
            try
            {
                void Run(OpContext ctx)
                {
                    ViewpointsActions.AddTravelTo(_view, screwA, ctx: ctx);
                    ViewpointsActions.AddTravelTo(_view, screwB, ctx: ctx);
                }
                var dry = new OpContext(dryRun: true);
                Run(dry);
                var live = new OpContext();
                Run(live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(_view.transform.childCount, Is.EqualTo(2), "a place for each");
                TravelTo toA = _view.transform.Find(ViewpointsActions.FacingName(_view, screwA)).GetComponent<TravelTo>();
                TravelTo toB = _view.transform.Find(ViewpointsActions.FacingName(_view, screwB)).GetComponent<TravelTo>();
                Assert.That((toA.facing, toB.facing), Is.EqualTo((screwA, screwB)), "each faces its own");
                Assert.That(toA.name, Is.EqualTo("FRONT_VIEW_FACING_A|SCREW"));
                var again = new OpContext(dryRun: true);
                Run(again);
                Assert.That(again.Changes, Is.Empty, "a second run changes nothing");
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Test]
        public void No_object_is_refused_naming_what_to_pass()
        {
            Assert.That(() => ViewpointsActions.AddTravelTo((GameObject)null), Throws.ArgumentNullException);
            Assert.That(() => ViewpointsActions.AddTraveler((GameObject)null), Throws.ArgumentNullException);
            Assert.That(() => ViewpointsActions.AddTraveler((PlannedObject)null), Throws.ArgumentNullException);
            Assert.That(() => ViewpointsActions.AddFacing(_view, null), Throws.ArgumentNullException);
        }

        [Test]
        public void Two_viewpoints_and_two_rigs_of_one_name_are_each_planned_apart()
        {
            var parent = new GameObject("ROOM");
            var twin = new GameObject("FRONT_VIEW");
            twin.transform.SetParent(parent.transform, false);
            var otherRig = new GameObject("RIG");
            otherRig.transform.SetParent(parent.transform, false);
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "DOOR";
            door.transform.position = new Vector3(3f, 0f, 0f);
            try
            {
                void Run(OpContext ctx)
                {
                    ViewpointsActions.AddTraveler(_rig, ctx);
                    ViewpointsActions.AddTraveler(otherRig, ctx);
                    ViewpointsActions.AddTravelTo(_view, ctx: ctx);
                    ViewpointsActions.AddTravelTo(twin, ctx: ctx);
                    ViewpointsActions.AddFacing(_view, door.transform, ctx);
                    ViewpointsActions.AddFacing(twin, door.transform, ctx);
                }
                var dry = new OpContext(dryRun: true);
                Run(dry);
                var live = new OpContext();
                Run(live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                Assert.That(dry.Changes.Count, Is.EqualTo(6), "each one's own, by its path");
            }
            finally
            {
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(door);
            }
        }

        [Test]
        public void A_scene_without_a_main_camera_gets_eyes_as_planned_once_and_undone()
        {
            Scene scene = SceneManager.GetActiveScene();
            var off = new List<Camera>();  // the test scene's own main cameras, switched off while it runs: the scene has none
            foreach (Camera each in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (each.enabled && each.CompareTag("MainCamera"))
                {
                    each.enabled = false;
                    off.Add(each);
                }
            try
            {
                var dry = new OpContext(dryRun: true);
                Assert.That(ViewpointsActions.AddEyes(scene, ctx: dry), Is.Null);
                Assert.That(dry.Changes.Select(c => (c.kind, c.target)), Is.EqualTo(new[] { ("create", ViewpointsActions.EyesName) }));
                Camera eyes = null;
                OpContext.RunMenu("Add eyes", ctx =>
                {
                    eyes = ViewpointsActions.AddEyes(scene, ctx: ctx);
                    Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                });
                Assert.That(eyes.gameObject.scene, Is.EqualTo(scene));
                Assert.That(eyes.CompareTag("MainCamera") && eyes.GetComponent<AudioListener>() != null, Is.True, "a main camera that hears");
                var again = new OpContext(dryRun: true);
                Assert.That(ViewpointsActions.AddEyes(scene, ctx: again), Is.SameAs(eyes), "the scene's own");
                Assert.That(again.Changes, Is.Empty);
                Undo.PerformUndo();
                Assert.That(eyes == null, "one Ctrl+Z took it away");
            }
            finally
            {
                foreach (Camera each in off)
                    if (each != null) each.enabled = true;
            }
        }

        [Test]
        public void A_travelers_eye_height_is_set_as_made_and_changed_asked_once()
        {
            Traveler made = ViewpointsActions.AddTraveler(_rig, 1.4f);
            Assert.That(made.eyeHeight, Is.EqualTo(1.4f));
            var dry = new OpContext(dryRun: true);
            Assert.That(ViewpointsActions.AddTraveler(_rig, 1.7f, dry), Is.SameAs(made));
            Assert.That(made.eyeHeight, Is.EqualTo(1.4f), "a dry run changes nothing");
            var live = new OpContext();
            ViewpointsActions.AddTraveler(_rig, 1.7f, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(made.eyeHeight, Is.EqualTo(1.7f));
            var again = new OpContext(dryRun: true);
            ViewpointsActions.AddTraveler(_rig, 1.7f, again);
            Assert.That(again.Changes, Is.Empty);
        }

        [Test]
        public void The_user_is_stood_at_a_viewpoint_behind_it_as_planned_once_and_undone()
        {
            _rig.AddComponent<Camera>();
            Traveler traveler = ViewpointsActions.AddTraveler(_rig, 1.5f);
            _view.transform.SetPositionAndRotation(new Vector3(4f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));  // faces +X
            TravelTo travel = ViewpointsActions.AddTravelTo(_view);
            var dry = new OpContext(dryRun: true);
            _rig.transform.hasChanged = false;
            Assert.That(ViewpointsActions.GoTo(traveler, travel, 1f, dry), Is.True);
            Assert.That(_rig.transform.hasChanged, Is.False, "a dry run moves nothing, not even to put it back");
            OpContext.RunMenu("Start the user", ctx =>
            {
                ViewpointsActions.GoTo(traveler, travel, 1f, ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That(_rig.transform.position, Is.EqualTo(new Vector3(3f, 1.5f, 0f)).Using(Vector3EqualityComparer.Instance), "a metre behind it, its eyes at their height");
            Assert.That(ViewpointsActions.GoTo(traveler, travel, 1f, new OpContext(dryRun: true)), Is.False, "there already");
            Undo.PerformUndo();
            Assert.That(_rig.transform.position, Is.EqualTo(Vector3.zero).Using(Vector3EqualityComparer.Instance), "one Ctrl+Z put it back");
        }

        [Test]
        public void A_rig_only_planned_gets_its_traveler_and_its_start_as_its_live_run_makes_them()
        {
            _view.transform.SetPositionAndRotation(new Vector3(4f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));
            TravelTo travel = ViewpointsActions.AddTravelTo(_view);
            var planned = new PlannedObject("EYES");
            var dry = new OpContext(dryRun: true);
            Assert.That(ViewpointsActions.AddTraveler(planned, dry), Is.Null);
            Assert.That(ViewpointsActions.GoTo(planned, travel, 1f, dry), Is.True);
            Assert.That(ViewpointsActions.GoTo(planned, travel, 1f, dry), Is.False, "planned once");
            var eyes = new GameObject("EYES", typeof(Camera));
            try
            {
                var live = new OpContext();
                Traveler traveler = ViewpointsActions.AddTraveler(eyes, 1.5f, live);
                ViewpointsActions.GoTo(traveler, travel, 1f, live);
                Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                var both = new OpContext(dryRun: true);
                Assert.That(ViewpointsActions.GoTo(planned, new PlannedObject("SPOT", new PlannedObject("VIEWS")), 0f, both), Is.True);
                Assert.That(both.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(new[] { ("change", "EYES", "stand the user at 'SPOT'") }));
                Assert.That(ViewpointsActions.GoTo(planned, travel, 1f, new OpContext()), Is.False, "nothing in a live run");
            }
            finally
            {
                Object.DestroyImmediate(eyes);
            }
        }

        [Test]
        public void A_viewpoint_under_its_holder_and_what_is_made_on_it_are_planned_as_its_live_run_makes_them()
        {
            var door = GameObject.CreatePrimitive(PrimitiveType.Cube);
            door.name = "DOOR";
            door.transform.position = new Vector3(3f, 0f, 2f);
            const string holder = "Viewpoints (test)";
            try
            {
                void Run(OpContext ctx)
                {
                    GameObject made = ViewpointsActions.AddViewpoint(holder, "VIEW_1", ctx: ctx);
                    if (made != null) ViewpointsActions.AddTravelTo(made, door.transform, 1f, ctx: ctx);
                    else ViewpointsActions.AddTravelTo(ViewpointsActions.PlannedViewpoint(holder, "VIEW_1"), door.transform, ctx);
                }
                var dry = new OpContext(dryRun: true);
                Run(dry);
                Assert.That(SceneRoots.Find(holder), Is.Null, "a dry run makes nothing");
                OpContext.RunMenu("Add a viewpoint", ctx =>
                {
                    Run(ctx);
                    Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
                });
                GameObject root = SceneRoots.Find(holder);
                Transform view = root.transform.Find("VIEW_1");
                Assert.That(view.Find("VIEW_1_FACING_DOOR").GetComponent<TravelTo>().standOff, Is.EqualTo(1f));
                Assert.That(ViewpointsActions.FacingName(ViewpointsActions.PlannedViewpoint(holder, "VIEW_1"), door.transform), Is.EqualTo("VIEW_1_FACING_DOOR"));
                var again = new OpContext(dryRun: true);
                Run(again);
                Assert.That(again.Changes, Is.Empty, "a second run changes nothing");
                Undo.PerformUndo();
                Assert.That(SceneRoots.Find(holder), Is.Null, "one Ctrl+Z took it all away");
            }
            finally
            {
                Object.DestroyImmediate(door);
                GameObject left = SceneRoots.Find(holder);
                if (left != null) Object.DestroyImmediate(left);
            }
        }

        [Test]
        public void A_travel_starts_switched_on_where_asked()
        {
            Assert.That(ViewpointsActions.AddTravelTo(_view, startEnabled: true).enabled, Is.True);
        }
    }
}
