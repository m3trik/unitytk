using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// One call, as pythontk's run-folder protocol carries it, answered by rules that
    /// need no editor: the C# twin of pythontk's <c>_RunFolderInternal</c>
    /// (<c>_answer</c>, <c>_answer_request</c>, <c>_settle</c>) and
    /// <c>PureOperationHost.run</c>, with the same statuses and messages. The editor
    /// hands in its scenes (<see cref="ISceneFiles"/>) and its operations
    /// (<see cref="IOperationTarget"/>); <c>dotnet test</c> holds the rules with
    /// stand-ins.
    /// </summary>
    public static class RunRules
    {
        /// <summary>The request a runner writes.</summary>
        public const string RequestFile = "request.json";

        /// <summary>The catalog entry the runner validated the request against.</summary>
        public const string EntryFile = "entry.json";

        /// <summary>The outcome this side writes.</summary>
        public const string OutcomeFile = "outcome.json";

        /// <summary>Present: the caller cancelled the call.</summary>
        public const string CancelFile = "cancel";

        /// <summary>Present while scenes are being saved, naming them (a cancel's kill waits for it).</summary>
        public const string SavingFile = "saving";

        /// <summary>A session's progress report: JSON <c>[fraction, message]</c>.</summary>
        public const string ProgressFile = "progress";

        /// <summary>The newest request format this side reads.</summary>
        public const int Format = 1;

        /// <summary>Seconds a cancelled code run has to end by itself before it is interrupted.</summary>
        public const double InterruptAfter = 2;

        /// <summary>A dry run of a change that cannot plan (<c>{0}</c>: the operation).</summary>
        public const string NoDryRun =
            "{0} cannot plan its changes (its function takes no ctx), so it has no dry run: apply it to run it";

        /// <summary>Why opening scenes is refused while the instance holds unsaved work.</summary>
        public const string Unsaved =
            "this instance holds unsaved changes (a live run since it last opened scenes changed what it " +
            "holds, and no file has them), which opening scenes would discard: save them from code, call " +
            "without scenes to work on them, or discard them from code first";

        /// <summary>The issue a call answered in play mode carries.</summary>
        public const string PlayModeCode = "unity.play_mode";

        /// <summary>Why a call that opens or saves scenes is refused in play mode.</summary>
        public const string PlayMode =
            "the editor is in play mode, where what a call changes in its scenes is lost when play ends and " +
            "nothing can be saved: exit play mode first (the issue's fix)";

        /// <summary>Why a code run was interrupted (thrown as <see cref="OperationInterruptedException"/>).</summary>
        public static string Interrupted =>
            $"interrupted after the cancel: it did not end within {InterruptAfter.ToString("g", CultureInfo.InvariantCulture)} s";

        const string NoJournal = " (it keeps no journal, so what it changed before failing is unknown)";

        /// <summary>
        /// Answer the request in <paramref name="run"/>, write its outcome there and hand
        /// it back; never throws (a request it cannot run is refused in the outcome).
        /// </summary>
        /// <param name="run">The run folder.</param>
        /// <param name="files">The editor's scenes.</param>
        /// <param name="heldWork">Whether the instance holds a live run's changes since it last opened scenes; updated.</param>
        /// <param name="resolve">The operation an entry names (throws when this editor has none).</param>
        public static Outcome Answer(string run, ISceneFiles files, ref bool heldWork,
                                     Func<OperationEntry, IOperationTarget> resolve)
        {
            string op = "?";
            bool dryRun = false;
            Outcome outcome;
            // A run folder's outcome is only ever this run's: a killed run leaves none.
            Discard(Path.Combine(run, OutcomeFile));
            try
            {
                object data = Json.Parse(File.ReadAllText(Path.Combine(run, RequestFile)));
                if (data is IDictionary<string, object> fields)
                {
                    op = fields.TryGetValue("op", out object id) && id is string s && s.Length > 0 ? s : op;
                    dryRun = fields.TryGetValue("dry_run", out object d) && d is bool b && b;
                    fields.TryGetValue("format", out object format);
                    if (!(format is long n) || n > Format)
                        throw new FormatException(
                            $"request format {Json.Write(format)} is not one this kernel reads (up to {Format})");
                }
                OpRequest request = OpRequest.Read(data) ?? throw new FormatException("the request is no JSON object");
                OperationEntry entry = OperationEntry.Read(Json.Parse(File.ReadAllText(Path.Combine(run, EntryFile))))
                                       ?? throw new FormatException("the entry is no JSON object");
                outcome = AnswerRequest(run, request, entry, files, ref heldWork, resolve);
            }
            catch (Exception e)
            {
                outcome = Refused(op, $"the run could not start: {e.GetType().Name}: {e.Message}", dryRun);
            }
            AtomicFile.Write(Path.Combine(run, OutcomeFile), Json.Write(outcome.ToJson()));
            return outcome;
        }

        /// <summary>A refusal: <c>failed</c>, nothing changed, and why.</summary>
        public static Outcome Refused(string op, string error, bool dryRun) => new Outcome
        {
            op = op, status = "failed", dry_run = dryRun, changes = new List<Change>(),
            issues = new List<Issue>(), error = error,
        };

        /// <summary>The body of <see cref="Answer"/>, the request read (pythontk's <c>_answer_request</c>).</summary>
        public static Outcome AnswerRequest(string run, OpRequest request, OperationEntry entry, ISceneFiles files,
                                            ref bool heldWork, Func<OperationEntry, IOperationTarget> resolve)
        {
            List<string> scenes = request.scenes ?? new List<string>();
            bool dryRun = request.dry_run ?? false;
            bool playing = files.Playing();
            if (playing && (scenes.Count > 0 || (!dryRun && (entry.effect == "state" || entry.effect == "files"))))
            {
                Outcome refused = Refused(request.op, PlayMode, dryRun);
                refused.issues.Add(PlayModeIssue("error"));
                return refused;
            }
            if (scenes.Count > 0 && heldWork && files.Unsaved())
                return Refused(request.op, Unsaved, dryRun);
            Dictionary<string, (long, long)> stamps;
            try
            {
                stamps = Stamps(scenes);
                files.Open(scenes);
            }
            catch (Exception e)
            {
                return Refused(request.op, $"the run could not start: {e.GetType().Name}: {e.Message}", dryRun);
            }
            if (scenes.Count > 0) heldWork = false;  // replaced by the scenes just opened
            // A live run that keeps no journal may change anything it reaches: what it
            // holds is marked changed BEFORE it runs, so a save the code makes clears the
            // mark as the editor's own save would.
            bool journal = (entry.dry_run ?? false) || entry.effect == "read";
            // In play mode nothing is marked: what a call changes there is lost when play ends.
            if (!dryRun && !journal && !playing) files.Changed();
            Outcome outcome = Run(run, request, entry, resolve);
            bool changed = !dryRun && (outcome.changes.Any(c => c.kind != "write")
                                       || (!journal && outcome.status != "failed"));
            if (changed && journal && !playing) files.Changed();  // its journal names changes the editor's flag may miss
            outcome = Settle(outcome, request, entry, files, run, stamps);
            if (changed) heldWork = true;  // whether a file now has them, the editor's own flag says
            if (playing) (outcome.issues = outcome.issues ?? new List<Issue>()).Add(PlayModeIssue("warning"));
            return outcome;
        }

        /// <summary>The play-mode issue, whose fix is the code that exits play mode.</summary>
        static Issue PlayModeIssue(string severity) => new Issue
        {
            code = PlayModeCode,
            severity = severity,
            message = severity == "error"
                ? PlayMode
                : "the editor is in play mode: what this call changed in its scenes is lost when play ends",
            fix = new OpCall
            {
                op = "unity.csharp.exec",
                args = new Dictionary<string, object> { ["code"] = "UnityEditor.EditorApplication.ExitPlaymode(); return null;" },
            },
        };

        static void Discard(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // held a moment (Windows): the atomic write below replaces it all the same
            }
        }

        /// <summary>Call the operation and say how it ended (pythontk's <c>PureOperationHost.run</c>).</summary>
        public static Outcome Run(string run, OpRequest request, OperationEntry entry,
                                  Func<OperationEntry, IOperationTarget> resolve)
        {
            string op = entry.id ?? request.op;
            bool dryRun = request.dry_run ?? false;
            IOperationTarget target;
            try
            {
                target = resolve(entry) ?? throw new KeyNotFoundException($"no operation '{op}'");
            }
            catch (Exception e)
            {
                return Refused(op, $"cannot call {entry.target}: {e.GetType().Name}: {e.Message}", dryRun);
            }
            bool takesContext = target.TakesContext;
            if (dryRun && !takesContext && entry.effect != "read")
                return Refused(op, string.Format(CultureInfo.InvariantCulture, NoDryRun, op), dryRun);
            string cancel = Path.Combine(run, CancelFile);
            Invocation ran = target.Invoke(request.args ?? new Dictionary<string, object>(), dryRun,
                                           () => File.Exists(cancel))
                             ?? new Invocation();
            bool live = !dryRun && entry.effect != "read";
            string status = "done", error = null;
            switch (ran.Error)
            {
                case null:
                    break;
                case OperationInterruptedException e:  // the cancel's interrupt: what it did is unknown
                    return new Outcome
                    {
                        op = request.op, status = live ? "partial" : "cancelled", dry_run = dryRun,
                        changes = new List<Change>(), issues = new List<Issue>(),
                        error = e.Message + (live ? " (what it changed before is unknown)" : ""),
                    };
                case OperationCancelledException e:
                    status = "cancelled";
                    error = string.IsNullOrEmpty(e.Message) ? "cancelled" : e.Message;
                    break;
                default:
                    Exception raised = ran.Error;
                    error = $"{raised.GetType().Name}: {raised.Message}";
                    bool neverRan = entry.effect == "code" && raised is CompileErrorException;
                    if (live && !takesContext && !neverRan)
                    {
                        status = "partial";
                        error += NoJournal;
                    }
                    else
                    {
                        status = ran.Changes.Count > 0 && !dryRun ? "partial" : "failed";
                    }
                    break;
            }
            return new Outcome
            {
                op = op, status = status, dry_run = dryRun, result = ran.Result,
                changes = ran.Changes ?? new List<Change>(), issues = ran.Issues ?? new List<Issue>(), error = error,
            };
        }

        /// <summary>
        /// Save the scenes a finished live run changed (a dry run lists them), and say which
        /// a run that did not finish left alone; never over a file changed on disk since it
        /// was opened (<paramref name="stamps"/>). Code saves what it saves itself, and a
        /// read saves nothing (pythontk's <c>_settle</c>).
        /// </summary>
        public static Outcome Settle(Outcome outcome, OpRequest request, OperationEntry entry, ISceneFiles files,
                                     string run, IDictionary<string, (long, long)> stamps)
        {
            List<string> scenes = request.scenes ?? new List<string>();
            if (scenes.Count == 0 || entry.effect == "read" || entry.effect == "code") return outcome;
            outcome.changes = outcome.changes ?? new List<Change>();
            bool changed = outcome.changes.Any(c => c.kind != "write");
            if (request.dry_run ?? false)
            {
                // A project folder names the project; only a scene file is saved.
                if (changed) outcome.changes.AddRange(scenes.Where(p => !Directory.Exists(p)).Select(SaveOf));
                return outcome;
            }
            if ((entry.dry_run ?? false) && !changed) return outcome;  // its journal changed no scene
            // Report each scene as the request spelled it, not as the editor does.
            var spelled = new Dictionary<string, string>();
            foreach (string p in scenes) spelled[Key(p)] = p;
            string saving = Path.Combine(run, SavingFile);
            List<string> modified;
            try
            {
                modified = files.Modified().Select(p => spelled.TryGetValue(Key(p), out string s) ? s : p).ToList();
                if (modified.Count > 0 && outcome.status != "done")
                {
                    outcome.error = $"{outcome.error} (not saved, so as they were: {string.Join(", ", modified)})";
                    return outcome;
                }
                Dictionary<string, (long, long)> now = Stamps(modified);
                List<string> moved = modified.Where(p => stamps.TryGetValue(Key(p), out var then)
                                                         && (!now.TryGetValue(Key(p), out var stamp) || stamp != then))
                                             .ToList();
                if (moved.Count > 0)
                {
                    outcome.status = "failed";
                    outcome.error = $"{string.Join(", ", moved)} changed on disk since this run opened it, so it " +
                                    "saved nothing over it; run it again on the file as it is now";
                    return outcome;
                }
                AtomicFile.Write(saving, string.Join("\n", modified));
                try
                {
                    files.Save(modified);
                }
                finally
                {
                    File.Delete(saving);
                }
            }
            catch (Exception e)
            {
                outcome.status = "partial";
                outcome.error = $"saving the scenes failed: {e.GetType().Name}: {e.Message}";
                return outcome;
            }
            outcome.changes.AddRange(modified.Select(SaveOf));
            return outcome;
        }

        static Change SaveOf(string path) => new Change { kind = "write", target = path, action = "save the scene" };

        /// <summary>A path as a key: full, one separator, and case-folded where the file system folds case.</summary>
        public static string Key(string path)
        {
            string full = Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
            return Path.DirectorySeparatorChar == '\\' ? full.ToLowerInvariant() : full;
        }

        /// <summary>Each existing file of <paramref name="paths"/> as its modification time and size.</summary>
        public static Dictionary<string, (long, long)> Stamps(IEnumerable<string> paths)
        {
            var stamps = new Dictionary<string, (long, long)>();
            foreach (string path in paths)
            {
                var info = new FileInfo(path);
                if (info.Exists) stamps[Key(path)] = (info.LastWriteTimeUtc.Ticks, info.Length);
            }
            return stamps;
        }
    }
}
