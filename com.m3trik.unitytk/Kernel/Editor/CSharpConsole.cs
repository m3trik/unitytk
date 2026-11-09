using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using M3trik.Unitytk.Core;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// C# run a snippet at a time in this editor, as a console runs it: each snippet
    /// is the body of a method, compiled against every assembly the editor has
    /// loaded and run on its main thread; <c>return x;</c> hands <c>x</c> back,
    /// and what it logged comes back with it. <see cref="Vars"/> keeps values from
    /// call to call (until the domain reloads). pythontk's <c>PythonConsole</c>,
    /// spelled for C#: the same result, limits and failure message.
    /// </summary>
    /// <remarks>
    /// It has no dry run (nothing can be known of code before it runs), so it is
    /// applied, never planned (<see cref="Effect.Code"/>). A cancel stops it while it
    /// compiles (<c>cancelled</c>), and interrupts it once it runs, after
    /// <see cref="RunRules.InterruptAfter"/> s (<c>partial</c>: what it changed stays);
    /// a snippet stuck in the engine's native code is the caller's grace kill to end.
    /// Prior art: the editor's own compiler (Roslyn's <c>csc</c> on its bundled
    /// .NET, both shipped with every editor) compiles exactly what the editor
    /// does; Mono's <c>Mono.CSharp</c> evaluator is in-process but stuck at an old
    /// language version, and the Roslyn scripting API is not shipped with Unity.
    /// </remarks>
    public static class CSharpConsole
    {
        /// <summary>The values snippets keep between calls (a snippet spells it <c>Vars</c>).</summary>
        public static readonly Dictionary<string, object> Vars = new Dictionary<string, object>();

        /// <summary>The most characters of output handed back; a longer output keeps its end.</summary>
        public const int OutputMax = 20000;

        /// <summary>The most characters a value may take as JSON; a bigger one comes back as the start of its text.</summary>
        public const int ValueMax = 100000;

        /// <summary>Seconds a compile may take before it is given up.</summary>
        const double CompileTimeout = 120;

        /// <summary>What a snippet's lines are called in an error or a stack trace.</summary>
        const string FileName = "snippet";

        const string Header =
            "using System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing UnityEditor;\nusing UnityEngine;\n" +
            "using Object = UnityEngine.Object;\nusing Random = UnityEngine.Random;\n";

        static int _count;

        /// <summary>A snippet that raised: its exception, then what it logged first.</summary>
        public sealed class SnippetException : Exception
        {
            internal SnippetException(string message) : base(message) { }
        }

        /// <summary>Run <paramref name="code"/> in this editor, as the next snippet typed into a console.</summary>
        /// <returns><c>{"value", "output"}</c>: what it returned (null without a <c>return</c>) and what it logged.</returns>
        /// <exception cref="CompileErrorException">It did not compile (each error with its line), or the compiler
        /// could not run: none of it ran.</exception>
        /// <exception cref="OperationInterruptedException">It was cancelled and did not end in time.</exception>
        /// <exception cref="SnippetException">It raised.</exception>
        [Operation("unity.csharp.exec",
            "Run *code* in this editor as the body of a method, as the next snippet typed into a console.",
            Effect = Effect.Code, Title = "Run C#")]
        public static Dictionary<string, object> Run(
            [Description("C# statements (System, System.Linq, System.Collections.Generic, UnityEngine and UnityEditor " +
                         "are imported); `return x;` hands x back. `Vars` (a Dictionary<string, object>) keeps values " +
                         "for later calls until the editor's domain reloads.")]
            string code)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));
            MethodInfo run;
            try
            {
                run = Compile(code);
            }
            catch (Exception e) when (!(e is CompileErrorException || e is OperationCancelledException))
            {
                // No folder, no compiler, a crash, a timeout: before any of the code ran.
                throw new CompileErrorException($"the compiler could not run: {e.GetType().Name}: {e.Message}");
            }
            // One buffer for the log and Console.Out alike, so their lines keep their order.
            var output = new StringBuilder();
            var console = new StringWriter(output, CultureInfo.InvariantCulture) { NewLine = "\n" };
            void Logged(string message, string stack, LogType type)
            {
                console.Flush();
                output.Append(type == LogType.Log ? "" : type.ToString().ToLowerInvariant() + ": ").Append(message).Append('\n');
            }
            TextWriter stdout = Console.Out;
            object value = null;
            Exception failed = null;
            Application.logMessageReceived += Logged;
            Console.SetOut(console);
            var main = Thread.CurrentThread;
            var watch = new Timer(_ => Interrupt(main), null, 250, 250);
            try
            {
                try
                {
                    try
                    {
                        lock (Gate) _running = true;
                        value = run.Invoke(null, null);
                    }
                    finally
                    {
                        lock (Gate) _running = false;
                    }
                    Thread.Sleep(0);  // an interrupt asked for as it returned lands here, still caught
                }
                catch (TargetInvocationException e) when (e.InnerException != null && !(e.InnerException is ThreadAbortException))
                {
                    failed = e.InnerException;
                }
            }
            catch (Exception e) when (e is ThreadAbortException || e.InnerException is ThreadAbortException)
            {
                Thread.ResetAbort();
                failed = new OperationInterruptedException(RunRules.Interrupted);
            }
            finally
            {
                watch.Dispose();
                lock (Gate) _cancelSeen = 0;
                Console.SetOut(stdout);
                Application.logMessageReceived -= Logged;
            }
            if (failed is OperationInterruptedException) throw failed;
            if (failed != null) throw new SnippetException(Join(Trace(failed, run.DeclaringType), output.ToString()));
            return new Dictionary<string, object>
            {
                ["value"] = Bounded(value),
                ["output"] = Clip(output.ToString()),
            };
        }

        // -- interrupting -------------------------------------------------------------

        static readonly object Gate = new object();
        static bool _running, _cannotAbort;
        static double _cancelSeen;

        /// <summary>
        /// From a timer thread: a snippet still running <see cref="RunRules.InterruptAfter"/> s
        /// after its call was cancelled is aborted (pythontk's console does the same with a
        /// KeyboardInterrupt), so a loop that never ends does not hold the editor. A runtime
        /// with no <c>Thread.Abort</c> leaves it to the caller's grace kill.
        /// </summary>
        static void Interrupt(Thread main)
        {
            lock (Gate)
            {
                if (!_running || _cannotAbort || !RunFolder.Cancelled)
                {
                    _cancelSeen = 0;
                    return;
                }
                double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (_cancelSeen == 0) _cancelSeen = now;
                if (now - _cancelSeen < RunRules.InterruptAfter) return;
                _running = false;  // once
                try
                {
#pragma warning disable 618  // Mono's Thread.Abort is the one way into a busy main thread
                    main.Abort();
#pragma warning restore 618
                }
                catch (NotSupportedException)  // PlatformNotSupportedException: a runtime without it
                {
                    _cannotAbort = true;
                }
            }
        }

        // -- compiling ----------------------------------------------------------------

        static MethodInfo Compile(string code)
        {
            string name = $"Snippet{++_count}_{Guid.NewGuid():N}";
            string source = Header +
                            "namespace M3trik.Unitytk.Snippets\n{\n" +
                            $"public static class {name}\n{{\n" +
                            "static Dictionary<string, object> Vars => M3trik.Unitytk.Kernel.CSharpConsole.Vars;\n" +
                            "public static object Run()\n{\n" +
                            $"#line 1 \"{FileName}\"\n" + code + "\n#line hidden\n;\nreturn null;\n}\n}\n}\n";
            // The project's own Temp folder, wherever the process's working folder is.
            string folder = Path.GetFullPath(Path.Combine(UnitySceneFiles.Project, FileUtil.GetUniqueTempPathInProject()));
            Directory.CreateDirectory(folder);
            try
            {
                string src = Path.Combine(folder, name + ".cs"), dll = Path.Combine(folder, name + ".dll"),
                       pdb = Path.Combine(folder, name + ".pdb"), rsp = Path.Combine(folder, name + ".rsp");
                File.WriteAllText(src, source);
                var args = new StringBuilder();
                args.AppendLine("-nologo").AppendLine("-noconfig").AppendLine("-nostdlib+").AppendLine("-target:library")
                    .AppendLine("-langversion:9.0").AppendLine("-warn:0").AppendLine("-debug:portable")
                    .AppendLine("-unsafe+").AppendLine(Quote("-out:", dll)).AppendLine(Quote("-pdb:", pdb));
                foreach (string reference in References()) args.AppendLine(Quote("-r:", reference));
                args.AppendLine(Quote("", src));
                File.WriteAllText(rsp, args.ToString());
                string printed = Csc(rsp, folder, out int exitCode);
                if (exitCode != 0 || !File.Exists(dll))
                {
                    // Run in the folder, it names the snippet's lines snippet(N,M); never the folder.
                    printed = printed.Replace(folder + Path.DirectorySeparatorChar, "");
                    List<string> errors = printed.Split('\n').Select(l => l.Trim())
                        .Where(l => l.Contains(": error ")).Distinct().ToList();
                    if (errors.Count > 0) throw new CompileErrorException(string.Join("\n", errors));
                    throw new CompileErrorException($"the compiler failed (exit {exitCode}): {printed.Trim()}");
                }
                Assembly assembly = Assembly.Load(File.ReadAllBytes(dll), File.ReadAllBytes(pdb));
                return assembly.GetType($"M3trik.Unitytk.Snippets.{name}", true).GetMethod("Run");
            }
            finally
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (IOException)
                {
                    // the project's Temp folder goes with the editor anyway
                }
            }
        }

        /// <summary>Every assembly the editor has loaded from a file, one per name.</summary>
        static IEnumerable<string> References()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                string location;
                try
                {
                    location = assembly.Location;
                }
                catch (NotSupportedException)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(location) || !File.Exists(location)) continue;
                if (names.Add(assembly.GetName().Name)) yield return location;
            }
        }

        /// <summary>Run the editor's own compiler on <paramref name="rsp"/>; what it printed.</summary>
        static string Csc(string rsp, string folder, out int exitCode)
        {
            string data = EditorApplication.applicationContentsPath;
            bool windows = Application.platform == RuntimePlatform.WindowsEditor;
            string dotnet = Path.Combine(data, "NetCoreRuntime", windows ? "dotnet.exe" : "dotnet");
            string csc = Path.Combine(data, "DotNetSdkRoslyn", "csc.dll");
            if (!File.Exists(dotnet) || !File.Exists(csc))
                throw new FileNotFoundException($"this editor ships no compiler at {csc} (on {dotnet})");
            var start = new ProcessStartInfo(dotnet, $"\"{csc}\" \"@{rsp}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            start.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            using (Process process = Process.Start(start))
            {
                var printed = new StringBuilder();
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (printed) printed.AppendLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (printed) printed.AppendLine(e.Data); };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                var clock = Stopwatch.StartNew();
                while (!process.WaitForExit(50))
                {
                    bool cancelled = RunFolder.Cancelled;
                    if (!cancelled && clock.Elapsed.TotalSeconds < CompileTimeout) continue;
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // it ended meanwhile
                    }
                    if (cancelled) throw new OperationCancelledException("cancelled while compiling");
                    throw new TimeoutException($"the compiler took over {CompileTimeout} s");
                }
                process.WaitForExit();  // the output's last lines
                exitCode = process.ExitCode;
                lock (printed) return printed.ToString();
            }
        }

        static string Quote(string flag, string path) => $"{flag}\"{path}\"";

        // -- results ------------------------------------------------------------------

        /// <summary>
        /// <paramref name="error"/>'s text down to the snippet's own frame (none of the
        /// reflection that called it), each snippet line as <c>snippet:N</c>.
        /// </summary>
        static string Trace(Exception error, Type snippet)
        {
            string[] lines = error.ToString().Replace("\r\n", "\n").Split('\n');
            int last = Array.FindLastIndex(lines, l => l.Contains(snippet.FullName ?? snippet.Name));
            IEnumerable<string> kept = last < 0 ? lines : lines.Take(last + 1);
            // The compiler records the snippet's file under the temp folder it compiled in.
            return string.Join("\n", kept.Select(l => SnippetFile.Replace(l, "$1" + FileName))).TrimEnd();
        }

        /// <summary>A frame's "in &lt;temp folder&gt;/snippet:N" (Mono's spelling of a frame's file and line).</summary>
        static readonly System.Text.RegularExpressions.Regex SnippetFile =
            new System.Text.RegularExpressions.Regex(@"( in )[^\n]*?[\\/]" + FileName + @"(?=:\d)");

        static string Join(string message, string printed)
        {
            printed = Clip(printed).TrimEnd();
            return printed.Length == 0 ? message : $"{message}\n--- it printed first ---\n{printed}";
        }

        static string Clip(string text)
        {
            if (text.Length <= OutputMax) return text;
            int cut = text.Length - OutputMax;
            return $"[the first {cut} characters were cut]\n{text.Substring(cut)}";
        }

        static object Bounded(object value)
        {
            object tree = JsonValue.From(value);
            int size;
            try
            {
                size = Json.Write(tree).Length;
            }
            catch (FormatException)
            {
                size = ValueMax + 1;
            }
            if (size <= ValueMax) return tree;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            if (text.Length > ValueMax) text = text.Substring(0, ValueMax);
            return $"{text}... [{value.GetType().Name} cut: {size} characters as JSON]";
        }
    }
}
