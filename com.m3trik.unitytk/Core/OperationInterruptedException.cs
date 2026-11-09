using System;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// Thrown when a cancelled run reached no checkpoint in time and was interrupted
    /// (pythontk's <c>KeyboardInterrupt</c> after the cancel). Unlike a cancel at a
    /// checkpoint, what it changed before is unknown, so a live change ends
    /// <c>partial</c> (<see cref="RunRules"/>).
    /// </summary>
    public sealed class OperationInterruptedException : Exception
    {
        /// <param name="reason">Why it was interrupted.</param>
        public OperationInterruptedException(string reason) : base(reason) { }
    }
}
