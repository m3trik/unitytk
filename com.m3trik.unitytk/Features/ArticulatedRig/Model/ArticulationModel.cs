// ----------------------------------------------------------------------------
// The ArticulatedRig feature's model: pythontk's ``ArticulationModel`` ported
// operation for operation, engine-free (its assembly references no UnityEngine),
// so the same code runs in a player, in the editor and under `dotnet test`.
//
// A joint's local transform is rest . A . pose(channels) . A^-1, where A is its
// ANCHOR (a point a.t and an orientation a.q in the rest frame) and pose is the
// slide after the turn E = Euler(rotate_order; rx, ry, rz):
//
//   C           = a.q (x) E (x) a.q^-1
//   rotation    = q (x) C
//   translation = t + q . (a.t - C . a.t + a.q . (tx, ty, tz))
//
// which, for the identity anchor, is q (x) E and t + q . (tx, ty, tz) -- taken
// on its own path, so a rig on its pivots poses bit for bit as before anchors.
// A joint's rig-space pose composes through its parent JOINT, never its part's
// scene parent, so a flat model chains. The port is pinned against the
// reference by the golden cases of ``ptk.Conformance.cases("articulation")``:
// under `dotnet test` in tests/models_dotnet, and in Unity by
// test_articulated_rig_runtime.py.
//
// The model is the same in any axes a mirror relates: stated in Unity's (X
// mirrored, as Unity's FBX import lands a file), it poses, reads and solves as
// the mirror of the reference, so the components hold their values in Unity's
// terms and nothing converts at run time (ArticulationModelTests pins it).
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;
using M3trik.Unitytk.Core;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// pythontk <c>ArticulationModel</c>, ported operation for operation: one
    /// rig's joints, their anchors, channels and limits, forward kinematics
    /// through the joint chain, reading a state back from local transforms, and
    /// the grab solver (damped least squares over the channels between the root
    /// and the held link, with limits never left, and the wrist split for a link
    /// hanging off a ball joint).
    ///
    /// Degrees for rotation channels, the rig's lengths for slides, quaternions
    /// (x, y, z, w) where <c>a (x) b</c> applies b first, double precision. One
    /// model is not for several threads at once: its forward pass, its read and its
    /// solve reuse its own scratch, so each into the caller's buffers allocates
    /// nothing (a grab's every frame). A constant or a step changed in the
    /// reference is a change here: the golden cases fail until it is ported.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>ArticulationBody</c> simulates jointed bodies under
    /// forces in PhysX, not a pose a hand sets on imported transforms, and has no
    /// notion of a rotate order or per-channel limits; Animation Rigging's
    /// constraints solve fixed chains (two-bone, chain IK) with neither the
    /// record's channels nor its limits. Neither reproduces the reference, and the
    /// readers of one export must pose alike, so the reference is ported and
    /// held to its golden cases.
    /// </remarks>
    public sealed class ArticulationModel
    {
        // ---------------------------------------------------------------- types
        /// <summary>A 3-vector in doubles.</summary>
        [Serializable]
        public struct Vec
        {
            /// <summary>The components.</summary>
            public double x, y, z;
            /// <summary>The vector (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).</summary>
            public Vec(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
            /// <summary>Component <paramref name="i"/> (0 = x, 1 = y, 2 = z).</summary>
            public double this[int i] => i == 0 ? x : (i == 1 ? y : z);
            /// <inheritdoc/>
            public override string ToString()
                => $"({x.ToString("R", CultureInfo.InvariantCulture)}, {y.ToString("R", CultureInfo.InvariantCulture)}, {z.ToString("R", CultureInfo.InvariantCulture)})";
        }

        /// <summary>A quaternion (x, y, z, w) in doubles.</summary>
        [Serializable]
        public struct Quat
        {
            /// <summary>The components.</summary>
            public double x, y, z, w;
            /// <summary>The quaternion (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>, <paramref name="w"/>).</summary>
            public Quat(double x, double y, double z, double w) { this.x = x; this.y = y; this.z = z; this.w = w; }
            /// <inheritdoc/>
            public override string ToString()
                => $"({x.ToString("R", CultureInfo.InvariantCulture)}, {y.ToString("R", CultureInfo.InvariantCulture)}, {z.ToString("R", CultureInfo.InvariantCulture)}, {w.ToString("R", CultureInfo.InvariantCulture)})";
        }

        /// <summary>A translation and a rotation: a joint's local transform, or its rig-space pose.</summary>
        public struct Frame
        {
            /// <summary>The translation.</summary>
            public Vec t;
            /// <summary>The rotation.</summary>
            public Quat q;
            /// <summary>The frame at <paramref name="t"/>, turned by <paramref name="q"/>.</summary>
            public Frame(Vec t, Quat q) { this.t = t; this.q = q; }
        }

        /// <summary>One joint of the rig.</summary>
        public sealed class Joint
        {
            /// <summary>The joint's name (the join key to its part).</summary>
            public readonly string name;
            /// <summary>An earlier joint's index, or -1 for a root (its parent is rig space).</summary>
            public readonly int parent;
            /// <summary>Rest translate in the parent joint's frame.</summary>
            public readonly Vec t;
            /// <summary>Rest rotation in the parent joint's frame, normalised.</summary>
            public readonly Quat q;
            /// <summary>The anchor's point, in the rest frame: what the joint turns about.</summary>
            public readonly Vec anchorT;
            /// <summary>The anchor's orientation in the rest frame, normalised: the axes its channels turn about and slide along.</summary>
            public readonly Quat anchorQ;
            /// <summary>The rotate order, e.g. "xyz" (x applied first).</summary>
            public readonly string rotateOrder;

            internal readonly bool anchored;       // false: the identity anchor's path
            internal readonly int[] order;          // axis per letter, innermost first
            internal readonly int[] slots;          // this joint's state slots, record order
            internal readonly int[] slotChannels;   // channel code per slot: 0-2 rx..rz, 3-5 tx..tz

            internal Joint(string name, int parent, Vec t, Quat q, Vec anchorT, Quat anchorQ, string rotateOrder,
                           int[] slots, int[] slotChannels)
            {
                this.name = name;
                this.parent = parent;
                this.t = t;
                this.q = q;
                this.anchorT = anchorT;
                this.anchorQ = anchorQ;
                anchored = !IsIdentity(anchorT, anchorQ);
                this.rotateOrder = rotateOrder;
                order = new int[3];
                for (int i = 0; i < 3; i++) order[i] = rotateOrder[i] - 'x';
                this.slots = slots;
                this.slotChannels = slotChannels;
            }

            /// <summary>True when the anchor is not the identity: the joint turns about a point or axes off its pivot's.</summary>
            public bool Anchored => anchored;

            /// <summary>True when the joint carries all three rotation channels (a ball).</summary>
            public bool IsBall
            {
                get
                {
                    bool x = false, y = false, z = false;
                    foreach (int c in slotChannels) { x |= c == 0; y |= c == 1; z |= c == 2; }
                    return x && y && z;
                }
            }
        }

        // ------------------------------------------------------------ constants
        /// <summary>Solver iterations per position solve; it stops early once within <see cref="Tolerance"/>.</summary>
        public const int Iterations = 32;
        /// <summary>Position solves of a wrist split.</summary>
        public const int Passes = 2;
        /// <summary>The damping, as a fraction of the chain's reach.</summary>
        public const double Damping = 0.05;
        /// <summary>Converged within this fraction of the reach.</summary>
        public const double Tolerance = 1.0e-4;
        /// <summary>The furthest one iteration chases the target, as a fraction of the reach.</summary>
        public const double MaxStep = 0.25;
        /// <summary>Halvings a step may take to bring the held point nearer its target: a step that would leave it further is never taken.</summary>
        public const int Halvings = 6;
        /// <summary>A length below this is no length.</summary>
        public const double Eps = 1.0e-9;

        /// <summary>The channels a joint may carry: rotations first, in axis order, then slides.</summary>
        public static readonly string[] ChannelNames = { "rx", "ry", "rz", "tx", "ty", "tz" };
        /// <summary>The rotate orders, in their enum order (0-5).</summary>
        public static readonly string[] RotateOrders = { "xyz", "yzx", "zxy", "xzy", "yxz", "zyx" };

        /// <summary>The identity rotation.</summary>
        public static readonly Quat Identity = new Quat(0.0, 0.0, 0.0, 1.0);
        /// <summary>The origin.</summary>
        public static readonly Vec Origin = new Vec(0.0, 0.0, 0.0);

        // CPython's math.radians / math.degrees factors, so every conversion rounds alike.
        const double DegToRad = Math.PI / 180.0;
        const double RadToDeg = 180.0 / Math.PI;

        // ---------------------------------------------------------------- state
        /// <summary>The rig's name.</summary>
        public readonly string name;

        readonly List<Joint> _joints = new List<Joint>();
        readonly List<int> _slotJoint = new List<int>();
        readonly List<int> _slotChannel = new List<int>();
        readonly List<double> _lo = new List<double>();       // NaN = unbounded
        readonly List<double> _hi = new List<double>();       // NaN = unbounded
        readonly List<double> _weights = new List<double>();

        // The forward pass's, the read's and the solve's scratch, so none allocates.
        readonly double[] _values = new double[6];
        readonly Euler _euler = new Euler();
        readonly Scratch _scratch;

        /// <summary>
        /// The model of one rig mapping of the record (a parsed
        /// <c>Json.Parse(text, false)</c> object), read through the record's declared
        /// shape (<see cref="ArticulationRecord.Shape.Rig"/>).
        /// </summary>
        /// <exception cref="ArgumentException">A rig that fails the record's
        /// declared shape (a key it requires left out -- a joint with no anchor is a
        /// version 1 record's, which is re-exported -- a value of another type, an
        /// unknown channel or rotate order), a joint whose parent is not an earlier
        /// joint, a channel named twice on one joint, or a limit whose min exceeds
        /// its max.</exception>
        public ArticulationModel(IDictionary<string, object> rig)
            : this(Shaped(rig ?? throw new ArgumentNullException(nameof(rig)), ArticulationRecord.Shape.Rig.Read))
        {
        }

        /// <summary>
        /// The model of one rig as its declared shape holds it -- read off a
        /// record, or filled by a runtime from its own components. A joint with no
        /// anchor turns on its pivot (the identity).
        /// </summary>
        /// <exception cref="ArgumentException">As <see cref="ArticulationModel(IDictionary{string, object})"/>,
        /// or a <c>t</c> / <c>q</c> holding fewer than its 3 / 4 values.</exception>
        public ArticulationModel(ArticulationRecord.Shape.Rig rig)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            name = rig.name ?? "";
            if (rig.joints != null)
            {
                for (int index = 0; index < rig.joints.Count; index++)
                    AddJoint(index, rig.joints[index] ?? new ArticulationRecord.Shape.Joint());
            }
            _scratch = new Scratch(_joints.Count, _slotJoint.Count);
        }

        /// <summary>
        /// The model of the rig <paramref name="rigName"/> (default: the first) in
        /// an <c>articulation</c> record payload.
        /// </summary>
        /// <exception cref="KeyNotFoundException">No rig by that name (or no rig at all).</exception>
        /// <exception cref="ArgumentException">A payload that fails the record's
        /// declared shape, or a rig the model refuses.</exception>
        public static ArticulationModel FromRecord(IDictionary<string, object> payload, string rigName = null)
            => FromRecord(Shaped(payload, ArticulationRecord.Shape.Payload.Read), rigName);

        /// <summary>
        /// <paramref name="value"/> read through the record's declared shape by
        /// <paramref name="read"/>; one that fails it is refused as every rig this
        /// model refuses, with an <see cref="ArgumentException"/> -- naming the fix
        /// where a joint has no anchor, a version 1 record's.
        /// </summary>
        static T Shaped<T>(IDictionary<string, object> value, Func<object, T> read)
        {
            try
            {
                return read(value);
            }
            catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException)
            {
                string fix = e.Message.Contains("'anchor'")
                    ? " A joint without an anchor is a version 1 record's: re-export it."
                    : "";
                throw new ArgumentException("The rig fails the articulation record's declared shape: " + e.Message + "." + fix, e);
            }
        }

        /// <summary>As <see cref="FromRecord(IDictionary{string, object}, string)"/>, from the read payload.</summary>
        public static ArticulationModel FromRecord(ArticulationRecord.Shape.Payload payload, string rigName = null)
        {
            foreach (var rig in payload?.rigs ?? new List<ArticulationRecord.Shape.Rig>())
            {
                if (rig != null && (rigName == null || rig.name == rigName))
                    return new ArticulationModel(rig);
            }
            throw new KeyNotFoundException($"No articulated rig '{rigName}' in the record.");
        }

        /// <summary>
        /// Whether the anchor (<paramref name="t"/>, <paramref name="q"/>) is the
        /// identity -- <c>t</c> exactly 0, <c>q</c> the identity rotation either sign,
        /// (0, 0, 0, w) for any w but 0, which normalizes to +-1 -- the anchor of a joint
        /// whose part nests on its pivot (the reference's <c>is_identity</c>).
        /// </summary>
        public static bool IsIdentity(Vec t, Quat q)
            => t.x == 0.0 && t.y == 0.0 && t.z == 0.0 && q.x == 0.0 && q.y == 0.0 && q.z == 0.0 &&
               q.w != 0.0 && !double.IsNaN(q.w) && !double.IsInfinity(q.w);

        void AddJoint(int index, ArticulationRecord.Shape.Joint spec)
        {
            string jointName = !string.IsNullOrEmpty(spec.name) ? spec.name : $"joint{index}";
            int parent = -1;
            if (spec.parent.HasValue)
            {
                parent = spec.parent.Value;
                if (!(0 <= parent && parent < index))
                    throw new ArgumentException($"Joint '{jointName}': parent {parent} is not an earlier joint.");
            }
            string order = !string.IsNullOrEmpty(spec.rotate_order) ? spec.rotate_order : "xyz";
            if (Array.IndexOf(RotateOrders, order) < 0)
                throw new ArgumentException($"Joint '{jointName}': unknown rotate order '{order}'.");
            Vec t = Vec3(spec.t, jointName, "t");
            Quat q = QNormalize(Quat4(spec.q, jointName, "q"));
            Vec anchorT = Vec3(spec.anchor?.t, jointName, "anchor t");
            Quat anchorQ = QNormalize(Quat4(spec.anchor?.q, jointName, "anchor q"));

            var slots = new List<int>();
            var codes = new List<int>();
            foreach (var raw in spec.channels ?? new List<ArticulationRecord.Shape.Channel>())
            {
                var entry = raw ?? new ArticulationRecord.Shape.Channel();
                string channel = entry.channel ?? "None";
                int code = Array.IndexOf(ChannelNames, channel);
                if (code < 0)
                    throw new ArgumentException($"Joint '{jointName}': unknown channel '{channel}'.");
                if (codes.Contains(code))
                    throw new ArgumentException($"Joint '{jointName}': channel '{channel}' named twice.");
                double lo = entry.min ?? double.NaN;
                double hi = entry.max ?? double.NaN;
                if (entry.min.HasValue && entry.max.HasValue && lo > hi)
                    throw new ArgumentException($"Joint '{jointName}': {channel} limits [{lo}, {hi}] are reversed.");
                int slot = _slotJoint.Count;
                _slotJoint.Add(index);
                _slotChannel.Add(code);
                _lo.Add(lo);
                _hi.Add(hi);
                _weights.Add(Math.Max(0.0, entry.weight ?? 1.0));
                slots.Add(slot);
                codes.Add(code);
            }
            _joints.Add(new Joint(jointName, parent, t, q, anchorT, anchorQ, order, slots.ToArray(), codes.ToArray()));
        }

        // Empty reads as the rest default, as in the reference; cut short is
        // refused here rather than indexed past its end.
        static Vec Vec3(double[] v, string joint, string what)
        {
            if (v == null || v.Length == 0) return Origin;
            if (v.Length < 3) throw new ArgumentException($"Joint '{joint}': {what} holds {v.Length} of its 3 values.");
            return new Vec(v[0], v[1], v[2]);
        }

        static Quat Quat4(double[] v, string joint, string what)
        {
            if (v == null || v.Length == 0) return Identity;
            if (v.Length < 4) throw new ArgumentException($"Joint '{joint}': {what} holds {v.Length} of its 4 values.");
            return new Quat(v[0], v[1], v[2], v[3]);
        }

        // -------------------------------------------------------------- queries
        /// <summary>The joints, parent first.</summary>
        public IReadOnlyList<Joint> Joints => _joints;
        /// <summary>How many joints the rig has.</summary>
        public int JointCount => _joints.Count;
        /// <summary>How many state slots (channels) the rig has.</summary>
        public int ChannelCount => _slotJoint.Count;
        /// <summary>The joint state slot <paramref name="slot"/> belongs to.</summary>
        public int ChannelJoint(int slot) => _slotJoint[slot];
        /// <summary>The channel ("rx" ... "tz") of state slot <paramref name="slot"/>.</summary>
        public string ChannelName(int slot) => ChannelNames[_slotChannel[slot]];
        /// <summary>The channel code (0-2 rx..rz, 3-5 tx..tz) of state slot <paramref name="slot"/>.</summary>
        public int ChannelCode(int slot) => _slotChannel[slot];

        /// <summary>The index of the joint called <paramref name="jointName"/>.</summary>
        /// <exception cref="KeyNotFoundException">No such joint.</exception>
        public int JointIndex(string jointName)
        {
            for (int i = 0; i < _joints.Count; i++)
                if (_joints[i].name == jointName) return i;
            throw new KeyNotFoundException($"No joint '{jointName}' in rig '{name}'.");
        }

        /// <summary>The joints from <paramref name="joint"/>'s root down to it, root first.</summary>
        public List<int> Chain(int joint)
        {
            var chain = new List<int>();
            for (int index = joint; index >= 0; index = _joints[index].parent) chain.Add(index);
            chain.Reverse();
            return chain;
        }

        /// <summary>Every channel at 0: the rig at rest.</summary>
        public double[] RestState() => new double[_slotJoint.Count];

        /// <summary>(min, max) of state slot <paramref name="slot"/>; null is unbounded.</summary>
        public void Limits(int slot, out double? min, out double? max)
        {
            min = double.IsNaN(_lo[slot]) ? (double?)null : _lo[slot];
            max = double.IsNaN(_hi[slot]) ? (double?)null : _hi[slot];
        }

        /// <summary>The solver weight of state slot <paramref name="slot"/>.</summary>
        public double Weight(int slot) => _weights[slot];

        /// <summary><paramref name="state"/> with every channel inside its limits.</summary>
        public double[] Clamp(double[] state)
        {
            var out_ = new double[state.Length];
            for (int s = 0; s < state.Length; s++) out_[s] = ClampSlot(s, state[s]);
            return out_;
        }

        double ClampSlot(int slot, double value)
        {
            double lo = _lo[slot], hi = _hi[slot];
            if (!double.IsNaN(lo) && value < lo) return lo;
            if (!double.IsNaN(hi) && value > hi) return hi;
            return value;
        }

        /// <summary>Joint <paramref name="joint"/>'s channel values by code (a missing channel 0), into <paramref name="values"/>.</summary>
        double[] Values(double[] state, int joint, double[] values)
        {
            Array.Clear(values, 0, 6);
            var spec = _joints[joint];
            for (int k = 0; k < spec.slots.Length; k++) values[spec.slotChannels[k]] = state[spec.slots[k]];
            return values;
        }

        // -------------------------------------------------------------- forward
        /// <summary>Joint <paramref name="joint"/>'s local (translation, rotation) at
        /// <paramref name="state"/>: rest . A . pose(channels) . A^-1.</summary>
        public Frame Local(double[] state, int joint)
        {
            var spec = _joints[joint];
            var values = Values(state, joint, _values);
            Quat turn = EulerQuat(spec.order, values);
            var slide = new Vec(values[3], values[4], values[5]);
            if (!spec.anchored)
                return new Frame(Add(spec.t, QRot(spec.q, slide)), QMul(spec.q, turn));
            Quat turned = Conjugated(spec.anchorQ, turn);
            Vec offset = Add(Sub(spec.anchorT, QRot(turned, spec.anchorT)), QRot(spec.anchorQ, slide));
            return new Frame(Add(spec.t, QRot(spec.q, offset)), QMul(spec.q, turned));
        }

        /// <summary><paramref name="turn"/> about <paramref name="frame"/>'s axes, stated in
        /// <paramref name="frame"/>'s parent: <c>frame (x) turn (x) frame^-1</c>.</summary>
        static Quat Conjugated(Quat frame, Quat turn) => QMul(QMul(frame, turn), QConj(frame));

        /// <summary>Every joint's local (translation, rotation) at <paramref name="state"/>.</summary>
        public Frame[] Pose(double[] state)
        {
            var out_ = new Frame[_joints.Count];
            Pose(state, out_);
            return out_;
        }

        /// <summary>Every joint's local (translation, rotation) at <paramref name="state"/>, into
        /// <paramref name="into"/> (one per joint): a pose that allocates nothing.</summary>
        public void Pose(double[] state, Frame[] into)
        {
            for (int j = 0; j < _joints.Count; j++) into[j] = Local(state, j);
        }

        /// <summary>Every joint's (position, rotation) in rig space at <paramref name="state"/>.</summary>
        public Frame[] World(double[] state)
        {
            var out_ = new Frame[_joints.Count];
            World(state, out_);
            return out_;
        }

        /// <summary>Every joint's (position, rotation) in rig space at <paramref name="state"/>, into
        /// <paramref name="into"/> (one per joint), each composed through its parent joint: a
        /// pose that allocates nothing.</summary>
        public void World(double[] state, Frame[] into)
        {
            for (int index = 0; index < _joints.Count; index++)
            {
                Frame local = Local(state, index);
                int parent = _joints[index].parent;
                if (parent < 0)
                {
                    into[index] = local;
                }
                else
                {
                    Frame p = into[parent];
                    into[index] = new Frame(Add(p.t, QRot(p.q, local.t)), QMul(p.q, local.q));
                }
            }
        }

        /// <summary><paramref name="localPoint"/> (in joint <paramref name="joint"/>'s frame) in rig space.</summary>
        public Vec Point(double[] state, int joint, Vec localPoint)
        {
            World(state, _scratch.poses);
            Frame f = _scratch.poses[joint];
            return Add(f.t, QRot(f.q, localPoint));
        }

        /// <summary>The rig-space <paramref name="point"/> in joint <paramref name="joint"/>'s frame --
        /// where a grab records what it holds.</summary>
        public Vec ToLocal(double[] state, int joint, Vec point)
        {
            World(state, _scratch.poses);
            Frame f = _scratch.poses[joint];
            return QRot(QConj(f.q), Sub(point, f.t));
        }

        // -------------------------------------------------------------- inverse
        /// <summary>
        /// The state that poses the joints at <paramref name="locals"/> -- one
        /// local (translation, rotation) per joint. Angles come back in (-180,
        /// 180], or as near <paramref name="hint"/> (a previous state) as whole
        /// turns allow; a joint with fewer than three rotation channels reads as
        /// the Euler triple that keeps the channels it lacks at 0, and a turn or
        /// slide outside its channels is dropped. Nothing is clamped.
        /// </summary>
        public double[] Read(Frame[] locals, double[] hint = null)
        {
            double[] state = hint == null ? RestState() : (double[])hint.Clone();
            Read(locals, state, state);
            return state;
        }

        /// <summary>
        /// As <see cref="Read(Frame[], double[])"/>, into <paramref name="state"/>, which
        /// holds the hint as it comes in (it may be <paramref name="hint"/> itself): a read
        /// that allocates nothing.
        /// </summary>
        public void Read(Frame[] locals, double[] hint, double[] state)
        {
            if (!ReferenceEquals(hint, state)) Array.Copy(hint, state, state.Length);
            for (int index = 0; index < locals.Length && index < _joints.Count; index++)
            {
                var spec = _joints[index];
                if (spec.slots.Length == 0) continue;
                Quat relative = QMul(QConj(spec.q), QNormalize(locals[index].q));
                Vec offset = QRot(QConj(spec.q), Sub(locals[index].t, spec.t));
                Quat turn;
                Vec slide;
                if (!spec.anchored)
                {
                    turn = relative;
                    slide = offset;
                }
                else
                {
                    // Undo the anchor: the turn about its axes, and the slide along
                    // them once the turn's swing of its point is taken out.
                    Quat back = QConj(spec.anchorQ);
                    turn = Conjugated(back, relative);
                    Vec swung = Add(Sub(offset, spec.anchorT), QRot(relative, spec.anchorT));
                    slide = QRot(back, swung);
                }
                bool rotates = false;
                for (int c = 0; c < 3; c++) { _euler.has[c] = false; _euler.reference[c] = 0.0; }
                for (int k = 0; k < spec.slots.Length; k++)
                {
                    int code = spec.slotChannels[k];
                    if (code < 3) { _euler.has[code] = true; _euler.reference[code] = state[spec.slots[k]]; rotates = true; }
                }
                // The reference reads the Euler triple for every joint; one that
                // carries no rotation channel never uses it.
                if (rotates) _euler.For(spec.order, turn);
                for (int k = 0; k < spec.slots.Length; k++)
                {
                    int code = spec.slotChannels[k];
                    state[spec.slots[k]] = code < 3 ? _euler.result[code] : slide[code - 3];
                }
            }
        }

        // ---------------------------------------------------------------- solve
        /// <summary>
        /// The state that brings <paramref name="localPoint"/> on joint
        /// <paramref name="joint"/>'s link to the rig-space <paramref name="target"/>
        /// -- a grab's step. Only the channels between the root and the joint
        /// move; limits are never left. With <paramref name="rotation"/> (the
        /// held link's wanted rig-space rotation) and a ball joint, the grab is
        /// split the way a wrist is: the ball takes the rotation (clamped), the
        /// chain above places the ball's centre -- its anchor's point -- and a last
        /// position solve closes what the limits left open: the point always wins
        /// over the rotation. It allocates the state it returns; the overload that
        /// solves into the caller's state allocates nothing.
        /// </summary>
        public double[] Solve(double[] state, int joint, Vec localPoint, Vec target, Quat? rotation = null)
        {
            var out_ = new double[state.Length];
            Solve(state, joint, localPoint, target, rotation, out_);
            return out_;
        }

        /// <summary>
        /// As <see cref="Solve(double[], int, Vec, Vec, Quat?)"/>, into <paramref name="into"/>
        /// (one value per channel; it may be <paramref name="state"/> itself): a grab's per-frame
        /// solve, on the model's own scratch, that allocates nothing.
        /// </summary>
        public void Solve(double[] state, int joint, Vec localPoint, Vec target, Quat? rotation, double[] into)
        {
            for (int s = 0; s < into.Length; s++) into[s] = ClampSlot(s, state[s]);
            List<int> chain = _scratch.chain;
            Chain(joint, chain);
            List<int> slots = _scratch.slots;
            slots.Clear();
            foreach (int j in chain)
                foreach (int s in _joints[j].slots) slots.Add(s);
            bool wrist = rotation.HasValue && _joints[joint].IsBall;
            double reach = Reach(chain, localPoint);
            if (!wrist)
            {
                Dls(into, slots, joint, localPoint, target, reach);
                return;
            }
            Quat wanted = QNormalize(rotation.Value);
            List<int> placed = _scratch.placed;
            placed.Clear();
            foreach (int s in slots)
                if (!(_slotJoint[s] == joint && _slotChannel[s] < 3)) placed.Add(s);
            Quat held = wanted;
            // The ball's centre is its anchor's point (its origin, for the
            // identity), which its own turn leaves where it is.
            var spec = _joints[joint];
            Vec pivot = spec.anchored ? spec.anchorT : Origin;
            Vec lever = spec.anchored ? Sub(localPoint, pivot) : localPoint;
            for (int pass = 0; pass < Passes; pass++)
            {
                Vec centre = Sub(target, QRot(held, lever));
                Dls(into, placed, joint, pivot, centre, reach);
                Turn(into, joint, wanted);
                World(into, _scratch.poses);
                held = _scratch.poses[joint].q;
            }
            // Position wins: the whole chain, ball included, closes the gap the
            // ball's limits left.
            Dls(into, slots, joint, localPoint, target, reach);
        }

        // The joints from joint's root down to it, root first, into chain.
        void Chain(int joint, List<int> chain)
        {
            chain.Clear();
            for (int index = joint; index >= 0; index = _joints[index].parent) chain.Add(index);
            chain.Reverse();
        }

        // The chain's length: its bones, pivot to pivot, plus the held point's
        // lever from its joint's pivot (a pivot is a joint's anchor point).
        double Reach(List<int> chain, Vec localPoint)
        {
            var last = _joints[chain[chain.Count - 1]];
            double length = Norm(last.anchored ? Sub(localPoint, last.anchorT) : localPoint);
            for (int i = 1; i < chain.Count; i++) length += Norm(Bone(chain[i]));
            return Math.Max(length, Eps);
        }

        // Joint index's pivot from its parent's, at rest, in the parent's frame.
        Vec Bone(int index)
        {
            var spec = _joints[index];
            bool above = spec.parent >= 0 && _joints[spec.parent].anchored;
            if (!spec.anchored && !above) return spec.t;
            Vec bone = spec.t;
            if (spec.anchored) bone = Add(bone, QRot(spec.q, spec.anchorT));
            if (above) bone = Sub(bone, _joints[spec.parent].anchorT);
            return bone;
        }

        // The ball's channels of state turned to rotation (each clamped), in place.
        void Turn(double[] state, int joint, Quat rotation)
        {
            var spec = _joints[joint];
            Quat parentQ = Identity;
            if (spec.parent >= 0)
            {
                World(state, _scratch.poses);
                parentQ = _scratch.poses[spec.parent].q;
            }
            Quat frame = QMul(parentQ, spec.q);
            Quat local = QMul(QConj(frame), rotation);
            // The link's turn in its rest frame, read about the anchor's axes.
            if (spec.anchored) local = Conjugated(QConj(spec.anchorQ), local);
            for (int c = 0; c < 3; c++) { _euler.has[c] = true; _euler.reference[c] = 0.0; }
            for (int k = 0; k < spec.slots.Length; k++)
                if (spec.slotChannels[k] < 3) _euler.reference[spec.slotChannels[k]] = state[spec.slots[k]];
            _euler.For(spec.order, local);
            for (int k = 0; k < spec.slots.Length; k++)
            {
                int code = spec.slotChannels[k];
                if (code < 3) state[spec.slots[k]] = ClampSlot(spec.slots[k], _euler.result[code]);
            }
        }

        Vec Column(Frame[] world, double[] state, int slot, Vec point, double reach)
        {
            int joint = _slotJoint[slot];
            int channel = _slotChannel[slot];
            var spec = _joints[joint];
            Quat parentQ = spec.parent < 0 ? Identity : world[spec.parent].q;
            Quat frame = QMul(parentQ, spec.q);
            // A turn's pivot: the joint's origin, or its anchor's point (where the
            // slide puts it; the joint's own turn leaves it there).
            Vec pivot = world[joint].t;
            if (spec.anchored)
            {
                frame = QMul(frame, spec.anchorQ);
                pivot = Add(pivot, QRot(world[joint].q, spec.anchorT));
            }
            if (channel >= 3)
                return Scale(QRot(frame, Axis(channel - 3)), reach);
            double[] values = Values(state, joint, _scratch.values);
            // Outermost first: each rotation's axis is carried by those applied
            // after it (the ones nearer the parent).
            for (int i = 2; i >= 0; i--)
            {
                int c = spec.order[i];
                if (c == channel)
                {
                    Vec worldAxis = QRot(frame, Axis(channel));
                    return Cross(worldAxis, Sub(point, pivot));
                }
                frame = QMul(frame, QAxis(c, values[c]));
            }
            return Origin;  // unreachable: every channel's axis is in the order
        }

        static Vec Axis(int axis)
            => new Vec(axis == 0 ? 1.0 : 0.0, axis == 1 ? 1.0 : 0.0, axis == 2 ? 1.0 : 0.0);

        double Unit(int slot, double reach) => _slotChannel[slot] < 3 ? 1.0 * RadToDeg : reach;

        // One damped least-squares step over the first count columns, into du, through a.
        static void Step(Vec[] columns, double[] weights, int count, Vec error, double damping, double[,] a, double[] du)
        {
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    a[r, c] = r == c ? damping : 0.0;
            for (int i = 0; i < count; i++)
            {
                double w = weights[i];
                if (w <= 0.0) continue;
                Vec col = columns[i];
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 3; c++)
                        a[r, c] += w * col[r] * col[c];
            }
            Vec y = Solve3(a, error);
            for (int i = 0; i < count; i++) du[i] = weights[i] * Dot(columns[i], y);
        }

        // The damped least-squares solve of slots, in place in current.
        void Dls(double[] current, List<int> slots, int joint, Vec localPoint, Vec target, double reach)
        {
            if (slots.Count == 0) return;
            double damping = Math.Pow(Damping * reach, 2.0);
            double tolerance = Tolerance * reach;
            double step = MaxStep * reach;
            Scratch scratch = _scratch;
            double[] state = current;
            double[] trial = scratch.trial;
            Frame[] world = scratch.world;
            Frame[] trialWorld = scratch.trialWorld;
            Vec[] columns = scratch.columns;
            double[] weights = scratch.weights;
            double[] du = scratch.du;
            Vec point;
            double distance = Held(state, joint, localPoint, target, world, out point);
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                if (distance <= tolerance) break;
                Vec error = Sub(target, point);
                if (distance > step) error = Scale(error, step / distance);
                for (int i = 0; i < slots.Count; i++)
                {
                    columns[i] = Column(world, state, slots[i], point, reach);
                    weights[i] = _weights[slots[i]];
                }
                Step(columns, weights, slots.Count, error, damping, scratch.normal, du);
                // A channel already on a limit and pushed further out sits THIS
                // step out, so the others make up for it.
                bool blocked = false;
                for (int i = 0; i < slots.Count; i++)
                {
                    int slot = slots[i];
                    double lo = _lo[slot], hi = _hi[slot], value = state[slot];
                    if ((!double.IsNaN(lo) && value <= lo && du[i] < 0.0)
                        || (!double.IsNaN(hi) && value >= hi && du[i] > 0.0))
                    {
                        weights[i] = 0.0;
                        blocked = true;
                    }
                }
                if (blocked) Step(columns, weights, slots.Count, error, damping, scratch.normal, du);
                // Halved until it brings the point nearer; none does -- as near as
                // the limits and the reach allow.
                double fraction = 1.0;
                Vec trialPoint = Origin;
                double trialDistance = distance;
                bool nearer = false;
                for (int halving = 0; halving <= Halvings; halving++)
                {
                    Array.Copy(state, trial, state.Length);
                    for (int i = 0; i < slots.Count; i++)
                    {
                        int slot = slots[i];
                        double value = state[slot] + fraction * du[i] * Unit(slot, reach);
                        trial[slot] = ClampSlot(slot, value);
                    }
                    trialDistance = Held(trial, joint, localPoint, target, trialWorld, out trialPoint);
                    if (trialDistance < distance) { nearer = true; break; }
                    fraction *= 0.5;
                }
                if (!nearer) break;
                // The trial is the new state, and its poses the next columns' --
                // swapped, not copied.
                double[] taken = state;
                state = trial;
                trial = taken;
                point = trialPoint;
                distance = trialDistance;
                Frame[] poses = world;
                world = trialWorld;
                trialWorld = poses;
            }
            if (!ReferenceEquals(state, current)) Array.Copy(state, current, current.Length);
        }

        // The rig-space poses at state (into world), where localPoint on joint
        // is (point), and how far that is from target.
        double Held(double[] state, int joint, Vec localPoint, Vec target, Frame[] world, out Vec point)
        {
            World(state, world);
            Frame f = world[joint];
            point = Add(f.t, QRot(f.q, localPoint));
            return Norm(Sub(target, point));
        }

        /// <summary>A solve's buffers, sized to the rig once: what lets a solve allocate nothing.</summary>
        sealed class Scratch
        {
            public readonly List<int> chain, slots, placed;
            public readonly double[] trial, weights, du;
            public readonly double[] values = new double[6];
            public readonly double[,] normal = new double[3, 3];
            public readonly Vec[] columns;
            public readonly Frame[] world, trialWorld, poses;

            public Scratch(int joints, int channels)
            {
                chain = new List<int>(joints);
                slots = new List<int>(channels);
                placed = new List<int>(channels);
                trial = new double[channels];
                weights = new double[channels];
                du = new double[channels];
                columns = new Vec[channels];
                world = new Frame[joints];
                trialWorld = new Frame[joints];
                poses = new Frame[joints];
            }
        }

        // ------------------------------------------------------------ the mirror
        /// <summary>
        /// <paramref name="v"/> in the axes a mirror of X relates -- how Unity's FBX import lands
        /// a point the record states in glTF's frame: (x, y, z) as (-x, y, z). A rig stated
        /// mirrored poses, reads and solves as the mirror of the reference, with the channels
        /// <see cref="MirrorFlips"/> names run the other way.
        /// </summary>
        public static Vec Mirror(Vec v)
        {
            var (x, y, z) = GltfFrame.Point(v.x, v.y, v.z);
            return new Vec(x, y, z);
        }

        /// <summary><paramref name="q"/> mirrored as <see cref="Mirror(Vec)"/>: (x, y, z, w) as (x, -y, -z, w).</summary>
        public static Quat Mirror(Quat q)
        {
            var (x, y, z, w) = GltfFrame.Rotation(q.x, q.y, q.z, q.w);
            return new Quat(x, y, z, w);
        }

        /// <summary>Whether channel <paramref name="code"/> (0-2 rx..rz, 3-5 tx..tz) turns or slides the
        /// other way in mirrored axes: ry, rz and tx.</summary>
        public static bool MirrorFlips(int code) => code == 1 || code == 2 || code == 3;

        /// <summary>
        /// Channel <paramref name="code"/>'s limits (<paramref name="min"/>, <paramref name="max"/>;
        /// null is unbounded) in mirrored axes: negated and swapped where it runs the other way
        /// (<see cref="MirrorFlips"/>), else as they are.
        /// </summary>
        public static void MirrorLimits(int code, double? min, double? max, out double? mirroredMin, out double? mirroredMax)
        {
            bool flips = MirrorFlips(code);
            mirroredMin = flips ? -max : min;
            mirroredMax = flips ? -min : max;
        }

        // ------------------------------------------------------ vector helpers
        /// <summary><paramref name="a"/> + <paramref name="b"/>.</summary>
        public static Vec Add(Vec a, Vec b) => new Vec(a.x + b.x, a.y + b.y, a.z + b.z);
        /// <summary><paramref name="a"/> - <paramref name="b"/>.</summary>
        public static Vec Sub(Vec a, Vec b) => new Vec(a.x - b.x, a.y - b.y, a.z - b.z);
        /// <summary><paramref name="a"/> times <paramref name="s"/>.</summary>
        public static Vec Scale(Vec a, double s) => new Vec(a.x * s, a.y * s, a.z * s);
        /// <summary>The dot product.</summary>
        public static double Dot(Vec a, Vec b) => a.x * b.x + a.y * b.y + a.z * b.z;
        /// <summary>The cross product.</summary>
        public static Vec Cross(Vec a, Vec b)
            => new Vec(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        /// <summary>The length.</summary>
        public static double Norm(Vec a) => Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z);

        /// <summary><c>a (x) b</c>: the rotation b followed by a.</summary>
        public static Quat QMul(Quat a, Quat b) => new Quat(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        /// <summary>The conjugate: the inverse of a unit quaternion.</summary>
        public static Quat QConj(Quat q) => new Quat(-q.x, -q.y, -q.z, q.w);

        /// <summary><paramref name="q"/> at unit length (the identity for a zero quaternion).</summary>
        public static Quat QNormalize(Quat q)
        {
            double n = Math.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            if (n < 1.0e-12) return Identity;
            return new Quat(q.x / n, q.y / n, q.z / n, q.w / n);
        }

        /// <summary><paramref name="v"/> rotated by the unit quaternion <paramref name="q"/>.</summary>
        public static Vec QRot(Quat q, Vec v)
        {
            var u = new Vec(q.x, q.y, q.z);
            Vec t = Scale(Cross(u, v), 2.0);
            return Add(Add(v, Scale(t, q.w)), Cross(u, t));
        }

        /// <summary>A rotation of <paramref name="degrees"/> about the unit axis <paramref name="axis"/> (0, 1, 2).</summary>
        public static Quat QAxis(int axis, double degrees)
        {
            double half = degrees * DegToRad * 0.5;
            double s = Math.Sin(half);
            return new Quat(axis == 0 ? s : 0.0, axis == 1 ? s : 0.0, axis == 2 ? s : 0.0, Math.Cos(half));
        }

        /// <summary>The rotation of Euler <paramref name="angles"/> (degrees by axis; index 0-2)
        /// in <paramref name="order"/> (axis per letter, x applied first for "xyz").</summary>
        public static Quat EulerQuat(int[] order, double[] angles)
        {
            Quat q = Identity;
            foreach (int c in order) q = QMul(QAxis(c, angles[c]), q);
            return q;
        }

        /// <summary>The 3 x 3 rotation matrix of <paramref name="q"/> (column vectors).</summary>
        public static double[,] QMatrix(Quat q)
        {
            var m = new double[3, 3];
            QMatrix(q, m);
            return m;
        }

        static void QMatrix(Quat q, double[,] m)
        {
            double x = q.x, y = q.y, z = q.z, w = q.w;
            m[0, 0] = 1 - 2 * (y * y + z * z); m[0, 1] = 2 * (x * y - z * w); m[0, 2] = 2 * (x * z + y * w);
            m[1, 0] = 2 * (x * y + z * w); m[1, 1] = 1 - 2 * (x * x + z * z); m[1, 2] = 2 * (y * z - x * w);
            m[2, 0] = 2 * (x * z - y * w); m[2, 1] = 2 * (y * z + x * w); m[2, 2] = 1 - 2 * (x * x + y * y);
        }

        /// <summary>Euler degrees (by axis) of the rotation matrix <paramref name="m"/> in
        /// <paramref name="order"/>; the middle angle is within [-90, 90].</summary>
        public static double[] MatrixEuler(int[] order, double[,] m)
        {
            var out_ = new double[3];
            MatrixEuler(order, m, out_);
            return out_;
        }

        static void MatrixEuler(int[] order, double[,] m, double[] out_)
        {
            int a = order[2], b = order[1], c = order[0];
            double s = (((b - a) % 3) + 3) % 3 == 1 ? 1.0 : -1.0;
            double sinB = Math.Max(-1.0, Math.Min(1.0, s * m[a, c]));
            double angleB = Math.Asin(sinB);
            double angleA, angleC;
            if (Math.Cos(angleB) > 1.0e-6)
            {
                angleA = Math.Atan2(-s * m[b, c], m[c, c]);
                angleC = Math.Atan2(-s * m[a, b], m[a, a]);
            }
            else
            {
                angleC = 0.0;
                angleA = Math.Atan2(s * m[c, b], m[b, b]);
            }
            out_[order[2]] = angleA * RadToDeg;
            out_[order[1]] = angleB * RadToDeg;
            out_[order[0]] = angleC * RadToDeg;
        }

        /// <summary>
        /// Euler degrees of <paramref name="m"/> in <paramref name="order"/> for a
        /// joint that turns on the axes <paramref name="channels"/> flags, each
        /// unwrapped toward <paramref name="reference"/>: of the two Euler triples
        /// of a rotation, the one that keeps a channel the joint lacks at 0, then
        /// the one nearer the reference; a tie keeps the first.
        /// </summary>
        public static double[] EulerFor(int[] order, double[,] m, bool[] channels, double[] reference)
        {
            var euler = new Euler();
            Array.Copy(channels, euler.has, 3);
            Array.Copy(reference, euler.reference, 3);
            euler.For(order, m);
            return (double[])euler.result.Clone();
        }

        /// <summary>One Euler read's buffers: the reference's choice of triple, with no allocation.</summary>
        sealed class Euler
        {
            public readonly bool[] has = new bool[3];
            public readonly double[] reference = new double[3];
            public readonly double[] result = new double[3];
            readonly double[,] _m = new double[3, 3];
            readonly double[] _first = new double[3];
            readonly double[] _second = new double[3];
            readonly double[] _w1 = new double[3];
            readonly double[] _w2 = new double[3];

            public void For(int[] order, Quat q)
            {
                QMatrix(q, _m);
                For(order, _m);
            }

            public void For(int[] order, double[,] m)
            {
                MatrixEuler(order, m, _first);
                _second[order[2]] = _first[order[2]] + 180.0;
                _second[order[1]] = 180.0 - _first[order[1]];
                _second[order[0]] = _first[order[0]] + 180.0;
                Score(_first, _w1, out double m1, out double d1);
                Score(_second, _w2, out double m2, out double d2);
                Array.Copy(m2 < m1 - 1.0e-6 || (Math.Abs(m2 - m1) <= 1.0e-6 && d2 < d1) ? _w2 : _w1, result, 3);
            }

            void Score(double[] triple, double[] wound, out double missing, out double drift)
            {
                for (int c = 0; c < 3; c++) wound[c] = Unwrap(triple[c], reference[c]);
                missing = 0.0;
                drift = 0.0;
                for (int c = 0; c < 3; c++)
                {
                    if (has[c]) drift += Math.Abs(wound[c] - reference[c]);
                    else missing += Math.Abs(Unwrap(triple[c], 0.0));
                }
            }
        }

        /// <summary><paramref name="value"/> shifted by whole turns to lie nearest
        /// <paramref name="reference"/> (a half turn rounds up).</summary>
        public static double Unwrap(double value, double reference)
            => value + 360.0 * Math.Floor((reference - value) / 360.0 + 0.5);

        /// <summary><c>A^-1 e</c> for a symmetric positive-definite 3 x 3 <paramref name="a"/>;
        /// the zero vector when it is singular.</summary>
        public static Vec Solve3(double[,] a, Vec e)
        {
            double a00 = a[0, 0], a01 = a[0, 1], a02 = a[0, 2];
            double a10 = a[1, 0], a11 = a[1, 1], a12 = a[1, 2];
            double a20 = a[2, 0], a21 = a[2, 1], a22 = a[2, 2];
            double c00 = a11 * a22 - a12 * a21;
            double c01 = a12 * a20 - a10 * a22;
            double c02 = a10 * a21 - a11 * a20;
            double det = a00 * c00 + a01 * c01 + a02 * c02;
            if (Math.Abs(det) < 1.0e-300) return Origin;
            double inv = 1.0 / det;
            double c10 = a02 * a21 - a01 * a22;
            double c11 = a00 * a22 - a02 * a20;
            double c12 = a01 * a20 - a00 * a21;
            double c20 = a01 * a12 - a02 * a11;
            double c21 = a02 * a10 - a00 * a12;
            double c22 = a00 * a11 - a01 * a10;
            return new Vec(
                (c00 * e.x + c10 * e.y + c20 * e.z) * inv,
                (c01 * e.x + c11 * e.y + c21 * e.z) * inv,
                (c02 * e.x + c12 * e.y + c22 * e.z) * inv);
        }
    }
}
