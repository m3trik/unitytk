using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>select</c> ("Selecting the button triggers step advancement"): each target a target,
    /// selectable while its step runs, whose selection completes the step, which then goes on (as every
    /// step that offers no choice does, <see cref="TaskBuild"/>): it signals the sequence as it is selected
    /// (<see cref="TaskKit.SelectedSignal"/>), and its step awaits that.
    /// </summary>
    [TaskBehavior("select", ActsOn = true, About = "Selecting the object completes its step.")]
    internal sealed class SelectBehavior : IManifestBehavior
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
                Target target = Interactions.MakeTarget(node, null, ctx);
                scene.EnableDuring(step.id, node, target, ctx);
                StepsActions.SignalOn(node, target, target == null ? null : target.onSelected, scene.Sequence(), TaskKit.SelectedSignal(scene, node), ctx);
                StepsActions.Await(scene.Sequence(), step.id, TaskKit.SelectedSignal(scene, node), ctx);
            }
        }
    }
}
