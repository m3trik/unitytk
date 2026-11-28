using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.RenderEffects;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>show</c> ("The lid appears"): each target, hidden until then, comes in as its step starts and stays, over its
    /// <see cref="ShowSettings.fade"/> (0: at once).
    /// </summary>
    [TaskBehavior("show", About = "The object, hidden until then, appears as its step starts and stays (its fade: how long it takes).")]
    internal sealed class ShowBehavior : IManifestBehavior
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
                float seconds = scene.Settings<TaskSettings>(step.id, path, action).show.fade;
                FadeTo fade = RenderEffectsActions.AddFadeTo(node, 1f, seconds, ctx: ctx);
                RenderEffectsActions.SetSeconds(fade, seconds, ctx);
                scene.EnableDuring(step.id, node, fade, ctx);
            }
        }
    }
}
