using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>quiz</c> ("Selecting 'Take Quiz' triggers advancement to the quiz"): the step offers the
    /// row's choice, a button on its page, going to the quiz: the first later step that asks a
    /// question (holds a row pairing with it, <see cref="TaskBehaviors.Pairs"/>: an <c>option</c>, a project's own).
    /// </summary>
    [TaskBehavior("quiz", Fields = new[] { "label" }, NamesObjects = false, Partner = "option", Chooses = true, Offers = true, EndsStep = true, About = "The step offers the row's choice, going to the quiz.")]
    internal sealed class QuizBehavior : IManifestBehavior
    {
        // The first step after action's that holds a row pairing with it (a question's); null when none does.
        static string Quiz(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            string own = null;
            foreach (var (step, later) in PlanOrder.From(scene.Plan, action))
            {
                own = own ?? step.id;
                if (step.id != own && TaskBehaviors.Pairs(later.behavior, action.behavior)) return step.id;
            }
            return null;
        }

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (string.IsNullOrEmpty(action.Field(TaskKit.Fields.Label))) yield return "it names no choice to offer";
            if (Quiz(action, scene) == null) yield return "no step after it asks a question: write the quiz's options";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
            => StepsActions.AddChoice(scene.Sequence(), step.id, action.Field(TaskKit.Fields.Label), Quiz(action, scene), ctx);
    }
}
