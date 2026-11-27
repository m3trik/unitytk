using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>return</c> ("Return the key to the table"): each target made holdable and sent back
    /// where it rests as its step ends (<see cref="TaskBehaviorAttribute.Returns"/>).
    /// </summary>
    [TaskBehavior("return", Returns = true, About = "The held object goes back where it rests as its step ends.")]
    internal sealed class ReturnBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
            => Rows.NeedTargets(action);

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            foreach (string path in action.targets)
            {
                var node = scene.Node(path).gameObject;
                scene.MakeHoldable(node, ctx);
                SendHome send = Interactions.AddSendHome(node, ctx: ctx);
                scene.EnableAtEnd(step.id, node, send, ctx);
            }
        }
    }
}
