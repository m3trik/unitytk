using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Interaction.Tests
{
    /// <summary>
    /// <see cref="ClipTrigger"/>: what a click, a press held and a timed press play, and what letting go
    /// does to each: a clip played while held stops or plays back, a timed press that played lets it play on.
    /// </summary>
    public class ClipTriggerTests
    {
        GameObject _root;
        Transform _lid;
        ClipPlayer _player;
        ClipTrigger _trigger;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("root");
            _root.AddComponent<Animator>();
            _lid = new GameObject("LID").transform;
            _lid.SetParent(_root.transform);
            var open = new AnimationClip { name = "Open" };
            open.SetCurve("LID", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            _player = _root.AddComponent<ClipPlayer>();
            _player.clips.Add(new ClipPlayer.Entry { clip = open });
            var button = new GameObject("BUTTON");
            button.transform.SetParent(_root.transform);
            button.AddComponent<Target>();
            _trigger = button.AddComponent<ClipTrigger>();
            _trigger.player = _player;
            _trigger.clip = "Open";
        }

        [TearDown]
        public void TearDown()
        {
            _player.Clear();
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void A_timed_press_held_long_enough_plays_its_clip_on_after_it_is_let_go()
        {
            _trigger.pressSeconds = 0.5f;
            _trigger.Hold(null);
            _trigger.Tick(0.3f);
            Assert.That(_player.IsPlaying("Open"), Is.False, "not yet held long enough");
            Assert.That(_trigger.PressProgress, Is.EqualTo(0.6f).Within(1e-4f));
            _trigger.Tick(0.3f);
            Assert.That(_player.IsPlaying("Open"), Is.True, "held long enough: it plays, as a click does");
            _trigger.Release(null);
            Assert.That(_player.IsPaused("Open"), Is.False, "let go after its time: it plays on");
            _player.Advance(5f);
            Assert.That(_lid.localPosition.x, Is.EqualTo(1f).Within(1e-4f), "to its end");
        }

        [Test]
        public void A_timed_press_let_go_before_its_time_plays_nothing()
        {
            _trigger.pressSeconds = 0.5f;
            _trigger.Hold(null);
            _trigger.Tick(0.2f);
            _trigger.Release(null);
            _trigger.Tick(1f);
            Assert.That(_player.IsPlaying("Open"), Is.False);
            Assert.That(_trigger.PressProgress, Is.Zero);
        }

        [Test]
        public void A_click_plays_its_clip_on_whatever_a_release_says()
        {
            _trigger.Play(null);
            _trigger.Release(null);
            Assert.That(_player.IsPlaying("Open") && !_player.IsPaused("Open"), Is.True);
        }

        [Test]
        public void A_press_held_pauses_its_clip_as_it_is_let_go_or_plays_it_back()
        {
            _trigger.whileHeld = true;
            _trigger.Hold(null);
            _player.Advance(0.25f);
            _trigger.Release(null);
            Assert.That(_player.IsPaused("Open"), Is.True, "let go: it stands where it was");
            _trigger.reverseOnRelease = true;
            _trigger.Hold(null);
            _trigger.Release(null);
            Assert.That(_player.IsRewinding("Open"), Is.True, "or plays back to its start");
        }
    }
}
