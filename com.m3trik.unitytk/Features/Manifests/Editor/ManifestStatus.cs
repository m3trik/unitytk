using System;
using System.Collections.Generic;
using M3trik.Unitytk.Core;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// The words a manifest says a step's or a row's state in: each status with its severity, its few
    /// words (what a row says without a hover) and its sentence (what it means and what to do). One
    /// vocabulary for every front end (the Manifest window, an agent's report), and the same words the
    /// sheet's other manifest uses where they mean the same (<c>not built</c>, <c>not in scene</c>,
    /// <c>to update</c>, <c>locked</c>).
    /// </summary>
    public static class ManifestStatus
    {
        /// <summary>Built, and as the plan says.</summary>
        public const string Valid = "valid";
        /// <summary>Nothing of it is in the scene yet: Build makes it.</summary>
        public const string Missing = "missing";
        /// <summary>Built, but not as the plan now says (or a part of it was taken away): Build brings it in line.</summary>
        public const string Update = "update";
        /// <summary>Its step is locked: Build leaves it as it is.</summary>
        public const string Locked = "locked";
        /// <summary>A node it names is not in the delivery placed in the scene.</summary>
        public const string MissingObject = "missing_object";
        /// <summary>A name of its matches several nodes.</summary>
        public const string AmbiguousObject = "ambiguous_object";
        /// <summary>The delivery carries no take of the name it plays.</summary>
        public const string MissingTake = "missing_take";
        /// <summary>The sheet's reader could not read the row into what it builds: a sheet fix.</summary>
        public const string Sheet = "sheet";
        /// <summary>Its builder says it cannot be built as written.</summary>
        public const string Unbuildable = "unbuildable";
        /// <summary>No builder in this editor has its behavior's name.</summary>
        public const string UnknownBehavior = "unknown_behavior";
        /// <summary>Built, with something that may be wrong.</summary>
        public const string Warned = "warned";
        /// <summary>Turned off in this project (its behavior, or this object's): a build leaves it out.</summary>
        public const string Off = "off";
        /// <summary>Added in this project: the sheet does not give the object this behavior.</summary>
        public const string Added = "added";
        /// <summary>Built, and a part it holds was changed in the scene since: kept by every build.</summary>
        public const string Adjusted = "adjusted";
        /// <summary>Built by the delivery's own build (keyed into its takes where it was made): left to it, never built here.</summary>
        public const string Upstream = "upstream";

        /// <summary>The severities, mildest first: a row's icon is its worst status's.</summary>
        public static readonly IReadOnlyList<string> Levels = new[] { "valid", "off", "locked", "edit", "info", "warn", "error" };

        static readonly Dictionary<string, (string level, string label, string help)> _table =
            new Dictionary<string, (string, string, string)>
            {
                [Valid] = ("valid", "", "Built, as the sheet says."),
                [Missing] = ("info", "not built", "Not in the scene yet: Build makes it."),
                [Update] = ("info", "to update", "The scene differs from the sheet here: Build brings it in line."),
                [Locked] = ("locked", "locked", "Locked: Build leaves this step as it is."),
                [MissingObject] = ("error", "not in scene", "A name it uses is no node of the delivery: a delivery or sheet fix; nothing is built for it."),
                [AmbiguousObject] = ("warn", "ambiguous name", "Several nodes answer to a name it uses: rename one, or write its path in the sheet."),
                [MissingTake] = ("error", "take missing", "The delivery carries no take of the name it plays: export it, or correct the sheet's take name."),
                [Sheet] = ("error", "sheet issue", "The sheet's row could not be read into what it builds: its fix is the sheet's."),
                [Unbuildable] = ("error", "incomplete row", "Its builder cannot build it as written: the row says what it lacks."),
                [UnknownBehavior] = ("warn", "unknown behavior", "No builder has this behavior's name: add one, or correct the sheet."),
                [Warned] = ("warn", "warning", "Built; something about it may be wrong."),
                [Off] = ("off", "off", "Turned off in this project: Build leaves it out."),
                [Added] = ("edit", "added here", "Added in this project: the sheet does not give the object this behavior."),
                [Adjusted] = ("edit", "adjusted here", "Built, and a part it holds was changed in the scene: every build keeps the change; Revert on the part gives it what the build gave it."),
                [Upstream] = ("valid", "in the delivery", "The delivery's own build made it (keyed into the step's takes where the delivery was made): left to it, so it is built once."),
            };

        /// <summary><paramref name="status"/>'s severity (one of <see cref="Levels"/>); <c>valid</c> for none.</summary>
        /// <param name="status">A status.</param>
        public static string Level(string status)
            => status != null && _table.TryGetValue(status, out var row) ? row.level : "valid";

        /// <summary>
        /// The severity (one of <see cref="Levels"/>) an issue an action raised is shown at: <c>error</c>, <c>info</c>, or, for
        /// <c>warning</c> and an issue that names none, <c>warn</c>.
        /// </summary>
        /// <param name="issue">An issue an action raised.</param>
        /// <exception cref="ArgumentNullException"><paramref name="issue"/> is null.</exception>
        public static string Level(Issue issue)
        {
            if (issue == null) throw new ArgumentNullException(nameof(issue), "name the issue");
            return issue.severity == "error" ? "error" : issue.severity == "info" ? "info" : "warn";
        }

        /// <summary><paramref name="status"/> in three words at most; empty for none, and for <see cref="Valid"/>.</summary>
        /// <param name="status">A status.</param>
        public static string Label(string status)
            => status != null && _table.TryGetValue(status, out var row) ? row.label : "";

        /// <summary>What <paramref name="status"/> means and what to do about it, in a sentence.</summary>
        /// <param name="status">A status.</param>
        public static string Help(string status)
            => status != null && _table.TryGetValue(status, out var row) ? row.help : "";

        /// <summary>Whether <paramref name="status"/> waits on a person: no build resolves it.</summary>
        /// <param name="status">A status.</param>
        public static bool NeedsYou(string status)
            => status == MissingObject || status == AmbiguousObject || status == MissingTake
               || status == Sheet || status == Unbuildable || status == UnknownBehavior;

        /// <summary>The status of <paramref name="statuses"/> at the worst severity (the first of equals); <see cref="Valid"/> for none.</summary>
        /// <param name="statuses">Statuses.</param>
        public static string Worst(IEnumerable<string> statuses)
        {
            string worst = Valid;
            int rank = 0;
            foreach (string status in statuses)
            {
                int r = Rank(Level(status));
                if (r > rank)
                {
                    worst = status;
                    rank = r;
                }
            }
            return worst;
        }

        static int Rank(string level)
        {
            for (int i = 0; i < Levels.Count; i++)
                if (Levels[i] == level) return i;
            return 0;
        }
    }
}
