using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The ArticulatedRig feature's actions, in Unity's terms: put a rig on a root
    /// (<see cref="CreateRig"/>), add a joint to it on a part, turning about the part's
    /// pivot (<see cref="AddJoint"/>), take a joint's limits from the pose it stands at
    /// (<see cref="SetLimitsFromPose"/>), make where a part stands its joint's rest
    /// (<see cref="CaptureRest"/>), and make a rig's grab parts grabbable by XR hands
    /// (<see cref="MakeXRGrabbable"/>, the XR Interaction Toolkit door's). Each takes an
    /// optional <see cref="OpContext"/>: a dry run plans, a menu's run is one undo step, and
    /// none just runs. A second run of each changes nothing.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's own joints (<c>HingeJoint</c>, <c>ConfigurableJoint</c>,
    /// <c>ArticulationBody</c>) are added from the Add Component menu and set up by hand in
    /// physics terms; they hold no rest pose a clip keys channels against, and no action
    /// takes their limits from a pose. These follow the producers' tools (a rig on a root, a
    /// joint on a part at its pivot, limits from the pose), so a rig made in Unity is the
    /// one an import makes.
    /// </remarks>
    public static class ArticulatedRigActions
    {
        /// <summary>The XR Interaction Toolkit is not in the project, so nothing is made grabbable.</summary>
        public const string NoXRCode = "unity.articulated_rig.no_xri";
        /// <summary>A rig lists no grab part, so nothing would be grabbable.</summary>
        public const string NoGrabsCode = "unity.articulated_rig.no_grabs";

        /// <summary>The verb of a <see cref="SetLimitsFromPose"/> change that moves a limit in, or bounds an
        /// unbounded side: what a menu asks about before it runs.</summary>
        internal const string Narrow = "narrow";

        /// <summary>The verb of one that only moves a limit out to the value.</summary>
        internal const string Widen = "widen";

        /// <summary>The XR door, registered as the editor loads when the XR Interaction Toolkit is in the project.</summary>
        internal static IArticulatedRigXRDoor XRDoor { get; set; }

        /// <summary>
        /// Put an <see cref="ArticulatedRig"/> on <paramref name="root"/>: the rig's space,
        /// listing no joint yet (<see cref="AddJoint"/>). A root that has one gets nothing.
        /// </summary>
        /// <param name="root">A scene object: the node the rig's root joints hang under.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>The rig (the existing one, where the root has one); null in a dry run.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="root"/> is not in a scene (a prefab asset).</exception>
        public static ArticulatedRig CreateRig(GameObject root, OpContext ctx = null)
        {
            InScene(root);
            ArticulatedRig existing = root.GetComponent<ArticulatedRig>();
            if (existing != null) return existing;
            if (ctx != null && !ctx.ShouldCreate($"{root.name}/{nameof(ArticulatedRig)}", "add an Articulated Rig")) return null;
            ArticulatedRig rig = ctx.AddComponent<ArticulatedRig>(root);
            ctx.Log($"Create Rig: an Articulated Rig on '{root.name}'.");
            return rig;
        }

        /// <summary>
        /// Make <paramref name="part"/> a joint of its rig, hanging from
        /// <paramref name="parentJoint"/> (none: a root joint): an <see cref="ArticulatedJoint"/>
        /// on it, at rest where it stands now (its pose in its parent joint's frame; a root's,
        /// under the node it hangs under, its space), turning about its own pivot and axes (the
        /// identity anchor), on <paramref name="channels"/> (unbounded, weight 1, at 0), listed on
        /// the rig after its parent. The rig is the one that lists <paramref name="parentJoint"/>,
        /// or for a root joint the nearest above the part. A part already the rig's joint gets
        /// nothing.
        /// </summary>
        /// <param name="part">A scene object under the rig's root.</param>
        /// <param name="parentJoint">The joint it hangs from, or null for a root joint.</param>
        /// <param name="channels">What it turns or slides on, in order (each once).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>The joint (the existing one, where the part is one); null in a dry run.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="part"/> is null.</exception>
        /// <exception cref="ArgumentException">The part is not in a scene, has no rig above it,
        /// is the rig's own root, is already a joint of another rig, a channel is named twice, or
        /// <paramref name="parentJoint"/> is not its rig's.</exception>
        public static ArticulatedJoint AddJoint(Transform part, ArticulatedJoint parentJoint, ArticulatedJoint.Axis[] channels,
                                                OpContext ctx = null)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            InScene(part.gameObject);
            ArticulatedRig rig = RigOf(part, parentJoint);
            if (part == rig.transform)
                throw new ArgumentException($"'{part.name}' is the rig's own root, its space: add the joint on a part under it.",
                                            nameof(part));
            var axes = new List<ArticulatedJoint.Axis>(channels ?? Array.Empty<ArticulatedJoint.Axis>());
            for (int i = 0; i < axes.Count; i++)
                if (axes.IndexOf(axes[i]) != i)
                    throw new ArgumentException($"channel {axes[i]} is named twice.", nameof(channels));
            ArticulatedJoint existing = part.GetComponent<ArticulatedJoint>();
            if (existing != null && rig.IndexOf(existing) >= 0) return existing;
            ArticulatedRig other = existing != null ? ArticulatedJointEditor.RigOf(existing) : null;
            if (other != null)
                throw new ArgumentException($"'{part.name}' is already a joint of '{other.name}': a joint is one rig's. " +
                                            $"Remove it from that rig's Joints first, or add it to '{other.name}'.", nameof(part));

            string what = axes.Count == 0 ? "no channel" : string.Join(", ", axes);
            string under = parentJoint != null ? $"from '{parentJoint.name}'" : "as a root";
            bool live = existing != null
                ? ctx == null || ctx.ShouldChange(existing, $"make '{part.name}' a joint {under} on {what}")
                : ctx == null || ctx.ShouldCreate($"{part.name}/{nameof(ArticulatedJoint)}", $"add a joint {under} on {what}");
            string listing = $"list the joint '{part.name}' on '{rig.name}'";
            if (!live)
            {
                ctx.ShouldChange(rig, listing);  // the plan names the listing too
                return null;
            }

            ArticulatedJoint joint = existing != null ? existing : ctx.AddComponent<ArticulatedJoint>(part.gameObject);
            // Asked after the component is made: Unity's record of a new component closes any
            // record of the rig taken before it, so the listing would not undo.
            if (ctx != null) ctx.ShouldChange(rig, listing);
            joint.parent = parentJoint;
            // Its rest: where it stands now, in its parent joint's frame (a root's, under its space),
            // as the rig reads it.
            ArticulatedRig.StandingLocal(joint, out Vector3 rest, out Quaternion turn);
            joint.restPosition = rest;
            joint.restRotation = turn;
            joint.anchorPosition = Vector3.zero;
            joint.anchorRotation = Quaternion.identity;
            joint.channels.Clear();
            foreach (ArticulatedJoint.Axis axis in axes) joint.channels.Add(new ArticulatedJoint.Channel { axis = axis });
            joint.rx = joint.ry = joint.rz = joint.tx = joint.ty = joint.tz = 0f;
            joint.MarkChanged();
            rig.joints.Add(joint);
            rig.Rebuild();
            ctx.Edited(joint);
            ctx.Edited(rig);
            ctx.Log($"Add Joint: '{part.name}' on '{rig.name}', {under}, on {what}.");
            return joint;
        }

        /// <summary>
        /// Take <paramref name="joint"/>'s limits from the pose it stands at: each channel away
        /// from rest gives its value to the limit on its side (min for a value below 0, max
        /// above), as the producers' "limits from the pose" does -- pose the part where it
        /// physically stops, then run this. With <paramref name="widen"/>, a limit only moves
        /// out to the value where the value lies beyond it (a limit is never narrowed, and an
        /// unbounded side stays unbounded). A limit the other side would cross moves with it.
        /// </summary>
        /// <param name="joint">A scene joint.</param>
        /// <param name="widen">Only widen: never narrow a limit, never bound an unbounded side.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>How many limits it set (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="joint"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="joint"/> is not in a scene.</exception>
        public static int SetLimitsFromPose(ArticulatedJoint joint, bool widen = false, OpContext ctx = null)
        {
            if (joint == null) throw new ArgumentNullException(nameof(joint));
            InScene(joint.gameObject);
            int set = 0;
            foreach (ArticulatedJoint.Channel c in joint.channels)
            {
                if (c == null) continue;
                float value = joint.GetValue(c.axis);
                if (Mathf.Abs(value) < 1e-6f) continue;  // at rest: this channel says nothing about a stop
                bool low = value < 0f;
                bool has = low ? c.hasMin : c.hasMax;
                float limit = low ? c.min : c.max;
                if (has && limit == value) continue;
                // Widening moves a limit out to a value beyond it; anything else narrows.
                bool widens = has && (low ? value < limit : value > limit);
                if (widen && !widens) continue;
                string side = low ? "min" : "max";
                string was = has ? limit.ToString("0.###") : "unbounded";
                if (ctx != null && !ctx.ShouldChange(joint, $"{(widens ? Widen : Narrow)} {joint.name} {c.axis} {side} {was} -> {value:0.###}"))
                {
                    set++;
                    continue;
                }
                if (low)
                {
                    c.hasMin = true;
                    c.min = value;
                    if (c.hasMax && c.max < value) c.max = value;
                }
                else
                {
                    c.hasMax = true;
                    c.max = value;
                    if (c.hasMin && c.min > value) c.min = value;
                }
                set++;
            }
            if (set > 0 && (ctx == null || !ctx.DryRun))
            {
                joint.MarkChanged();
                ctx.Edited(joint);
                ctx.Log($"Set Limits From Pose: {set} limit(s) of '{joint.name}' from its pose.");
            }
            return set;
        }

        /// <summary>
        /// Make where <paramref name="joint"/>'s part stands its rest: the rest that, at the
        /// joint's values, poses the part exactly where it stands now (in its parent joint's
        /// frame; a root's under its space) -- after a move made with the Move tool, which the
        /// edit-mode preview yields to, so play poses the part where it was put. The values stay.
        /// </summary>
        /// <param name="joint">A scene joint its rig lists.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether the rest changed (in a dry run, would); false where it already poses the part there.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="joint"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="joint"/> is not in a scene, no rig
        /// lists it, or its rig's joints make no rig.</exception>
        public static bool CaptureRest(ArticulatedJoint joint, OpContext ctx = null)
        {
            if (joint == null) throw new ArgumentNullException(nameof(joint));
            InScene(joint.gameObject);
            if (!RestFromStanding(joint, out Vector3 rest, out Quaternion turn, out string problem))
                throw new ArgumentException(problem, nameof(joint));
            if (Same(rest, turn, joint.restPosition, joint.restRotation)) return false;
            if (ctx != null && !ctx.ShouldChange(joint, $"capture {joint.name}'s rest from where it stands")) return true;
            joint.restPosition = rest;
            joint.restRotation = turn;
            joint.MarkChanged();
            ctx.Edited(joint);
            ctx.Log($"Capture Rest From Transform: '{joint.name}' rests where it stands.");
            return true;
        }

        /// <summary>Whether <paramref name="joint"/>'s part stands off where its rest and values pose it
        /// (a rig that follows its animation never does: its clip, not its rest, places the parts).</summary>
        internal static bool RestDiffers(ArticulatedJoint joint)
        {
            ArticulatedRig rig = joint != null ? ArticulatedJointEditor.RigOf(joint) : null;
            if (rig == null || rig.followAnimation) return false;
            return RestFromStanding(joint, out Vector3 rest, out Quaternion turn, out _)
                   && !Same(rest, turn, joint.restPosition, joint.restRotation);
        }

        // The rest that poses joint's part where it stands at its values: with the joint's local
        // L = rest . P (P the anchor's turn and slide at the values), the standing local S gives
        // rest = S . P^-1.
        static bool RestFromStanding(ArticulatedJoint joint, out Vector3 rest, out Quaternion turn, out string problem)
        {
            rest = joint.restPosition;
            turn = joint.restRotation;
            ArticulatedRig rig = ArticulatedJointEditor.RigOf(joint);
            if (rig == null)
            {
                problem = $"no Articulated Rig above '{joint.name}' lists it: add it to its rig's Joints first.";
                return false;
            }
            ArticulationModel model = rig.Model;
            if (model == null)
            {
                problem = $"'{rig.name}' does not pose: {rig.Problem}";
                return false;
            }
            problem = null;
            int j = rig.IndexOf(joint);
            ArticulationModel.Frame local = model.Local(rig.GetState(), j);
            ArticulationModel.Joint spec = model.Joints[j];
            ArticulationModel.Quat back = ArticulationModel.QConj(spec.q);
            ArticulationModel.Quat c = ArticulationModel.QMul(back, local.q);
            ArticulationModel.Vec o = ArticulationModel.QRot(back, ArticulationModel.Sub(local.t, spec.t));
            ArticulatedRig.StandingLocal(joint, out Vector3 standing, out Quaternion standingTurn);
            var s = new ArticulationModel.Quat(standingTurn.x, standingTurn.y, standingTurn.z, standingTurn.w);
            ArticulationModel.Quat q = ArticulationModel.QNormalize(ArticulationModel.QMul(s, ArticulationModel.QConj(c)));
            ArticulationModel.Vec t = ArticulationModel.Sub(new ArticulationModel.Vec(standing.x, standing.y, standing.z),
                                                            ArticulationModel.QRot(q, o));
            rest = new Vector3((float)t.x, (float)t.y, (float)t.z);
            turn = new Quaternion((float)q.x, (float)q.y, (float)q.z, (float)q.w);
            return true;
        }

        static bool Same(Vector3 t, Quaternion q, Vector3 rest, Quaternion restTurn)
        {
            Quaternion r = restTurn.x == 0f && restTurn.y == 0f && restTurn.z == 0f && restTurn.w == 0f ? Quaternion.identity : restTurn;
            return Vector3.Distance(t, rest) < 1e-5f && Quaternion.Angle(q, r) < 1e-2f;
        }

        /// <summary>
        /// Make <paramref name="rig"/>'s grab parts grabbable by XR hands: the XR Interaction
        /// Toolkit door's setup on the rig's root, which makes each grab part an XR interactable
        /// that hands the grab to the rig. Without the toolkit in the project it says so and does
        /// nothing; a rig that lists no grab part is said too. A rig with the setup gets nothing.
        /// </summary>
        /// <param name="rig">A scene rig.</param>
        /// <param name="ctx">The run's context: a dry run lists the change; null just makes it.</param>
        /// <returns>Whether the rig has the setup (in a dry run, would).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="rig"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="rig"/> is not in a scene.</exception>
        public static bool MakeXRGrabbable(ArticulatedRig rig, OpContext ctx = null)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            InScene(rig.gameObject);
            if (XRDoor == null)
            {
                ctx.Raise(NoXRCode, $"'{rig.name}' is not made grabbable: the XR Interaction Toolkit (3.x) is not in the " +
                                    "project. Add com.unity.xr.interaction.toolkit through the Package Manager, then run it again.",
                          location: rig.name);
                return false;
            }
            bool any = false;
            foreach (ArticulatedRig.GrabPart grab in rig.grabs)
                any |= grab != null && grab.part != null && grab.joint != null;
            if (!any)
                ctx.Raise(NoGrabsCode, $"'{rig.name}' lists no grab part, so an XR hand has nothing to take hold of: add one to " +
                                       "its Grabs (a part and the joint it rides).", location: rig.name);
            return XRDoor.MakeGrabbable(rig, ctx);
        }

        // The rig a joint on part joins: the one listing its parent joint, else the nearest above.
        static ArticulatedRig RigOf(Transform part, ArticulatedJoint parentJoint)
        {
            for (Transform t = part; t != null; t = t.parent)
                foreach (ArticulatedRig rig in t.GetComponents<ArticulatedRig>())
                    if (parentJoint == null || rig.IndexOf(parentJoint) >= 0) return rig;
            throw new ArgumentException(parentJoint == null
                ? $"'{part.name}' has no Articulated Rig above it: put one on the rig's root first (CreateRig)."
                : $"'{parentJoint.name}' is not a joint of a rig above '{part.name}': hang the part from a joint of its own rig.",
                parentJoint == null ? nameof(part) : nameof(parentJoint));
        }

        static void InScene(GameObject go)
        {
            if (go == null) throw new ArgumentNullException(nameof(go));
            if (!go.scene.IsValid() || EditorUtility.IsPersistent(go))
                throw new ArgumentException($"'{go.name}' is not in a scene: place the prefab in a scene (or open it in " +
                                            "Prefab Mode) and run it there", nameof(go));
        }
    }
}
