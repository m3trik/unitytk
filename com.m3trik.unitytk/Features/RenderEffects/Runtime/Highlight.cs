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
    /// keyed intensity. The nearest Highlight above a renderer is the one it shows; a
    /// delivery's <c>highlight</c> curves are bound to <see cref="intensity"/>.
    /// </summary>
    /// <remarks>
    /// Prior art: glTF's animated <c>emissiveFactor</c> (<c>KHR_animation_pointer</c>), the
    /// open standard's animated glow; it cannot animate a texture's presence, so the
    /// <c>render_effects</c> contract states the map's rule (set aside while the glow is on,
    /// back at rest), as this follows. The pipelines' own emission slot and keyword, which
    /// the twin turns on, draw it; an emission keyed on the material itself was weighed and
    /// not adopted: the Animator writes it through a property block.
    /// </remarks>
    [AddComponentMenu("Rendering/Highlight")]
    [DisallowMultipleComponent]
    public sealed class Highlight : SubtreeEffect
    {
        /// <summary>The glow at intensity 1 of a Highlight just added: white.</summary>
        internal static readonly Color DefaultColor = Color.white;

        [Tooltip("How strong the glow is, 0 at rest to 1 at its colour.")]
        [Range(0f, 1f)]
        public float intensity;

        [Tooltip("The glow at intensity 1, added to the emission.")]
        [ColorUsage(false, true)]
        public Color color = DefaultColor;

        [Tooltip("The glow at intensity 0 (black adds nothing): over an unmapped emission it shows " +
                 "even at rest; over a mapped one the map is back at rest.")]
        [ColorUsage(false, true)]
        public Color colorDim = Color.black;

        [Tooltip("Swing the glow between its two colours on its own, within the intensity.")]
        public bool pulse;

        [Tooltip("The pulse's swings per second.")]
        [Min(0f)]
        public float pulseRate = 1f;

        /// <summary>
        /// The intensity shown now: <see cref="intensity"/>, swung from 0 to it and back
        /// <see cref="pulseRate"/> times a second while <see cref="pulse"/> is on.
        /// </summary>
        public float Current => CurrentAt(Application.isPlaying ? Time.time : Time.realtimeSinceStartup);

        /// <summary>The intensity shown at <paramref name="clock"/> seconds (<see cref="Current"/>'s, at a given time).</summary>
        internal float CurrentAt(float clock)
        {
            float i = Mathf.Clamp01(intensity);
            if (!pulse || pulseRate <= 0f) return i;
            return i * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * pulseRate * clock));
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
    }
}
