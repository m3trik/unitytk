using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>pause</c> ("Selecting 'Next' triggers step advancement"): the step waits on the row's button. As it
    /// finishes (<see cref="PauseWhen.Finish"/>, the usual): offered once the step's actions are done, going on to
    /// the next step. At its start (<see cref="PauseWhen.Start"/>): the button starts the step's actions, which come
    /// on only then (read first, then go), and the step goes on by itself once they are done. Every other step goes
    /// on by itself (<see cref="TaskBuild"/>); a pause is what makes one wait (<see cref="IStepGate"/>: the role a
    /// project's own gate takes too).
    /// </summary>
    [TaskBehavior("pause", Fields = new[] { "label" }, Settings = new[] { nameof(TaskSettings.pauseWhen) },
                      Suffix = new[] { nameof(TaskSettings.pauseWhen) }, NamesObjects = false, Chooses = true, Offers = true, EndsStep = true,
                      About = "The step waits on the row's button: as it finishes (offered once its actions are done), or at its start (the button starts them).")]
    internal sealed class PauseBehavior : IManifestBehavior, IStepGate
    {
        /// <inheritdoc/>
        public bool AtStart(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene)
            => scene.Settings<TaskSettings>(step.id, "", action).pauseWhen == PauseWhen.Start;

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (string.IsNullOrEmpty(action.Field(TaskKit.Fields.Label))) yield return "it names no button: set its Label";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            StepsActions.AddChoice(scene.Sequence(), step.id, action.Field(TaskKit.Fields.Label), null, ctx);
            StepsActions.SetStarts(scene.Sequence(), step.id, action.Field(TaskKit.Fields.Label), AtStart(action, step, scene), ctx);
        }
    }
}
