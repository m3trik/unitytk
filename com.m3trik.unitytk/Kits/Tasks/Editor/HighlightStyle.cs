namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>How a Highlight row's object glows (<see cref="TaskSettings.highlightStyle"/>).</summary>
    public enum HighlightStyle
    {
        /// <summary>Pulsing where a row of the task has the user act on the object, steady where they only look.</summary>
        Auto = 0,
        /// <summary>Pulsing, whatever the task asks of the object.</summary>
        Pulse = 1,
        /// <summary>A steady glow.</summary>
        Steady = 2,
    }
}
