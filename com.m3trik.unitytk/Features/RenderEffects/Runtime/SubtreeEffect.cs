using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// An effect on a subtree root, over every renderer under it: <see cref="Fade"/> and
    /// <see cref="Highlight"/>. The outermost enabled one in a hierarchy (on its node, the
    /// Fade before the Highlight) is the root that writes every renderer under it; one
    /// nested under it only holds its values, which the root reads: a nested Fade's alpha
    /// multiplies its parents', and the nearest Highlight above a renderer is the one it shows.
    /// </summary>
    public abstract class SubtreeEffect : RenderEffect
    {
        /// <summary>
        /// The root writing the renderers under <paramref name="node"/>: on the topmost node
        /// at or above it holding an enabled Fade or Highlight, its Fade, else its Highlight;
        /// null when there is none.
        /// </summary>
        internal static SubtreeEffect RootAbove(Transform node)
        {
            SubtreeEffect root = null;
            for (Transform t = node; t != null; t = t.parent)
            {
                SubtreeEffect here = OnNode(t);
                if (here != null) root = here;
            }
            return root;
        }

        /// <summary>The enabled Fade on <paramref name="node"/>, else its enabled Highlight, else null.</summary>
        internal static SubtreeEffect OnNode(Transform node)
        {
            Fade fade = node.GetComponent<Fade>();
            if (fade != null && fade.Live) return fade;
            Highlight highlight = node.GetComponent<Highlight>();
            return highlight != null && highlight.Live ? highlight : null;
        }

        /// <summary>Whether an enabled Fade or Highlight at or above <paramref name="node"/> covers it.</summary>
        internal static bool Covered(Transform node) => RootAbove(node) != null;

        internal override IEnumerable<RenderEffect> Writers()
        {
            SubtreeEffect root = RootAbove(transform);
            if (root != null) yield return root;
        }

        internal override bool WriteNow()
        {
            bool rebuilt = dirty;
            if (dirty) Rebuild();
            return writer != null ? writer.Write() | rebuilt : rebuilt;
        }

        void Rebuild()
        {
            if (RootAbove(transform) != this)
            {
                // Nested, or off: its root writes what it covers.
                if (writer != null) Release();
                dirty = false;
                return;
            }
            // Its own renderers first held by an effect it now covers, or by a track: given back.
            foreach (SubtreeEffect nested in GetComponentsInChildren<SubtreeEffect>(true))
                if (nested != this && nested.writer != null) nested.Release();
            // Every leading track whose scope meets this subtree: each slot takes the one whose
            // scope holds its renderer.
            var tracks = new List<EmissionTrack>();
            foreach (EmissionTrack track in transform.root.GetComponentsInChildren<EmissionTrack>(true))
            {
                if (!track.Live || track.material == null || !track.Leads()) continue;
                Transform scope = ScopeOf(track);
                if (!transform.IsChildOf(scope) && !scope.IsChildOf(transform)) continue;
                if (track.writer != null && track.writer.HoldsUnder(transform)) track.Release();
                tracks.Add(track);
            }
            writer?.Release();
            writer = new EffectWriter();
            writer.ScanSubtree(this, tracks);
            dirty = false;
        }

        internal override void Touch()
        {
            dirty = true;
            for (Transform t = transform.parent; t != null; t = t.parent)
                foreach (SubtreeEffect above in t.GetComponents<SubtreeEffect>())
                    above.dirty = true;
            foreach (SubtreeEffect below in GetComponentsInChildren<SubtreeEffect>(true))
                below.dirty = true;
            foreach (EmissionTrack track in transform.root.GetComponentsInChildren<EmissionTrack>(true))
                track.dirty = true;
        }
    }
}
