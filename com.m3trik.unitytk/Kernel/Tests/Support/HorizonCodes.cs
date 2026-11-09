using System.Collections.Generic;

namespace M3trik.Unitytk.Testing
{
    /// <summary>
    /// A packed horizon map's stored codes, as the shadow conformance's <c>texel</c>
    /// tolerance reads them. Engine-free: the Edit Mode tests and <c>models_dotnet</c>
    /// (which links this file) compare maps the same way.
    /// </summary>
    public static class HorizonCodes
    {
        /// <summary>
        /// Every stored code of two RGBA8 images of one layout (<paramref name="size"/> texels a
        /// tile, <paramref name="spans"/> span tiles then the aux and pyramid tiles): a 16-bit pair
        /// (the span tiles' RG and BA, the aux tile's RG, the pyramid tile's left-half BA) as one
        /// code, every other channel as its 8-bit one.
        /// </summary>
        public static IEnumerable<(int x, int y, string channel, int a, int b)> Of(byte[] a, byte[] b, int size, int spans)
        {
            int S = size, K = spans;
            int width = S * (K + 2);
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int t = (y * width + x) * 4;
                    int tile = x / S, xr = x % S;
                    bool rg16 = tile <= K;
                    bool ba16 = tile < K || (tile == K + 1 && xr < S / 2);
                    if (rg16) yield return (x, y, "RG", a[t] * 256 + a[t + 1], b[t] * 256 + b[t + 1]);
                    else
                    {
                        yield return (x, y, "R", a[t], b[t]);
                        yield return (x, y, "G", a[t + 1], b[t + 1]);
                    }
                    if (ba16) yield return (x, y, "BA", a[t + 2] * 256 + a[t + 3], b[t + 2] * 256 + b[t + 3]);
                    else
                    {
                        yield return (x, y, "B", a[t + 2], b[t + 2]);
                        yield return (x, y, "A", a[t + 3], b[t + 3]);
                    }
                }
            }
        }
    }
}
