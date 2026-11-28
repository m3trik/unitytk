using System;
using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using M3trik.Unitytk.Panels;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    // What every builder of a part that needs something in hand shares: selected without it, the part says so on
    // the task's placard (its use.refused), so a refusal is never silent.
    static class Refusals
    {
        // Have node's target (path, a target of action in step), needing tool in hand, tell the user to take it when refused.
        internal static void Say(ManifestScene scene, SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction action, string path,
                                 GameObject node, GameObject tool, OpContext ctx)
        {
            TaskSettings own = scene.Settings<TaskSettings>(step.id, path, action);
            if (tool == null || string.IsNullOrEmpty(own.use.refused)) return;
            Target target = node.GetComponent<Target>();
            PanelsActions.SayOn(TaskKit.PlacardName(scene), node, target, target == null ? null : target.onRefused,
                                own.use.refused.Replace("{0}", Readable(tool.name, own.nameSuffixes)), ctx);
        }

        // Whether a target of action in step says something when refused (it names a With, and its reply says
        // something): its step puts that on the placard.
        internal static bool Says(ManifestScene scene, SheetPlan.Shape.PlanStep step, SheetPlan.Shape.PlanAction action)
        {
            if (string.IsNullOrEmpty(action.Field(TaskKit.Fields.With)) || action.targets == null) return false;
            foreach (string path in action.targets)
                if (!string.IsNullOrEmpty(scene.Settings<TaskSettings>(step.id, path, action).use.refused)) return true;
            return false;
        }

        // A node's name as a person says it: KEY_GRP key, LATCH_UPPER_GEO latch upper (suffixes: the last words
        // not said, ignoring case).
        internal static string Readable(string name, IReadOnlyCollection<string> suffixes)
        {
            var words = new List<string>(name.ToLowerInvariant().Split(new[] { '_', ' ' }, StringSplitOptions.RemoveEmptyEntries));
            if (words.Count > 1 && suffixes != null)
                foreach (string suffix in suffixes)
                    if (string.Equals(words[words.Count - 1], suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        words.RemoveAt(words.Count - 1);
                        break;
                    }
            return string.Join(" ", words);
        }
    }
}
