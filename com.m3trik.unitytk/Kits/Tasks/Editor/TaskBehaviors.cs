using System;
using System.Collections.Generic;
using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// What kind of row a task behavior's are, as its builder declares: the traits of its
    /// <see cref="TaskBehaviorAttribute"/>, read from the manifest's registry
    /// (<see cref="ManifestBehaviors.Mark"/>), and the roles its builder takes by an interface
    /// (<see cref="IStepGate"/>). The task knows no behavior by its name: only by these.
    /// </summary>
    public static class TaskBehaviors
    {
        /// <summary>
        /// What kind of row <paramref name="behavior"/>'s are (<see cref="TaskBehaviorAttribute.ActsOn"/>,
        /// <see cref="TaskBehaviorAttribute.Offers"/>, <see cref="TaskBehaviorAttribute.Teleports"/>...);
        /// none of them where this editor has no builder for it, or its builder is no task's.
        /// </summary>
        /// <param name="behavior">A plan action's behavior.</param>
        public static TaskBehaviorAttribute Traits(string behavior)
            => ManifestBehaviors.Mark(behavior) as TaskBehaviorAttribute ?? new TaskBehaviorAttribute(behavior ?? "");

        /// <summary>The behaviors this editor builds whose traits <paramref name="has"/> holds for, sorted.</summary>
        /// <param name="has">What their traits are.</param>
        /// <exception cref="ArgumentNullException"><paramref name="has"/> is null.</exception>
        public static IEnumerable<string> With(Func<TaskBehaviorAttribute, bool> has)
        {
            if (has == null) throw new ArgumentNullException(nameof(has));
            foreach (string name in ManifestBehaviors.Names())
                if (ManifestBehaviors.Mark(name) is TaskBehaviorAttribute traits && has(traits)) yield return name;
        }

        /// <summary>
        /// Whether rows of <paramref name="behavior"/> and <paramref name="other"/> pair (a check and the checklist it
        /// ticks, a quiz and its questions): either names the other its <see cref="TaskBehaviorAttribute.Partner"/>.
        /// </summary>
        /// <param name="behavior">A plan action's behavior.</param>
        /// <param name="other">Another's.</param>
        public static bool Pairs(string behavior, string other)
        {
            if (string.IsNullOrEmpty(behavior) || string.IsNullOrEmpty(other)) return false;
            return Traits(behavior).Partner == other || Traits(other).Partner == behavior;
        }

        /// <summary>The gate <paramref name="behavior"/>'s builder is (<see cref="IStepGate"/>); null where it is none.</summary>
        /// <param name="behavior">A plan action's behavior.</param>
        public static IStepGate Gate(string behavior) => ManifestBehaviors.Find(behavior) as IStepGate;
    }
}
