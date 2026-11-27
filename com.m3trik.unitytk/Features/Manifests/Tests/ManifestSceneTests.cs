using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Manifests.Tests
{
    /// <summary>
    /// <see cref="ManifestScene"/>: a plan's node paths found under the delivery's instance
    /// by the records' rule, whatever the instance is named; a tie never guessed.
    /// </summary>
    public class ManifestSceneTests
    {
        GameObject _root;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        static Transform Child(Transform parent, string name)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent);
            return node;
        }

        [Test]
        public void A_path_finds_its_node_under_any_instance_name_and_same_names_apart()
        {
            _root = new GameObject("Renamed Instance (1)");
            Transform panel = Child(_root.transform, "PANEL");
            Transform a = Child(Child(panel, "OPEN_BUTTON"), "button_A");
            Transform b = Child(Child(panel, "CLOSE_BUTTON"), "button_A");
            var scene = new ManifestScene(_root, new AnimationClip[0]);

            Assert.That(scene.Node("PANEL|OPEN_BUTTON|button_A"), Is.SameAs(a));
            Assert.That(scene.Node("PANEL|CLOSE_BUTTON|button_A"), Is.SameAs(b));
            Assert.That(scene.Find("button_A").Outcome, Is.EqualTo(RecordNames.Outcome.Ambiguous), "a bare shared name is a tie");
            Assert.That(scene.Problem("button_A", "target"), Does.Contain("matches several nodes"));
            Assert.That(scene.Node("PANEL|HINGE"), Is.Null);
            Assert.That(scene.Problem("PANEL", "target"), Is.Null);
        }

        [Test]
        public void A_path_names_the_deliverys_node_over_an_object_a_build_added_beside_it_under_its_name()
        {
            // A helper Unity made for a row (CAM_A01_DOOR, as every tool names it), then the delivery bringing the one the
            // Shot Manifest made under that name, beside it: the row's path named both, and the row was refused as a tie.
            const string Path = "Assets/ManifestSceneTests delivered helper.prefab";
            var made = new GameObject("DELIVERY");
            Child(Child(made.transform, "DOOR"), "CAM_A01_DOOR");
            try
            {
                GameObject asset = UnityEditor.PrefabUtility.SaveAsPrefabAsset(made, Path);
                _root = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(asset);
                Transform door = _root.transform.Find("DOOR"), delivered = door.Find("CAM_A01_DOOR");
                Transform own = Child(door, "CAM_A01_DOOR");  // what a build added
                var scene = new ManifestScene(_root, new AnimationClip[0]);
                Assert.That(scene.Node("DOOR|CAM_A01_DOOR"), Is.SameAs(delivered), "the delivery's own, never the build's");
                Assert.That(scene.Problem("DOOR|CAM_A01_DOOR", "where"), Is.Null);
                Object.DestroyImmediate(own.gameObject);
                Child(_root.transform, "LOOSE");
                Child(_root.transform, "LOOSE");
                Assert.That(new ManifestScene(_root, new AnimationClip[0]).Find("LOOSE").Outcome, Is.EqualTo(RecordNames.Outcome.Ambiguous),
                            "two added objects of one name are still a tie: no delivered one to prefer");
            }
            finally
            {
                Object.DestroyImmediate(made);
                UnityEditor.AssetDatabase.DeleteAsset(Path);
            }
        }

        [Test]
        public void An_extra_files_nodes_are_found_after_the_deliverys_own()
        {
            _root = new GameObject("assembly");
            var extra = new GameObject("arrows");
            try
            {
                Transform door = Child(Child(_root.transform, "INTERACTIVE"), "DOOR_GRP");
                Transform arrow = Child(Child(extra.transform, "INTERACTIVE"), "DOOR_ARROW");
                Transform shadow = Child(Child(extra.transform, "INTERACTIVE"), "DOOR_GRP");
                var scene = new ManifestScene(_root, new[] { extra }, new AnimationClip[0]);

                Assert.That(scene.Node("INTERACTIVE|DOOR_ARROW"), Is.SameAs(arrow), "an extra file's node");
                Assert.That(scene.Node("INTERACTIVE|DOOR_GRP"), Is.SameAs(door), "the delivery's own first");
                Assert.That(shadow, Is.Not.SameAs(door));
                Assert.That(scene.Extras, Is.EqualTo(new[] { extra }));
                Assert.That(scene.Problem("INTERACTIVE|LID", "target"), Does.Contain("assembly").And.Contain("arrows"));
                Assert.That(() => new ManifestScene(_root, new GameObject[] { null }), Throws.ArgumentNullException);
            }
            finally
            {
                Object.DestroyImmediate(extra);
            }
        }

        [Test]
        public void Clips_are_found_by_take_name()
        {
            _root = new GameObject("cabinet");
            var scene = new ManifestScene(_root, new[] { new AnimationClip { name = "A02" }, new AnimationClip { name = "A01" } });
            Assert.That(scene.ClipNames, Is.EqualTo(new[] { "A01", "A02" }));
            Assert.That(scene.Clip("A02").name, Is.EqualTo("A02"));
            Assert.That(scene.Clip("B01"), Is.Null);
        }
    }
}
