using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using M3trik.Unitytk.Steps;
using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    // What every builder that plays a clip shares: the delivery's clip player plays it, its
    // end signals the steps, and the step it plays in awaits that end; a target's trigger for it
    // is enabled by that step alone.
    static class Clips
    {
        // action's take plays over the window and at the speed its row gives it: the row's as a whole (its RowSettings), whatever
        // objects it names, as the clip player keeps one entry for the take.
        internal static void Play(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string clip = action.clip;
            TaskSettings own = scene.Settings<TaskSettings>(step.id, "", action);
            Interactions.AddClip(scene.Root, scene.Clip(clip), null, new Vector2(own.playFrom, own.playUntil), ctx);
            ClipPlayer player = scene.Root.GetComponent<ClipPlayer>();
            StepsActions.SignalOn(scene.Root, player, player == null ? null : player.onFinished, scene.Sequence(), ctx);
            StepsActions.Await(scene.Sequence(), step.id, clip, ctx);
            Interactions.SetSpeed(scene.Root, clip, own.playSpeed, ctx);
        }

        // The take settings of action a person may change on its clip's entry of the delivery's clip player (found by its
        // clip, wherever it stands in the list), each with the entry's property: kept as the row's (IManifestBound), asked of the
        // row itself (an empty target), never of one of its objects.
        internal static IEnumerable<(string setting, Object part, string property)> Bound(SheetPlan.Shape.PlanAction action,
                                                                                           ManifestScene scene, string target)
        {
            if (string.IsNullOrEmpty(action.clip) || !string.IsNullOrEmpty(target)) yield break;
            ClipPlayer player = scene.Root.GetComponent<ClipPlayer>();
            int at = player == null ? -1 : player.clips.FindIndex(e => e != null && e.clip != null && e.clip.name == action.clip);
            if (at < 0) yield break;
            string entry = $"{nameof(ClipPlayer.clips)}.Array.data[{at}].";
            yield return (nameof(TaskSettings.playFrom), player, entry + nameof(ClipPlayer.Entry.from));
            yield return (nameof(TaskSettings.playUntil), player, entry + nameof(ClipPlayer.Entry.until));
            yield return (nameof(TaskSettings.playSpeed), player, entry + nameof(ClipPlayer.Entry.speed));
        }

        // node's trigger for clip (tool: what the selecting hand holds, working where the clip
        // does not move it), on while step runs: a target later steps use again plays their clips then.
        // How it plays (a click, held, a timed press; played back as let go) is the object's settings' (own).
        internal static void Trigger(GameObject node, string clip, SheetPlan.Shape.PlanStep step, ManifestScene scene,
                                     GameObject tool, UseTrigger how, TaskSettings own, OpContext ctx)
        {
            bool held = how == UseTrigger.HoldToPlay;
            ClipTrigger trigger = Interactions.AddClipTrigger(node, scene.Root, clip, tool, held,
                                                              pressSeconds: how == UseTrigger.TimedPress ? own.pressSeconds : 0f,
                                                              reverseOnRelease: held && own.releaseRewinds, ctx: ctx);
            if (tool != null) Spots.Work(scene, step, node, clip, tool, trigger, ctx);
            scene.EnableDuring(step.id, node, trigger, ctx);
        }
    }
}
