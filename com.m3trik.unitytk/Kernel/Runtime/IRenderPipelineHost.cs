using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The render pipeline a project draws with, as unitytk's features ask it: Unity's
    /// host. It answers what each pipeline spells its own way (the blend and additive switches, its
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

        /// <summary>
        /// The additive switch: set <paramref name="material"/> to add its colour, times its alpha, to what is
        /// behind it (a glow: blended, with no depth write), by the names this pipeline's own shader inspector writes,
        /// as a viewer's additive blending draws it.
        /// </summary>
        /// <returns>False where its shader has no additive switch this pipeline knows; the material is unchanged.</returns>
        bool SetAdditive(Material material);

        /// <summary>Render <paramref name="camera"/> into <paramref name="target"/> once, the way this pipeline documents.</summary>
        void Render(Camera camera, RenderTexture target);

        /// <summary>
        /// The size per channel of the table <see cref="Display"/> reads (URP: its asset's colour grading
        /// table size, the only one its colour lookup takes); 0 where it shows none.
        /// </summary>
        int DisplaySize { get; }

        /// <summary>
        /// Show the game cameras' frames (<paramref name="options"/>: every one by default) through one display transform, in place of this pipeline's own
        /// tone mapping: the scene's linear light times <paramref name="scale"/>, clamped to [0, 1], then
        /// looked up in <paramref name="display"/>, a strip lookup table (<see cref="DisplaySize"/> squared
        /// wide, <see cref="DisplaySize"/> high, sRGB in and sRGB out, the layout Unity's colour lookups
        /// read). Disposing what it returns puts the pipeline's own look back.
        /// </summary>
        /// <param name="display">The display transform as a strip lookup table.</param>
        /// <param name="scale">What the scene's light is scaled by first: one over the brightest light the table spans.</param>
        /// <param name="why">Why it is not shown, where it returns null.</param>
        /// <param name="options">Its weight, the effects it quiets and its cameras (null: the defaults, <see cref="DisplayOptions"/>).</param>
        /// <returns>The transform while shown; null where this pipeline cannot show one.</returns>
        System.IDisposable Display(Texture2D display, float scale, out string why, DisplayOptions options = null);
    }
}
