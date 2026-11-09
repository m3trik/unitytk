using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using M3trik.Unitytk.Models.Tests;
using NUnit.Framework;

using M3trik.Unitytk.Testing;
namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// The ShadowPlane feature's horizon baker (<see cref="ShadowHorizonBaker"/>)
    /// and map (<see cref="HeightFieldMap"/>), its Model part, held to pythontk's
    /// golden cases (<c>ptk.Conformance.cases("shadow_horizon")</c>): every case's
    /// layout and packed image from the same triangles, and the evaluation of the
    /// image the reference delivered at every sampled point and source, each within
    /// the document's own tolerances.
    /// </summary>
    public class ShadowHorizonBakerTests
    {
        const int Seed = 3;
        const string Options = "{\"per_kind\": 4}";

        IDictionary<string, object> _tolerance;
        List<IDictionary<string, object>> _cases;

        [OneTimeSetUp]
        public void Generate()
        {
            IDictionary<string, object> doc = Conformance.Cases("shadow_horizon", Seed, Options);
            _tolerance = O(doc["tolerance"]);
            _cases = L(doc["cases"]).Select(O).ToList();
            Assert.That(_cases.Count(c => c.ContainsKey("map")), Is.GreaterThan(20), "the reference baked too few maps");
            Assert.That(_cases.Any(c => c.ContainsKey("error")), Is.True, "no refused input among the cases");
        }

        [Test]
        public void The_bake_chooses_the_reference_layout()
        {
            double tol = D(_tolerance["layout"]);
            foreach (var (c, at) in Baked())
            {
                HeightFieldMap got = Bake(c);
                var want = O(c["map"]);
                Assert.That(got.Size, Is.EqualTo(I(want["size"])), at + " size");
                Assert.That(got.Spans, Is.EqualTo(I(want["spans"])), at + " spans");
                Assert.That(got.Levels, Is.EqualTo(I(want["levels"])), at + " levels");
                Assert.That((got.Width, got.Height), Is.EqualTo((I(want["width"]), I(want["height"]))), at + " image size");
                Assert.That(MaxDiff(got.Bounds, Numbers(want["bounds"])), Is.LessThanOrEqualTo(tol), at + " bounds");
                Assert.That(Math.Abs(got.HeightScale - D(want["height_scale"])), Is.LessThanOrEqualTo(tol), at + " height scale");
                Assert.That(got.Ground, Is.EqualTo(D(want["ground"])), at + " ground");
                Assert.That(got.Up, Is.EqualTo(I(want["up"])), at + " up");
            }
        }

        [Test]
        public void The_bake_packs_the_reference_image()
        {
            double tol = D(_tolerance["texel"]);
            int worst = 0, off = 0, codes = 0;
            string worstAt = "";
            foreach (var (c, at) in Baked())
            {
                HeightFieldMap got = Bake(c);
                byte[] want = Image(O(c["map"]));
                byte[] mine = got.ToRgba();
                Assert.That(mine.Length, Is.EqualTo(want.Length), at + " image bytes");
                foreach (var (x, y, channel, a, b) in HorizonCodes.Of(mine, want, got.Size, got.Spans))
                {
                    codes++;
                    int diff = Math.Abs(a - b);
                    if (diff == 0) continue;
                    off++;
                    if (diff > worst)
                    {
                        worst = diff;
                        worstAt = $"{at} texel ({x}, {y}) channel {channel}: {a} for {b}";
                    }
                }
            }
            TestContext.Out.WriteLine($"{off} of {codes} codes differ; the worst by {worst} ({worstAt})");
            Assert.That(worst, Is.LessThanOrEqualTo(tol), worstAt);
            // The document's one code of slack is the contract for a port whose
            // arithmetic rounds otherwise; this one computes what the reference
            // computes, so it is held to every byte (a floor for a round, one code
            // off, stays inside the slack).
            Assert.That(off, Is.Zero, $"{off} of {codes} codes differ; the worst by {worst} ({worstAt})");
        }

        [Test]
        public void The_evaluation_of_the_delivered_image_matches_the_reference()
        {
            double tol = D(_tolerance["alpha"]);
            double worst = 0.0;
            int evaluated = 0;
            foreach (var (c, at) in Baked())
            {
                var m = O(c["map"]);
                HeightFieldMap map = HeightFieldMap.FromRgba(Image(m), I(m["width"]), I(m["height"]), I(m["size"]),
                    I(m["spans"]), Numbers(m["bounds"]), D(m["ground"]), I(m["up"]), D(m["height_scale"]));
                int source = 0;
                foreach (var e in L(c["alpha"]).Select(O))
                {
                    double[] points = Numbers(e["points"]);
                    float[] got = e["light"] != null
                        ? Alpha(map, points, Numbers(e["light"]), D(e["source_size"]), D(e["intensity"]))
                        : AlphaDirectional(map, points, Numbers(e["direction"]), D(e["source_angle"]), D(e["intensity"]));
                    double diff = MaxDiff(got.Select(v => (double)v).ToArray(), Numbers(e["alpha"]));
                    Assert.That(diff, Is.LessThanOrEqualTo(tol), $"{at} source {source}");
                    worst = Math.Max(worst, diff);
                    evaluated += points.Length / 3;
                    source++;
                }
            }
            TestContext.Out.WriteLine($"{evaluated} alphas, the worst off by {worst:G3}");
        }

        [Test]
        public void A_refused_input_is_refused()
        {
            foreach (var c in _cases.Where(c => c.ContainsKey("error")))
                Assert.Throws<ArgumentException>(() => Bake(c), (string)c["name"]);
        }

        [Test]
        public void A_baked_map_decodes_from_its_image_to_its_own_fields()
        {
            // test_shadow_horizon's test_rgba_round_trip_is_exact: the fields to a
            // float32 rounding. Not the image again byte for byte: the pyramid is
            // re-derived from the 8-bit hull, and the reference's own re-encoding
            // moves a level's floor or ceiling by one code there.
            foreach (var (c, at) in Baked())
            {
                HeightFieldMap got = Bake(c);
                HeightFieldMap back = HeightFieldMap.FromRgba(got.ToRgba(), got.Width, got.Height, got.Size, got.Spans,
                    got.Bounds, got.Ground, got.Up, got.HeightScale);
                SameField(got.Lo, back.Lo, at + " lo");
                SameField(got.Hi, back.Hi, at + " hi");
                SameField(got.Dist, back.Dist, at + " dist");
                SameField(got.NearLo, back.NearLo, at + " near lo");
                SameField(got.NearHi, back.NearHi, at + " near hi");
                SameField(got.Interior, back.Interior, at + " interior");
            }
        }

        static void SameField(float[] a, float[] b, string at)
        {
            Assert.That(b.Length, Is.EqualTo(a.Length), at);
            for (int i = 0; i < a.Length; i++)
            {
                bool same = float.IsNaN(a[i]) ? float.IsNaN(b[i]) : Math.Abs(a[i] - b[i]) <= 1e-6;
                if (!same) Assert.Fail($"{at} [{i}]: {b[i]} decoded for {a[i]}");
            }
        }

        [Test]
        public void A_bake_at_the_producers_sizes_stays_within_budget()
        {
            // the producers' fixture: a chair, every size the adaptive ladder bakes
            var chair = _cases.First(c => (string)c["name"] == "adaptive_large_spans_3");
            var meshes = Meshes(In(chair));
            foreach (int size in ShadowHorizonBaker.AdaptiveSizes)
            {
                ShadowHorizonBaker.Bake(meshes, size: size).ToRgba();  // warm
                var watch = Stopwatch.StartNew();
                const int runs = 5;
                for (int i = 0; i < runs; i++) ShadowHorizonBaker.Bake(meshes, size: size).ToRgba();
                double seconds = watch.Elapsed.TotalSeconds / runs;
                TestContext.Out.WriteLine($"size {size}: {seconds * 1000.0:F1} ms per bake and pack");
                // the reference's own guard (test_bake_time_stays_within_budget): 40x its measured bake
                Assert.That(seconds, Is.LessThan(1.5), $"size {size}");
            }
        }

        // ---- the document ------------------------------------------------------
        IEnumerable<(IDictionary<string, object>, string)> Baked()
            => _cases.Select((c, i) => (c, $"case {i} ({c["name"]})")).Where(t => t.c.ContainsKey("map"));

        static HeightFieldMap Bake(IDictionary<string, object> c)
        {
            var given = In(c);
            return ShadowHorizonBaker.Bake(Meshes(given), D(given["ground"]), I(given["up"]), I(given["size"]),
                I(given["spans"]), given["bounds"] == null ? null : Numbers(given["bounds"]), D(given["padding"]));
        }

        static List<(double[], int[])> Meshes(IDictionary<string, object> given)
            => L(given["meshes"]).Select(O)
                .Select(m => (Numbers(m["points"]), L(m["triangles"]).Select(I).ToArray()))
                .ToList();

        static float[] Alpha(HeightFieldMap map, double[] points, double[] light, double size, double intensity)
            => map.Alpha(points, light[0], light[1], light[2], size, intensity);

        static float[] AlphaDirectional(HeightFieldMap map, double[] points, double[] d, double angle, double intensity)
            => map.AlphaDirectional(points, d[0], d[1], d[2], angle, intensity);

        static byte[] Image(IDictionary<string, object> map)
        {
            using var packed = new MemoryStream(Convert.FromBase64String((string)map["rgba"]));
            using var inflate = new ZLibStream(packed, CompressionMode.Decompress);
            using var raw = new MemoryStream();
            inflate.CopyTo(raw);
            return raw.ToArray();
        }

        static IDictionary<string, object> In(IDictionary<string, object> c) => O(c["input"]);
        static IDictionary<string, object> O(object o) => (IDictionary<string, object>)o;
        static IList<object> L(object o) => (IList<object>)o;
        static double D(object o) => Convert.ToDouble(o, System.Globalization.CultureInfo.InvariantCulture);
        static int I(object o) => Convert.ToInt32(o, System.Globalization.CultureInfo.InvariantCulture);
        static double[] Numbers(object o) => L(o).Select(D).ToArray();

        static double MaxDiff(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            Assert.That(a.Count, Is.EqualTo(b.Count), "one value per term");
            double most = 0.0;
            for (int i = 0; i < a.Count; i++) most = Math.Max(most, Math.Abs(a[i] - b[i]));
            return most;
        }
    }
}
