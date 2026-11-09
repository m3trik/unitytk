using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The pointer grabber reads whatever pointer is registered (the seam the
    /// Input System door fills): a press over a grab part takes it, a release lets
    /// go; with none registered it reads the legacy mouse. Each frame is the
    /// component's own <c>Update</c>, run by hand, as the player loop would.
    /// </summary>
    public class ArticulatedRigPointerGrabTests
    {
        /// <summary>A pointer a test moves and presses.</summary>
        sealed class ScriptedPointer : IPointerInput
        {
            public bool down;
            public Vector3 at;

            public bool Available(int button) => true;
            public bool Pressed(int button) => down;
            public bool Held(int button) => down;
            public Vector3 Position => at;
            public float Scroll => 0f;
        }

        static readonly MethodInfo Update = typeof(ArticulatedRigPointerGrab).GetMethod(
            "Update", BindingFlags.Instance | BindingFlags.NonPublic);

        GameObject _root;
        GameObject _view;
        RenderTexture _screen;

        [TearDown]
        public void TearDown()
        {
            ArticulatedRigPointerGrab.UsePointer(null);
            foreach (Object made in new Object[] { _root, _view, _screen })
                if (made != null) Object.DestroyImmediate(made);
        }

        [Test]
        public void The_registered_pointer_is_read_and_none_reads_the_legacy_mouse()
        {
            var pointer = new ScriptedPointer();
            ArticulatedRigPointerGrab.UsePointer(pointer);
            Assert.That(ArticulatedRigPointerGrab.Pointer, Is.SameAs(pointer));
            ArticulatedRigPointerGrab.UsePointer(null);
            Assert.That(ArticulatedRigPointerGrab.Pointer, Is.Not.Null.And.Not.SameAs(pointer));
        }

        [Test]
        public void PlanePoint_is_where_the_ray_crosses_the_camera_facing_plane_at_the_depth()
        {
            var view = Vector3.zero;
            Vector3 forward = Vector3.forward;
            var slanted = new Ray(new Vector3(0f, 0f, 0.5f), new Vector3(1f, 0f, 1f).normalized);
            Vector3 at = ArticulatedRigPointerGrab.PlanePoint(slanted, view, forward, 2f);
            Assert.That(Vector3.Distance(at, new Vector3(1.5f, 0f, 2f)), Is.LessThan(1e-5f), "1.5 along x by z = 2");
            var parallel = new Ray(new Vector3(0f, 1f, 0f), Vector3.right);
            Assert.That(ArticulatedRigPointerGrab.PlanePoint(parallel, view, forward, 3f), Is.EqualTo(new Vector3(3f, 1f, 0f)),
                        "parallel to the plane: the ray's point at the depth");
        }

        [Test]
        public void A_drag_moves_the_held_point_in_the_camera_plane_and_a_scroll_pushes_and_pulls_its_depth()
        {
            _root = ArmRig.Build(out Transform part);
            ArticulatedRig rig = ArmRig.Rig(_root, part);
            rig.joints[0].rz = 20f;
            rig.joints[1].rz = 40f;
            rig.ApplyNow();
            rig.EnsureGrabColliders();
            Physics.SyncTransforms();
            _view = new GameObject("View");
            _view.transform.position = part.position + new Vector3(0f, 0f, -1f);
            var grabber = _view.AddComponent<ArticulatedRigPointerGrab>();
            grabber.scrollStep = 0.1f;
            grabber.minDepth = 0.95f;

            Assert.That(grabber.TryBegin(new Ray(_view.transform.position, Vector3.forward), _view.transform), Is.True);
            Assert.That(grabber.Depth, Is.EqualTo(1f).Within(0.02f), "the hit's depth in front of the camera");
            float depth = grabber.Depth;
            // A ray a little up and right: the target is its point on the plane at that depth.
            var ray = new Ray(_view.transform.position, new Vector3(0.02f, 0.03f, 1f).normalized);
            Vector3 target = ArticulatedRigPointerGrab.PlanePoint(ray, _view.transform.position, Vector3.forward, depth);
            grabber.Drag(ray, _view.transform, 0f);
            Assert.That(Vector3.Distance(rig.GrabPointWorld, target), Is.LessThan(1e-3f), "the held point follows the drag");

            grabber.Drag(ray, _view.transform, 1f);
            Assert.That(grabber.Depth, Is.EqualTo(depth * 1.1f).Within(1e-5f), "a scroll away pushes it a step out");
            grabber.Drag(ray, _view.transform, -1f);
            grabber.Drag(ray, _view.transform, -1f);
            Assert.That(grabber.Depth, Is.EqualTo(Mathf.Max(0.95f, depth * 1.1f * 0.9f * 0.9f)).Within(1e-5f),
                        "toward the camera, no nearer than its least depth");
            grabber.End();
            Assert.That(grabber.Held, Is.Null);
            Assert.That(rig.IsGrabbing, Is.False);
        }

        [Test]
        public void A_press_over_a_grab_part_takes_it_and_a_release_lets_go()
        {
            _root = ArmRig.Build(out Transform part);
            ArticulatedRig rig = ArmRig.Rig(_root, part);
            Assert.That(rig.EnsureGrabColliders(), Is.EqualTo(1));

            // A camera a metre in front of the part, rendering to a fixed-size target
            // so its screen is the same with or without a display.
            _view = new GameObject("View");
            _view.transform.position = part.position + new Vector3(0f, 0f, -1f);
            _screen = new RenderTexture(256, 256, 0);
            var cam = _view.AddComponent<Camera>();
            cam.targetTexture = _screen;
            Physics.SyncTransforms();

            var grabber = _view.AddComponent<ArticulatedRigPointerGrab>();
            grabber.viewCamera = cam;
            var pointer = new ScriptedPointer { at = cam.WorldToScreenPoint(part.position) };
            ArticulatedRigPointerGrab.UsePointer(pointer);

            Update.Invoke(grabber, null);
            Assert.That(grabber.Held, Is.Null, "no press, no grab");

            pointer.down = true;
            Update.Invoke(grabber, null);
            Assert.That(grabber.Held, Is.SameAs(rig));
            Assert.That(rig.GrabbedPart, Is.SameAs(part));

            pointer.down = false;
            Update.Invoke(grabber, null);
            Assert.That(grabber.Held, Is.Null);
            Assert.That(rig.IsGrabbing, Is.False);
        }
    }
}
