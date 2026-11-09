using System;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;

namespace M3trik.Unitytk.Carrier
{
    /// <summary>
    /// The records audit: every scene record the project's models carry that this
    /// unitytk would refuse, because another version wrote it. A record bump is a
    /// major version, so this lists, before a pin moves, each file to re-export.
    /// </summary>
    public static class Records
    {
        /// <summary>The issue code of a refused record.</summary>
        public const string RefusedCode = "unity.records.refused";

        /// <summary>
        /// Every record the models <paramref name="assetPaths"/> (default: every FBX
        /// under <c>Assets/</c>) carry at a version this unitytk does not read.
        /// Reads only: each is also an issue on <paramref name="ctx"/>.
        /// </summary>
        /// <param name="assetPaths">The models to audit, as asset paths; null: every FBX in the project.</param>
        /// <param name="records">Where the records come from; default: the project's provider.</param>
        /// <param name="ctx">The run's context, for the issues; null returns them and writes each to the Console.</param>
        /// <returns>Each refused record, by model and key.</returns>
        public static List<RecordRefusal> Audit(string[] assetPaths = null, IRecordSource records = null, OpContext ctx = null)
        {
            if (records == null) records = Provided.One<IRecordSource>();
            SortedDictionary<string, int> read = ReadsRecordAttribute.Read(AppDomain.CurrentDomain.GetAssemblies());
            string[] models = assetPaths ?? AssetDatabase.FindAssets("t:Model", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(Imports.IsDelivery)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
            var refused = new List<RecordRefusal>();
            for (int i = 0; i < models.Length; i++)
            {
                ctx?.Progress((double)i / Math.Max(1, models.Length), models[i]);
                foreach (KeyValuePair<string, int> reader in read)
                {
                    string json = records.Record(models[i], reader.Key);
                    if (string.IsNullOrEmpty(json)) continue;
                    int version = VersionOf(json);
                    if (version == reader.Value) continue;
                    var refusal = new RecordRefusal
                    {
                        assetPath = models[i], key = reader.Key, version = version, supported = reader.Value,
                        message = UnitytkRecords.Refusal(reader.Key, version, reader.Value, models[i]),
                    };
                    refused.Add(refusal);
                    ctx.Raise(RefusedCode, refusal.message, "warning", models[i]);
                }
            }
            return refused;
        }

        static int VersionOf(string json)
        {
            try
            {
                return UnitytkRecords.VersionOf(Json.Parse(json, false));
            }
            catch (FormatException)
            {
                return -1;
            }
        }
    }
}
