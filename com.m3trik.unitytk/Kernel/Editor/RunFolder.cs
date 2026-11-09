using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Core;
using UnityEditor;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// One call, as pythontk's run-folder protocol carries it, answered in this
    /// editor: the rules are <see cref="RunRules"/> (host-free, tested without an
    /// editor); this class hands them the editor's scenes (<see cref="UnitySceneFiles"/>),
    /// its operations (<see cref="OperationRegistry"/>) and the held-work flag, kept
    /// across domain reloads.
    /// </summary>
    public static class RunFolder
    {
        const string HeldWorkKey = "M3trik.Unitytk.Kernel.HeldWork";

        /// <summary>
        /// Answer the request in <paramref name="run"/>, write its outcome there and
        /// hand it back; never throws (a request it cannot run is refused in the outcome).
        /// </summary>
        /// <param name="run">The run folder.</param>
        /// <param name="progress">Where progress reports go.</param>
        public static Outcome Answer(string run, Action<double, string> progress)
        {
            Current = run;
            try
            {
                bool held = HeldWork;
                Outcome outcome = RunRules.Answer(run, new UnitySceneFiles(), ref held, entry => Target(entry, progress));
                HeldWork = held;
                return outcome;
            }
            finally
            {
                Current = null;
            }
        }

        /// <summary>
        /// The run folder of the call being answered, or null between calls: where an
        /// operation finds the caller's temp root (two folders up) for a file it hands back.
        /// </summary>
        public static string Current { get; private set; }

        /// <summary>
        /// Whether the call being answered was cancelled: a check for work that takes no
        /// <see cref="OpContext"/> yet waits (a compile), so it can stop between steps.
        /// </summary>
        public static bool Cancelled
        {
            get
            {
                string run = Current;
                return run != null && File.Exists(Path.Combine(run, RunRules.CancelFile));
            }
        }

        /// <summary>Whether a live run changed what this editor holds since it last opened scenes.</summary>
        public static bool HeldWork
        {
            get => SessionState.GetBool(HeldWorkKey, false);
            private set => SessionState.SetBool(HeldWorkKey, value);
        }

        static IOperationTarget Target(OperationEntry entry, Action<double, string> progress)
        {
            OperationRegistry.Operation op = OperationRegistry.Find(entry.id);
            if (op == null)
                throw new KeyNotFoundException(
                    $"no operation '{entry.id}' in this editor: the project's unitytk package is another version " +
                    "than the catalog's (unity_project.install updates it)");
            return new Registered(op, progress);
        }

        /// <summary>A registered operation, called with an <see cref="OpContext"/> where it takes one.</summary>
        sealed class Registered : IOperationTarget
        {
            readonly OperationRegistry.Operation _op;
            readonly Action<double, string> _progress;

            public Registered(OperationRegistry.Operation op, Action<double, string> progress)
            {
                _op = op;
                _progress = progress;
            }

            public bool TakesContext => _op.TakesContext;

            public Invocation Invoke(IDictionary<string, object> args, bool dryRun, Func<bool> cancelled)
            {
                var ctx = new OpContext(dryRun, cancelled, _progress);
                var ran = new Invocation { Changes = ctx.Changes, Issues = ctx.Issues };
                try
                {
                    ran.Result = JsonValue.From(OperationRegistry.Invoke(_op, args, ctx));
                }
                catch (Exception e)
                {
                    ran.Error = e;
                }
                return ran;
            }
        }
    }
}
