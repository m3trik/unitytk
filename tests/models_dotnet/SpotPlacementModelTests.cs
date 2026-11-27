using System;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Models.Tests;
using NUnit.Framework;

namespace M3trik.Unitytk.Placement
{
    /// <summary>
    /// The Placement feature's model (<see cref="SpotPlacementModel"/>, its Model part) held to pythontk's golden cases
    /// (<c>ptk.Conformance.cases("spot_placement")</c>): every named scene (each pins one rule) and the seeded random
    /// rooms, through the model over the same boxes (<see cref="BoxRays"/>), within the document's tolerances. Unity's
    /// physics adapter is held to the box scenes in PlacerTests.
    /// </summary>
    public class SpotPlacementModelTests
    {
        const int Seed = 7;
        const string Options = "{\"per_kind\": 12}";

        IDictionary<string, object> _tolerance;
        List<IDictionary<string, object>> _cases;

        [OneTimeSetUp]
        public void Generate()
        {
            IDictionary<string, object> doc = Conformance.Cases("spot_placement", Seed, Options);
            _tolerance = O(doc["tolerance"]);
            _cases = L(doc["cases"]).Select(O).ToList();
            Assert.That(_cases.Count, Is.GreaterThan(36), "the named scenes and 12 rooms a purpose");
        }

        [Test]
        public void Every_golden_case_places_as_the_reference_does()
        {
            var failures = new List<string>();
            foreach (var c in _cases)
            {
                bool found = Place(c, out SpotPlacementModel.Vec position, out SpotPlacementModel.Vec aim);
                var expected = O(c["expected"]);
                double[] p = Numbers(expected["position"]), a = Numbers(expected["aim"]);
                double dp = Far(position, p), da = Far(aim, a);
                if (found != (bool)expected["found"] || dp > D(_tolerance["position"]) || da > D(_tolerance["aim"]))
                    failures.Add($"{c["name"]}: found {found} (reference {expected["found"]}), position {position} off by {dp:G3}, aim {aim} off by {da:G3}");
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void Without_rays_a_first_pose_is_the_plain_one()
        {
            var place = new SpotPlacementModel();
            var box = new SpotPlacementModel.Box(new SpotPlacementModel.Vec(-0.1, 1.0, 0.4), new SpotPlacementModel.Vec(0.1, 1.2, 0.6));
            var room = new SpotPlacementModel.Box(new SpotPlacementModel.Vec(-1, 0, -1), new SpotPlacementModel.Vec(1, 2.5, 1));
            Assert.That(place.Stand(box, out SpotPlacementModel.Vec feet, out _, room), Is.True);
            Assert.That((feet.x, feet.y, feet.z), Is.EqualTo((0.0, 0.0, 0.5 + 0.1 + SpotPlacementModel.StandOff)));
        }

        static bool Place(IDictionary<string, object> c, out SpotPlacementModel.Vec position, out SpotPlacementModel.Vec aim)
        {
            BoxRays rays = null;
            var boxes = L(c["boxes"]).Select(O).ToList();
            if (boxes.Count > 0)
            {
                rays = new BoxRays();
                foreach (var b in boxes) rays.Add((string)b["owner"], V(b["low"]), V(b["high"]));
            }
            var place = new SpotPlacementModel(rays, (int)Opt(c, "up", 1.0), Opt(c, "unit", 1.0));
            var owners = new HashSet<object>(c.TryGetValue("owners", out object o) && o != null ? L(o).Cast<object>() : Enumerable.Empty<object>());
            Func<object, bool> mine = owners.Contains;
            SpotPlacementModel.Box target = Bx(c["target"]);
            SpotPlacementModel.Box? delivery = c.TryGetValue("delivery", out object d) && d != null ? Bx(d) : (SpotPlacementModel.Box?)null;
            SpotPlacementModel.Vec? eyes = c.TryGetValue("eyes", out object e) && e != null ? V(e) : (SpotPlacementModel.Vec?)null;
            switch ((string)c["purpose"])
            {
                case "stand":
                    var also = new List<(SpotPlacementModel.Box, Func<object, bool>)>();
                    if (c.TryGetValue("also", out object al) && al != null)
                        foreach (var item in L(al).Select(O))
                        {
                            var its = new HashSet<object>(L(item["owners"]).Cast<object>());
                            also.Add((Bx(item), its.Contains));
                        }
                    var keepOut = c.TryGetValue("keep_out", out object k) && k != null ? L(k).Select(Bx).ToList() : new List<SpotPlacementModel.Box>();
                    return place.Stand(target, out position, out aim, delivery,
                                       floor: c.TryGetValue("floor", out object f) && f != null ? D(f) : (double?)null,
                                       owners: mine,
                                       side: c.TryGetValue("side", out object s) && s != null ? V(s) : (SpotPlacementModel.Vec?)null,
                                       near: c.TryGetValue("near", out object n) && n != null ? V(n) : (SpotPlacementModel.Vec?)null,
                                       also: also, keepOut: keepOut, fieldOfView: Opt(c, "field_of_view", 0.0),
                                       grounded: c.TryGetValue("grounded", out object g) && g != null ? (bool)g : (bool?)null);
                case "view":
                    return place.View(target, out position, out aim, delivery, eyes, mine);
                default:
                    return place.Point(target, out position, out aim, delivery, eyes, mine, travel: Opt(c, "travel", 0.0));
            }
        }

        static double Far(SpotPlacementModel.Vec v, double[] r) => Math.Sqrt((v.x - r[0]) * (v.x - r[0]) + (v.y - r[1]) * (v.y - r[1]) + (v.z - r[2]) * (v.z - r[2]));
        static double Opt(IDictionary<string, object> c, string key, double fallback) => c.TryGetValue(key, out object v) && v != null ? D(v) : fallback;
        static IDictionary<string, object> O(object v) => (IDictionary<string, object>)v;
        static IList<object> L(object v) => (IList<object>)v;
        static double D(object v) => Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture);
        static double[] Numbers(object v) => L(v).Select(D).ToArray();

        static SpotPlacementModel.Vec V(object v)
        {
            double[] n = Numbers(v);
            return new SpotPlacementModel.Vec(n[0], n[1], n[2]);
        }

        static SpotPlacementModel.Box Bx(object v) => new SpotPlacementModel.Box(V(O(v)["low"]), V(O(v)["high"]));
    }
}
