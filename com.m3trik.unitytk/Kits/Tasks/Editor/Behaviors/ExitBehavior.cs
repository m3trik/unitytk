using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary><c>exit</c> ("Selecting 'Exit' ends the task"): the step offers the row's choice, which finishes the sequence.</summary>
    [TaskBehavior("exit", Fields = new[] { "label" }, NamesObjects = false, Chooses = true, Offers = true, EndsStep = true, About = "The step offers the row's choice, which finishes the task.")]
    internal sealed class ExitBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (string.IsNullOrEmpty(action.Field(TaskKit.Fields.Label))) yield return "it names no choice to offer";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
            => StepsActions.AddExit(scene.Sequence(), step.id, action.Field(TaskKit.Fields.Label), ctx);
    }
}
