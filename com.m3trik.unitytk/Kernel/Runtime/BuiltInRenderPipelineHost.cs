using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The Built-in render pipeline as unitytk's features ask it: the default
    /// <see cref="IRenderPipelineHost"/>, serving a project with no pipeline asset.
    /// </summary>
    /// <remarks>
    /// Prior art: the blend switch is the Standard shader inspector's own
    /// (<c>StandardShaderGUI.SetupMaterialWithBlendMode</c>, its Fade mode), which a
    /// script cannot call outside the editor; <c>Camera.Render</c> is the documented
    /// way to render a camera on demand in this pipeline.
    /// </remarks>
    public sealed class BuiltInRenderPipelineHost : IRenderPipelineHost
    {
        /// <inheritdoc/>
        public string Name => "Built-in";

        /// <inheritdoc/>
        public bool Serves(RenderPipelineAsset pipeline) => pipeline == null;

        /// <inheritdoc/>
        public LitTable Lit => LitTable.BuiltIn;

        /// <inheritdoc/>
        public string BaseColorProperty => Lit.BaseColor;

        /// <inheritdoc/>
        public string BaseMapProperty => Lit.BaseMap;

        /// <inheritdoc/>
        public string UnlitShader(bool blended) => blended ? Lit.UnlitBlended : Lit.Unlit;

        /// <inheritdoc/>
        public string LitShader => Lit.Lit;

        /// <inheritdoc/>
        public bool Draws(Shader shader) => RenderPipelineHosts.Tagged(shader, "");

        /// <inheritdoc/>
        public bool SetBlended(Material material, bool blended)
        {
            // The switch is four properties, and a shader holding them all is one the Standard
            // inspector's Fade drives: a custom shader with a _Mode but its blending written in
            // its passes would take the keyword, tag and queue with no blend to show for them.
            if (material == null || !material.HasProperty("_Mode") || !material.HasProperty("_SrcBlend")
                || !material.HasProperty("_DstBlend") || !material.HasProperty("_ZWrite")) return false;
            // Standard, Standard (Specular setup) and Autodesk Interactive: Fade, or Opaque.
            material.SetFloat("_Mode", blended ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)(blended ? BlendMode.SrcAlpha : BlendMode.One));
            material.SetFloat("_DstBlend", (float)(blended ? BlendMode.OneMinusSrcAlpha : BlendMode.Zero));
            material.SetFloat("_ZWrite", blended ? 0f : 1f);
            material.DisableKeyword("_ALPHATEST_ON");
            if (blended) material.EnableKeyword("_ALPHABLEND_ON");
            else material.DisableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.SetOverrideTag("RenderType", blended ? "Transparent" : "");
            material.renderQueue = blended ? (int)RenderQueue.Transparent : -1;
            return true;
        }

        /// <inheritdoc/>
        public void Render(Camera camera, RenderTexture target)
        {
            RenderTexture was = camera.targetTexture;
            camera.targetTexture = target;
            try
            {
                camera.Render();
            }
            finally
            {
                camera.targetTexture = was;
            }
        }
    }
}
