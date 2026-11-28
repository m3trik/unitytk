using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using UnityEngine;
using PlanAction = M3trik.Unitytk.Manifests.SheetPlan.Shape.PlanAction;
using PlanStep = M3trik.Unitytk.Manifests.SheetPlan.Shape.PlanStep;

namespace M3trik.Unitytk.Kits.Tasks
{
    // What every builder of a shown thing shares (a picture, a list, a travel's ring): its span, how long it lasts, carried
    // by the row that shows it, so no second row takes it away. `step`: while its own step runs; a step's ID: from its step
    // until that one starts; empty: from its step on.
    static class Spans
    {
        // The span that says a shown thing lasts while its own step runs.
        internal const string Step = "step";

        // What is wrong with span (the setting what of a row of step): a step's ID the plan lacks, or one no later than step;
        // null where nothing is.
        internal static string Problem(SheetPlan.Shape.Payload plan, PlanStep step, string span, string what)
        {
            if (string.IsNullOrEmpty(span) || span == Step || plan == null || plan.steps == null) return null;
            int until = plan.steps.FindIndex(each => each.id == span), from = step == null ? -1 : plan.steps.IndexOf(step);
            if (until < 0) return $"its {what} '{span}' is no step of the task: name a later step, or {Step} (while its own step runs)";
            if (from >= 0 && until <= from) return $"its {what} '{span}' is no later step than {step.id}: name a later one, or {Step} (while its own step runs)";
            return null;
        }

        // The step of the next row of action's word after action's step (the shown thing it replaces); null where none comes.
        internal static string Next(ManifestScene scene, PlanStep step, PlanAction action)
        {
            foreach (var (each, later) in PlanOrder.From(scene.Plan, action))
                if (each != step && later.behavior == action.behavior) return each.id;
            return null;
        }

        // Show part (on node) from step's start for span: while step runs (Step), else from its start until the step span
        // names starts (empty: on from then). A span ending at a step this run leaves as it stands switches nothing there.
        internal static void Show<T>(ManifestScene scene, PlanStep step, string span, GameObject node, T part, OpContext ctx) where T : Behaviour
        {
            if (span == Step)
            {
                scene.EnableDuring(step.id, node, part, ctx);
                return;
            }
            scene.EnableFrom(step.id, node, part, ctx);
            if (!string.IsNullOrEmpty(span) && scene.Builds(span)) scene.DisableFrom(span, node, part, ctx);
        }

        // Show for a part on node, an object a dry run only planned.
        internal static void Show<T>(ManifestScene scene, PlanStep step, string span, PlannedObject node, OpContext ctx) where T : Behaviour
        {
            if (span == Step)
            {
                scene.EnableDuring<T>(step.id, node, ctx);
                return;
            }
            scene.EnableFrom<T>(step.id, node, ctx);
            if (!string.IsNullOrEmpty(span) && scene.Builds(span)) scene.DisableFrom<T>(span, node, ctx);
        }
    }
}
