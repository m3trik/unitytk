using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>
    /// A test-only behavior, <c>test_marker</c>: a step's own row, read two ways (<see cref="ToolTestSettings.show"/>),
    /// its chip saying which where the row sets it. It builds nothing.
    /// </summary>
    [ManifestBehavior("test_marker", NamesObjects = false, Settings = new[] { nameof(ToolTestSettings.show) },
                      Suffix = new[] { nameof(ToolTestSettings.show) })]
    public sealed class TestMarkerBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            yield break;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            scene.Settings<ToolTestSettings>(step.id, "", action);  // what its settings say, read as a builder does
        }
    }
}
