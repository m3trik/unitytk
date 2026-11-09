namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The widest change an operation may make, and so what undoes it: pythontk's
    /// effects, spelled for C# (the catalog writes them lower-case).
    /// </summary>
    internal enum Effect
    {
        /// <summary>Changes nothing: agents run it freely.</summary>
        Read,

        /// <summary>Changes what the editor holds (open scenes and their objects): one undo step.</summary>
        State,

        /// <summary>Writes files (assets, scenes, settings): a dry run lists them first.</summary>
        Files,

        /// <summary>Runs code: may change anything; no plan, no undo, applied only.</summary>
        Code,
    }
}
