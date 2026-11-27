namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>When a cue (an arrow, a highlight) shows within its step (<see cref="TaskSettings.cueWhen"/>).</summary>
    public enum CueWhen
    {
        /// <summary>
        /// While the user can act on what it points out: once what that needs is in hand, until they have acted on it.
        /// A cue at something its step has nobody act on shows for the whole step.
        /// </summary>
        Ready = 0,
        /// <summary>For the whole step, whatever the user holds or has done.</summary>
        Step = 1,
    }
}
