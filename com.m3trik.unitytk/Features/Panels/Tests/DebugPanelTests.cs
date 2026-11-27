using System.Collections.Generic;
using M3trik.Unitytk.Contracts;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Panels.Tests
{
    /// <summary>
    /// <see cref="DebugPanel"/>: commands put on it by their owners, run by label, taken away with
    /// their owner; a status line; a tab folding it away; riding beside the eyes; the command surface read off it.
    /// </summary>
    public class DebugPanelTests
    {
        GameObject _node;
        DebugPanel _panel;

        [SetUp]
        public void SetUp()
        {
            _node = new GameObject("Debug");
            _node.AddComponent<UIDocument>();
            _panel = _node.AddComponent<DebugPanel>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_node);

        [Test]
        public void Commands_are_put_on_run_by_label_replaced_and_taken_away_with_their_owner()
        {
            var ran = new List<string>();
            var owner = new GameObject("owner");
            try
            {
                _panel.Add(owner, "Skip", () => ran.Add("skip"));
                _panel.Add(owner, "Back", () => ran.Add("back"));
                _panel.Add(_node, "Lights", () => ran.Add("lights"));
                _panel.Add(owner, "Skip", () => ran.Add("skip again"));  // the same owner and label: replaced
                Assert.That(_panel.Commands, Is.EqualTo(new[] { "Skip", "Back", "Lights" }));
                Assert.That(_panel.Run("Skip") && _panel.Run("Lights") && !_panel.Run("Nothing"));
                Assert.That(ran, Is.EqualTo(new[] { "skip again", "lights" }));
                _panel.Remove(owner);
                Assert.That(_panel.Commands, Is.EqualTo(new[] { "Lights" }), "the owner's commands go with it");
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void It_is_the_command_surface_read_off_its_object()
        {
            var surface = _node.GetComponent<ICommandSurface>();
            Assert.That(surface, Is.SameAs(_panel), "what puts commands beside it finds it through the contract");
            surface.Add(_node, "Skip", () => { });
            surface.SetStatus("Step A02");
            Assert.That(_panel.Commands, Is.EqualTo(new[] { "Skip" }));
            Assert.That(_panel.Status, Is.EqualTo("Step A02"));
            surface.Remove(_node);
            Assert.That(_panel.Commands, Is.Empty, "taken away with their owner");
        }

        [Test]
        public void A_release_build_shows_it_only_where_asked_and_a_drag_keeps_within_its_limits()
        {
            Assert.That(_panel.ShowsIn(debugBuild: false), Is.False, "by default a release build leaves it out");
            Assert.That(_panel.ShowsIn(debugBuild: true), Is.True, "the editor and a development build keep it");
            _panel.releaseBuilds = true;
            Assert.That(_panel.ShowsIn(debugBuild: false), Is.True, "a release build shows it where asked");

            var eyes = new GameObject("eyes").transform;
            try
            {
                _panel.eyes = eyes;
                _panel.maxSwing = 10f;
                _panel.Place();
                Vector3 before = Vector3.ProjectOnPlane(_panel.transform.position - eyes.position, Vector3.up);
                _panel.Drag(new Vector2(5000f, 0f));  // far past 10 degrees
                Vector3 now = Vector3.ProjectOnPlane(_panel.transform.position - eyes.position, Vector3.up);
                Assert.That(Vector3.SignedAngle(before, now, Vector3.up), Is.EqualTo(10f).Within(0.05f), "swung to its limit");
            }
            finally
            {
                Object.DestroyImmediate(eyes.gameObject);
            }
        }

        [Test]
        public void It_says_its_status_folds_away_and_rides_beside_the_eyes()
        {
            _panel.SetStatus("Step B08");
            Assert.That(_panel.Status, Is.EqualTo("Step B08"));
            _panel.SetStatus(null);
            Assert.That(_panel.Status, Is.Empty);
            Assert.That(_panel.open, Is.True);
            _panel.Toggle();
            Assert.That(_panel.open, Is.False, "folded to its title");

            var eyes = new GameObject("eyes").transform;
            try
            {
                eyes.SetPositionAndRotation(new Vector3(1, 1.6f, 2), Quaternion.Euler(20f, 90f, 0f));  // looking down a little, heading +X
                _panel.eyes = eyes;
                _panel.Place();
                Vector3 expected = eyes.position + Quaternion.Euler(0f, 90f, 0f) * _panel.offset;
                Assert.That(_panel.transform.position, Is.EqualTo(expected).Using(UnityEngine.TestTools.Utils.Vector3EqualityComparer.Instance),
                            "beside the eyes, turned with their heading, not their pitch");
                Assert.That(Vector3.Dot(_panel.transform.forward, (_panel.transform.position - eyes.position).normalized), Is.GreaterThan(0.999f), "facing them");

                // Dragged as every window is: swung around the eyes, and it keeps the spot as it rides them.
                float radius = Vector3.Distance(eyes.position, _panel.transform.position);
                _panel.Drag(new Vector2(radius * 30f * Mathf.Deg2Rad * Placard.DefaultPixelsPerMetre, 0f));
                Vector3 flat = Vector3.ProjectOnPlane(expected - eyes.position, Vector3.up);
                Vector3 now = Vector3.ProjectOnPlane(_panel.transform.position - eyes.position, Vector3.up);
                Assert.That(Vector3.SignedAngle(flat, now, Vector3.up), Is.EqualTo(30f).Within(0.05f), "swung 30 degrees around the eyes");
                _panel.Place();
                Assert.That(Vector3.SignedAngle(flat, Vector3.ProjectOnPlane(_panel.transform.position - eyes.position, Vector3.up), Vector3.up),
                            Is.EqualTo(30f).Within(0.05f), "where the user put it");
            }
            finally
            {
                Object.DestroyImmediate(eyes.gameObject);
            }
        }
    }
}
