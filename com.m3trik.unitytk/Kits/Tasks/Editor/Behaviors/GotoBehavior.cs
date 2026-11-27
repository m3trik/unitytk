using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>goto</c> ("Selecting 'YES' triggers step advancement to step B03.5"): the step offers the row's
    /// choice, going to its step; one whose jump the plan says restarts (<c>PlanAction.restart</c>: its reading's
    /// rule for a jump to the first step) starts over (<see cref="StepsActions.AddRestart"/>: the scene loaded again).
    /// </summary>
    [TaskBehavior("goto", Fields = new[] { "to", "label" }, NamesObjects = false, Chooses = true, Offers = true, EndsStep = true, About = "The step offers the row's choice, going to the step it names.")]
    internal sealed class GotoBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (string.IsNullOrEmpty(action.Field(TaskKit.Fields.Label))) yield return "it names no choice to offer";
            if (string.IsNullOrEmpty(action.to)) yield return "it names no step to go to";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            if (action.restart == true)
                StepsActions.AddRestart(scene.Sequence(), step.id, action.Field(TaskKit.Fields.Label), ctx);
            else
                StepsActions.AddChoice(scene.Sequence(), step.id, action.Field(TaskKit.Fields.Label), action.to, ctx);
        }
    }
}
