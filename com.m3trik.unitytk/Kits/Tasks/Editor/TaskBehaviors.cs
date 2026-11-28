using System;
using System.Collections.Generic;
using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// What kind of row a task behavior's are, as its builder declares: the traits of its
    /// <see cref="TaskBehaviorAttribute"/>, read from the manifest's registry
    /// (<see cref="ManifestBehaviors.Mark"/>) and refined per row where its builder says so
    /// (<see cref="ITaskRowTraits"/>), and the roles its builder takes by an interface
    /// (<see cref="IStepGate"/>). The task knows no behavior by its name: only by these.
    /// </summary>
    public static class TaskBehaviors
    {
        /// <summary>
        /// What kind of row <paramref name="action"/>, a row of <paramref name="step"/>, is (<see cref="TaskBehaviorAttribute.ActsOn"/>,
        /// <see cref="TaskBehaviorAttribute.Offers"/>, <see cref="TaskBehaviorAttribute.EndsStep"/>...): what its behavior's
        /// builder declares, refined for the row where the builder does (<see cref="ITaskRowTraits"/>: a button whose To is
        /// <c>retry</c> ends no step); none of them where this editor has no builder for it, or its builder is no task's.
        /// </summary>
        /// <param name="action">A plan action.</param>
        /// <param name="step">The step it is in; null where it is in none.</param>
        public static TaskBehaviorAttribute Traits(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step)
        {
            TaskBehaviorAttribute declared = Traits(action?.behavior);
            if (action == null || !(ManifestBehaviors.Find(action.behavior) is ITaskRowTraits rows)) return declared;
            TaskBehaviorAttribute own = declared.Copy();
            rows.Refine(own, action, step);
            return own;
        }

        /// <summary>
        /// What its builder declares every row of <paramref name="behavior"/> is, before a row refines it
        /// (<see cref="Traits(SheetPlan.Shape.PlanAction, SheetPlan.Shape.PlanStep)"/> asks of one row); none of them where this
        /// editor has no builder for it, or its builder is no task's.
        /// </summary>
        /// <param name="behavior">A plan action's behavior.</param>
        public static TaskBehaviorAttribute Traits(string behavior)
            => ManifestBehaviors.Mark(behavior) as TaskBehaviorAttribute ?? new TaskBehaviorAttribute(behavior ?? "");

        /// <summary>The behaviors this editor builds whose declared traits <paramref name="has"/> holds for, sorted.</summary>
        /// <param name="has">What their traits are.</param>
        /// <exception cref="ArgumentNullException"><paramref name="has"/> is null.</exception>
        public static IEnumerable<string> With(Func<TaskBehaviorAttribute, bool> has)
        {
            if (has == null) throw new ArgumentNullException(nameof(has));
            foreach (string name in ManifestBehaviors.Names())
                if (ManifestBehaviors.Mark(name) is TaskBehaviorAttribute traits && has(traits)) yield return name;
        }

        /// <summary>
        /// Whether rows of <paramref name="behavior"/> and <paramref name="other"/> pair (a tick and the list it ticks): either
        /// names the other its <see cref="TaskBehaviorAttribute.Partner"/>.
        /// </summary>
        /// <param name="behavior">A plan action's behavior.</param>
        /// <param name="other">Another's.</param>
        public static bool Pairs(string behavior, string other)
        {
            if (string.IsNullOrEmpty(behavior) || string.IsNullOrEmpty(other)) return false;
            return Traits(behavior).Partner == other || Traits(other).Partner == behavior;
        }

        /// <summary>
        /// The gate <paramref name="action"/>'s builder is for it (<see cref="IStepGate"/>, where the row holds its step:
        /// <see cref="IStepGate.Gates"/>); null where its builder is none, or the row holds nothing.
        /// </summary>
        /// <param name="action">A plan action.</param>
        /// <param name="step">The step it is in.</param>
        public static IStepGate Gate(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step)
            => action != null && ManifestBehaviors.Find(action.behavior) is IStepGate gate && gate.Gates(action, step) ? gate : null;
    }
}
