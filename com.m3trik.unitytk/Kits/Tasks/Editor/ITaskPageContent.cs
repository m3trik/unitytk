using M3trik.Unitytk.Manifests;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// A behavior whose rows put something on the task's placard: on their step's page (the key pad an
    /// <c>answer</c> row brings, a wrong answer's reply), or on the placard beside it (a list, a tick, what a part
    /// selected without what it needs in hand says: <see cref="TaskPage.OnPlacard"/>).
    /// An <see cref="IManifestBehavior"/> implementing it fills the page as it is made (<see cref="TaskPage"/>), a
    /// project's own behavior as the kit's do; what it fills decides whether its step gets a page and the task its
    /// placard (<see cref="TaskSettings.pages"/>). A label it offers needs no filling:
    /// <see cref="TaskBehaviorAttribute.Offers"/> says so.
    /// </summary>
    public interface ITaskPageContent
    {
        /// <summary>Put what <paramref name="action"/> shows on <paramref name="page"/>, its step's page.</summary>
        /// <param name="action">The plan's action, of this behavior.</param>
        /// <param name="step">The step it is in.</param>
        /// <param name="page">The step's page, as its rows before this one left it.</param>
        /// <param name="scene">The delivery the task is built into (its <see cref="ManifestScene.Settings{T}()"/>).</param>
        void Fill(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, TaskPage page, ManifestScene scene);
    }
}
