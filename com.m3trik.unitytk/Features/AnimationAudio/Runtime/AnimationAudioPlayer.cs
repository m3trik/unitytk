// The Animation Audio feature's runtime half (M3trik.Unitytk.AnimationAudio): the
// component that plays the sounds a model's clips cue with PlayAudio and StopAudio
// AnimationEvents. Its importer and actions are the feature's editor half (Editor/).
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Audio;

[assembly: InternalsVisibleTo("M3trik.Unitytk.AnimationAudio.Editor")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.AnimationAudio.Tests")]

namespace M3trik.Unitytk.AnimationAudio
{
    /// <summary>
    /// Plays the sounds a clip cues: an <see cref="AnimationEvent"/> calling
    /// <see cref="PlayAudio"/> starts its object (an <see cref="AudioClip"/>, from its
    /// float parameter's seconds in; or any other <see cref="AudioResource"/>, such as
    /// an Audio Random Container, from its start), and one calling
    /// <see cref="StopAudio"/> with the same object stops it. Delivered cues (written
    /// into a model's clips at import) and events a Unity user authors (the model
    /// importer's Events tab, the Animation window on a clip of their own) play
    /// alike. Unity sends a clip's events to the GameObject whose Animator or
    /// Animation plays it, so the player sits there: the model's root, where the
    /// import adds it. Its own <see cref="AudioSource"/> plays nothing: it holds the
    /// settings (mixer group, volume, 3D) every sound plays with, each sound on a
    /// source of its own, so several overlap and each stops alone.
    /// </summary>
    /// <remarks>
    /// <para>A sound also stops when the clip that started it stops playing, since an
    /// interrupted clip fires no more events: each sound is kept with the clip whose
    /// event started it, and once a frame (<see cref="LateUpdate"/>) the player asks
    /// the Animator which clips its states play, on every layer (the current and the
    /// next state, so a cross-fade keeps it), or, for a legacy Animation, whether the
    /// clip's state is still enabled. A sound started by an Animator with no
    /// controller (a Playables graph, Timeline), by a graph that drives an Animator
    /// beside its controller (its clip in none of the controller's states when the
    /// sound starts), or by a call that names no clip plays to its stop event or its
    /// end: the Animator's states do not show what such a graph plays.</para>
    /// <para>Prior art: Unity's AnimationEvent dispatch, which hands a method that
    /// takes an <see cref="AnimationEvent"/> the whole event (its object and float
    /// parameters, and the clip that fired it), is the trigger, as the Events tab and
    /// the Animation window author it; <see cref="AudioSource"/> the playback, one per
    /// sound, since <see cref="AudioSource.PlayOneShot(AudioClip)"/> can neither
    /// start into a sound nor stop one of several. The Animator's own state (
    /// <see cref="Animator.GetCurrentAnimatorClipInfo(int, List{AnimatorClipInfo})"/>)
    /// says which clips play; the Playables graph was weighed for that and not
    /// adopted, since an Animator Controller's graph shows no clips. A Timeline
    /// signal track was weighed for the cues and not adopted: the source delivers
    /// clips, not timelines.</para>
    /// </remarks>
    [AddComponentMenu("Audio/Animation Audio Player")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class AnimationAudioPlayer : MonoBehaviour
    {
        [Tooltip("Scales the volume of every sound this player plays (the AudioSource's own volume times this).")]
        [Range(0f, 1f)]
        public float volume = 1f;

        // A sound playing: its source, what it plays, and the clip whose event
        // started it (null when it names none), with that clip's legacy state, and
        // whether the Animator's states played that clip when it started (so they
        // tell when it stops).
        struct Voice
        {
            public AudioSource source;
            public AudioResource sound;
            public AnimationClip by;
            public AnimationState state;
            public bool legacy;
            public bool watched;
        }

        readonly List<Voice> _voices = new List<Voice>();
        readonly List<AudioSource> _idle = new List<AudioSource>();
        readonly List<AnimatorClipInfo> _infos = new List<AnimatorClipInfo>();
        AudioSource _template;
        Animator _animator;

        /// <summary>How many sounds are playing now.</summary>
        public int Playing => _voices.Count;

        /// <summary>Whether <paramref name="sound"/> is playing now, started by any clip.</summary>
        /// <param name="sound">The sound, as a PlayAudio event's object names it.</param>
        public bool IsPlaying(AudioResource sound)
        {
            for (int i = 0; i < _voices.Count; i++)
                if (_voices[i].sound == sound)
                    return true;
            return false;
        }

        /// <summary>
        /// Start the event's object: an <see cref="AudioClip"/> from its float
        /// parameter's seconds into it (a sound a cut between shots divides, or one
        /// trimmed at the head), another <see cref="AudioResource"/> from its start.
        /// Unity calls it for each AnimationEvent whose function is <c>PlayAudio</c>.
        /// </summary>
        /// <param name="evt">The event: its object is the sound, its float parameter the seconds into it.</param>
        public void PlayAudio(AnimationEvent evt)
        {
            if (evt == null) return;
            var sound = evt.objectReferenceParameter as AudioResource;
            if (sound == null)
            {
                Debug.LogWarning($"{name}: a PlayAudio event at {evt.time:F3} s of '{ClipName(evt)}' names no sound -- " +
                                 "set its Object to an AudioClip (the Events tab, or the Animation window).", this);
                return;
            }
            AnimationClip by = ClipOf(evt, out AnimationState state, out bool legacy);
            Play(sound, evt.floatParameter, by, state, legacy);
        }

        /// <summary>
        /// Start <paramref name="sound"/> (an AudioClip <paramref name="offset"/>
        /// seconds in), kept with the clip <paramref name="by"/> that started it
        /// (with its legacy <paramref name="state"/> when a legacy Animation plays it).
        /// </summary>
        internal void Play(AudioResource sound, float offset, AnimationClip by,
                           AnimationState state = null, bool legacy = false)
        {
            var clip = sound as AudioClip;
            offset = Mathf.Max(0f, offset);
            if (clip != null && offset >= clip.length) return;
            AudioSource source = Acquire();
            AudioSource template = Template();
            source.resource = sound;
            source.volume = (template != null ? template.volume : 1f) * volume;
            source.time = clip != null ? offset : 0f;
            source.Play();
            bool watched = !legacy && by != null && StatesPlay(by);
            _voices.Add(new Voice { source = source, sound = sound, by = by, state = state, legacy = legacy, watched = watched });
        }

        /// <summary>
        /// Stop the event's object where the clip that fires this started it (where
        /// the event names no clip, wherever it plays). Unity calls it for each
        /// AnimationEvent whose function is <c>StopAudio</c>.
        /// </summary>
        /// <param name="evt">The event: its object is the sound to stop.</param>
        public void StopAudio(AnimationEvent evt)
        {
            if (evt == null) return;
            var sound = evt.objectReferenceParameter as AudioResource;
            if (sound == null) return;
            AnimationClip by = ClipOf(evt, out _, out _);
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                Voice voice = _voices[i];
                if (voice.sound == sound && (by == null || voice.by == null || voice.by == by))
                    Release(i);
            }
        }

        /// <summary>Stop every sound this player plays.</summary>
        public void StopAll()
        {
            for (int i = _voices.Count - 1; i >= 0; i--)
                Release(i);
        }

        void LateUpdate() => Watch();

        void OnDisable() => StopAll();

        /// <summary>The settings a new player's own AudioSource starts with: nothing on
        /// awake, and 3D, heard where the model is.</summary>
        internal static void Defaults(AudioSource source)
        {
            if (source == null) return;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
        }

        /// <summary>
        /// Let go of each sound that has ended, and stop each one whose clip no
        /// longer plays (its Animator's states play it on no layer, or its legacy
        /// state is disabled); allocates nothing.
        /// </summary>
        internal void Watch()
        {
            if (_voices.Count == 0) return;
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                Voice voice = _voices[i];
                if (voice.source == null || !voice.source.isPlaying || !ClipPlays(voice))
                    Release(i);
            }
        }

        // Whether the clip that started the voice still plays; true when that
        // cannot be told (no clip, an Animator with no controller, or a clip its
        // controller's states did not play when the sound started: a Playables
        // graph's, Timeline's, beside the controller).
        bool ClipPlays(Voice voice)
        {
            if (voice.legacy) return voice.state != null && voice.state.enabled;
            if (voice.by == null || !voice.watched) return true;
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_animator == null || _animator.runtimeAnimatorController == null) return true;
            if (!_animator.isActiveAndEnabled) return false;
            return StatesPlay(voice.by);
        }

        // Whether this object's Animator plays the clip in one of its controller's
        // states, current or next, on any layer.
        bool StatesPlay(AnimationClip clip)
        {
            if (_animator == null) _animator = GetComponent<Animator>();
            if (_animator == null || _animator.runtimeAnimatorController == null || !_animator.isActiveAndEnabled)
                return false;
            for (int layer = 0; layer < _animator.layerCount; layer++)
            {
                _animator.GetCurrentAnimatorClipInfo(layer, _infos);
                if (Holds(clip)) return true;
                _animator.GetNextAnimatorClipInfo(layer, _infos);
                if (Holds(clip)) return true;
            }
            return false;
        }

        bool Holds(AnimationClip clip)
        {
            for (int i = 0; i < _infos.Count; i++)
                if (_infos[i].clip == clip)
                    return true;
            return false;
        }

        // The clip whose event this is (and, for a legacy Animation, its state);
        // null for an event no Animator or Animation fired.
        static AnimationClip ClipOf(AnimationEvent evt, out AnimationState state, out bool legacy)
        {
            state = null;
            legacy = false;
            if (evt.isFiredByAnimator) return evt.animatorClipInfo.clip;
            if (!evt.isFiredByLegacy) return null;
            legacy = true;
            state = evt.animationState;
            return state != null ? state.clip : null;
        }

        static string ClipName(AnimationEvent evt)
        {
            AnimationClip clip = ClipOf(evt, out _, out _);
            return clip != null ? clip.name : "its clip";
        }

        AudioSource Template()
        {
            if (_template == null) _template = GetComponent<AudioSource>();
            return _template;
        }

        // A source to play one sound, set as the player's own is (which plays
        // nothing itself, so its settings stay the user's): a free one, else a new one.
        AudioSource Acquire()
        {
            AudioSource source = null;
            while (source == null && _idle.Count > 0)
            {
                source = _idle[_idle.Count - 1];
                _idle.RemoveAt(_idle.Count - 1);
            }
            if (source == null)
            {
                source = gameObject.AddComponent<AudioSource>();
                source.hideFlags = HideFlags.HideAndDontSave;
                source.playOnAwake = false;
            }
            AudioSource template = Template();
            if (template != null) Copy(template, source);
            return source;
        }

        void Release(int index)
        {
            AudioSource source = _voices[index].source;
            _voices.RemoveAt(index);
            if (source == null) return;
            source.Stop();
            _idle.Add(source);
        }

        // The settings that shape how a sound is heard, the player's own source's.
        static void Copy(AudioSource from, AudioSource to)
        {
            to.outputAudioMixerGroup = from.outputAudioMixerGroup;
            to.mute = from.mute;
            to.bypassEffects = from.bypassEffects;
            to.bypassListenerEffects = from.bypassListenerEffects;
            to.bypassReverbZones = from.bypassReverbZones;
            to.priority = from.priority;
            to.pitch = from.pitch;
            to.panStereo = from.panStereo;
            to.spatialBlend = from.spatialBlend;
            to.reverbZoneMix = from.reverbZoneMix;
            to.dopplerLevel = from.dopplerLevel;
            to.spread = from.spread;
            to.rolloffMode = from.rolloffMode;
            to.minDistance = from.minDistance;
            to.maxDistance = from.maxDistance;
            to.spatialize = from.spatialize;
            to.spatializePostEffects = from.spatializePostEffects;
            to.velocityUpdateMode = from.velocityUpdateMode;
            to.ignoreListenerPause = from.ignoreListenerPause;
            to.ignoreListenerVolume = from.ignoreListenerVolume;
            if (from.rolloffMode == AudioRolloffMode.Custom)
                to.SetCustomCurve(AudioSourceCurveType.CustomRolloff, from.GetCustomCurve(AudioSourceCurveType.CustomRolloff));
        }
    }
}
