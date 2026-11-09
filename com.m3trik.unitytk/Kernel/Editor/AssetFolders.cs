using System;
using System.Text;
using UnityEditor;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The folders an action writes its assets into: a name made safe for any OS, and a
    /// folder made with every missing parent.
    /// </summary>
    /// <remarks>
    /// Prior art: <c>Path.GetInvalidFileNameChars</c> is the running OS's set (on Linux
    /// and macOS only '/' and NUL), so a name an editor there writes can be one Windows
    /// refuses, and a project saved on one OS breaks on another; the set here is fixed,
    /// Windows' (the strictest of the three). <c>AssetDatabase.CreateFolder</c> makes one
    /// level, so <see cref="Ensure"/> walks the path. A helper two features' actions
    /// shared (Lightmaps, ShadowPlane), moved down.
    /// </remarks>
    public static class AssetFolders
    {
        /// <summary>The characters no file name may hold on some OS: <c>\ / : * ? " &lt; &gt; |</c>, then control characters.</summary>
        static readonly char[] Refused = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary>
        /// <paramref name="name"/> with each character some OS refuses in a file name (the
        /// fixed set <c>\ / : * ? " &lt; &gt; |</c> and the control characters below space)
        /// as '_'; "" for null.
        /// </summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var safe = new StringBuilder(name.Length);
            foreach (char c in name) safe.Append(c < ' ' || Array.IndexOf(Refused, c) >= 0 ? '_' : c);
            return safe.ToString();
        }

        /// <summary>
        /// Make the project folder <paramref name="folder"/> (<c>Assets/...</c>, either
        /// separator) and each missing parent; nothing where it is already there.
        /// </summary>
        public static void Ensure(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            folder = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = "";
            foreach (string part in folder.Split('/'))
            {
                string next = parent.Length == 0 ? part : parent + "/" + part;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, part);
                parent = next;
            }
        }
    }
}
