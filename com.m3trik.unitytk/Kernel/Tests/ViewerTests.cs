using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>The viewer's eyes: the ones given, else the main camera's, else none.</summary>
    public class ViewerTests
    {
        GameObject _camera;
        GameObject _eyes;

        [TearDown]
        public void TearDown()
        {
            if (_camera != null) Object.DestroyImmediate(_camera);
            if (_eyes != null) Object.DestroyImmediate(_eyes);
        }

        [Test]
        public void The_eyes_given_come_first_then_the_main_camera()
        {
            _eyes = new GameObject("eyes");
            Assert.That(Viewer.Eyes(_eyes.transform), Is.SameAs(_eyes.transform));
            Camera main = Camera.main;
            if (main == null)
            {
                _camera = new GameObject("main", typeof(Camera)) { tag = "MainCamera" };
                main = _camera.GetComponent<Camera>();
            }
            Assert.That(Viewer.Eyes(), Is.SameAs(main.transform));
            Assert.That(Viewer.ViewCamera(), Is.SameAs(main));
            Camera own = _eyes.AddComponent<Camera>();
            Assert.That(Viewer.ViewCamera(own), Is.SameAs(own));
        }
    }
}
