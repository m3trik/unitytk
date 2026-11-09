using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// Where an importer says what it found: importers run outside any action, so
    /// they have no <see cref="OpContext"/> to report to. Each line names the asset,
    /// so the Console reads the same for every feature, and the kernel can route the
    /// lines elsewhere later (a per-import journal) without an edit to any importer.
    /// </summary>
    public static class ImportLog
    {
        /// <summary>What the import did, when it is worth a line.</summary>
        public static void Info(string assetPath, string message) => Debug.Log(Line(assetPath, message));

        /// <summary>Something the import skipped or could not read, with what to do.</summary>
        public static void Warning(string assetPath, string message) => Debug.LogWarning(Line(assetPath, message));

        /// <summary>Something that leaves the import wrong, with what to do.</summary>
        public static void Error(string assetPath, string message) => Debug.LogError(Line(assetPath, message));

        static string Line(string assetPath, string message)
            => string.IsNullOrEmpty(assetPath) ? $"[unitytk] {message}" : $"[unitytk] {assetPath}: {message}";
    }
}
