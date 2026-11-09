using System;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>The editor's own command line, read as the runner wrote it.</summary>
    internal static class CommandLine
    {
        /// <summary>The value after <paramref name="name"/> (<c>-unitytkServe &lt;folder&gt;</c>), or null.</summary>
        public static string Value(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
