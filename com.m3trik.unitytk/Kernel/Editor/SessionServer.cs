using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using M3trik.Unitytk.Core;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The editor a session keeps: started with <c>-unitytkServe &lt;session folder&gt;</c>
    /// (and <c>-unitytkOwner &lt;pid&gt;</c>), it is that folder's pythontk
    /// <c>JobQueue</c> worker -- each job a run folder it answers
    /// (<see cref="RunFolder"/>), in order, on the main thread, between editor
    /// updates -- until asked to stop, its lease lapses or its owner is gone.
    /// </summary>
    /// <remarks>
    /// A changed script never ends it: before each job it refreshes the asset
    /// database, so changed scripts compile and the domain reloads in place, and
    /// this class re-arms itself (<see cref="InitializeOnLoadAttribute"/>) with its
    /// state in <see cref="SessionState"/>; a job is never taken mid-compile. An
    /// import worker inherits the command line too and never serves.
    /// Prior art: Unity's own <c>-executeMethod</c> runs one method and its
    /// command-line test runner one suite, each a fresh editor (tens of seconds);
    /// mcp-unity and similar servers listen on a port in an editor a person
    /// opened, which the session-safety rule forbids. A file queue the tooling's
    /// own editor serves needs neither.
    /// </remarks>
    [InitializeOnLoad]
    public static class SessionServer
    {
        const string FolderKey = "M3trik.Unitytk.Kernel.SessionServer.Folder";
        const string StartedKey = "M3trik.Unitytk.Kernel.SessionServer.Started";
        // The queue's layout, generated from pythontk's JobQueue (QueueLayout.g.cs), never retyped.
        const string Worker = QueueLayout.Worker, Stop = QueueLayout.Stop, Lease = QueueLayout.Lease;
        const string Queued = QueueLayout.Queued, Running = QueueLayout.Running, Done = QueueLayout.Done;
        const string Failed = QueueLayout.Failed, InstanceLockFile = "instance.lock";
        const string ServedBy = "served_by";
        /// <summary>Seconds it may wait for a lock another instance still holds.</summary>
        const double LockWait = 60;
        /// <summary>Seconds between two looks at the queue: about 40 a second, not every editor update.</summary>
        const double TickEvery = 0.025;

        static string _folder;
        static int _owner;
        static bool _serving, _stopping, _busy, _ended;
        static double _lockDeadline, _ownerChecked, _started, _lastTick;
        static string _refreshedFor;
        static string _job;
        static Timer _beat;
        static readonly object Gate = new object();

        static SessionServer()
        {
            string folder = CommandLine.Value("-unitytkServe");
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsAssetImportWorkerProcess()) return;
            _folder = Path.GetFullPath(folder);
            int.TryParse(CommandLine.Value("-unitytkOwner") ?? "", NumberStyles.Integer, CultureInfo.InvariantCulture, out _owner);
            AssemblyReloadEvents.beforeAssemblyReload += StopBeating;
            EditorApplication.update += Tick;
        }

        /// <summary>The session folder this editor serves, or null when it serves none.</summary>
        public static string Folder => _folder;

        static void Tick()
        {
            if (_busy || Now() - _lastTick < TickEvery) return;
            _lastTick = Now();
            _busy = true;
            try
            {
                if (!_serving) Begin();
                else Step();
            }
            catch (Exception e)
            {
                Debug.LogError($"unitytk session server: {e}");
            }
            finally
            {
                _busy = false;
            }
        }

        // -- becoming the worker ----------------------------------------------------
        static void Begin()
        {
            bool rearmed = SessionState.GetString(FolderKey, "") == _folder && InstanceLock.Held;
            if (!rearmed)
            {
                if (_lockDeadline == 0) _lockDeadline = Now() + LockWait;  // from the first try, not the domain's load
                Directory.CreateDirectory(_folder);
                if (!InstanceLock.TryTake(Path.Combine(_folder, InstanceLockFile)))
                {
                    if (Now() > _lockDeadline)
                    {
                        Debug.LogError("another instance serves this session");
                        EditorApplication.update -= Tick;
                        EditorApplication.Exit(1);
                    }
                    return;  // the one before it is still letting go
                }
                SessionState.SetString(FolderKey, _folder);
                SessionState.SetString(StartedKey, Now().ToString("R", CultureInfo.InvariantCulture));
                _started = Now();
                FirstArm();
            }
            // Read here, on the main thread: the heartbeat's thread may not ask SessionState.
            _started = double.TryParse(SessionState.GetString(StartedKey, ""), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out double s) ? s : Now();
            _serving = true;
            StartBeating();
            Beat(QueueLayout.StateServing);
        }

        static void FirstArm()
        {
            Delete(Path.Combine(_folder, Stop));
            double? left = LeaseLeft();
            if (left.HasValue && left.Value <= 0) Delete(Path.Combine(_folder, Lease));
            foreach (string running in Directory.GetFiles(_folder, "*" + Running))
            {
                string id = Path.GetFileName(running);
                id = id.Substring(0, id.Length - Running.Length);
                Write(Path.Combine(_folder, id + Failed),
                      new Dictionary<string, object> { ["error"] = "the worker running it died before it finished" });
                Delete(running);
            }
            Beat(QueueLayout.StateStarting);
        }

        // -- serving ----------------------------------------------------------------
        static void Step()
        {
            if (ShouldStop())
            {
                End();
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string id = NextJob();
            if (id == null) return;
            if (_refreshedFor != id)
            {
                // Changed scripts compile, and the domain reloads in place, before the job.
                _refreshedFor = id;
                AssetDatabase.Refresh();
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            }
            string queued = Path.Combine(_folder, id + Queued), running = Path.Combine(_folder, id + Running);
            try
            {
                File.Move(queued, running);
            }
            catch (IOException)
            {
                return;  // taken or withdrawn meanwhile
            }
            _job = id;
            Beat(null);
            var started = Stopwatch.StartNew();
            try
            {
                var job = Json.Parse(File.ReadAllText(running)) as IDictionary<string, object>;
                string run = job != null && job.TryGetValue("run", out object r) ? r as string : null;
                if (string.IsNullOrEmpty(run)) throw new FormatException("the job names no run folder");
                File.WriteAllText(Path.Combine(run, ServedBy), Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
                Outcome outcome = RunFolder.Answer(run, (fraction, message) => Progress(run, fraction, message));
                Write(Path.Combine(_folder, id + Done), new Dictionary<string, object>
                {
                    ["result"] = outcome.ToJson(),
                    ["seconds"] = Math.Round(started.Elapsed.TotalSeconds, 3),
                });
            }
            catch (Exception e)
            {
                Write(Path.Combine(_folder, id + Failed), new Dictionary<string, object> { ["error"] = e.ToString() });
            }
            Delete(running);
            _job = null;
            Beat(null);
        }

        static string NextJob()
        {
            string first = Directory.GetFiles(_folder, "*" + Queued).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
            return first?.Substring(0, first.Length - Queued.Length);
        }

        static bool ShouldStop()
        {
            if (_stopping || File.Exists(Path.Combine(_folder, Stop))) return true;
            double? left = LeaseLeft();
            if (left.HasValue && left.Value <= 0 && NextJob() == null) return true;
            if (_owner > 0 && Now() - _ownerChecked > 2)
            {
                _ownerChecked = Now();
                if (!Alive(_owner)) _stopping = true;
            }
            return _stopping;
        }

        static void End()
        {
            EditorApplication.update -= Tick;
            StopBeating();
            lock (Gate) _ended = true;  // a beat already on its way never writes serving over stopped
            Beat(QueueLayout.StateStopped);
            Delete(Path.Combine(_folder, Stop));
            InstanceLock.Release();
            SessionState.EraseString(FolderKey);
            EditorApplication.Exit(0);
        }

        // -- the heartbeat ----------------------------------------------------------
        static void StartBeating()
        {
            StopBeating();
            _beat = new Timer(_ => Beat(null), null, 500, 500);
        }

        static void StopBeating()
        {
            _beat?.Dispose();
            _beat = null;
        }

        static void Beat(string state)
        {
            lock (Gate)
            {
                if (_ended && state == null) return;
                double started = _started > 0 ? _started : Now();
                string current = state ?? (_serving ? QueueLayout.StateServing : QueueLayout.StateStarting);
                var worker = new Dictionary<string, object>
                {
                    ["pid"] = Process.GetCurrentProcess().Id,
                    ["host"] = Environment.MachineName,
                    ["state"] = current,
                    ["job"] = _job,
                    ["started"] = started,
                    ["beat"] = Now(),
                };
                try
                {
                    Write(Path.Combine(_folder, Worker), worker);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // a reader held it open (Windows): the next beat lands
                }
            }
        }

        // -- files ------------------------------------------------------------------
        static void Progress(string run, double fraction, string message)
        {
            try
            {
                AtomicFile.Write(Path.Combine(run, RunRules.ProgressFile),
                                     Json.Write(new List<object> { fraction, message ?? "" }));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // the caller held it open (Windows): the next report lands
            }
        }

        static double? LeaseLeft()
        {
            try
            {
                string path = Path.Combine(_folder, Lease);
                if (!File.Exists(path)) return null;
                // As pythontk reads it: a lease with no "until" has lapsed.
                var lease = Json.Parse(File.ReadAllText(path)) as IDictionary<string, object>;
                if (lease == null) return 0 - Now();
                lease.TryGetValue("until", out object until);
                return (until is double || until is long || until is decimal
                        ? Convert.ToDouble(until, CultureInfo.InvariantCulture) : 0) - Now();
            }
            catch (Exception)
            {
                return null;  // mid-write: read again next update
            }
        }

        static void Write(string path, object tree) => AtomicFile.Write(path, Json.Write(tree));

        static void Delete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // held a moment (Windows): best effort
            }
        }

        static bool Alive(int pid)
        {
            try
            {
                return !Process.GetProcessById(pid).HasExited;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
            catch (Win32Exception)
            {
                return true;  // it exists, but this process may not ask about it: not gone
            }
        }

        static double Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }
}
