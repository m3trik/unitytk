using System;
using System.Collections.Generic;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// Which node each name of a record is: the one rule every reader of a delivery
    /// resolves a record's names by (the lightmaps record's objects, the shadows
    /// record's planes, sources and contacts, the articulation record's joints and
    /// grabbed parts, a render effect's node where the file carries no proxy).
    /// </summary>
    /// <remarks>
    /// A name matches exactly, else by its leaf (<see cref="Leaf"/>: the name after the
    /// last ':', for an import or a record that dropped a namespace), case-sensitive.
    /// Objects that share a name are told apart by the entry's hierarchy (its path
    /// from the scene root, its own name last) against where each node sits
    /// (<see cref="HierarchyScore"/>). Every set of entries whose candidates overlap is
    /// settled together, each (entry, node) pair ranked by that score, then by whether
    /// the name matched exactly; an entry binds a node only where each is the other's
    /// unique best match. An entry whose best candidates all went to better matches is
    /// absent (its object is not in this file); anything still tied is ambiguous and
    /// never guessed, since a guess puts one object's light, shadow or pose on another.
    /// Prior art: pythontk's <c>_Media.resolve_entries</c> is the reference (and the
    /// WebXR viewer's <c>kernel/resolve.js</c> its other port); this port is held to it
    /// by its golden cases (<c>ptk.Conformance.cases("record_names")</c>). Unity's own
    /// lookups (<c>Transform.Find</c>) take one path and no namespace, so none fits.
    /// Engine-free: a node is any type, read through its name and its parent.
    /// </remarks>
    public static class RecordNames
    {
        /// <summary>What became of one name.</summary>
        public enum Outcome
        {
            /// <summary>Bound to its node.</summary>
            Bound,
            /// <summary>No node of the file is it.</summary>
            Absent,
            /// <summary>Several nodes match it equally: left unbound.</summary>
            Ambiguous,
        }

        /// <summary>One entry of a record: the name it states, and where its object sits.</summary>
        public readonly struct Entry
        {
            /// <summary>An entry named <paramref name="name"/>, sitting at <paramref name="hierarchy"/>.</summary>
            /// <param name="name">The node's name as the record states it.</param>
            /// <param name="hierarchy">Its names from the scene root down, its own last; null where the record states none.</param>
            public Entry(string name, IList<string> hierarchy = null)
            {
                Name = name;
                Hierarchy = hierarchy;
            }

            /// <summary>The node's name as the record states it.</summary>
            public string Name { get; }

            /// <summary>Its names from the scene root down, its own last; null or empty where the record states none.</summary>
            public IList<string> Hierarchy { get; }
        }

        /// <summary>One entry's node, or why it has none, and the candidates behind a tie.</summary>
        public readonly struct Match<T>
        {
            internal Match(T item, int index, Outcome outcome, bool exact, string[] candidates)
            {
                Item = item;
                Index = index;
                Outcome = outcome;
                Exact = exact;
                Candidates = candidates;
            }

            /// <summary>The node, where <see cref="Outcome"/> is <see cref="Outcome.Bound"/>.</summary>
            public T Item { get; }

            /// <summary>The node's index in the list resolved over; -1 where none is bound.</summary>
            public int Index { get; }

            /// <summary>What became of the name.</summary>
            public Outcome Outcome { get; }

            /// <summary>Whether a bound node is named exactly so (else by its leaf).</summary>
            public bool Exact { get; }

            /// <summary>
            /// Where each tied candidate sits (its names below the root, joined by '/',
            /// sorted), for the report; empty unless <see cref="Outcome"/> is <see cref="Outcome.Ambiguous"/>.
            /// </summary>
            public string[] Candidates { get; }
        }

        /// <summary><paramref name="name"/> without its namespace: after the last ':'; "" for null.</summary>
        public static string Leaf(string name) => string.IsNullOrEmpty(name) ? "" : name.Substring(name.LastIndexOf(':') + 1);

        /// <summary>
        /// How many trailing names <paramref name="hierarchy"/> and <paramref name="lineage"/>
        /// (both root first) share, compared from the object up, namespace-blind, stopped
        /// at the first disagreement: an export may root the file under a node the scene
        /// never had, but cannot rename the parents it keeps.
        /// </summary>
        public static int HierarchyScore(IList<string> hierarchy, IList<string> lineage)
        {
            int score = 0;
            for (int a = hierarchy.Count - 1, b = lineage.Count - 1; a >= 0 && b >= 0; a--, b--)
            {
                if (!string.Equals(Leaf(hierarchy[a]), Leaf(lineage[b]), StringComparison.Ordinal)) break;
                score++;
            }
            return score;
        }

        /// <summary>
        /// The one node of <paramref name="nodes"/> <paramref name="name"/> denotes, as an
        /// assignment of its own (several entries may share one node: a source, a contact),
        /// or why there is none.
        /// </summary>
        /// <param name="nodes">The candidates.</param>
        /// <param name="name">The name the record states.</param>
        /// <param name="nameOf">A node's name.</param>
        /// <param name="parentOf">A node's parent, null at the top.</param>
        /// <param name="root">The node a tie's candidate paths are stated under; null: from the top.</param>
        public static Match<T> Find<T>(IList<T> nodes, string name, Func<T, string> nameOf, Func<T, T> parentOf,
                                       T root = null) where T : class
            => Resolve(nodes, new[] { new Entry(name) }, nameOf, parentOf, root)[0];

        /// <summary>
        /// Each of <paramref name="entries"/> matched to a node of <paramref name="nodes"/>
        /// as ONE assignment, by the rule this type states; one result per entry, in order.
        /// </summary>
        /// <param name="nodes">The candidates (a record of meshes: the mesh nodes alone).</param>
        /// <param name="entries">The record's entries.</param>
        /// <param name="nameOf">A node's name.</param>
        /// <param name="parentOf">A node's parent, null at the top: every ancestor counts toward the hierarchy, candidate or not.</param>
        /// <param name="root">The node a tie's candidate paths are stated under; null: from the top.</param>
        public static Match<T>[] Resolve<T>(IList<T> nodes, IList<Entry> entries, Func<T, string> nameOf,
                                            Func<T, T> parentOf, T root = null) where T : class
        {
            var byName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            var names = new List<string>();  // first-seen order, as the reference indexes them
            for (int k = 0; k < nodes.Count; k++)
            {
                if (nodes[k] == null) continue;
                string name = nameOf(nodes[k]) ?? "";
                if (!byName.TryGetValue(name, out var named))
                {
                    byName[name] = named = new List<int>();
                    names.Add(name);
                }
                named.Add(k);
            }
            var leaves = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string full in names)
            {
                string leaf = Leaf(full);
                if (!leaves.TryGetValue(leaf, out var fulls)) leaves[leaf] = fulls = new List<string>();
                fulls.Add(full);
            }

            var results = new Match<T>[entries.Count];
            var exact = new bool[entries.Count];
            var pools = new List<int>[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                results[i] = new Match<T>(null, -1, Outcome.Absent, false, Array.Empty<string>());
                string name = entries[i].Name;
                // A nameless entry never reaches the lookup: "" is where nameless nodes are indexed.
                if (string.IsNullOrEmpty(name)) continue;
                List<int> pool = null;
                if (byName.TryGetValue(name, out var exactly))
                {
                    pool = exactly;
                    exact[i] = true;
                }
                else if (leaves.TryGetValue(Leaf(name), out var fulls))
                {
                    pool = new List<int>();
                    foreach (string full in fulls) pool.AddRange(byName[full]);
                }
                if (pool != null && pool.Count > 0) pools[i] = pool;
            }

            // Union-find over node indices: entries sharing any candidate land together.
            var parent = new int[nodes.Count];
            for (int k = 0; k < parent.Length; k++) parent[k] = k;
            int Top(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }
                return x;
            }
            foreach (List<int> pool in pools)
            {
                if (pool == null) continue;
                int first = Top(pool[0]);
                for (int j = 1; j < pool.Count; j++) parent[Top(pool[j])] = first;
            }
            var groups = new Dictionary<int, List<int>>();
            var order = new List<int>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (pools[i] == null) continue;
                int key = Top(pools[i][0]);
                if (!groups.TryGetValue(key, out var members))
                {
                    groups[key] = members = new List<int>();
                    order.Add(key);
                }
                members.Add(i);
            }

            var lineages = new Dictionary<int, Chain>();
            Chain ChainOf(int k)
            {
                if (!lineages.TryGetValue(k, out Chain chain)) lineages[k] = chain = Walk(nodes[k], nameOf, parentOf, root);
                return chain;
            }

            foreach (int key in order)
            {
                List<int> members = groups[key];
                var all = new HashSet<int>();
                foreach (int i in members) all.UnionWith(pools[i]);
                if (members.Count == 1 && all.Count == 1)
                {
                    // One entry, one node: the name already said it.
                    int only = members[0], k = pools[only][0];
                    results[only] = new Match<T>(nodes[k], k, Outcome.Bound, exact[only], Array.Empty<string>());
                    continue;
                }
                // (hierarchy score, exact name) as one number: an entry without a hierarchy
                // scores 0 on every node, which leaves the name to decide.
                var rank = new Dictionary<(int, int), int>();
                var takers = new Dictionary<int, List<int>>();
                foreach (int i in members)
                {
                    IList<string> hierarchy = entries[i].Hierarchy;
                    bool usable = hierarchy != null && hierarchy.Count > 0;
                    foreach (int k in pools[i])
                    {
                        int score = usable ? HierarchyScore(hierarchy, ChainOf(k).Names) : 0;
                        rank[(i, k)] = score * 2 + (exact[i] ? 1 : 0);
                        if (!takers.TryGetValue(k, out var list)) takers[k] = list = new List<int>();
                        list.Add(i);
                    }
                }
                var best = new Dictionary<int, int>();
                foreach (int i in members) best[i] = UniqueBest(pools[i], k => rank[(i, k)]);
                var owner = new Dictionary<int, int>();
                foreach (var pair in takers)
                {
                    int k = pair.Key;
                    int taker = UniqueBest(pair.Value, i => rank[(i, k)]);
                    if (taker >= 0 && best[taker] == k) owner[k] = taker;
                }
                foreach (int i in members)
                {
                    int k = best[i];
                    if (k >= 0 && owner.TryGetValue(k, out int o) && o == i)
                    {
                        // Settled out of a leaf pool is still a leaf match.
                        results[i] = new Match<T>(nodes[k], k, Outcome.Bound, exact[i], Array.Empty<string>());
                        continue;
                    }
                    int top = int.MinValue;
                    foreach (int c in pools[i]) top = Math.Max(top, rank[(i, c)]);
                    bool taken = true;
                    foreach (int c in pools[i])
                        if (rank[(i, c)] == top && !owner.ContainsKey(c)) taken = false;
                    if (taken)
                    {
                        // Every node this entry could best be went to a better match.
                        results[i] = new Match<T>(null, -1, Outcome.Absent, false, Array.Empty<string>());
                        continue;
                    }
                    var paths = new List<string>();
                    foreach (int c in pools[i]) paths.Add(ChainOf(c).Path);
                    paths.Sort(StringComparer.Ordinal);
                    results[i] = new Match<T>(null, -1, Outcome.Ambiguous, false, paths.ToArray());
                }
            }
            return results;
        }

        // A node's names from the top down, itself last, and its path below the root.
        readonly struct Chain
        {
            public Chain(List<string> names, string path)
            {
                Names = names;
                Path = path;
            }

            public List<string> Names { get; }
            public string Path { get; }
        }

        static Chain Walk<T>(T node, Func<T, string> nameOf, Func<T, T> parentOf, T root) where T : class
        {
            var names = new List<string>();
            int below = -1;  // how many names sit below the root, once it is met
            var seen = new HashSet<T>();
            for (T at = node; at != null && seen.Add(at); at = parentOf(at))
            {
                if (root != null && below < 0 && EqualityComparer<T>.Default.Equals(at, root)) below = names.Count;
                names.Add(nameOf(at) ?? "");
            }
            names.Reverse();
            int skip = below < 0 ? 0 : names.Count - below;
            return new Chain(names, string.Join("/", names.GetRange(skip, names.Count - skip)));
        }

        // The item with the strictly highest score; -1 on a tie or none.
        static int UniqueBest(List<int> items, Func<int, int> score)
        {
            int best = -1, top = int.MinValue;
            bool tie = false;
            foreach (int item in items)
            {
                int s = score(item);
                if (best < 0 || s > top)
                {
                    best = item;
                    top = s;
                    tie = false;
                }
                else if (s == top) tie = true;
            }
            return tie ? -1 : best;
        }
    }
}
