using System;
using System.Globalization;
using M3trik.Unitytk.Core;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// One call per editor: <c>-executeMethod M3trik.Unitytk.Kernel.BatchServer.Run
    /// -unitytkRun &lt;run folder&gt;</c> answers that run folder (<see cref="RunFolder"/>),
    /// then the editor exits with the outcome's code -- the per-call half of
    /// pythontk's <c>BatchOperationHost</c>, as a Python host's fresh instance is.
    /// </summary>
    public static class BatchServer
    {
        /// <summary>Answer the run folder the command line names, then exit (0: done).</summary>
        public static void Run()
        {
            int code = 1;
            try
            {
                string run = CommandLine.Value("-unitytkRun");
                if (string.IsNullOrEmpty(run))
                {
                    Debug.LogError("BatchServer.Run needs -unitytkRun <run folder>");
                    return;
                }
                // A progress marker is one line the runner reads: no stack trace under it.
                Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Outcome outcome = RunFolder.Answer(run, Report);
                code = outcome.status == "done" ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError($"BatchServer.Run failed: {e}");
            }
            finally
            {
                EditorApplication.Exit(code);
            }
        }

        static void Report(double fraction, string message)
        {
            int step = (int)Math.Round(fraction * 1000);
            Debug.Log($"::progress:: {step.ToString(CultureInfo.InvariantCulture)}/1000 {message}".TrimEnd());
        }
    }
}
