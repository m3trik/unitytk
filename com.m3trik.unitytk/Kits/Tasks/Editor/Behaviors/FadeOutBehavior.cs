using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;
using M3trik.Unitytk.RenderEffects;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>fade_out</c> ("The bolt fades away"): each target fades out as its step ends. One its step also
    /// sends back where it rests (a row that <see cref="TaskBehaviorAttribute.Returns"/> it: "The key fades away
    /// from hand", then "Return the key to the table") goes back shown, the return being how it leaves the hand,
    /// unless the task fades it too (<see cref="TaskSettings.fadeReturned"/>); a note says which
    /// (<see cref="TaskKit.FadeReturnedCode"/>).
    /// </summary>
    [TaskBehavior("fade_out", About = "The object fades away as its step ends (one its step returns goes back shown, by its settings).",
                      Settings = new[] { nameof(TaskSettings.fadeSeconds), nameof(TaskSettings.fadeReturned) }, Family = "fade", Variant = "out")]
    internal sealed class FadeOutBehavior : IManifestBehavior
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
                if (!scene.Settings<TaskSettings>(step.id, path, action).fadeReturned && Returned(step, path))
                {
                    ctx.Raise(TaskKit.FadeReturnedCode, $"{step.id}: '{node.name}' goes back shown where it rests, not faded: its step returns it " +
                                        "(Fade Returned, in Fade Out's settings, fades it as the sheet says, and it then rests hidden)");
                    continue;
                }
                float seconds = scene.Settings<TaskSettings>(step.id, path, action).fadeSeconds;
                FadeTo fade = RenderEffectsActions.AddFadeTo(node, 0f, seconds, ctx: ctx);
                RenderEffectsActions.SetSeconds(fade, seconds, ctx);
                scene.EnableAtEnd(step.id, node, fade, ctx);
            }
        }

        // Whether a row of step sends path back where it rests.
        static bool Returned(SheetPlan.Shape.PlanStep step, string path)
        {
            if (step.actions == null) return false;
            foreach (var other in step.actions)
                if (TaskBehaviors.Traits(other.behavior).Returns && other.targets != null && other.targets.Contains(path)) return true;
            return false;
        }
    }
}
