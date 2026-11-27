using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Tools.Manifests.Tests
{
    /// <summary>
    /// A test-only behavior, <c>test_choice</c>: a step's own row offering a choice that goes to a step. It builds
    /// nothing; it says how its fields are labelled and what its chip tells two of its rows apart by.
    /// </summary>
    [ManifestBehavior("test_choice", NamesObjects = false, Fields = new[] { "label", "to" }, FieldLabels = new[] { "label=Choice" },
                      ChipFields = new[] { "'{label}'", "→ {to}" }, About = "The step offers the row's choice.")]
    public sealed class TestChoiceBehavior : IManifestBehavior
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
