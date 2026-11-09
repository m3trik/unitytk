using System.Collections.Generic;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// One render pipeline's own Lit and Unlit shaders as <see cref="MaterialWriter"/>
    /// writes them: each slot of a glTF material by the property and keyword names the
    /// pipeline's shaders and inspectors use. The rows are declared here, side by side
    /// (<see cref="BuiltIn"/>, <see cref="Universal"/>, <see cref="HighDefinition"/>),
    /// and each pipeline's host names its own (<see cref="IRenderPipelineHost.Lit"/>).
    /// A property a row leaves null is a slot that pipeline does not have.
    /// </summary>
    /// <remarks>
    /// The names are the shaders' own: Built-in's <c>Standard</c> and <c>Unlit/*</c>,
    /// URP 17's <c>Lit</c> and <c>Unlit</c> (<c>LitInput.hlsl</c>, <c>BaseShaderGUI</c>),
    /// HDRP 17's <c>HDRP/Lit</c> and <c>HDRP/Unlit</c> (<c>LitProperties.hlsl</c>). Every
    /// pipeline reads metal and smoothness from one packed map, never glTF's layout
    /// (roughness G, metal B), so the mask map is Unity's one layout (<see cref="MaskMap"/>),
    /// which Standard and URP Lit read as their metallic map (R metal, A smoothness) and
    /// occlusion map (G), and HDRP Lit as its mask map (R metal, G occlusion, B detail
    /// mask, A smoothness). Each pipeline applies the base map's tiling and offset to
    /// every main map. HDRP's row is declared and held to its tests; a project draws with
    /// it once an HDRP door registers a host that names it (none does yet), which also
    /// runs HDRP's own material validation after a write (its keywords and passes come
    /// from <c>HDShaderUtils.ResetMaterialKeywords</c>, in HDRP's editor package).
    /// </remarks>
    public sealed class LitTable
    {
        LitTable() { }

        /// <summary>The pipeline's name, as a warning says it.</summary>
        public string Pipeline { get; private set; }

        /// <summary>The lit shader: the pipeline's own metallic, physically based Lit.</summary>
        public string Lit { get; private set; }

        /// <summary>The unlit shader.</summary>
        public string Unlit { get; private set; }

        /// <summary>The unlit shader for a blended surface where the pipeline has one apart (Built-in), else null: <see cref="Unlit"/>, through the blend switch.</summary>
        public string UnlitBlended { get; private set; }

        /// <summary>The unlit shader for a cut-out surface where the pipeline has one apart (Built-in), else null.</summary>
        public string UnlitCutout { get; private set; }

        /// <summary>The unlit shader for a surface with no base map where the pipeline has one apart (Built-in), else null.</summary>
        public string UnlitUntextured { get; private set; }

        /// <summary>Whether the unlit shaders tint their map by the base colour (Built-in's do not).</summary>
        public bool UnlitTints { get; private set; } = true;

        /// <summary>The base colour (RGBA, the alpha the opacity).</summary>
        public string BaseColor { get; private set; }

        /// <summary>The base colour map.</summary>
        public string BaseMap { get; private set; }

        /// <summary>The unlit shader's colour, where it differs from <see cref="BaseColor"/> (HDRP), else null.</summary>
        public string UnlitColor { get; private set; }

        /// <summary>The unlit shader's map, where it differs from <see cref="BaseMap"/> (HDRP), else null.</summary>
        public string UnlitMap { get; private set; }

        /// <summary>
        /// A second colour and map the lit shader keeps beside its own and reads for its
        /// baked lighting (URP Lit's hidden <c>_Color</c> and <c>_MainTex</c>), or null.
        /// </summary>
        public string MetaColor { get; private set; }

        /// <summary>The map of the pair <see cref="MetaColor"/> names, or null.</summary>
        public string MetaMap { get; private set; }

        /// <summary>The metal value, read with no mask map.</summary>
        public string Metallic { get; private set; }

        /// <summary>The smoothness value (1 - roughness), read with no mask map.</summary>
        public string Smoothness { get; private set; }

        /// <summary>The packed map in Unity's mask layout (<see cref="MaskMap"/>).</summary>
        public string MaskMap { get; private set; }

        /// <summary>The keyword that makes the shader sample <see cref="MaskMap"/>.</summary>
        public string MaskKeyword { get; private set; }

        /// <summary>The occlusion map, read from G; null where the mask map carries it (HDRP).</summary>
        public string OcclusionMap { get; private set; }

        /// <summary>The keyword that makes the shader sample <see cref="OcclusionMap"/>, or null for none needed.</summary>
        public string OcclusionKeyword { get; private set; }

        /// <summary>The occlusion's strength, or null.</summary>
        public string OcclusionStrength { get; private set; }

        /// <summary>The normal map.</summary>
        public string NormalMap { get; private set; }

        /// <summary>The normal map's strength.</summary>
        public string NormalScale { get; private set; }

        /// <summary>The keywords that make the shader sample <see cref="NormalMap"/>.</summary>
        public IReadOnlyList<string> NormalKeywords { get; private set; }

        /// <summary>The emission colour (HDR).</summary>
        public string EmissionColor { get; private set; }

        /// <summary>The emission map.</summary>
        public string EmissionMap { get; private set; }

        /// <summary>The keyword that turns emission on (Built-in, URP: <c>_EMISSION</c>), or that samples the map (HDRP).</summary>
        public string EmissionKeyword { get; private set; }

        /// <summary>Whether <see cref="EmissionKeyword"/> is needed for emission at all (true), or only for its map (HDRP).</summary>
        public bool EmissionKeywordForMapOnly { get; private set; }

        /// <summary>The alpha-clip toggle, or null where the pipeline's switch is <see cref="Clipped"/> alone (Built-in).</summary>
        public string AlphaClip { get; private set; }

        /// <summary>The alpha cutoff.</summary>
        public string Cutoff { get; private set; }

        /// <summary>The keyword a cut-out surface enables.</summary>
        public string AlphaTestKeyword { get; private set; }

        /// <summary>The face culling mode (<c>UnityEngine.Rendering.CullMode</c>: 0 both faces, 2 the front), or null where the shader culls back faces always (Built-in's Standard).</summary>
        public string Cull { get; private set; }

        /// <summary>A double-sided toggle beside the cull mode (HDRP), or null.</summary>
        public string DoubleSided { get; private set; }

        /// <summary>The keyword a double-sided surface enables (HDRP), or null.</summary>
        public string DoubleSidedKeyword { get; private set; }

        /// <summary>Values every write sets on the lit shader (URP's metallic workflow, smoothness from the map's alpha).</summary>
        public IReadOnlyList<KeyValuePair<string, float>> Fixed { get; private set; } = new KeyValuePair<string, float>[0];

        /// <summary>Values a write with a mask map sets: the scales and remaps the map is read through, at identity.</summary>
        public IReadOnlyList<KeyValuePair<string, float>> Masked { get; private set; } = new KeyValuePair<string, float>[0];

        /// <summary>Values a cut-out surface sets besides the toggle, cutoff and keyword (Built-in's Cutout mode).</summary>
        public IReadOnlyList<KeyValuePair<string, float>> Clipped { get; private set; } = new KeyValuePair<string, float>[0];

        /// <summary>This row on other shaders: a test's stand-in for a pipeline the project lacks.</summary>
        internal LitTable WithShaders(string lit, string unlit)
        {
            var copy = (LitTable)MemberwiseClone();
            copy.Lit = lit;
            copy.Unlit = unlit;
            return copy;
        }

        static KeyValuePair<string, float>[] Values(params (string property, float value)[] values)
        {
            var pairs = new KeyValuePair<string, float>[values.Length];
            for (int i = 0; i < values.Length; i++) pairs[i] = new KeyValuePair<string, float>(values[i].property, values[i].value);
            return pairs;
        }

        /// <summary>The Built-in pipeline's: <c>Standard</c> and the four <c>Unlit/*</c> shaders.</summary>
        public static LitTable BuiltIn { get; } = new LitTable
        {
            Pipeline = "Built-in",
            Lit = "Standard",
            Unlit = "Unlit/Texture",
            UnlitBlended = "Unlit/Transparent",
            UnlitCutout = "Unlit/Transparent Cutout",
            UnlitUntextured = "Unlit/Color",
            UnlitTints = false,
            BaseColor = "_Color",
            BaseMap = "_MainTex",
            Metallic = "_Metallic",
            Smoothness = "_Glossiness",
            MaskMap = "_MetallicGlossMap",
            MaskKeyword = "_METALLICGLOSSMAP",
            OcclusionMap = "_OcclusionMap",
            OcclusionStrength = "_OcclusionStrength",
            NormalMap = "_BumpMap",
            NormalScale = "_BumpScale",
            NormalKeywords = new[] { "_NORMALMAP" },
            EmissionColor = "_EmissionColor",
            EmissionMap = "_EmissionMap",
            EmissionKeyword = "_EMISSION",
            Cutoff = "_Cutoff",
            AlphaTestKeyword = "_ALPHATEST_ON",
            Fixed = Values(("_SmoothnessTextureChannel", 0f)),
            Masked = Values(("_GlossMapScale", 1f)),
            Clipped = Values(("_Mode", 1f)),
        };

        /// <summary>URP's: <c>Universal Render Pipeline/Lit</c> and <c>/Unlit</c>.</summary>
        public static LitTable Universal { get; } = new LitTable
        {
            Pipeline = "URP",
            Lit = "Universal Render Pipeline/Lit",
            Unlit = "Universal Render Pipeline/Unlit",
            BaseColor = "_BaseColor",
            BaseMap = "_BaseMap",
            MetaColor = "_Color",
            MetaMap = "_MainTex",
            Metallic = "_Metallic",
            Smoothness = "_Smoothness",
            MaskMap = "_MetallicGlossMap",
            MaskKeyword = "_METALLICSPECGLOSSMAP",
            OcclusionMap = "_OcclusionMap",
            OcclusionKeyword = "_OCCLUSIONMAP",
            OcclusionStrength = "_OcclusionStrength",
            NormalMap = "_BumpMap",
            NormalScale = "_BumpScale",
            NormalKeywords = new[] { "_NORMALMAP" },
            EmissionColor = "_EmissionColor",
            EmissionMap = "_EmissionMap",
            EmissionKeyword = "_EMISSION",
            AlphaClip = "_AlphaClip",
            Cutoff = "_Cutoff",
            AlphaTestKeyword = "_ALPHATEST_ON",
            Cull = "_Cull",
            Fixed = Values(("_WorkflowMode", 1f), ("_SmoothnessTextureChannel", 0f)),
            Masked = Values(("_Smoothness", 1f)),
            Clipped = Values(("_AlphaToMask", 1f)),
        };

        /// <summary>HDRP's: <c>HDRP/Lit</c> and <c>HDRP/Unlit</c> (no host serves it yet: see the remarks).</summary>
        public static LitTable HighDefinition { get; } = new LitTable
        {
            Pipeline = "HDRP",
            Lit = "HDRP/Lit",
            Unlit = "HDRP/Unlit",
            BaseColor = "_BaseColor",
            BaseMap = "_BaseColorMap",
            UnlitColor = "_UnlitColor",
            UnlitMap = "_UnlitColorMap",
            Metallic = "_Metallic",
            Smoothness = "_Smoothness",
            MaskMap = "_MaskMap",
            MaskKeyword = "_MASKMAP",
            NormalMap = "_NormalMap",
            NormalScale = "_NormalScale",
            NormalKeywords = new[] { "_NORMALMAP", "_NORMALMAP_TANGENT_SPACE" },
            EmissionColor = "_EmissiveColor",
            EmissionMap = "_EmissiveColorMap",
            EmissionKeyword = "_EMISSIVE_COLOR_MAP",
            EmissionKeywordForMapOnly = true,
            AlphaClip = "_AlphaCutoffEnable",
            Cutoff = "_AlphaCutoff",
            AlphaTestKeyword = "_ALPHATEST_ON",
            Cull = "_CullMode",
            DoubleSided = "_DoubleSidedEnable",
            DoubleSidedKeyword = "_DOUBLESIDED_ON",
            Fixed = Values(("_UseEmissiveIntensity", 0f)),
            Masked = Values(("_MetallicRemapMin", 0f), ("_MetallicRemapMax", 1f), ("_SmoothnessRemapMin", 0f),
                            ("_SmoothnessRemapMax", 1f), ("_AORemapMin", 0f), ("_AORemapMax", 1f)),
        };
    }
}
