using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>
    /// Test-only behaviors, <c>test_swing_open</c> and <c>test_swing_shut</c>: one family, <c>test_swing</c>, read
    /// two ways (<see cref="ManifestBehaviorAttribute.Family"/>), as the Tasks kit's fades are. They build nothing.
    /// </summary>
    [ManifestBehavior("test_swing_open", Family = "test_swing", Variant = "open")]
    [ManifestBehavior("test_swing_shut", Family = "test_swing", Variant = "shut")]
    public sealed class TestSwingBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public System.Collections.Generic.IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            yield break;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
        }
    }
}
