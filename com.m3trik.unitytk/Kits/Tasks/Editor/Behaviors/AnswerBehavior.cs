using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>answer</c> ("Entering '1337' triggers step B04"): the value keyed in on the step's key pad goes to the row's
    /// step; or, by its setting (<see cref="AnswerKind.Otherwise"/>: "Entering any other number triggers step B03.5"),
    /// any value no other answer row of the step names.
    /// </summary>
    [TaskBehavior("answer", Fields = new[] { "value", "to" }, Settings = new[] { nameof(TaskSettings.answerKind) },
                      Suffix = new[] { nameof(TaskSettings.answerKind) }, NamesObjects = false, Chooses = true,
                      About = "The value keyed in on the step's key pad (or, by its setting, any other) goes to the row's step.")]
    internal sealed class AnswerBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            SheetPlan.Shape.PlanStep step = PlanOrder.StepOf(scene.Plan, action);
            if (step == null || !Otherwise(action, step, scene))
                if (string.IsNullOrEmpty(action.Field(TaskKit.Fields.Value))) yield return "it names no value to enter";
            if (string.IsNullOrEmpty(action.to)) yield return "it names no step to go to";
        }

        // Whether action, an answer of step, takes any value no other answer names.
        static bool Otherwise(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene)
            => scene.Settings<TaskSettings>(step.id, "", action).answerKind == AnswerKind.Otherwise;

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            if (Otherwise(action, step, scene)) StepsActions.AddOtherwise(scene.Sequence(), step.id, action.to, ctx);
            else StepsActions.AddChoice(scene.Sequence(), step.id, action.Field(TaskKit.Fields.Value), action.to, ctx);
        }
    }
}
