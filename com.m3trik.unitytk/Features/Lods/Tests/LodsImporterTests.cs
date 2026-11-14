using System.Linq;
using M3trik.Unitytk.Contracts;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Lods.Tests
{
    /// <summary>
    /// <see cref="LodsImporter.Build"/>: one LODGroup per group the <c>lods</c> record
    /// names. Unity's own FBX import merges every <c>_LODn</c> mesh of a file into ONE
    /// LODGroup on the model's root and drops the thresholds (measured 2026-11-10,
    /// Unity 6000.3), which the fixture stands up the way Unity does.
    /// </summary>
    public class LodsImporterTests
    {
        const string Model = "Assets/UnitytkLodsTests/scene.fbx";

        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.DestroyImmediate(_root);
        }

        /// <summary>A model as Unity imports a scene FBX: two groups' levels under their
        /// nodes, a lone lamp, and ONE merged LODGroup on the root.</summary>
        GameObject UnityImport()
        {
            _root = new GameObject("scene");
            var levels = new System.Collections.Generic.List<Renderer>[3];
            for (int i = 0; i < 3; i++) levels[i] = new System.Collections.Generic.List<Renderer>();
            foreach (string group in new[] { "chair", "stool" })
            {
                var node = new GameObject(group);
                node.transform.SetParent(_root.transform, false);
                for (int i = 0; i < 3; i++)
                {
                    var level = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    level.name = $"{group}_LOD{i}";
                    level.transform.SetParent(node.transform, false);
                    levels[i].Add(level.GetComponent<Renderer>());
                }
            }
            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lamp.name = "lamp";
            lamp.transform.SetParent(_root.transform, false);
            var merged = _root.AddComponent<LODGroup>();
            merged.SetLODs(new[]
            {
                new LOD(0.25f, levels[0].ToArray()), new LOD(0.125f, levels[1].ToArray()), new LOD(0.01f, levels[2].ToArray()),
            });
            return _root;
        }

        static string Record(string groups = null)
            => @"{""version"": 1, ""groups"": [" + (groups ??
               @"{""name"": ""chair"", ""levels"": [""chair_LOD0"", ""chair_LOD1"", ""chair_LOD2""], ""heights"": [0.6, 0.3, 0.0]}, " +
               @"{""name"": ""stool"", ""levels"": [""stool_LOD0"", ""stool_LOD1"", ""stool_LOD2""], ""heights"": [0.5, 0.25, 0.0]}") +
               "]}";

        [Test]
        public void Each_group_gets_its_own_lod_group_at_the_recorded_heights()
        {
            GameObject root = UnityImport();
            Assert.That(LodsImporter.Build(root, Record(), Model, new PureRecordSource()), Is.EqualTo(2));
            LOD[] chair = root.transform.Find("chair").GetComponent<LODGroup>().GetLODs();
            Assert.That(chair.Select(l => l.screenRelativeTransitionHeight), Is.EqualTo(new[] { 0.6f, 0.3f, 0f }));
            Assert.That(chair.Select(l => l.renderers.Single().name), Is.EqualTo(new[] { "chair_LOD0", "chair_LOD1", "chair_LOD2" }));
            Assert.That(root.transform.Find("stool").GetComponent<LODGroup>().GetLODs()[0].screenRelativeTransitionHeight,
                        Is.EqualTo(0.5f));
        }

        [Test]
        public void The_roots_merged_group_goes_once_every_renderer_is_claimed()
        {
            GameObject root = UnityImport();
            LodsImporter.Build(root, Record(), Model, new PureRecordSource());
            Assert.That(root.GetComponent<LODGroup>(), Is.Null);
        }

        [Test]
        public void The_merged_group_keeps_what_no_group_claims()
        {
            GameObject root = UnityImport();
            string onlyChair =
                @"{""name"": ""chair"", ""levels"": [""chair_LOD0"", ""chair_LOD1"", ""chair_LOD2""], ""heights"": [0.6, 0.3, 0.0]}";
            Assert.That(LodsImporter.Build(root, Record(onlyChair), Model, new PureRecordSource()), Is.EqualTo(1));
            LOD[] rest = root.GetComponent<LODGroup>().GetLODs();
            Assert.That(rest.SelectMany(l => l.renderers).Select(r => r.name),
                        Is.EquivalentTo(new[] { "stool_LOD0", "stool_LOD1", "stool_LOD2" }));
        }

        [Test]
        public void A_group_the_file_lacks_is_left_as_unity_imported_it()
        {
            GameObject root = UnityImport();
            string missing = @"{""name"": ""table"", ""levels"": [""table_LOD0"", ""table_LOD1""], ""heights"": [0.5, 0.0]}";
            Assert.That(LodsImporter.Build(root, Record(missing), Model, new PureRecordSource()), Is.EqualTo(0));
            Assert.That(root.GetComponent<LODGroup>(), Is.Not.Null, "Unity's own group stays");
        }

        [Test]
        public void A_level_whose_namespace_the_import_dropped_still_binds()
        {
            GameObject root = UnityImport();
            string spaced =
                @"{""name"": ""props:chair"", ""levels"": [""props:chair_LOD0"", ""props:chair_LOD1"", ""props:chair_LOD2""], ""heights"": [0.6, 0.3, 0.0]}";
            Assert.That(LodsImporter.Build(root, Record(spaced), Model, new PureRecordSource()), Is.EqualTo(1));
            Assert.That(root.transform.Find("chair").GetComponent<LODGroup>().GetLODs().Length, Is.EqualTo(3));
        }

        [Test]
        public void A_level_the_file_lacks_gives_its_range_to_the_next_and_the_last_keeps_the_cull()
        {
            GameObject root = UnityImport();
            Object.DestroyImmediate(root.transform.Find("chair/chair_LOD2").gameObject);
            string chair =
                @"{""name"": ""chair"", ""levels"": [""chair_LOD0"", ""chair_LOD1"", ""chair_LOD2""], ""heights"": [0.6, 0.3, 0.05]}";
            Assert.That(LodsImporter.Build(root, Record(chair), Model, new PureRecordSource()), Is.EqualTo(1));
            LOD[] lods = root.transform.Find("chair").GetComponent<LODGroup>().GetLODs();
            Assert.That(lods.Select(l => l.screenRelativeTransitionHeight), Is.EqualTo(new[] { 0.6f, 0.05f }));
        }

        [Test]
        public void Heights_that_miss_a_level_take_the_default_ladder()
        {
            GameObject root = UnityImport();
            string chair =
                @"{""name"": ""chair"", ""levels"": [""chair_LOD0"", ""chair_LOD1"", ""chair_LOD2""], ""heights"": [0.6]}";
            LodsImporter.Build(root, Record(chair), Model, new PureRecordSource());
            LOD[] lods = root.transform.Find("chair").GetComponent<LODGroup>().GetLODs();
            Assert.That(lods.Select(l => l.screenRelativeTransitionHeight), Is.EqualTo(new[] { 0.5f, 0.25f, 0f }));
        }

        [Test]
        public void A_merged_group_below_the_root_gives_up_what_the_groups_claim()
        {
            GameObject root = UnityImport();
            var holder = new GameObject("holder");
            holder.transform.SetParent(root.transform, false);
            LODGroup merged = root.GetComponent<LODGroup>();
            LOD[] lods = merged.GetLODs();
            Object.DestroyImmediate(merged);
            holder.AddComponent<LODGroup>().SetLODs(lods);
            LodsImporter.Build(root, Record(), Model, new PureRecordSource());
            Assert.That(holder.GetComponent<LODGroup>(), Is.Null, "every renderer it held is a group's now");
        }

        [Test]
        public void No_record_changes_nothing()
        {
            GameObject root = UnityImport();
            Assert.That(LodsImporter.Build(root, null, Model, new PureRecordSource()), Is.EqualTo(0));
            Assert.That(root.GetComponent<LODGroup>().GetLODs().Length, Is.EqualTo(3));
        }
    }
}
