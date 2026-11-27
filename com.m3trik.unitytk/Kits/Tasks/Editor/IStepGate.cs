using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// A behavior whose rows hold their step on a button (the kit's <c>pause</c>): its label is the button, offered
    /// once the step's actions are done where the step also asks the user to act, or, held at its start, starting
    /// them. A step whose only choice is a gate at its start goes on by itself once started. An
    /// <see cref="IManifestBehavior"/> implementing it takes the role, a project's own as the kit's.
    /// </summary>
    public interface IStepGate
    {
        /// <summary>Whether <paramref name="action"/>'s button holds <paramref name="step"/> at its start (it starts the step's actions) rather than as it finishes.</summary>
        /// <param name="action">The plan's action, of this behavior.</param>
        /// <param name="step">The step it is in.</param>
        /// <param name="scene">The delivery the task is built into (the row's settings: <see cref="ManifestScene.Settings{T}(string, string, SheetPlan.Shape.PlanAction)"/>).</param>
        bool AtStart(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene);
    }
}
