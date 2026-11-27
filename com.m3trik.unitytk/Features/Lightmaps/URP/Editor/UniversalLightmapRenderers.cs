using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace M3trik.Unitytk.Lightmaps.URP
{
    /// <summary>
    /// URP's answer to what it does to a Lightmap Set's surfaces (<see cref="ILightmapRenderers"/>): it draws them
    /// with Baked Lit, made on import from its own Lit (<see cref="BakedLitImporter"/>); a Universal Renderer on the
    /// Forward+ path reads reflection probes through its atlas where its pipeline asset blends probes through one;
    /// a renderer's Screen Space Ambient Occlusion feature, switched on, draws over the lightmapped surfaces. Each fix
    /// is the renderer's own setting, as its inspector writes it: the Forward path; the feature switched off.
    /// </summary>
    /// <remarks>
    /// Prior art: URP's own <c>UniversalRendererData.renderingMode</c>,
    /// <c>UniversalRenderPipelineAsset.reflectionProbeBlending</c> and <c>reflectionProbeAtlas</c>, and
    /// <c>ScriptableRendererFeature.SetActive</c>, the settings its inspectors show; measured (6000.3.10f1, URP 17.3)
    /// on a production room, the same frame on the Forward path reflects the room clean, and a larger probe box, no
    /// normal map or another smoothness change nothing.
    /// </remarks>
    [Provides(typeof(ILightmapRenderers))]
    internal sealed class UniversalLightmapRenderers : ILightmapRenderers
    {
        /// <inheritdoc/>
        public string BakedLit(RenderPipelineAsset pipeline) => pipeline is UniversalRenderPipelineAsset ? BakedLitImporter.Stub : null;

        /// <inheritdoc/>
        public IReadOnlyList<ScriptableObject> ProbeAtlas(RenderPipelineAsset pipeline)
        {
            var found = new List<ScriptableObject>();
            if (!(pipeline is UniversalRenderPipelineAsset urp) || !urp.reflectionProbeBlending || !urp.reflectionProbeAtlas) return found;
            foreach (ScriptableRendererData data in Renderers(urp))
                if (data is UniversalRendererData universal && universal.renderingMode == RenderingMode.ForwardPlus) found.Add(universal);
            return found;
        }

        /// <inheritdoc/>
        public void ReadProbesWhole(ScriptableObject renderer) => ((UniversalRendererData)renderer).renderingMode = RenderingMode.Forward;

        /// <inheritdoc/>
        public IReadOnlyList<ScriptableObject> Occlusion(RenderPipelineAsset pipeline)
        {
            var found = new List<ScriptableObject>();
            if (!(pipeline is UniversalRenderPipelineAsset urp)) return found;
            foreach (ScriptableRendererData data in Renderers(urp))
                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                    if (feature is ScreenSpaceAmbientOcclusion && feature.isActive) found.Add(feature);
            return found;
        }

        /// <inheritdoc/>
        public void SwitchOff(ScriptableObject occlusion) => ((ScriptableRendererFeature)occlusion).SetActive(false);

        // The renderers the pipeline asset draws with, each once.
        static List<ScriptableRendererData> Renderers(UniversalRenderPipelineAsset urp)
        {
            var renderers = new List<ScriptableRendererData>();
            foreach (ScriptableRendererData data in urp.rendererDataList)
                if (data != null && !renderers.Contains(data)) renderers.Add(data);
            return renderers;
        }
    }
}
