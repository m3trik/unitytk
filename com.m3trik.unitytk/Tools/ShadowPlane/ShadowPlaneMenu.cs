using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.ShadowPlane;
using UnityEditor;
using UnityEngine;
using ShadowPlaneComponent = M3trik.Unitytk.ShadowPlane.ShadowPlane;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Tools.ShadowPlane.Tests")]

namespace M3trik.Unitytk.Tools.ShadowPlane
{
    /// <summary>
    /// Tools ▸ unitytk ▸ Shadow Plane: a line per menu item over the feature's actions
    /// (<see cref="ShadowPlaneActions"/>), run as a person's menu runs one: one undo step,
    /// progress with Cancel, and its issues in the Console. A bake that writes over a map
    /// already on disk, which no undo brings back, is planned first and asks before it
    /// overwrites.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>MenuItem</c>, the convention every Unity tool follows;
    /// Unity Search already finds each item, so there is no palette to build. The ask is
    /// Unity's own <c>EditorUtility.DisplayDialog</c>.
    /// </remarks>
    static class ShadowPlaneMenu
    {
        const string CreatePath = "Tools/unitytk/Shadow Plane/Create Shadow Plane";
        const string BakePath = "Tools/unitytk/Shadow Plane/Bake Horizon";
        const string RebakePath = "Tools/unitytk/Shadow Plane/Rebake Stale Planes";

        /// <summary>How many of the plan's overwrites the ask lists by name.</summary>
        const int Listed = 12;

        static bool Ask(string title, string message) => EditorUtility.DisplayDialog(title, message, "Overwrite", "Cancel");

        [MenuItem(CreatePath)]
        static void Create()
            => OpContext.RunMenu("Create Shadow Plane", ctx =>
            {
                GameObject[] targets = InScene();
                for (int i = 0; i < targets.Length; i++)
                {
                    ctx.Progress((double)i / targets.Length, $"'{targets[i].name}' ({i + 1} of {targets.Length})");
                    ShadowPlaneActions.Create(targets[i], null, ctx);
                }
            });

        [MenuItem(CreatePath, true)]
        static bool CanCreate() => InScene().Length > 0;

        [MenuItem(BakePath)]
        static void Bake() => Bake(SelectedPlanes(), Ask);

        [MenuItem(BakePath, true)]
        static bool CanBake() => SelectedPlanes().Length > 0;

        [MenuItem(RebakePath)]
        static void RebakeStale() => RebakeStale(Ask);

        /// <summary>
        /// The menu's Bake Horizon over <paramref name="planes"/>: planned first (a dry run
        /// of <see cref="ShadowPlaneActions.BakeHorizon"/>, forced); where the plan writes over
        /// a file already there, those files are put to <paramref name="confirm"/> (a title
        /// and a message; true: go on), and declined, nothing changes. Then run as a menu runs
        /// an action.
        /// </summary>
        /// <returns>How many planes it baked; 0 when declined.</returns>
        internal static int Bake(ShadowPlaneComponent[] planes, Func<string, string, bool> confirm)
        {
            const string title = "Bake Horizon";
            var plan = new OpContext(dryRun: true);
            foreach (ShadowPlaneComponent plane in planes) ShadowPlaneActions.BakeHorizon(plane, force: true, ctx: plan);
            if (!Confirmed(title, plan, confirm)) return 0;
            return OpContext.RunMenu(title, ctx =>
            {
                int baked = 0;
                for (int i = 0; i < planes.Length; i++)
                {
                    ctx.Progress((double)i / planes.Length, $"'{planes[i].name}' ({i + 1} of {planes.Length})");
                    if (ShadowPlaneActions.BakeHorizon(planes[i], force: true, ctx: ctx)) baked++;
                }
                return baked;
            });
        }

        /// <summary>
        /// The menu's Rebake Stale Planes: planned first (a dry run of
        /// <see cref="ShadowPlaneActions.RebakeStale"/>), its overwrites put to
        /// <paramref name="confirm"/> as <see cref="Bake"/>'s are, then run.
        /// </summary>
        /// <returns>How many planes it rebaked; 0 when declined.</returns>
        internal static int RebakeStale(Func<string, string, bool> confirm)
        {
            const string title = "Rebake Stale Planes";
            var plan = new OpContext(dryRun: true);
            ShadowPlaneActions.RebakeStale(plan);
            if (!Confirmed(title, plan, confirm)) return 0;
            return OpContext.RunMenu(title, ctx => ShadowPlaneActions.RebakeStale(ctx));
        }

        // Whether the plan's overwrites, if any, were confirmed. A plane whose map another
        // plane holds too (a duplicate's) is never written over: it gets a new file, which
        // the ask names beside the overwrites (and asks nothing of alone).
        static bool Confirmed(string title, OpContext plan, Func<string, string, bool> confirm)
        {
            var overwrites = plan.Changes.Where(c => c.kind == "write" && File.Exists(c.target)).ToList();
            var forks = plan.Changes.Where(c => c.kind == "write" && !File.Exists(c.target)
                                                && c.action.Contains(ShadowPlaneActions.NewFileNote)).ToList();
            if (overwrites.Count == 0) return true;
            string message = $"{title} writes over {overwrites.Count} horizon map(s) already on disk:\n\n{List(overwrites)}\n\n" +
                             "No undo brings the old maps back.";
            if (forks.Count > 0)
                message += $"\n\n{forks.Count} plane(s) get a new map, since another plane holds theirs too (that map is " +
                           $"left as it is):\n\n{List(forks)}";
            if (confirm(title, message)) return true;
            plan.Log($"{title}: cancelled; the maps are left as they are.");
            return false;
        }

        static string List(List<Change> changes)
        {
            string listed = string.Join("\n", changes.Take(Listed).Select(c => $"- {c.target} ({c.action})"));
            return changes.Count > Listed ? listed + $"\n- and {changes.Count - Listed} more" : listed;
        }

        // The selected objects a scene holds (a prefab asset casts no shadow in a scene).
        static GameObject[] InScene() => Array.FindAll(Selection.gameObjects, go => go.scene.IsValid());

        static ShadowPlaneComponent[] SelectedPlanes()
            => InScene().Select(go => go.GetComponent<ShadowPlaneComponent>()).Where(p => p != null).ToArray();
    }
}
