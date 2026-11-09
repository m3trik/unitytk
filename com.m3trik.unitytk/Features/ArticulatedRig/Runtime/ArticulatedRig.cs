// ----------------------------------------------------------------------------
// The ArticulatedRig feature's runtime: rigid parts on hinge, swivel, ball and
// slide joints (a desk lamp, a magnifier arm) that a hand or a mouse grabs and
// poses. Each joint is an ArticulatedJoint on its part; the rig, on the rig's
// root, lists them (parents first) and its grab parts by reference, and poses
// every part from the JOINT CHAIN -- its parent joint's pose, then its own rest,
// anchor and channels -- never from its scene parent, so a flat model (every
// part a child of the root) chains as a nested one does. Each root joint's
// chain is posed and grabbed in its own SPACE, the node its root's part hangs
// under, as every reader of the record does: no channel spans two roots, so roots
// under different nodes need no space they share. The pose and the grab are
// the ported model's (ArticulationModel), run on the components' values as
// they are: in Unity's axes and the rig's units (each space's own: metres for
// an unscaled one), which the model poses alike (it is the same in any axes a
// mirror relates), so nothing converts at run time.
//
// Animation and posing:
//   * followAnimation on (an imported delivery, whose clips animate the parts):
//     each frame, after the Animator, the rig reads the channel values off the
//     parts, so the values and their events track the clip;
//   * followAnimation off (a rig made in Unity, whose clips key the channel
//     fields): each frame the values pose the parts;
//   * a pose -- a grab, ApplyState, a value a script sets while following --
//     holds over either, written after the Animator every frame, until
//     ReturnToAnimation() (or, with resumeOnAnimatorChange, the Animator's
//     state or time moving after a release).
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("M3trik.Unitytk.ArticulatedRig.Tests")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.ArticulatedRig.Editor")]

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// An articulated rig: its joints (<see cref="ArticulatedJoint"/>, parents first) and
    /// the parts a hand grabs, by reference, posed every frame from the joint chain -- each
    /// root's chain in its own space, the node its root's part hangs under
    /// (<see cref="SpaceOf"/>) -- and an input-agnostic grab solved by the ported model:
    /// <see cref="BeginGrab(Transform, Vector3, Quaternion)"/>,
    /// <see cref="UpdateGrab(Vector3, Quaternion)"/> and <see cref="EndGrab"/> take world
    /// poses from whatever holds the part (a mouse -- <c>ArticulatedRigPointerGrab</c> -- an
    /// XR hand, a script). The importer fills it from a delivery's record; the actions
    /// (<c>ArticulatedRigActions</c>) build one in Unity.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>ArticulationBody</c> moves jointed bodies by physics forces and
    /// would take the parts out of the Animator's hands (a later door: physics-driven
    /// articulations); Animation Rigging's constraints need a <c>RigBuilder</c> evaluated in
    /// the Animator's graph and hold no per-channel limits; Unity's <c>HingeJoint</c> and
    /// <c>ConfigurableJoint</c> are physics joints between rigidbodies. A component that
    /// writes the pose in <c>LateUpdate</c>, after the Animator, poses the transforms
    /// directly and hands them back with <see cref="ReturnToAnimation"/>.
    /// </remarks>
    [AddComponentMenu("Animation/Articulated Rig")]
    public class ArticulatedRig : MonoBehaviour
    {
        /// <summary>How a grab turns the held link when it hangs off a ball joint.</summary>
        public enum GrabRotation
        {
            /// <summary>The link follows the grabbing hand's rotation (an XR controller).</summary>
            Hand,
            /// <summary>The link keeps the rig-space rotation it had when taken (a mouse).</summary>
            Keep,
            /// <summary>Position only: the solver chooses the rotation.</summary>
            Free,
        }

        /// <summary>A part a hand takes hold of, and the joint it rides.</summary>
        [Serializable]
        public sealed class GrabPart
        {
            [Tooltip("The part a hand takes hold of (a hit on it or below it grabs it).")]
            public Transform part;

            [Tooltip("The joint whose link the part rides: the grab moves the chain down to it.")]
            public ArticulatedJoint joint;
        }

        [Tooltip("The rig's joints, each after its parent joint.")]
        public List<ArticulatedJoint> joints = new List<ArticulatedJoint>();

        [Tooltip("The parts a hand takes hold of, each with the joint it rides.")]
        public List<GrabPart> grabs = new List<GrabPart>();

        [Tooltip("The parts' transforms are animated (a delivery's clips): each frame the rig reads the channel " +
                 "values off them, until a grab or a script poses it. Off: the channel values pose the parts " +
                 "every frame (a clip keying them, the Inspector, a script).")]
        public bool followAnimation;

        [Tooltip("How a grab turns a link hanging off a ball joint: Hand follows the grabbing hand's rotation (XR), " +
                 "Keep holds the rotation it was taken with (a mouse), Free leaves it to the solver. Hinges and " +
                 "slides always follow the held point.")]
        public GrabRotation grabRotation = GrabRotation.Hand;

        [Tooltip("The Animator a grab takes the pose over from (this object's, or a parent's, when empty).")]
        public Animator animator;

        [Tooltip("After a release, resume the animation as soon as the Animator's current state or its time moves " +
                 "(a clip played or scrubbed). Off: the released pose holds until ReturnToAnimation().")]
        public bool resumeOnAnimatorChange;

        // ------------------------------------------------------------- the model
        ArticulationModel _model;
        string _problem;
        int _built = int.MinValue;          // the joints' signature the model was built for
        bool _dirty = true;
        bool _warned;
        ArticulationModel.Frame[] _locals;   // per joint: read off the parts, or posed
        ArticulationModel.Frame[] _world;    // per joint: rig space
        double[] _state;                     // per slot
        float[] _last;                       // per slot: the value last seen, for the events
        int[] _slotJoint;
        ArticulatedJoint.Axis[] _slotAxis;
        bool _held;

        // ------------------------------------------------------------- the grab
        int _grabJoint = -1;
        Transform _grabPart;
        ArticulationModel.Vec _grabLocal;
        ArticulationModel.Quat _grabKeep;     // the link's rig-space rotation when taken
        ArticulationModel.Quat _grabOffset;   // hand -> link, rig space
        ArticulationModel.Quat _lastHand;     // the hand's latest rig-space rotation
        bool _hasHand;
        bool _grabbing;

        // The Animator's place at the release (resumeOnAnimatorChange).
        int _releaseHash;
        float _releaseTime;
        bool _releaseKnown;

        /// <summary>The model the joints make (null when they make none: see <see cref="Problem"/>).</summary>
        public ArticulationModel Model => Ready() ? _model : null;

        /// <summary>Whether the joints make a rig that poses.</summary>
        public bool IsValid => Ready();

        /// <summary>Why the joints make no rig (null when they do).</summary>
        public string Problem
        {
            get
            {
                Ready();
                return _problem;
            }
        }

        /// <summary>True while a part is held.</summary>
        public bool IsGrabbing => _grabbing;

        /// <summary>True while a pose holds over the animation (held, or released and not yet returned).</summary>
        public bool IsHoldingPose => _held;

        /// <summary>The grab part being held (null when none).</summary>
        public Transform GrabbedPart => _grabbing ? _grabPart : null;

        /// <summary>The joint whose link is held (null when none).</summary>
        public ArticulatedJoint GrabbedJoint => _grabbing ? joints[_grabJoint] : null;

        /// <summary>The held point, in its joint's frame (valid while grabbing).</summary>
        public ArticulationModel.Vec GrabLocalPoint => _grabLocal;

        /// <summary>The index of <paramref name="joint"/> in <see cref="joints"/> (-1 when not this rig's).</summary>
        public int IndexOf(ArticulatedJoint joint) => joint == null ? -1 : joints.IndexOf(joint);

        /// <summary>Rebuild the model from the joints the next time the rig poses (after a script changes them).</summary>
        public void Rebuild()
        {
            _dirty = true;
            _warned = false;
        }

#if UNITY_EDITOR
        /// <summary>An Inspector edit to the lists rebuilds the model.</summary>
        void OnValidate() => Rebuild();
#endif

        // The model, built from the joints when they changed; false when they make none.
        bool Ready()
        {
            int signature = Signature();
            if (!_dirty && signature == _built) return _model != null;
            _dirty = false;
            _built = signature;
            _model = Build(out _problem);
            _held = false;
            _grabbing = false;
            if (_model == null)
            {
                if (!_warned && Application.isPlaying)
                    Debug.LogWarning($"[ArticulatedRig] '{name}' does not pose: {_problem}", this);
                _warned = true;
                return false;
            }
            int count = _model.ChannelCount;
            _locals = new ArticulationModel.Frame[_model.JointCount];
            _world = new ArticulationModel.Frame[_model.JointCount];
            _state = new double[count];
            _last = new float[count];
            _slotJoint = new int[count];
            _slotAxis = new ArticulatedJoint.Axis[count];
            for (int s = 0; s < count; s++)
            {
                _slotJoint[s] = _model.ChannelJoint(s);
                _slotAxis[s] = (ArticulatedJoint.Axis)_model.ChannelCode(s);
            }
            Gather(_state);
            for (int s = 0; s < count; s++) _last[s] = (float)_state[s];
            return true;
        }

        int Signature()
        {
            int v = joints == null ? 0 : joints.Count;
            if (joints == null) return v;
            foreach (ArticulatedJoint joint in joints)
                v = unchecked(v * 31 + (joint == null ? 7 : joint.GetInstanceID() * 17 + joint.Version));
            return v;
        }

        static readonly string[] Orders = { "xyz", "yzx", "zxy", "xzy", "yxz", "zyx" };

        ArticulationModel Build(out string problem)
        {
            problem = null;
            if (joints == null || joints.Count == 0)
            {
                problem = "it lists no joints.";
                return null;
            }
            var rig = new ArticulationRecord.Shape.Rig
            {
                name = name,
                joints = new List<ArticulationRecord.Shape.Joint>(joints.Count),
                grab = new List<ArticulationRecord.Shape.Grab>(),
            };
            for (int i = 0; i < joints.Count; i++)
            {
                ArticulatedJoint joint = joints[i];
                if (joint == null)
                {
                    problem = $"its joint {i} is missing.";
                    return null;
                }
                int parent = joint.parent == null ? -1 : joints.IndexOf(joint.parent);
                if (joint.parent != null && (parent < 0 || parent >= i))
                {
                    problem = parent < 0
                        ? $"joint '{joint.name}' hangs from '{joint.parent.name}', which the rig does not list."
                        : $"joint '{joint.name}' is listed before its parent '{joint.parent.name}': list each joint after its parent.";
                    return null;
                }
                var channels = new List<ArticulationRecord.Shape.Channel>(joint.channels.Count);
                foreach (ArticulatedJoint.Channel c in joint.channels)
                {
                    if (c == null) continue;
                    channels.Add(new ArticulationRecord.Shape.Channel
                    {
                        channel = ArticulationModel.ChannelNames[(int)c.axis],
                        min = c.hasMin ? c.min : (double?)null,
                        max = c.hasMax ? c.max : (double?)null,
                        weight = c.weight,
                    });
                }
                rig.joints.Add(new ArticulationRecord.Shape.Joint
                {
                    name = joint.name,
                    parent = parent < 0 ? (int?)null : parent,
                    t = Array3(joint.restPosition),
                    q = Array4(joint.restRotation),
                    anchor = new ArticulationRecord.Shape.Anchor { t = Array3(joint.anchorPosition), q = Array4(joint.anchorRotation) },
                    rotate_order = Orders[(int)joint.rotateOrder],
                    channels = channels,
                });
            }
            try
            {
                return new ArticulationModel(rig);
            }
            catch (ArgumentException e)
            {
                problem = e.Message;
                return null;
            }
        }

        static double[] Array3(Vector3 v) => new double[] { v.x, v.y, v.z };

        // A Quaternion the Inspector left all zeros reads as the identity, as Unity treats it.
        static double[] Array4(Quaternion q)
            => q.x == 0f && q.y == 0f && q.z == 0f && q.w == 0f ? new[] { 0.0, 0.0, 0.0, 1.0 } : new double[] { q.x, q.y, q.z, q.w };

        // ------------------------------------------------------- values <-> state
        void Gather(double[] state)
        {
            for (int s = 0; s < state.Length; s++) state[s] = joints[_slotJoint[s]].GetValue(_slotAxis[s]);
        }

        void Scatter(double[] state)
        {
            for (int s = 0; s < state.Length; s++) joints[_slotJoint[s]].SetValue(_slotAxis[s], (float)state[s]);
        }

        bool FieldsMoved()
        {
            for (int s = 0; s < _last.Length; s++)
                if (joints[_slotJoint[s]].GetValue(_slotAxis[s]) != _last[s]) return true;
            return false;
        }

        void Fire()
        {
            for (int s = 0; s < _last.Length; s++)
            {
                ArticulatedJoint joint = joints[_slotJoint[s]];
                float value = joint.GetValue(_slotAxis[s]);
                if (value == _last[s]) continue;
                _last[s] = value;
                joint.changed?.Invoke(_slotAxis[s], value);
            }
        }

        // -------------------------------------------------------- parts <-> model
        // Where a joint's local is stated: a root's in the node its part hangs under (its
        // space), a child's in its parent joint's frame -- that part's place and turn, its own
        // scale left out, as every reader of the record frames it. A part that hangs under that
        // node (a root's always; a nested child's) takes its local as it is; any other (a flat
        // model's) is placed where the frame and its local put it.
        internal static bool Nested(ArticulatedJoint joint)
            => joint.parent == null || joint.transform.parent == joint.parent.transform;

        // A point of joint's parent-joint frame, in the world.
        static Vector3 FramePoint(Transform above, Vector3 local)
        {
            Vector3 inParent = above.localPosition + above.localRotation * local;
            return above.parent != null ? above.parent.TransformPoint(inParent) : inParent;
        }

        // A world point in above's frame (its own scale left out).
        static Vector3 InFrame(Transform above, Vector3 world)
        {
            Vector3 inParent = above.parent != null ? above.parent.InverseTransformPoint(world) : world;
            return Quaternion.Inverse(above.localRotation) * (inParent - above.localPosition);
        }

        /// <summary>Pose every part from the joint chain at <paramref name="state"/>, leaving each
        /// joint whose <paramref name="leave"/> is set as it stands.</summary>
        void PoseParts(double[] state, bool[] leave = null)
        {
            _model.Pose(state, _locals);
            for (int j = 0; j < _locals.Length; j++)
            {
                if (leave != null && leave[j]) continue;
                ArticulatedJoint joint = joints[j];
                Transform part = joint.transform;
                if (Nested(joint))
                {
                    part.localPosition = U(_locals[j].t);
                    part.localRotation = U(_locals[j].q);
                }
                else
                {
                    Transform above = joint.parent.transform;
                    part.SetPositionAndRotation(FramePoint(above, U(_locals[j].t)), above.rotation * U(_locals[j].q));
                }
            }
        }

        /// <summary>
        /// Where <paramref name="joint"/>'s part stands now, as its local: under its space for a
        /// root, in its parent joint's frame for a child (what the model's pose of it is).
        /// </summary>
        internal static void StandingLocal(ArticulatedJoint joint, out Vector3 position, out Quaternion rotation)
        {
            Transform part = joint.transform;
            if (Nested(joint))
            {
                position = part.localPosition;
                rotation = part.localRotation;
                return;
            }
            Transform above = joint.parent.transform;
            position = InFrame(above, part.position);
            rotation = Quaternion.Inverse(above.rotation) * part.rotation;
        }

        /// <summary>The state the parts stand at, unwrapped toward <paramref name="state"/>, into it.</summary>
        void ReadParts(double[] state)
        {
            for (int j = 0; j < _locals.Length; j++)
            {
                StandingLocal(joints[j], out Vector3 position, out Quaternion rotation);
                _locals[j] = new ArticulationModel.Frame(V(position), Q(rotation));
            }
            _model.Read(_locals, state, state);
        }

        /// <summary>
        /// The node <paramref name="joint"/>'s chain is posed and grabbed in: the one its root
        /// joint's part hangs under (null for a part at a scene's root: the world). Null when the
        /// joint is not this rig's.
        /// </summary>
        public Transform SpaceOf(ArticulatedJoint joint)
        {
            if (IndexOf(joint) < 0) return null;
            ArticulatedJoint root = joint;
            for (int up = 0; root.parent != null && up < joints.Count; up++) root = root.parent;
            return root.transform.parent;
        }

        Transform SpaceOf(int j) => SpaceOf(joints[j]);

        // ------------------------------------------------------------ the frame
        void LateUpdate() => Step();

        /// <summary>One frame's work, after the Animator: hold a pose, follow the parts, or pose them from the values.</summary>
        internal void Step()
        {
            if (!Ready()) return;
            if (_held && resumeOnAnimatorChange && !_grabbing && AnimatorMoved()) ReturnToAnimation();
            if (_held)
            {
                Scatter(_state);
                PoseParts(_state);
            }
            else if (followAnimation)
            {
                if (FieldsMoved())
                {
                    // A value set by a script (or the Inspector) poses the rig over the clip.
                    Gather(_state);
                    _held = true;
                    PoseParts(_state);
                }
                else
                {
                    ReadParts(_state);
                    Scatter(_state);
                }
            }
            else
            {
                Gather(_state);
                PoseParts(_state);
            }
            Fire();
        }

        /// <summary>
        /// Pose the parts from the channel values now (what a frame does with
        /// <see cref="followAnimation"/> off): the Inspector's edit-mode preview, an action, a
        /// test. Returns false when the joints make no rig.
        /// </summary>
        public bool ApplyNow() => ApplyNow(null);

        /// <summary>As <see cref="ApplyNow()"/>, leaving each joint whose <paramref name="leave"/> is
        /// set as it stands (the edit-mode preview's parts at rest or yielded to a person's move).</summary>
        internal bool ApplyNow(bool[] leave)
        {
            if (!Ready()) return false;
            Gather(_state);
            PoseParts(_state, leave);
            Fire();
            return true;
        }

        /// <summary>
        /// Read the channel values off the parts as they stand now (an imported pose, a playing
        /// clip) into the joints' fields. Returns false when the joints make no rig.
        /// </summary>
        public bool ReadPose()
        {
            if (!Ready()) return false;
            Gather(_state);
            ReadParts(_state);
            Scatter(_state);
            Fire();
            return true;
        }

        /// <summary>
        /// The channel values in slot order (the model's: joint by joint, each in its channel
        /// order), in doubles: a held pose's own, else the fields'. Null when the joints make
        /// no rig.
        /// </summary>
        public double[] GetState()
        {
            if (!Ready()) return null;
            if (!_held) Gather(_state);
            return (double[])_state.Clone();
        }

        /// <summary>
        /// The state on screen: a held pose's own; while following the animation, read off the
        /// parts (unwrapped toward the last state); else the fields'. Null when the joints make
        /// no rig.
        /// </summary>
        public double[] ReadState()
        {
            if (!Ready()) return null;
            if (!_held)
            {
                if (followAnimation) ReadParts(_state);
                else Gather(_state);
            }
            return (double[])_state.Clone();
        }

        /// <summary>
        /// Pose the rig at <paramref name="state"/> (slot order) now: the fields set and the parts
        /// posed. While following the animation the pose holds over it until
        /// <see cref="ReturnToAnimation"/>. Not clamped: a script may pose outside the limits.
        /// </summary>
        /// <exception cref="ArgumentException">A state of another length than the rig's channels.</exception>
        public void ApplyState(double[] state)
        {
            if (!Ready() || state == null) return;
            if (state.Length != _state.Length)
                throw new ArgumentException($"State has {state.Length} values; rig '{name}' has {_state.Length} channels.");
            Array.Copy(state, _state, state.Length);
            if (followAnimation) _held = true;
            Scatter(_state);
            PoseParts(_state);
            Fire();
        }

        /// <summary>
        /// The frame joint <paramref name="joint"/>'s anchor is stated in -- its rest frame -- as
        /// a matrix to world space, at the current values: its chain's space
        /// (<see cref="SpaceOf"/>), its parent joint's pose, then its rest. False when it is not
        /// this rig's or the joints make no rig.
        /// </summary>
        public bool TryGetRestFrame(ArticulatedJoint joint, out Matrix4x4 restToWorld)
        {
            restToWorld = Matrix4x4.identity;
            int j = IndexOf(joint);
            if (j < 0 || !Ready()) return false;
            if (!_held) Gather(_state);
            _model.World(_state, _world);
            ArticulationModel.Joint spec = _model.Joints[j];
            ArticulationModel.Frame above = spec.parent < 0
                ? new ArticulationModel.Frame(ArticulationModel.Origin, ArticulationModel.Identity)
                : _world[spec.parent];
            ArticulationModel.Vec t = ArticulationModel.Add(above.t, ArticulationModel.QRot(above.q, spec.t));
            ArticulationModel.Quat q = ArticulationModel.QMul(above.q, spec.q);
            Transform space = SpaceOf(j);
            Matrix4x4 toWorld = space != null ? space.localToWorldMatrix : Matrix4x4.identity;
            restToWorld = toWorld * Matrix4x4.TRS(U(t), U(q), Vector3.one);
            return true;
        }

        // ------------------------------------------------------------- grabbing
        /// <summary>
        /// The grab part a hit on <paramref name="hit"/> takes, and the joint it rides: the
        /// nearest grab part at or above it, up to this object.
        /// </summary>
        public bool FindGrab(Transform hit, out Transform part, out ArticulatedJoint joint)
        {
            part = null;
            joint = null;
            if (hit == null || grabs == null) return false;
            for (Transform t = hit; t != null; t = t.parent)
            {
                foreach (GrabPart grab in grabs)
                {
                    if (grab == null || grab.part != t || grab.joint == null || IndexOf(grab.joint) < 0) continue;
                    part = t;
                    joint = grab.joint;
                    return true;
                }
                if (t == transform) break;
            }
            return false;
        }

        /// <summary>
        /// Take hold of <paramref name="part"/> at the world point <paramref name="worldPoint"/>
        /// with a hand at <paramref name="handWorldRotation"/>. The grab starts from the pose on
        /// screen. Returns false when the part is not (under) a grab part of a rig that poses. A
        /// grab in progress is ended first.
        /// </summary>
        public bool BeginGrab(Transform part, Vector3 worldPoint, Quaternion handWorldRotation)
            => Begin(part, worldPoint, handWorldRotation, true);

        /// <summary>
        /// Take hold of <paramref name="part"/> at <paramref name="worldPoint"/> with no hand
        /// rotation (a mouse): a ball-jointed link keeps the rotation it was taken with unless
        /// <see cref="grabRotation"/> is Free.
        /// </summary>
        public bool BeginGrab(Transform part, Vector3 worldPoint)
            => Begin(part, worldPoint, Quaternion.identity, false);

        bool Begin(Transform hit, Vector3 worldPoint, Quaternion hand, bool hasHand)
        {
            if (!Ready() || !FindGrab(hit, out Transform part, out ArticulatedJoint joint)) return false;
            if (_grabbing) EndGrab();
            int j = IndexOf(joint);
            // The pose on screen: a held pose is already exact; otherwise what the Animator
            // (or anything else) left.
            if (!_held)
            {
                if (followAnimation) ReadParts(_state);
                else Gather(_state);
            }
            _grabLocal = _model.ToLocal(_state, j, ToRig(j, worldPoint));
            _model.World(_state, _world);
            _grabKeep = _world[j].q;
            _hasHand = hasHand;
            if (hasHand)
            {
                _lastHand = HandToRig(j, hand);
                _grabOffset = ArticulationModel.QMul(ArticulationModel.QConj(_lastHand), _grabKeep);
            }
            _grabJoint = j;
            _grabPart = part;
            _grabbing = true;
            _held = true;
            _releaseKnown = false;
            Scatter(_state);
            PoseParts(_state);
            Fire();
            return true;
        }

        /// <summary>Move the held point to <paramref name="worldTarget"/>, the hand now at
        /// <paramref name="handWorldRotation"/>: solve and pose. (A grab begun without a hand
        /// rotation ignores it and keeps the link's rotation as taken.)</summary>
        public void UpdateGrab(Vector3 worldTarget, Quaternion handWorldRotation)
        {
            if (!_grabbing) return;
            if (_hasHand) _lastHand = HandToRig(_grabJoint, handWorldRotation);
            Solve(worldTarget);
        }

        /// <summary>Move the held point to <paramref name="worldTarget"/> (the hand's rotation, if
        /// the grab has one, as last given).</summary>
        public void UpdateGrab(Vector3 worldTarget)
        {
            if (!_grabbing) return;
            Solve(worldTarget);
        }

        void Solve(Vector3 worldTarget)
        {
            if (!Ready()) return;
            ArticulationModel.Quat? rotation = null;
            switch (grabRotation)
            {
                case GrabRotation.Hand:
                    rotation = _hasHand ? ArticulationModel.QMul(_lastHand, _grabOffset) : _grabKeep;
                    break;
                case GrabRotation.Keep:
                    rotation = _grabKeep;
                    break;
            }
            _model.Solve(_state, _grabJoint, _grabLocal, ToRig(_grabJoint, worldTarget), rotation, _state);
            Scatter(_state);
            PoseParts(_state);
            Fire();
        }

        /// <summary>
        /// Let go. The released pose holds until <see cref="ReturnToAnimation"/> (or, with
        /// <see cref="resumeOnAnimatorChange"/>, until the Animator moves).
        /// </summary>
        public void EndGrab()
        {
            if (!_grabbing) return;
            _grabbing = false;
            Animator a = ResolveAnimator();
            _releaseKnown = a != null && a.isActiveAndEnabled && a.isInitialized;
            if (_releaseKnown)
            {
                AnimatorStateInfo info = a.GetCurrentAnimatorStateInfo(0);
                _releaseHash = info.fullPathHash;
                _releaseTime = info.normalizedTime;
            }
        }

        /// <summary>Stop holding the pose: the animation (or the values) drive the parts again.</summary>
        public void ReturnToAnimation()
        {
            _grabbing = false;
            _releaseKnown = false;
            _held = false;
        }

        /// <summary>The held point's world position now (the grab point riding its link).</summary>
        public Vector3 GrabPointWorld
        {
            get
            {
                if (!_grabbing || !Ready()) return Vector3.zero;
                Vector3 point = U(_model.Point(_state, _grabJoint, _grabLocal));
                Transform space = SpaceOf(_grabJoint);
                return space != null ? space.TransformPoint(point) : point;
            }
        }

        // A world point, and a hand's world rotation, in joint j's chain's space.
        ArticulationModel.Vec ToRig(int j, Vector3 world)
        {
            Transform space = SpaceOf(j);
            return V(space != null ? space.InverseTransformPoint(world) : world);
        }

        ArticulationModel.Quat HandToRig(int j, Quaternion world)
        {
            Transform space = SpaceOf(j);
            return Q(space != null ? Quaternion.Inverse(space.rotation) * world : world);
        }

        Animator ResolveAnimator()
        {
            if (animator == null) animator = GetComponentInParent<Animator>();
            return animator;
        }

        bool AnimatorMoved()
        {
            if (!_releaseKnown) return false;
            Animator a = ResolveAnimator();
            if (a == null || !a.isActiveAndEnabled || !a.isInitialized) return false;
            AnimatorStateInfo info = a.GetCurrentAnimatorStateInfo(0);
            return info.fullPathHash != _releaseHash || Mathf.Abs(info.normalizedTime - _releaseTime) > 1e-4f;
        }

        /// <summary>Give every grab part's meshes a convex collider where they have none (the importer
        /// does this on import; for rigs built at run time). Returns how many were added.</summary>
        public int EnsureGrabColliders()
        {
            int added = 0;
            if (grabs == null) return 0;
            foreach (GrabPart grab in grabs)
                if (grab != null) added += AddGrabColliders(grab.part);
            return added;
        }

        /// <summary>
        /// Give every mesh under <paramref name="node"/> that has no collider a convex
        /// MeshCollider -- what a pointer ray or an XR hand hits. Returns how many were added.
        /// </summary>
        public static int AddGrabColliders(Transform node)
        {
            if (node == null) return 0;
            int added = 0;
            foreach (MeshFilter filter in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = true;
                added++;
            }
            return added;
        }

        // -------------------------------------------------------- the conversions
        // The model runs on Unity's own values (it is the same in any axes a mirror
        // relates), so these only change the precision.
        static ArticulationModel.Vec V(Vector3 v) => new ArticulationModel.Vec(v.x, v.y, v.z);
        static ArticulationModel.Quat Q(Quaternion q) => new ArticulationModel.Quat(q.x, q.y, q.z, q.w);
        static Vector3 U(ArticulationModel.Vec v) => new Vector3((float)v.x, (float)v.y, (float)v.z);
        static Quaternion U(ArticulationModel.Quat q) => new Quaternion((float)q.x, (float)q.y, (float)q.z, (float)q.w);
    }
}
