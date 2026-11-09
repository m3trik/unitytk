using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.ArticulatedRig;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using Rig = M3trik.Unitytk.ArticulatedRig.ArticulatedRig;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Tools.ArticulatedRig.Tests")]

namespace M3trik.Unitytk.Tools.ArticulatedRig
{
    /// <summary>
    /// Tools ▸ unitytk ▸ Articulated Rig: a line per menu item over the feature's actions
    /// (<see cref="ArticulatedRigActions"/>), run as a person's menu runs one: one undo step,
    /// progress with Cancel, and its issues in the Console. Set Limits From Pose, which may
    /// narrow limits a person set, is planned first and asks before it narrows any: the ask
    /// lists the plan's own narrowing changes.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>MenuItem</c>, the convention every Unity tool follows; Unity
    /// Search already finds each item, so there is no palette to build. The ask is Unity's own
    /// <c>EditorUtility.DisplayDialog</c>.
    /// </remarks>
    static class ArticulatedRigMenu
    {
        const string Root = "Tools/unitytk/Articulated Rig/";
        const string CreatePath = Root + "Create Rig";
        const string HingePath = Root + "Add Hinge Joint";
        const string SlidePath = Root + "Add Slide Joint";
        const string BallPath = Root + "Add Ball Joint";
        const string LimitsPath = Root + "Set Limits From Pose";
        const string WidenPath = Root + "Widen Limits To Pose";
        const string CapturePath = Root + "Capture Rest From Transform";
        const string XRPath = Root + "Make XR Grabbable";
        const string LimitsTitle = "Set Limits From Pose";

        /// <summary>How many of the plan's narrowings the ask lists by name.</summary>
        const int Listed = 12;

        [MenuItem(CreatePath)]
        static void Create()
            => OpContext.RunMenu("Create Rig", ctx =>
            {
                foreach (GameObject go in InScene()) ArticulatedRigActions.CreateRig(go, ctx);
            });

        [MenuItem(CreatePath, true)]
        static bool CanCreate() => InScene().Length > 0;

        [MenuItem(HingePath)]
        static void Hinge() => AddJoints("Add Hinge Joint", Parts(), ArticulatedJoint.Axis.Rz);

        [MenuItem(SlidePath)]
        static void Slide() => AddJoints("Add Slide Joint", Parts(), ArticulatedJoint.Axis.Tx);

        [MenuItem(BallPath)]
        static void Ball() => AddJoints("Add Ball Joint", Parts(), ArticulatedJoint.Axis.Rx, ArticulatedJoint.Axis.Ry,
                                        ArticulatedJoint.Axis.Rz);

        [MenuItem(HingePath, true)]
        [MenuItem(SlidePath, true)]
        [MenuItem(BallPath, true)]
        static bool CanAdd() => Parts().Length > 0;

        /// <summary>
        /// The menu's Add Joint over <paramref name="parts"/>, parents first: each a joint on
        /// <paramref name="channels"/>, hanging from the nearest joint above it (none: a root).
        /// </summary>
        internal static int AddJoints(string title, Transform[] parts, params ArticulatedJoint.Axis[] channels)
            => OpContext.RunMenu(title, ctx =>
            {
                int added = 0;
                for (int i = 0; i < parts.Length; i++)
                {
                    ctx.Progress((double)i / parts.Length, $"'{parts[i].name}' ({i + 1} of {parts.Length})");
                    ArticulatedJoint above = parts[i].parent != null ? parts[i].parent.GetComponentInParent<ArticulatedJoint>() : null;
                    if (ArticulatedRigActions.AddJoint(parts[i], above, channels, ctx) != null) added++;
                }
                return added;
            });

        [MenuItem(LimitsPath)]
        static void Limits() => SetLimits(SelectedJoints(), (title, message) => EditorUtility.DisplayDialog(title, message, "Narrow", "Cancel"));

        [MenuItem(LimitsPath, true)]
        [MenuItem(WidenPath, true)]
        [MenuItem(CapturePath, true)]
        static bool CanSetLimits() => SelectedJoints().Length > 0;

        [MenuItem(CapturePath)]
        static void Capture()
            => OpContext.RunMenu("Capture Rest From Transform", ctx =>
            {
                foreach (ArticulatedJoint joint in SelectedJoints()) ArticulatedRigActions.CaptureRest(joint, ctx);
            });

        [MenuItem(WidenPath)]
        static void Widen()
            => OpContext.RunMenu("Widen Limits To Pose", ctx =>
            {
                foreach (ArticulatedJoint joint in SelectedJoints()) ArticulatedRigActions.SetLimitsFromPose(joint, true, ctx);
            });

        /// <summary>
        /// The menu's Set Limits From Pose over <paramref name="joints"/>: planned first (a dry
        /// run of <see cref="ArticulatedRigActions.SetLimitsFromPose"/>); where its plan would
        /// narrow a limit -- move one in, or bound an unbounded side -- those changes are put to
        /// <paramref name="confirm"/> (a title and a message; true: go on), and declined, nothing
        /// changes. Then run as a menu runs an action.
        /// </summary>
        /// <returns>How many limits it set; 0 when declined.</returns>
        internal static int SetLimits(ArticulatedJoint[] joints, Func<string, string, bool> confirm)
        {
            var plan = new OpContext(dryRun: true);
            foreach (ArticulatedJoint joint in joints) ArticulatedRigActions.SetLimitsFromPose(joint, false, plan);
            string verb = ArticulatedRigActions.Narrow + " ";
            List<string> narrowed = plan.Changes.Where(c => c.action.StartsWith(verb, StringComparison.Ordinal))
                                        .Select(c => c.action.Substring(verb.Length)).ToList();
            if (narrowed.Count > 0)
            {
                string listed = string.Join("\n", narrowed.Take(Listed).Select(n => "- " + n));
                if (narrowed.Count > Listed) listed += $"\n- and {narrowed.Count - Listed} more";
                string message = $"{LimitsTitle} narrows {narrowed.Count} limit(s), each to the value its joint stands at:\n\n" +
                                 listed + "\n\nThe joint then stops there. Undo brings the limits back.";
                if (!confirm(LimitsTitle, message))
                {
                    plan.Log($"{LimitsTitle}: cancelled; the limits are left as they are.");
                    return 0;
                }
            }
            return OpContext.RunMenu(LimitsTitle, ctx =>
            {
                int set = 0;
                foreach (ArticulatedJoint joint in joints) set += ArticulatedRigActions.SetLimitsFromPose(joint, false, ctx);
                return set;
            });
        }

        [MenuItem(XRPath)]
        static void XR()
            => OpContext.RunMenu("Make XR Grabbable", ctx =>
            {
                foreach (Rig rig in SelectedRigs()) ArticulatedRigActions.MakeXRGrabbable(rig, ctx);
            });

        [MenuItem(XRPath, true)]
        static bool CanXR() => SelectedRigs().Length > 0;

        // The selected objects a scene holds (a prefab asset poses no scene).
        static GameObject[] InScene() => Array.FindAll(Selection.gameObjects, go => go.scene.IsValid());

        // The selected parts under a rig, parents first.
        static Transform[] Parts()
            => InScene().Select(go => go.transform)
                        .Where(t => t.parent != null && t.parent.GetComponentInParent<Rig>() != null)
                        .OrderBy(Depth).ToArray();

        static int Depth(Transform t)
        {
            int depth = 0;
            for (Transform p = t.parent; p != null; p = p.parent) depth++;
            return depth;
        }

        static ArticulatedJoint[] SelectedJoints()
            => InScene().Select(go => go.GetComponent<ArticulatedJoint>()).Where(j => j != null).ToArray();

        static Rig[] SelectedRigs() => InScene().SelectMany(go => go.GetComponents<Rig>()).ToArray();
    }
}
