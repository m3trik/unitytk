using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Scripting;

// The only player code here is the registration, which nothing references: kept, or
// managed code stripping drops the assembly and a player falls back to Built-in answers.
[assembly: AlwaysLinkAssembly]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Kernel.URP.Tests")]

namespace M3trik.Unitytk.Kernel.URP
{
    /// <summary>
    /// URP as unitytk's features ask it (<see cref="IRenderPipelineHost"/>): the door
    /// registers it with <see cref="RenderPipelineHosts"/> as the editor or a player
    /// loads, and it serves a project whose pipeline asset is URP's.
    /// </summary>
    /// <remarks>
    /// Prior art: the blend switch is URP's own material inspector's
    /// (<c>BaseShaderGUI.SetupMaterialBlendModeInternal</c>), an editor class a player
    /// cannot call; a camera renders through <c>RenderPipeline.SubmitRenderRequest</c>
    /// with a <c>StandardRequest</c>, the route URP's manual documents for rendering
    /// a camera outside the loop (<c>Camera.Render</c> works under URP but is not
    /// documented there). <c>UniversalRenderPipeline.SingleCameraRequest</c> was
    /// weighed: it renders the one camera without its stack or post-processing.
    /// </remarks>
    sealed class UniversalRenderPipelineHost : IRenderPipelineHost
    {
        /// <summary>The <c>RenderPipeline</c> tag URP's shaders carry.</summary>
        const string ShaderTag = "UniversalPipeline";

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        [Preserve]
        static void Register() => RenderPipelineHosts.Register(new UniversalRenderPipelineHost());

        /// <inheritdoc/>
        public string Name => "URP";

        /// <inheritdoc/>
        public bool Serves(RenderPipelineAsset pipeline) => pipeline is UniversalRenderPipelineAsset;

        /// <summary>
        /// The <c>LightMode</c>s URP draws a subshader tagged for no pipeline through: its
        /// forward passes, and a legacy unlit pass, one that states none, as
        /// <c>SRPDefaultUnlit</c>. A copy of the shader tags URP's <c>DrawObjectsPass</c>
        /// draws with (<c>Runtime/Passes/DrawObjectsPass.cs</c>, Unity 6.3 / URP 17.3), plus
        /// the empty <c>LightMode</c> it reads as <c>SRPDefaultUnlit</c>.
        /// </summary>
        static readonly string[] UntaggedLightModes = { "", "SRPDefaultUnlit", "UniversalForward", "UniversalForwardOnly" };

        /// <inheritdoc/>
        public LitTable Lit => LitTable.Universal;

        /// <inheritdoc/>
        public string BaseColorProperty => Lit.BaseColor;

        /// <inheritdoc/>
        public string BaseMapProperty => Lit.BaseMap;

        /// <inheritdoc/>
        public string UnlitShader(bool blended) => Lit.Unlit;

        /// <inheritdoc/>
        public string LitShader => Lit.Lit;

        /// <inheritdoc/>
        /// <remarks>
        /// A subshader written for URP, or one written for no pipeline whose pass URP
        /// draws (<see cref="UntaggedLightModes"/>) and that holds none of Built-in's own
        /// lighting passes (<see cref="RenderPipelineHosts.DrawnThroughUntaggedSubshader"/>): a legacy
        /// unlit shader draws in URP too; Built-in's lit shaders (<c>ForwardBase</c>
        /// passes, which URP draws with its error shader) do not, a helper pass beside
        /// them notwithstanding.
        /// </remarks>
        public bool Draws(Shader shader)
            => RenderPipelineHosts.Tagged(shader, ShaderTag) || RenderPipelineHosts.DrawnThroughUntaggedSubshader(shader, UntaggedLightModes);

        /// <inheritdoc/>
        public bool SetBlended(Material material, bool blended)
        {
            if (material == null || !material.HasProperty("_Surface")) return false;
            // URP Lit, Simple Lit, Baked Lit and Unlit, and Shader Graphs with material override: Transparent, Alpha.
            bool clipped = material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > 0.5f;
            material.SetFloat("_Surface", blended ? 1f : 0f);
            SetIfPresent(material, "_Blend", 0f);
            SetIfPresent(material, "_BlendModePreserveSpecular", 0f);
            SetIfPresent(material, "_SrcBlend", (float)(blended ? BlendMode.SrcAlpha : BlendMode.One));
            SetIfPresent(material, "_DstBlend", (float)(blended ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            SetIfPresent(material, "_SrcBlendAlpha", (float)BlendMode.One);
            SetIfPresent(material, "_DstBlendAlpha", (float)(blended ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            SetIfPresent(material, "_ZWrite", blended ? 0f : 1f);
            if (blended) material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_ALPHAMODULATE_ON");
            material.SetOverrideTag("RenderType", blended ? "Transparent" : clipped ? "TransparentCutout" : "Opaque");
            material.SetShaderPassEnabled("DepthOnly", !blended);
            int queue = blended ? (int)RenderQueue.Transparent : clipped ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            material.renderQueue = queue + (material.HasProperty("_QueueOffset") ? (int)material.GetFloat("_QueueOffset") : 0);
            return true;
        }

        /// <inheritdoc/>
        public void Render(Camera camera, RenderTexture target)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(camera, request))
                RenderPipeline.SubmitRenderRequest(camera, request);
            else
                RenderPipelineHosts.BuiltIn.Render(camera, target);
        }

        static void SetIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }
    }
}
