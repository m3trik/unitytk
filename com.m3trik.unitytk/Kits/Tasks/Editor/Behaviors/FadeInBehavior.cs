using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;
using M3trik.Unitytk.RenderEffects;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>fade_in</c> ("The marker fades in"), and <c>show</c> ("Highlighted lid appears in place", its template's
    /// <c>also</c> highlighting it besides), one builder registered under both names: each target, hidden until
    /// then, fades in as its step starts and stays.
    /// </summary>
    [TaskBehavior("fade_in", About = "The object, hidden until then, fades in as its step starts.", Family = "fade", Variant = "in",
                  Settings = new[] { nameof(TaskSettings.fadeSeconds) })]
    [TaskBehavior("show", About = "The object, hidden until then, appears as its step starts and stays.",
                  Settings = new[] { nameof(TaskSettings.fadeSeconds) })]
    internal sealed class FadeInBehavior : IManifestBehavior
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
                float seconds = scene.Settings<TaskSettings>(step.id, path, action).fadeSeconds;
                FadeTo fade = RenderEffectsActions.AddFadeTo(node, 1f, seconds, ctx: ctx);
                RenderEffectsActions.SetSeconds(fade, seconds, ctx);
                scene.EnableDuring(step.id, node, fade, ctx);
            }
        }
    }
}
