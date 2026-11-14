// ----------------------------------------------------------------------------
// LODs: one LODGroup per LOD group a delivery's `lods` record names.
//
// A LOD group is a node <base> whose children <base>_LOD0..n are its levels,
// LOD0 the most detailed (pythontk's LodChain). Unity's FBX import reads the
// names alone: it puts ONE LODGroup on the model's root, merging every
// level-named mesh of the file by level index (an unrelated chair_LOD0 and
// table_LOD0 become one LOD0), and ignores the FBX's LodGroup thresholds
// (measured 2026-11-10, Unity 6000.3). The record carries each group's node,
// levels and switch heights, so each group gets its own LODGroup at the heights
// the DCC set, and the root's merged group keeps only what no group claims.
// ----------------------------------------------------------------------------
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using Shape = LodsRecord.Shape;

[assembly: ReadsRecord(UnitytkRecords.Lods, typeof(LodsRecord))]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Lods.Tests")]

namespace M3trik.Unitytk.Lods
{
    /// <summary>
    /// AssetPostprocessor that gives each LOD group of a delivery its own
    /// <see cref="LODGroup"/> from the <c>lods</c> record on <c>data_export</c>
    /// (<see cref="UnitytkRecords.Lods"/>), read through its generated shape
    /// (<see cref="LodsRecord"/>) at the one version it declares: the group's node
    /// bound by name (<see cref="RecordNames"/>), each level's renderers one LOD,
    /// drawn down to its height on screen, the object culled below the last.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's model importer builds a LODGroup from the <c>_LODn</c>
    /// names by itself, and that is the plug-and-play floor without this package
    /// (one asset per FBX, Unity's own convention). It was kept, not replaced:
    /// this only regroups what a scene FBX holds several of, and sets the heights
    /// the FBX states but Unity does not read. Unity's Mesh LOD (6.2+) is a
    /// different, index-buffer mechanism Unity advises against combining with a
    /// LODGroup; nothing here enables it.
    /// </remarks>
    public class LodsImporter : AssetPostprocessor
    {
        const string Tag = "Lods";

        /// <summary>Bump to reimport every model when what the import makes changes.</summary>
        const uint VERSION = 1;

        /// <inheritdoc/>
        public override uint GetVersion() => VERSION;

        static readonly Dictionary<string, string> _pending = new Dictionary<string, string>();

        static IRecordSource Records => Provided.One<IRecordSource>();

        bool ShouldProcess() => Imports.IsDelivery(assetPath);

        void OnPreprocessModel() => _pending.Remove(assetPath);

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] propNames, object[] values)
        {
            if (!ShouldProcess()) return;
            IRecordSource records = Records;
            if (records.TryGetRecord(propNames, values, UnitytkRecords.Lods, out string json))
                records.Collect(_pending, assetPath, go, json);
        }

        void OnPostprocessModel(GameObject root)
        {
            _pending.TryGetValue(assetPath, out string json);
            _pending.Remove(assetPath);
            Import(root, json, assetPath);
        }

        /// <summary>The import's work on <paramref name="root"/> where it is a delivery
        /// (<see cref="Imports.IsDelivery"/>); the groups built.</summary>
        internal static int Import(GameObject root, string json, string assetPath)
        {
            if (!Imports.IsDelivery(assetPath)) return 0;
            return Build(root, json, assetPath, Records);
        }

        /// <summary>The height level <paramref name="k"/> of <paramref name="count"/> is
        /// drawn down to with none recorded: half the last's, LOD0 at half the screen,
        /// never culled (pythontk's <c>LodChain.default_heights</c>).</summary>
        static float DefaultHeight(int k, int count) => k == count - 1 ? 0f : 0.5f * Mathf.Pow(0.5f, k);

        /// <summary>The child of <paramref name="node"/> that is level <paramref name="name"/>:
        /// exactly, else by namespace-stripped leaf, as the record binds its group
        /// (<see cref="RecordNames"/>); null when none is, or several are.</summary>
        static Transform LevelOf(Transform node, string name, out bool ambiguous)
        {
            ambiguous = false;
            Transform[] children = node.Cast<Transform>().ToArray();
            Transform exact = children.FirstOrDefault(c => c.name == name);
            if (exact != null) return exact;
            Transform[] leaves = children.Where(c => RecordNames.Leaf(c.name) == RecordNames.Leaf(name)).ToArray();
            ambiguous = leaves.Length > 1;
            return leaves.Length == 1 ? leaves[0] : null;
        }

        /// <summary>
        /// One LODGroup per group of the record <paramref name="json"/> on the model
        /// <paramref name="root"/>; the root's merged group (Unity's) keeps what no
        /// group claims, and goes when that is nothing. A group whose node or levels
        /// are not found is named and left as Unity made it. Returns the groups built.
        /// </summary>
        internal static int Build(GameObject root, string json, string assetPath, IRecordSource records)
        {
            Shape.Payload payload = records.Read(
                UnitytkRecords.Lods, json, LodsRecord.CurrentVersion, Shape.Payload.Read, Tag, assetPath);
            if (payload == null || payload.groups.Count == 0) return 0;
            var nodes = new List<Transform>(root.GetComponentsInChildren<Transform>(true));
            RecordNames.Match<Transform>[] matches = RecordNames.Resolve(
                nodes, payload.groups.ConvertAll(g => new RecordNames.Entry(g.name)),
                t => t.name, t => t.parent, root.transform);
            var claimed = new HashSet<Renderer>();
            var planned = new List<(Transform node, LOD[] lods)>();
            for (int i = 0; i < payload.groups.Count; i++)
            {
                Shape.LodGroupEntry group = payload.groups[i];
                if (matches[i].Outcome != RecordNames.Outcome.Bound)
                {
                    ImportLog.Warning(assetPath, $"LOD group {group.name}: no one node of that name " +
                                                 $"({matches[i].Outcome}); left as Unity imported it.");
                    continue;
                }
                Transform node = nodes[matches[i].Index];
                int count = group.levels.Count;
                bool fits = group.heights != null && group.heights.Length == count;
                if (!fits)
                    ImportLog.Warning(assetPath, $"LOD group {group.name}: {group.heights?.Length ?? 0} height(s) for " +
                                                 $"{count} levels; it switches at the default ladder.");
                var kept = new List<(int k, Renderer[] renderers)>();
                var missing = new List<string>();
                for (int k = 0; k < count; k++)
                {
                    Transform level = LevelOf(node, group.levels[k], out bool ambiguous);
                    if (level == null)
                    {
                        missing.Add(group.levels[k] + (ambiguous ? " (several children of that name)" : ""));
                        continue;
                    }
                    kept.Add((k, level.GetComponentsInChildren<Renderer>(true)));
                }
                if (kept.Count < 2)
                {
                    ImportLog.Warning(assetPath, $"LOD group {group.name}: fewer than two of its levels are in the " +
                                                 "file; left as Unity imported it.");
                    continue;
                }
                if (missing.Count > 0)
                    ImportLog.Warning(assetPath, $"LOD group {group.name}: {string.Join(", ", missing)} not found; " +
                                                 "the next level kept draws its range.");
                // Each kept level switches where it did; the last kept draws down to the
                // group's cull (pythontk's LodChain.kept_heights).
                float HeightOf(int k) => fits ? (float)group.heights[k] : DefaultHeight(k, count);
                var lods = new LOD[kept.Count];
                for (int j = 0; j < kept.Count; j++)
                    lods[j] = new LOD(j == kept.Count - 1 ? HeightOf(count - 1) : HeightOf(kept[j].k), kept[j].renderers);
                foreach (var level in kept) claimed.UnionWith(level.renderers);
                planned.Add((node, lods));
            }
            // Every LODGroup Unity made outside the groups (its merged one, on the root)
            // first gives up what the groups claim (a renderer in two LODGroups is a
            // Unity warning), and goes when that is everything.
            var groupNodes = new HashSet<Transform>(planned.Select(p => p.node));
            foreach (LODGroup other in planned.Count > 0 ? root.GetComponentsInChildren<LODGroup>(true) : new LODGroup[0])
            {
                if (groupNodes.Contains(other.transform)) continue;
                LOD[] rest = other.GetLODs()
                    .Select(l => new LOD(l.screenRelativeTransitionHeight,
                                         l.renderers.Where(r => r != null && !claimed.Contains(r)).ToArray()))
                    .Where(l => l.renderers.Length > 0)
                    .ToArray();
                if (rest.Length == 0) Object.DestroyImmediate(other, true);
                else other.SetLODs(rest);
            }
            var built = new HashSet<LODGroup>();
            foreach (var (node, lods) in planned)
            {
                LODGroup lodGroup = node.GetComponent<LODGroup>();
                if (lodGroup == null) lodGroup = node.gameObject.AddComponent<LODGroup>();
                lodGroup.SetLODs(lods);
                lodGroup.RecalculateBounds();
                built.Add(lodGroup);
            }
            return built.Count;
        }
    }
}
