using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// The opacity of every renderer under this object, as a <c>CanvasGroup</c>'s alpha
    /// is a UI subtree's: a Fade under another multiplies its parent's alpha. At 1 the
    /// renderers wear their materials as authored (opaque, sorted with the opaque pass,
    /// writing depth, casting shadows); below 1 each wears its material's blended twin,
    /// one per material shared by every renderer the root covers; at 0 they are hidden.
    /// Keyed in the Animation window (<see cref="alpha"/>), or by a delivery's
    /// <c>render_effects</c> record, whose <c>opacity</c> curves the import binds here.
    /// </summary>
    /// <remarks>
    /// Prior art: <c>CanvasGroup.alpha</c>, the nesting and the name of the field; the
    /// pipelines' own blend switches (<see cref="M3trik.Unitytk.Kernel.IRenderPipelineHost.SetBlended"/>)
    /// make the twin, as their material inspectors do.
    /// </remarks>
    [AddComponentMenu("Rendering/Fade")]
    [DisallowMultipleComponent]
    public sealed class Fade : SubtreeEffect
    {
        [Tooltip("The opacity of the renderers under this object, 0 hidden to 1 as authored; " +
                 "multiplied by every Fade above it.")]
        [Range(0f, 1f)]
        public float alpha = 1f;

        /// <summary>The opacity shown: <see cref="alpha"/> times that of every enabled Fade above it.</summary>
        public float EffectiveAlpha
        {
            get
            {
                float a = Mathf.Clamp01(alpha);
                for (Transform t = transform.parent; t != null; t = t.parent)
                {
                    Fade above = t.GetComponent<Fade>();
                    if (above != null && above.Live) a *= Mathf.Clamp01(above.alpha);
                }
                return a;
            }
        }
    }
}
