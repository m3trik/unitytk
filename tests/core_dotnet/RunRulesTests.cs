using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Core;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>
    /// The run-folder rules held against pythontk's: the statuses
    /// (<c>PureOperationHost.run</c>), the scene rules (<c>_answer_request</c>,
    /// <c>_settle</c>) and their messages, word for word.
    /// </summary>
    public class RunRulesTests
    {
        /// <summary>Scenes a test sets up: what is open, dirty, saved.</summary>
        sealed class Scenes : ISceneFiles
        {
            public readonly List<string> Opened = new List<string>(), Saved = new List<string>();
            public bool Dirty, Play;
            public int Marks;
            public Exception OpenError, SaveError;

            public void Open(IList<string> paths)
            {
                if (OpenError != null) throw OpenError;
                Opened.AddRange(paths.Where(File.Exists));
            }

            public IList<string> Modified() => Dirty ? Opened.Select(Path.GetFullPath).ToList() : new List<string>();
            public bool Unsaved() => Dirty;
            public bool Playing() => Play;

            public void Changed()
            {
                Marks++;
                Dirty = true;
            }

            public void Save(IList<string> paths)
            {
                if (SaveError != null) throw SaveError;
                Saved.AddRange(paths);
                Dirty = false;
            }
        }

        /// <summary>An operation a test scripts.</summary>
        sealed class Target : IOperationTarget
        {
            public bool TakesContext { get; set; }
            public Func<bool, Invocation> Body = dry => new Invocation { Result = 42L };
            public int Calls;

            public Invocation Invoke(IDictionary<string, object> args, bool dryRun, Func<bool> cancelled)
            {
                Calls++;
                return Body(dryRun);
            }
        }

        string _root, _run, _scene, _project;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "ptk", "unitytk_core_tests", Guid.NewGuid().ToString("N"));
            _run = Path.Combine(_root, "run");
            _project = Path.Combine(_root, "Project");
            Directory.CreateDirectory(_run);
            Directory.CreateDirectory(Path.Combine(_project, "Assets"));
            _scene = Path.Combine(_project, "Assets", "Main.unity");
            File.WriteAllText(_scene, "scene");
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_root, true);

        Outcome Answer(string effect, Target target, Scenes files, ref bool held, bool dryRun = false,
                       bool scenes = true, bool entryDryRun = false, object format = null)
        {
            var request = new Dictionary<string, object>
            {
                ["format"] = format ?? 1L, ["op"] = "unity.test.op", ["args"] = new Dictionary<string, object>(),
                ["dry_run"] = dryRun, ["scenes"] = scenes ? new List<object> { _project, _scene } : new List<object>(),
            };
            var entry = new Dictionary<string, object>
            {
                ["id"] = "unity.test.op", ["title"] = "Test", ["effect"] = effect, ["host"] = "unity",
                ["target"] = "Tests.Op", ["dry_run"] = entryDryRun,
                ["args"] = new Dictionary<string, object> { ["type"] = "object" },
            };
            File.WriteAllText(Path.Combine(_run, RunRules.RequestFile), Json.Write(request));
            File.WriteAllText(Path.Combine(_run, RunRules.EntryFile), Json.Write(entry));
            return RunRules.Answer(_run, files, ref held, _ => target);
        }

        static Invocation Changed(Exception error = null) => new Invocation
        {
            Changes = new List<Change> { new Change { kind = "change", target = "Cube", action = "move" } },
            Error = error,
        };

        // -- faults -------------------------------------------------------------------

        [Test]
        public void A_stale_outcome_in_the_run_folder_is_gone_before_the_run_starts()
        {
            string outcome = Path.Combine(_run, RunRules.OutcomeFile);
            File.WriteAllText(outcome, "{\"op\": \"an.earlier.run\", \"status\": \"done\"}");
            bool held = false, seen = true;
            var target = new Target { Body = dry => { seen = File.Exists(outcome); return new Invocation { Result = 1L }; } };
            Outcome o = Answer("read", target, new Scenes(), ref held, scenes: false, entryDryRun: true);
            Assert.That(seen, Is.False, "a killed run leaves no outcome, never an earlier run's");
            Assert.That(o.op, Is.EqualTo("unity.test.op"));
            Assert.That(File.ReadAllText(outcome), Does.Contain("unity.test.op"));
        }

        [Test]
        public void In_play_mode_a_call_that_opens_or_saves_scenes_is_refused_with_the_way_out()
        {
            var files = new Scenes { Play = true };
            bool held = false;
            var target = new Target { TakesContext = true, Body = dry => Changed() };
            Outcome o = Answer("state", target, files, ref held, entryDryRun: true);
            Assert.That(o.status, Is.EqualTo("failed"));
            Assert.That(target.Calls, Is.EqualTo(0), "it never ran");
            Assert.That(files.Opened, Is.Empty);
            Assert.That(o.error, Does.Contain("play mode"));
            Assert.That(o.issues.Single().code, Is.EqualTo(RunRules.PlayModeCode));
            Assert.That(o.issues.Single().fix.op, Is.EqualTo("unity.csharp.exec"));
            Assert.That((string)o.issues.Single().fix.args["code"], Does.Contain("ExitPlaymode"));
        }

        [Test]
        public void In_play_mode_code_and_reads_still_run_with_a_warning()
        {
            var files = new Scenes { Play = true };
            bool held = false;
            Outcome o = Answer("code", new Target(), files, ref held, scenes: false);
            Assert.That(o.status, Is.EqualTo("done"));
            Assert.That(files.Marks, Is.EqualTo(0), "nothing is marked changed: play mode's edits are lost");
            Issue warning = o.issues.Single();
            Assert.That(warning.code, Is.EqualTo(RunRules.PlayModeCode));
            Assert.That(warning.severity, Is.EqualTo("warning"));
            o = Answer("read", new Target(), files, ref held, scenes: false, entryDryRun: true);
            Assert.That(o.status, Is.EqualTo("done"));
            Assert.That(o.issues.Single().code, Is.EqualTo(RunRules.PlayModeCode));
            o = Answer("state", new Target { TakesContext = true, Body = dry => Changed() }, files, ref held,
                       dryRun: true, scenes: false, entryDryRun: true);
            Assert.That(o.status, Is.EqualTo("done"), "a plan saves nothing, so it runs");
        }

        // -- statuses -----------------------------------------------------------------

        [Test]
        public void A_read_is_done_saves_nothing_and_holds_nothing()
        {
            var files = new Scenes();
            bool held = false;
            Outcome o = Answer("read", new Target(), files, ref held, entryDryRun: true);
            Assert.That((o.status, o.result, o.error), Is.EqualTo(("done", (object)42L, (string)null)));
            Assert.That(files.Saved, Is.Empty);
            Assert.That(held, Is.False);
            Assert.That(Json.Parse(File.ReadAllText(Path.Combine(_run, RunRules.OutcomeFile))),
                        Is.EqualTo(Json.Parse(Json.Write(o.ToJson()))));
            Assert.That(Directory.GetFiles(_run, "*.part"), Is.Empty);
        }

        [Test]
        public void A_code_run_is_marked_before_it_runs_and_never_saved_by_the_runner()
        {
            var files = new Scenes();
            bool held = false;
            var target = new Target { Body = dry => new Invocation { Result = files.Marks } };
            Outcome o = Answer("code", target, files, ref held);
            Assert.That(o.status, Is.EqualTo("done"));
            Assert.That(o.result, Is.EqualTo(1), "marked once, before it ran");
            Assert.That(files.Saved, Is.Empty, "code saves what it saves itself");
            Assert.That(o.changes, Is.Empty);
            Assert.That(held, Is.True, "set whenever a live run changed something, scenes named or not");
        }

        [Test]
        public void A_compile_error_is_failed_with_nothing_said_unknown()
        {
            bool held = false;
            var target = new Target { Body = dry => new Invocation { Error = new CompileErrorException("snippet(1,8): error CS0103") } };
            Outcome o = Answer("code", target, new Scenes(), ref held);
            Assert.That(o.status, Is.EqualTo("failed"));
            Assert.That(o.error, Is.EqualTo("CompileErrorException: snippet(1,8): error CS0103"));
            Assert.That(held, Is.False);
        }

        [Test]
        public void A_code_run_that_raised_is_partial()
        {
            bool held = false;
            var target = new Target { Body = dry => new Invocation { Error = new InvalidOperationException("boom") } };
            Outcome o = Answer("code", target, new Scenes(), ref held);
            Assert.That(o.status, Is.EqualTo("partial"));
            Assert.That(o.error, Is.EqualTo(
                "InvalidOperationException: boom (it keeps no journal, so what it changed before failing is unknown)"));
            Assert.That(held, Is.True);
        }

        [Test]
        public void An_interrupted_code_run_is_partial_and_a_checkpoint_cancel_is_cancelled()
        {
            bool held = false;
            var interrupted = new Target { Body = dry => new Invocation { Error = new OperationInterruptedException(RunRules.Interrupted) } };
            Outcome o = Answer("code", interrupted, new Scenes(), ref held);
            Assert.That(o.status, Is.EqualTo("partial"));
            Assert.That(o.error, Is.EqualTo(
                "interrupted after the cancel: it did not end within 2 s (what it changed before is unknown)"));

            var cancelled = new Target { TakesContext = true, Body = dry => new Invocation { Error = new OperationCancelledException("cancelled") } };
            o = Answer("state", cancelled, new Scenes(), ref held, entryDryRun: true);
            Assert.That((o.status, o.error), Is.EqualTo(("cancelled", "cancelled")));
        }

        [Test]
        public void A_journal_run_raising_before_a_change_is_failed_after_one_partial()
        {
            bool held = false;
            var before = new Target { TakesContext = true, Body = dry => new Invocation { Error = new ArgumentException("bad") } };
            Assert.That(Answer("state", before, new Scenes(), ref held, entryDryRun: true).status, Is.EqualTo("failed"));

            var files = new Scenes();
            var after = new Target { TakesContext = true, Body = dry => Changed(new ArgumentException("bad")) };
            Outcome o = Answer("state", after, files, ref held, entryDryRun: true);
            Assert.That(o.status, Is.EqualTo("partial"));
            Assert.That(o.error, Is.EqualTo($"ArgumentException: bad (not saved, so as they were: {_scene})"));
            Assert.That(files.Saved, Is.Empty);
        }

        [Test]
        public void A_dry_run_of_a_change_that_cannot_plan_is_refused()
        {
            bool held = false;
            var target = new Target();
            Outcome o = Answer("state", target, new Scenes(), ref held, dryRun: true);
            Assert.That(o.status, Is.EqualTo("failed"));
            Assert.That(o.error, Is.EqualTo(
                "unity.test.op cannot plan its changes (its function takes no ctx), so it has no dry run: apply it to run it"));
            Assert.That(target.Calls, Is.Zero);
        }

        [Test]
        public void An_operation_the_editor_lacks_is_refused_naming_its_target()
        {
            bool held = false;
            var files = new Scenes();
            File.WriteAllText(Path.Combine(_run, RunRules.RequestFile),
                              "{\"format\": 1, \"op\": \"unity.test.op\", \"args\": {}, \"dry_run\": false, \"scenes\": []}");
            File.WriteAllText(Path.Combine(_run, RunRules.EntryFile),
                              "{\"id\": \"unity.test.op\", \"title\": \"T\", \"effect\": \"read\", \"target\": \"Tests.Op\", \"args\": {}}");
            Outcome o = RunRules.Answer(_run, files, ref held, _ => throw new KeyNotFoundException("no operation 'unity.test.op'"));
            Assert.That(o.error, Is.EqualTo("cannot call Tests.Op: KeyNotFoundException: no operation 'unity.test.op'"));
        }

        [Test]
        public void A_newer_request_format_is_refused()
        {
            bool held = false;
            Outcome o = Answer("read", new Target(), new Scenes(), ref held, format: 2L);
            Assert.That(o.error, Is.EqualTo(
                "the run could not start: FormatException: request format 2 is not one this kernel reads (up to 1)"));
        }

        // -- scenes -------------------------------------------------------------------

        [Test]
        public void Held_work_refuses_opening_scenes_only_while_something_is_unsaved()
        {
            var files = new Scenes { Dirty = true };
            bool held = true;
            var target = new Target();
            Outcome o = Answer("read", target, files, ref held, entryDryRun: true);
            Assert.That(o.status, Is.EqualTo("failed"));
            Assert.That(o.error, Is.EqualTo(
                "this instance holds unsaved changes (a live run since it last opened scenes changed what it holds, " +
                "and no file has them), which opening scenes would discard: save them from code, call without " +
                "scenes to work on them, or discard them from code first"));
            Assert.That(target.Calls, Is.Zero);

            files.Dirty = false;  // the code saved its work
            Assert.That(Answer("read", target, files, ref held, entryDryRun: true).status, Is.EqualTo("done"));
            Assert.That(held, Is.False, "opening scenes replaces what it held");
        }

        [Test]
        public void A_scene_that_cannot_open_refuses_the_run()
        {
            bool held = false;
            var files = new Scenes { OpenError = new ArgumentException("x is outside this editor's project") };
            Outcome o = Answer("read", new Target(), files, ref held, entryDryRun: true);
            Assert.That(o.error, Is.EqualTo("the run could not start: ArgumentException: x is outside this editor's project"));
        }

        [Test]
        public void A_finished_journaled_change_saves_its_scenes()
        {
            var files = new Scenes();
            bool held = false;
            Outcome o = Answer("state", new Target { TakesContext = true, Body = dry => Changed() }, files, ref held, entryDryRun: true);
            Assert.That(o.status, Is.EqualTo("done"));
            Assert.That(files.Saved, Is.EqualTo(new[] { _scene }), "spelled as the request spelled it");
            Assert.That(o.changes.Last().target, Is.EqualTo(_scene));
            Assert.That(o.changes.Last().action, Is.EqualTo("save the scene"));
            Assert.That(File.Exists(Path.Combine(_run, RunRules.SavingFile)), Is.False);
            Assert.That(held, Is.True);
        }

        [Test]
        public void A_dry_run_lists_the_scene_saves_but_not_the_project_folder()
        {
            bool held = false;
            var files = new Scenes();
            Outcome o = Answer("state", new Target { TakesContext = true, Body = dry => Changed() }, files, ref held,
                               dryRun: true, entryDryRun: true);
            Assert.That(o.changes.Where(c => c.kind == "write").Select(c => c.target), Is.EqualTo(new[] { _scene }));
            Assert.That(files.Saved, Is.Empty);
            Assert.That(held, Is.False);
        }

        [Test]
        public void A_scene_changed_on_disk_since_it_opened_is_never_saved_over()
        {
            var files = new Scenes();
            bool held = false;
            var target = new Target
            {
                TakesContext = true,
                Body = dry =>
                {
                    File.WriteAllText(_scene, "someone else's save");
                    File.SetLastWriteTimeUtc(_scene, DateTime.UtcNow.AddMinutes(1));
                    return Changed();
                },
            };
            Outcome o = Answer("state", target, files, ref held, entryDryRun: true);
            Assert.That(o.status, Is.EqualTo("failed"));
            Assert.That(o.error, Is.EqualTo($"{_scene} changed on disk since this run opened it, so it saved nothing " +
                                             "over it; run it again on the file as it is now"));
            Assert.That(files.Saved, Is.Empty);
        }

        [Test]
        public void A_save_that_fails_is_partial()
        {
            var files = new Scenes { SaveError = new IOException("the editor did not save Assets/Main.unity") };
            bool held = false;
            Outcome o = Answer("state", new Target { TakesContext = true, Body = dry => Changed() }, files, ref held, entryDryRun: true);
            Assert.That(o.status, Is.EqualTo("partial"));
            Assert.That(o.error, Is.EqualTo("saving the scenes failed: IOException: the editor did not save Assets/Main.unity"));
            Assert.That(File.Exists(Path.Combine(_run, RunRules.SavingFile)), Is.False);
        }

        [Test]
        public void An_atomic_write_that_fails_leaves_no_temp_file()
        {
            string path = Path.Combine(_run, "worker.json");
            File.WriteAllText(path, "{}");
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try
                {
                    AtomicFile.Write(path, "{\"state\": \"serving\"}");
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // Windows: a reader holds the file; elsewhere the replace goes through
                }
            }
            Assert.That(Directory.GetFiles(_run, "*.part"), Is.Empty);
        }
    }
}
