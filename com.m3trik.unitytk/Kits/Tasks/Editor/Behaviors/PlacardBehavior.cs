using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>placard</c> ("Placard text reads "Select the door to open it""): the step's page on
    /// the placard says the quoted text, in place of the sheet's placard column or voice line.
    /// </summary>
    [TaskBehavior("placard", NamesObjects = false, About = "The step's page says the row's quoted text.")]
    internal sealed class PlacardBehavior : IManifestBehavior, ITaskPageContent
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            if (string.IsNullOrEmpty(action.Field(TaskKit.Fields.Label))) yield return "it quotes no text for the placard";
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
        }

        /// <inheritdoc/>
        public void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene)
        {
            if (!string.IsNullOrEmpty(action.Field(TaskKit.Fields.Label))) page.Text = action.Field(TaskKit.Fields.Label);
        }
    }
}
