namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>Which steps get a page on the task's placard (<see cref="TaskSettings.pages"/>).</summary>
    public enum PlacardPages
    {
        /// <summary>
        /// A step with something to show on it alone: its placard text (the sheet's placard column), a button, the key
        /// pad or a reply. The placard is made only where a step has a page, or a row puts something else on it (a list,
        /// a tick, a refusal).
        /// </summary>
        Needed = 0,
        /// <summary>Every step, its text its placard text, else its voice line.</summary>
        EveryStep = 1,
    }
}
