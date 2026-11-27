using M3trik.Unitytk.Manifests;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// <c>advance</c> ("Auto Advance"): the step goes on by itself once everything it awaits is done
    /// and it has run long enough for what it says to be heard: its sound, where it has one, else
    /// its words read (<see cref="TaskKit.DwellOf(SheetPlan.Shape.PlanStep, ManifestScene)"/>).
    /// Every step that leaves the user no choice does so already (<see cref="TaskBuild"/>): the row
    /// says so (<see cref="TaskBehaviorAttribute.GoesOn"/>), makes a step that offers a choice go on too,
    /// and turned off, has its step wait. How long a step runs is the task's (<see cref="TaskSettings.wordsPerSecond"/>,
    /// <see cref="TaskSettings.minimumDwell"/>, <see cref="TaskSettings.voiceTail"/>), one pace for every step: no row's.
    /// </summary>
    [TaskBehavior("advance", NamesObjects = false, GoesOn = true,
                      About = "The step goes on by itself once done and heard or read: every step that offers no choice does; turned off, it waits.")]
    internal sealed class AdvanceBehavior : IManifestBehavior
    {
        /// <inheritdoc/>
        public IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene)
        {
            yield break;
        }

        /// <inheritdoc/>
        public void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx)
        {
            // The kit gives its step its advance as the build ends (TaskBuild.GoOn), with every other step's.
        }
    }
}
