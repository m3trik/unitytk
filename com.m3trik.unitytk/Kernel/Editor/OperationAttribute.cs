using System;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// Marks a static method as an operation: one named, typed action people and
    /// agents call alike (pythontk's <c>Operation</c>, declared where the code is).
    /// Its parameters are the arguments (each documented with
    /// <c>[System.ComponentModel.Description]</c>), an <see cref="OpContext"/>
    /// parameter gives it a dry run, and its return value is the result.
    /// Internal: only the kernel declares operations (its console, scene view and
    /// doctor), as each Python host declares only its console and view; a
    /// feature's actions are public methods that code reaches through
    /// <c>unity.csharp.exec</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class OperationAttribute : Attribute
    {
        /// <param name="id">
        /// <c>unity.&lt;area&gt;.&lt;action&gt;</c> in lower case, at most 34 characters:
        /// published the day it ships (saved data and agent prompts name it).
        /// </param>
        /// <param name="summary">One sentence: what it does, as a tool's description.</param>
        public OperationAttribute(string id, string summary)
        {
            Id = id;
            Summary = summary;
        }

        /// <summary>The id every front end calls it by.</summary>
        public string Id { get; }

        /// <summary>What it does, in one sentence.</summary>
        public string Summary { get; }

        /// <summary>The widest change it may make (default: none).</summary>
        public Effect Effect { get; set; } = Effect.Read;

        /// <summary>The label a menu or palette shows; default: the summary.</summary>
        public string Title { get; set; }
    }
}
