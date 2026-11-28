using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>answer</c> ("Entering '1337' goes to step B04"): the step's page has the key pad (the task's
    /// <see cref="TaskSettings.numberKeys"/>, one pad for every step), whose Enter chooses what was keyed in; the row's
    /// Label keyed in goes where its To says (<see cref="Choices"/>), and a Label of <c>*</c> takes any value no other answer
    /// row of the step names ("Entering anything else goes to step B03A").
    /// </summary>
    [TaskBehavior("answer", Fields = new[] { TaskKit.Fields.Label, "to" }, NamesObjects = false, Chooses = true,
                  About = "The step's page has the key pad; the row's value keyed in (or, Label *, any other) goes where its To says.")]
    internal sealed class AnswerBehavior : IManifestBehavior, ITaskPageContent
    {
        // The Label that takes any value no other answer of its step names.
        internal const string Otherwise = "*";

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            string label = action.Field(TaskKit.Fields.Label);
            if (string.IsNullOrEmpty(label)) yield return $"it names no value to enter: set its Label (or {Otherwise}: any other)";
            string where = Choices.Problem(scene.Plan, action.to);
            if (where != null) yield return where;
            else if (label == Otherwise && (action.to == TaskKit.To.Exit || action.to == TaskKit.To.Restart))
                yield return $"any other value goes on or to a step: set its To to {TaskKit.To.Next} or a step's ID";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string label = action.Field(TaskKit.Fields.Label);
            if (label != Otherwise) Choices.Add(scene, step, label, action.to, ctx);
            else if (Choices.Goes(action.to)) StepsActions.AddOtherwise(scene.Sequence(), step.id, action.to == TaskKit.To.Next ? null : action.to, ctx);
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
            => page.Keys = true;  // every answer puts the key pad on its page
    }
}
