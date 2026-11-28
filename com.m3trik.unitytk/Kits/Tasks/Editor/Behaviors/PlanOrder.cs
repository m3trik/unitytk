using M3trik.Unitytk.Manifests;
using System.Collections.Generic;

namespace M3trik.Unitytk.Kits.Tasks
{
    // A plan's actions in its order, around one of them: what a builder reads of the rest of the
    // task (the list an earlier row showed, the next row of its own word).
    static class PlanOrder
    {
        // Every (step, action) of the plan, in order.
        internal static IEnumerable<(SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction action)> All(SheetPlan.Shape.Payload plan)
        {
            if (plan == null || plan.steps == null) yield break;
            foreach (var step in plan.steps)
                if (step.actions != null)
                    foreach (var action in step.actions)
                        yield return (step, action);
        }

        // Every (step, action) of the plan with its row of assessment (Assess rows them in this order).
        internal static IEnumerable<(SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction action, ManifestAssessment.Row row)> Assessed(
            SheetPlan.Shape.Payload plan, ManifestAssessment assessment)
        {
            int row = 0;
            foreach (var (step, action) in All(plan)) yield return (step, action, assessment.rows[row++]);
        }

        // The step of plan holding action; null where none does (a probe's row).
        internal static SheetPlan.Shape.PlanStep StepOf(SheetPlan.Shape.Payload plan, SheetPlan.Shape.PlanAction action)
        {
            if (plan?.steps == null || action == null) return null;
            foreach (var step in plan.steps)
                if (step.actions != null && step.actions.Contains(action)) return step;
            return null;
        }

        // The plan's actions before action, latest first.
        internal static IEnumerable<(SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction action)> Before(SheetPlan.Shape.Payload plan, SheetPlan.Shape.PlanAction action)
        {
            var before = new List<(SheetPlan.Shape.PlanStep, SheetPlan.Shape.PlanAction)>();
            foreach (var each in All(plan))
            {
                if (each.action == action) break;
                before.Add(each);
            }
            before.Reverse();
            return before;
        }

        // The plan's actions from action on (itself first); none when the plan lacks it.
        internal static IEnumerable<(SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction action)> From(SheetPlan.Shape.Payload plan, SheetPlan.Shape.PlanAction action)
        {
            bool found = false;
            foreach (var each in All(plan))
            {
                found |= each.action == action;
                if (found) yield return each;
            }
        }
    }
}
