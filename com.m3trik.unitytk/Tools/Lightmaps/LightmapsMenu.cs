using System;
using System.Linq;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Lightmaps;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Tools.Lightmaps.Tests")]

namespace M3trik.Unitytk.Tools.Lightmaps
{
    /// <summary>
    /// Tools ▸ unitytk ▸ Lightmaps: a line per menu item over the feature's actions
    /// (<see cref="LightmapsActions"/>), run as a person's menu runs one: one undo step,
    /// progress with Cancel, and its issues in the Console. Apply, whose clear of
    /// Unity's bake no undo brings back, is planned first and asks before it deletes.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>MenuItem</c>, the convention every Unity tool follows;
    /// Unity Search already finds each item, so there is no palette to build. The ask is
    /// Unity's own <c>EditorUtility.DisplayDialog</c>.
    /// </remarks>
    static class LightmapsMenu
    {
        const string ApplyPath = "Tools/unitytk/Lightmaps/Apply Scene Lightmaps";
        const string CapturePath = "Tools/unitytk/Lightmaps/Capture Scene Lightmaps";
        const string ValidatePath = "Tools/unitytk/Lightmaps/Validate Scene Lightmaps";
        const string ApplyTitle = "Apply Scene Lightmaps";

        /// <summary>How many of the plan's deletes the ask lists by name.</summary>
        const int Listed = 12;

        [MenuItem(ApplyPath)]
        static void Apply() => Apply((title, message) => EditorUtility.DisplayDialog(title, message, "Clear and Apply", "Cancel"));

        /// <summary>
        /// The menu's Apply: planned first (a dry run of <see cref="LightmapsActions.Apply"/>);
        /// where the plan deletes anything -- Unity's bake, which no undo brings back --
        /// those deletes are put to <paramref name="confirm"/> (a title and a message;
        /// true: go on), and declined, nothing changes. Then run as a menu runs an action.
        /// </summary>
        /// <returns>How many renderers the sets bind; 0 when declined.</returns>
        internal static int Apply(Func<string, string, bool> confirm)
        {
            var plan = new OpContext(dryRun: true);
            LightmapsActions.Apply(plan);
            var deletes = plan.Changes.Where(c => c.kind == "delete").ToList();
            if (deletes.Count > 0)
            {
                string listed = string.Join("\n", deletes.Take(Listed).Select(c => $"- {c.target} ({c.action})"));
                if (deletes.Count > Listed) listed += $"\n- and {deletes.Count - Listed} more";
                var probes = plan.Changes.FirstOrDefault(c => c.kind == "change" && c.target == "light probes");
                string message = $"Applying the scene lightmaps clears Unity's bake first, which deletes {deletes.Count} file(s):\n\n" +
                                 listed + (probes != null ? $"\n\nIt also clears {probes.action.Replace("clear ", "")}." : "") +
                                 "\n\nNo undo brings them back: re-bake (Generate Lighting) to return to Unity's bake.";
                if (!confirm(ApplyTitle, message))
                {
                    plan.Log($"{ApplyTitle}: cancelled; Unity's bake is left as it is.");
                    return 0;
                }
            }
            return OpContext.RunMenu(ApplyTitle, ctx => LightmapsActions.Apply(ctx));
        }

        [MenuItem(CapturePath)]
        static void Capture()
            => OpContext.RunMenu("Capture Scene Lightmaps", ctx =>
            {
                GameObject[] roots = InScene();
                int count = 0;
                foreach (GameObject root in roots) count += LightmapsActions.Capture(root, ctx);
                ctx.Log($"Capture Scene Lightmaps: {count} renderer(s) captured under the {roots.Length} selected object(s).");
            });

        [MenuItem(CapturePath, true)]
        static bool CanCapture() => InScene().Length > 0;

        // The selected objects a scene holds (a prefab asset lights no scene).
        static GameObject[] InScene() => Array.FindAll(Selection.gameObjects, go => go.scene.IsValid());

        [MenuItem(ValidatePath)]
        static void Validate()
            => OpContext.RunMenu("Validate Scene Lightmaps", ctx => ctx.Log(
                $"Validate Scene Lightmaps: {LightmapsActions.Validate(ctx)} problem(s) in the open scene(s)."));
    }
}
