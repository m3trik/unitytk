using M3trik.Unitytk.Manifests;
using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>option</c> (a question's answer row; the right one written with the sheet's correct
    /// mark): a button on the step's page (<see cref="TaskPage"/>). The right answer goes
    /// on to the next step; a wrong one says the row's <see cref="TaskSettings.wrongChoiceReply"/> and the page
    /// stays. A row of the step that <see cref="TaskBehaviorAttribute.EndsStep"/> offering the same label
    /// (<c>goto</c>, <c>exit</c>) says what it does instead.
    /// </summary>
    [TaskBehavior("option", Fields = new[] { "label", "value" }, NamesObjects = false, Chooses = true, Offers = true,
                      About = "An answer button on the step's page: the right one goes on, a wrong one says so and stays.",
                      Settings = new[] { nameof(TaskSettings.wrongChoiceReply) })]
    internal sealed class OptionBehavior : IManifestBehavior, ITaskPageContent
    {
        /// <summary>Whether another row of <paramref name="step"/> says what choosing <paramref name="label"/> does.</summary>
        internal static bool Offered(SheetPlan.Shape.PlanStep step, string label)
        {
            foreach (var other in step.actions)
                if (TaskBehaviors.Traits(other.behavior).EndsStep && string.Equals(other.Field(TaskKit.Fields.Label), label, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (string.IsNullOrEmpty(action.Field(TaskKit.Fields.Label))) yield return "it names no answer";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            if (action.Field(TaskKit.Fields.Value) == TaskKit.Correct && !Offered(step, action.Field(TaskKit.Fields.Label)))
                StepsActions.AddChoice(scene.Sequence(), step.id, action.Field(TaskKit.Fields.Label), null, ctx);
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
        {
            if (action.Field(TaskKit.Fields.Value) != TaskKit.Correct && !Offered(step, action.Field(TaskKit.Fields.Label))) page.Reply(action.Field(TaskKit.Fields.Label), scene.Settings<TaskSettings>(step.id, "", action).wrongChoiceReply);
        }
    }
}
