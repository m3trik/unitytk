using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Panels;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>tick</c> ("'Door open' is ticked on the list"): the item is ticked, as its step ends (the step's work done), on
    /// the latest list before it that lists it: a row of its partner's (<see cref="TaskBehaviorAttribute.Partner"/>,
    /// <c>list</c>).
    /// </summary>
    [TaskBehavior("tick", Fields = new[] { TaskKit.Fields.Label }, NamesObjects = false, Partner = "list",
                  About = "The item is ticked on the list as its step ends.")]
    internal sealed class TickBehavior : IManifestBehavior, ITaskPageContent
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            string item = action.Field(TaskKit.Fields.Label);
            if (string.IsNullOrEmpty(item)) yield return "it names no item to tick";
            else if (Lists.ListStep(action, scene) == null) yield return $"no list before it lists '{item}'";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string placard = TaskKit.PlacardName(scene), item = action.Field(TaskKit.Fields.Label);
            string list = TaskKit.ListName(Lists.ListStep(action, scene));
            PlacardTick tick = PanelsActions.AddTick(placard, list, item, ctx: ctx);
            if (tick != null) scene.EnableAtEnd(step.id, tick.gameObject, tick, ctx);
            else scene.EnableAtEnd<PlacardTick>(step.id, PanelsActions.PlannedTick(placard, list, item), ctx);
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
            => page.OnPlacard = true;  // its tick is on the placard's list
    }
}
