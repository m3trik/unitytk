using System;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// Thrown by an operation of effect <c>code</c> when the code it was given never
    /// ran: it did not compile (each error with its line), or the compiler itself
    /// could not run (none installed, a crash, a timeout, no temp folder). Nothing
    /// changed, so the call fails (pythontk's console raises a <c>SyntaxError</c>
    /// for the same reason).
    /// </summary>
    public sealed class CompileErrorException : Exception
    {
        /// <param name="errors">The compiler's errors, each with its line, or why it could not run.</param>
        public CompileErrorException(string errors) : base(errors) { }
    }
}
