// ----------------------------------------------------------------------------
// The ArticulatedRig feature's import half: a delivery's ``articulation`` record
// (articulation v2, published by the producers onto the shared ``data_export``
// carrier) read through its generated shape (ArticulationRecord.g.cs) at the
// version pythontk declares alone: another version -- a version 1 record, with
// no anchors and lengths in the file's own units -- or a record that fails that
// shape is refused with its fix (re-export the file), and nothing is wired.
//
// On import, each rig of the record becomes components BY REFERENCE: an
// ArticulatedJoint on each joint's transform, found by name (RecordNames:
// exact, else the namespace-stripped leaf; a rig's joints settled together; a
// tie stays unbound and is reported, and leaves its rig out), and an
// ArticulatedRig listing them and its grab parts (each found under its joint's
// part), on the node its root joints hang under -- or, where roots hang under
// different nodes, the nearest node above them all: each root's chain poses in
// its own root's space, as every reader of the record does, so neither split
// roots nor two rigs on one node are refused. The record is stated in glTF's
// frame, which the kernel converts into the import's (Imports.Frame, the core's
// GltfFrame: each length as the import scales the file, read off the importer,
// and X mirrored), so each rest and anchor lands mirrored and the channels
// that turn or slide the other way there (ry, rz, tx) have their limits negated
// and swapped, by the model's one mirror table (ArticulationModel.MirrorLimits,
// which its tests pin): the components hold Unity's own values. The
// values start where the imported parts stand (the file's own pose), the rig
// follows the parts' animation, and every grab part's bare meshes get a convex
// collider.
//
// The runtime never reads the record: the components hold what it resolved.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using Shape = ArticulationRecord.Shape;

[assembly: ReadsRecord(UnitytkRecords.Articulation, typeof(ArticulationRecord))]
[assembly: InternalsVisibleTo("M3trik.Unitytk.ArticulatedRig.Tests")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.ArticulatedRig.XRI.Editor")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.ArticulatedRig.XRI.Tests")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Tools.ArticulatedRig")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Tools.ArticulatedRig.Tests")]

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// AssetPostprocessor that turns a delivery's articulated rigs into components on
    /// import, from the <c>articulation</c> record on <c>data_export</c>: an
    /// <see cref="ArticulatedJoint"/> on each joint's transform and an
    /// <see cref="ArticulatedRig"/> on the node its roots hang under (the nearest above
    /// them all, where they hang under several), by reference, in Unity's axes, posed
    /// where the file stands, its grab parts given colliders.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's FBX import hands the record's string to every postprocessor as
    /// a user property and knows nothing of it; this reads it through the carrier
    /// (<see cref="IRecordSource"/>) as every unitytk importer does. The record states a
    /// length as glTF's frame holds the file (FBX2glTF reads a local as the file value x
    /// 0.01 whatever its UnitScaleFactor); Unity's import multiplies every local by the
    /// file's unit (<c>fileScale</c>, with Convert Units on) and <c>globalScale</c>, a
    /// root's own scale staying above the rig -- measured 2026-11-08 on deliveries whose
    /// UnitScaleFactor is 1, 100 and 0.1, at global scales 1 and 2: a joint whose file
    /// value is 24 imported at 0.24 from a centimetre file and at 24 from a metre one. So
    /// the record's lengths are scaled by that conversion, read off the importer
    /// (<see cref="Imports.Frame"/>, <see cref="GltfFrame.Length"/>), nothing measured;
    /// and the import mirrors X (<see cref="GltfFrame.Point(double[])"/>,
    /// <see cref="GltfFrame.Rotation(double[])"/>), which the components take as they are.
    /// </remarks>
    public class ArticulatedRigImporter : AssetPostprocessor
    {
        const string Tag = "ArticulatedRig";

        /// <summary>Bump to reimport every model when what the import makes changes.</summary>
        const uint VERSION = 3;

        /// <inheritdoc/>
        public override uint GetVersion() => VERSION;

        // Per-assetPath raw JSON, collected during
        // OnPostprocessGameObjectWithUserProperties (hierarchy not yet parented)
        // and consumed in OnPostprocessModel (root is final).
        static readonly Dictionary<string, string> _pending = new Dictionary<string, string>();

        static IRecordSource Records => Provided.One<IRecordSource>();

        bool ShouldProcess() => Imports.IsDelivery(assetPath);

        void OnPreprocessModel()
        {
            // Fresh-import reset: JSON stranded by an import that aborted between
            // the callbacks must not resurrect on a re-export without the carrier.
            _pending.Remove(assetPath);
        }

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] propNames, object[] values)
        {
            if (!ShouldProcess()) return;
            IRecordSource records = Records;
            if (records.TryGetRecord(propNames, values, ArticulationRecord.Channel, out string json))
                records.Collect(_pending, assetPath, go, json);
        }

        void OnPostprocessModel(GameObject root)
        {
            _pending.TryGetValue(assetPath, out string json);
            _pending.Remove(assetPath);
            if (!ShouldProcess()) return;
            Wire(root, json, assetPath, Records, Imports.Frame(assetImporter as ModelImporter));
        }

        /// <summary>
        /// Fill the rigs of the model <paramref name="root"/> from the record
        /// <paramref name="json"/> read through <paramref name="records"/>, each by reference;
        /// a rig whose joints do not all bind, or whose root joint is the model's root, is left
        /// out and reported. Every rig and joint component under the root the record does
        /// not fill goes (a re-export without it, a refused record, a rig left out). Returns
        /// the rigs wired.
        /// </summary>
        /// <param name="assetPath">The model's file, for the import log and a refusal.</param>
        /// <param name="frame">The frame the import lands the record in (<see cref="Imports.Frame"/>); the default frame when null.</param>
        internal static List<ArticulatedRig> Wire(GameObject root, string json, string assetPath, IRecordSource records,
                                                  GltfFrame? frame = null)
        {
            GltfFrame unit = frame ?? GltfFrame.Default;
            Shape.Payload payload = records.Read(
                ArticulationRecord.Channel, json, ArticulationRecord.CurrentVersion, Shape.Payload.Read, Tag, assetPath);
            var wired = new List<ArticulatedRig>();
            var filled = new HashSet<ArticulatedJoint>();
            if (payload != null)
            {
                var nodes = new List<Transform>(root.GetComponentsInChildren<Transform>(true));
                foreach (Shape.Rig spec in payload.rigs)
                {
                    ArticulatedRig rig = WireRig(root.transform, spec, nodes, wired, filled, assetPath, unit);
                    if (rig != null) wired.Add(rig);
                }
            }
            int joints = 0, colliders = 0;
            foreach (ArticulatedRig rig in wired)
            {
                joints += rig.joints.Count;
                colliders += rig.EnsureGrabColliders();
            }
            // What the record does not fill is stale: the prefab matches the re-exported source.
            foreach (ArticulatedRig stale in root.GetComponentsInChildren<ArticulatedRig>(true))
                if (!wired.Contains(stale)) UnityEngine.Object.DestroyImmediate(stale, true);
            foreach (ArticulatedJoint stale in root.GetComponentsInChildren<ArticulatedJoint>(true))
                if (!filled.Contains(stale)) UnityEngine.Object.DestroyImmediate(stale, true);
            if (wired.Count > 0)
                ImportLog.Info(assetPath, $"wired {wired.Count} articulated rig(s) ({joints} joint(s), {colliders} grab " +
                                          "collider(s) added)");
            return wired;
        }

        static ArticulatedRig WireRig(Transform root, Shape.Rig spec, List<Transform> nodes, List<ArticulatedRig> wired,
                                      HashSet<ArticulatedJoint> filled, string assetPath, GltfFrame unit)
        {
            try
            {
                _ = new ArticulationModel(spec);
            }
            catch (ArgumentException e)
            {
                ImportLog.Warning(assetPath, $"articulated rig '{spec.name}' is not one the model poses ({e.Message}): it is " +
                                             "left out. Re-export it from a current DCC exporter.");
                return null;
            }
            var names = spec.joints.ConvertAll(j => j.name);
            RecordNames.Match<Transform>[] matches = RecordNames.Resolve(
                nodes, names.ConvertAll(n => new RecordNames.Entry(n)), t => t.name, t => t.parent, root);
            bool bound = true;
            for (int j = 0; j < matches.Length; j++)
            {
                if (matches[j].Outcome == RecordNames.Outcome.Bound) continue;
                bound = false;
                Unbound(assetPath, $"articulated rig '{spec.name}''s joint '{names[j]}'", matches[j], "the rig is left out");
            }
            if (!bound) return null;
            var parts = Array.ConvertAll(matches, m => m.Item);

            // Each root's chain poses in its own space, the node its part hangs under; the rig
            // sits on the node those spaces share, or the nearest above them all.
            Transform home = null;
            for (int j = 0; j < parts.Length; j++)
            {
                if (spec.joints[j].parent.HasValue) continue;
                Transform above = parts[j].parent;
                if (above == null)
                {
                    ImportLog.Warning(assetPath, $"articulated rig '{spec.name}''s root joint '{names[j]}' is the model's root, " +
                                                 "which leaves its chain no space to pose in: it is left out. Group the rig " +
                                                 "under a node in the DCC.");
                    return null;
                }
                home = home == null ? above : Shared(home, above);
            }
            // A node may hold several rigs (each poses in its roots' spaces): a reimport refills
            // the ones it held, in order.
            ArticulatedRig rig = null;
            foreach (ArticulatedRig held in home.GetComponents<ArticulatedRig>())
                if (!wired.Contains(held))
                {
                    rig = held;
                    break;
                }
            if (rig == null) rig = home.gameObject.AddComponent<ArticulatedRig>();

            rig.joints.Clear();
            for (int j = 0; j < parts.Length; j++)
            {
                ArticulatedJoint joint = parts[j].GetComponent<ArticulatedJoint>();
                if (joint == null) joint = parts[j].gameObject.AddComponent<ArticulatedJoint>();
                Shape.Joint js = spec.joints[j];
                Fill(joint, js, js.parent.HasValue ? rig.joints[js.parent.Value] : null, unit);
                rig.joints.Add(joint);
                filled.Add(joint);
            }
            rig.grabs.Clear();
            foreach (Shape.Grab grab in spec.grab)
            {
                int j = grab.joint.Value;
                if (j < 0 || j >= parts.Length)
                {
                    ImportLog.Warning(assetPath, $"articulated rig '{spec.name}''s grab part '{grab.node}' rides joint {j}, " +
                                                 "which the rig does not have: it is not grabbable. Re-export the rig.");
                    continue;
                }
                var under = new List<Transform>(parts[j].GetComponentsInChildren<Transform>(true));
                RecordNames.Match<Transform> match = RecordNames.Find(under, grab.node, t => t.name, t => t.parent, root);
                if (match.Outcome == RecordNames.Outcome.Bound)
                    rig.grabs.Add(new ArticulatedRig.GrabPart { part = match.Item, joint = rig.joints[j] });
                else
                    Unbound(assetPath, $"articulated rig '{spec.name}''s grab part '{grab.node}' (under '{parts[j].name}')",
                            match, "it is not grabbable");
            }
            // The delivery's clips animate the parts: the rig follows them, from the file's own pose.
            rig.followAnimation = true;
            rig.Rebuild();
            rig.ReadPose();
            return rig;
        }

        // The nearest node at or above both a and b (both under one model root, so one is).
        static Transform Shared(Transform a, Transform b)
        {
            for (Transform t = a; t != null; t = t.parent)
                if (b == t || b.IsChildOf(t)) return t;
            return a.root;
        }

        static void Unbound(string assetPath, string what, RecordNames.Match<Transform> match, string fallback)
            => RecordNamesLog.Unbound(assetPath, what, match.Outcome, match.Candidates, fallback);

        /// <summary>
        /// <paramref name="joint"/> filled from the record's <paramref name="spec"/>, in Unity's
        /// axes: its parent joint, its rest and anchor mirrored, its rotate order, and its
        /// channels with the limits of those that turn or slide the other way in Unity negated
        /// and swapped; every length -- rest, anchor point, a slide's limits -- as the import
        /// scales the file (<paramref name="frame"/>, the default frame when null). Its values
        /// start at rest (the import reads the file's pose after).
        /// </summary>
        internal static void Fill(ArticulatedJoint joint, Shape.Joint spec, ArticulatedJoint parent, GltfFrame? frame = null)
        {
            GltfFrame unit = frame ?? GltfFrame.Default;
            joint.parent = parent;
            joint.restPosition = Point(spec.t, unit);
            joint.restRotation = Turn(spec.q);
            joint.anchorPosition = Point(spec.anchor.t, unit);
            joint.anchorRotation = Turn(spec.anchor.q);
            joint.rotateOrder = (ArticulatedJoint.RotateOrder)Array.IndexOf(ArticulationModel.RotateOrders, spec.rotate_order);
            joint.channels.Clear();
            foreach (Shape.Channel c in spec.channels)
            {
                int code = Array.IndexOf(ArticulationModel.ChannelNames, c.channel);
                var axis = (ArticulatedJoint.Axis)code;
                ArticulationModel.MirrorLimits(code, c.min, c.max, out double? lo, out double? hi);
                double length = code >= 3 ? unit.Length(1.0) : 1.0;  // tx/ty/tz slide a length; rx/ry/rz turn degrees
                lo *= length;
                hi *= length;
                joint.channels.Add(new ArticulatedJoint.Channel
                {
                    axis = axis,
                    hasMin = lo.HasValue,
                    min = lo.HasValue ? (float)lo.Value : 0f,
                    hasMax = hi.HasValue,
                    max = hi.HasValue ? (float)hi.Value : 0f,
                    weight = (float)c.weight.Value,
                });
            }
            joint.rx = joint.ry = joint.rz = joint.tx = joint.ty = joint.tz = 0f;
            joint.MarkChanged();
        }

        /// <summary>A record point (glTF's frame) as <paramref name="frame"/>'s import lands it: X mirrored, scaled as the file.</summary>
        internal static Vector3 Point(double[] t, GltfFrame frame)
        {
            double[] p = GltfFrame.Point(t);
            return new Vector3((float)frame.Length(p[0]), (float)frame.Length(p[1]), (float)frame.Length(p[2]));
        }

        /// <summary>A record rotation (x, y, z, w) as Unity's FBX import lands it, mirrored.</summary>
        internal static Quaternion Turn(double[] q)
        {
            double[] m = GltfFrame.Rotation(q);
            return new Quaternion((float)m[0], (float)m[1], (float)m[2], (float)m[3]);
        }
    }
}
