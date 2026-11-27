using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Panels;
using M3trik.Unitytk.Steps;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>check</c> ("Checklist item 'Door open' gets a check mark"): the item is ticked, as its step ends (the
    /// step's work done), on the latest list before it that lists it: a row of its partner's
    /// (<see cref="TaskBehaviorAttribute.Partner"/>, <c>checklist</c>).
    /// </summary>
    [TaskBehavior("check", Fields = new[] { "label" }, NamesObjects = false, Partner = "checklist",
                      About = "The item is ticked on the checklist as its step ends.")]
    internal sealed class CheckBehavior : IManifestBehavior, ITaskPageContent
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            string item = action.Field(TaskKit.Fields.Label);
            if (string.IsNullOrEmpty(item)) yield return "it names no item to tick";
            else if (Checklists.ListStep(action, scene) == null) yield return $"no checklist before it lists '{item}'";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string placard = TaskKit.PlacardName(scene), item = action.Field(TaskKit.Fields.Label);
            string list = TaskKit.ChecklistName(Checklists.ListStep(action, scene));
            PlacardTick tick = PanelsActions.AddTick(placard, list, item, ctx: ctx);
            if (tick != null) scene.EnableAtEnd(step.id, tick.gameObject, tick, ctx);
            else scene.EnableAtEnd<PlacardTick>(step.id, PanelsActions.PlannedTick(placard, list, item), ctx);
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
            => page.OnPlacard = true;  // its tick is on the placard's list
    }
}
