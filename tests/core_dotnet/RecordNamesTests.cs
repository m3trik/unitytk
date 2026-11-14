using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Models.Tests;
using NUnit.Framework;

namespace M3trik.Unitytk.Core.Tests
{
    /// <summary>
    /// How a record's names find their nodes (<see cref="RecordNames"/>), held to
    /// pythontk's reference (<c>_Media.resolve_entries</c>) by its golden cases
    /// (<c>ptk.Conformance.cases("record_names")</c>), then the clauses each reader
    /// leans on, spelled out.
    /// </summary>
    public class RecordNamesTests
    {
        sealed class Node
        {
            public Node(string name, Node parent = null)
            {
                Name = name;
                Parent = parent;
            }

            public string Name { get; }
            public Node Parent { get; }
        }

        static RecordNames.Match<Node>[] Resolve(IList<Node> nodes, IList<RecordNames.Entry> entries, Node root = null)
            => RecordNames.Resolve(nodes, entries, n => n.Name, n => n.Parent, root);

        static RecordNames.Entry E(string name, params string[] hierarchy)
            => new RecordNames.Entry(name, hierarchy.Length > 0 ? hierarchy : null);

        [Test]
        public void Every_golden_case_resolves_as_the_reference_does()
        {
            IDictionary<string, object> doc = Conformance.Cases("record_names", 5, "{\"count\": 96}");
            var cases = ((IList<object>)doc["cases"]).Cast<IDictionary<string, object>>().ToList();
            Assert.That(cases, Is.Not.Empty, "the reference generated no case");
            foreach (var c in cases)
            {
                var given = (IDictionary<string, object>)c["input"];
                var rows = ((IList<object>)given["nodes"]).Cast<IDictionary<string, object>>().ToList();
                var all = new List<Node>();
                foreach (var row in rows)
                {
                    Node parent = row["parent"] == null ? null : all[(int)(double)row["parent"]];
                    all.Add(new Node((string)row["name"], parent));
                }
                var meshes = new List<Node>();
                for (int i = 0; i < rows.Count; i++)
                    if ((bool)rows[i]["mesh"]) meshes.Add(all[i]);
                var entries = new List<RecordNames.Entry>();
                foreach (IDictionary<string, object> entry in (IList<object>)given["entries"])
                {
                    entry.TryGetValue("name", out object name);
                    entry.TryGetValue("hierarchy", out object hierarchy);
                    entries.Add(new RecordNames.Entry(
                        name as string, (hierarchy as IList<object>)?.Select(h => (string)h).ToList()));
                }
                RecordNames.Match<Node>[] found = Resolve(meshes, entries);
                var expected = ((IList<object>)c["expected"]).Cast<IDictionary<string, object>>().ToList();
                Assert.That(found.Length, Is.EqualTo(expected.Count), $"{c["name"]}: one result per entry");
                for (int i = 0; i < expected.Count; i++)
                {
                    string at = $"{c["name"]} entry {i}";
                    string status = (string)expected[i]["status"];
                    Assert.That(Status(found[i]), Is.EqualTo(status), at);
                    Node bound = expected[i]["node"] == null ? null : all[(int)(double)expected[i]["node"]];
                    Assert.That(found[i].Item, Is.SameAs(bound), at);
                    if (bound != null) Assert.That(meshes[found[i].Index], Is.SameAs(bound), $"{at}: the index");
                    var candidates = ((IList<object>)expected[i]["candidates"]).Cast<string>().ToArray();
                    Assert.That(found[i].Candidates, Is.EqualTo(candidates), $"{at}: where each candidate sits");
                }
            }
        }

        static string Status(RecordNames.Match<Node> m)
        {
            switch (m.Outcome)
            {
                case RecordNames.Outcome.Bound: return m.Exact ? "exact" : "leaf";
                case RecordNames.Outcome.Ambiguous: return "ambiguous";
                default: return "absent";
            }
        }

        [Test]
        public void A_leaf_is_the_name_after_the_last_colon()
        {
            Assert.That(RecordNames.Leaf("a:b:elbow"), Is.EqualTo("elbow"));
            Assert.That(RecordNames.Leaf("elbow"), Is.EqualTo("elbow"));
            Assert.That(RecordNames.Leaf(null), Is.EqualTo(""));
        }

        [Test]
        public void A_name_binds_exactly_else_by_its_leaf_case_sensitive()
        {
            var nodes = new List<Node> { new Node("elbow"), new Node("ns:base") };
            var found = Resolve(nodes, new[] { E("elbow"), E("base"), E("Elbow") });
            Assert.That(found[0].Item, Is.SameAs(nodes[0]));
            Assert.That(found[0].Exact, Is.True);
            Assert.That(found[1].Item, Is.SameAs(nodes[1]));
            Assert.That(found[1].Exact, Is.False, "bound by its leaf");
            Assert.That(found[2].Outcome, Is.EqualTo(RecordNames.Outcome.Absent), "case-sensitive");
        }

        [Test]
        public void A_tie_is_never_guessed_and_names_where_each_candidate_sits_under_the_root()
        {
            var root = new Node("Model");
            var a = new Node("a", root);
            var nodes = new List<Node> { new Node("geo", a), new Node("ns:geo", root) };
            var tie = RecordNames.Find(nodes, "x:geo", n => n.Name, n => n.Parent, root);
            Assert.That(tie.Outcome, Is.EqualTo(RecordNames.Outcome.Ambiguous));
            Assert.That(tie.Item, Is.Null);
            Assert.That(tie.Candidates, Is.EqualTo(new[] { "a/geo", "ns:geo" }));
            Assert.That(RecordNames.Find(nodes, "ns:geo", n => n.Name, n => n.Parent, root).Item, Is.SameAs(nodes[1]));
            Assert.That(RecordNames.Find(nodes, "", n => n.Name, n => n.Parent, root).Outcome,
                        Is.EqualTo(RecordNames.Outcome.Absent));
        }

        [Test]
        public void An_exact_name_owns_its_node_and_a_leaf_whose_candidates_all_went_is_absent()
        {
            var nodes = new List<Node> { new Node("hand") };
            var found = Resolve(nodes, new[] { E("hand"), E("other:hand") });
            Assert.That(found[0].Outcome, Is.EqualTo(RecordNames.Outcome.Bound));
            Assert.That(found[1].Outcome, Is.EqualTo(RecordNames.Outcome.Absent));
        }

        [Test]
        public void Two_names_on_one_leaf_tie_for_it_and_exact_names_win_their_own()
        {
            var x = new List<Node> { new Node("x:hand") };
            var found = Resolve(x, new[] { E("a:hand"), E("b:hand") });
            Assert.That(found[0].Outcome, Is.EqualTo(RecordNames.Outcome.Ambiguous), "neither is its unique best taker");
            Assert.That(found[1].Outcome, Is.EqualTo(RecordNames.Outcome.Ambiguous));

            var boxes = new List<Node> { new Node("Box"), new Node("ns:Box") };
            found = Resolve(boxes, new[] { E("ns:Box"), E("Box"), E("x:Box") });
            Assert.That(found[0].Item, Is.SameAs(boxes[1]), "exact");
            Assert.That(found[1].Item, Is.SameAs(boxes[0]), "exact");
            Assert.That(found[2].Outcome, Is.EqualTo(RecordNames.Outcome.Absent),
                        "its leaf's every node went to an exact name: not in this file");
        }

        [Test]
        public void A_tie_names_nested_candidates_by_their_path_under_the_root()
        {
            var root = new Node("Model");
            var geo = new Node("geo", root);
            var nodes = new List<Node> { geo, new Node("geo", geo) };
            var tie = RecordNames.Find(nodes, "geo", n => n.Name, n => n.Parent, root);
            Assert.That(tie.Candidates, Is.EqualTo(new[] { "geo", "geo/geo" }));
            Assert.That(RecordNames.Find(new[] { nodes[1] }, "ns:geo", n => n.Name, n => n.Parent, root).Item,
                        Is.SameAs(nodes[1]), "by its leaf");
        }

        [Test]
        public void The_hierarchy_tells_apart_objects_that_share_a_name()
        {
            var roomA = new Node("roomA");
            var roomB = new Node("ns:roomB");
            var nodes = new List<Node> { new Node("BODY", roomA), new Node("BODY", roomB) };
            var found = Resolve(nodes, new[] { E("BODY", "roomB", "BODY"), E("BODY", "roomA", "BODY"), E("BODY", "BODY") });
            Assert.That(found[0].Item, Is.SameAs(nodes[1]), "namespace-blind");
            Assert.That(found[1].Item, Is.SameAs(nodes[0]));
            Assert.That(found[2].Outcome, Is.EqualTo(RecordNames.Outcome.Absent), "a stale entry: both went to better matches");
        }

        [Test]
        public void The_hierarchy_score_counts_the_trailing_names_both_share()
        {
            Assert.That(RecordNames.HierarchyScore(new[] { "root", "ns:grp", "BODY" }, new[] { "RootNode", "grp", "BODY" }),
                        Is.EqualTo(2));
            Assert.That(RecordNames.HierarchyScore(new[] { "grp", "BODY" }, new[] { "grp", "lid" }), Is.EqualTo(0));
        }
    }
}
