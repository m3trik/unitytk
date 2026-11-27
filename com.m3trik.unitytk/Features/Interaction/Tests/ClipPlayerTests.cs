using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.Interaction.Tests
{
    /// <summary>
    /// <see cref="ClipPlayer"/>: clips played on demand, each over the stretch its entry names and
    /// kept at its end, a later one over an earlier, and the holdables a clip moves lent to it,
    /// the clip waiting at its first pose until they arrive. Time is driven by the test.
    /// </summary>
    public class ClipPlayerTests
    {
        [Test]
        public void No_component_of_the_feature_names_a_method_after_a_Unity_message_it_does_not_answer()
        {
            // Unity calls Start, Awake, Update... by name: one taking parameters is a script error
            // ("Start() can not take parameters") on every domain reload. Added: 2026-11-22
            var messages = new HashSet<string> { "Awake", "Start", "Update", "LateUpdate", "FixedUpdate", "OnEnable", "OnDisable", "OnDestroy", "Reset", "OnValidate" };
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            var wrong = new List<string>();
            foreach (System.Type type in typeof(ClipPlayer).Assembly.GetTypes())
            {
                if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                foreach (var method in type.GetMethods(flags))
                    if (messages.Contains(method.Name) && method.GetParameters().Length > 0) wrong.Add($"{type.Name}.{method.Name}");
            }
            Assert.That(wrong, Is.Empty);
        }

        GameObject _root;
        Transform _door;
        Transform _lid;
        ClipPlayer _player;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("delivery");
            _root.AddComponent<Animator>();
            _door = Child("DOOR");
            _lid = Child("LID");
            _player = _root.AddComponent<ClipPlayer>();
            _player.clips.Add(new ClipPlayer.Entry { clip = Clip("Open", "DOOR", 0f, 2f) });
            _player.clips.Add(new ClipPlayer.Entry { clip = Clip("Lift", "LID", 0f, 3f) });
            _player.clips.Add(new ClipPlayer.Entry { clip = Clip("Close", "DOOR", 2f, 0f) });
        }

        [TearDown]
        public void TearDown()
        {
            _player.Clear();
            Object.DestroyImmediate(_root);
        }

        Transform Child(string name)
        {
            var node = new GameObject(name).transform;
            node.SetParent(_root.transform);
            return node;
        }

        // A one-second clip moving path's local X from a to b.
        static AnimationClip Clip(string name, string path, float a, float b)
        {
            var clip = new AnimationClip { name = name };
            clip.SetCurve(path, typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, a, 1f, b));
            return clip;
        }

        [Test]
        public void A_clip_plays_from_its_start_to_its_end_and_stays_there()
        {
            var finished = new List<string>();
            _player.onFinished.AddListener(finished.Add);
            _player.Play("Open");
            Assert.That(_player.IsPlaying("Open"));
            Assert.That(_door.localPosition.x, Is.EqualTo(0f).Within(1e-4));
            _player.Advance(0.5f);
            Assert.That(_door.localPosition.x, Is.EqualTo(1f).Within(1e-4));
            _player.Advance(5f);
            Assert.That(_door.localPosition.x, Is.EqualTo(2f).Within(1e-4));
            Assert.That(!_player.IsPlaying("Open") && !_player.Busy);
            _player.Advance(1f);
            Assert.That(finished, Is.EqualTo(new[] { "Open" }), "it finishes once");
        }

        [Test]
        public void A_clip_held_plays_while_held_stops_as_let_go_and_goes_on_from_there()
        {
            var finished = new List<string>();
            _player.onFinished.AddListener(finished.Add);
            _player.Hold("Open");
            _player.Advance(0.25f);
            _player.Pause("Open");
            _player.Advance(5f);
            Assert.That(_door.localPosition.x, Is.EqualTo(0.5f).Within(1e-4), "let go: it stands where it was");
            Assert.That(_player.IsPaused("Open") && _player.IsPlaying("Open") && !_player.Busy, Is.True, "paused: playing, waiting on a hold");
            _player.Hold("Open");
            Assert.That(_player.IsPaused("Open"), Is.False);
            _player.Advance(0.25f);
            Assert.That(_door.localPosition.x, Is.EqualTo(1f).Within(1e-4), "held again: on from there, not from the start");
            _player.Advance(5f);
            Assert.That(finished, Is.EqualTo(new[] { "Open" }));
        }

        [Test]
        public void A_clip_rewound_plays_back_to_its_start_and_waits_there_on_a_hold()
        {
            var finished = new List<string>();
            _player.onFinished.AddListener(finished.Add);
            _player.Hold("Open");
            _player.Advance(0.5f);
            _player.Rewind("Open");
            Assert.That(_player.IsRewinding("Open") && _player.Busy, Is.True, "let go: it plays back");
            _player.Advance(0.25f);
            Assert.That(_door.localPosition.x, Is.EqualTo(0.5f).Within(1e-4), "back the way it came");
            _player.Advance(5f);
            Assert.That(_door.localPosition.x, Is.EqualTo(0f).Within(1e-4), "at its start");
            Assert.That(_player.IsPaused("Open") && !_player.IsRewinding("Open") && !_player.Busy, Is.True, "it waits there on a hold");
            _player.Hold("Open");
            _player.Advance(5f);
            Assert.That(finished, Is.EqualTo(new[] { "Open" }), "held through, it ends");
            _player.Rewind("Open");
            Assert.That(_player.IsRewinding("Open"), Is.False, "a clip at its end is left");
        }

        [Test]
        public void A_later_clip_moves_only_what_it_animates_and_over_an_earlier_one()
        {
            _player.Play("Open");
            _player.Advance(1f);
            _player.Play("Lift");
            _player.Advance(1f);
            Assert.That(_door.localPosition.x, Is.EqualTo(2f).Within(1e-4), "the door keeps the pose Open left");
            Assert.That(_lid.localPosition.x, Is.EqualTo(3f).Within(1e-4));
            _player.Play("Close");
            _player.Advance(1f);
            Assert.That(_door.localPosition.x, Is.EqualTo(0f).Within(1e-4), "Close is over Open");
            Assert.That(_lid.localPosition.x, Is.EqualTo(3f).Within(1e-4));
        }

        [Test]
        public void A_holdable_the_clip_moves_is_lent_while_it_plays()
        {
            var lid = _lid.gameObject.AddComponent<Holdable>();
            _player.Find("Lift").lends.Add(lid);
            _player.Play("Lift");
            Assert.That(lid.IsLent && lid.Travelling, "lent, and on its way into the clip's pose");
            _player.Advance(0.5f);
            Assert.That(_player.IsWaiting("Lift") && _player.Busy, "the clip waits at its first pose for it");
            Assert.That(_lid.localPosition.x, Is.EqualTo(0f).Within(1e-4), "nothing played before it arrived");
            lid.Tick(0f);
            lid.Tick(lid.TravelFor + 0.01f);
            _player.Advance(0.5f);
            Assert.That(!_player.IsWaiting("Lift"));
            Assert.That(_lid.localPosition.x, Is.EqualTo(1.5f).Within(1e-4), "arrived: it plays");
            _player.Advance(1f);
            Assert.That(!lid.IsLent, "given back at its end");

            _player.Find("Lift").waits = false;
            _player.Play("Lift");
            _player.Advance(0.5f);
            Assert.That(_lid.localPosition.x, Is.EqualTo(1.5f).Within(1e-4), "told not to wait: it plays at once, the holdable catching up");
        }

        // A lent holdable switched off never moved on (its LateUpdate is a frame's own), so the clip waited at its first
        // pose for good, and its end, which a step waits on, never came.
        [Test]
        public void A_holdable_switched_off_is_brought_there_at_once_and_the_clip_plays_to_its_end()
        {
            var finished = new List<string>();
            _player.onFinished.AddListener(finished.Add);
            var lid = _lid.gameObject.AddComponent<Holdable>();
            lid.enabled = false;
            _player.Find("Lift").lends.Add(lid);
            _player.Play("Lift");
            Assert.That(lid.IsLent && !lid.Travelling, "lent, and brought into the clip's first pose at once");
            _player.Advance(0.5f);
            Assert.That(_player.IsWaiting("Lift"), Is.False, "nothing to wait for");
            _player.Advance(5f);
            Assert.That(finished, Is.EqualTo(new[] { "Lift" }), "it plays to its end");
        }

        [Test]
        public void A_clip_plays_the_stretch_its_entry_names_at_its_speed()
        {
            var finished = new List<string>();
            _player.onFinished.AddListener(finished.Add);
            ClipPlayer.Entry open = _player.Find("Open");
            open.from = 0.25f;   // its first quarter popped the door into place
            open.until = 0.75f;  // its last held the next take's pose
            _player.Play("Open");
            Assert.That(_door.localPosition.x, Is.EqualTo(0.5f).Within(1e-4), "it starts where its motion does");
            _player.Advance(0.25f);
            Assert.That(_door.localPosition.x, Is.EqualTo(1f).Within(1e-4));
            _player.Advance(5f);
            Assert.That(_door.localPosition.x, Is.EqualTo(1.5f).Within(1e-4), "and ends, and stays, where its motion does");
            Assert.That(finished, Is.EqualTo(new[] { "Open" }));

            open.speed = 2f;
            _player.Play("Open");
            _player.Advance(0.125f);
            Assert.That(_door.localPosition.x, Is.EqualTo(1f).Within(1e-4), "twice as fast");
            Assert.That((open.Start, open.End), Is.EqualTo((0.25f, 0.75f)));
            open.until = 0f;
            Assert.That(open.End, Is.EqualTo(1f), "no end named: the clip's");
        }

        [Test]
        public void A_clip_named_to_follow_plays_when_the_first_ends()
        {
            var finished = new List<string>();
            _player.onFinished.AddListener(finished.Add);
            _player.Find("Open").then = "Lift";
            _player.Play("Open");
            _player.Advance(1f);
            Assert.That(_player.IsPlaying("Lift"));
            _player.Advance(1f);
            Assert.That(finished, Is.EqualTo(new[] { "Open", "Lift" }));
            Assert.That(_lid.localPosition.x, Is.EqualTo(3f).Within(1e-4));
        }

        [Test]
        public void A_clip_ending_as_the_one_before_it_names_it_to_follow_still_raises_its_end()
        {
            // Open's end played Lift again, which dropped Lift's layer, ended the same frame, before its end was raised.
            var finished = new List<string>();
            _player.onFinished.AddListener(finished.Add);
            _player.Find("Open").then = "Lift";
            _player.Play("Open");
            _player.Play("Lift");
            _player.Advance(1f);
            Assert.That(finished, Is.EqualTo(new[] { "Open", "Lift" }), "both ends raised");
            Assert.That(_player.IsPlaying("Lift"), Is.True, "and Lift plays again, after Open");
        }

        [Test]
        public void A_clip_it_does_not_know_plays_nothing()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("no clip 'Spin'"));
            _player.Play("Spin");
            Assert.That(!_player.Busy);
        }

        // How many clip layers the player's graph holds.
        int Layers() => ((System.Collections.IList)typeof(ClipPlayer)
            .GetField("_layers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).GetValue(_player)).Count;

        [Test]
        public void A_clip_played_again_takes_the_place_of_its_finished_layer_and_the_poses_stand()
        {
            for (int i = 0; i < 3; i++)
            {
                _player.Play("Open");
                _player.Advance(5f);
            }
            Assert.That(Layers(), Is.EqualTo(1), "the graph does not grow with every play of one clip");
            _player.Play("Lift");
            _player.Advance(5f);
            _player.Play("Open");
            Assert.That(Layers(), Is.EqualTo(2), "another clip's layer stays: it holds the lid where it ended");
            _player.Advance(5f);
            Assert.That(_door.localPosition.x, Is.EqualTo(2f).Within(1e-4));
            Assert.That(_lid.localPosition.x, Is.EqualTo(3f).Within(1e-4), "the lid keeps the pose Lift left");
            _player.Play("Close");
            _player.Advance(5f);
            Assert.That(_door.localPosition.x, Is.EqualTo(0f).Within(1e-4), "a later clip still plays over an earlier one");
        }
    }
}
