using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Steps;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>place</c> ("Moving the lid close to its slot triggers Lid Install Animation"): each target a
    /// target requiring what the hand holds (the row's tool), playing
    /// the action's clip when selected, as <c>use</c> builds, and selected by itself once the held
    /// thing comes within reach of it (<see cref="SelectNear"/>: a controller's hand moving it there);
    /// a pointer's click with it in hand does the same. The step awaits the clip's end, and what the
    /// clip moved into place stays there, out of the hand (<see cref="TaskSettings.placedStays"/>; off:
    /// it comes back to the hand, as a tool does).
    /// </summary>
    [TaskBehavior("place", ActsOn = true,
                      About = "Bringing the held thing to the object, or selecting it, plays the row's take, which leaves it in place there.",
                      Settings = new[] { nameof(TaskSettings.refusedReply), nameof(TaskSettings.placedStays), nameof(TaskSettings.playSpeed),
                                         nameof(TaskSettings.playFrom), nameof(TaskSettings.playUntil) },
                      RowSettings = new[] { nameof(TaskSettings.playSpeed), nameof(TaskSettings.playFrom), nameof(TaskSettings.playUntil) },
                      Fields = new[] { "clip", "tool" })]
    internal sealed class PlaceBehavior : IManifestBehavior, IManifestBound, ITaskPageContent
    {
        /// <inheritdoc/>
        public IEnumerable<(string setting, UnityEngine.Object part, string property)> Bound(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step,
                                                                                            ManifestScene scene, string target)
            => Clips.Bound(action, scene, target);

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (action.targets == null || action.targets.Count == 0) yield return "it names nowhere to place it";
            if (string.IsNullOrEmpty(action.Field(TaskKit.Fields.Tool))) yield return "it names nothing in hand to place: take it in an earlier row";
            if (string.IsNullOrEmpty(action.clip)) yield return "it names no clip to play";
            // Where it goes inside what is placed travels into the hand with it: there is nowhere to bring it.
            Transform held = string.IsNullOrEmpty(action.Field(TaskKit.Fields.Tool)) ? null : scene.Node(action.Field(TaskKit.Fields.Tool));
            if (held == null || action.targets == null) yield break;
            foreach (string path in action.targets)
            {
                Transform spot = scene.Node(path);
                if (spot != null && spot.IsChildOf(held))
                    yield return $"the sheet names '{spot.name}' as where it goes, but in the delivery '{spot.name}' is part of '{held.name}', what this row " +
                                 $"places: it would go into the hand with it. Repair the sheet's Asset Names (the node it goes to), or here: turn Place off for " +
                                 $"'{spot.name}', add Place to that node, and set its Take and Tool in its settings";
            }
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            GameObject held = scene.Node(action.Field(TaskKit.Fields.Tool)).gameObject;
            scene.MakeHoldable(held, ctx);
            Rows.OneTool(action, step, scene, ctx);
            Clips.Play(action, step, scene, ctx);
            foreach (string path in action.targets)
            {
                var node = scene.Node(path).gameObject;
                Interactions.MakeTarget(node, held, ctx);
                Refusals.Say(scene, step, action, path, node, held, ctx);
                TaskSettings own = scene.Settings<TaskSettings>(step.id, path, action);
                // Carried there, it goes into its place and rides what the take moves (a stand-in the take slides home):
                // onto that part's own pivot and axes, as its rig has it, or where a person moved its spot.
                Clips.Trigger(node, action.clip, step, scene, held, UseTrigger.Click, own, ctx);
                Interactions.SetLeaves(scene.Root, scene.Clip(action.clip), held, own.placedStays, ctx);  // fitted, it stays: never back to the hand
                scene.EnableDuring(step.id, node, node.GetComponent<Target>(), ctx);
                scene.EnableDuring(step.id, node, Interactions.AddSelectNear(node, ctx: ctx), ctx);
            }
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
        {
            if (Refusals.Says(scene, step, action)) page.OnPlacard = true;  // a refusal is said on the placard
        }
    }
}
