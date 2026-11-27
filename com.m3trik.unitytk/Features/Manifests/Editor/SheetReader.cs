using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// Runs the sheet's reader (the Python automation beside the package) from the editor, as a person would in a
    /// terminal: one command, its exit code and what it printed. The package itself needs only Unity; where no reader
    /// answers (no Python on the path, the automation not installed) the result says so, and the same command stays
    /// there to copy.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity has no notion of an outside tool to run but <see cref="Process"/>; the kernel's console runs the
    /// editor's own compiler the same way. A file watcher that staged on every save was weighed and left: reading a
    /// sheet again is a person's decision, as a build is.
    /// </remarks>
    internal static class SheetReader
    {
        /// <summary>How long a reader may take, in seconds, before it is stopped (a narration is rendered on its first read).</summary>
        internal const double Timeout = 600;

        /// <summary>What a run came to: whether it ended at all, its exit code and what it printed.</summary>
        internal struct Result
        {
            internal bool ran;
            internal int exitCode;
            internal string printed;
        }

        /// <summary>
        /// Run <paramref name="program"/> (found on the path as a terminal finds it: <see cref="Resolve"/>) with
        /// <paramref name="arguments"/> in <paramref name="folder"/>, a cancel checkpoint each tick. Each argument reaches the
        /// program as it is written (a path with <c>%</c>, <c>$</c>, <c>&amp;</c>, <c>^</c>, a backtick or a quote in it too): no
        /// shell reads them but a batch file's own (<c>cmd</c>, which runs a launcher's shim), handed them quoted by its rules
        /// (<see cref="BatchLine"/>).
        /// </summary>
        internal static Result Run(string program, IEnumerable<string> arguments, string folder, OpContext ctx = null, double timeout = Timeout)
        {
            bool windows = Environment.OSVersion.Platform == PlatformID.Win32NT;
            string file = windows ? Resolve(program, Environment.GetEnvironmentVariable("PATH"), Environment.GetEnvironmentVariable("PATHEXT"), File.Exists)
                                  : program;  // elsewhere the runtime searches the path itself, and a shim is a program
            bool batch = windows && IsBatch(file);
            string shell = Environment.GetEnvironmentVariable("ComSpec");
            var start = new ProcessStartInfo(batch ? string.IsNullOrEmpty(shell) ? "cmd.exe" : shell : file,
                                             batch ? BatchLine(file, arguments) : CommandLine(arguments))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            start.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            var printed = new StringBuilder();
            try
            {
                using (Process process = Process.Start(start))
                {
                    process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (printed) printed.AppendLine(e.Data); };
                    process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (printed) printed.AppendLine(e.Data); };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    var clock = Stopwatch.StartNew();
                    while (!process.WaitForExit(100))
                    {
                        bool late = clock.Elapsed.TotalSeconds > timeout;
                        try
                        {
                            if (!late) ctx?.Progress(Math.Min(0.95, clock.Elapsed.TotalSeconds / 60.0), "Reading the sheet");
                        }
                        catch (OperationCancelledException)
                        {
                            Stop(process, batch);
                            throw;
                        }
                        if (!late) continue;
                        Stop(process, batch);
                        lock (printed) return new Result { ran = false, exitCode = -1, printed = printed + $"stopped: it took over {timeout:0} s" };
                    }
                    process.WaitForExit();  // the output's last lines
                    lock (printed) return new Result { ran = true, exitCode = process.ExitCode, printed = printed.ToString() };
                }
            }
            catch (System.ComponentModel.Win32Exception failed)
            {
                return new Result { ran = false, exitCode = -1, printed = $"{program}: {failed.Message}" };  // no such program on the path
            }
        }

        /// <summary>
        /// <paramref name="arguments"/> as one command line that the runtime splits back into exactly them: each quoted where
        /// it holds a space, a quote or a backslash (or is empty), a backslash before a quote doubled. The rules of Windows'
        /// own splitter, which .NET follows on every platform, and which Mono's on macOS and Linux reads alike.
        /// </summary>
        internal static string CommandLine(IEnumerable<string> arguments)
        {
            var line = new StringBuilder();
            foreach (string argument in arguments ?? Array.Empty<string>())
            {
                if (line.Length > 0) line.Append(' ');
                string each = argument ?? "";
                if (each.Length > 0 && each.IndexOfAny(Special) < 0)
                {
                    line.Append(each);
                    continue;
                }
                line.Append('"');
                int slashes = 0;
                foreach (char c in each)
                {
                    if (c == '\\')
                    {
                        slashes++;
                        continue;
                    }
                    line.Append('\\', c == '"' ? slashes * 2 + 1 : slashes).Append(c);
                    slashes = 0;
                }
                line.Append('\\', slashes * 2).Append('"');
            }
            return line.ToString();
        }

        // What an argument is quoted for: a splitter reads it as a break, a quote or an escape.
        static readonly char[] Special = { ' ', '\t', '\n', '\v', '"', '\'', '\\' };

        /// <summary>
        /// The file <paramref name="program"/> names on Windows, found as a terminal finds it: a name with no folder in each
        /// folder of <paramref name="path"/> in turn (PATH's, quoted or not), and in each with each ending of
        /// <paramref name="extensions"/> (PATHEXT's, in its order) after it, or as named where it ends in one already. Only what
        /// runs without a shell's file association is taken: a program (<c>.exe</c>, <c>.com</c>) or a batch file (<c>.bat</c>,
        /// <c>.cmd</c>: a launcher's shim, pyenv-win's <c>python.bat</c>). The current folder is not searched, so a file planted
        /// in a project never runs as the reader. <paramref name="program"/> as named where it names a folder, or where none is
        /// found (the run then says there is no such program).
        /// </summary>
        /// <param name="program">A program's name (<c>python</c>), or its path.</param>
        /// <param name="path">The folders searched, separated by <c>;</c>.</param>
        /// <param name="extensions">The endings tried, separated by <c>;</c>; empty: none, and nothing is searched.</param>
        /// <param name="exists">Whether a file is there.</param>
        internal static string Resolve(string program, string path, string extensions, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(program) || program.IndexOfAny(Folder) >= 0) return program;
            var endings = new List<string>();
            foreach (string each in (extensions ?? "").Split(';'))
            {
                string ending = each.Trim();
                if (Array.Exists(Runnable, kind => string.Equals(kind, ending, StringComparison.OrdinalIgnoreCase))) endings.Add(ending);
            }
            string own = Path.GetExtension(program);
            if (own.Length > 0 && endings.Exists(ending => string.Equals(ending, own, StringComparison.OrdinalIgnoreCase)))
                endings = new List<string> { "" };
            foreach (string each in (path ?? "").Split(';'))
            {
                string folder = each.Trim().Trim('"').TrimEnd('\\', '/');
                if (folder.Length == 0) continue;
                foreach (string ending in endings)
                {
                    string file = folder + "\\" + program + ending;
                    if (exists(file)) return file;
                }
            }
            return program;
        }

        // What names a folder in a program's name; the endings of what runs without a shell's file association.
        static readonly char[] Folder = { '\\', '/', ':' };
        static readonly string[] Runnable = { ".com", ".exe", ".bat", ".cmd" };

        /// <summary>Whether <paramref name="file"/> is a batch file (<c>.bat</c>, <c>.cmd</c>), which <c>cmd</c> runs.</summary>
        /// <param name="file">A file's name or path.</param>
        internal static bool IsBatch(string file)
        {
            string ending = Path.GetExtension(file ?? "");
            return string.Equals(ending, ".bat", StringComparison.OrdinalIgnoreCase) || string.Equals(ending, ".cmd", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The arguments of <c>cmd</c> that run the batch file <paramref name="script"/> with exactly <paramref name="arguments"/>:
        /// <c>/d /e:on /v:off /s /c ""script" arguments"</c> (no AutoRun, extensions on for the escape below, delayed expansion
        /// off so a <c>!</c> is plain, the outer quotes taken off whole). The script and each argument holding a space, a quote
        /// or what cmd reads otherwise (<c>&amp; | &lt; &gt; ^ ( ) % ! , ; =</c> and the like) are quoted, so cmd reads each such
        /// character as written; within, a quote is doubled and the backslashes before it (and before the closing quote) are
        /// doubled, as the program the batch file hands them on to splits them back, and a <c>%</c> is written <c>%%cd:~,%</c>,
        /// an empty slice of a variable beside it that cmd expands to nothing (it expands variables inside quotes too). What the
        /// batch file does with them is its own: one that expands its arguments again (<c>call</c>) reads them anew.
        /// </summary>
        /// <param name="script">The batch file's path.</param>
        /// <param name="arguments">Its arguments.</param>
        /// <exception cref="ArgumentException">An argument holds a line break, which cmd hands no batch file whole.</exception>
        /// <remarks>Prior art: Rust's standard library runs a batch file so (its fix of the batch file argument flaw, 2024).</remarks>
        internal static string BatchLine(string script, IEnumerable<string> arguments)
        {
            var line = new StringBuilder("/d /e:on /v:off /s /c \"");
            Batch(line, script ?? "", quote: true);
            foreach (string argument in arguments ?? Array.Empty<string>())
                Batch(line.Append(' '), argument ?? "", quote: false);
            return line.Append('"').ToString();
        }

        // argument onto line as cmd reads it back for a batch file, quoted where it must be (BatchLine).
        static void Batch(StringBuilder line, string argument, bool quote)
        {
            if (argument.IndexOfAny(LineBreaks) >= 0)
                throw new ArgumentException($"'{argument}' holds a line break, which cmd hands no batch file whole: name one without it", nameof(argument));
            quote |= argument.Length == 0 || argument.IndexOfAny(BatchSpecial) >= 0;
            if (quote) line.Append('"');
            int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '"') line.Append('\\', slashes).Append('"');
                else if (c == '%') line.Append("%%cd:~,");
                slashes = c == '\\' ? slashes + 1 : 0;
                line.Append(c);
            }
            if (quote) line.Append('\\', slashes).Append('"');
        }

        // What cmd reads as other than itself outside quotes (a break, an operator, an escape, an expansion, a quote).
        static readonly char[] BatchSpecial = "\t &()[]{}^=;!'+,`~%|<>\"".ToCharArray();
        static readonly char[] LineBreaks = { '\r', '\n' };

        // Stop process; tree: with what it started (a batch file's cmd and the reader it runs, which outlives it otherwise).
        static void Stop(Process process, bool tree)
        {
            if (tree)
                try
                {
                    var kill = new ProcessStartInfo("taskkill", $"/PID {process.Id} /T /F") { UseShellExecute = false, CreateNoWindow = true };
                    using (Process killing = Process.Start(kill)) killing?.WaitForExit(5000);
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // no taskkill: the batch file's cmd alone, below
                }
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // it ended meanwhile
            }
        }

        /// <summary>
        /// What a reader's operation printed, read as its outcome (the operations' own contract, <see cref="Core.Outcome"/>:
        /// the JSON object from its first <c>{</c> to its last <c>}</c>, lines before it aside); null where it printed none
        /// (no braces, a brace never closed, or an object that is no outcome).
        /// </summary>
        internal static Outcome Outcome(string printed)
        {
            int open = printed == null ? -1 : printed.IndexOf('{');
            int close = printed == null ? -1 : printed.LastIndexOf('}');
            if (open < 0 || close < open) return null;
            try
            {
                return Core.Outcome.Read(Json.Parse(printed.Substring(open, close - open + 1)));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
