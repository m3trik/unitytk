using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>take</c> ("Selecting the key places it in hand"): each target made holdable and a
    /// target taken into the selecting hand when selected, selectable while its step runs; the
    /// step waits for it (a Next it offers comes once the tool is in hand).
    /// </summary>
    [TaskBehavior("take", ActsOn = true, Holds = true, About = "Selecting the object takes it into the hand.")]
    internal sealed class TakeBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
            => Rows.NeedTargets(action);

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            Rows.OneTool(action, step, scene, ctx);
            foreach (string path in action.targets)
            {
                var node = scene.Node(path).gameObject;
                scene.MakeHoldable(node, ctx);
                Interactions.MakeTarget(node, null, ctx);
                Interactions.TakeOnSelect(node, ctx);
                Target target = node.GetComponent<Target>();
                scene.EnableDuring(step.id, node, target, ctx);
                StepsActions.SignalOn(node, target, target == null ? null : target.onSelected, scene.Sequence(), TaskKit.SelectedSignal(scene, node), ctx);
                StepsActions.Await(scene.Sequence(), step.id, TaskKit.SelectedSignal(scene, node), ctx);
            }
        }
    }
}
