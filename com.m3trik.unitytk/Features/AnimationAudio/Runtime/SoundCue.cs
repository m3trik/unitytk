using UnityEngine;

namespace M3trik.Unitytk.AnimationAudio
{
    /// <summary>
    /// A sound that plays while this is switched on and stops as it is switched off: whatever switches it on
    /// (a script, an event, a timeline) plays it for as long as it stays on, and switching it off early stops
    /// it. It plays through the <see cref="AudioSource"/> beside it, by default flat (no position: in the
    /// listener's ears) and once; its spatial blend and looping are its own, and it can play the source as
    /// the source is set up instead (<see cref="setSource"/>).
    /// </summary>
    /// <remarks>
    /// Prior art: an AudioSource's own Play On Awake plays once, as its object wakes, and never
    /// again as it is re-enabled, nor stops on disable; Timeline's audio tracks belong to a
    /// timeline's clock. What switches this on and off is its clock, so it plays on each switch on.
    /// </remarks>
    [AddComponentMenu("unitytk/Animation Audio/Sound Cue")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class SoundCue : MonoBehaviour
    {
        [Tooltip("The sound it plays (empty with Set Source off: the source's own clip).")]
        public AudioClip clip;

        [Tooltip("Set the AudioSource from these fields each time it plays (its clip, looping and spatial blend); off: play " +
                 "the source as it is set up.")]
        public bool setSource = true;

        [Tooltip("How far the sound is placed at this object: 0 flat (in the listener's ears), 1 fully 3D.")]
        [Range(0f, 1f)]
        public float spatialBlend;

        [Tooltip("Play it over and over while switched on; off: once.")]
        public bool loop;

        /// <summary>How long the sound plays, in seconds (0 with none).</summary>
        public float Length
        {
            get
            {
                AudioClip playing = Playing;
                return playing != null ? playing.length : 0f;
            }
        }

        /// <summary>The source it plays through.</summary>
        public AudioSource Source => GetComponent<AudioSource>();

        // The clip a play plays: its own where it sets the source, else the source's.
        AudioClip Playing
        {
            get
            {
                if (setSource) return clip;
                AudioSource source = Source;
                return source != null ? source.clip : null;
            }
        }

        /// <summary>Play the sound from its start (what switching it on does).</summary>
        /// <returns>Whether there is a sound to play.</returns>
        public bool Play()
        {
            AudioSource source = Source;
            source.playOnAwake = false;
            if (setSource)
            {
                source.clip = clip;
                source.loop = loop;
                source.spatialBlend = spatialBlend;
            }
            if (source.clip == null) return false;
            source.Play();
            return true;
        }

        /// <summary>Stop the sound (what switching it off does).</summary>
        public void Stop()
        {
            AudioSource source = Source;
            if (source != null) source.Stop();
        }

        void OnEnable() => Play();

        void OnDisable() => Stop();

        // Added in the editor: its source waits for it, never playing as the scene starts (Unity's default plays it on
        // awake, its clip with it where Set Source is off).
        void Reset()
        {
            if (TryGetComponent(out AudioSource source)) source.playOnAwake = false;
        }
    }
}
