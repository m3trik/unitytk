namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>How a Use row's object plays its take (<see cref="TaskSettings.useTrigger"/>).</summary>
    public enum UseTrigger
    {
        /// <summary>A click plays it to its end.</summary>
        Click = 0,
        /// <summary>It plays while the object is held pressed; let go, it stops (or plays back: <see cref="TaskSettings.releaseRewinds"/>).</summary>
        HoldToPlay = 1,
        /// <summary>A press held for <see cref="TaskSettings.pressSeconds"/> plays it, as a click would; let go sooner, nothing plays.</summary>
        TimedPress = 2,
    }
}
