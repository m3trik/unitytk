// ----------------------------------------------------------------------------
// The ShadowPlane feature's height-field map: pythontk's ``HeightFieldMap``
// (geo_utils/shadow_horizon.py) ported term for term, engine-free (its
// assembly references no UnityEngine): the packed RGBA8 image (``to_rgba``),
// its decoding (``from_rgba``) and the reference evaluation (``alpha``), the
// oracle the plane's shader is tested against.
//
// The port is pinned against the reference by the golden cases of
// ``ptk.Conformance.cases("shadow_horizon")`` under `dotnet test` in
// test/models_dotnet. Arithmetic the reference does in float32 (every
// quantisation) is done in float32 here, each step rounded by a cast; the
// march is double, as numpy's is.
// ----------------------------------------------------------------------------
using System;

namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// A baked height-field shadow map and the frame it was baked in, pythontk
    /// <c>HeightFieldMap</c>: per footprint pixel up to <see cref="Spans"/> solid
    /// vertical spans, the distance to the nearest solid column and that column's
    /// hull, and the distance from a solid pixel to the nearest empty one. Heights
    /// are above the ground plane, in frame units, already quantised as the image
    /// stores them, so a map and its image round trip evaluate alike. <c>NaN</c>
    /// marks an absent span.
    /// </summary>
    /// <remarks>
    /// The arrays are row-major, <c>[ib, ia]</c> (row <c>ib</c> along the frame's
    /// second horizontal axis, column <c>ia</c> along its first); the spans add a
    /// leading <c>k</c>: <c>lo[(k * S + ib) * S + ia]</c>.
    /// Prior art: Unity draws the shadow of the meshes a light sees (shadow maps,
    /// URP's decal projector); none ships a baked height field a ground plane can
    /// march for any source position, and the deliverable's other readers (the
    /// WebXR viewer, the DCC previews) evaluate pythontk's, so the reference is
    /// ported and held to its golden cases.
    /// </remarks>
    public sealed class HeightFieldMap
    {
        /// <summary>The 16-bit range a height or a distance is quantised over (<c>MAX16</c>).</summary>
        public const float Max16 = 65535.0f;

        /// <summary>Fixed-point scale of the distance field: 1/64 of a footprint pixel (<c>DIST_SCALE</c>).</summary>
        public const float DistScale = 64.0f;

        /// <summary>The distance an empty map reports everywhere, the field's largest value (<c>DIST_FAR</c>).</summary>
        public const float DistFar = Max16 / DistScale;

        /// <summary>Fixed-point scale of the interior distance field: 1/4 of a pixel, 8-bit (<c>INTERIOR_SCALE</c>).</summary>
        public const float InteriorScale = 4.0f;

        /// <summary>The deepest interior distance the field holds, pixels (<c>INTERIOR_FAR</c>).</summary>
        public const float InteriorFar = 255.0f / InteriorScale;

        /// <summary>
        /// The texel (pyramid tile, <c>(S/2, S/2)</c>) that says a map carries the
        /// interior field: "SH", version 1, 0xA5 (<c>INTERIOR_MARKER</c>).
        /// </summary>
        public static readonly byte[] InteriorMarker = { 83, 72, 1, 165 };

        readonly float[] _lo, _hi, _dist, _nearLo, _nearHi, _interior;

        /// <summary>
        /// A map from its fields, as <see cref="ShadowHorizonBaker.Bake"/> and
        /// <see cref="FromRgba"/> make it.
        /// </summary>
        /// <param name="size">Footprint pixels per side, <c>S</c>, a power of two of at least 2.</param>
        /// <param name="spans">Solid spans per column, <c>K</c>, at least 1.</param>
        /// <param name="bounds"><c>(a0, a1, b0, b1)</c> in the frame.</param>
        /// <param name="ground">Height of the ground plane along <paramref name="up"/> in the frame.</param>
        /// <param name="up">The vertical axis index: 1 Y-up, 2 Z-up.</param>
        /// <param name="heightScale">The height a 16-bit value of 65535 stands for.</param>
        /// <param name="lo">The span bottoms, <c>K x S x S</c>.</param>
        /// <param name="hi">The span tops, <c>K x S x S</c>.</param>
        /// <param name="dist">The distance to the nearest column, in the smaller pitch, <c>S x S</c>.</param>
        /// <param name="nearLo">That column's hull bottom.</param>
        /// <param name="nearHi">That column's hull top.</param>
        /// <param name="interior">The distance from a solid pixel to the nearest empty one, or null (a map baked before it).</param>
        /// <exception cref="ArgumentException">A size, spans count, bounds or field of the wrong shape.</exception>
        public HeightFieldMap(int size, int spans, double[] bounds, double ground, int up, double heightScale,
            float[] lo, float[] hi, float[] dist, float[] nearLo, float[] nearHi, float[] interior)
        {
            if (size < 2 || (size & (size - 1)) != 0 || spans < 1)
                throw new ArgumentException(
                    $"HeightFieldMap: size must be a power of two and spans at least 1; got size {size}, spans {spans}.");
            if (bounds == null || bounds.Length != 4)
                throw new ArgumentException("HeightFieldMap: bounds must be (a0, a1, b0, b1).", nameof(bounds));
            if (up < 0 || up > 2)
                throw new ArgumentException($"HeightFieldMap: up must be an axis index 0..2; got {up}.", nameof(up));
            int n = size * size;
            Need(lo, spans * n, nameof(lo));
            Need(hi, spans * n, nameof(hi));
            Need(dist, n, nameof(dist));
            Need(nearLo, n, nameof(nearLo));
            Need(nearHi, n, nameof(nearHi));
            if (interior != null) Need(interior, n, nameof(interior));
            Size = size;
            Spans = spans;
            A0 = bounds[0];
            A1 = bounds[1];
            B0 = bounds[2];
            B1 = bounds[3];
            Ground = ground;
            Up = up;
            HeightScale = heightScale;
            _lo = lo;
            _hi = hi;
            _dist = dist;
            _nearLo = nearLo;
            _nearHi = nearHi;
            _interior = interior;
        }

        static void Need(float[] field, int length, string name)
        {
            if (field == null || field.Length != length)
                throw new ArgumentException($"HeightFieldMap: {name} must hold {length} values.", name);
        }

        // ------------------------------------------------------------ layout

        /// <summary>Footprint pixels per side, <c>S</c>.</summary>
        public int Size { get; }

        /// <summary>Solid spans per column, <c>K</c>.</summary>
        public int Spans { get; }

        /// <summary>The footprint along the frame's first horizontal axis.</summary>
        public double A0 { get; }

        /// <summary>The footprint's far edge along the first horizontal axis.</summary>
        public double A1 { get; }

        /// <summary>The footprint along the frame's second horizontal axis (row 0 of the image).</summary>
        public double B0 { get; }

        /// <summary>The footprint's far edge along the second horizontal axis.</summary>
        public double B1 { get; }

        /// <summary>Height of the ground plane along <see cref="Up"/> in the frame.</summary>
        public double Ground { get; }

        /// <summary>The vertical axis index: 1 Y-up, 2 Z-up.</summary>
        public int Up { get; }

        /// <summary>The height a 16-bit channel value of 65535 stands for.</summary>
        public double HeightScale { get; }

        /// <summary><c>(a0, a1, b0, b1)</c>, a fresh array.</summary>
        public double[] Bounds => new[] { A0, A1, B0, B1 };

        /// <summary>Pyramid levels above level 0: <c>log2(S)</c> (the top is one cell).</summary>
        public int Levels
        {
            get
            {
                int levels = 0;
                for (int s = Size; s > 1; s >>= 1) levels++;
                return levels;
            }
        }

        /// <summary>Tiles across the image: <c>K</c> spans, the aux tile, the pyramid.</summary>
        public int Tiles => Spans + 2;

        /// <summary>The image's width in texels, <c>S (K + 2)</c>.</summary>
        public int Width => Size * Tiles;

        /// <summary>The image's height in texels, <c>S</c>.</summary>
        public int Height => Size;

        /// <summary>A footprint pixel's extent along the first horizontal axis.</summary>
        public double PixelA => (A1 - A0) / Size;

        /// <summary>A footprint pixel's extent along the second horizontal axis.</summary>
        public double PixelB => (B1 - B0) / Size;

        /// <summary>
        /// The larger pixel pitch over the smaller, at least 1: the distance fields
        /// are measured in the smaller pitch.
        /// </summary>
        public double Aspect
        {
            get
            {
                double pw = PixelA, ph = PixelB;
                double lo = Math.Min(pw, ph), hi = Math.Max(pw, ph);
                return lo > 1e-12 ? hi / lo : 1.0;
            }
        }

        /// <summary>The span bottoms, <c>K x S x S</c> (<c>NaN</c> where absent).</summary>
        public float[] Lo => _lo;

        /// <summary>The span tops, <c>K x S x S</c>.</summary>
        public float[] Hi => _hi;

        /// <summary>The distance to the nearest solid column, in the smaller pitch, <c>S x S</c>.</summary>
        public float[] Dist => _dist;

        /// <summary>The nearest column's hull bottom (a solid pixel's own), <c>S x S</c>.</summary>
        public float[] NearLo => _nearLo;

        /// <summary>The nearest column's hull top, <c>S x S</c>.</summary>
        public float[] NearHi => _nearHi;

        /// <summary>The distance from a solid pixel to the nearest empty one, or null on a map baked before it.</summary>
        public float[] Interior => _interior;

        // ------------------------------------------------------------ pyramid

        /// <summary>
        /// Per level <c>1 .. Levels</c>, the cells' hull <c>(lo, hi)</c> and least
        /// distance over <c>n x n</c> cells of <c>2^level</c> pixels
        /// (<c>HeightFieldMap.pyramid</c>): the hull spans the nearest column of
        /// each of the cell's pixels and of their one-pixel ring; <c>NaN</c> where
        /// the cell holds no column.
        /// </summary>
        public (float[] lo, float[] hi, float[] dist)[] Pyramid()
        {
            int S = Size;
            float[] lo = Dilate(_nearLo, S, true);
            float[] hi = Dilate(_nearHi, S, false);
            float[] dist = _dist;
            var levels = new (float[], float[], float[])[Levels];
            int w = S, level = 0;
            while (w > 1)
            {
                int n = w / 2;
                var lo2 = new float[n * n];
                var hi2 = new float[n * n];
                var d2 = new float[n * n];
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int p00 = (2 * j) * w + 2 * i, p01 = p00 + 1, p10 = p00 + w, p11 = p10 + 1;
                        lo2[j * n + i] = FMin(FMin(FMin(lo[p00], lo[p01]), lo[p10]), lo[p11]);
                        hi2[j * n + i] = FMax(FMax(FMax(hi[p00], hi[p01]), hi[p10]), hi[p11]);
                        d2[j * n + i] = Math.Min(Math.Min(Math.Min(dist[p00], dist[p01]), dist[p10]), dist[p11]);
                    }
                }
                var outLo = new float[n * n];
                var outHi = new float[n * n];
                for (int c = 0; c < n * n; c++)
                {
                    bool column = d2[c] <= 0.0f;
                    outLo[c] = column ? lo2[c] : float.NaN;
                    outHi[c] = column ? hi2[c] : float.NaN;
                }
                levels[level++] = (outLo, outHi, d2);
                lo = lo2;
                hi = hi2;
                dist = d2;
                w = n;
            }
            return levels;
        }

        /// <summary><paramref name="field"/> reduced over each pixel's 3 x 3 neighbourhood, a NaN ignored (kept where all are).</summary>
        static float[] Dilate(float[] field, int S, bool min)
        {
            var output = new float[S * S];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float v = float.NaN;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= S) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= S) continue;
                            float f = field[yy * S + xx];
                            v = min ? FMin(v, f) : FMax(v, f);
                        }
                    }
                    output[y * S + x] = v;
                }
            }
            return output;
        }

        /// <summary>numpy's <c>fmin</c>: the lesser, a NaN ignored.</summary>
        internal static float FMin(float a, float b) => float.IsNaN(a) ? b : float.IsNaN(b) ? a : (b < a ? b : a);

        /// <summary>numpy's <c>fmax</c>: the greater, a NaN ignored.</summary>
        internal static float FMax(float a, float b) => float.IsNaN(a) ? b : float.IsNaN(b) ? a : (b > a ? b : a);

        // ------------------------------------------------------------ encoding

        /// <summary>
        /// The map as one RGBA8 image, <see cref="Width"/> x <see cref="Height"/>,
        /// four bytes per texel, rows top first as the PNG holds them (row 0 is
        /// <c>ib = 0</c>, the frame's <c>b0</c> edge; a <c>Texture2D</c> filled
        /// from it bottom row first takes the rows reversed): pythontk
        /// <c>HeightFieldMap.to_rgba</c>, byte for byte. Span tile <c>k</c>:
        /// <c>R, G</c> the 16-bit bottom, <c>B, A</c> the 16-bit top (all zero when
        /// absent). Aux tile: <c>R, G</c> the 16-bit distance in 1/64 pixel,
        /// <c>B</c> the nearest hull's top and <c>A</c> its bottom, 8-bit. Pyramid
        /// tile: level <c>l</c> in <c>S &gt;&gt; l</c> rows from row
        /// <c>S - (S &gt;&gt; (l - 1))</c>, <c>R</c> the hull's floor, <c>G</c> its
        /// ceiling (0 = no column), <c>B, A</c> the least distance; its right half
        /// the interior field, four pixels per texel, under
        /// <see cref="InteriorMarker"/> at <c>(S/2, S/2)</c>.
        /// </summary>
        public byte[] ToRgba()
        {
            int S = Size, K = Spans, W = Width;
            float hs = (float)HeightScale;
            var img = new byte[S * W * 4];
            for (int k = 0; k < K; k++)
            {
                for (int y = 0; y < S; y++)
                {
                    for (int x = 0; x < S; x++)
                    {
                        int p = (k * S + y) * S + x;
                        float lo = _lo[p], hi = _hi[p];
                        float lo16 = float.IsNaN(lo) ? 0.0f : (float)((float)(lo / hs) * Max16);
                        float hi16 = float.IsNaN(hi) ? 0.0f : (float)((float)(hi / hs) * Max16);
                        int t = Texel(W, y, k * S + x);
                        Split16(lo16, img, t);
                        Split16(hi16, img, t + 2);
                    }
                }
            }
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    int p = y * S + x;
                    int t = Texel(W, y, K * S + x);
                    Split16((float)(_dist[p] * DistScale), img, t);
                    img[t + 2] = Byte8(Round((float)((float)(_nearHi[p] / hs) * 255.0f)), 0.0f);
                    img[t + 3] = Byte8(Round((float)((float)(_nearLo[p] / hs) * 255.0f)), 0.0f);
                }
            }
            int pyr = (K + 1) * S;
            var levels = Pyramid();
            for (int level = 1; level <= levels.Length; level++)
            {
                var (lo, hi, dist) = levels[level - 1];
                int n = S >> level;
                int y0 = S - (S >> (level - 1));
                for (int j = 0; j < n; j++)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int c = j * n + i;
                        int t = Texel(W, y0 + j, pyr + i);
                        bool empty = float.IsNaN(hi[c]);
                        float l = float.IsNaN(lo[c]) ? 0.0f : lo[c];
                        float h = float.IsNaN(hi[c]) ? 0.0f : hi[c];
                        img[t] = empty ? (byte)0 : Byte8((float)Math.Floor((float)((float)(l / hs) * 255.0f)), 0.0f);
                        img[t + 1] = empty ? (byte)0 : Byte8((float)Math.Ceiling((float)((float)(h / hs) * 255.0f)), 1.0f);
                        Split16((float)(dist[c] * DistScale), img, t + 2);
                    }
                }
            }
            if (_interior != null)
            {
                int h = S / 2;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < h; x++)
                    {
                        int t = Texel(W, y, pyr + h + x);
                        img[t] = InteriorCode(2 * y, 2 * x);
                        img[t + 1] = InteriorCode(2 * y, 2 * x + 1);
                        img[t + 2] = InteriorCode(2 * y + 1, 2 * x);
                        img[t + 3] = InteriorCode(2 * y + 1, 2 * x + 1);
                    }
                }
                int m = Texel(W, h, pyr + h);
                for (int c = 0; c < 4; c++) img[m + c] = InteriorMarker[c];
            }
            return img;
        }

        byte InteriorCode(int y, int x) => Byte8(Round((float)(_interior[y * Size + x] * InteriorScale)), 0.0f);

        static int Texel(int width, int row, int column) => (row * width + column) * 4;

        /// <summary>numpy's <c>round</c> on a float32: to the nearest, halves to even.</summary>
        static float Round(float v) => (float)Math.Round((double)v, MidpointRounding.ToEven);

        /// <summary>An 8-bit code: <paramref name="v"/> (already whole) clipped to [floor, 255].</summary>
        static byte Byte8(float v, float floor) => (byte)Math.Min(Math.Max(v, floor), 255.0f);

        /// <summary><paramref name="value"/> rounded, clipped to 16 bits and written high byte first.</summary>
        static void Split16(float value, byte[] img, int at)
        {
            float r = Math.Min(Math.Max(Round(value), 0.0f), Max16);
            int v = (int)r;
            img[at] = (byte)(v / 256);
            img[at + 1] = (byte)(v % 256);
        }

        /// <summary>
        /// A map decoded from the image <see cref="ToRgba"/> wrote (rows top first)
        /// or a copy of it, pythontk <c>HeightFieldMap.from_rgba</c>; the pyramid is
        /// re-derived, never trusted. A larger image is read from its top-left.
        /// </summary>
        /// <param name="rgba">The image, four bytes per texel, rows top first.</param>
        /// <param name="width">The image's width in texels.</param>
        /// <param name="height">The image's height in texels.</param>
        /// <param name="size">The layout's footprint pixels per side.</param>
        /// <param name="spans">The layout's spans per column.</param>
        /// <param name="bounds"><c>(a0, a1, b0, b1)</c>.</param>
        /// <param name="ground">The ground plane's height along <paramref name="up"/>.</param>
        /// <param name="up">The vertical axis index.</param>
        /// <param name="heightScale">The height 65535 stands for.</param>
        /// <exception cref="ArgumentException">The layout is not a power-of-two size with a span, or the image is too small for it.</exception>
        public static HeightFieldMap FromRgba(byte[] rgba, int width, int height, int size, int spans,
            double[] bounds, double ground, int up, double heightScale)
        {
            int S = size, K = spans;
            if (rgba == null || width < 0 || height < 0 || rgba.Length < (long)width * height * 4)
                throw new ArgumentException("HeightFieldMap.FromRgba: expected width x height RGBA texels.", nameof(rgba));
            if (S < 2 || (S & (S - 1)) != 0 || K < 1)
                throw new ArgumentException(
                    $"HeightFieldMap.FromRgba: size must be a power of two and spans at least 1 (the pyramid halves " +
                    $"the footprint down to one cell); got size {S}, spans {K}.");
            if (height < S || width < S * (K + 2))
                throw new ArgumentException(
                    $"HeightFieldMap.FromRgba: a {S} x {S * (K + 2)} image is needed for size {S} and {K} spans; " +
                    $"got {width} x {height}.");
            double hs = heightScale;
            int n = S * S;
            var lo = new float[K * n];
            var hi = new float[K * n];
            for (int k = 0; k < K; k++)
            {
                for (int y = 0; y < S; y++)
                {
                    for (int x = 0; x < S; x++)
                    {
                        int t = Texel(width, y, k * S + x);
                        double vLo = rgba[t] * 256.0 + rgba[t + 1];
                        double vHi = rgba[t + 2] * 256.0 + rgba[t + 3];
                        bool present = vHi > 0;
                        int p = (k * S + y) * S + x;
                        lo[p] = present ? (float)(vLo / 65535.0 * hs) : float.NaN;
                        hi[p] = present ? (float)(vHi / 65535.0 * hs) : float.NaN;
                    }
                }
            }
            var dist = new float[n];
            var nearLo = new float[n];
            var nearHi = new float[n];
            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    int t = Texel(width, y, K * S + x);
                    int p = y * S + x;
                    dist[p] = (float)((rgba[t] * 256.0 + rgba[t + 1]) / 64.0);
                    nearHi[p] = (float)(rgba[t + 2] / 255.0 * hs);
                    nearLo[p] = (float)(rgba[t + 3] / 255.0 * hs);
                }
            }
            int pyr = (K + 1) * S, h = S / 2;
            int m = Texel(width, h, pyr + h);
            float[] interior = null;
            if (rgba[m] == InteriorMarker[0] && rgba[m + 1] == InteriorMarker[1]
                && rgba[m + 2] == InteriorMarker[2] && rgba[m + 3] == InteriorMarker[3])
            {
                interior = new float[n];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < h; x++)
                    {
                        int t = Texel(width, y, pyr + h + x);
                        interior[(2 * y) * S + 2 * x] = rgba[t] / InteriorScale;
                        interior[(2 * y) * S + 2 * x + 1] = rgba[t + 1] / InteriorScale;
                        interior[(2 * y + 1) * S + 2 * x] = rgba[t + 2] / InteriorScale;
                        interior[(2 * y + 1) * S + 2 * x + 1] = rgba[t + 3] / InteriorScale;
                    }
                }
            }
            return new HeightFieldMap(S, K, (double[])bounds.Clone(), ground, up, hs, lo, hi, dist, nearLo, nearHi, interior);
        }

        // ------------------------------------------------------------ the reference

        /// <summary>
        /// Shadow alpha at frame <paramref name="points"/> for a positional source,
        /// pythontk <c>HeightFieldMap.alpha(points, light)</c>: the share of the
        /// source's disc the prop hides (1 buried, 1/2 on the hard edge, 0 clear;
        /// for a sizeless source 1 where the ray toward it crosses a span), 0 where
        /// the source is below the ground. A point's height is replaced by
        /// <see cref="Ground"/> before the ray is formed, as a shader must.
        /// </summary>
        /// <param name="points">Points in the map's frame, <c>x, y, z</c> runs.</param>
        /// <param name="lightX">The source's position in the frame.</param>
        /// <param name="lightY">The source's position in the frame.</param>
        /// <param name="lightZ">The source's position in the frame.</param>
        /// <param name="sourceSize">The source's diameter, frame units.</param>
        /// <param name="intensity">A multiplier on the result, clipped to [0, 1] after.</param>
        /// <returns>One alpha per point.</returns>
        public float[] Alpha(double[] points, double lightX, double lightY, double lightZ,
            double sourceSize = 0.0, double intensity = 1.0)
        {
            int n = PointCount(points);
            var output = new float[n];
            double[] lv = { lightX, lightY, lightZ };
            for (int i = 0; i < n; i++)
            {
                double l0 = lv[0] - points[3 * i], l1 = lv[1] - points[3 * i + 1], l2 = lv[2] - points[3 * i + 2];
                // the ground replaces the point's height
                if (Up == 0) l0 = lv[0] - Ground;
                else if (Up == 1) l1 = lv[1] - Ground;
                else l2 = lv[2] - Ground;
                double dist = Math.Sqrt(l0 * l0 + l1 * l1 + l2 * l2);
                if (!(dist > 1e-12)) dist = 1e-12;
                l0 /= dist;
                l1 /= dist;
                l2 /= dist;
                double rho = Math.Asin(Math.Min(Math.Max(0.5 * sourceSize / dist, 0.0), 1.0));
                output[i] = Ray(points, i, l0, l1, l2, rho, intensity);
            }
            return output;
        }

        /// <summary>
        /// Shadow alpha at frame <paramref name="points"/> for a directional source,
        /// pythontk <c>HeightFieldMap.alpha(points, direction=...)</c>; 0 everywhere
        /// for a zero direction or one that shines upward.
        /// </summary>
        /// <param name="points">Points in the map's frame, <c>x, y, z</c> runs.</param>
        /// <param name="directionX">The way the source shines, in the frame (normalised here).</param>
        /// <param name="directionY">The way the source shines, in the frame.</param>
        /// <param name="directionZ">The way the source shines, in the frame.</param>
        /// <param name="sourceAngle">The source's angular diameter, radians.</param>
        /// <param name="intensity">A multiplier on the result, clipped to [0, 1] after.</param>
        /// <returns>One alpha per point.</returns>
        public float[] AlphaDirectional(double[] points, double directionX, double directionY, double directionZ,
            double sourceAngle = 0.0, double intensity = 1.0)
        {
            int n = PointCount(points);
            var output = new float[n];
            double d0 = -directionX, d1 = -directionY, d2 = -directionZ;
            double norm = Math.Sqrt(d0 * d0 + d1 * d1 + d2 * d2);
            if (norm < 1e-12) return output;
            d0 /= norm;
            d1 /= norm;
            d2 /= norm;
            double upward = Up == 0 ? d0 : Up == 1 ? d1 : d2;
            if (upward <= 0.0) return output;
            double rho = 0.5 * sourceAngle;
            for (int i = 0; i < n; i++) output[i] = Ray(points, i, d0, d1, d2, rho, intensity);
            return output;
        }

        static int PointCount(double[] points)
        {
            if (points == null || points.Length % 3 != 0)
                throw new ArgumentException("HeightFieldMap.Alpha: points must be x, y, z runs.", nameof(points));
            return points.Length / 3;
        }

        /// <summary>One point's alpha toward the unit direction (l0, l1, l2).</summary>
        float Ray(double[] points, int i, double l0, double l1, double l2, double rho, double intensity)
        {
            double[] l = { l0, l1, l2 };
            int a = Up == 0 ? 1 : 0, b = Up == 2 ? 1 : 2;
            double la = l[a], lb = l[b], lu = l[Up];
            double horiz = Math.Sqrt(la * la + lb * lb);
            if (!(horiz > 1e-9 && lu > 0.0)) return 0.0f;
            float alpha = March(points[3 * i + a], points[3 * i + b], la / horiz, lb / horiz, lu / horiz, rho, horiz);
            float scaled = (float)(alpha * (float)intensity);
            return Math.Min(Math.Max(scaled, 0.0f), 1.0f);
        }

        /// <summary>
        /// The exact walk, pythontk <c>HeightFieldMap._march</c>: the alpha of the ray
        /// from the frame-horizontal origin along the horizontal unit direction,
        /// rising <paramref name="slope"/> per unit of horizontal travel, against a
        /// source of angular radius <paramref name="rho"/>; <paramref name="c"/> is
        /// the cosine of the ray's elevation. Every level-0 pixel the ray crosses is
        /// visited (Amanatides-Woo); the alpha is <see cref="Disc"/> of the least
        /// clearance, or of minus the depth of the least turn that frees a blocked ray.
        /// </summary>
        float March(double originA, double originB, double dirA, double dirB, double slope, double rho, double c)
        {
            int S = Size, K = Spans;
            double pw = PixelA, ph = PixelB;
            double px = Math.Min(pw, ph);
            double aspect = Aspect;
            double c2 = c * c;
            double ox = (originA - A0) / pw;
            double oy = (originB - B0) / ph;
            double dx = dirA / pw;
            double dy = dirB / ph;
            bool zx = Math.Abs(dx) < 1e-12, zy = Math.Abs(dy) < 1e-12;
            double dxs = zx ? 1.0 : dx, dys = zy ? 1.0 : dy;
            bool inx = ox >= 0.0 && ox < S, iny = oy >= 0.0 && oy < S;
            double tx0 = (0.0 - ox) / dxs, tx1 = (S - ox) / dxs;
            double ty0 = (0.0 - oy) / dys, ty1 = (S - oy) / dys;
            double txLo = zx ? (inx ? double.NegativeInfinity : double.PositiveInfinity) : Math.Min(tx0, tx1);
            double txHi = zx ? (inx ? double.PositiveInfinity : double.NegativeInfinity) : Math.Max(tx0, tx1);
            double tyLo = zy ? (iny ? double.NegativeInfinity : double.PositiveInfinity) : Math.Min(ty0, ty1);
            double tyHi = zy ? (iny ? double.PositiveInfinity : double.NegativeInfinity) : Math.Max(ty0, ty1);
            double tEnter = Math.Max(Math.Max(txLo, tyLo), 0.0);
            double tExit = Math.Min(txHi, tyHi);
            bool active = tExit > tEnter;
            bool hit = false;
            double clear = double.PositiveInfinity, rise = 0.0, side = 0.0;
            if (active)
            {
                double t = tEnter;
                const double eps = 1e-6;
                long cx = (long)Math.Floor(ox + dx * t + Math.Sign(dx) * eps);
                long cy = (long)Math.Floor(oy + dy * t + Math.Sign(dy) * eps);
                int stepx = dx > 0 ? 1 : dx < 0 ? -1 : 0;
                int stepy = dy > 0 ? 1 : dy < 0 ? -1 : 0;
                double tdx = zx ? double.PositiveInfinity : 1.0 / Math.Abs(dxs);
                double tdy = zy ? double.PositiveInfinity : 1.0 / Math.Abs(dys);
                double tmx = zx ? double.PositiveInfinity : ((dx > 0 ? cx + 1 : cx) - ox) / dxs;
                double tmy = zy ? double.PositiveInfinity : ((dy > 0 ? cy + 1 : cy) - oy) / dys;
                int n = S * S;
                for (int step = 0; step < 2 * S + 4 && active; step++)
                {
                    double te = Math.Min(Math.Min(tmx, tmy), tExit);
                    double h0 = slope * t, h1 = slope * te;
                    bool inb = cx >= 0 && cx < S && cy >= 0 && cy < S;
                    int cxc = (int)Math.Min(Math.Max(cx, 0), S - 1), cyc = (int)Math.Min(Math.Max(cy, 0), S - 1);
                    double gap = double.PositiveInfinity;
                    bool solid = false, blocked = false;
                    double up = -1.0;
                    for (int k = 0; k < K; k++)
                    {
                        int p = k * n + cyc * S + cxc;
                        double loK = _lo[p], hiK = _hi[p];
                        bool has = inb && !double.IsNaN(loK);
                        bool overlap = has && h1 >= loK && h0 <= hiK;
                        blocked |= overlap;
                        solid |= has;
                        if (has && hiK >= h0) up = Math.Max(up, hiK - h0);
                        double g = h0 > hiK ? h0 - hiK : loK - h1;
                        if (has && !overlap) gap = Math.Min(gap, g);
                    }
                    bool empty = inb && !solid;
                    double tmid = Math.Max(0.5 * (t + te), 1e-9);
                    if (up >= 0.0) rise = Math.Max(rise, up * c2 / tmid);
                    // the distance fields and the nearest column's hull, read
                    // bilinearly at the ray's midpoint through the pixel, each
                    // corner clamped to the tile on its own
                    double mx = ox + dx * tmid - 0.5;
                    double my = oy + dy * tmid - 0.5;
                    double fx = Math.Floor(mx), fy = Math.Floor(my);
                    double wx = Math.Min(Math.Max(mx - fx, 0.0), 1.0);
                    double wy = Math.Min(Math.Max(my - fy, 0.0), 1.0);
                    long ifx = (long)fx, ify = (long)fy;
                    int ix0 = Clamp(ifx, S), iy0 = Clamp(ify, S), ix1 = Clamp(ifx + 1, S), iy1 = Clamp(ify + 1, S);
                    double lateral = Math.Max(Bilerp(_dist, ix0, iy0, ix1, iy1, wx, wy) - 0.5 * aspect, 0.0) * px * c;
                    double nlo = Bilerp(_nearLo, ix0, iy0, ix1, iy1, wx, wy);
                    double nhi = Bilerp(_nearHi, ix0, iy0, ix1, iy1, wx, wy);
                    double vertical = h0 > nhi ? h0 - nhi : h1 < nlo ? nlo - h1 : 0.0;
                    if (empty)
                    {
                        double v = vertical * c2;
                        gap = Math.Sqrt(lateral * lateral + v * v);
                    }
                    else
                    {
                        gap *= c2;
                    }
                    if (!hit && !blocked) clear = Math.Min(clear, gap / tmid);
                    double turn = double.PositiveInfinity;
                    if (_interior != null)
                    {
                        double inner = Bilerp(_interior, ix0, iy0, ix1, iy1, wx, wy);
                        turn = Math.Max(inner - 0.5 * aspect, 0.0) * px * c / tmid;
                    }
                    if (blocked) side = Math.Max(side, turn);
                    hit |= blocked;
                    bool moveX = tmx <= tmy, moveY = tmy <= tmx;
                    t = te;
                    if (moveX)
                    {
                        cx += stepx;
                        tmx += tdx;
                    }
                    if (moveY)
                    {
                        cy += stepy;
                        tmy += tdy;
                    }
                    double depthNow = Math.Min(rise, side);
                    active = t < tExit - 1e-12 && !(hit && depthNow >= rho);
                }
            }
            double rhoS = Math.Max(rho, 1e-12);
            double depth = Math.Min(rise, side);
            if (!(rho > 1e-12)) return hit ? 1.0f : 0.0f;
            return (float)Disc(hit ? -depth / rhoS : clear / rhoS);
        }

        static int Clamp(long i, int S) => (int)Math.Min(Math.Max(i, 0L), S - 1);

        double Bilerp(float[] field, int ix0, int iy0, int ix1, int iy1, double wx, double wy)
        {
            int S = Size;
            double top = field[iy0 * S + ix0] * (1.0 - wx) + field[iy0 * S + ix1] * wx;
            double bottom = field[iy1 * S + ix0] * (1.0 - wx) + field[iy1 * S + ix1] * wx;
            return top * (1.0 - wy) + bottom * wy;
        }

        /// <summary>
        /// The share of a disc source a straight edge hides when the disc's centre
        /// lies <paramref name="s"/> radii past it (pythontk <c>HeightFieldMap.disc</c>):
        /// 0 at <c>s &gt;= 1</c>, 1/2 at <c>s = 0</c>, 1 at <c>s &lt;= -1</c>.
        /// </summary>
        public static double Disc(double s)
        {
            s = Math.Min(Math.Max(s, -1.0), 1.0);
            return (Math.Acos(s) - s * Math.Sqrt(1.0 - s * s)) / Math.PI;
        }
    }
}
