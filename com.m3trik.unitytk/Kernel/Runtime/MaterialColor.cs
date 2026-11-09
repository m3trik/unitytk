using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// A colour as a material stores it, for every writer of one (the kernel's
    /// <see cref="MaterialWriter"/>, the render effects' colour fields). Colours are
    /// linear in a delivery and its records, and sRGB-encoded in a material: Unity
    /// converts a Color property to linear on upload in a Linear project and shows it
    /// as is in a Gamma one, so each is written as its <c>.gamma</c> in either colour
    /// space, a component above 1 (an HDR emission) included.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>Color.gamma</c> is the encoding itself; this keeps the
    /// alpha out of it (alpha is never encoded) and gives every writer one call.
    /// </remarks>
    public static class MaterialColor
    {
        /// <summary>
        /// <paramref name="linear"/> as a material stores it: sRGB-encoded, a
        /// component above 1 kept, the alpha as given (alpha is never encoded).
        /// </summary>
        public static Color Encode(Color linear)
        {
            var c = linear.gamma;
            c.a = linear.a;
            return c;
        }

        /// <summary>
        /// <paramref name="stored"/>, a colour as a material (or a colour field a person
        /// picks) stores it, back in linear terms: <see cref="Encode"/>'s inverse, the
        /// alpha as given.
        /// </summary>
        public static Color Decode(Color stored)
        {
            var c = stored.linear;
            c.a = stored.a;
            return c;
        }
    }
}
