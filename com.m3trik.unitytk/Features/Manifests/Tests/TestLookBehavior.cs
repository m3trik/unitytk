using System.Collections.Generic;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Manifests.Tests
{
    /// <summary>
    /// A test-only behavior, <c>test_look</c>, whose settings are its kit's group named as it (<see cref="TestLookSettings"/>):
    /// it names none of its own, so they are each field of that group, by its path. It builds nothing.
    /// </summary>
    [ManifestBehavior("test_look", Suffix = new[] { nameof(TestLookSettings.solid) }, RowSettings = new[] { nameof(TestLookSettings.size) })]
    public sealed class TestLookBehavior : IManifestBehavior
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
    }
}
