using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;
using M3trik.Unitytk.RenderEffects;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>highlight</c> ("Highlight the door"): each target glows while its step runs: pulsing where
    /// the task asks the user to act on it (a row that <see cref="TaskBehaviorAttribute.ActsOn"/> it:
    /// select, use, take, place), the cue to act; steady where it only ever points it out ("this is the
    /// lamp we test", then Next), so a part shown is never mistaken for one to select. One glow per part,
    /// so the task decides, not the step.
    /// </summary>
    [TaskBehavior("highlight",
                      About = "The object is highlighted while its step runs: pulsing where the user acts on it (as long as it runs, or once, by its settings), steady where they only look; its style and colour are its settings.",
                      Settings = new[] { nameof(TaskSettings.highlightStyle), nameof(TaskSettings.highlightColor), nameof(TaskSettings.highlightLoop),
                                         nameof(TaskSettings.cueWhen) })]
    internal sealed class HighlightBehavior : IManifestBehavior
    {
        /// <summary>Whether a step of <paramref name="plan"/> asks the user to act on the node at <paramref name="path"/> (a row targets it).</summary>
        /// <param name="plan">The plan (null: none does).</param>
        /// <param name="path">A node's path, as the plan names it.</param>
        internal static bool ActedOn(SheetPlan.Shape.Payload plan, string path)
        {
            if (plan == null || plan.steps == null) return false;
            foreach (var step in plan.steps)
                if (step.actions != null)
                    foreach (var each in step.actions)
                        if (each != null && TaskBehaviors.Traits(each.behavior).ActsOn && each.targets != null && each.targets.Contains(path)) return true;
            return false;
        }

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
                bool pulse = own.highlightStyle == HighlightStyle.Auto ? ActedOn(scene.Plan, path) : own.highlightStyle == HighlightStyle.Pulse;
                Highlight glow = RenderEffectsActions.AddHighlight(node, pulse, ctx: ctx);
                RenderEffectsActions.SetPulse(glow, pulse, ctx);
                RenderEffectsActions.SetColor(glow, own.highlightColor, ctx);
                RenderEffectsActions.SetLoop(glow, own.highlightLoop, ctx);
                Cues.During(scene, step, node, glow, new[] { node.transform }, own, ctx);
            }
        }
    }
}
