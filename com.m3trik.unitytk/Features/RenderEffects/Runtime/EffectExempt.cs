using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// What sits at or under its object is no part of the effects above it: a <see cref="Fade"/> there does not
    /// hide it and a <see cref="Highlight"/> there does not light it. For what is put under a part to point it
    /// out (an arrow, a ring): it shows while the part is hidden or fades in, and keeps its own look while the
    /// part glows. An effect on its own object, or under it, still reaches what is there.
    /// </summary>
    [AddComponentMenu("unitytk/Render Effects/Effect Exempt")]
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class EffectExempt : MonoBehaviour
    {
        /// <summary>Whether an exemption between <paramref name="node"/> and <paramref name="root"/> (above it) leaves node out of root's effects.</summary>
        /// <param name="node">A node at or under <paramref name="root"/>.</param>
        /// <param name="root">The object of the effect asking.</param>
        internal static bool Leaves(Transform node, Transform root)
        {
            for (Transform t = node; t != null && t != root; t = t.parent)
                if (On(t)) return true;
            return false;
        }

        /// <summary>Whether <paramref name="node"/> holds an exemption that is on.</summary>
        internal static bool On(Transform node) => node.TryGetComponent(out EffectExempt exempt) && exempt.enabled;

        void OnEnable() => Touch();

        void OnDisable() => Touch();

        // The effects above it cover other renderers now.
        void Touch()
        {
            for (Transform t = transform.parent; t != null; t = t.parent)
                foreach (SubtreeEffect above in t.GetComponents<SubtreeEffect>())
                    above.Touch();
        }
    }
}
