using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>A scene's top-level object by name: the first of a repeated name, a switched-off one by choice, one holding a component.</summary>
    public class SceneRootsTests
    {
        Scene _scene;

        [SetUp]
        public void SetUp() => _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void The_first_of_a_name_is_found_and_only_at_the_top()
        {
            var first = new GameObject("Holder");
            var second = new GameObject("Holder");
            new GameObject("Deep").transform.SetParent(first.transform);
            Assert.That(SceneRoots.Find("Holder"), Is.SameAs(first));
            Assert.That(SceneRoots.Find("Holder", _scene), Is.SameAs(first));
            Assert.That(SceneRoots.Find("Deep"), Is.Null, "a child is no top-level object");
            Assert.That(SceneRoots.Find(""), Is.Null);
            first.SetActive(false);
            Assert.That(SceneRoots.Find("Holder"), Is.SameAs(first), "a switched-off one by default");
            Assert.That(SceneRoots.Find("Holder", includeInactive: false), Is.SameAs(second));
        }

        [Test]
        public void One_holding_a_component_is_found_past_another_of_its_name()
        {
            new GameObject("Rig");
            var held = new GameObject("Rig", typeof(Camera));
            Assert.That(SceneRoots.Find<Camera>("Rig"), Is.SameAs(held.GetComponent<Camera>()));
            Assert.That(SceneRoots.Find<Light>("Rig"), Is.Null);
        }
    }
}
