using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace M3trik.Unitytk.Interaction
{
    /// <summary>
    /// Something a <see cref="Hand"/> can hold: a tool, a part. Held, it sits at the hand's
    /// anchor; let go, it rests where it was set up (its home pose); while a clip that moves
    /// it plays, it is lent to the clip (<see cref="ClipPlayer"/>), and after, it goes back to
    /// the hand or its rest. A tool a clip does not move can work at a target instead
    /// (<see cref="WorkAt"/>): it goes there, its tip on the target's pivot, rides the target
    /// while the clip moves it (a screw turning out), and comes back (<see cref="StopWork"/>).
    /// Every way it goes (into the hand, to its work, into a clip's pose, back to its rest) is
    /// one eased move from where it is to where it belongs, taking <see cref="travelSeconds"/>.
    /// </summary>
    /// <remarks>
    /// Held, its colliders are switched off (those that were on, switched back on as it
    /// goes back), so it never stands between the user's pointer or controller and what
    /// they aim at past it.
    /// It is never re-parented: a clip animates it by its place in the delivery's hierarchy,
    /// so it keeps that place and its pose is set each frame instead (after animation, so a
    /// clip that ended holding its last pose does not move it).
    /// <para>The move is of its whole shape: itself and every part under it. A clip often keys
    /// a tool's parts and not the tool (the keys on a ring, each turning in a lock), so where
    /// the tool is and what its parts do are both the clip's while it plays; as it is lent, the
    /// shape it had in the hand blends into the clip's, and as the clip ends, the clip's blends
    /// back into its own (its parts as they rest, at the hand or its rest). Out of a clip, its
    /// parts are held in their own shape, whatever pose the clip left them.</para>
    /// <para>Where it works is the rig's: its pivot at its tip, its axes lined up with the
    /// target's (a screwdriver's blade on a screw's), as a rigger sets a tool up; where the rig
    /// is off, <see cref="tipPosition"/> and <see cref="tipRotation"/> correct it here. Prior
    /// art: XRI's attach transform (an interactable's grab point) and a socket's attach point
    /// meet the same way, a frame on each side; this is the tool's frame, the target's own
    /// pivot the other. XRI eases a grabbed thing to its attach point (attachEaseInTime) the
    /// same way: one eased move, no path.</para>
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/Holdable")]
    [DisallowMultipleComponent]
    public sealed class Holdable : MonoBehaviour
    {
        /// <summary>The <see cref="travelSeconds"/> of a holdable just added.</summary>
        public const float DefaultTravelSeconds = 0.4f;

        [Tooltip("Where it rests (its local position) when no hand holds it.")]
        public Vector3 homePosition;

        // Whether its rest was taken (CaptureHome): one added by hand takes where it stands as the scene starts.
        [SerializeField, HideInInspector] bool _homeCaptured;

        [Tooltip("How it rests (its local rotation) when no hand holds it.")]
        public Quaternion homeRotation = Quaternion.identity;

        [Tooltip("Where it works, from its pivot, in metres along its own axes: zero where the rig put the pivot at its tip.")]
        public Vector3 tipPosition;

        [Tooltip("How it turns at its work (Euler angles in its own axes): zero where the rig lined its axes up with " +
                 "the target's; else the turn that does.")]
        public Vector3 tipRotation;

        [Tooltip("Seconds a move takes: into the hand, to a target it works at, into a clip that moves it, and back. 0: there at once.")]
        [Min(0f)]
        public float travelSeconds = DefaultTravelSeconds;

        [Tooltip("How a move speeds up and slows down: its share of the way (up) against its share of the time (across).")]
        public AnimationCurve ease = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Tooltip("A long way takes longer and a short one less: Travel Seconds over Pace Distance, within Pace Limits. " +
                 "Off: every move takes Travel Seconds.")]
        public bool paced = true;

        /// <summary>The <see cref="paceDistance"/> of a holdable just added, in metres.</summary>
        public const float DefaultPaceDistance = 0.6f;

        [Tooltip("Paced: the way, in metres, a move of Travel Seconds goes; a longer one takes longer, a shorter one less.")]
        [Min(0.01f)]
        public float paceDistance = DefaultPaceDistance;

        /// <summary>The <see cref="paceLimits"/> of a holdable just added: no less than three quarters of its travel seconds, no more than twice.</summary>
        public static Vector2 DefaultPaceLimits => new Vector2(0.75f, 2f);

        [Tooltip("Paced: the least (x) and the most (y) a move takes, as a share of Travel Seconds.")]
        public Vector2 paceLimits = DefaultPaceLimits;

        [Tooltip("Move in real time, unaffected by the time scale (a paused or slowed scene).")]
        public bool unscaledTime;

        /// <summary>The <see cref="approach"/> of a holdable just added: it goes straight to its work.</summary>
        public const float DefaultApproach = 0f;

        [Tooltip("How far back along its tip's axis (metres) it lines up before it goes in to work, and comes out to before it " +
                 "goes back: 0 goes straight there.")]
        [Min(0f)]
        public float approach = DefaultApproach;

        /// <summary>The seconds the move it is on takes (<see cref="travelSeconds"/>, by how far it goes where <see cref="paced"/>).</summary>
        public float TravelFor => _travelFor;

        [Tooltip("A hand took it.")]
        public UnityEvent onTaken = new UnityEvent();

        [Tooltip("It was let go, to go back where it rests (or left where a clip put it in place).")]
        public UnityEvent onReturned = new UnityEvent();

        // A part under it, with the shape it has at rest (its local pose as the scene starts).
        struct Part
        {
            public Transform at;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        struct Pose
        {
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        int _lent;
        readonly List<Collider> _switchedOff = new List<Collider>();
        Transform _workAt;
        bool _lentAtRest;
        List<Part> _parts;                 // every transform under it (none of a holdable inside it), parents first
        Pose[] _from;                      // where it and its parts were as the move began: itself first
        Pose[] _to;
        Pose[] _wrote;                     // its and each part's local pose as the last blend left it
        Pose[] _belongs;                   // and the local pose each belonged in then (the clip's, read before the blend)
        bool _blended;                     // a blend moved it and its parts since they were last put where they belong
        bool _shaped;                      // a clip has moved its parts: out of a clip they are held in their own shape
        float _travelled = 1f;             // 0..1 of the move
        float _travelFor;                  // the seconds this move takes
        bool _unpaced;                     // the move's length is not yet set by how far it goes
        bool _goingIn, _comingOut;         // to its work, or out of it: by its tip's axis where it has an approach
        const float LinedUp = 0.7f;        // the share of a move to work spent lining up; the rest goes in (or, reversed, out)

        /// <summary>The hand holding it; null when it rests.</summary>
        public Hand Holder { get; private set; }

        /// <summary>Whether a hand holds it.</summary>
        public bool IsHeld => Holder != null;

        /// <summary>Whether a playing clip moves it (its pose is the clip's).</summary>
        public bool IsLent => _lent > 0;

        /// <summary>The target it works at (<see cref="WorkAt"/>); null when it works at none.</summary>
        public Transform WorkingAt => _workAt;

        /// <summary>Whether it is still on a move (into the hand, to a target, into a clip, or back).</summary>
        public bool Travelling => _travelled < 1f;

        /// <summary>Let <paramref name="hand"/> take it (a target's selection passes the hand).</summary>
        /// <param name="hand">The hand; null does nothing.</param>
        public void TakeBy(Hand hand)
        {
            if (hand != null) hand.Take(this);
        }

        /// <summary>Let it go: it moves back where it rests, and the hand holding it lets go.</summary>
        public void ReturnHome()
        {
            Hand holder = Holder;
            if (!IsLent) Depart();
            Holder = null;
            _workAt = null;
            if (holder != null) holder.Released(this);
            SwitchCollidersOn();
            if (!IsLent) Settle();
            onReturned.Invoke();
        }

        /// <summary>Make where it stands now its rest.</summary>
        public void CaptureHome()
        {
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            _homeCaptured = true;
        }

        /// <summary>
        /// A clip that moves it started: its pose, and its parts', are the clip's until
        /// <see cref="Reclaim"/>, moved into from the shape it had. A clip that moves its parts but
        /// does not key its own place made that motion from where it rests
        /// (<paramref name="atRest"/>): it stands there while the clip has it, so its parts are where
        /// the clip puts them (a key in its lock).
        /// </summary>
        /// <param name="atRest">The clip does not key its own place: it stands where it rests.</param>
        /// <param name="seconds">How long the move into the clip takes (0: <see cref="travelSeconds"/>).</param>
        public void Lend(bool atRest = false, float seconds = 0f)
        {
            if (_lent++ != 0) return;
            Depart(seconds);
            // A move still on (back to the hand, to its rest) left it part way: it and its parts go where they
            // belonged out of a clip, so what this clip does not key stays in its own shape, and no pose that
            // move wrote is taken for one this clip did not write. The move into the clip is from the shape just read.
            if (_blended) Unblend(all: true);
            _lentAtRest = atRest;
            _shaped = true;
        }

        /// <summary>A clip that moved it ended: it moves back to the hand, or its rest, its parts into their own shape.</summary>
        public void Reclaim()
        {
            if (_lent > 0) _lent--;
            if (_lent != 0) return;
            Depart();
            _lentAtRest = false;
            Settle();
        }

        /// <summary>
        /// A clip that put it in place for good ended (a part fitted into its slot): it stays as the clip left it,
        /// that place its rest and that shape its own from then on, and the hand holding it lets go.
        /// </summary>
        public void Leave()
        {
            if (_lent > 1)
            {
                _lent--;  // another clip still has it
                return;
            }
            if (_lent == 1) Belong();  // exactly where the clip has it, whatever move into the clip was still on
            _lent = 0;
            _lentAtRest = false;
            _travelled = 1f;           // no move to make: it is where it stays
            _unpaced = false;
            _blended = false;
            _parts = null;             // its parts' shape as the clip left them is their own now
            Parts();
            CaptureHome();
            Hand holder = Holder;
            Holder = null;
            _workAt = null;
            if (holder != null) holder.Released(this);
            SwitchCollidersOn();
            onReturned.Invoke();
        }

        /// <summary>
        /// Let it go where it works (<see cref="WorkAt"/>): out of the hand, it stays at its work for good, riding it as
        /// that moves (a part carried to its slot stays in the door as the door swings shut), with no move to make. One
        /// that works nowhere is left where it is (<see cref="Leave"/>).
        /// </summary>
        public void StayAtWork()
        {
            if (_workAt == null)
            {
                Leave();
                return;
            }
            _travelled = 1f;
            _unpaced = false;
            _goingIn = _comingOut = false;
            Hand holder = Holder;
            Holder = null;
            if (holder != null) holder.Released(this);
            SwitchCollidersOn();
            onReturned.Invoke();
        }

        /// <summary>
        /// Work at <paramref name="target"/> while a clip that does not move it plays: it moves
        /// there, its tip (its pivot, corrected by <see cref="tipPosition"/> and
        /// <see cref="tipRotation"/>) on the target's pivot and lined up with its axes, and rides
        /// the target as it moves, until <see cref="StopWork"/>.
        /// </summary>
        /// <param name="target">What it works at (a screw it turns out); null does nothing.</param>
        public void WorkAt(Transform target)
        {
            if (target == null || target == _workAt) return;
            Depart();
            _workAt = target;
            _goingIn = approach > 0f;
            if (_lent == 0) Settle();
        }

        /// <summary>Stop working at its target: it moves back to the hand, or its rest.</summary>
        public void StopWork()
        {
            if (_workAt == null) return;
            Depart();
            _comingOut = approach > 0f;
            _workAt = null;
            if (_lent == 0) Settle();
        }

        /// <summary>
        /// Where its pivot goes for its tip to meet <paramref name="target"/>'s pivot, lined up
        /// with its axes (<see cref="tipPosition"/>, <see cref="tipRotation"/>).
        /// </summary>
        /// <param name="target">What it works at.</param>
        /// <param name="position">Its pivot's position, in the world.</param>
        /// <param name="rotation">Its rotation, in the world.</param>
        public void WorkPose(Transform target, out Vector3 position, out Quaternion rotation)
        {
            rotation = target.rotation * Quaternion.Inverse(Quaternion.Euler(tipRotation));
            position = target.position - rotation * tipPosition;
        }

        // A hand took it.
        internal void HeldBy(Hand hand)
        {
            if (!IsLent) Depart();
            if (Holder == null) SwitchCollidersOff();
            Holder = hand;
            onTaken.Invoke();
            if (!IsLent) Settle();
        }

        void LateUpdate() => Tick(unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime);

        /// <summary>
        /// Move <paramref name="seconds"/> on and pose it: where it belongs (its target, the hand's
        /// anchor, its rest, or the clip's pose, which the clip set this frame), from the shape it
        /// left while its move lasts. A player calls it each frame, after animation; a test may
        /// call it itself.
        /// </summary>
        /// <param name="seconds">How far to move (not below 0).</param>
        public void Tick(float seconds)
        {
            Belong();
            if (_travelled >= 1f) return;
            if (_unpaced) Pace();
            _travelled = _travelFor <= 0f ? 1f : Mathf.Min(1f, _travelled + Mathf.Max(0f, seconds) / _travelFor);
            if (_travelled < 1f) Blend();
        }

        /// <summary>
        /// Finish the move it is on at once: it stands where it belongs. What skips a move does (a
        /// fast-forward, a test of what follows it).
        /// </summary>
        public void Arrive()
        {
            _travelled = 1f;
            _unpaced = false;
            Belong();
        }

        // Pose it now, as a move begins or with none to make: where it belongs, from where it left.
        internal void Settle()
        {
            Belong();
            if (_travelled < 1f) Blend();
        }

        // It, and out of a clip its parts, exactly where they belong now.
        void Belong()
        {
            if (IsLent)
            {
                // The clip's pose is where it belongs. What the clip has not posed anew since the last blend (a
                // part it does not key; any of it while the clip stands still) still stands where that blend
                // left it: back where it belonged then. Each of place, turn and size by itself: a clip may key
                // one of a part's and not the others.
                if (_blended) Unblend(all: false);
                if (_lentAtRest) Rest();  // its parts are the clip's, keyed from where it rests
                return;
            }
            _blended = false;
            if (_shaped)
                foreach (Part part in Parts())
                    if (part.at != null)
                    {
                        part.at.localPosition = part.position;
                        part.at.localRotation = part.rotation;
                        part.at.localScale = part.scale;
                    }
            if (_workAt != null)
            {
                WorkPose(_workAt, out Vector3 position, out Quaternion rotation);
                transform.SetPositionAndRotation(position, rotation);
            }
            else if (Holder != null) transform.SetPositionAndRotation(Holder.Anchor.position, Holder.Anchor.rotation);
            else Rest();
        }

        // Back where each belonged when the last blend moved it: every one (all), or only what still stands as
        // that blend left it, the rest being posed anew since.
        void Unblend(bool all)
        {
            List<Part> parts = Parts();
            for (int i = -1; i < parts.Count; i++)
            {
                Transform at = i < 0 ? transform : parts[i].at;
                if (at == null) continue;
                if (all || at.localPosition == _wrote[i + 1].position) at.localPosition = _belongs[i + 1].position;
                if (all || at.localRotation == _wrote[i + 1].rotation) at.localRotation = _belongs[i + 1].rotation;
                if (all || at.localScale == _wrote[i + 1].scale) at.localScale = _belongs[i + 1].scale;
            }
            _blended = false;
        }

        // It leaves the shape it has now: where it belongs next is moved into from here, over seconds (0: travelSeconds).
        void Depart(float seconds = 0f)
        {
            List<Part> parts = Parts();
            if (_from == null || _from.Length != parts.Count + 1)
            {
                _from = new Pose[parts.Count + 1];
                _to = new Pose[parts.Count + 1];
                _wrote = new Pose[parts.Count + 1];
                _belongs = new Pose[parts.Count + 1];
            }
            Read(_from);
            _travelFor = seconds > 0f ? seconds : travelSeconds;
            _travelled = _travelFor <= 0f ? 1f : 0f;
            _unpaced = paced && seconds <= 0f;
            _goingIn = _comingOut = false;
        }

        // The move's seconds by how far it goes (the farthest any of it does): travelSeconds over paceDistance,
        // within paceLimits' shares of it.
        void Pace()
        {
            _unpaced = false;
            Read(_to);
            float far = 0f;
            for (int i = 0; i < _to.Length; i++) far = Mathf.Max(far, Vector3.Distance(_from[i].position, _to[i].position));
            float least = Mathf.Max(0f, Mathf.Min(paceLimits.x, paceLimits.y)), most = Mathf.Max(paceLimits.x, paceLimits.y);
            _travelFor *= Mathf.Clamp(far / Mathf.Max(paceDistance, 0.01f), least, most);
        }

        // Between the shape it left and where it belongs (as it stands now), by how far along the move is.
        void Blend()
        {
            List<Part> parts = Parts();
            Read(_to);
            for (int i = -1; i < parts.Count; i++)  // where each belongs now, as it stands before the blend moves it
            {
                Transform at = i < 0 ? transform : parts[i].at;
                if (at != null) _belongs[i + 1] = new Pose { position = at.localPosition, rotation = at.localRotation, scale = at.localScale };
            }
            float t = Mathf.Clamp01(ease != null && ease.length > 0 ? ease.Evaluate(_travelled) : _travelled);
            Way(_from[0], _to[0], t, out Vector3 position, out Quaternion rotation);
            transform.SetPositionAndRotation(position, rotation);
            _wrote[0] = new Pose { position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale };
            _blended = true;
            if (!_shaped)  // no clip ever moved its parts: they ride it as they are
            {
                for (int i = 0; i < parts.Count; i++)
                    if (parts[i].at != null) _wrote[i + 1] = _belongs[i + 1];
                return;
            }
            for (int i = 0; i < parts.Count; i++)
            {
                Transform at = parts[i].at;
                if (at == null) continue;
                at.SetPositionAndRotation(Vector3.Lerp(_from[i + 1].position, _to[i + 1].position, t),
                                          Quaternion.Slerp(_from[i + 1].rotation, _to[i + 1].rotation, t));
                at.localScale = Vector3.Lerp(_from[i + 1].scale, _to[i + 1].scale, t);
                _wrote[i + 1] = new Pose { position = at.localPosition, rotation = at.localRotation, scale = at.localScale };
            }
        }

        // Its own pose t of the way: straight there, or, with an approach, lined up back along its tip's axis
        // before it goes in to work, and out along it first as it leaves.
        void Way(Pose from, Pose to, float t, out Vector3 position, out Quaternion rotation)
        {
            if (_goingIn)
            {
                Vector3 back = to.position - to.rotation * TipAxis * approach;
                float lined = Mathf.Clamp01(t / LinedUp);
                rotation = Quaternion.Slerp(from.rotation, to.rotation, lined);
                position = t < LinedUp ? Vector3.Lerp(from.position, back, lined) : Vector3.Lerp(back, to.position, (t - LinedUp) / (1f - LinedUp));
                return;
            }
            if (_comingOut)
            {
                Vector3 back = from.position - from.rotation * TipAxis * approach;
                float split = 1f - LinedUp;
                float turned = Mathf.Clamp01((t - split) / LinedUp);
                rotation = Quaternion.Slerp(from.rotation, to.rotation, turned);
                position = t < split ? Vector3.Lerp(from.position, back, t / split) : Vector3.Lerp(back, to.position, turned);
                return;
            }
            position = Vector3.Lerp(from.position, to.position, t);
            rotation = Quaternion.Slerp(from.rotation, to.rotation, t);
        }

        // Its tip's axis, in its own axes: from its pivot to its tip; its +Z where the rig put the pivot at the tip.
        Vector3 TipAxis => tipPosition.sqrMagnitude > 1e-8f ? tipPosition.normalized : Vector3.forward;

        // Where it and its parts stand now, in the world: itself first.
        void Read(Pose[] poses)
        {
            List<Part> parts = Parts();
            poses[0] = new Pose { position = transform.position, rotation = transform.rotation, scale = transform.localScale };
            for (int i = 0; i < parts.Count; i++)
            {
                Transform at = parts[i].at;
                poses[i + 1] = at == null ? default : new Pose { position = at.position, rotation = at.rotation, scale = at.localScale };
            }
        }

        // Its parts, found once, each with its shape at rest: the scene's, before any clip plays.
        List<Part> Parts()
        {
            if (_parts != null) return _parts;
            _parts = new List<Part>();
            Collect(transform);
            return _parts;
        }

        void Collect(Transform under)
        {
            foreach (Transform child in under)
            {
                if (child.GetComponent<Holdable>() != null) continue;  // a holdable inside it poses itself
                _parts.Add(new Part { at = child, position = child.localPosition, rotation = child.localRotation, scale = child.localScale });
                Collect(child);
            }
        }

        // Its rest where none was taken (added by hand): where it stands as the scene starts.
        void Awake()
        {
            if (!_homeCaptured) CaptureHome();
            Parts();
        }

        // Added in the editor: it rests where it stands.
        void Reset() => CaptureHome();

        void SwitchCollidersOff()
        {
            _switchedOff.Clear();
            foreach (Collider collider in GetComponentsInChildren<Collider>())
                if (collider.enabled)
                {
                    collider.enabled = false;
                    _switchedOff.Add(collider);
                }
        }

        // Back on, those of a target that is on (a target off keeps its own off).
        void SwitchCollidersOn()
        {
            foreach (Collider collider in _switchedOff)
                if (collider != null) collider.enabled = Target.Live(collider);
            _switchedOff.Clear();
        }

        /// <summary>Whether a holdable over <paramref name="collider"/> is held (its colliders off).</summary>
        /// <param name="collider">A collider in the scene.</param>
        internal static bool Held(Collider collider)
        {
            foreach (Holdable holdable in collider.GetComponentsInParent<Holdable>(true))
                if (holdable.IsHeld) return true;
            return false;
        }

        void Rest()
        {
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
        }
    }
}
