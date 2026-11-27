using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The one subscriber to Unity's per-camera render callbacks, for every runtime part that does something as a
    /// camera draws (a glow card drawn for it, what a picture leaves out hidden from it, a cache marched before it).
    /// A scriptable pipeline raises <see cref="RenderPipelineManager"/>'s callbacks and the Built-in pipeline
    /// <see cref="Camera"/>'s, each never the other's, so this hooks both while anything listens and gives each
    /// listener one event whichever pipeline draws: <see cref="Starting"/> and <see cref="Ended"/> per camera,
    /// <see cref="Rendering"/> before the cameras a render draws. It hooks them as its first listener joins and lets
    /// them go as its last leaves.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's own callbacks (<c>RenderPipelineManager.beginContextRendering</c>,
    /// <c>beginCameraRendering</c> and <c>endCameraRendering</c>, which every scriptable pipeline raises, and the
    /// Built-in pipeline's <c>Camera.onPreCull</c> and <c>Camera.onPostRender</c>) are adopted as they stand; three
    /// parts each subscribed to both families themselves, two of them never letting go. Which family a pipeline
    /// raises is Unity's, not a pipeline's names, so it is answered here once rather than by
    /// <see cref="IRenderPipelineHost"/>, which a pipeline no door serves (HDRP) has none of. The listeners are the
    /// registry the C# rules allow: each joins again as its part is switched on after a domain reload.
    /// </remarks>
    public static class CameraDrawing
    {
        static Action<Camera> _starting, _ended;
        static Action _rendering;

        /// <summary>Whether it holds Unity's callbacks now: while anything listens.</summary>
        internal static bool Hooked { get; private set; }

        /// <summary>Each camera as it starts to draw: a scriptable pipeline's <c>beginCameraRendering</c>, the Built-in pipeline's <c>Camera.onPreCull</c>.</summary>
        public static event Action<Camera> Starting
        {
            add
            {
                _starting += value;
                Hook();
            }
            remove
            {
                _starting -= value;
                Hook();
            }
        }

        /// <summary>Each camera once it has drawn: a scriptable pipeline's <c>endCameraRendering</c>, the Built-in pipeline's <c>Camera.onPostRender</c>.</summary>
        public static event Action<Camera> Ended
        {
            add
            {
                _ended += value;
                Hook();
            }
            remove
            {
                _ended -= value;
                Hook();
            }
        }

        /// <summary>
        /// Before the cameras a render draws, ahead of the first one's <see cref="Starting"/>: a scriptable pipeline's
        /// <c>beginContextRendering</c>, once a render; the Built-in pipeline draws each camera alone, so before each
        /// (its <c>Camera.onPreCull</c>). What is done once a frame keeps its own count of frames.
        /// </summary>
        public static event Action Rendering
        {
            add
            {
                _rendering += value;
                Hook();
            }
            remove
            {
                _rendering -= value;
                Hook();
            }
        }

        // Unity's callbacks held while anything listens, let go once nothing does.
        static void Hook()
        {
            bool wanted = _starting != null || _ended != null || _rendering != null;
            if (wanted == Hooked) return;
            Hooked = wanted;
            if (wanted)
            {
                RenderPipelineManager.beginContextRendering += BeginContext;
                RenderPipelineManager.beginCameraRendering += BeginCamera;
                RenderPipelineManager.endCameraRendering += EndCamera;
                Camera.onPreCull += PreCull;
                Camera.onPostRender += PostRender;
            }
            else
            {
                RenderPipelineManager.beginContextRendering -= BeginContext;
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                RenderPipelineManager.endCameraRendering -= EndCamera;
                Camera.onPreCull -= PreCull;
                Camera.onPostRender -= PostRender;
            }
        }

        static void BeginContext(ScriptableRenderContext context, List<Camera> cameras) => _rendering?.Invoke();

        static void BeginCamera(ScriptableRenderContext context, Camera camera) => _starting?.Invoke(camera);

        static void EndCamera(ScriptableRenderContext context, Camera camera) => _ended?.Invoke(camera);

        static void PreCull(Camera camera)
        {
            _rendering?.Invoke();
            _starting?.Invoke(camera);
        }

        static void PostRender(Camera camera) => _ended?.Invoke(camera);
    }
}
