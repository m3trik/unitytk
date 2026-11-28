using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>play</c> ("A01_2 plays by itself after A01_1"): the row's clip plays when the take its
    /// <see cref="PlaySettings.after"/> names ends (empty: the take of the row before it in its step), at its speed; the step
    /// awaits it too.
    /// </summary>
    [TaskBehavior("play", Fields = new[] { "clip" }, NamesObjects = false,
                  RowSettings = new[] { nameof(PlaySettings.after), nameof(ClipSettings.speed), nameof(ClipSettings.start), nameof(ClipSettings.stop) },
                  About = "The row's take plays by itself when the take it follows ends, at its speed.")]
    internal sealed class PlayBehavior : IManifestBehavior, IManifestBound
    {
        /// <inheritdoc/>
        public IEnumerable<(string setting, UnityEngine.Object part, string property)> Bound(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step,
                                                                                            ManifestScene scene, string target)
            => Clips.Bound(action, scene, target, nameof(TaskSettings.play));

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (string.IsNullOrEmpty(action.clip)) yield return "it names no clip to play";
            SheetPlan.Shape.PlanStep step = PlanOrder.StepOf(scene.Plan, action);
            string after = scene.Settings<TaskSettings>(step?.id ?? "", "", action).play.after;
            if (!string.IsNullOrEmpty(after) && scene.Clip(after) == null) yield return $"the delivery carries no take '{after}' for it to play after";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            PlaySettings own = scene.Settings<TaskSettings>(step.id, "", action).play;
            string before = string.IsNullOrEmpty(own.after) ? null : own.after;
            if (before == null)
                foreach (var other in step.actions)
                {
                    if (other == action) break;
                    if (!string.IsNullOrEmpty(other.clip)) before = other.clip;
                }
            if (before == null)
            {
                ctx.Raise(TaskKit.FollowsNothingCode, $"{step.id}: '{action.text}' plays after no clip of its step: name the take it follows (after), " +
                                                      "or play it on a row that selects something");
                return;
            }
            Clips.Play(action, step, scene, own, ctx);
            Interactions.PlayAfter(scene.Root, action.clip, before, ctx);
        }
    }
}
