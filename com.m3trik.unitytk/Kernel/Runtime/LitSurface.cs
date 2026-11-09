using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// A glTF 2.0 metallic-roughness material in Unity's terms: what
    /// <see cref="MaterialWriter.Write"/> puts on the render pipeline's own Lit. Its
    /// values are glTF's (linear colours, metal and roughness factors, the alpha mode)
    /// and its maps Unity textures; the one map glTF and Unity lay out differently,
    /// metal / roughness / occlusion, arrives already repacked into Unity's mask layout
    /// (<see cref="MaskMap.Pack"/>). A caller builds one from a glTF material (the
    /// Materials feature, from the <c>materials</c> record or any glTF document).
    /// </summary>
    public sealed class LitSurface
    {
        /// <summary>The base colour, linear RGBA; A is the opacity the alpha mode reads.</summary>
        public Color baseColor = Color.white;

        /// <summary>The base colour map (sRGB), multiplied by <see cref="baseColor"/>; its alpha is the opacity.</summary>
        public Texture baseMap;

        /// <summary>The metal factor, 0 dielectric to 1 metal: read where there is no <see cref="maskMap"/>.</summary>
        public float metallic = 1f;

        /// <summary>The roughness factor, 0 mirror to 1 matte: read where there is no <see cref="maskMap"/>.</summary>
        public float roughness = 1f;

        /// <summary>
        /// The packed mask in Unity's layout (<see cref="MaskMap"/>: R metal, G occlusion,
        /// B 1, A smoothness), the factors baked into it; null for none.
        /// </summary>
        public Texture maskMap;

        /// <summary>Whether <see cref="maskMap"/>'s G holds an occlusion to apply (else it is white).</summary>
        public bool maskHasOcclusion;

        /// <summary>The tangent-space normal map (+Y up, glTF's and Unity's convention), imported as a normal map.</summary>
        public Texture normalMap;

        /// <summary>Scales the normal map's X and Y.</summary>
        public float normalScale = 1f;

        /// <summary>The emission, linear RGB and above 1 where it is brighter (glTF's factor times its strength).</summary>
        public Color emission = Color.black;

        /// <summary>The emission map (sRGB), multiplied by <see cref="emission"/>.</summary>
        public Texture emissionMap;

        /// <summary>How the alpha renders.</summary>
        public LitAlpha alpha = LitAlpha.Opaque;

        /// <summary><see cref="LitAlpha.Mask"/> only: the opacity below which a pixel is cut.</summary>
        public float alphaCutoff = 0.5f;

        /// <summary>Both faces render.</summary>
        public bool doubleSided;

        /// <summary>Shows its base colour and opacity alone: the pipeline's unlit shader (glTF's <c>KHR_materials_unlit</c>).</summary>
        public bool unlit;

        /// <summary>The maps' tiling, in Unity's UV space (origin bottom left).</summary>
        public Vector2 tiling = Vector2.one;

        /// <summary>The maps' offset, in Unity's UV space, applied after the tiling.</summary>
        public Vector2 offset = Vector2.zero;
    }
}
