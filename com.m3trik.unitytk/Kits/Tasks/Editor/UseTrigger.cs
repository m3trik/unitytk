namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>What sets a Use row's take off (<see cref="UseSettings.trigger"/>, the sheet's <c>trigger</c>).</summary>
    public enum UseTrigger
    {
        /// <summary>Selecting the object plays it to its end (<see cref="UseSettings.hold"/> is unused: a click).</summary>
        Select = 0,
        /// <summary>
        /// Holding the object pressed: with <see cref="UseSettings.hold"/> 0 it plays while held, and let go it stops (or plays
        /// back: <see cref="UseSettings.rewind"/>); above 0, a press held that long plays it, as a select would.
        /// </summary>
        Hold = 1,
        /// <summary>
        /// Bringing what the hand holds (the row's With) to the object, or selecting the object with it in hand: what the take
        /// moves into place stays there, out of the hand (<see cref="UseSettings.stays"/>).
        /// </summary>
        Near = 2,
    }
}
