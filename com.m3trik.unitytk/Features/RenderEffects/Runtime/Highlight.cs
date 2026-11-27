using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// A glow over every renderer under this object: <see cref="colorDim"/> at an
    /// <see cref="intensity"/> of 0 to <see cref="color"/> at 1, added to each material's
    /// own uniform emission. Over a mapped emission the map is set aside while the
    /// highlight is above 0 (a map hides an added glow) and is back at rest. A
    /// <see cref="pulse"/> swings the glow between the two colours on its own, within the
    /// keyed intensity: up, held bright, down, held dim, as the DCC side keys it. The nearest
    /// Highlight above a renderer is the one it shows; a delivery's <c>highlight</c> curves
    /// are bound to <see cref="intensity"/>.
    /// </summary>
    /// <remarks>
    /// Prior art: glTF's animated <c>emissiveFactor</c> (<c>KHR_animation_pointer</c>), the
    /// open standard's animated glow; it cannot animate a texture's presence, so the
    /// <c>render_effects</c> contract states the map's rule (set aside while the glow is on,
    /// back at rest), as this follows. The pipelines' own emission slot and keyword, which
    /// the twin turns on, draw it; an emission keyed on the material itself was weighed and
    /// not adopted: the Animator writes it through a property block. Its defaults (the colour,
    /// the pulse's cadence) are pythontk's effect recipe's, generated
    /// (<see cref="EffectRecipeDefaults"/>), so a highlight added here looks like one the DCC keys.
    /// </remarks>
    [AddComponentMenu("Rendering/Highlight")]
    [DisallowMultipleComponent]
    public sealed class Highlight : SubtreeEffect
    {
        /// <summary>The glow at intensity 1 of a Highlight just added: the effect recipe's bright colour.</summary>
        public static readonly Color DefaultColor = MaterialColor.Encode(EffectRecipeDefaults.PulseBright);

        /// <summary>The glow at intensity 0 of a Highlight just added: the effect recipe's dim colour.</summary>
        internal static readonly Color DefaultColorDim = MaterialColor.Encode(EffectRecipeDefaults.PulseDim);

        /// <summary>The dim colour that goes with <paramref name="bright"/>: as far under it as the effect recipe's dim is under its bright.</summary>
        /// <param name="bright">A glow's colour at intensity 1.</param>
        public static Color DimOf(Color bright)
        {
            float under = DefaultColor.maxColorComponent > 0f ? DefaultColorDim.maxColorComponent / DefaultColor.maxColorComponent : 0f;
            return new Color(bright.r * under, bright.g * under, bright.b * under, bright.a);
        }

        [Tooltip("How strong the glow is, 0 at rest to 1 at its colour.")]
        [Range(0f, 1f)]
        public float intensity;

        [Tooltip("The glow at intensity 1, added to the emission.")]
        [ColorUsage(false, true)]
        public Color color = DefaultColor;

        [Tooltip("The glow at intensity 0 (black adds nothing): over an unmapped emission it shows " +
                 "even at rest; over a mapped one the map is back at rest.")]
        [ColorUsage(false, true)]
        public Color colorDim = DefaultColorDim;

        [Tooltip("Swing the glow between its two colours on its own, within the intensity.")]
        public bool pulse;

        [Tooltip("Pulse as long as it is on; off: one swing, then it holds steady at its colour.")]
        public bool loop = true;

        [Tooltip("One swing of the pulse, up and back down, in seconds.")]
        [Min(0.05f)]
        public float pulsePeriod = EffectRecipeDefaults.PulsePeriod;

        [Tooltip("The share of each swing spent bright, its ramps' halves included.")]
        [Range(0.01f, 0.99f)]
        public float pulseDuty = EffectRecipeDefaults.PulseDuty;

        [Tooltip("The share of each swing each ramp (up, down) takes.")]
        [Range(0f, 0.5f)]
        public float pulseRamp = EffectRecipeDefaults.PulseRamp;

        [Tooltip("Pulse on real time, unpaused by the game's time scale (a paused game still pulses); off: game time.")]
        public bool unscaledTime;

        float _started;

        float Clock => !Application.isPlaying ? Time.realtimeSinceStartup : unscaledTime ? Time.unscaledTime : Time.time;

        /// <summary>
        /// The intensity shown now: <see cref="intensity"/>, swung from 0 up to it and back each
        /// <see cref="pulsePeriod"/> while <see cref="pulse"/> is on, from when it was switched on.
        /// </summary>
        public float Current => CurrentAt(Clock - _started);

        /// <summary>
        /// The intensity shown <paramref name="seconds"/> after it was switched on: rising over a
        /// ramp, held bright, falling over a ramp, held dim, then again.
        /// </summary>
        internal float CurrentAt(float seconds)
        {
            float i = Mathf.Clamp01(intensity);
            if (!pulse || pulsePeriod <= 0f) return i;
            float period = Mathf.Max(0.05f, pulsePeriod);
            float ramp = Mathf.Clamp(pulseRamp, 0f, 0.5f) * period;
            float bright = Mathf.Clamp(pulseDuty, 0f, 1f) * period;
            // Up, held, down within one swing: a duty and ramp past it would cut the fall short (a jump to dim).
            float hold = Mathf.Clamp(bright - ramp, 0f, period - 2f * ramp);
            if (!loop && seconds >= period) return i;  // its one swing done: steady
            float t = Mathf.Repeat(Mathf.Max(0f, seconds), period);
            float wave;
            if (t < ramp) wave = t / ramp;
            else if (t < ramp + hold) wave = 1f;
            else if (t < ramp + hold + ramp) wave = 1f - (t - ramp - hold) / ramp;
            else wave = 0f;
            return i * wave;
        }

        /// <summary>Whether it glows over a mapped emission: its intensity is above 0.</summary>
        public bool On => intensity > 0f;

        /// <summary>Whether its low stop shows over an unmapped emission at rest: it is not black.</summary>
        internal bool DimLit => colorDim.r > 0f || colorDim.g > 0f || colorDim.b > 0f;

        /// <summary>The glow added now, linear RGB: between the two colours by <see cref="Current"/>.</summary>
        public Color Glow
        {
            get
            {
                Color glow = Color.Lerp(MaterialColor.Decode(colorDim), MaterialColor.Decode(color), Current);
                glow.a = 1f;
                return glow;
            }
        }

        /// <inheritdoc/>
        protected override void OnEnable()
        {
            _started = Clock;
            base.OnEnable();
        }
    }
}
