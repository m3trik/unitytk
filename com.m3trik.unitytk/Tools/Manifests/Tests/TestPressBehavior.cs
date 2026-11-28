using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>
    /// A test-only behavior, <c>test_press</c>: pressing its object plays the row's take, read several ways (its kit's
    /// settings group named as it, <see cref="ToolTestSettings.test_press"/>: its rows' <c>way</c> is
    /// <c>test_press.way</c>), its chip saying which; its row's <c>with</c> names a node (what the hand holds), so an
    /// assessment finds it. It builds nothing.
    /// </summary>
    [ManifestBehavior("test_press", Fields = new[] { "clip", "with" }, NodeFields = new[] { "with" }, FieldLabels = new[] { "with=In hand" },
                      Suffix = new[] { nameof(TestPressSettings.way) })]
    public sealed class TestPressBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            yield break;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            foreach (string target in action.targets ?? new List<string>())
                scene.Settings<ToolTestSettings>(step.id, target, action);
        }
    }
}
