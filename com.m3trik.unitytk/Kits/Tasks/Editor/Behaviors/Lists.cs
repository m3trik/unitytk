using System;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Kits.Tasks
{
    // What a list row and the rows ticking it share (list and tick, a project's own of either): the items a list holds, and
    // the list a tick finds. Two rows pair by their traits (TaskBehaviors.Pairs), never by name.
    static class Lists
    {
        // The items a row's label lists: an item a line, or split at commas and semicolons, quotes trimmed. The sheet's check
        // splits the sheet's label by the same rule (sheet_check.py _list_items); this splits the label the row is built with,
        // which a list behavior declaring it among its Fields lets a person set in the manifest, past the reader: the plan
        // cannot carry the items.
        internal static List<string> Items(string label)
            => (label ?? "").Split('\n', '\r', ',', ';').Select(item => item.Trim().Trim('\'', '"')).Where(item => item.Length > 0).ToList();

        // The items list (a list row) holds: its own, else the labels of the later rows pairing with it, up to the next
        // row of its behavior, each once, in their order.
        internal static List<string> Items(SheetPlan.Shape.PlanAction list, ManifestScene scene)
        {
            List<string> items = Items(list.Field(TaskKit.Fields.Label));
            if (items.Count > 0) return items;
            foreach (var (_, later) in PlanOrder.From(scene.Plan, list).Skip(1))
            {
                if (later.behavior == list.behavior) break;
                string label = later.Field(TaskKit.Fields.Label);
                if (TaskBehaviors.Pairs(later.behavior, list.behavior) && !string.IsNullOrEmpty(label)
                    && !items.Exists(item => string.Equals(item, label, StringComparison.OrdinalIgnoreCase)))
                    items.Add(label);
            }
            return items;
        }

        // The step of the latest row before tick that pairs with it and lists its label; null where none does.
        internal static string ListStep(SheetPlan.Shape.PlanAction tick, ManifestScene scene)
        {
            string label = tick.Field(TaskKit.Fields.Label);
            foreach (var (step, before) in PlanOrder.Before(scene.Plan, tick))
                if (TaskBehaviors.Pairs(before.behavior, tick.behavior)
                    && Items(before, scene).Exists(item => string.Equals(item, label, StringComparison.OrdinalIgnoreCase)))
                    return step.id;
            return null;
        }
    }
}
