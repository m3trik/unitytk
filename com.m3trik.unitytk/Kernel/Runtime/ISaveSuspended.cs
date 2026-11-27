namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// What holds edits a scene or a prefab would save that are not its own (a binding's, a preview's, a look's):
    /// before the editor writes one, each gives them back (<see cref="Suspend"/>), and once it is written, takes
    /// them again (<see cref="Resume"/>), so a saved file holds none of them. A component implements it and is
    /// found in the open scenes on every write; an editor keeper of components implements it and adds itself to
    /// the editor's save hook (the kernel's <c>SaveSuspension.Add</c>). It runs in the editor alone: a player
    /// saves no scene.
    /// </summary>
    public interface ISaveSuspended
    {
        /// <summary>
        /// Give back every edit it made that a scene or a prefab saves, in whichever open scene it made it (a binding
        /// in one scene may edit another's); nothing where it holds none. It may be asked again before it is taken
        /// again (a scene's save after a prefab's write): what it gave back stays given back, and what it will take
        /// again stays known.
        /// </summary>
        void Suspend();

        /// <summary>Take again what <see cref="Suspend"/> gave back; nothing where it was not suspended.</summary>
        void Resume();
    }
}
