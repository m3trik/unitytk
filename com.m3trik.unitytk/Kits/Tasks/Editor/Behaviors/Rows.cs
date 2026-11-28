using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Kits.Tasks
{
    // What every builder's check shares: a row that does something to what it names, naming nothing.
    static class Rows
    {
        // "it names nothing to <verb>" where action names no target (verb: what its rows do to them; null: its
        // behavior's name in words, pick_up: pick up); nothing where it names one.
        internal static IEnumerable<string> NeedTargets(SheetPlan.Shape.PlanAction action, string verb = null)
        {
            if (action.targets != null && action.targets.Count > 0) yield break;
            yield return $"it names nothing to {verb ?? (action.behavior ?? "").Replace('_', ' ')}";
        }

        // Said where step's row asks something else of one of its targets than the scene's target will: a target asks for
        // one thing in hand (its row's With) in every step it is acted on (the last a row names), so a row that names
        // another, or none, is done only with that one in hand. Nothing where every row acting on it agrees.
        internal static void OneTool(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            if (action.targets == null) return;
            string own = action.Field(TaskKit.Fields.With) ?? "";
            foreach (string path in action.targets)
            {
                string asked = "", by = "";
                foreach (var (other, row) in PlanOrder.All(scene.Plan))
                {
                    string theirs = row.Field(TaskKit.Fields.With) ?? "";
                    if (theirs.Length == 0 || row.targets == null || !row.targets.Contains(path) || !TaskBehaviors.Traits(row, other).ActsOn) continue;
                    asked = theirs;
                    by = other.id;
                }
                if (asked.Length == 0 || asked == own) continue;
                ctx.Raise(TaskKit.OneToolCode, $"{step.id}: '{Leaf(path)}' asks for '{Leaf(asked)}' in hand in this step too, where its row names " +
                                                   $"{(own.Length == 0 ? "nothing" : $"'{Leaf(own)}'")}: {by} acts on it with that in hand, and a target asks for " +
                                                   "one thing in every step. Name another object in one of the two rows (a part of it, or a helper placed there)");
            }
        }

        static string Leaf(string path) => path.Substring(path.LastIndexOf('|') + 1);
    }
}
