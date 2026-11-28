using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using PlanAction = M3trik.Unitytk.Manifests.SheetPlan.Shape.PlanAction;
using PlanStep = M3trik.Unitytk.Manifests.SheetPlan.Shape.PlanStep;

namespace M3trik.Unitytk.Kits.Tasks
{
    // When a cue (an arrow, a highlight) shows within its step. One at something the step has nobody act on shows
    // for the whole of it. One at what a row of the step has the user act on points them to what they can do now
    // (its row's cue: HighlightSettings.cue, IndicateSettings.cue; Interaction's TargetCue): it comes once what that needs is in hand and goes once they
    // have acted, so the keys are pointed out first, then the locks, each lock's arrow gone as its key turns.
    static class Cues
    {
        // What step's rows have the user act on that a cue at aims points out: each such object at one of aims,
        // inside one, or holding one (a cue at a group of two locks is about both; one at a screw's head, the screw).
        internal static List<GameObject> ActedOn(ManifestScene scene, PlanStep step, IReadOnlyList<Transform> aims)
        {
            var found = new List<GameObject>();
            if (step.actions == null) return found;
            foreach (PlanAction action in step.actions)
            {
                if (action == null || action.targets == null || !TaskBehaviors.Traits(action, step).ActsOn) continue;
                foreach (string path in action.targets)
                {
                    Transform node = scene.Node(path);
                    if (node == null || found.Contains(node.gameObject)) continue;
                    foreach (Transform aim in aims)
                    {
                        if (aim == null || !(node == aim || node.IsChildOf(aim) || aim.IsChildOf(node))) continue;
                        found.Add(node.gameObject);
                        break;
                    }
                }
            }
            return found;
        }

        // Show cue (on node; null: a dry run only planned it there) while step runs, as when (its row's cue setting) says.
        internal static void During<T>(ManifestScene scene, PlanStep step, GameObject node, T cue, IReadOnlyList<Transform> aims,
                                       CueWhen when, OpContext ctx) where T : Behaviour
        {
            List<GameObject> acted = ActedOn(scene, step, aims);
            if (acted.Count == 0)
            {
                scene.EnableDuring(step.id, node, cue, ctx);
                return;
            }
            bool ready = when == CueWhen.Ready;
            scene.Hold(cue);  // the row's part is its cue; what switches it is held with it
            TargetCue shows = null;
            if (cue != null) shows = Interactions.AddTargetCue(node, cue, acted, ready, ready, ctx);
            else Interactions.AddTargetCue(new PlannedObject(node.name, node.transform.parent), typeof(T), ctx);
            scene.EnableDuring(step.id, node, shows, ctx);
        }

        // During for a cue on node, an object a dry run only planned.
        internal static void During<T>(ManifestScene scene, PlanStep step, PlannedObject node, IReadOnlyList<Transform> aims,
                                       OpContext ctx) where T : Behaviour
        {
            if (ActedOn(scene, step, aims).Count == 0)
            {
                scene.EnableDuring<T>(step.id, node, ctx);
                return;
            }
            Interactions.AddTargetCue(node, typeof(T), ctx);
            scene.EnableDuring<TargetCue>(step.id, node, ctx);
        }
    }
}
