using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// An animated emission on one <see cref="material"/>: <see cref="color"/> times
    /// <see cref="strength"/> times <see cref="weight"/>, its map kept, on every renderer
    /// wearing it in this object's scope (the nearest Animator's hierarchy, else this
    /// hierarchy). While the emission differs from the material's own, those renderers
    /// wear its twin, one shared by all of them; where it equals the material's own,
    /// the shared original. A Fade or Highlight over one of them takes it into its own
    /// twin. A delivery's <c>render_effects</c> record names each material whose
    /// <c>emission</c> is animated, with its colour and strength; that curve, a weight from
    /// 0 to 1, is bound to <see cref="weight"/>, one track per material.
    /// </summary>
    /// <remarks>
    /// Prior art: glTF's animated <c>emissiveFactor</c> (<c>KHR_animation_pointer</c>, and
    /// <c>KHR_materials_emissive_strength</c> for a glow above 1), the open standard's
    /// animated emission, which colour times strength times weight states; a key on the
    /// material's own emission was weighed and not adopted: the Animator writes it through
    /// a property block.
    /// </remarks>
    [AddComponentMenu("Rendering/Emission Track")]
    [DisallowMultipleComponent]
    public sealed class EmissionTrack : RenderEffect
    {
        [Tooltip("The material whose emission this animates, on every renderer in scope wearing it.")]
        public Material material;

        [Tooltip("The emission's weight, 0 dark to 1 at its colour and strength.")]
        [Range(0f, 1f)]
        public float weight = 1f;

        [Tooltip("The emission's colour at weight 1.")]
        [ColorUsage(false, true)]
        public Color color = Color.white;

        [Tooltip("A multiplier of the colour: above 1 for a glow brighter than white.")]
        [Min(0f)]
        public float strength = 1f;

        /// <summary>The emission shown now, linear RGB: colour times strength times weight.</summary>
        public Color Emission
        {
            get
            {
                Color e = MaterialColor.Decode(color) * (Mathf.Max(0f, strength) * Mathf.Clamp01(weight));
                e.a = 1f;
                return e;
            }
        }

        // The material its writer was scanned for: a change of it rescans.
        Material _scanned;

        /// <summary>
        /// Whether it is the track its material plays in its scope: the first enabled one there
        /// animating that material (a second is said once, and left out).
        /// </summary>
        internal bool Leads()
        {
            Transform scope = ScopeOf(this);
            foreach (EmissionTrack other in scope.GetComponentsInChildren<EmissionTrack>(true))
            {
                if (other == this) return true;
                if (other.Live && other.material == material && ScopeOf(other) == scope) return false;
            }
            return true;
        }

        internal override IEnumerable<RenderEffect> Writers()
        {
            yield return this;
            foreach (SubtreeEffect root in transform.root.GetComponentsInChildren<SubtreeEffect>(true))
                if (root.writer != null || SubtreeEffect.RootAbove(root.transform) == root)
                    yield return root;
        }

        internal override bool WriteNow()
        {
            bool rebuilt = dirty;
            if (dirty)
            {
                writer?.Release();
                writer = null;
                _scanned = material;
                if (Live && material != null)
                {
                    if (Leads())
                    {
                        writer = new EffectWriter();
                        writer.ScanTrack(this, ScopeOf(this));
                    }
                    else
                    {
                        EffectWriter.Warn($"'{name}' animates '{material.name}', which an Emission Track before it in its " +
                                          "scope animates already: only the first plays");
                    }
                }
                dirty = false;
            }
            return writer != null ? writer.Write() | rebuilt : rebuilt;
        }

        internal override void Touch()
        {
            dirty = true;
            foreach (SubtreeEffect root in transform.root.GetComponentsInChildren<SubtreeEffect>(true))
                root.dirty = true;
            // Another track may lead its material now, or no longer.
            foreach (EmissionTrack other in transform.root.GetComponentsInChildren<EmissionTrack>(true))
                other.dirty = true;
        }

#if UNITY_EDITOR
        /// <summary>Unity's: another material is another scan.</summary>
        protected override void OnValidate()
        {
            base.OnValidate();
            if (material != _scanned) Touch();
        }
#endif
    }
}
