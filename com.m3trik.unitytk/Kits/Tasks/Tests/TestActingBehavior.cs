using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// A project's own behavior whose rows act on their targets, as a project declares one
    /// (<see cref="TaskBehaviorAttribute.ActsOn"/>), and say their label on their step's page
    /// (<see cref="ITaskPageContent"/>): only the tests register it, and it builds nothing.
    /// </summary>
    [TaskBehavior("test_acting", ActsOn = true)]
    public sealed class TestActingBehavior : IManifestBehavior, ITaskPageContent
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
        {
            if (!string.IsNullOrEmpty(action.Field("label"))) page.Text = action.Field("label");
        }
    }
}
