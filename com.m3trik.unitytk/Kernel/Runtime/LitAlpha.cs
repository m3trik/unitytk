namespace M3trik.Unitytk.Kernel
{
    /// <summary>How a surface's alpha renders: glTF 2.0's <c>alphaMode</c>.</summary>
    public enum LitAlpha
    {
        /// <summary>The alpha is ignored: the surface is opaque (glTF <c>OPAQUE</c>).</summary>
        Opaque,
        /// <summary>Cut where the alpha is below the cutoff, opaque elsewhere (glTF <c>MASK</c>).</summary>
        Mask,
        /// <summary>Blended over what is behind it, sorted, with no depth write (glTF <c>BLEND</c>).</summary>
        Blend,
    }
}
