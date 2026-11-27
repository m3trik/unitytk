using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>The scene's switched-on component of a kind: one on an active object and enabled, else none.</summary>
    public class SceneObjectsTests
    {
        [SetUp]
        public void SetUp() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void Only_a_switched_on_one_is_found()
        {
            Assert.That(SceneObjects.FirstEnabled<Camera>(), Is.Null, "none: none");

            var off = new GameObject("Off").AddComponent<Camera>();
            off.enabled = false;
            var hidden = new GameObject("Hidden").AddComponent<Camera>();
            hidden.gameObject.SetActive(false);
            Assert.That(SceneObjects.FirstEnabled<Camera>(), Is.Null, "a disabled one and one on an inactive object are no one's");

            var on = new GameObject("On").AddComponent<Camera>();
            Assert.That(SceneObjects.FirstEnabled<Camera>(), Is.SameAs(on));
        }
    }
}
