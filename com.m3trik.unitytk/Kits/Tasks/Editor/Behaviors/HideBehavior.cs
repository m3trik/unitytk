using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.RenderEffects;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>hide</c> ("The bolt disappears"): each target goes as its step ends, over its <see cref="HideSettings.fade"/> (0: at
    /// once). One its step also sends back where it rests (a row that <see cref="TaskBehaviorAttribute.Returns"/> it: hidden
    /// from the hand, then returned) goes back shown, the return being how it leaves the hand, unless its settings hide it too
    /// (<see cref="HideSettings.returned"/>); a note says which (<see cref="TaskKit.FadeReturnedCode"/>).
    /// </summary>
    [TaskBehavior("hide", About = "The object goes as its step ends (its fade: how long it takes; one its step returns goes back shown, by its settings).")]
    internal sealed class HideBehavior : IManifestBehavior
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
                HideSettings own = scene.Settings<TaskSettings>(step.id, path, action).hide;
                if (!own.returned && Returned(step, path))
                {
                    ctx.Raise(TaskKit.FadeReturnedCode, $"{step.id}: '{node.name}' goes back shown where it rests, not hidden: its step returns it " +
                                                        "(Returned, in Hide's settings, hides it as the sheet says, and it then rests hidden)");
                    continue;
                }
                FadeTo fade = RenderEffectsActions.AddFadeTo(node, 0f, own.fade, ctx: ctx);
                RenderEffectsActions.SetSeconds(fade, own.fade, ctx);
                scene.EnableAtEnd(step.id, node, fade, ctx);
            }
        }

        // Whether a row of step sends path back where it rests.
        static bool Returned(SheetPlan.Shape.PlanStep step, string path)
        {
            if (step.actions == null) return false;
            foreach (var other in step.actions)
                if (TaskBehaviors.Traits(other, step).Returns && other.targets != null && other.targets.Contains(path)) return true;
            return false;
        }
    }
}
