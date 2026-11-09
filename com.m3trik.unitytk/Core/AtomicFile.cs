using System;
using System.IO;

namespace M3trik.Unitytk.Core
{
    /// <summary>A file written whole or not at all: what every unitytk writer replaces a file with.</summary>
    public static class AtomicFile
    {
        /// <summary>
        /// Write <paramref name="text"/> to <paramref name="path"/> whole or not at all: a
        /// reader never sees half a document, and a write that fails leaves no temp file.
        /// </summary>
        /// <exception cref="IOException">The file could not be replaced (a reader holds it, on Windows).</exception>
        public static void Write(string path, string text)
        {
            Sweep(path);
            string part = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                File.WriteAllText(part, text);
                if (File.Exists(path)) File.Replace(part, path, null);
                else File.Move(part, path);
            }
            finally
            {
                if (File.Exists(part))
                {
                    try
                    {
                        File.Delete(part);
                    }
                    catch (IOException)
                    {
                        // held a moment by a scanner: the caller's own error is the one to see
                    }
                }
            }
        }

        /// <summary>The age at which a temp file beside <c>path</c> is a killed write's leftover.</summary>
        public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

        /// <summary>Delete the temp files a killed write left beside <paramref name="path"/> (older than <see cref="StaleAfter"/>).</summary>
        public static void Sweep(string path)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (folder == null || !Directory.Exists(folder)) return;
            foreach (string part in Directory.GetFiles(folder, Path.GetFileName(path) + ".*.part"))
            {
                try
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(part) > StaleAfter) File.Delete(part);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    // another writer's, or held: the next write sweeps it
                }
            }
        }
    }
}
