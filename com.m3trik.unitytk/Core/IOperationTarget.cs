using System;
using System.Collections.Generic;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// The operation a request names, as the editor found it: what
    /// <see cref="RunRules"/> calls, so the rules run without an editor.
    /// </summary>
    public interface IOperationTarget
    {
        /// <summary>Whether it takes an operation context: it keeps a journal, and can plan.</summary>
        bool TakesContext { get; }

        /// <summary>
        /// Call it with <paramref name="args"/>; never throws: what it raised rides on
        /// the result (<see cref="Invocation.Error"/>).
        /// </summary>
        /// <param name="args">The request's arguments (a JSON object's tree).</param>
        /// <param name="dryRun">Whether it only plans.</param>
        /// <param name="cancelled">Whether the caller cancelled (its checkpoints ask).</param>
        Invocation Invoke(IDictionary<string, object> args, bool dryRun, Func<bool> cancelled);
    }
}
