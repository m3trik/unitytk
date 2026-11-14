using System;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Models.Tests;
using NUnit.Framework;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The ArticulatedRig feature's model (<see cref="ArticulationModel"/>, its
    /// Model part) held to pythontk's golden cases
    /// (<c>ptk.Conformance.cases("articulation")</c>): every case's local pose and
    /// rig-space world, the state read back from its locals toward a wound hint,
    /// and every grab solve with and without a hand's rotation, each within the
    /// document's own tolerances (quaternions sign-insensitive) -- the anchored
    /// rigs of articulation v2 (an anchored arm, a flat model chained only by its
    /// parents, a tree under anchors) by name. And the model is the same in
    /// Unity's axes: a rig stated mirrored, as Unity's FBX import mirrors X, poses,
    /// reads and solves as the mirror of the reference, so the components hold
    /// their values in Unity's terms and nothing converts at run time. The same
    /// cases run in Unity through test_articulated_rig_runtime.py.
    /// </summary>
    public class ArticulationModelTests
    {
        // The Unity-side suite's draw (test_articulated_rig_runtime.py SEED, PER_RIG).
        const int Seed = 0;
        const string Options = "{\"per_rig\": 4}";

        IDictionary<string, object> _doc;
        IDictionary<string, object> _tolerance;
        List<IDictionary<string, object>> _cases;
        Dictionary<string, ArticulationModel> _models;

        [OneTimeSetUp]
        public void Generate()
        {
            _doc = Conformance.Cases("articulation", Seed, Options);
            _tolerance = O(_doc["tolerance"]);
            _cases = L(_doc["cases"]).Select(O).ToList();
            _models = O(_doc["rigs"]).ToDictionary(r => r.Key, r => new ArticulationModel(O(r.Value)));
            Assert.That(_cases, Is.Not.Empty, "the reference generated no case");
        }

        [Test]
        public void Pose_and_world_match_the_reference()
        {
            foreach (var (c, at) in Each()) PoseAndWorld(c, at);
        }

        [Test]
        public void Read_unwraps_toward_the_hint_as_the_reference_does()
        {
            foreach (var (c, at) in Each()) Read(c, at);
        }

        [Test]
        public void Every_solve_matches_the_reference()
        {
            int count = 0;
            foreach (var (c, at) in Each()) count += Solves(c, at);
            Assert.That(count, Is.GreaterThan(0));
        }

        /// <summary>
        /// The rigs of articulation v2, each by name: hinges, slides and a ball about
        /// anchors off their pivots; a flat model, every rest the root's, its hinges in
        /// its anchors and chained by its parents alone; a branch under anchors; and the
        /// anchors on the identity's edge -- a pure offset (AddJoint's: the anchor at the
        /// part's pivot), a pure turn, a hair off the identity in each -- and the anchors
        /// unnormalised.
        /// </summary>
        [TestCase("anchored")]
        [TestCase("flat")]
        [TestCase("anchored_tree")]
        [TestCase("offset_only")]
        [TestCase("turn_only")]
        [TestCase("hair_off_t")]
        [TestCase("hair_off_q")]
        [TestCase("unnormalised_anchor")]
        public void An_anchored_rig_poses_reads_and_solves_as_the_reference(string rig)
        {
            var cases = Each().Where(e => (string)e.Item1["rig"] == rig).ToList();
            Assert.That(cases, Is.Not.Empty, $"the reference has no '{rig}' case");
            int solves = 0;
            foreach (var (c, at) in cases)
            {
                PoseAndWorld(c, at);
                Read(c, at);
                solves += Solves(c, at);
            }
            Assert.That(solves, Is.GreaterThan(0));
            ArticulationModel model = _models[rig];
            bool anchored = false;
            for (int j = 0; j < model.JointCount; j++) anchored |= model.Joints[j].Anchored;
            Assert.That(anchored, Is.True, $"'{rig}' carries anchors off the identity");
        }

        /// <summary>
        /// The identity spelt as -q and as an unnormalised q: the anchorless path, as the
        /// reference reads it, posing, reading and solving as the reference does.
        /// </summary>
        [TestCase("negated_identity")]
        [TestCase("unnormalised_identity")]
        public void The_identity_however_spelt_is_the_anchorless_path(string rig)
        {
            var cases = Each().Where(e => (string)e.Item1["rig"] == rig).ToList();
            Assert.That(cases, Is.Not.Empty, $"the reference has no '{rig}' case");
            foreach (var (c, at) in cases)
            {
                PoseAndWorld(c, at);
                Read(c, at);
                Solves(c, at);
            }
            ArticulationModel model = _models[rig];
            for (int j = 0; j < model.JointCount; j++)
                Assert.That(model.Joints[j].Anchored, Is.False, $"'{rig}' joint {j}");
        }

        [Test]
        public void The_flat_rig_chains_by_its_parents_with_every_rest_the_roots()
        {
            ArticulationModel flat = _models["flat"];
            for (int j = 0; j < flat.JointCount; j++)
            {
                Assert.That(flat.Joints[j].parent, Is.EqualTo(j - 1), "a chain");
                Assert.That(Math.Abs(flat.Joints[j].t.x) + Math.Abs(flat.Joints[j].t.y) + Math.Abs(flat.Joints[j].t.z), Is.Zero);
            }
            // The tip moves when the root turns: the chain, not the scene, carries it.
            double[] rest = flat.RestState();
            double[] turned = flat.RestState();
            turned[0] = 40.0;
            var a = flat.World(rest)[flat.JointCount - 1];
            var b = flat.World(turned)[flat.JointCount - 1];
            Assert.That(CloseQuat(a.q, b.q), Is.GreaterThan(0.1), "the root's turn reaches the tip");
        }

        [Test]
        public void An_identity_anchor_is_the_anchorless_path()
        {
            for (int j = 0; j < _models["arm"].JointCount; j++)
                Assert.That(_models["arm"].Joints[j].Anchored, Is.False, "the arm's joints sit on their pivots");
        }

        /// <summary>
        /// Every rig stated as Unity's FBX import lands it (X mirrored: a point
        /// (x, y, z) is (-x, y, z), a rotation (x, y, z, w) is (x, -y, -z, w), so ry, rz
        /// and tx turn and slide the other way, their limits swapped and negated):
        /// its poses are the mirror of the reference's, its read the reference's with
        /// those channels negated, and every solve the reference's the same way.
        /// </summary>
        [Test]
        public void A_rig_stated_in_unitys_axes_is_the_mirror_of_the_reference()
        {
            var rigs = O(_doc["rigs"]);
            var mirrored = rigs.ToDictionary(r => r.Key, r => new ArticulationModel(MirrorRig(O(r.Value))));
            int solves = 0;
            foreach (var (c, at) in Each())
            {
                string rig = (string)c["rig"];
                ArticulationModel model = mirrored[rig];
                double[] sign = Signs(_models[rig]);
                double[] state = Flip(Numbers(c["state"]), sign);
                double tol = D(_tolerance["pose"]);
                Same(model.Pose(state), MirrorFrames(L(c["pose"])), tol, at + " mirrored pose");
                Same(model.World(state), MirrorFrames(L(c["world"])), tol, at + " mirrored world");

                var read = O(c["read"]);
                double[] got = model.Read(Frames(MirrorFrames(L(read["locals"]))), Flip(Numbers(read["hint"]), sign));
                Assert.That(MaxDiff(got, Flip(Numbers(read["expect"]), sign)), Is.LessThanOrEqualTo(D(_tolerance["read"])),
                    at + " mirrored read");

                foreach (var raw in L(c["solve"]))
                {
                    var s = O(raw);
                    ArticulationModel.Quat? rotation = s["rotation"] == null
                        ? (ArticulationModel.Quat?)null
                        : MirrorQ(Quat(s["rotation"]));
                    double[] solved = model.Solve(Flip(Numbers(s["from"]), sign), (int)D(s["joint"]), MirrorV(Vec(s["local"])),
                                                  MirrorV(Vec(s["target"])), rotation);
                    Assert.That(MaxDiff(solved, Flip(Numbers(s["expect"]), sign)), Is.LessThanOrEqualTo(D(_tolerance["solve"])),
                        $"{at} mirrored solve {solves}");
                    solves++;
                }
            }
            Assert.That(solves, Is.GreaterThan(0));
        }

        /// <summary>
        /// A grab's per-frame work allocates nothing once warmed: a solve into the caller's
        /// state (with and without a hand's rotation, the wrist split included), a pose, a
        /// world and a read, each into the caller's buffers -- the model's scratch is its own.
        /// </summary>
        [Test]
        public void A_warmed_solve_pose_and_read_allocate_nothing()
        {
            foreach (var (rig, model) in _models.Select(m => (m.Key, m.Value)))
            {
                var c = Each().Select(e => e.Item1).FirstOrDefault(e => (string)e["rig"] == rig);
                if (c == null) continue;
                var solves = L(c["solve"]).Select(O).Select(s => (
                    from: Numbers(s["from"]), joint: (int)D(s["joint"]), local: Vec(s["local"]), target: Vec(s["target"]),
                    rotation: s["rotation"] == null ? (ArticulationModel.Quat?)null : Quat(s["rotation"]))).ToArray();
                double[] state = Numbers(c["state"]);
                var into = new double[model.ChannelCount];
                var frames = new ArticulationModel.Frame[model.JointCount];
                void Frame()
                {
                    foreach (var s in solves) model.Solve(s.from, s.joint, s.local, s.target, s.rotation, into);
                    model.Pose(state, frames);
                    model.World(state, frames);
                    model.Read(frames, into, into);
                }
                Frame();  // warm: a first call may JIT
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int round = 0; round < 3; round++) Frame();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.Zero, $"'{rig}': {allocated} bytes allocated by warmed solves, poses and reads");

                // And the solve into the caller's state is the solve that returns one, bit for bit.
                foreach (var s in solves)
                {
                    model.Solve(s.from, s.joint, s.local, s.target, s.rotation, into);
                    Assert.That(into, Is.EqualTo(model.Solve(s.from, s.joint, s.local, s.target, s.rotation)));
                }
            }
        }

        /// <summary>
        /// The one mirror table the importer and these tests share is the one the reviewer's
        /// stress script proves over 400 random rigs (one-sided limits, anchors, every channel):
        /// a point (x, y, z) is (-x, y, z), a rotation (x, y, z, w) is (x, -y, -z, w), and only
        /// ry, rz and tx run the other way, their limits negated and swapped.
        /// </summary>
        [Test]
        public void The_mirror_table_is_the_one_the_stress_script_proves()
        {
            ArticulationModel.Vec v = ArticulationModel.Mirror(new ArticulationModel.Vec(1, 2, 3));
            Assert.That((v.x, v.y, v.z), Is.EqualTo((-1.0, 2.0, 3.0)));
            ArticulationModel.Quat q = ArticulationModel.Mirror(new ArticulationModel.Quat(1, 2, 3, 4));
            Assert.That((q.x, q.y, q.z, q.w), Is.EqualTo((1.0, -2.0, -3.0, 4.0)));
            Assert.That(ArticulationModel.ChannelNames.Where((_, code) => ArticulationModel.MirrorFlips(code)),
                        Is.EqualTo(new[] { "ry", "rz", "tx" }));
            ArticulationModel.MirrorLimits(1, null, 30.0, out double? lo, out double? hi);
            Assert.That((lo, hi), Is.EqualTo(((double?)-30.0, (double?)null)), "ry: a max alone is a min alone");
            ArticulationModel.MirrorLimits(3, -0.2, null, out lo, out hi);
            Assert.That((lo, hi), Is.EqualTo(((double?)null, (double?)0.2)), "tx: a min alone is a max alone");
            ArticulationModel.MirrorLimits(0, -10.0, 20.0, out lo, out hi);
            Assert.That((lo, hi), Is.EqualTo(((double?)-10.0, (double?)20.0)), "rx: kept");
        }

        [Test]
        public void A_record_payload_reads_through_its_generated_shape()
        {
            var rigs = O(_doc["rigs"]);
            var payload = new Dictionary<string, object>
            {
                ["version"] = (double)ArticulationRecord.CurrentVersion,
                ["rigs"] = new List<object> { rigs["arm"], rigs["tree"] },
            };
            Assert.That(ArticulationModel.FromRecord(payload).name, Is.EqualTo("arm"), "the first rig by default");
            Assert.That(ArticulationModel.FromRecord(payload, "tree").JointCount, Is.EqualTo(_models["tree"].JointCount));
            Assert.Throws<KeyNotFoundException>(() => ArticulationModel.FromRecord(payload, "no such rig"));

            var cut = new Dictionary<string, object>(O(rigs["arm"]));
            cut.Remove("grab");
            Assert.Throws<ArgumentException>(() => new ArticulationModel(cut), "a rig that fails the declared shape");
        }

        [Test]
        public void A_joint_without_an_anchor_is_a_version_1_rigs_and_is_refused()
        {
            var arm = new Dictionary<string, object>(O(O(_doc["rigs"])["arm"]));
            var joints = L(arm["joints"]).Select(j => (object)new Dictionary<string, object>(O(j))).ToList();
            ((IDictionary<string, object>)joints[1]).Remove("anchor");
            arm["joints"] = joints;
            var e = Assert.Throws<ArgumentException>(() => new ArticulationModel(arm));
            Assert.That(e.Message, Does.Contain("anchor").And.Contain("re-export"));
        }

        // ---- the cases ---------------------------------------------------------
        void PoseAndWorld(IDictionary<string, object> c, string at)
        {
            var model = _models[(string)c["rig"]];
            double[] state = Numbers(c["state"]);
            Same(model.Pose(state), L(c["pose"]), D(_tolerance["pose"]), at + " pose");
            Same(model.World(state), L(c["world"]), D(_tolerance["world"]), at + " world");
        }

        void Read(IDictionary<string, object> c, string at)
        {
            var read = O(c["read"]);
            double[] got = _models[(string)c["rig"]].Read(Frames(read["locals"]), Numbers(read["hint"]));
            Assert.That(MaxDiff(got, Numbers(read["expect"])), Is.LessThanOrEqualTo(D(_tolerance["read"])), at + " read");
        }

        int Solves(IDictionary<string, object> c, string at)
        {
            var model = _models[(string)c["rig"]];
            int count = 0;
            foreach (var raw in L(c["solve"]))
            {
                var s = O(raw);
                ArticulationModel.Quat? rotation = s["rotation"] == null ? (ArticulationModel.Quat?)null : Quat(s["rotation"]);
                double[] got = model.Solve(Numbers(s["from"]), (int)D(s["joint"]), Vec(s["local"]), Vec(s["target"]), rotation);
                Assert.That(MaxDiff(got, Numbers(s["expect"])), Is.LessThanOrEqualTo(D(_tolerance["solve"])),
                    $"{at} solve {count} (joint {D(s["joint"])}, rotation {(rotation.HasValue ? "yes" : "no")})");
                count++;
            }
            return count;
        }

        // ---- the mirror (Unity's FBX import): the model's own table -------------
        static bool Flips(string channel) => ArticulationModel.MirrorFlips(Array.IndexOf(ArticulationModel.ChannelNames, channel));

        static IDictionary<string, object> MirrorRig(IDictionary<string, object> rig)
        {
            var copy = new Dictionary<string, object>(rig);
            copy["joints"] = L(rig["joints"]).Select(raw =>
            {
                var joint = new Dictionary<string, object>(O(raw));
                joint["t"] = MirrorT(L(joint["t"]));
                joint["q"] = MirrorQList(L(joint["q"]));
                var anchor = O(joint["anchor"]);
                joint["anchor"] = new Dictionary<string, object>
                {
                    ["t"] = MirrorT(L(anchor["t"])),
                    ["q"] = MirrorQList(L(anchor["q"])),
                };
                joint["channels"] = L(joint["channels"]).Select(rawChannel =>
                {
                    var channel = new Dictionary<string, object>(O(rawChannel));
                    int code = Array.IndexOf(ArticulationModel.ChannelNames, (string)channel["channel"]);
                    ArticulationModel.MirrorLimits(code, channel["min"] == null ? (double?)null : D(channel["min"]),
                                                   channel["max"] == null ? (double?)null : D(channel["max"]),
                                                   out double? lo, out double? hi);
                    channel["min"] = lo.HasValue ? (object)lo.Value : null;
                    channel["max"] = hi.HasValue ? (object)hi.Value : null;
                    return (object)channel;
                }).ToList();
                return (object)joint;
            }).ToList();
            return copy;
        }

        static List<object> MirrorT(IList<object> t)
        {
            ArticulationModel.Vec v = MirrorV(new ArticulationModel.Vec(D(t[0]), D(t[1]), D(t[2])));
            return new List<object> { v.x, v.y, v.z };
        }

        static List<object> MirrorQList(IList<object> q)
        {
            ArticulationModel.Quat m = MirrorQ(new ArticulationModel.Quat(D(q[0]), D(q[1]), D(q[2]), D(q[3])));
            return new List<object> { m.x, m.y, m.z, m.w };
        }

        static ArticulationModel.Vec MirrorV(ArticulationModel.Vec v) => ArticulationModel.Mirror(v);
        static ArticulationModel.Quat MirrorQ(ArticulationModel.Quat q) => ArticulationModel.Mirror(q);

        static IList<object> MirrorFrames(IList<object> frames)
            => frames.Select(f => (object)new List<object> { MirrorT(L(L(f)[0])), MirrorQList(L(L(f)[1])) }).ToList();

        static double[] Signs(ArticulationModel model)
        {
            var sign = new double[model.ChannelCount];
            for (int s = 0; s < sign.Length; s++) sign[s] = Flips(model.ChannelName(s)) ? -1.0 : 1.0;
            return sign;
        }

        static double[] Flip(double[] state, double[] sign) => state.Select((v, i) => v * sign[i]).ToArray();

        // ---- the document ------------------------------------------------------
        IEnumerable<(IDictionary<string, object>, string)> Each()
            => _cases.Select((c, i) => (c, $"case {i} ({c["rig"]})"));

        static IDictionary<string, object> O(object o) => (IDictionary<string, object>)o;
        static IList<object> L(object o) => (IList<object>)o;
        static double D(object o) => (double)o;
        static double[] Numbers(object o) => L(o).Select(D).ToArray();
        static ArticulationModel.Vec Vec(object o) { var l = L(o); return new ArticulationModel.Vec(D(l[0]), D(l[1]), D(l[2])); }
        static ArticulationModel.Quat Quat(object o) { var l = L(o); return new ArticulationModel.Quat(D(l[0]), D(l[1]), D(l[2]), D(l[3])); }
        static ArticulationModel.Frame[] Frames(object o)
            => L(o).Select(f => new ArticulationModel.Frame(Vec(L(f)[0]), Quat(L(f)[1]))).ToArray();

        static double MaxDiff(IReadOnlyList<double> a, IReadOnlyList<double> b)
        {
            Assert.That(a.Count, Is.EqualTo(b.Count), "one value per channel");
            double most = 0.0;
            for (int i = 0; i < a.Count; i++) most = Math.Max(most, Math.Abs(a[i] - b[i]));
            return most;
        }

        // q and -q are one rotation.
        static double CloseQuat(ArticulationModel.Quat q, ArticulationModel.Quat e)
        {
            double[] a = { q.x, q.y, q.z, q.w }, b = { e.x, e.y, e.z, e.w };
            double minus = 0.0, plus = 0.0;
            for (int i = 0; i < 4; i++)
            {
                minus = Math.Max(minus, Math.Abs(a[i] - b[i]));
                plus = Math.Max(plus, Math.Abs(a[i] + b[i]));
            }
            return Math.Min(minus, plus);
        }

        static void Same(ArticulationModel.Frame[] got, IList<object> expect, double tolerance, string at)
        {
            Assert.That(got.Length, Is.EqualTo(expect.Count), at + ": one frame per joint");
            for (int j = 0; j < got.Length; j++)
            {
                var e = L(expect[j]);
                ArticulationModel.Vec t = Vec(e[0]);
                Assert.That(MaxDiff(new[] { got[j].t.x, got[j].t.y, got[j].t.z }, new[] { t.x, t.y, t.z }),
                    Is.LessThanOrEqualTo(tolerance), $"{at} joint {j}: t {got[j].t} != {t}");
                Assert.That(CloseQuat(got[j].q, Quat(e[1])), Is.LessThanOrEqualTo(tolerance),
                    $"{at} joint {j}: q {got[j].q} != {Quat(e[1])}");
            }
        }
    }
}
