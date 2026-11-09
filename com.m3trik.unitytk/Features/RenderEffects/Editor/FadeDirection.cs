namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// Which way a fade <see cref="RenderEffectsActions.KeyFade"/> keys runs:
    /// <see cref="Out"/> from shown to hidden, <see cref="In"/> back.
    /// </summary>
    public enum FadeDirection
    {
        /// <summary>From alpha 1 to 0.</summary>
        Out,
        /// <summary>From alpha 0 to 1.</summary>
        In,
    }
}
