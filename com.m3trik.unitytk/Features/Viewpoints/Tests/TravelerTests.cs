using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace M3trik.Unitytk.Viewpoints.Tests
{
    /// <summary>
    /// <see cref="Traveler"/> and <see cref="TravelTo"/>: the user is brought to a viewpoint,
    /// standing over it and facing its way, whether a rig carries the eyes (a headset's origin,
    /// a desktop rig) or a camera is its own eyes.
    /// </summary>
    public class TravelerTests
    {
        GameObject _rig, _view;

        [SetUp]
        public void SetUp()
        {
            _rig = new GameObject("RIG");
            _view = new GameObject("VIEW");
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_rig);
            Object.DestroyImmediate(_view);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z).normalized;

        [Test]
        public void A_stand_off_brings_the_user_toward_what_they_face_never_away()
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);  // one metre: its near side at x 6.5
            part.transform.position = new Vector3(7f, 1f, 0f);
            _view.transform.position = new Vector3(2f, 0f, 0f);
            var travel = _view.AddComponent<TravelTo>();
            travel.facing = part.transform;
            try
            {
                Assert.That(travel.Floor.x, Is.EqualTo(2f).Within(1e-4f), "no stand-off: where it was placed");
                travel.standOff = 1f;
                Assert.That(travel.Floor, Is.EqualTo(new Vector3(5.5f, 0f, 0f)).Using(Vector3EqualityComparer.Instance), "a metre from its near side");
                Assert.That(travel.Heading * Vector3.forward, Is.EqualTo(Vector3.right).Using(Vector3EqualityComparer.Instance));
                _view.transform.position = new Vector3(6f, 0f, 0.5f);  // half a metre off already
                Assert.That(travel.Floor, Is.EqualTo(new Vector3(6f, 0f, 0.5f)).Using(Vector3EqualityComparer.Instance), "never moved back");
            }
            finally
            {
                Object.DestroyImmediate(part);
            }
        }

        [Test]
        public void A_viewpoint_stands_the_user_on_its_disc_facing_what_it_faces_where_that_now_stands()
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);  // its mesh two units tall
            disc.transform.SetPositionAndRotation(new Vector3(2f, -0.3f, 0f), Quaternion.identity);
            disc.transform.localScale = new Vector3(1f, 0.05f, 1f);  // its top at -0.25: its pivot below the floor
            disc.GetComponent<MeshRenderer>().enabled = false;  // hidden, as a marker off leaves it
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.transform.position = new Vector3(2f, 1f, 5f);
            var place = new GameObject("VIEW_FACING_PART");
            place.transform.SetParent(disc.transform, false);
            var travel = place.AddComponent<TravelTo>();
            travel.facing = part.transform;
            try
            {
                Assert.That(travel.Floor.y, Is.EqualTo(-0.25f).Within(1e-4f), "on its viewpoint's disc, shown or not");
                Assert.That(travel.Heading * Vector3.forward, Is.EqualTo(Vector3.forward).Using(Vector3EqualityComparer.Instance));
                part.transform.position = new Vector3(7f, 1f, 0f);  // moved since the build: a door swung open
                Assert.That(travel.Heading * Vector3.forward, Is.EqualTo(Vector3.right).Using(Vector3EqualityComparer.Instance),
                            "toward where it stands as the user arrives");
            }
            finally
            {
                Object.DestroyImmediate(disc);
                Object.DestroyImmediate(part);
            }
        }

        [Test]
        public void A_rig_is_turned_and_moved_so_its_eyes_stand_over_the_viewpoint_facing_its_way()
        {
            var eyes = new GameObject("EYES").AddComponent<Camera>();
            eyes.transform.SetParent(_rig.transform, false);
            eyes.transform.localPosition = new Vector3(0.3f, 1.7f, 0.2f);  // a tracked head, off the rig's centre
            eyes.transform.localRotation = Quaternion.Euler(10f, 25f, 0f);  // looking a little down and aside
            _rig.transform.SetPositionAndRotation(new Vector3(-1f, 0f, 3f), Quaternion.Euler(0f, 30f, 0f));
            var traveler = _rig.AddComponent<Traveler>();
            Assert.That(traveler.Head, Is.SameAs(eyes.transform), "its camera is its eyes when none is named");

            _view.transform.SetPositionAndRotation(new Vector3(5f, 0.5f, -2f), Quaternion.Euler(0f, 90f, 0f));
            traveler.GoTo(_view.transform.position, _view.transform.rotation);

            Vector3 at = eyes.transform.position;
            Assert.That(new Vector2(at.x, at.z), Is.EqualTo(new Vector2(5f, -2f)).Using(Vector2EqualityComparer.Instance), "its eyes stand over the viewpoint");
            Assert.That(_rig.transform.position.y, Is.EqualTo(0.5f).Within(1e-5f), "its floor is the viewpoint's");
            Assert.That(at.y, Is.EqualTo(2.2f).Within(1e-5f), "its eyes keep their height over the floor");
            Assert.That(Vector3.Dot(Flat(eyes.transform.forward), Vector3.right), Is.EqualTo(1f).Within(1e-5f), "they face the viewpoint's way");
            Assert.That(Vector3.Dot(_rig.transform.up, Vector3.up), Is.EqualTo(1f).Within(1e-5f), "the rig stays upright");
        }

        [Test]
        public void Where_a_rig_goes_is_solved_moving_nothing_and_its_move_goes_there()
        {
            var eyes = new GameObject("EYES").AddComponent<Camera>();
            eyes.transform.SetParent(_rig.transform, false);
            eyes.transform.localPosition = new Vector3(0.3f, 1.7f, 0.2f);
            eyes.transform.localRotation = Quaternion.Euler(10f, 25f, 0f);
            _rig.transform.SetPositionAndRotation(new Vector3(-1f, 0f, 3f), Quaternion.Euler(0f, 30f, 0f));
            var traveler = _rig.AddComponent<Traveler>();
            _rig.transform.hasChanged = false;
            traveler.Solve(new Vector3(5f, 0.5f, -2f), Quaternion.Euler(0f, 90f, 0f), out Vector3 position, out Quaternion rotation);
            Assert.That(_rig.transform.hasChanged, Is.False, "solved: nothing moved");
            traveler.GoTo(new Vector3(5f, 0.5f, -2f), Quaternion.Euler(0f, 90f, 0f));
            Assert.That(_rig.transform.position, Is.EqualTo(position).Using(Vector3EqualityComparer.Instance));
            Assert.That(Quaternion.Angle(_rig.transform.rotation, rotation), Is.LessThan(1e-3f), "the move goes where it was solved to");
        }

        [Test]
        public void A_camera_that_is_its_own_eyes_stands_at_eye_height_looking_level()
        {
            _rig.AddComponent<Camera>();
            var traveler = _rig.AddComponent<Traveler>();
            traveler.eyeHeight = 1.5f;
            _view.transform.SetPositionAndRotation(new Vector3(2f, 1f, 0f), Quaternion.Euler(30f, 180f, 0f));

            traveler.GoTo(_view.transform.position, _view.transform.rotation);

            Assert.That(_rig.transform.position, Is.EqualTo(new Vector3(2f, 2.5f, 0f)).Using(Vector3EqualityComparer.Instance));
            Assert.That(Vector3.Dot(_rig.transform.forward, Vector3.back), Is.EqualTo(1f).Within(1e-5f), "level, facing the viewpoint's way");
        }

        [Test]
        public void A_travel_brings_the_scenes_traveler_and_says_when_there_is_none()
        {
            var travel = _view.AddComponent<TravelTo>();
            travel.enabled = false;
            _view.transform.position = new Vector3(0f, 0f, 4f);

            LogAssert.Expect(LogType.Warning, "VIEW: no Traveler in the scene to bring here; add one to the user's rig (the camera on a desktop, the origin in a headset).");
            Assert.That(travel.TryTravel(), Is.False);

            _rig.AddComponent<Camera>();
            var traveler = _rig.AddComponent<Traveler>();
            Assert.That(travel.Traveler, Is.SameAs(traveler), "the scene's traveler when none is named");
            Assert.That(travel.TryTravel(), Is.True);
            Assert.That(_rig.transform.position, Is.EqualTo(new Vector3(0f, traveler.eyeHeight, 4f)).Using(Vector3EqualityComparer.Instance));

            traveler.enabled = false;
            LogAssert.Expect(LogType.Warning, "VIEW: no Traveler in the scene to bring here; add one to the user's rig (the camera on a desktop, the origin in a headset).");
            Assert.That(travel.TryTravel(), Is.False, "a switched-off traveler is not the scene's");
        }

        [Test]
        public void The_floor_under_a_viewpoint_is_its_rules_a_floor_below_a_roof_a_pivot_or_a_height()
        {
            // A walkthrough under a building: the viewpoint at ground level, a child of the building's roof slab.
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.position = new Vector3(0f, 4f, 0f);
            roof.transform.localScale = new Vector3(10f, 0.2f, 10f);
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.transform.position = new Vector3(0f, -0.1f, 0f);
            ground.transform.localScale = new Vector3(10f, 0.2f, 10f);  // its top at 0
            var place = new GameObject("VIEW_UNDER");
            place.transform.SetParent(roof.transform, false);
            place.transform.position = new Vector3(1f, 0.3f, 1f);
            var travel = place.AddComponent<TravelTo>();
            Physics.SyncTransforms();
            try
            {
                Assert.That(travel.Floor.y, Is.EqualTo(4.1f).Within(1e-4f), "by default its parent's mesh top: the roof");
                travel.floorRule = TravelFloor.Below;
                Assert.That(travel.Floor.y, Is.EqualTo(0f).Within(1e-4f), "the first floor below it");
                Assert.That(new Vector2(travel.Floor.x, travel.Floor.z), Is.EqualTo(new Vector2(1f, 1f)).Using(Vector2EqualityComparer.Instance), "right under it");
                travel.floorRule = TravelFloor.Pivot;
                Assert.That(travel.Floor.y, Is.EqualTo(0.3f).Within(1e-4f));
                travel.floorRule = TravelFloor.Height;
                travel.floorHeight = -2f;
                Assert.That(travel.Floor.y, Is.EqualTo(-2f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(roof);
                Object.DestroyImmediate(ground);
            }
        }

        [Test]
        public void A_rigs_feet_stand_on_the_viewpoint_whatever_its_origin()
        {
            var eyes = new GameObject("EYES").AddComponent<Camera>();
            eyes.transform.SetParent(_rig.transform, false);
            eyes.transform.localPosition = new Vector3(0f, 1.7f, 0f);
            var feet = new GameObject("FEET").transform;
            feet.SetParent(_rig.transform, false);
            feet.localPosition = new Vector3(0f, 0.4f, 0f);  // the rig's origin is under its floor
            var traveler = _rig.AddComponent<Traveler>();
            traveler.feet = feet;
            traveler.GoTo(new Vector3(2f, 1f, 0f), Quaternion.identity);
            Assert.That(feet.position.y, Is.EqualTo(1f).Within(1e-5f), "its feet on the floor");
            Assert.That(eyes.transform.position.y, Is.EqualTo(2.3f).Within(1e-5f), "its eyes their height over its feet");
        }

        [Test]
        public void A_travel_a_UnityEvent_calls_brings_the_user()
        {
            _rig.AddComponent<Camera>();
            var traveler = _rig.AddComponent<Traveler>();
            var travel = _view.AddComponent<TravelTo>();
            travel.enabled = false;
            _view.transform.position = new Vector3(0f, 0f, 3f);
            var call = new UnityEngine.Events.UnityEvent();
            UnityEditor.Events.UnityEventTools.AddVoidPersistentListener(call, travel.Travel);
            call.SetPersistentListenerState(0, UnityEngine.Events.UnityEventCallState.EditorAndRuntime);
            call.Invoke();
            Assert.That(_rig.transform.position, Is.EqualTo(new Vector3(0f, traveler.eyeHeight, 3f)).Using(Vector3EqualityComparer.Instance));
        }
    }
}
