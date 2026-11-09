// ----------------------------------------------------------------------------
// The ShadowPlane feature's horizon baker: pythontk's ``ShadowHorizon.bake``
// (geo_utils/shadow_horizon.py) and the depth-peeled rasterisation it runs on
// (``ImgUtils.rasterize_height_spans``, img_utils/_rasterize.py), ported step
// for step, engine-free (its assembly references no UnityEngine), so the
// editor bakes a plane's map from its target's meshes with no DCC.
//
// The port is pinned against the reference by the golden cases of
// ``ptk.Conformance.cases("shadow_horizon")`` under `dotnet test` in
// test/models_dotnet: the packed image byte for byte (the cases allow a port
// one code of slack where the reference rounds; this one is held to none). The
// reference's vectorised passes are spelled as
// loops in the same arithmetic: double where numpy computes in float64,
// float32 (each step rounded by a cast) where it computes in float32, the same
// tie-breaks where it sorts or takes an argmin.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// Bakes a <see cref="HeightFieldMap"/> from triangles in the map's frame,
    /// pythontk <c>ShadowHorizon.bake</c>: the prop's footprint rasterised into
    /// per-pixel solid vertical spans, the distance field to the nearest column
    /// with its hull, and the interior distance; <see cref="HeightFieldMap.ToRgba"/>
    /// packs it, with the pyramid, into the one RGBA8 image a plane's
    /// <c>horizon</c> block names.
    /// </summary>
    /// <remarks>
    /// The frame is the plane's: the contact at the horizontal origin, <c>up</c>
    /// vertical (1 for Y-up, Unity's), the horizontal axes the other two in order.
    /// A caller hands its meshes' vertices already moved into it and reads the
    /// layout the record states (<see cref="HeightFieldMap.Size"/>,
    /// <see cref="HeightFieldMap.Spans"/>, the bounds, the height scale) off the
    /// returned map. The adaptive bake (<c>bake_adaptive</c>) scores a map against
    /// the exact projection and is not ported: a caller picks a size from
    /// <see cref="AdaptiveSizes"/>.
    /// Prior art: Unity bakes no height field for a ground shadow (its shadow maps
    /// are per frame, per light; Progressive's lightmaps bake one light's result);
    /// the map is the shadows record's own format, read by the WebXR viewer and
    /// the DCC rigs that bake it in Python, so the baker is ported and held to the
    /// reference's golden cases rather than replaced.
    /// </remarks>
    public static class ShadowHorizonBaker
    {
        /// <summary>Footprint pixels per side (<c>ShadowHorizon.DEFAULT_SIZE</c>).</summary>
        public const int DefaultSize = 128;

        /// <summary>Solid spans per column (<c>ShadowHorizon.DEFAULT_SPANS</c>).</summary>
        public const int DefaultSpans = 2;

        /// <summary>
        /// Margin round the geometry on every side, as a fraction of the larger of
        /// its horizontal extent and its height (<c>ShadowHorizon.DEFAULT_PADDING</c>).
        /// </summary>
        public const double DefaultPadding = 0.1;

        /// <summary>The footprint sizes the producers' adaptive bake walks through (<c>ShadowHorizon.ADAPTIVE_SIZES</c>).</summary>
        public static readonly int[] AdaptiveSizes = { 128, 256 };

        /// <summary>
        /// Bake the map of <paramref name="meshes"/> in the map's frame.
        /// </summary>
        /// <param name="meshes">Each mesh's points (<c>x, y, z</c> runs, in the frame) and triangles (<c>i, j, k</c> runs into them).</param>
        /// <param name="ground">Height of the ground plane along <paramref name="up"/> in the frame.</param>
        /// <param name="up">The vertical axis index: 1 Y-up, 2 Z-up.</param>
        /// <param name="size">Footprint pixels per side, rounded up to a power of two of at least 8.</param>
        /// <param name="spans">Solid spans kept per column (at least 1); a column with more merges its smallest gaps first.</param>
        /// <param name="bounds"><c>(a0, a1, b0, b1)</c> footprint to rasterise; null fits it to the geometry with <paramref name="padding"/>.</param>
        /// <param name="padding">Margin round the geometry, every side, as a fraction of the larger of its horizontal extent and its height.</param>
        /// <returns>The map; <see cref="HeightFieldMap.ToRgba"/> gives its image.</returns>
        /// <exception cref="ArgumentException">No mesh has a point and a triangle, or an argument is malformed.</exception>
        public static HeightFieldMap Bake(
            IEnumerable<(double[] points, int[] triangles)> meshes,
            double ground = 0.0, int up = 1, int size = DefaultSize, int spans = DefaultSpans,
            double[] bounds = null, double padding = DefaultPadding)
        {
            if (meshes == null) throw new ArgumentNullException(nameof(meshes));
            if (up < 0 || up > 2) throw new ArgumentException($"ShadowHorizonBaker.Bake: up must be 0, 1 or 2; got {up}.", nameof(up));
            if (bounds != null && bounds.Length != 4)
                throw new ArgumentException("ShadowHorizonBaker.Bake: bounds must be (a0, a1, b0, b1).", nameof(bounds));
            // every triangle's three corners, in the frame (pythontk's pts[tris])
            var corners = new List<double>();
            foreach (var (points, triangles) in meshes)
            {
                if (points == null || triangles == null || points.Length < 3 || triangles.Length < 3) continue;
                if (points.Length % 3 != 0 || triangles.Length % 3 != 0)
                    throw new ArgumentException("ShadowHorizonBaker.Bake: points and triangles must be runs of three.");
                int count = points.Length / 3;
                foreach (int index in triangles)
                {
                    if (index < 0 || index >= count)
                        throw new ArgumentException(
                            $"ShadowHorizonBaker.Bake: triangle index {index} is outside the mesh's {count} points.");
                    corners.Add(points[3 * index]);
                    corners.Add(points[3 * index + 1]);
                    corners.Add(points[3 * index + 2]);
                }
            }
            if (corners.Count == 0)
                throw new ArgumentException("ShadowHorizonBaker.Bake: no geometry provided.", nameof(meshes));
            double[] tri = corners.ToArray();
            int S = 8;
            while (S < size) S <<= 1;
            int K = Math.Max(spans, 1);
            Axes(up, out int a, out int b);
            double top = double.NegativeInfinity;
            for (int i = 0; i < tri.Length; i += 3) top = Math.Max(top, tri[i + up]);
            double tall = top - ground;
            if (bounds == null)
            {
                double loA = double.PositiveInfinity, hiA = double.NegativeInfinity;
                double loB = double.PositiveInfinity, hiB = double.NegativeInfinity;
                for (int i = 0; i < tri.Length; i += 3)
                {
                    loA = Math.Min(loA, tri[i + a]);
                    hiA = Math.Max(hiA, tri[i + a]);
                    loB = Math.Min(loB, tri[i + b]);
                    hiB = Math.Max(hiB, tri[i + b]);
                }
                double pad = padding * PyMax(PyMax(PyMax(hiA - loA, hiB - loB), tall), 1e-3) + 1e-6;
                bounds = new[] { loA - pad, hiA + pad, loB - pad, hiB + pad };
            }
            else
            {
                bounds = (double[])bounds.Clone();
            }
            int n = S * S;
            var lo = new float[K * n];
            var hi = new float[K * n];
            // A gap no wider than one step of the 16-bit height code at the geometry's
            // height is no daylight (pythontk ShadowHorizon.bake): it encodes as
            // touching, and two parts meeting face to face come through an export and
            // an import a float apart (measured: 1.2e-7 m between an awning's post and slab).
            RasterizeSpans(tri, up, S, ground, bounds, K, lo, hi, PyMax(tall, 1e-3) / HeightFieldMap.Max16);

            float topHi = float.NaN;
            foreach (float v in hi) topHi = HeightFieldMap.FMax(topHi, v);
            double hs = float.IsNaN(topHi) ? 1e-3 : Math.Max(topHi, 1e-3);
            float hs32 = (float)hs;
            // quantised as the image will hold them, conservatively: bottoms down,
            // tops up, so no span thins out of a ray that met it in float
            for (int i = 0; i < lo.Length; i++)
            {
                float l = (float)((float)Math.Floor((float)((float)(lo[i] / hs32) * HeightFieldMap.Max16)) / HeightFieldMap.Max16);
                float h = (float)((float)Math.Ceiling((float)((float)(hi[i] / hs32) * HeightFieldMap.Max16)) / HeightFieldMap.Max16);
                l = (float)(l * hs32);
                h = (float)(h * hs32);
                if (!float.IsNaN(h) && h <= 0.0f) l = h = float.NaN;
                lo[i] = l;
                hi[i] = h;
            }
            var hullLo = new float[n];
            var hullHi = new float[n];
            var solid = new bool[n];
            for (int p = 0; p < n; p++)
            {
                float hl = float.NaN, hh = float.NaN;
                for (int k = 0; k < K; k++)
                {
                    hl = HeightFieldMap.FMin(hl, lo[k * n + p]);
                    hh = HeightFieldMap.FMax(hh, hi[k * n + p]);
                }
                hullLo[p] = hl;
                hullHi[p] = hh;
                solid[p] = !float.IsNaN(hh);
            }
            // distances in the smaller pixel pitch, the other axis weighted up
            double pw = (bounds[1] - bounds[0]) / S, ph = (bounds[3] - bounds[2]) / S;
            double pitch = Math.Max(Math.Min(pw, ph), 1e-12);
            double wx = pw / pitch, wy = ph / pitch;
            DistanceField(solid, hullLo, hullHi, S, wx, wy, out float[] dist, out float[] nearLo, out float[] nearHi);
            for (int p = 0; p < n; p++)
                dist[p] = Math.Min((float)(Round((float)(dist[p] * HeightFieldMap.DistScale)) / HeightFieldMap.DistScale), HeightFieldMap.DistFar);
            // and from every solid pixel to the nearest EMPTY one: the sideways
            // turn that frees a blocked ray (the inner penumbra)
            var open = new bool[n];
            for (int p = 0; p < n; p++) open[p] = !solid[p];
            DistanceField(open, hullLo, hullHi, S, wx, wy, out float[] interior, out _, out _);
            for (int p = 0; p < n; p++)
            {
                interior[p] = solid[p]
                    ? Math.Min((float)(Round((float)(interior[p] * HeightFieldMap.InteriorScale)) / HeightFieldMap.InteriorScale), HeightFieldMap.InteriorFar)
                    : 0.0f;
                nearLo[p] = (float)((float)((float)Math.Floor((float)((float)(nearLo[p] / hs32) * 255.0f)) / 255.0f) * hs32);
                nearHi[p] = (float)((float)((float)Math.Ceiling((float)((float)(nearHi[p] / hs32) * 255.0f)) / 255.0f) * hs32);
            }
            return new HeightFieldMap(S, K, bounds, ground, up, hs, lo, hi, dist, nearLo, nearHi, interior);
        }

        /// <summary>Python's <c>max(x, y)</c>: the first unless the second is greater.</summary>
        static double PyMax(double x, double y) => y > x ? y : x;

        /// <summary>numpy's <c>round</c> on a float32: to the nearest, halves to even.</summary>
        static float Round(float v) => (float)Math.Round((double)v, MidpointRounding.ToEven);

        /// <summary>The two horizontal axis indices, in order, for the vertical <paramref name="up"/>.</summary>
        static void Axes(int up, out int a, out int b)
        {
            a = up == 0 ? 1 : 0;
            b = up == 2 ? 1 : 2;
        }

        // -------------------------------------------------------- rasterisation

        struct Crossing
        {
            public long px;
            public double z;
            public int sign;
        }

        sealed class CrossingOrder : IComparer<Crossing>
        {
            public static readonly CrossingOrder Instance = new CrossingOrder();

            public int Compare(Crossing x, Crossing y)
            {
                int c = x.px.CompareTo(y.px);
                if (c != 0) return c;
                c = x.z.CompareTo(y.z);
                return c != 0 ? c : x.sign.CompareTo(y.sign);
            }
        }

        /// <summary>
        /// The solid vertical spans per footprint pixel (pythontk
        /// <c>rasterize_height_spans</c>): the surface crossings at each pixel's
        /// centre, sorted and paired into spans by the running count of entries
        /// minus exits, every crossing also a span of no thickness; then the
        /// <paramref name="K"/> kept (<see cref="AssignSpans"/>, a gap no wider than
        /// <paramref name="mergeGap"/> touching); triangle edges splatted at
        /// half-pixel steps as one hull span where the fill missed.
        /// </summary>
        static void RasterizeSpans(double[] tri, int up, int size, double ground, double[] bounds, int K,
            float[] lo, float[] hi, double mergeGap)
        {
            Axes(up, out int a, out int b);
            for (int i = 0; i < lo.Length; i++) lo[i] = hi[i] = float.NaN;
            int m = tri.Length / 9;
            double a0 = bounds[0], a1 = bounds[1], b0 = bounds[2], b1 = bounds[3];
            double sa = Math.Max(a1 - a0, 1e-9), sb = Math.Max(b1 - b0, 1e-9);
            var x = new double[3 * m];
            var y = new double[3 * m];
            var z = new double[3 * m];
            var facing = new int[m];
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int v = 9 * i + 3 * j;
                    // heights above the ground, clamped AT it: a face below it
                    // still crosses a column, and the span then starts there
                    z[3 * i + j] = Math.Max(tri[v + up] - ground, 0.0);
                    x[3 * i + j] = (tri[v + a] - a0) / sa * size;
                    y[3 * i + j] = (tri[v + b] - b0) / sb * size;
                }
                // which way the face looks along up: a vertical ray enters the
                // solid through a face looking down and leaves through one up
                int t0 = 9 * i, t1 = t0 + 3, t2 = t0 + 6;
                double u0 = tri[t1] - tri[t0], u1 = tri[t1 + 1] - tri[t0 + 1], u2 = tri[t1 + 2] - tri[t0 + 2];
                double w0 = tri[t2] - tri[t0], w1 = tri[t2 + 1] - tri[t0 + 1], w2 = tri[t2 + 2] - tri[t0 + 2];
                double normalUp = up == 0 ? u1 * w2 - u2 * w1 : up == 1 ? u2 * w0 - u0 * w2 : u0 * w1 - u1 * w0;
                facing[i] = normalUp < 0.0 ? 1 : -1;
            }
            // -- surface crossings at pixel centres, per triangle, watertight: a
            //    centre on an edge two triangles share is one triangle's (Covers)
            var crossings = new List<Crossing>();
            for (int i = 0; i < m; i++)
            {
                double ax = x[3 * i], ay = y[3 * i], bx = x[3 * i + 1], by = y[3 * i + 1], cx = x[3 * i + 2], cy = y[3 * i + 2];
                long x0 = (long)Math.Floor(Math.Min(Math.Min(ax, bx), cx)), x1 = (long)Math.Ceiling(Math.Max(Math.Max(ax, bx), cx));
                long y0 = (long)Math.Floor(Math.Min(Math.Min(ay, by), cy)), y1 = (long)Math.Ceiling(Math.Max(Math.Max(ay, by), cy));
                x0 = Math.Max(x0, 0);
                y0 = Math.Max(y0, 0);
                x1 = Math.Min(x1, size - 1);
                y1 = Math.Min(y1, size - 1);
                if (x1 < x0 || y1 < y0) continue;
                double denom = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
                if (Math.Abs(denom) < 1e-12) continue;
                double z0 = z[3 * i], z1 = z[3 * i + 1], z2 = z[3 * i + 2];
                double sigma = denom > 0.0 ? 1.0 : -1.0;
                for (long yy = y0; yy <= y1; yy++)
                {
                    double yc = yy + 0.5;
                    for (long xx = x0; xx <= x1; xx++)
                    {
                        double xc = xx + 0.5;
                        if (!(Covers(ax, ay, bx, by, sigma, xc, yc) && Covers(bx, by, cx, cy, sigma, xc, yc)
                              && Covers(cx, cy, ax, ay, sigma, xc, yc))) continue;
                        double l1 = ((by - cy) * (xc - cx) + (cx - bx) * (yc - cy)) / denom;
                        double l2 = ((cy - ay) * (xc - cx) + (ax - cx) * (yc - cy)) / denom;
                        double l3 = 1.0 - l1 - l2;
                        crossings.Add(new Crossing
                        {
                            px = yy * size + xx,
                            z = l1 * z0 + l2 * z1 + l3 * z2,
                            sign = facing[i],
                        });
                    }
                }
            }
            // -- edge splat at half-pixel steps: the hull of the samples a pixel
            //    receives, one span, where the fill missed
            int n = size * size;
            var splatTop = new double[n];
            var splatBot = new double[n];
            for (int p = 0; p < n; p++)
            {
                splatTop[p] = double.NegativeInfinity;
                splatBot[p] = double.PositiveInfinity;
            }
            for (int edge = 0; edge < 3; edge++)
            {
                int j0 = edge, j1 = (edge + 1) % 3;
                for (int i = 0; i < m; i++)
                {
                    double ex0 = x[3 * i + j0], ex1 = x[3 * i + j1];
                    double ey0 = y[3 * i + j0], ey1 = y[3 * i + j1];
                    double ez0 = z[3 * i + j0], ez1 = z[3 * i + j1];
                    double ddx = ex1 - ex0, ddy = ey1 - ey0;
                    double length = Math.Sqrt(ddx * ddx + ddy * ddy);
                    long count = Math.Min((long)Math.Ceiling(length * 2.0) + 1, 4L * size);
                    double last = Math.Max(count - 1, 1);
                    for (long s = 0; s < count; s++)
                    {
                        double frac = s / last;
                        double sx = ex0 + (ex1 - ex0) * frac;
                        double sy = ey0 + (ey1 - ey0) * frac;
                        double sz = ez0 + (ez1 - ez0) * frac;
                        double fx = Math.Floor(sx), fy = Math.Floor(sy);
                        if (!(fx >= 0 && fx < size && fy >= 0 && fy < size)) continue;
                        int flat = (int)fy * size + (int)fx;
                        if (sz > splatTop[flat]) splatTop[flat] = sz;
                        if (sz < splatBot[flat]) splatBot[flat] = sz;
                    }
                }
            }
            // -- the crossings into spans, pixel by pixel: sorted by height, the
            //    running count of entries minus exits is non-zero INSIDE the
            //    solid, so each gap between consecutive crossings with a non-zero
            //    count is a span; every crossing also stands as a span of no
            //    thickness (an open surface still blocks at its height)
            var filled = new bool[n];
            if (crossings.Count > 0)
            {
                Crossing[] sorted = crossings.ToArray();
                Array.Sort(sorted, CrossingOrder.Instance);
                // one surface reported twice at a pixel centre (a face doubled, two
                // meshes meeting on one face) is one crossing; a shared edge reports once
                var kept = new List<Crossing>(sorted.Length);
                for (int i = 0; i < sorted.Length; i++)
                {
                    if (i > 0)
                    {
                        Crossing p = sorted[i - 1], c = sorted[i];
                        bool sameZ = Math.Abs(c.z - p.z) <= 1e-9 * (1.0 + Math.Abs(c.z));
                        if (c.px == p.px && sameZ && c.sign == p.sign) continue;
                    }
                    kept.Add(sorted[i]);
                }
                int count = kept.Count;
                var spanPx = new List<long>(2 * count);
                var spanLo = new List<double>(2 * count);
                var spanHi = new List<double>(2 * count);
                long total = 0, baseline = 0;
                for (int i = 0; i < count; i++)
                {
                    if (i == 0 || kept[i].px != kept[i - 1].px) baseline = total;
                    total += kept[i].sign;
                    long inside = total - baseline;  // entries minus exits below, inclusive
                    bool sameNext = i + 1 < count && kept[i + 1].px == kept[i].px;
                    if (sameNext && inside != 0)
                    {
                        spanPx.Add(kept[i].px);
                        spanLo.Add(kept[i].z);
                        spanHi.Add(kept[i + 1].z);
                    }
                }
                for (int i = 0; i < count; i++)
                {
                    spanPx.Add(kept[i].px);
                    spanLo.Add(kept[i].z);
                    spanHi.Add(kept[i].z);
                    filled[kept[i].px] = true;
                }
                AssignSpans(lo, hi, spanPx, spanLo, spanHi, size, K, mergeGap);
            }
            // -- the splat hull where the fill left a pixel empty
            for (int p = 0; p < n; p++)
            {
                if (filled[p] || double.IsInfinity(splatTop[p]) || !(splatTop[p] > 0.0)) continue;
                lo[p] = (float)Math.Max(splatBot[p], 0.0);
                hi[p] = (float)splatTop[p];
            }
            // a span that never rises above the ground blocks nothing
            for (int i = 0; i < hi.Length; i++)
            {
                if (!float.IsNaN(hi[i]) && hi[i] <= 0.0f) lo[i] = hi[i] = float.NaN;
            }
        }

        /// <summary>
        /// Whether the point <c>(px, py)</c> is on the inner side of the triangle's
        /// edge <c>j -&gt; k</c>, watertight (pythontk <c>_covers</c>): the side test is
        /// computed from the edge's endpoints in one order (the lesser <c>(x, y)</c>
        /// first, then negated for the triangle's own direction), so the two
        /// triangles sharing an edge read one value with opposite signs; a point
        /// exactly on it is the triangle's when the edge, walked counter-clockwise
        /// round it, heads up or due left (a top-left rule), so it is exactly one
        /// neighbour's. <paramref name="sigma"/> is +1 for a counter-clockwise
        /// triangle in <c>(x, y)</c>, -1 otherwise.
        /// </summary>
        static bool Covers(double jx, double jy, double kx, double ky, double sigma, double px, double py)
        {
            bool flip = kx < jx || (kx == jx && ky < jy);
            double p0 = flip ? kx : jx, p1 = flip ? ky : jy, q0 = flip ? jx : kx, q1 = flip ? jy : ky;
            double e = (q0 - p0) * (py - p1) - (q1 - p1) * (px - p0);
            e = sigma * (flip ? -e : e);
            double dx = sigma * (kx - jx), dy = sigma * (ky - jy);
            bool tie = dy > 0.0 || (dy == 0.0 && dx < 0.0);
            return e > 0.0 || (e == 0.0 && tie);
        }

        /// <summary>
        /// Merge touching spans per pixel (a gap no wider than <paramref name="mergeGap"/>
        /// touches), keep the <paramref name="K"/> (a pixel with more merges its
        /// smallest gaps first) and write them, sorted by height (pythontk
        /// <c>_assign_spans</c>).
        /// </summary>
        static void AssignSpans(float[] lo, float[] hi, List<long> px, List<double> spanLo, List<double> spanHi, int size, int K,
            double mergeGap)
        {
            int count = px.Count;
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            // numpy's lexsort is stable: the index breaks a tie
            Array.Sort(order, (i, j) =>
            {
                int c = px[i].CompareTo(px[j]);
                if (c != 0) return c;
                c = spanLo[i].CompareTo(spanLo[j]);
                return c != 0 ? c : i.CompareTo(j);
            });
            double most = double.NegativeInfinity, least = double.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                most = Math.Max(most, spanHi[i]);
                least = Math.Min(least, spanLo[i]);
            }
            // a span merges into the one before it (same pixel) when it starts at
            // or below that one's top: the running maximum of the tops, restarted
            // per pixel by lifting each pixel's values onto their own plateau
            double lift = most - least + 1.0;
            var gPx = new List<long>();
            var gLo = new List<double>();
            var gHi = new List<double>();
            long seg = -1;
            double running = double.NegativeInfinity, prevTop = double.NegativeInfinity;
            for (int r = 0; r < count; r++)
            {
                int i = order[r];
                bool newPixel = r == 0 || px[i] != px[order[r - 1]];
                if (newPixel) seg++;
                double lifted = spanHi[i] + seg * lift;
                running = Math.Max(running, lifted);
                double runTop = running - seg * lift;
                bool start = newPixel || spanLo[i] > prevTop + mergeGap;
                if (start)
                {
                    gPx.Add(px[i]);
                    gLo.Add(spanLo[i]);
                    gHi.Add(spanHi[i]);
                }
                else
                {
                    int g = gLo.Count - 1;
                    gLo[g] = Math.Min(gLo[g], spanLo[i]);
                    gHi[g] = Math.Max(gHi[g], spanHi[i]);
                }
                prevTop = runTop;
            }
            int n = size * size;
            for (int s = 0; s < gPx.Count;)
            {
                int e = s;
                while (e < gPx.Count && gPx[e] == gPx[s]) e++;
                long pixel = gPx[s];
                if (e - s <= K)
                {
                    for (int k = 0; k < e - s; k++)
                    {
                        lo[k * n + pixel] = (float)gLo[s + k];
                        hi[k * n + pixel] = (float)gHi[s + k];
                    }
                }
                else
                {
                    var l = gLo.GetRange(s, e - s);
                    var h = gHi.GetRange(s, e - s);
                    while (l.Count > K)
                    {
                        int j = 0;
                        double best = l[1] - h[0];
                        for (int q = 1; q < l.Count - 1; q++)
                        {
                            double gap = l[q + 1] - h[q];
                            if (gap < best)
                            {
                                best = gap;
                                j = q;
                            }
                        }
                        h[j] = Math.Max(h[j], h[j + 1]);
                        l.RemoveAt(j + 1);
                        h.RemoveAt(j + 1);
                    }
                    for (int k = 0; k < l.Count; k++)
                    {
                        lo[k * n + pixel] = (float)l[k];
                        hi[k * n + pixel] = (float)h[k];
                    }
                }
                s = e;
            }
        }

        // -------------------------------------------------------- distance field

        /// <summary>
        /// The Euclidean distance (pixel centres) from every pixel to the nearest
        /// <paramref name="solid"/> one and that pixel's hull (pythontk
        /// <c>ShadowHorizon._distance_field</c>), a step along a row weighted
        /// <paramref name="wx"/> and along a column <paramref name="wy"/>: a row's
        /// nearest solid column first, then the best row per pixel, the lowest row
        /// on a tie as numpy's argmin takes it. The row scan walks outward from the
        /// pixel's own row and stops once the row distance alone exceeds the best,
        /// which no later row can tie.
        /// </summary>
        static void DistanceField(bool[] solid, float[] hullLo, float[] hullHi, int S, double wx, double wy,
            out float[] dist, out float[] nearLo, out float[] nearHi)
        {
            int n = S * S;
            dist = new float[n];
            nearLo = new float[n];
            nearHi = new float[n];
            bool any = false;
            foreach (bool s in solid) any |= s;
            if (!any)
            {
                for (int p = 0; p < n; p++) dist[p] = HeightFieldMap.DistFar;
                return;
            }
            long big = 4L * S * S;
            var rowArg = new long[n];
            var rowD2 = new double[n];
            for (int y = 0; y < S; y++)
            {
                var arg = new long[S];
                long last = -big;
                for (int x = 0; x < S; x++)
                {
                    if (solid[y * S + x]) last = x;
                    arg[x] = last;
                }
                long next = 2 * big;
                for (int x = S - 1; x >= 0; x--)
                {
                    if (solid[y * S + x]) next = x;
                    long fwd = arg[x] >= 0 ? x - arg[x] : big;
                    long bwd = next < S ? next - x : big;
                    rowArg[y * S + x] = fwd <= bwd ? arg[x] : next;
                    double step = Math.Min(fwd, bwd) * wx;
                    rowD2[y * S + x] = step * step;
                }
            }
            for (int x = 0; x < S; x++)
            {
                for (int y = 0; y < S; y++)
                {
                    double best = double.PositiveInfinity;
                    int bestK = -1;
                    for (int d = 0; d < S; d++)
                    {
                        double dy = d * wy;
                        double d2 = dy * dy;
                        if (d2 > best) break;
                        int kLow = y - d, kHigh = y + d;
                        if (kLow >= 0)
                        {
                            double total = d2 + rowD2[kLow * S + x];
                            if (total < best || (total == best && kLow < bestK))
                            {
                                best = total;
                                bestK = kLow;
                            }
                        }
                        if (d > 0 && kHigh < S)
                        {
                            double total = d2 + rowD2[kHigh * S + x];
                            if (total < best || (total == best && kHigh < bestK))
                            {
                                best = total;
                                bestK = kHigh;
                            }
                        }
                    }
                    int p = y * S + x;
                    dist[p] = (float)Math.Sqrt(best);
                    long column = rowArg[bestK * S + x];
                    int q = bestK * S + (int)column;
                    nearLo[p] = float.IsNaN(hullLo[q]) ? 0.0f : hullLo[q];
                    nearHi[p] = float.IsNaN(hullHi[q]) ? 0.0f : hullHi[q];
                    if (solid[p]) dist[p] = 0.0f;
                }
            }
        }
    }
}
