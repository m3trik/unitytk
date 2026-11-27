using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace M3trik.Unitytk.Kernel.Tests
{
    /// <summary>
    /// <see cref="CameraDrawing"/>: a camera's drawing is raised once each way, whichever pipeline draws it, the render's
    /// own before its first camera's; Unity's callbacks are held while anything listens and let go with the last listener.
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
            void Rendering() => said.Add("rendering");
            bool others = CameraDrawing.Hooked;  // another part's listener (a glow, a cache) holds them already
            try
            {
                CameraDrawing.Starting += Starting;
                CameraDrawing.Ended += Ended;
                CameraDrawing.Rendering += Rendering;
                Assert.That(CameraDrawing.Hooked, Is.True, "held while anything listens");
                camera.Render();
                Assert.That(said.Where(s => s != "rendering"), Is.EqualTo(new[] { "starting", "ended" }), "once each way");
                Assert.That(said.IndexOf("rendering"), Is.InRange(0, said.IndexOf("starting") - 1), "the render's own first");
            }
            finally
            {
                CameraDrawing.Starting -= Starting;
                CameraDrawing.Ended -= Ended;
                CameraDrawing.Rendering -= Rendering;
                Object.DestroyImmediate(camera.gameObject);
                Object.DestroyImmediate(target);
            }
            Assert.That(CameraDrawing.Hooked, Is.EqualTo(others), "let go with its last listener");
        }
    }
}
