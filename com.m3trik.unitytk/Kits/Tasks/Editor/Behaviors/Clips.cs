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
        // action's take plays over the window and at the speed its row gives it (take: its row's settings as a whole, the
        // RowSettings of its word's group), whatever objects it names, as the clip player keeps one entry for the take.
        internal static void Play(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, ClipSettings take, OpContext ctx)
        {
            string clip = action.clip;
            Interactions.AddClip(scene.Root, scene.Clip(clip), null, new Vector2(take.start, take.stop), ctx);
            ClipPlayer player = scene.Root.GetComponent<ClipPlayer>();
            StepsActions.SignalOn(scene.Root, player, player == null ? null : player.onFinished, scene.Sequence(), ctx);
            StepsActions.Await(scene.Sequence(), step.id, clip, ctx);
            Interactions.SetSpeed(scene.Root, clip, take.speed, ctx);
        }

        // The take settings of action a person may change on its clip's entry of the delivery's clip player (found by its
        // clip, wherever it stands in the list), each with the entry's property: kept as the row's (IManifestBound), asked of the
        // row itself (an empty target), never of one of its objects. group: the member of TaskSettings its word's are under.
        internal static IEnumerable<(string setting, Object part, string property)> Bound(SheetPlan.Shape.PlanAction action,
                                                                                           ManifestScene scene, string target, string group)
        {
            if (string.IsNullOrEmpty(action.clip) || !string.IsNullOrEmpty(target)) yield break;
            ClipPlayer player = scene.Root.GetComponent<ClipPlayer>();
            int at = player == null ? -1 : player.clips.FindIndex(e => e != null && e.clip != null && e.clip.name == action.clip);
            if (at < 0) yield break;
            string entry = $"{nameof(ClipPlayer.clips)}.Array.data[{at}].";
            yield return ($"{group}.{nameof(ClipSettings.start)}", player, entry + nameof(ClipPlayer.Entry.from));
            yield return ($"{group}.{nameof(ClipSettings.stop)}", player, entry + nameof(ClipPlayer.Entry.until));
            yield return ($"{group}.{nameof(ClipSettings.speed)}", player, entry + nameof(ClipPlayer.Entry.speed));
        }

        // node's trigger for clip (tool: what the selecting hand holds, working where the clip does not move it), on while
        // step runs: a target later steps use again plays their clips then. How it plays is the object's settings' (own):
        // selected, or brought what the hand holds, it plays to its end; held, with no hold, it plays while held (played back
        // as let go, by its rewind); a hold above 0 is a timed press of that long, held or selected.
        internal static void Trigger(GameObject node, string clip, SheetPlan.Shape.PlanStep step, ManifestScene scene,
                                     GameObject tool, UseSettings own, OpContext ctx)
        {
            bool near = own.trigger == UseTrigger.Near, held = own.trigger == UseTrigger.Hold && own.hold <= 0f;
            ClipTrigger trigger = Interactions.AddClipTrigger(node, scene.Root, clip, tool, held,
                                                              pressSeconds: near ? 0f : own.hold,
                                                              reverseOnRelease: held && own.rewind, ctx: ctx);
            if (tool != null) Spots.Work(scene, step, node, clip, tool, trigger, ctx);
            scene.EnableDuring(step.id, node, trigger, ctx);
        }
    }
}
