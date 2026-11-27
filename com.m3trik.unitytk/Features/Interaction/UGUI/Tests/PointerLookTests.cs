using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace M3trik.Unitytk.Interaction.UGUI.Tests
{
    /// <summary>
    /// <see cref="PointerLook"/>: a drag with its button turns the eyes about the upright and tilts them no
    /// further than its pitch; it answers every pointer on its view, behind every other hit; set up once.
    /// </summary>
    public class PointerLookTests
    {
        GameObject _eyes;
        PointerLook _look;

        [SetUp]
        public void SetUp()
        {
            _eyes = new GameObject("EYES", typeof(Camera));
            _look = _eyes.AddComponent<PointerLook>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_eyes);

        [Test]
        public void A_drag_turns_the_eyes_about_the_upright_and_tilts_them_no_further_than_its_pitch()
        {
            _look.Turn(new Vector2(100f, 0f));
            Assert.That(_eyes.transform.localEulerAngles.y, Is.EqualTo(100f * _look.degreesPerPixel).Within(1e-3f), "across: about the upright");
            _look.Turn(new Vector2(0f, -100000f));
            float pitch = _eyes.transform.localEulerAngles.x;
            Assert.That(pitch > 180f ? pitch - 360f : pitch, Is.EqualTo(_look.maxPitch).Within(1e-3f), "down: no further than its pitch");
            Assert.That(_eyes.transform.localEulerAngles.z, Is.EqualTo(0f).Within(1e-3f), "never rolled");
        }

        [Test]
        public void Only_a_drag_with_its_button_turns_the_eyes_from_the_first_pixel()
        {
            var other = new PointerEventData(null) { button = PointerEventData.InputButton.Left, delta = new Vector2(50f, 0f), useDragThreshold = true };
            _look.OnInitializePotentialDrag(other);
            _look.OnDrag(other);
            Assert.That(other.useDragThreshold, Is.True);
            Assert.That(_eyes.transform.localRotation, Is.EqualTo(Quaternion.identity), "another button's drag does nothing");
            var own = new PointerEventData(null) { button = _look.button, delta = new Vector2(50f, 0f), useDragThreshold = true };
            _look.OnInitializePotentialDrag(own);
            Assert.That(own.useDragThreshold, Is.False, "from the first pixel");
            _look.OnDrag(own);
            Assert.That(_eyes.transform.localEulerAngles.y, Is.EqualTo(50f * _look.degreesPerPixel).Within(1e-3f));
        }

        [Test]
        public void It_answers_a_pointer_on_its_view_behind_every_other_hit()
        {
            Camera camera = _eyes.GetComponent<Camera>();
            var view = new RenderTexture(64, 64, 16);  // a view of a known size, with or without a screen
            try
            {
                camera.targetTexture = view;
                var hits = new List<RaycastResult>();
                _look.Raycast(new PointerEventData(null) { position = new Vector2(32f, 32f) }, hits);
                Assert.That(hits.Count, Is.EqualTo(1));
                Assert.That(hits[0].gameObject, Is.SameAs(_eyes));
                Assert.That(hits[0].distance, Is.EqualTo(float.MaxValue), "the farthest: a target or a panel in front takes it");
                hits.Clear();
                _look.Raycast(new PointerEventData(null) { position = new Vector2(-10f, -10f) }, hits);
                Assert.That(hits, Is.Empty, "off its view: none");
            }
            finally
            {
                camera.targetTexture = null;
                Object.DestroyImmediate(view);
            }
        }

        [Test]
        public void A_camera_set_up_to_look_gets_one_as_planned_once_and_undone()
        {
            Object.DestroyImmediate(_look);
            Camera camera = _eyes.GetComponent<Camera>();
            var dry = new OpContext(dryRun: true);
            Assert.That(PointerActions.SetUpLook(camera, dry), Is.Null);
            Assert.That(dry.Changes.Select(c => c.target), Is.EqualTo(new[] { "EYES/PointerLook" }));
            PointerLook made = null;
            OpContext.RunMenu("Set up look", ctx =>
            {
                made = PointerActions.SetUpLook(camera, ctx);
                Assert.That(ctx.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            });
            Assert.That(made, Is.Not.Null);
            var again = new OpContext(dryRun: true);
            Assert.That(PointerActions.SetUpLook(camera, again), Is.SameAs(made));
            Assert.That(again.Changes, Is.Empty);
            Undo.PerformUndo();
            Assert.That(camera.GetComponent<PointerLook>() == null, "one Ctrl+Z took it away");
        }
    }
}
