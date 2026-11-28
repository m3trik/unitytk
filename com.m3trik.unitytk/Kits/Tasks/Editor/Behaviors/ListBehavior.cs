using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Panels;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>list</c> ("A list is shown: Door open, Bolt out"): a list of the row's items (its Label, an item a line, or split
    /// at commas and semicolons; a row listing none lists the items the rows pairing with it tick after it,
    /// <see cref="TaskBehaviors.Pairs"/>: <c>tick</c>, a project's own, up to the next list, in their order) on the task's
    /// placard, below each step's page, from its step on for its <see cref="ListSettings.until"/> (a span: <c>step</c>, or
    /// until a named step starts; empty: until the next list row's step, whose list replaces it).
    /// </summary>
    [TaskBehavior("list", Fields = new[] { TaskKit.Fields.Label }, NamesObjects = false,
                  About = "A list of items on the placard, shown from its step on for its span.")]
    internal sealed class ListBehavior : IManifestBehavior, ITaskPageContent
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (Lists.Items(action, scene).Count == 0)
                yield return "it lists no items, and no tick row follows it: write them in its Label, an item a line";
            SheetPlan.Shape.PlanStep step = PlanOrder.StepOf(scene.Plan, action);
            string until = Spans.Problem(scene.Plan, step, scene.Settings<TaskSettings>(step?.id ?? "", "", action).list.until, "until");
            if (until != null) yield return until;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            string placard = TaskKit.PlacardName(scene), name = TaskKit.ListName(step.id);
            string until = scene.Settings<TaskSettings>(step.id, "", action).list.until;
            if (string.IsNullOrEmpty(until)) until = Spans.Next(scene, step, action);
            PlacardList list = PanelsActions.AddList(placard, name, Lists.Items(action, scene), ctx: ctx);
            if (list != null) Spans.Show(scene, step, until, list.gameObject, list, ctx);
            else Spans.Show<PlacardList>(scene, step, until, PanelsActions.PlannedList(placard, name), ctx);
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
            => page.OnPlacard = true;  // its list is on the placard
    }
}
