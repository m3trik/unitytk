using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The one subscriber to Unity's per-camera render callbacks, for every runtime part that acts as a camera
    /// draws, the doors' among them. A scriptable pipeline raises <see cref="RenderPipelineManager"/>'s callbacks and
    /// the Built-in pipeline <see cref="Camera"/>'s, each never the other's, so this hooks both while anything listens
    /// and gives each listener one event whichever pipeline draws: <see cref="Rendering"/> for each camera a render
    /// draws, before the first of them starts, then <see cref="Starting"/> and <see cref="Ended"/> as each one draws.
    /// It hooks them as its first listener joins and lets them go as its last leaves.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's own callbacks (<c>RenderPipelineManager.beginContextRendering</c>,
    /// <c>beginCameraRendering</c> and <c>endCameraRendering</c>, which every scriptable pipeline raises, and the
    /// Built-in pipeline's <c>Camera.onPreCull</c> and <c>Camera.onPostRender</c>) are adopted as they stand, held
    /// here alone so a listener never subscribes to both families or forgets to let one go. Which family a pipeline
    /// raises is Unity's, not a pipeline's names, so it is answered here once rather than by
    /// <see cref="IRenderPipelineHost"/>, which a pipeline no door serves (HDRP) has none of. The listeners are the
    /// registry the C# rules allow: each joins again as its part is switched on after a domain reload.
    /// </remarks>
    public static class CameraDrawing
    {
        static Action<Camera> _rendering, _starting, _ended;

        /// <summary>Whether it holds Unity's callbacks now: while anything listens.</summary>
        internal static bool Hooked { get; private set; }

        /// <summary>
        /// How many listen now, across its three events (one listening to two counts twice), each one leaving taking
        /// one off. It allocates; never read it per frame.
        /// </summary>
        public static int Listeners => Count(_rendering) + Count(_starting) + Count(_ended);

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
        /// Each camera a render draws, before the first of them starts (ahead of its <see cref="Starting"/>): a
        /// scriptable pipeline's <c>beginContextRendering</c>, raised for each camera in it; the Built-in pipeline draws
        /// each camera alone, so for each as it starts (its <c>Camera.onPreCull</c>). What a camera reads before its own
        /// drawing starts (its post-processing switch) is set here; what is done once a frame keeps its own count of frames.
        /// </summary>
        public static event Action<Camera> Rendering
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

        static void BeginContext(ScriptableRenderContext context, List<Camera> cameras)
        {
            Action<Camera> rendering = _rendering;
            if (rendering == null) return;
            for (int i = 0; i < cameras.Count; i++) rendering(cameras[i]);
        }

        static void BeginCamera(ScriptableRenderContext context, Camera camera) => _starting?.Invoke(camera);

        static void EndCamera(ScriptableRenderContext context, Camera camera) => _ended?.Invoke(camera);

        static void PreCull(Camera camera)
        {
            _rendering?.Invoke(camera);
            _starting?.Invoke(camera);
        }

        static void PostRender(Camera camera) => _ended?.Invoke(camera);

        static int Count(Delegate listeners) => listeners == null ? 0 : listeners.GetInvocationList().Length;
    }
}
