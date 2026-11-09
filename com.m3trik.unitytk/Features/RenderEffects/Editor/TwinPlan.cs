using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// Which twins a hierarchy's render effects can ask for, each once: per material,
    /// every state the effects over its renderers can combine to (a Fade's blend, a
    /// Highlight's glow, a track's emission, alone or together), in the order first met.
    /// The importer and <see cref="RenderEffectsActions.PrepareMaterials"/> make a
    /// template for each, so the runtime only instances them.
    /// </summary>
    internal static class TwinPlan
    {
        /// <summary>
        /// Every (material, state) the effects at or under <paramref name="root"/> can ask
        /// for: a renderer under a Fade or a Highlight (enabled or not: a clip may enable
        /// it), or wearing a material an <see cref="EmissionTrack"/> in its scope animates.
        /// </summary>
        internal static List<(Material material, TwinState state)> Needed(Transform root)
        {
            var tracks = Tracks(root);
            var needed = new List<(Material, TwinState)>();
            var seen = new HashSet<(Material, TwinState)>();
            foreach (Renderer renderer in Renderers(root))
            {
                TwinState over = 0;
                for (Transform t = renderer.transform; t != null; t = t.parent)
                {
                    if (t.GetComponent<Fade>() != null) over |= TwinState.Blended;
                    if (t.GetComponent<Highlight>() != null) over |= TwinState.Glowing;
                }
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    TwinState all = over | (Tracked(tracks, material, renderer.transform) ? TwinState.Emitting : 0);
                    for (int sub = (int)all; sub > 0; sub = (sub - 1) & (int)all)
                    {
                        TwinState state = MaterialWriter.Normalized((TwinState)sub);
                        if (seen.Add((material, state))) needed.Add((material, state));
                    }
                }
            }
            return needed;
        }

        /// <summary>
        /// The tracks in <paramref name="root"/>'s hierarchy whose scope meets it (holds it, or
        /// lies under it): those that may animate a material its renderers wear.
        /// </summary>
        internal static List<EmissionTrack> Tracks(Transform root)
        {
            var tracks = new List<EmissionTrack>();
            foreach (EmissionTrack track in root.root.GetComponentsInChildren<EmissionTrack>(true))
            {
                Transform scope = RenderEffect.ScopeOf(track);
                if (root.IsChildOf(scope) || scope.IsChildOf(root)) tracks.Add(track);
            }
            return tracks;
        }

        // Whether a track animates material on the renderer at node: one whose scope holds it.
        static bool Tracked(List<EmissionTrack> tracks, Material material, Transform node)
        {
            foreach (EmissionTrack track in tracks)
                if (track.material == material && node.IsChildOf(RenderEffect.ScopeOf(track))) return true;
            return false;
        }

        // Every renderer under a Fade or Highlight at or under the root, and every one in a
        // track's scope wearing its material.
        static List<Renderer> Renderers(Transform root)
        {
            var renderers = new List<Renderer>();
            var met = new HashSet<Renderer>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                if (renderer.GetComponentInParent<SubtreeEffect>(true) != null && met.Add(renderer)) renderers.Add(renderer);
            foreach (EmissionTrack track in Tracks(root))
            {
                if (track.material == null) continue;
                // Its renderers in its scope and under the root.
                Transform scope = RenderEffect.ScopeOf(track);
                foreach (Renderer renderer in (scope.IsChildOf(root) ? scope : root).GetComponentsInChildren<Renderer>(true))
                    if (System.Array.IndexOf(renderer.sharedMaterials, track.material) >= 0 && met.Add(renderer))
                        renderers.Add(renderer);
            }
            return renderers;
        }
    }
}
