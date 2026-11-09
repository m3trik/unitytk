using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The render pipeline a project draws with, as unitytk's features ask it: Unity's
    /// host. It answers what each pipeline spells its own way (the blend switch, its
    /// own Lit and Unlit as the declared table states them, <see cref="Lit"/>, which
    /// <see cref="MaterialWriter"/> writes a glTF material through; whether a shader
    /// draws under it) and renders a camera to a texture the way the pipeline documents. The Built-in pipeline's host is the default
    /// (<see cref="BuiltInRenderPipelineHost"/>); a pipeline's door registers its own
    /// with <see cref="RenderPipelineHosts.Register"/>, as URP's does. A feature asks
    /// <see cref="RenderPipelineHosts.Current"/>, never the pipeline asset's type.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity splits a pipeline into its <c>RenderPipelineAsset</c> and the
    /// <c>RenderPipeline</c> tag a shader's subshader carries, which say which pipeline
    /// draws and which shaders it draws, but not what a material's properties are
    /// called or how a script renders a camera outside the loop (Built-in:
    /// <c>Camera.Render</c>; URP: <c>RenderPipeline.SubmitRenderRequest</c>). SRP Core's
    /// <c>RenderPipelineGlobalSettings</c> and Shader Graph's targets were weighed and
    /// not adopted: each lives in a pipeline's own package, which a Built-in project
    /// cannot compile against. The features each told the pipeline apart by its
    /// asset's type name, six different ways; this one seam replaces that.
    /// </remarks>
    public interface IRenderPipelineHost
    {
        /// <summary>The pipeline's name, as a warning says it (<c>Built-in</c>, <c>URP</c>).</summary>
        string Name { get; }

        /// <summary>Whether this host is the one for <paramref name="pipeline"/> (null: the Built-in pipeline).</summary>
        bool Serves(RenderPipelineAsset pipeline);

        /// <summary>
        /// This pipeline's own Lit and Unlit, slot by slot (its row of <see cref="LitTable"/>):
        /// what <see cref="MaterialWriter"/> writes a glTF material through.
        /// </summary>
        LitTable Lit { get; }

        /// <summary>The material property this pipeline's lit and unlit shaders hold their base colour in.</summary>
        string BaseColorProperty { get; }

        /// <summary>The texture property this pipeline's lit and unlit shaders hold their base map (albedo) in.</summary>
        string BaseMapProperty { get; }

        /// <summary>The name of this pipeline's unlit shader: one that blends its alpha when <paramref name="blended"/>.</summary>
        string UnlitShader(bool blended);

        /// <summary>
        /// The name of this pipeline's lit shader: its metallic, physically based one, which
        /// Unity's own model import maps a material to (Built-in's <c>Standard</c>, URP's
        /// <c>Universal Render Pipeline/Lit</c>).
        /// </summary>
        string LitShader { get; }

        /// <summary>Whether this pipeline draws <paramref name="shader"/>: one of its subshaders is written for it.</summary>
        bool Draws(Shader shader);

        /// <summary>
        /// The blend switch: set <paramref name="material"/> to blend its alpha over the
        /// background, or back to opaque, by the property and keyword names this
        /// pipeline's own shader inspector writes, so opening the material later lands
        /// on the same state.
        /// </summary>
        /// <returns>False when its shader has no blend switch this pipeline knows; the material is unchanged.</returns>
        bool SetBlended(Material material, bool blended);

        /// <summary>Render <paramref name="camera"/> into <paramref name="target"/> once, the way this pipeline documents.</summary>
        void Render(Camera camera, RenderTexture target);
    }
}
