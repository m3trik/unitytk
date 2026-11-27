using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>number_pad</c> ("A number pad appears on the placard"): the step's page has a key pad of
    /// the task's <see cref="TaskSettings.numberKeys"/> whose Enter chooses what was keyed in, which the step's
    /// <c>answer</c> and <c>answer_else</c> rows send somewhere. Its keys are the task's, one pad for every step: no row's.
    /// </summary>
    [TaskBehavior("number_pad", NamesObjects = false, Chooses = true,
                      About = "The step's page has a key pad; Enter chooses what was keyed in.")]
    internal sealed class NumberPadBehavior : IManifestBehavior, ITaskPageContent
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            yield break;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
            => page.Keys = true;
    }
}
