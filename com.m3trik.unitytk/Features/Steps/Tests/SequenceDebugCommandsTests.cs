using M3trik.Unitytk.Contracts;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Steps.Tests
{
    /// <summary>
    /// <see cref="SequenceDebugCommands"/>: a sequence's commands on the surface it is given, Skip and Back moving
    /// through its steps, Autopilot switching it on and off, its status the step it is on.
    /// </summary>
    public class SequenceDebugCommandsTests
    {
        GameObject _root;
        Sequence _sequence;
        SequenceDebugCommands _commands;
        PureCommandSurface _surface;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Steps");
            _sequence = _root.AddComponent<Sequence>();
            foreach (string id in new[] { "A01", "A02", "A03" })
            {
                var node = new GameObject(id);
                node.transform.SetParent(_root.transform);
                var step = node.AddComponent<Step>();
                step.id = id;
                step.title = id == "A02" ? "Open the door" : id;
                _sequence.steps.Add(step);
            }
            _commands = _root.AddComponent<SequenceDebugCommands>();
            _commands.sequence = _sequence;
            _commands.autopilot = _root.AddComponent<SequenceAutopilot>();
            _commands.autopilot.enabled = false;
            _surface = new PureCommandSurface();
            _commands.Surface = _surface;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void Skip_and_back_move_through_the_steps_and_the_status_says_where()
        {
            _sequence.Begin();
            Assert.That(_commands.Status(), Is.EqualTo("Step A01"));
            _commands.SkipStep();
            Assert.That(_sequence.Current.id, Is.EqualTo("A02"));
            Assert.That(_commands.Status(), Is.EqualTo("Step A02   Open the door"), "its title beside its ID");
            _commands.SkipStep();
            _commands.StepBack();
            Assert.That(_sequence.Current.id, Is.EqualTo("A02"), "back to the step before");
            _commands.ToggleAutopilot();
            Assert.That(_commands.autopilot.enabled && _commands.autopilot.sequence == _sequence);
            Assert.That(_commands.Status(), Does.EndWith("(autopilot)"));
            _commands.ToggleAutopilot();
            Assert.That(_commands.autopilot.enabled, Is.False);
        }

        [Test]
        public void Its_commands_go_on_its_surface_under_their_labels()
        {
            _commands.restartLabel = "Start Over";
            Assert.That(_commands.Show(), Is.True);
            Assert.That(_surface.Commands, Is.EqualTo(new[] { "Skip", "Back", "Start Over", "Autopilot" }));
            Assert.That(_commands.Show(), Is.True);
            Assert.That(_surface.Commands, Is.EqualTo(new[] { "Skip", "Back", "Start Over", "Autopilot" }), "shown again: each replaced, none twice");
            _sequence.Begin();
            Assert.That(_surface.Run("Skip"), Is.True);
            Assert.That(_sequence.Current.id, Is.EqualTo("A02"), "the surface's Skip skips");
            _commands.Surface = null;
            Assert.That(_commands.Show(), Is.False, "no surface set, none beside it: nowhere to put them");
        }

        [Test]
        public void Back_resumes_on_the_reloaded_scenes_load_alone_with_the_autopilot_among_its_own_objects()
        {
            // Only the reloaded scene's own load ends the wait, and its own autopilot walks it back: an additive start
            // over's old copy is still loaded beside the new one.
            SequenceAutopilot old = _commands.autopilot;
            Scene copy = EditorSceneManager.NewPreviewScene();  // a second scene beside the test's, saving nothing
            try
            {
                var node = new GameObject("Steps");
                SceneManager.MoveGameObjectToScene(node, copy);
                SequenceAutopilot fresh = node.AddComponent<SequenceAutopilot>();
                fresh.enabled = false;
                Assert.That(SequenceDebugCommands.Resume(copy, "Assets/Another.unity", "A01"), Is.False, "another scene's load: left alone");
                Assert.That(fresh.enabled, Is.False);
                Assert.That(SequenceDebugCommands.Resume(copy, copy.path, "A01"), Is.True, "the copy's load");
                Assert.That((fresh.enabled, fresh.fastForward, fresh.stopAt), Is.EqualTo((true, true, "A01")), "its own autopilot walks it back");
                Assert.That((old.enabled, old.stopAt), Is.EqualTo((false, "")), "the old copy's is left alone");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(copy);
            }
        }

        [Test]
        public void A_scene_part_none_names_is_looked_up_once_none_is_found_until_its_owner_is_switched_on_again()
        {
            // A part none names is looked up once, not on every access (several a frame), until its owner is switched on again.
            Assume.That(Object.FindAnyObjectByType<WindZone>(), Is.Null, "a kind no test leaves in the scene");
            var lookup = new SceneLookup<WindZone>();
            Assert.That(lookup.Get(), Is.Null, "none there");
            var later = new GameObject("Later").AddComponent<WindZone>();
            try
            {
                Assert.That(lookup.Get(), Is.Null, "none found: not looked up again");
                lookup.Forget();  // its owner switched on again
                Assert.That(lookup.Get(), Is.SameAs(later));
                Object.DestroyImmediate(later.gameObject);
                Assert.That(lookup.Get(), Is.Null, "gone: looked up again, and none there");
            }
            finally
            {
                if (later != null) Object.DestroyImmediate(later.gameObject);
            }
        }

        [Test]
        public void The_scenes_sequence_and_autopilot_are_kept_once_found_and_found_again_once_gone()
        {
            // Where none is named, each is found once and kept, not looked up every frame.
            SequenceAutopilot pilot = _commands.autopilot;
            _commands.sequence = null;
            _commands.autopilot = null;
            Assert.That((_commands.Sequence, _commands.Autopilot), Is.EqualTo((_sequence, pilot)), "the scene's");
            var later = new GameObject("Later");
            try
            {
                Sequence other = later.AddComponent<Sequence>();
                SequenceAutopilot otherPilot = later.AddComponent<SequenceAutopilot>();
                Assert.That((_commands.Sequence, _commands.Autopilot), Is.EqualTo((_sequence, pilot)), "the ones found are kept");
                Object.DestroyImmediate(pilot);
                Object.DestroyImmediate(_sequence);
                Assert.That((_commands.Sequence, _commands.Autopilot), Is.EqualTo((other, otherPilot)), "once gone, the scene's again");
            }
            finally
            {
                Object.DestroyImmediate(later);
            }
        }
    }
}
