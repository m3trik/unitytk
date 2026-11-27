using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// <see cref="CameraDrawing"/>: a camera's drawing is raised once each way, whichever pipeline draws it, the render's
    /// own with it before it starts; Unity's callbacks are held while anything listens and let go with the last listener,
    /// and the count of listeners says each one joining and leaving.
    /// </summary>
    public class CameraDrawingTests
    {
        [Test]
        public void A_cameras_drawing_is_raised_once_each_way_and_the_hooks_go_with_the_last_listener()
        {
            var target = new RenderTexture(16, 16, 16);
            var camera = new GameObject("CAMERA (drawing test)").AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = target;
            var said = new List<string>();
            void Starting(Camera drawing)
            {
                if (drawing == camera) said.Add("starting");
            }
            void Ended(Camera drawing)
            {
                if (drawing == camera) said.Add("ended");
            }
            void Rendering(Camera drawing)
            {
                if (drawing == camera) said.Add("rendering");
            }
            bool others = CameraDrawing.Hooked;  // listeners outside this test may hold them already
            int listening = CameraDrawing.Listeners;
            try
            {
                CameraDrawing.Starting += Starting;
                CameraDrawing.Ended += Ended;
                CameraDrawing.Rendering += Rendering;
                Assert.That(CameraDrawing.Hooked, Is.True, "held while anything listens");
                Assert.That(CameraDrawing.Listeners, Is.EqualTo(listening + 3), "each listener counted");
                camera.Render();
                Assert.That(said, Is.EqualTo(new[] { "rendering", "starting", "ended" }),
                            "once each way, the render's own with the camera before it starts");
            }
            finally
            {
                CameraDrawing.Starting -= Starting;
                CameraDrawing.Ended -= Ended;
                CameraDrawing.Rendering -= Rendering;
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(target);
            }
            Assert.That(CameraDrawing.Listeners, Is.EqualTo(listening), "each one gone as it leaves");
            Assert.That(CameraDrawing.Hooked, Is.EqualTo(others), "let go with its last listener");
        }
    }
}
