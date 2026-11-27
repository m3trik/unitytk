using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>play</c> ("Trigger Lid Open Animation"): the row's clip plays when the clip
    /// of the row before it in its step ends; the step awaits it too.
    /// </summary>
    [TaskBehavior("play", Fields = new[] { "clip" }, NamesObjects = false, About = "The row's take plays when the take before it in its step ends, at its speed.",
                  Settings = new[] { nameof(TaskSettings.playSpeed), nameof(TaskSettings.playFrom), nameof(TaskSettings.playUntil) },
                  RowSettings = new[] { nameof(TaskSettings.playSpeed), nameof(TaskSettings.playFrom), nameof(TaskSettings.playUntil) })]
    internal sealed class PlayBehavior : IManifestBehavior, IManifestBound
    {
        /// <inheritdoc/>
        public IEnumerable<(string setting, UnityEngine.Object part, string property)> Bound(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step,
                                                                                            ManifestScene scene, string target)
            => Clips.Bound(action, scene, target);

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (string.IsNullOrEmpty(action.clip)) yield return "it names no clip to play";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string before = null;
            foreach (var other in step.actions)
            {
                if (other == action) break;
                if (!string.IsNullOrEmpty(other.clip)) before = other.clip;
            }
            if (before == null)
            {
                ctx.Raise(TaskKit.FollowsNothingCode, $"{step.id}: '{action.text}' plays after no clip of its step: play it on a row that selects something");
                return;
            }
            Clips.Play(action, step, scene, ctx);
            Interactions.PlayAfter(scene.Root, action.clip, before, ctx);
        }
    }
}
