using System.Collections.Generic;
using M3trik.Unitytk.Core;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// A plan against a scene, step by step and row by row: whether each row can be built, what a
    /// build would change for it (the dry run's own list, so a row with none is built) and what it
    /// holds in the scene. The management view a person reads in the Manifest window and an agent
    /// reads from code.
    /// </summary>
    public sealed class ManifestAssessment
    {
        /// <summary>Whether a row can be built.</summary>
        public enum State
        {
            /// <summary>Every node and take it names is in the scene, and a builder has its behavior.</summary>
            Ready,
            /// <summary>Something it names is missing (or the plan could not read it): <see cref="Row.problems"/> say what.</summary>
            Unresolved,
            /// <summary>No builder has its behavior yet (<see cref="ManifestBehaviors"/>).</summary>
            Unsupported,
            /// <summary>The delivery's own build made it (<c>PlanAction.upstream</c>): left to it, never built here.</summary>
            Upstream,
        }

        /// <summary>One action of the plan: a row of its step.</summary>
        public sealed class Row
        {
            /// <summary>The step it is in.</summary>
            public string step;
            /// <summary>Its place among the step's actions, from 0.</summary>
            public int index;
            /// <summary>Its behavior.</summary>
            public string behavior;
            /// <summary>The row as the sheet writes it.</summary>
            public string text;
            /// <summary>The objects it acts on, as the sheet names them (those an edit left out aside).</summary>
            public List<string> targets = new List<string>();
            /// <summary>The objects of the sheet's row this project leaves it out for (<see cref="Manifest.edits"/>).</summary>
            public List<string> dropped = new List<string>();
            /// <summary>The objects the delivery's own build made it on (<c>PlanAction.upstream</c>): left to it, never built here.</summary>
            public List<string> upstream = new List<string>();
            /// <summary>Which of its step's rows in the sheet it is, from 0; -1 for one this project added.</summary>
            public int source = -1;
            /// <summary>Turned off in this project (its behavior, or every object it names): a build leaves it out.</summary>
            public bool off;
            /// <summary>Added in this project: the sheet has no such row (<see cref="Manifest.edits"/>).</summary>
            public bool added;
            /// <summary>Whether it can be built.</summary>
            public State state;
            /// <summary>Why it cannot be built, as a <see cref="ManifestStatus"/>; null when it can.</summary>
            public string cause;
            /// <summary>What keeps it from being built; empty when nothing does.</summary>
            public List<string> problems = new List<string>();
            /// <summary>What may be wrong with it, which is built anyway (the plan's warnings).</summary>
            public List<string> warnings = new List<string>();
            /// <summary>What its build said of it (a spot no place was found for): built anyway.</summary>
            public List<string> notes = new List<string>();
            /// <summary>What a build changes for it: the dry run's list after an assessment (empty: built), the changes made after a build.</summary>
            public List<Change> changes = new List<Change>();
            /// <summary>What it holds in the scene: each part its builder made, or found made.</summary>
            public List<UnityEngine.Object> parts = new List<UnityEngine.Object>();
            /// <summary>Whether its step is locked: a build leaves it as it is.</summary>
            public bool locked;
            /// <summary>Whether a build just made <see cref="changes"/> (they are what it did, not what is left to do).</summary>
            public bool made;
            /// <summary>Whether the delivery is not placed in the scene yet: nothing of the row can be there.</summary>
            public bool unplaced;

            /// <summary>What a person changed in the scene on the parts it holds (<see cref="ManifestsActions.Adjustments"/>).</summary>
            public List<ManifestAdjustment> adjusted = new List<ManifestAdjustment>();

            /// <summary>Whether a build has work for it: it can be built, and is not as the plan says yet.</summary>
            public bool NeedsBuild => state == State.Ready && !locked && (unplaced || (!made && changes.Count > 0));

            /// <summary>Where it stands, as a <see cref="ManifestStatus"/>.</summary>
            public string Status
            {
                get
                {
                    if (off) return ManifestStatus.Off;
                    if (state == State.Upstream) return ManifestStatus.Upstream;
                    if (state == State.Unsupported) return ManifestStatus.UnknownBehavior;
                    if (state == State.Unresolved) return cause ?? ManifestStatus.Unbuildable;
                    if (locked) return ManifestStatus.Locked;
                    if (unplaced) return ManifestStatus.Missing;
                    if (NeedsBuild) return parts.Count == 0 ? ManifestStatus.Missing : ManifestStatus.Update;
                    if (warnings.Count + notes.Count > 0) return ManifestStatus.Warned;
                    return adjusted.Count > 0 ? ManifestStatus.Adjusted : ManifestStatus.Valid;
                }
            }
        }

        /// <summary>One step of the plan: its rows, and what the kits build for the step itself.</summary>
        public sealed class Step
        {
            /// <summary>The step's ID; empty for <see cref="shared"/>.</summary>
            public string id = "";
            /// <summary>The rows of it a build builds, in sheet order (those this project added last).</summary>
            public List<Row> rows = new List<Row>();
            /// <summary>The sheet's rows of the step this project turned off: listed, never built.</summary>
            public List<Row> off = new List<Row>();
            /// <summary>What a build changes for the step itself, beside its rows (its own object, its page, its sound).</summary>
            public List<Change> changes = new List<Change>();
            /// <summary>What the step itself holds in the scene.</summary>
            public List<UnityEngine.Object> parts = new List<UnityEngine.Object>();
            /// <summary>What its build said of it.</summary>
            public List<string> notes = new List<string>();
            /// <summary>What a person changed in the scene on the parts the step itself holds (its page, its own object).</summary>
            public List<ManifestAdjustment> adjusted = new List<ManifestAdjustment>();
            /// <summary>Whether it is locked: a build leaves it as it is.</summary>
            public bool locked;
            /// <summary>Whether a build just made <see cref="changes"/> (they are what it did, not what is left to do).</summary>
            public bool made;

            /// <summary>Whether a build has work here: a change to the step itself, or to a row of it.</summary>
            public bool NeedsBuild
            {
                get
                {
                    if (locked) return false;
                    if (!made && changes.Count > 0) return true;
                    foreach (Row row in rows)
                        if (row.NeedsBuild) return true;
                    return false;
                }
            }

            /// <summary>Whether a row of it waits on a person (<see cref="ManifestStatus.NeedsYou"/>).</summary>
            public bool NeedsYou
            {
                get
                {
                    foreach (Row row in rows)
                        if (ManifestStatus.NeedsYou(row.Status)) return true;
                    return false;
                }
            }

            /// <summary>Where it stands, as a <see cref="ManifestStatus"/>: its rows' worst, then its own.</summary>
            public string Status
            {
                get
                {
                    var all = new List<string>();
                    foreach (Row row in rows) all.Add(row.Status);
                    if (locked) all.Add(ManifestStatus.Locked);
                    else if (!made && changes.Count > 0) all.Add(parts.Count == 0 ? ManifestStatus.Missing : ManifestStatus.Update);
                    else if (notes.Count > 0) all.Add(ManifestStatus.Warned);
                    return ManifestStatus.Worst(all);
                }
            }
        }

        /// <summary>Every action, in plan order.</summary>
        public List<Row> rows = new List<Row>();

        /// <summary>Every step, in plan order, with its rows.</summary>
        public List<Step> steps = new List<Step>();

        /// <summary>What the kits build outside any step (the sequence the steps run in, a panel their pages are on).</summary>
        public Step shared = new Step();

        /// <summary>What the plan as a whole could not account for (its own issues).</summary>
        public List<string> planIssues = new List<string>();

        /// <summary>What a person changed in the scene on the parts the build made, each field with what the build gave it
        /// (<see cref="ManifestsActions.Adjustments"/>): kept by every build, put back by a revert.</summary>
        public List<ManifestAdjustment> adjusted = new List<ManifestAdjustment>();

        /// <summary>The step with <paramref name="id"/>; null where the plan has none.</summary>
        /// <param name="id">A step's ID.</param>
        public Step Find(string id)
        {
            foreach (Step step in steps)
                if (step.id == id) return step;
            return null;
        }

        /// <summary>How many actions stand at <paramref name="state"/>.</summary>
        /// <param name="state">A state.</param>
        public int Count(State state)
        {
            int n = 0;
            foreach (Row row in rows)
                if (row.state == state) n++;
            return n;
        }

        /// <summary>The behaviors no builder has yet, each with how many actions wait on it, by name.</summary>
        public SortedDictionary<string, int> Unsupported()
        {
            var counts = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            foreach (Row row in rows)
                if (row.state == State.Unsupported)
                    counts[row.behavior] = counts.TryGetValue(row.behavior, out int n) ? n + 1 : 1;
            return counts;
        }

        /// <summary>
        /// Whether a row, a step or what the steps share says <paramref name="text"/> (a build's note, a warning, a problem), or the
        /// plan as a whole does: what is said there needs saying nowhere else.
        /// </summary>
        /// <param name="text">What was said.</param>
        public bool Says(string text)
        {
            if (planIssues.Contains(text) || shared.notes.Contains(text)) return true;
            foreach (Step step in steps)
                if (step.notes.Contains(text)) return true;
            foreach (Row row in rows)
                if (row.notes.Contains(text) || row.warnings.Contains(text) || row.problems.Contains(text)) return true;
            return false;
        }

        /// <summary>How many warnings the actions carry (the plan's, and their builds' notes).</summary>
        public int Warnings()
        {
            int n = 0;
            foreach (Row row in rows) n += row.warnings.Count + row.notes.Count;
            return n;
        }

        /// <summary>What a build would do, in numbers (<see cref="BuildPlan"/>).</summary>
        public BuildPlan Plan()
        {
            var plan = new BuildPlan();
            foreach (Step step in steps)
            {
                if (step.NeedsBuild) plan.steps++;
                if (step.locked) plan.locked++;
                foreach (Row row in step.rows)
                {
                    string status = row.Status;
                    if (ManifestStatus.NeedsYou(status)) plan.needsYou++;
                    else if (status == ManifestStatus.Missing) plan.create++;
                    else if (status == ManifestStatus.Update) plan.update++;
                }
            }
            plan.shared = shared.made ? 0 : shared.changes.Count;
            return plan;
        }

        /// <summary>What a build would do, counted: the Build button's number and its plan.</summary>
        public sealed class BuildPlan
        {
            /// <summary>Steps a build changes.</summary>
            public int steps;
            /// <summary>Rows it builds for the first time.</summary>
            public int create;
            /// <summary>Rows it brings in line with the sheet.</summary>
            public int update;
            /// <summary>Steps it leaves alone, locked.</summary>
            public int locked;
            /// <summary>Rows that wait on a person: no build resolves them.</summary>
            public int needsYou;
            /// <summary>Changes it makes outside any step.</summary>
            public int shared;

            /// <summary>Whether a build has anything to do.</summary>
            public bool Any => steps > 0 || shared > 0;

            /// <summary>How many things a build has to do: the steps it changes, and one for what it changes outside them.</summary>
            public int Count => steps + (shared > 0 ? 1 : 0);

            /// <summary>The plan in a sentence, as a Build button's tooltip says it.</summary>
            public string Text()
            {
                var parts = new List<string>();
                if (create > 0) parts.Add($"builds {create} row(s)");
                if (update > 0) parts.Add($"updates {update}");
                if (shared > 0) parts.Add($"makes {shared} change(s) to what the steps share");
                string does = parts.Count == 0 ? "Nothing to build" : "Build " + string.Join(", ", parts);
                if (locked > 0) does += $"; {locked} step(s) locked";
                if (needsYou > 0) does += $"; {needsYou} row(s) wait on a sheet or delivery fix";
                return does + ".";
            }
        }

        /// <summary>One line: the counts a run reports.</summary>
        public string Summary()
        {
            int built = 0, toBuild = 0, needsYou = 0, locked = 0;
            foreach (Row row in rows)
            {
                string status = row.Status;
                if (ManifestStatus.NeedsYou(status)) needsYou++;
                else if (status == ManifestStatus.Locked) locked++;
                else if (status == ManifestStatus.Missing || status == ManifestStatus.Update) toBuild++;
                else built++;
            }
            string text = $"{steps.Count} steps, {rows.Count} rows: {built} built, {toBuild} to build, {needsYou} need you";
            if (locked > 0) text += $", {locked} locked";
            text += $"; {Warnings()} warning(s)";
            if (planIssues.Count > 0) text += $"; {planIssues.Count} sheet issue(s)";
            return text;
        }
    }
}
