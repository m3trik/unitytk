using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;
using M3trik.Unitytk.RenderEffects;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>turn_on</c> ("The lamp turns on"): each target glows steadily while its step runs, in the colour its settings give
    /// it (<see cref="TaskSettings.highlightColor"/>, the sheet's <c>color</c>: one name and one meaning in every behavior
    /// that glows). A steady glow swings never, so a highlight's pulsing settings are no setting of it.
    /// </summary>
    [TaskBehavior("turn_on", About = "The object glows steadily while its step runs, in its colour.",
                  Settings = new[] { nameof(TaskSettings.highlightColor) })]
    internal sealed class TurnOnBehavior : IManifestBehavior
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
                TaskSettings own = scene.Settings<TaskSettings>(step.id, path, action);
                Highlight glow = RenderEffectsActions.AddHighlight(node, false, ctx: ctx);
                RenderEffectsActions.SetColor(glow, own.highlightColor, ctx);
                scene.EnableDuring(step.id, node, glow, ctx);
            }
        }
    }
}
