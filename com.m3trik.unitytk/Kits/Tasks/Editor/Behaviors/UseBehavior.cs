using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>use</c> ("Selecting the bolt with the wrench in hand plays B01_1"): each target a target requiring the row's With
    /// in hand (made holdable) where it names one, playing the row's clip as its <see cref="UseSettings.trigger"/> says,
    /// selectable while its step runs; the step awaits the clip's end. Select (the usual): a click plays it. Hold: held
    /// pressed, it plays while held and pauses as it is let go (or plays back to its start, <see cref="UseSettings.rewind"/>).
    /// A <see cref="UseSettings.hold"/> above 0, held or selected, is a timed press: held that long, it plays. Near: bringing
    /// what the hand holds within reach of the target (<see cref="SelectNear"/>: a controller's hand moving it there), or a
    /// click with it in hand, plays it, and what the clip moves into place stays there, out of the hand
    /// (<see cref="UseSettings.stays"/>; off: it comes back to the hand, as a tool does). A With the clip moves is lent to it
    /// and comes back to the hand after (Near aside); one it does not move goes to the part the clip moves and works there
    /// while it plays (a wrench on the bolt it turns out; a part placed rides what its take slides home). A target selected
    /// without what it needs in hand says so (<see cref="UseSettings.refused"/>).
    /// </summary>
    [TaskBehavior("use", ActsOn = true, Fields = new[] { "clip", TaskKit.Fields.With },
                  RowSettings = new[] { nameof(ClipSettings.speed), nameof(ClipSettings.start), nameof(ClipSettings.stop) },
                  Suffix = new[] { nameof(UseSettings.trigger) },
                  About = "Selecting the object, with the row's With in hand, plays its take; by its trigger, held, or the held thing brought to it, which leaves what it places in place.")]
    internal sealed class UseBehavior : IManifestBehavior, IManifestBound, ITaskPageContent
    {
        /// <inheritdoc/>
        public IEnumerable<(string setting, UnityEngine.Object part, string property)> Bound(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step,
                                                                                            ManifestScene scene, string target)
            => Clips.Bound(action, scene, target, nameof(TaskSettings.use));

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            foreach (string problem in Rows.NeedTargets(action, "select")) yield return problem;
            if (string.IsNullOrEmpty(action.clip)) yield return "it names no clip to play";
            if (action.targets == null) yield break;
            string named = action.Field(TaskKit.Fields.With);
            Transform held = string.IsNullOrEmpty(named) ? null : scene.Node(named);
            string step = PlanOrder.StepOf(scene.Plan, action)?.id ?? "";
            foreach (string path in action.targets)
            {
                if (scene.Settings<TaskSettings>(step, path, action).use.trigger != UseTrigger.Near) continue;
                if (string.IsNullOrEmpty(named))
                {
                    yield return $"its trigger near brings nothing to '{path}': set its With (what the hand holds, taken in an earlier row)";
                    continue;
                }
                // Where it goes inside what is placed travels into the hand with it: there is nowhere to bring it.
                Transform spot = scene.Node(path);
                if (held != null && spot != null && spot.IsChildOf(held))
                    yield return $"the sheet names '{spot.name}' as where it goes, but in the delivery '{spot.name}' is part of '{held.name}', what this row " +
                                 $"places: it would go into the hand with it. Repair the sheet's Target (the node it goes to), or here: turn Use off for " +
                                 $"'{spot.name}', add Use to that node, and set its Take, its With and its trigger near in its settings";
            }
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string named = action.Field(TaskKit.Fields.With);
            GameObject with = string.IsNullOrEmpty(named) ? null : scene.Node(named).gameObject;
            if (with != null) scene.MakeHoldable(with, ctx);
            Rows.OneTool(action, step, scene, ctx);
            Clips.Play(action, step, scene, scene.Settings<TaskSettings>(step.id, "", action).use, ctx);
            foreach (string path in action.targets)
            {
                var node = scene.Node(path).gameObject;
                Interactions.MakeTarget(node, with, ctx);
                Refusals.Say(scene, step, action, path, node, with, ctx);
                UseSettings own = scene.Settings<TaskSettings>(step.id, path, action).use;
                // Brought there, it goes into its place and rides what the take moves (a stand-in the take slides home):
                // onto that part's own pivot and axes, as its rig has it, or where a person moved its spot.
                Clips.Trigger(node, action.clip, step, scene, with, own, ctx);
                bool near = own.trigger == UseTrigger.Near && with != null;  // its check holds back a near trigger with nothing in hand
                if (near)
                {
                    if (own.hold > 0f)
                        ctx.Raise(ManifestsActions.NoteCode, $"{step.id}: '{node.name}' is set off by what the hand brings, held no time: its hold is unused");
                    Interactions.SetLeaves(scene.Root, scene.Clip(action.clip), with, own.stays, ctx);  // fitted, it stays: never back to the hand
                }
                scene.EnableDuring(step.id, node, node.GetComponent<Target>(), ctx);
                if (near) scene.EnableDuring(step.id, node, Interactions.AddSelectNear(node, ctx: ctx), ctx);
            }
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
        {
            if (Refusals.Says(scene, step, action)) page.OnPlacard = true;  // a refusal is said on the placard
        }
    }
}
