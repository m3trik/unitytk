using System;
using System.Reflection;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UIElements;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Panels.Tests
{
    /// <summary>
    /// <see cref="PipView"/>: riding the eyes in a corner it allocates nothing a frame, its camera leaves its own
    /// window layer out, its picture's texture goes with it, a field of view out of range frames the part as the
    /// nearest in range does, its camera draws only while its picture is seen, and what it leaves out is hidden while
    /// its own camera draws.
    /// </summary>
    public class PipViewTests
    {
        GameObject _eyes, _part;
        PipView _pip;

        [SetUp]
        public void SetUp()
        {
            _eyes = new GameObject("EYES");
            _eyes.transform.position = new Vector3(0, 1.6f, 0);
            _part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _part.transform.position = new Vector3(0, 1.2f, 2f);
            _pip = PanelsActions.MakePip("PIP (view test)", PipStyle.Corner);
            _pip.eyes = _eyes.transform;
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_pip.gameObject);
            Object.DestroyImmediate(_part);
            Object.DestroyImmediate(_eyes);
        }

        // What Unity calls by name (Edit Mode calls none of them), as a delegate: calling it allocates nothing itself.
        Action Message(string name)
            => (Action)typeof(PipView).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).CreateDelegate(typeof(Action), _pip);

        [Test]
        public void Riding_the_eyes_in_a_corner_it_allocates_nothing_a_frame()
        {
            _pip.Show(_part.transform, "The part");
            Action frame = Message("LateUpdate");
            frame();  // its tree and camera made, once
            _eyes.transform.Rotate(0f, 10f, 0f);
            Assert.That(() =>
            {
                for (int i = 0; i < 10; i++)
                {
                    _eyes.transform.Rotate(0f, 1f, 0f);
                    frame();
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(Vector3.Distance(_pip.transform.position, _eyes.transform.position + _eyes.transform.rotation * _pip.cornerOffset),
                        NUnit.Framework.Is.LessThan(1e-4f), "it rode the eyes");
        }

        [Test]
        public void Its_camera_leaves_its_window_layer_out_and_its_texture_goes_with_it()
        {
            _pip.windowLayer = "Water";  // a project's own layer for its windows
            _pip.Show(_part.transform, "The part");
            Camera camera = _pip.PictureCamera;
            Assert.That(camera.cullingMask & (1 << LayerMask.NameToLayer("Water")), NUnit.Framework.Is.Zero);
            Assert.That(camera.cullingMask & (1 << LayerMask.NameToLayer("UI")), NUnit.Framework.Is.Not.Zero, "the UI layer is the world's now");
            RenderTexture texture = camera.targetTexture;
            Assert.That(texture != null);
            Message("OnDestroy")();
            Assert.That(texture == null, "destroyed, not only released");
        }

        [Test]
        public void Its_picture_is_the_size_its_fields_say_its_caption_wrapping_under_it()
        {
            // The card's size was the style sheet's, the fields only a guess at it before the card laid out.
            _pip.pictureHeight = 0.2f;
            _pip.resolution = new Vector2Int(800, 400);
            _pip.Show(_part.transform, "A caption long enough to be wider than the picture it sits under, by far");
            VisualElement root = _pip.GetComponent<UIDocument>().rootVisualElement;
            var picture = root.Q(className: PipView.ImageClass);
            float ppm = _pip.pixelsPerMetre;
            Assert.That(_pip.PictureWidth, NUnit.Framework.Is.EqualTo(0.4f).Within(1e-5f), "its height in the resolution's shape");
            Assert.That(picture.style.width.value.value, NUnit.Framework.Is.EqualTo(0.4f * ppm).Within(1e-3f), "in panel pixels");
            Assert.That(picture.style.height.value.value, NUnit.Framework.Is.EqualTo(0.2f * ppm).Within(1e-3f));
            Assert.That(root.Q<Label>(className: PipView.CaptionClass).style.maxWidth.value.value,
                        NUnit.Framework.Is.EqualTo(0.4f * ppm).Within(1e-3f), "the caption no wider than the picture");
        }

        [Test]
        public void A_field_of_view_out_of_range_frames_as_the_nearest_in_range()
        {
            // At 0 a script's value put the camera at NaN: infinitely far to fill the picture.
            _pip.fieldOfView = 0f;
            _pip.Show(_part.transform, "The part");
            Camera camera = _pip.PictureCamera;
            Assert.That(float.IsNaN(camera.transform.position.x), NUnit.Framework.Is.False);
            Assert.That(camera.fieldOfView, NUnit.Framework.Is.EqualTo(1f));
            _pip.fieldOfView = 200f;
            _pip.Frame();
            Assert.That(camera.fieldOfView, NUnit.Framework.Is.EqualTo(179f));
        }

        // Folded away its picture is seen by no one, and its camera still drew a 640 x 480 view every frame.
        [Test]
        public void Its_camera_draws_only_while_its_picture_is_seen()
        {
            _pip.Show(_part.transform, "The part");  // in Edit Mode, shown at once at full opacity
            Camera camera = _pip.PictureCamera;
            Assert.That(camera.enabled, NUnit.Framework.Is.True, "shown: it draws");
            _pip.Collapse(true);
            Assert.That(camera.enabled, NUnit.Framework.Is.False, "folded away: it draws nothing");
            _pip.Collapse(false);
            Assert.That(camera.enabled, NUnit.Framework.Is.True, "unfolded: it draws again");
            _pip.Clear();
            Assert.That(camera.enabled, NUnit.Framework.Is.False, "nothing shown");
        }

        // Its per-camera hook (what its own camera's drawing leaves out) had no test: Hide and Unhide only, called by hand.
        [Test]
        public void What_it_leaves_out_is_hidden_while_its_own_camera_draws_and_only_then()
        {
            var arrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var shown = arrow.GetComponent<Renderer>();
            _pip.hidden.Add(arrow);
            var seen = new System.Collections.Generic.List<bool>();
            void Probe(Camera drawing)
            {
                if (drawing == _pip.PictureCamera) seen.Add(shown.forceRenderingOff);
            }
            Action enable = Message("OnEnable"), disable = Message("OnDisable");
            try
            {
                _pip.Show(_part.transform, "The part");
                enable();  // what Unity sends it as it is switched on (Edit Mode sends none)
                CameraDrawing.Starting += Probe;  // after its own: what its camera's drawing finds
                _pip.PictureCamera.Render();
                Assert.That(seen, NUnit.Framework.Is.EqualTo(new[] { true }), "left out while its camera draws");
                Assert.That(shown.forceRenderingOff, NUnit.Framework.Is.False, "drawn again once it has drawn");
            }
            finally
            {
                CameraDrawing.Starting -= Probe;
                disable();
                Object.DestroyImmediate(arrow);
            }
        }
    }
}
