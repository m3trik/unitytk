using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// A behavior whose rows are not all of one kind: its builder refines the traits its <see cref="TaskBehaviorAttribute"/>
    /// declares for each row, by the row's own cells and settings (a <c>button</c> whose To is <c>retry</c> is an answer of a
    /// question, which ends no step), never by a behavior's name. <see cref="TaskBehaviors.Traits(SheetPlan.Shape.PlanAction, SheetPlan.Shape.PlanStep)"/>
    /// asks it; an <see cref="IManifestBehavior"/> implementing it takes the role, a project's own as the kit's.
    /// </summary>
    public interface ITaskRowTraits
    {
        /// <summary>Refine <paramref name="traits"/>, a copy of what the builder's attribute declares, for <paramref name="action"/>.</summary>
        /// <param name="traits">The row's traits, as the attribute declares them until refined here.</param>
        /// <param name="action">The plan's action, of this behavior.</param>
        /// <param name="step">The step it is in; null where it is in none (a row a probe asks of).</param>
        void Refine(TaskBehaviorAttribute traits, SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step);
    }
}
