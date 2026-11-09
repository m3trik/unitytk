using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The render-pipeline host's stand-in: a host a test sets up as the pipeline it
    /// plays (its names, its shaders, what it draws, its blend switch's answer), which
    /// keeps every blend switch it is asked for and every camera it renders. No pipeline
    /// is served by it but the one it is given (<see cref="ServedPipeline"/>), so it is
    /// never a project's host by accident. A member <see cref="IRenderPipelineHost"/>
    /// gains is answered here once, for every feature's tests.
    /// </summary>
    /// <remarks>
    /// Its defaults are URP's names, the scriptable pipeline a Built-in test project
    /// lacks: a test of Built-in asks <see cref="RenderPipelineHosts.BuiltIn"/> itself.
    /// </remarks>
    public sealed class PureRenderPipelineHost : IRenderPipelineHost
    {
        /// <param name="name">The pipeline's name, as a warning says it.</param>
        public PureRenderPipelineHost(string name = "URP-like")
        {
            Name = name;
        }

        /// <inheritdoc/>
        public string Name { get; set; }

        /// <summary>The pipeline asset this host serves, or null for none.</summary>
        public RenderPipelineAsset ServedPipeline { get; set; }

        /// <summary>The table row it names; default: URP's (<see cref="LitTable.Universal"/>).</summary>
        public LitTable Lit { get; set; } = LitTable.Universal;

        /// <inheritdoc/>
        public string BaseColorProperty { get; set; } = "_BaseColor";

        /// <inheritdoc/>
        public string BaseMapProperty { get; set; } = "_BaseMap";

        /// <summary>The unlit shader's name, the same blended or not unless <see cref="BlendedUnlitShaderName"/> says otherwise.</summary>
        public string UnlitShaderName { get; set; } = "Universal Render Pipeline/Unlit";

        /// <summary>The blended unlit shader's name; null: <see cref="UnlitShaderName"/>.</summary>
        public string BlendedUnlitShaderName { get; set; }

        /// <inheritdoc/>
        public string LitShader { get; set; } = "Universal Render Pipeline/Lit";

        /// <summary>Which shaders it draws; default: a subshader tagged for URP (<c>UniversalPipeline</c>).</summary>
        public Func<Shader, bool> DrawsShader { get; set; } = shader => RenderPipelineHosts.Tagged(shader, "UniversalPipeline");

        /// <summary>What its blend switch answers: true, a switch for every shader.</summary>
        public bool Blends { get; set; } = true;

        /// <summary>Every blend switch it was asked for, in order.</summary>
        public List<(Material material, bool blended)> Blended { get; } = new List<(Material, bool)>();

        /// <summary>Every camera it was asked to render, in order.</summary>
        public List<Camera> Rendered { get; } = new List<Camera>();

        /// <inheritdoc/>
        public bool Serves(RenderPipelineAsset pipeline) => pipeline != null && pipeline == ServedPipeline;

        /// <inheritdoc/>
        public string UnlitShader(bool blended) => blended && BlendedUnlitShaderName != null ? BlendedUnlitShaderName : UnlitShaderName;

        /// <inheritdoc/>
        public bool Draws(Shader shader) => shader != null && DrawsShader(shader);

        /// <inheritdoc/>
        public bool SetBlended(Material material, bool blended)
        {
            Blended.Add((material, blended));
            return Blends;
        }

        /// <inheritdoc/>
        public void Render(Camera camera, RenderTexture target) => Rendered.Add(camera);
    }
}
