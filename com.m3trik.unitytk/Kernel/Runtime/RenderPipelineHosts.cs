using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Kernel.Tests")]

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The render-pipeline hosts this editor or player knows: the Built-in default and
    /// each one a pipeline's door registers as it loads (URP's, from
    /// <c>M3trik.Unitytk.Kernel.URP</c>). A feature asks <see cref="Current"/>.
    /// </summary>
    /// <remarks>
    /// The registry is the one piece of mutable static state the C# rules allow: the
    /// doors fill it again after every domain reload and as a player loads. It holds
    /// plain objects, never a Unity object, and is read on the main thread alone.
    /// </remarks>
    public static class RenderPipelineHosts
    {
        static readonly List<IRenderPipelineHost> _hosts = new List<IRenderPipelineHost>();

        /// <summary>The Built-in pipeline's host, which serves a project with no pipeline asset.</summary>
        public static IRenderPipelineHost BuiltIn { get; } = new BuiltInRenderPipelineHost();

        /// <summary>The host of the pipeline drawing now (the quality level's asset, else the default one), or null when none serves it.</summary>
        public static IRenderPipelineHost Current => For(GraphicsSettings.currentRenderPipeline);

        /// <summary>
        /// The host of <paramref name="pipeline"/> (null: the Built-in pipeline), or null
        /// when no registered host serves it: a pipeline whose door is absent, such as
        /// HDRP, which unitytk has none for.
        /// </summary>
        public static IRenderPipelineHost For(RenderPipelineAsset pipeline)
        {
            foreach (IRenderPipelineHost host in _hosts)
                if (host.Serves(pipeline))
                    return host;
            return BuiltIn.Serves(pipeline) ? BuiltIn : null;
        }

        /// <summary>
        /// The pipeline <paramref name="host"/> serves, as a message names it: its name;
        /// for none, the pipeline drawing now by its asset's type and name (a pipeline no
        /// door serves, such as HDRP) -- a name in a message, never a branch on it.
        /// </summary>
        public static string NameOf(IRenderPipelineHost host)
        {
            if (host != null) return host.Name;
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline == null ? "active" : $"{pipeline.GetType().Name} ('{pipeline.name}')";
        }

        /// <summary>
        /// Add <paramref name="host"/>, replacing one of its type registered before (a
        /// door registers again after each domain reload, and as each play mode starts).
        /// A door calls it from its <c>[RuntimeInitializeOnLoadMethod]</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="host"/> is null.</exception>
        public static void Register(IRenderPipelineHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            for (int i = 0; i < _hosts.Count; i++)
            {
                if (_hosts[i].GetType() != host.GetType()) continue;
                _hosts[i] = host;
                return;
            }
            _hosts.Add(host);
        }

        /// <summary>Take <paramref name="host"/> out again; whether it was registered (a test's fake).</summary>
        internal static bool Unregister(IRenderPipelineHost host) => _hosts.Remove(host);

        /// <summary>
        /// Whether one of <paramref name="shader"/>'s subshaders carries the
        /// <c>RenderPipeline</c> tag <paramref name="pipelineTag"/>: the pipeline's
        /// <c>renderPipelineShaderTag</c> (URP's <c>UniversalPipeline</c>), or empty for one
        /// tagged for no pipeline, as the Built-in pipeline's shaders are.
        /// </summary>
        public static bool Tagged(Shader shader, string pipelineTag)
        {
            if (shader == null) return false;
            var tag = new ShaderTagId("RenderPipeline");
            for (int i = 0; i < shader.subshaderCount; i++)
            {
                string value = shader.FindSubshaderTagValue(i, tag).name;
                if (string.Equals(value ?? "", pipelineTag ?? "", StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The Built-in pipeline's own lighting passes (Unity's manual, <i>Pass tags in the
        /// Built-in Render Pipeline</i>): a subshader holding one is written for Built-in.
        /// A scriptable pipeline draws its object with an error shader in the editor and a
        /// development build alone (URP 17.3's <c>RenderingUtils</c> legacy pass names, its
        /// error draw <c>[Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]</c>),
        /// and not at all in a release player, nor ever for <c>ForwardAdd</c>,
        /// <c>Deferred</c> or <c>PrepassFinal</c>. Its utility passes (<c>ShadowCaster</c>,
        /// <c>MotionVectors</c>, <c>Meta</c>) are not here: they draw no colour.
        /// </summary>
        static readonly string[] BuiltInLightingModes =
        {
            "Always", "ForwardBase", "ForwardAdd", "Deferred", "PrepassBase", "PrepassFinal",
            "Vertex", "VertexLM", "VertexLMRGBM",
        };

        /// <summary>
        /// Whether a scriptable pipeline that draws the passes <paramref name="lightModes"/>
        /// (empty: a pass that states no <c>LightMode</c>) draws <paramref name="shader"/>
        /// through one of its subshaders tagged for no pipeline: one holding a pass of those
        /// modes and none of the Built-in pipeline's own lighting passes (<c>Always</c>,
        /// <c>ForwardBase</c>, <c>ForwardAdd</c>, <c>Deferred</c>, <c>PrepassBase</c>,
        /// <c>PrepassFinal</c>, <c>Vertex</c>, <c>VertexLM</c>, <c>VertexLMRGBM</c>), whose
        /// object such a pipeline draws as an error (in the editor and a development build)
        /// or not at all. So a Built-in lit shader is never drawn this way, even asked for its
        /// own modes. Modes are compared ignoring case: Unity reports Built-in's upper-cased
        /// (<c>FORWARDBASE</c>). URP draws a legacy unlit pass, one with no <c>LightMode</c>,
        /// as <c>SRPDefaultUnlit</c>. Any other pass -- a shadow caster, a depth prepass, a
        /// <c>LightMode</c> no pipeline draws by itself (a command buffer's) -- neither
        /// counts nor disqualifies.
        /// </summary>
        /// <remarks>
        /// Not every pass is required to be one the pipeline draws or a utility pass: a
        /// shader may hold a pass of a <c>LightMode</c> no camera draws, which a command
        /// buffer renders by index, and still draw in the pipeline through its other passes.
        /// </remarks>
        public static bool DrawnThroughUntaggedSubshader(Shader shader, params string[] lightModes)
        {
            if (shader == null || lightModes == null) return false;
            var pipeline = new ShaderTagId("RenderPipeline");
            var lightMode = new ShaderTagId("LightMode");
            for (int s = 0; s < shader.subshaderCount; s++)
            {
                if (!string.IsNullOrEmpty(shader.FindSubshaderTagValue(s, pipeline).name)) continue;
                bool drawn = false, builtInLit = false;
                for (int p = 0; p < shader.GetPassCountInSubshader(s); p++)
                {
                    string mode = shader.FindPassTagValue(s, p, lightMode).name ?? "";
                    drawn |= OneOf(mode, lightModes);
                    builtInLit |= OneOf(mode, BuiltInLightingModes);
                }
                if (drawn && !builtInLit) return true;
            }
            return false;
        }

        static bool OneOf(string mode, string[] modes)
        {
            foreach (string wanted in modes)
                if (string.Equals(mode, wanted ?? "", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
