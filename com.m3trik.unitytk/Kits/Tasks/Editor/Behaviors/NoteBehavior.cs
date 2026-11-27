using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary><c>note</c> (a row whose Action cell says <c>note</c>): a row for people, such as the scene's description; nothing is built.</summary>
    [TaskBehavior("note", NamesObjects = false, About = "A row for people: nothing is built.")]
    internal sealed class NoteBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            yield break;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
        }
    }
}
