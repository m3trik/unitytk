using System;
using System.Collections.Generic;

namespace M3trik.Unitytk.Core
{
    /// <summary>What one call of an operation did: its result, its journal and issues, or what it raised.</summary>
    public sealed class Invocation
    {
        /// <summary>What it returned, as a JSON tree (null when it raised or returns nothing).</summary>
        public object Result { get; set; }

        /// <summary>Every change it asked for: the plan in a dry run, the journal in a live one.</summary>
        public List<Change> Changes { get; set; } = new List<Change>();

        /// <summary>The problems it found.</summary>
        public List<Issue> Issues { get; set; } = new List<Issue>();

        /// <summary>What it raised, or null when it returned.</summary>
        public Exception Error { get; set; }
    }
}
