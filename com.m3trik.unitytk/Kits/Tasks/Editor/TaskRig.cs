namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// The rig a task's build gives a scene that has none (<see cref="TaskSettings.rig"/>): a scene declares its own
    /// by a rig that travels (anything with a <see cref="Viewpoints.Traveler"/>, a headset's origin given one), which every
    /// choice leaves as it is.
    /// </summary>
    public enum TaskRig
    {
        /// <summary>The scene's main camera made the user: it travels, looks, holds and clicks.</summary>
        Desktop = 0,
        /// <summary>None: the scene brings its own rig.</summary>
        None = 1,
    }
}
