using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Core;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The catalog of this editor's operations: pythontk's
    /// <c>OperationCatalogDocument</c>, one entry per <see cref="OperationAttribute"/>
    /// method, written as Python writes a catalog, so the committed
    /// <c>editor_operations.json</c> reads (and diffs) like every other one.
    /// </summary>
    public static class Catalog
    {
        /// <summary>The catalog's provider: the package that ships the operations.</summary>
        public const string Provider = "com.m3trik.unitytk";

        /// <summary>The newest catalog format this side writes.</summary>
        public const int Format = 1;

        /// <summary>Every registered operation's entry, by id.</summary>
        public static OperationCatalogDocument Build() => new OperationCatalogDocument
        {
            format = Format,
            provider = Provider,
            operations = OperationRegistry.All.Values
                .OrderBy(op => op.Id, StringComparer.Ordinal)
                .Select(OperationRegistry.Describe)
                .ToList(),
        };

        /// <summary>Write the catalog to <paramref name="path"/> (indented, as Python's <c>json.dumps(indent=2)</c>).</summary>
        public static void Write(string path)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            AtomicFile.Write(path, Json.Write(Build().ToJson(), 2) + "\n");
        }

        /// <summary>
        /// <c>-executeMethod</c> entry: write the catalog to <c>-unitytkCatalog &lt;path&gt;</c>,
        /// then exit 0 (1 when it could not, or a declaration was left out).
        /// </summary>
        public static void WriteFromCommandLine()
        {
            int code = 1;
            try
            {
                string path = CommandLine.Value("-unitytkCatalog");
                if (string.IsNullOrEmpty(path))
                {
                    Debug.LogError("Catalog: pass -unitytkCatalog <path.json>");
                    return;
                }
                foreach (string problem in OperationRegistry.Problems) Debug.LogError($"Catalog: {problem}");
                Write(path);
                Debug.Log($"Catalog: wrote {OperationRegistry.All.Count} operations to {path}");
                code = OperationRegistry.Problems.Count == 0 ? 0 : 1;
            }
            catch (Exception e)
            {
                Debug.LogError($"Catalog: {e}");
            }
            finally
            {
                EditorApplication.Exit(code);
            }
        }
    }
}
