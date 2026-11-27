using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Lightmaps
{
    /// <summary>
    /// What a render pipeline does to a Lightmap Set's surfaces, as its door answers it (<c>[Provides]</c>; URP's is
    /// the feature's URP door): the shader it draws them with by the rule their import gives them
    /// (<see cref="BakedLit"/>), and what its own settings do to them, each with its fix.
    /// <see cref="LightmapsActions.Validate(OpContext)"/> names what it finds, and
    /// <see cref="LightmapsActions.ReadProbesWhole(OpContext)"/> and
    /// <see cref="LightmapsActions.SwitchOcclusionOff(OpContext)"/> fix it, each asked first. A pipeline with no door
    /// (Built-in) has nothing to find, and its materials stay on its Lit.
    /// </summary>
    internal interface ILightmapRenderers
    {
        /// <summary>
        /// The asset path of the shader <paramref name="pipeline"/> (null: none) draws a set's lightmapped surfaces with
        /// by the reflection rule their import gives them (URP's Baked Lit, made on import from its installed Lit), or
        /// null where this door has none for it.
        /// </summary>
        string BakedLit(RenderPipelineAsset pipeline);

        /// <summary>
        /// The renderers of <paramref name="pipeline"/> (null: none) that read reflection probes through an atlas, which
        /// breaks a box-projected probe into scattered dark texels on the room it reflects (URP's Forward+ path).
        /// </summary>
        IReadOnlyList<ScriptableObject> ProbeAtlas(RenderPipelineAsset pipeline);

        /// <summary>Give <paramref name="renderer"/>, one <see cref="ProbeAtlas"/> named, a path that reads each probe whole (URP's Forward).</summary>
        void ReadProbesWhole(ScriptableObject renderer);

        /// <summary>
        /// The screen-space ambient occlusion each renderer of <paramref name="pipeline"/> (null: none) draws, switched
        /// on: it darkens and speckles what lightmaps already shade (they hold its occlusion).
        /// </summary>
        IReadOnlyList<ScriptableObject> Occlusion(RenderPipelineAsset pipeline);

        /// <summary>Switch <paramref name="occlusion"/>, one <see cref="Occlusion"/> named, off.</summary>
        void SwitchOff(ScriptableObject occlusion);
    }
}
