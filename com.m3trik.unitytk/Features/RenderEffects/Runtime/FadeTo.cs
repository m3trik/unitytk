using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// Fades its object's <see cref="Fade"/> to <see cref="alpha"/> over <see cref="seconds"/>
    /// each time it is enabled (a sequence's step enabling it as it starts or ends: "the lamp
    /// fades away"), then holds there. Several may sit on one object (a fade in, a fade out), each
    /// enabled by its own moment.
    /// </summary>
    /// <remarks>
    /// Prior art: a keyed fade (<see cref="RenderEffectsActions.KeyFade"/>) plays at a time
    /// in a clip; a fade a step asks for runs when the step gets there, which no clip
    /// knows, so it runs on its own clock. The Fade stays the one that shows the opacity.
    /// </remarks>
    [AddComponentMenu("Rendering/Fade To")]
    [RequireComponent(typeof(Fade))]
    public sealed class FadeTo : MonoBehaviour
    {
        /// <summary>A fade's length by default: the effect recipe's (<c>fade_frames</c> at its reference rate), as a delivery keys one.</summary>
        public const float DefaultSeconds = EffectRecipeDefaults.FadeFrames / EffectRecipeDefaults.ReferenceFps;

        [Tooltip("The opacity it fades to: 0 hidden, 1 as authored.")]
        [Range(0f, 1f)]
        public float alpha;

        [Tooltip("How long the fade takes, in seconds (0: at once).")]
        [Min(0f)]
        public float seconds = DefaultSeconds;

        [Tooltip("Run on real time, unpaused by the game's time scale (a paused game still fades); off: game time.")]
        public bool unscaledTime;

        Fade _fade;
        float _from;
        float _elapsed;

        /// <summary>Whether the fade is under way (started, its opacity not reached).</summary>
        public bool Running { get; private set; }

        /// <summary>Start the fade from where the Fade stands now (what enabling it does).</summary>
        public void Play()
        {
            _fade = GetComponent<Fade>();
            if (_fade == null) return;
            _fade.enabled = true;
            _from = _fade.alpha;
            _elapsed = 0f;
            Running = true;
            Advance(0f);
        }

        /// <summary>Move the fade <paramref name="delta"/> seconds on (a player calls it each frame).</summary>
        /// <param name="delta">Seconds (not below 0).</param>
        public void Advance(float delta)
        {
            if (!Running || _fade == null) return;
            _elapsed += Mathf.Max(0f, delta);
            float t = seconds > 0f ? Mathf.Clamp01(_elapsed / seconds) : 1f;
            _fade.alpha = Mathf.Lerp(_from, alpha, t);
            if (t >= 1f) Running = false;
        }

        void OnEnable() => Play();

        void Update() => Advance(unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);
    }
}
