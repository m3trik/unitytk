using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks.Tests
{
    /// <summary>
    /// A project's own gate (<see cref="IStepGate"/>): its step waits on its button, which, as it finishes, is offered
    /// once the step's actions are done. Only the tests register it.
    /// </summary>
    [TaskBehavior("test_gate", NamesObjects = false, Offers = true, Chooses = true, EndsStep = true, Fields = new[] { "label" })]
    public sealed class TestGateBehavior : IManifestBehavior, IStepGate
    {
        /// <inheritdoc/>
        public bool AtStart(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene) => false;

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            yield break;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
            => StepsActions.AddChoice(scene.Sequence(), step.id, action.Field(TaskKit.Fields.Label), null, ctx);
    }
}
