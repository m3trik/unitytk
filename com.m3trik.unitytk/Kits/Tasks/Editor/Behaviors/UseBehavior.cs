using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>use</c> ("Selecting the bolt with wrench in hand triggers Bolt Removal Animation"): each target
    /// a target requiring the action's tool in hand (made holdable) where it names one, playing the action's
    /// clip when selected, selectable while its step runs; the step awaits the clip's end. A tool the clip
    /// moves is lent to it and comes back to the hand after; one it does not move goes to the part the clip
    /// moves and works there while it plays (a wrench on the bolt it turns out). How it plays is the object's
    /// settings' (<see cref="TaskSettings.useTrigger"/>): a click, held to play, or a timed press.
    /// </summary>
    [TaskBehavior("use", ActsOn = true,
                      About = "Selecting the object, with the row's tool in hand, plays its take: a click, or by its settings held to play or a timed press.",
                      Settings = new[] { nameof(TaskSettings.useTrigger), nameof(TaskSettings.pressSeconds), nameof(TaskSettings.releaseRewinds),
                                         nameof(TaskSettings.refusedReply), nameof(TaskSettings.playSpeed), nameof(TaskSettings.playFrom),
                                         nameof(TaskSettings.playUntil) },
                      RowSettings = new[] { nameof(TaskSettings.playSpeed), nameof(TaskSettings.playFrom), nameof(TaskSettings.playUntil) },
                      Suffix = new[] { nameof(TaskSettings.useTrigger) },
                      Fields = new[] { "clip", "tool" })]
    internal sealed class UseBehavior : IManifestBehavior, IManifestBound, ITaskPageContent
    {
        /// <inheritdoc/>
        public IEnumerable<(string setting, UnityEngine.Object part, string property)> Bound(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step,
                                                                                            ManifestScene scene, string target)
            => Clips.Bound(action, scene, target);

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            foreach (string problem in Rows.NeedTargets(action, "select")) yield return problem;
            if (string.IsNullOrEmpty(action.clip)) yield return "it names no clip to play";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string named = action.Field(TaskKit.Fields.Tool);
            GameObject tool = string.IsNullOrEmpty(named) ? null : scene.Node(named).gameObject;
            if (tool != null) scene.MakeHoldable(tool, ctx);
            Rows.OneTool(action, step, scene, ctx);
            Clips.Play(action, step, scene, ctx);
            foreach (string path in action.targets)
            {
                var node = scene.Node(path).gameObject;
                Interactions.MakeTarget(node, tool, ctx);
                Refusals.Say(scene, step, action, path, node, tool, ctx);
                TaskSettings own = scene.Settings<TaskSettings>(step.id, path, action);
                Clips.Trigger(node, action.clip, step, scene, tool, own.useTrigger, own, ctx);
                scene.EnableDuring(step.id, node, node.GetComponent<Target>(), ctx);
            }
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
        {
            if (Refusals.Says(scene, step, action)) page.OnPlacard = true;  // a refusal is said on the placard
        }
    }
}
