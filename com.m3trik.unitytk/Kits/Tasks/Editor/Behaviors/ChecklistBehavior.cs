using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Panels;
using M3trik.Unitytk.Steps;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>checklist</c> ("A checklist appears on the placard: Door open, Bolt out"): a list of the row's items
    /// (its label, split at commas and semicolons; a row listing none, "a checklist appears with blank check
    /// boxes", lists the items the rows pairing with it tick after it (<see cref="TaskBehaviors.Pairs"/>:
    /// <c>check</c>, a project's own), up to the next checklist, in their order) on the task's placard,
    /// shown from its step on, below each step's page, until another list replaces it.
    /// </summary>
    [TaskBehavior("checklist", NamesObjects = false,
                      About = "A list of items on the placard, shown from its step on, or (by its setting) the list an earlier row put up goes.",
                      Settings = new[] { nameof(TaskSettings.checklistShow) }, Suffix = new[] { nameof(TaskSettings.checklistShow) })]
    internal sealed class ChecklistBehavior : IManifestBehavior, ITaskPageContent
    {
        /// <summary>Whether <paramref name="action"/>, a list row of <paramref name="step"/>, takes the list away (<see cref="TaskSettings.checklistShow"/>: the sheet's <c>until</c>).</summary>
        internal static bool Hides(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene)
            => scene.Settings<TaskSettings>(step.id, "", action).checklistShow == Showing.Hide;

        /// <summary>The step of the row of action's behavior whose list is still up as <paramref name="action"/> comes; null where a hiding row took it away, or none came before.</summary>
        internal static SheetPlan.Shape.PlanStep Shown(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            foreach (var each in PlanOrder.Before(scene.Plan, action))
            {
                if (each.action.behavior != action.behavior) continue;
                return Hides(each.action, each.step, scene) ? null : each.step;
            }
            return null;
        }

        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            SheetPlan.Shape.PlanStep step = PlanOrder.StepOf(scene.Plan, action);
            if (step != null && Hides(action, step, scene))
            {
                if (Shown(action, scene) == null) yield return "no row before it shows a checklist";
                yield break;
            }
            if (Checklists.Items(action, scene).Count == 0)
                yield return "it lists no items, and no check row follows it: write them after a colon, comma separated";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string placard = TaskKit.PlacardName(scene);
            if (Hides(action, step, scene))
            {
                // The list an earlier row put up goes as this step starts.
                string shown = TaskKit.ChecklistName(Shown(action, scene).id);
                GameObject owner = SceneRoots.Find(placard);
                Transform node = owner == null ? null : owner.transform.Find(shown);
                PlacardList up = node != null ? node.GetComponent<PlacardList>() : null;
                if (up != null) scene.DisableFrom(step.id, up.gameObject, up, ctx);
                else scene.DisableFrom<PlacardList>(step.id, PanelsActions.PlannedList(placard, shown), ctx);
                return;
            }
            string name = TaskKit.ChecklistName(step.id);
            PlacardList list = PanelsActions.AddList(placard, name, Checklists.Items(action, scene), ctx: ctx);
            if (list != null) scene.EnableFrom(step.id, list.gameObject, list, ctx);
            else scene.EnableFrom<PlacardList>(step.id, PanelsActions.PlannedList(placard, name), ctx);
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
        {
            if (!Hides(action, step, scene)) page.OnPlacard = true;  // its list is on the placard
        }
    }
}
