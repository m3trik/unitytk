using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>hold</c> ("The wrench returns to hand"): each target made holdable. A clip lends
    /// what it moves and gives it back to the hand at its end (<see cref="ClipPlayer"/>), so
    /// the row asks for nothing more.
    /// </summary>
    [TaskBehavior("hold", Holds = true, About = "The object can be held: a clip that moves it gives it back to the hand.")]
    internal sealed class HoldBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
            => Rows.NeedTargets(action);

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            foreach (string path in action.targets)
                scene.MakeHoldable(scene.Node(path).gameObject, ctx);
        }
    }
}
