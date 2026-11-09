// ----------------------------------------------------------------------------
// The RenderEffects feature's runtime part (M3trik.Unitytk.RenderEffects): the
// components a person keys in the Animation window, or the importer fills from
// a delivery's render_effects record, and the one writer that puts their twins
// on the renderers they cover. It reaches the player: the clips bind its fields.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("M3trik.Unitytk.RenderEffects.Editor")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.RenderEffects.Tests")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.RenderEffects.PlayModeTests")]

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// What every render effect shares: the twins prepared for it
    /// (<see cref="MaterialTwins"/>), and its part in the one writer. A
    /// <see cref="Fade"/> or <see cref="Highlight"/> covers the renderers under it and
    /// an <see cref="EmissionTrack"/> a material in its scope; the outermost enabled
    /// Fade or Highlight writes every renderer under it (nested ones only hold values),
    /// and a track writes the renderers no Fade or Highlight covers. A renderer wears
    /// a twin of its material while an effect over it is active, one instance per
    /// root, material and state, shared by every renderer the root covers, and its
    /// shared original at rest; no property block, no shader of its own.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>CanvasGroup</c>, an alpha on a subtree root that nests by
    /// multiplying; Unity animates a material only through renderer bindings, which
    /// write a property block (out of the SRP Batcher), or through a material instance
    /// per renderer (no batching at all), so a root swaps its renderers onto one twin
    /// per material instead. Renderer Shader User Value was weighed and not adopted:
    /// only a shader that reads it responds, and none of the pipelines' own Lit does.
    /// </remarks>
    public abstract class RenderEffect : MonoBehaviour
    {
        [Tooltip("The twins prepared for the materials this effect changes: made at import, or by " +
                 "Tools > unitytk > Render Effects > Prepare Materials, so a player build keeps the " +
                 "shader variants they draw with. Without them a twin is made at runtime, and a " +
                 "player may lack its variant.")]
        public MaterialTwins twins;

        /// <summary>The writer while this effect writes its renderers, else null.</summary>
        internal EffectWriter writer;

        /// <summary>What it covers changed: its writer is rebuilt before the next write.</summary>
        internal bool dirty = true;

        /// <summary>
        /// Told of each effect an <see cref="Apply"/> outside play mode leaves writing: there
        /// Unity sends it no OnDisable, OnDestroy or LateUpdate, so nothing of its own gives
        /// its renderers back. The editor half's keeper sets it as it loads (after every
        /// domain reload) and gives them back before a save, a reload, a scene's close or
        /// play mode, and when the effect goes; null in a player, where every effect plays
        /// itself.
        /// </summary>
        internal static Action<RenderEffect> editModeKeeper;

        /// <summary>
        /// Put the current values on the renderers now instead of at the end of the frame:
        /// for a script that sets a value and needs it on screen this frame, and for code
        /// that runs outside the player loop. Cheap when nothing changed. In the editor
        /// outside play mode it previews: the renderers are given back when the effect is
        /// removed, disabled or deactivated, and before a scene is saved or closed, scripts
        /// reload or play mode starts; an effect in a prefab or model asset shows nothing
        /// (open the prefab in Prefab Mode, or place it in a scene).
        /// </summary>
        public void Apply() => Show();

        /// <summary><see cref="Apply"/>, saying whether anything on screen changed.</summary>
        internal bool Show()
        {
            bool changed = false;
            foreach (RenderEffect writing in Writers())
            {
                changed |= writing.WriteNow();
                if (writing.writer != null && !Application.isPlaying) editModeKeeper?.Invoke(writing);
            }
            return changed;
        }

        /// <summary>
        /// Look again at what it covers before the next write: for renderers added or removed
        /// deeper under it than its own children, or materials swapped on them, which Unity
        /// tells it nothing of. Its own children coming and going it notices by itself.
        /// </summary>
        public void Refresh() => Touch();

        /// <summary>The effects whose writers show this one's values: itself or its root.</summary>
        internal abstract IEnumerable<RenderEffect> Writers();

        /// <summary>Rebuild if needed and write, as this frame's end does; whether anything on screen changed.</summary>
        internal abstract bool WriteNow();

        /// <summary>Give every renderer it holds its original back and free its twins.</summary>
        internal void Release()
        {
            writer?.Release();
            writer = null;
            dirty = true;
        }

        /// <summary>Its writer and those it shares renderers with are rebuilt before their next write.</summary>
        internal abstract void Touch();

        /// <summary>Whether it takes part: enabled, on an active object.</summary>
        internal bool Live => this != null && isActiveAndEnabled;

        /// <summary>Unity's: it takes part again.</summary>
        protected virtual void OnEnable() => Touch();

        /// <summary>Unity's: every renderer it held is back on its original.</summary>
        protected virtual void OnDisable()
        {
            Release();
            Touch();
        }

        /// <summary>Unity's: it moved, so what covers what may have.</summary>
        protected virtual void OnTransformParentChanged() => Touch();

        /// <summary>Unity's: renderers may have come or gone under it.</summary>
        protected virtual void OnTransformChildrenChanged() => Touch();

        /// <summary>Unity's: the end of a frame, after the Animator wrote the values.</summary>
        protected virtual void LateUpdate()
        {
            if (writer != null || dirty) WriteNow();
        }

        /// <summary>
        /// The transform a track's material is looked for under, and the one a root finds
        /// its tracks under: the nearest Animator's (a model's root), else the hierarchy's root.
        /// </summary>
        internal static Transform ScopeOf(Component component)
        {
            Animator animator = component.GetComponentInParent<Animator>(true);
            return animator != null ? animator.transform : component.transform.root;
        }

#if UNITY_EDITOR
        /// <summary>Unity's: an inspector edit lands on the next write.</summary>
        protected virtual void OnValidate()
        {
            if (writer != null) writer.Invalidate();
        }
#endif
    }
}
