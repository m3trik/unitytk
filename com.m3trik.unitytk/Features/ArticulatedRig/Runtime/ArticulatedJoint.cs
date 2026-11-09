using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// One joint of an articulated rig, on the part it turns or slides (a lamp's arm, a
    /// drawer): its parent joint, its rest pose in that joint's frame, its anchor (the
    /// point and axes it turns about and slides along, in the rest frame; the part's own
    /// pivot by default), its ordered channels with their limits and weights, and the
    /// channel values -- plain fields, so the Animation window keys them and a clip
    /// poses the rig. Everything is in Unity's terms: positions in the parent joint's
    /// frame as the Inspector shows a local position (a root's under the node its part
    /// hangs under, its space), in the rig's units -- that space's own, so metres for an
    /// unscaled space, and 100 times that under a root that carries a scale of 100 -- and
    /// rotations as Unity turns, so a value reads as the same turn Unity's own rotate
    /// tool gives about that axis. The
    /// <see cref="ArticulatedRig"/> on the rig's root lists its joints and poses each
    /// part from this chain every frame -- from its parent JOINT, never its scene
    /// parent, so the parts of a flat model chain -- and fires <see cref="changed"/>
    /// when a value moves.
    /// </summary>
    /// <remarks>
    /// The pose is <c>rest . anchor . pose(channels) . anchor^-1</c>, pythontk's
    /// articulation model (<see cref="ArticulationModel"/>, ported and held to its
    /// golden cases).
    /// </remarks>
    [AddComponentMenu("Animation/Articulated Joint")]
    [DisallowMultipleComponent]
    public class ArticulatedJoint : MonoBehaviour
    {
        /// <summary>A channel: a turn about one of the anchor's axes, or a slide along one.</summary>
        public enum Axis
        {
            /// <summary>Degrees about the anchor's x axis.</summary>
            [InspectorName("Rotate X")] Rx,
            /// <summary>Degrees about the anchor's y axis.</summary>
            [InspectorName("Rotate Y")] Ry,
            /// <summary>Degrees about the anchor's z axis.</summary>
            [InspectorName("Rotate Z")] Rz,
            /// <summary>A slide along the anchor's x axis, in the rig's units (metres for an unscaled space).</summary>
            [InspectorName("Slide X")] Tx,
            /// <summary>A slide along the anchor's y axis, in the rig's units (metres for an unscaled space).</summary>
            [InspectorName("Slide Y")] Ty,
            /// <summary>A slide along the anchor's z axis, in the rig's units (metres for an unscaled space).</summary>
            [InspectorName("Slide Z")] Tz,
        }

        /// <summary>The order the turns compose in; the first letter applies first.</summary>
        public enum RotateOrder
        {
            /// <summary>x, then y, then z.</summary>
            XYZ,
            /// <summary>y, then z, then x.</summary>
            YZX,
            /// <summary>z, then x, then y.</summary>
            ZXY,
            /// <summary>x, then z, then y.</summary>
            XZY,
            /// <summary>y, then x, then z.</summary>
            YXZ,
            /// <summary>z, then y, then x.</summary>
            ZYX,
        }

        /// <summary>One channel the joint turns or slides on, with its limits.</summary>
        [Serializable]
        public sealed class Channel
        {
            [Tooltip("The axis it turns about or slides along: the anchor's.")]
            public Axis axis;

            [Tooltip("Whether the value stops at Min.")]
            public bool hasMin;

            [Tooltip("The lowest value (degrees; for a slide, the rig's units: metres for an unscaled space).")]
            public float min;

            [Tooltip("Whether the value stops at Max.")]
            public bool hasMax;

            [Tooltip("The highest value (degrees; for a slide, the rig's units: metres for an unscaled space).")]
            public float max;

            [Tooltip("How readily a grab moves this channel against the others: 2 twice as readily as 1, 0 not at all.")]
            [Min(0f)]
            public float weight = 1f;
        }

        /// <summary>Fired with a channel and its new value.</summary>
        [Serializable]
        public sealed class ValueChanged : UnityEvent<Axis, float>
        {
        }

        [Tooltip("The joint this one hangs from: its pose is that joint's, then this one's rest and channels. " +
                 "None: a root, posed in its space, the node its part hangs under.")]
        public ArticulatedJoint parent;

        [Tooltip("Where the part stands at rest, in its parent joint's frame (for a root, under the node its part " +
                 "hangs under), in the rig's units.")]
        public Vector3 restPosition;

        [Tooltip("How the part is turned at rest, in its parent joint's frame.")]
        public Quaternion restRotation = Quaternion.identity;

        [Tooltip("The point the part turns about, in its rest frame (zero: its pivot).")]
        public Vector3 anchorPosition;

        [Tooltip("The axes the part turns about and slides along, in its rest frame (identity: its own).")]
        public Quaternion anchorRotation = Quaternion.identity;

        [Tooltip("The order the turns compose in; the first letter applies first.")]
        public RotateOrder rotateOrder;

        [Tooltip("The channels it turns or slides on, in order.")]
        public List<Channel> channels = new List<Channel>();

        [Tooltip("The turn about the anchor's x axis, in degrees (when the joint has that channel).")]
        public float rx;

        [Tooltip("The turn about the anchor's y axis, in degrees (when the joint has that channel).")]
        public float ry;

        [Tooltip("The turn about the anchor's z axis, in degrees (when the joint has that channel).")]
        public float rz;

        [Tooltip("The slide along the anchor's x axis, in the rig's units, metres for an unscaled space (when the joint has that channel).")]
        public float tx;

        [Tooltip("The slide along the anchor's y axis, in the rig's units, metres for an unscaled space (when the joint has that channel).")]
        public float ty;

        [Tooltip("The slide along the anchor's z axis, in the rig's units, metres for an unscaled space (when the joint has that channel).")]
        public float tz;

        [Tooltip("Fired with the channel and its new value whenever one moves (a clip, a grab, a script).")]
        public ValueChanged changed = new ValueChanged();

        int _version;

        /// <summary>
        /// Bumped whenever the joint's shape (its parent, rest, anchor, order or channels)
        /// may have changed: its rig rebuilds the model the next time it poses. An edit in
        /// the Inspector bumps it; a script changing those fields calls
        /// <see cref="MarkChanged"/>.
        /// </summary>
        public int Version => _version;

        /// <summary>Say the joint's shape changed, so its rig rebuilds its model.</summary>
        public void MarkChanged() => _version++;

        /// <summary>The value of <paramref name="axis"/> (degrees; for a slide, the rig's units: metres for an unscaled space).</summary>
        public float GetValue(Axis axis)
        {
            switch (axis)
            {
                case Axis.Rx: return rx;
                case Axis.Ry: return ry;
                case Axis.Rz: return rz;
                case Axis.Tx: return tx;
                case Axis.Ty: return ty;
                default: return tz;
            }
        }

        /// <summary>
        /// Set <paramref name="axis"/>'s value: the field alone, which the rig poses from
        /// the next time it poses (and holds over a playing clip of the parts).
        /// </summary>
        public void SetValue(Axis axis, float value)
        {
            switch (axis)
            {
                case Axis.Rx: rx = value; break;
                case Axis.Ry: ry = value; break;
                case Axis.Rz: rz = value; break;
                case Axis.Tx: tx = value; break;
                case Axis.Ty: ty = value; break;
                default: tz = value; break;
            }
        }

#if UNITY_EDITOR
        /// <summary>An Inspector edit may change the joint's shape: its rig rebuilds.</summary>
        void OnValidate() => _version++;
#endif
    }
}
