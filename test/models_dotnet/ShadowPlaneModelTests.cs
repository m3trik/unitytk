using System;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Models.Tests;
using NUnit.Framework;

namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// The ShadowPlane feature's model (<see cref="ShadowPlaneModel"/>, its Model
    /// part) held to pythontk's golden cases
    /// (<c>ptk.Conformance.cases("shadow_projection")</c>): every term of the model
    /// from the reference's source point, a sun's from its direction too, the point
    /// a sun is written as, and where a plane carrying the case's canvas lands, each
    /// within the document's own tolerances. The same cases run in Unity through
    /// test_shadow_plane_runtime_integration.py.
    /// </summary>
    public class ShadowPlaneModelTests
    {
        // The Unity-side suite's draw (test_shadow_plane_runtime_integration.py _conformance).
        const int Seed = 3;
        const string Options = "{\"per_kind\": 8}";

        IDictionary<string, object> _tolerance;
        List<IDictionary<string, object>> _cases;

        [OneTimeSetUp]
        public void Generate()
        {
            IDictionary<string, object> doc = Conformance.Cases("shadow_projection", Seed, Options);
            _tolerance = O(doc["tolerance"]);
            _cases = L(doc["cases"]).Select(O).ToList();
            Assert.That(_cases, Is.Not.Empty, "the reference generated no case");
            Assert.That(_cases.Any(c => In(c)["direction"] != null), Is.True, "no sun among the cases");
        }

        [Test]
        public void The_model_matches_the_reference_from_its_source_point()
        {
            foreach (var (c, at) in Each())
            {
                var given = In(c);
                double[] contact = Numbers(given["contact"]), source = Numbers(c["source"]);
                ShadowPlaneModel.Result m = ShadowPlaneModel.Evaluate(
                    contact[0], contact[1], contact[2], source[0], source[1], source[2],
                    D(given["ground"]), D(given["radius"]), D(given["height"]), D(given["max_stretch"]));
                SameModel(m, O(c["model"]), at);
            }
        }

        [Test]
        public void A_sun_is_written_as_the_reference_writes_it_and_casts_the_same_model()
        {
            int suns = 0;
            foreach (var (c, at) in Each())
            {
                var given = In(c);
                if (given["direction"] == null) continue;
                double[] contact = Numbers(given["contact"]), d = Numbers(given["direction"]);
                double radius = D(given["radius"]), height = D(given["height"]);
                ShadowPlaneModel.FarPoint(contact[0], contact[1], contact[2], d[0], d[1], d[2],
                    Math.Max(height, 2.0 * radius), out double x, out double y, out double z);
                Assert.That(MaxDiff(new[] { x, y, z }, Numbers(c["source"])), Is.LessThanOrEqualTo(D(_tolerance["source"])),
                    at + " far point");
                ShadowPlaneModel.Result m = ShadowPlaneModel.EvaluateDirectional(
                    contact[0], contact[1], contact[2], d[0], d[1], d[2],
                    D(given["ground"]), radius, height, D(given["max_stretch"]));
                SameModel(m, O(c["model"]), at + " (directional)");
                suns++;
            }
            Assert.That(suns, Is.GreaterThan(0));
        }

        [Test]
        public void The_placement_of_the_canvas_matches_the_reference()
        {
            foreach (var (c, at) in Each())
            {
                var given = In(c);
                double[] contact = Numbers(given["contact"]), source = Numbers(c["source"]);
                double[] canvas = Numbers(given["canvas"]);
                ShadowPlaneModel.Result m = ShadowPlaneModel.Evaluate(
                    contact[0], contact[1], contact[2], source[0], source[1], source[2],
                    D(given["ground"]), D(given["radius"]), D(given["height"]), D(given["max_stretch"]));
                m.Placement(canvas[0], canvas[1], canvas[2], canvas[3],
                    out double cx, out double cz, out double along, out double across);
                var p = O(c["placement"]);
                double[] want = Numbers(p["centre"]).Concat(new[] { D(p["along"]), D(p["across"]) }).ToArray();
                Assert.That(MaxDiff(new[] { cx, cz, along, across }, want), Is.LessThanOrEqualTo(D(_tolerance["placement"])), at);
            }
        }

        [Test]
        public void The_fade_matches_the_reference_for_a_point_and_a_sun()
        {
            int fades = 0, suns = 0;
            foreach (var (c, at) in Each())
            {
                var given = In(c);
                double[] contact = Numbers(given["contact"]);
                double ground = D(given["ground"]), radius = D(given["radius"]), height = D(given["height"]);
                double maxStretch = D(given["max_stretch"]), intensity = D(given["intensity"]);
                double power = D(given["falloff_power"]), fadeHeight = D(given["fade_height"]);
                // The model's last parameter is the ramp's span in metres: the plane's
                // source_ramp times the record's unit_scale (the cases state it so).
                double unit = D(given["source_ramp"]);
                double got;
                if (given["direction"] != null)
                {
                    double[] d = Numbers(given["direction"]);
                    got = ShadowPlaneModel.FadeDirectional(contact[0], contact[1], contact[2], d[0], d[1], d[2],
                        ground, radius, height, maxStretch, intensity, power, fadeHeight, unit);
                    suns++;
                }
                else
                {
                    double[] l = Numbers(given["light"]);
                    got = ShadowPlaneModel.Fade(contact[0], contact[1], contact[2], l[0], l[1], l[2],
                        ground, radius, height, maxStretch, intensity, power, fadeHeight, unit);
                }
                Assert.That(Math.Abs(got - D(c["fade"])), Is.LessThanOrEqualTo(D(_tolerance["fade"])), at + " fade");
                Assert.That(got, Is.InRange(0.0, Math.Max(intensity, 0.0)), at + " the fade never exceeds the intensity");
                fades++;
            }
            Assert.That(fades, Is.GreaterThan(0));
            Assert.That(suns, Is.GreaterThan(0), "a sun's fade, from its far point");
            Assert.That(_cases.Select(c => c["name"] as string).Where(n => n.StartsWith("fade_")), Is.Not.Empty,
                "the fade's own cases: one per term that decides it");
        }

        [Test]
        public void A_zero_direction_is_noon()
        {
            ShadowPlaneModel.FarPoint(1.0, 2.0, 3.0, 0.0, 0.0, 0.0, 1.0, out double x, out double y, out double z);
            Assert.That((x, y, z), Is.EqualTo((1.0, 2.0 + ShadowPlaneModel.FarFactor, 3.0)));
        }

        // ---- the document ------------------------------------------------------
        IEnumerable<(IDictionary<string, object>, string)> Each()
            => _cases.Select((c, i) => (c, $"case {i} ({c["name"]})"));

        void SameModel(ShadowPlaneModel.Result m, IDictionary<string, object> e, string at)
        {
            double[] got =
            {
                m.anchorX, m.anchorZ, m.bearingX, m.bearingZ, m.kBase, m.kTop, m.reach, m.baseRadius, m.topRadius, m.width,
            };
            double[] want = Numbers(e["anchor"]).Concat(Numbers(e["bearing"]))
                .Concat(new[] { D(e["k_base"]), D(e["k_top"]), D(e["reach"]), D(e["base"]), D(e["top"]), D(e["width"]) })
                .ToArray();
            Assert.That(MaxDiff(got, want), Is.LessThanOrEqualTo(D(_tolerance["model"])), at);
            Assert.That(m.overhead, Is.EqualTo((bool)e["overhead"]), at + " overhead");
        }

        static IDictionary<string, object> In(IDictionary<string, object> c) => O(c["input"]);
        static IDictionary<string, object> O(object o) => (IDictionary<string, object>)o;
        static IList<object> L(object o) => (IList<object>)o;
        static double D(object o) => (double)o;
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
