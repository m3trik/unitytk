namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>Where a Pause row holds its step (<see cref="TaskSettings.pauseWhen"/>).</summary>
    public enum PauseWhen
    {
        /// <summary>As it finishes: its button is offered once its actions are done, and goes on.</summary>
        Finish = 0,
        /// <summary>At its start: its button starts the step's actions (read first, then go).</summary>
        Start = 1,
    }
}
