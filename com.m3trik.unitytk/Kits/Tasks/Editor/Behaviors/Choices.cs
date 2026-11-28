using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    // What every builder of a choice shares (a button, a keyed-in answer): where choosing it goes, by the To cell's words.
    static class Choices
    {
        // Whether to is somewhere a choice goes: on (next), the task's end (exit), its start again (restart), or a step's ID.
        internal static bool Goes(string to) => !string.IsNullOrEmpty(to) && to != TaskKit.To.Retry;

        // What is wrong with to, a choice's: none written, or a step the plan lacks; null where nothing is.
        internal static string Problem(SheetPlan.Shape.Payload plan, string to)
        {
            if (string.IsNullOrEmpty(to))
                return $"it names nowhere to go: set its To ({TaskKit.To.Next}, a step's ID, {TaskKit.To.Exit}, {TaskKit.To.Restart} or {TaskKit.To.Retry})";
            if (to == TaskKit.To.Next || to == TaskKit.To.Exit || to == TaskKit.To.Restart || to == TaskKit.To.Retry) return null;
            return plan != null && plan.steps != null && !plan.steps.Exists(step => step.id == to)
                ? $"it goes to '{to}', no step of the task: name one of its steps" : null;
        }

        // Have step offer label, going where to says: on, to the task's end, starting over (the scene loaded again: nothing a take
        // did stands), or to a step. Retry goes nowhere: the step stays.
        internal static void Add(ManifestScene scene, SheetPlan.Shape.PlanStep step, string label, string to, OpContext ctx)
        {
            if (to == TaskKit.To.Exit) StepsActions.AddExit(scene.Sequence(), step.id, label, ctx);
            else if (to == TaskKit.To.Restart) StepsActions.AddRestart(scene.Sequence(), step.id, label, ctx);
            else if (Goes(to)) StepsActions.AddChoice(scene.Sequence(), step.id, label, to == TaskKit.To.Next ? null : to, ctx);
        }
    }
}
