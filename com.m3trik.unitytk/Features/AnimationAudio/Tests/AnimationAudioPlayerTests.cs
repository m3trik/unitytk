using System.Linq;
using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.AnimationAudio.Tests
{
    /// <summary>
    /// The player starts a cue's sound and stops it at its stop event, each sound on a
    /// source of its own, and stops a sound when the clip that started it stops
    /// playing: an Animator's states no longer play it on any layer (an interrupted
    /// clip fires no more events). Events an Animator fires reach it as Unity sends
    /// them, delivered and Unity-authored alike.
    /// </summary>
    public class AnimationAudioPlayerTests
    {
        GameObject _actor;
        AnimationAudioPlayer _player;
        AudioClip _sound;
        AudioClip _other;
        AudioClip _long;

        [SetUp]
        public void SetUp()
        {
            _actor = new GameObject("Actor");
            _player = _actor.AddComponent<AnimationAudioPlayer>();
            _sound = AudioClip.Create("chime", 8000 * 2, 1, 8000, false);
            _other = AudioClip.Create("door", 8000, 1, 8000, false);
            _long = AudioClip.Create("hum", 8000 * 6, 1, 8000, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_actor);
            Object.DestroyImmediate(_sound);
            Object.DestroyImmediate(_other);
            Object.DestroyImmediate(_long);
        }

        static AnimationEvent Cue(string function, AudioClip sound, float offset = 0f)
            => new AnimationEvent { functionName = function, objectReferenceParameter = sound, floatParameter = offset };

        [Test]
        public void The_function_names_the_events_call_are_the_players_own()
        {
            Assert.That(PersistedStrings.PlayAudioFunction, Is.EqualTo(nameof(AnimationAudioPlayer.PlayAudio)));
            Assert.That(PersistedStrings.StopAudioFunction, Is.EqualTo(nameof(AnimationAudioPlayer.StopAudio)));
        }

        [Test]
        public void A_cue_plays_from_its_offset_until_its_stop_event()
        {
            _player.PlayAudio(Cue(PersistedStrings.PlayAudioFunction, _sound, 0.5f));
            Assert.That(_player.IsPlaying(_sound), Is.True);
            AudioSource voice = Voices().Single(s => s.isPlaying);
            Assert.That(voice.clip, Is.EqualTo(_sound));
            Assert.That(voice.time, Is.EqualTo(0.5f).Within(0.01f), "started half a second into the sound");
            Assert.That(_actor.GetComponent<AudioSource>().isPlaying, Is.False, "the player's own source only holds the settings");

            _player.Watch();
            Assert.That(_player.IsPlaying(_sound), Is.True, "still sounding: kept");
            _player.StopAudio(Cue(PersistedStrings.StopAudioFunction, _sound));
            Assert.That(_player.Playing, Is.EqualTo(0));
            Assert.That(Voices().Any(s => s.isPlaying), Is.False);
        }

        [Test]
        public void Two_sounds_overlap_and_each_stops_alone()
        {
            _player.PlayAudio(Cue(PersistedStrings.PlayAudioFunction, _sound));
            _player.PlayAudio(Cue(PersistedStrings.PlayAudioFunction, _other));
            Assert.That(_player.Playing, Is.EqualTo(2));
            _player.StopAudio(Cue(PersistedStrings.StopAudioFunction, _other));
            Assert.That(_player.IsPlaying(_sound), Is.True);
            Assert.That(_player.IsPlaying(_other), Is.False);
        }

        [Test]
        public void An_offset_past_the_sounds_end_plays_nothing_and_a_cue_with_no_sound_says_so()
        {
            _player.PlayAudio(Cue(PersistedStrings.PlayAudioFunction, _other, 5f));
            Assert.That(_player.Playing, Is.EqualTo(0));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("names no sound"));
            _player.PlayAudio(Cue(PersistedStrings.PlayAudioFunction, null));
            Assert.That(_player.Playing, Is.EqualTo(0));
        }

        // -- the clip that started it ---------------------------------------------------

        AnimatorController Controller(out AnimationClip intro, out AnimationClip outro)
        {
            intro = new AnimationClip { name = "Intro" };
            outro = new AnimationClip { name = "Outro" };
            var curve = AnimationCurve.Linear(0f, 0f, 2f, 1f);
            intro.SetCurve("", typeof(Transform), "localPosition.x", curve);
            outro.SetCurve("", typeof(Transform), "localPosition.y", curve);
            var controller = new AnimatorController();
            controller.AddLayer("Base Layer");
            controller.layers[0].stateMachine.AddState("Intro").motion = intro;
            controller.layers[0].stateMachine.AddState("Outro").motion = outro;
            return controller;
        }

        [Test]
        public void A_sound_stops_when_the_clip_that_started_it_stops_playing()
        {
            var controller = Controller(out AnimationClip intro, out AnimationClip outro);
            var animator = _actor.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            try
            {
                animator.Play("Intro", 0, 0f);
                animator.Update(0f);
                _player.Play(_sound, 0f, intro);
                _player.Play(_other, 0f, null);
                _player.Watch();
                Assert.That(_player.IsPlaying(_sound), Is.True, "Intro plays: its sound plays on");

                animator.Play("Outro", 0, 0f);
                animator.Update(0f);
                _player.Watch();
                Assert.That(_player.IsPlaying(_sound), Is.False, "Intro was interrupted: no stop event will fire, so it stops");
                Assert.That(_player.IsPlaying(_other), Is.True, "a sound no clip started plays to its stop or its end");
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(intro);
                Object.DestroyImmediate(outro);
            }
        }

        [Test]
        public void A_stop_event_that_names_no_clip_stops_the_sound_wherever_it_plays()
        {
            // A call no Animator or Animation fired (a script's) names no clip.
            var controller = Controller(out AnimationClip intro, out AnimationClip outro);
            try
            {
                _player.Play(_sound, 0f, outro);
                _player.Play(_sound, 0f, intro);
                Assert.That(_player.Playing, Is.EqualTo(2));
                _player.StopAudio(Cue(PersistedStrings.StopAudioFunction, _sound));
                Assert.That(_player.Playing, Is.EqualTo(0), "a stop that names no clip stops the sound wherever it plays");
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(intro);
                Object.DestroyImmediate(outro);
            }
        }

        [Test]
        public void A_stop_event_an_animator_fires_stops_only_what_its_own_clip_started()
        {
            // At a cut the next clip plays the sound on while the last one stops it: the
            // two events may come in either order within the frame, so Intro's stop
            // must leave Outro's voice playing.
            var controller = Controller(out AnimationClip intro, out AnimationClip outro);
            AnimationUtility.SetAnimationEvents(intro, new[]
            {
                new AnimationEvent { time = 0.5f, functionName = PersistedStrings.StopAudioFunction, objectReferenceParameter = _sound },
            });
            var animator = _actor.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            try
            {
                animator.Play("Intro", 0, 0f);
                animator.Update(0f);
                _player.Play(_sound, 0f, outro);
                _player.Play(_sound, 0f, intro);
                animator.Update(0.6f);
                Assert.That(_player.Playing, Is.EqualTo(1), "Intro's stop stopped Intro's voice");
                Assert.That(_player.IsPlaying(_sound), Is.True, "and left the one Outro started");
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(intro);
                Object.DestroyImmediate(outro);
            }
        }

        [Test]
        public void A_sound_stops_when_its_animator_is_disabled()
        {
            var controller = Controller(out AnimationClip intro, out AnimationClip outro);
            var animator = _actor.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            try
            {
                animator.Play("Intro", 0, 0f);
                animator.Update(0f);
                _player.Play(_sound, 0f, intro);
                _player.Watch();
                Assert.That(_player.IsPlaying(_sound), Is.True);
                animator.enabled = false;
                _player.Watch();
                Assert.That(_player.IsPlaying(_sound), Is.False, "a disabled Animator plays nothing: no stop event will fire");
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(intro);
                Object.DestroyImmediate(outro);
            }
        }

        [Test]
        public void A_cross_fade_keeps_the_sound_its_next_state_started()
        {
            var controller = Controller(out AnimationClip intro, out AnimationClip outro);
            var animator = _actor.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            try
            {
                animator.Play("Intro", 0, 0f);
                animator.Update(0f);
                animator.CrossFade("Outro", 0.5f, 0, 0f);
                animator.Update(0.1f);
                Assert.That(animator.IsInTransition(0), Is.True, "mid-fade");
                _player.Play(_sound, 0f, intro);
                _player.Play(_other, 0f, outro);
                _player.Watch();
                Assert.That(_player.IsPlaying(_sound), Is.True, "the current state's clip still plays");
                Assert.That(_player.IsPlaying(_other), Is.True, "and the next state's, fading in");
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(intro);
                Object.DestroyImmediate(outro);
            }
        }

        [Test]
        public void A_sound_stops_when_its_legacy_animation_stops()
        {
            var clip = new AnimationClip { name = "Swing", legacy = true };
            clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 2f, 1f));
            var animation = _actor.AddComponent<Animation>();
            animation.AddClip(clip, clip.name);
            try
            {
                animation.Play(clip.name);
                AnimationState state = animation[clip.name];
                _player.Play(_sound, 0f, clip, state, legacy: true);
                _player.Watch();
                Assert.That(_player.IsPlaying(_sound), Is.True, "its state is enabled");
                animation.Stop(clip.name);
                _player.Watch();
                Assert.That(_player.IsPlaying(_sound), Is.False, "its state is disabled: no stop event will fire");
            }
            finally
            {
                Object.DestroyImmediate(clip);
            }
        }

        [Test]
        public void At_a_cut_the_next_clip_plays_the_sound_on_and_the_last_clip_stops_its_own()
        {
            // The cut the delivery writes: Intro stops the sound at its last frame
            // (t == its length), Outro plays it on from its first (t = 0), and the
            // Animator moves from one to the other as Intro ends. Fired by real
            // Animator updates, never by hand.
            var controller = Controller(out AnimationClip intro, out AnimationClip outro);
            var cut = controller.layers[0].stateMachine.states[0].state.AddTransition(
                controller.layers[0].stateMachine.states[1].state);
            cut.hasExitTime = true;
            cut.exitTime = 1f;
            cut.hasFixedDuration = true;
            cut.duration = 0f;
            AnimationUtility.SetAnimationEvents(intro, new[]
            {
                new AnimationEvent { time = 0f, functionName = PersistedStrings.PlayAudioFunction, objectReferenceParameter = _long },
                new AnimationEvent { time = intro.length, functionName = PersistedStrings.StopAudioFunction, objectReferenceParameter = _long },
            });
            AnimationUtility.SetAnimationEvents(outro, new[]
            {
                new AnimationEvent { time = 0f, functionName = PersistedStrings.PlayAudioFunction, objectReferenceParameter = _long, floatParameter = intro.length },
                new AnimationEvent { time = outro.length, functionName = PersistedStrings.StopAudioFunction, objectReferenceParameter = _long },
            });
            var animator = _actor.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            try
            {
                animator.Play("Intro", 0, 0f);
                animator.Update(0f);
                animator.Update(0.5f);
                Assert.That(_player.Playing, Is.EqualTo(1), "Intro's PlayAudio at t = 0 fired");
                for (int i = 0; i < 4; i++) animator.Update(0.5f);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Outro"), Is.True, "the cut was taken");
                _player.Watch();
                Assert.That(_player.Playing, Is.EqualTo(1), "Intro stopped its own at its end; Outro plays it on");
                AudioSource voice = Voices().Single(s => s.isPlaying);
                Assert.That(voice.time, Is.GreaterThanOrEqualTo(intro.length - 0.01f), "from the offset its cue carries");
                for (int i = 0; i < 4; i++) animator.Update(0.5f);
                Assert.That(_player.Playing, Is.EqualTo(0), "Outro's StopAudio at t == its length fired");
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(intro);
                Object.DestroyImmediate(outro);
            }
        }

        [Test]
        public void A_sound_a_playables_graph_starts_plays_on_beside_the_animators_controller()
        {
            // Timeline, or any Playables graph, can drive an Animator that also has a
            // controller: its clip fires events with an animatorClipInfo, but no
            // controller state plays it, so the states cannot say when it stops.
            var controller = Controller(out AnimationClip intro, out AnimationClip outro);
            var cued = new AnimationClip { name = "Cued" };
            cued.SetCurve("", typeof(Transform), "localPosition.z", AnimationCurve.Linear(0f, 0f, 2f, 1f));
            AnimationUtility.SetAnimationEvents(cued, new[]
            {
                new AnimationEvent { time = 0.2f, functionName = PersistedStrings.PlayAudioFunction, objectReferenceParameter = _sound },
            });
            var animator = _actor.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            PlayableGraph graph = PlayableGraph.Create("cued");
            try
            {
                animator.Play("Intro", 0, 0f);
                animator.Update(0f);
                var output = AnimationPlayableOutput.Create(graph, "out", animator);
                output.SetSourcePlayable(AnimationClipPlayable.Create(graph, cued));
                graph.Evaluate(0f);
                graph.Evaluate(0.3f);
                Assert.That(_player.IsPlaying(_sound), Is.True, "the graph's clip sent PlayAudio");
                _player.Watch();
                Assert.That(_player.IsPlaying(_sound), Is.True, "and its sound plays on: no controller state plays its clip");
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(intro);
                Object.DestroyImmediate(outro);
                Object.DestroyImmediate(cued);
            }
        }

        [Test]
        public void An_animators_events_reach_the_player_and_its_stop_ends_the_sound()
        {
            var controller = Controller(out AnimationClip intro, out AnimationClip outro);
            AnimationUtility.SetAnimationEvents(intro, new[]
            {
                new AnimationEvent { time = 0.2f, functionName = PersistedStrings.PlayAudioFunction, objectReferenceParameter = _sound, floatParameter = 0.25f },
                new AnimationEvent { time = 0.6f, functionName = PersistedStrings.StopAudioFunction, objectReferenceParameter = _sound },
            });
            var animator = _actor.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            try
            {
                animator.Play("Intro", 0, 0f);
                animator.Update(0f);
                animator.Update(0.3f);
                Assert.That(_player.IsPlaying(_sound), Is.True, "the Animator sent PlayAudio");
                animator.Update(0.4f);
                Assert.That(_player.IsPlaying(_sound), Is.False, "and then StopAudio");
            }
            finally
            {
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(intro);
                Object.DestroyImmediate(outro);
            }
        }

        AudioSource[] Voices() => _actor.GetComponents<AudioSource>().Where(s => s.hideFlags != HideFlags.None).ToArray();
    }
}
