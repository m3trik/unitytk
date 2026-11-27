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
            // Shadows as URP's own validation sets them (BaseShaderGUI.UpdateMaterialSurfaceOptions, run on every material
            // an asset import brings in): the Cast Shadows switch where the shader has one, else an opaque surface casts and
            // a transparent one does not. Left as the original was, a twin saved in an asset came back from its import with
            // its shadows off while the one made at runtime kept them, and never read as made.
            material.SetShaderPassEnabled("ShadowCaster", material.HasProperty("_CastShadows") ? material.GetFloat("_CastShadows") != 0f : !blended);
            int queue = blended ? (int)RenderQueue.Transparent : clipped ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Geometry;
            material.renderQueue = queue + (material.HasProperty("_QueueOffset") ? (int)material.GetFloat("_QueueOffset") : 0);
            return true;
        }

        /// <inheritdoc/>
        public bool SetAdditive(Material material)
        {
            if (!SetBlended(material, true)) return false;
            // URP's Additive (BaseShaderGUI's BlendMode.Additive): its colour times its alpha added, its alpha too.
            SetIfPresent(material, "_Blend", 2f);
            SetIfPresent(material, "_DstBlend", (float)BlendMode.One);
            SetIfPresent(material, "_DstBlendAlpha", (float)BlendMode.One);
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

        /// <inheritdoc/>
        /// <remarks>
        /// A global volume of its own (hidden, never saved, its profile made in memory: nothing an asset
        /// could lose on a reload), at the options' priority (over every project volume by default): tone
        /// mapping off, the post exposure the scale, the table as the colour lookup, bloom and vignette off
        /// as the options say; and post-processing on for the options' cameras (every game camera by default,
        /// those there now and each one later before its frame's cameras render: a camera reads it before its
        /// own rendering starts), off again where it was off once the last display shown over the camera is put away.
        /// </remarks>
        public System.IDisposable Display(Texture2D display, float scale, out string why, DisplayOptions options = null)
        {
            why = null;
            if (display == null || scale <= 0f)
            {
                why = "no table, or no positive scale, to show";
                return null;
            }
            if (display.height != DisplaySize || display.width != DisplaySize * DisplaySize)
            {
                why = $"its table is {display.width} x {display.height}; URP's colour lookup takes only its asset's size, " +
                      $"{DisplaySize * DisplaySize} x {DisplaySize} (DisplaySize)";
                return null;
            }
            return new Displayed(display, scale, options ?? new DisplayOptions());
        }

        /// <inheritdoc/>
        public int DisplaySize => UniversalRenderPipeline.asset != null ? UniversalRenderPipeline.asset.colorGradingLutSize : 0;

        // One display transform shown. Two may show at once over one camera: the camera's post-processing, which the first
        // turned on, is handed on as that one is put away to one still shown over it, and turned off only by the last.
        sealed class Displayed : System.IDisposable
        {
            static readonly System.Collections.Generic.List<Displayed> _shown = new System.Collections.Generic.List<Displayed>();
            readonly GameObject _owner;
            readonly VolumeProfile _profile;
            readonly Camera[] _cameras;  // null: every game camera
            readonly System.Collections.Generic.List<UniversalAdditionalCameraData> _turnedOn =
                new System.Collections.Generic.List<UniversalAdditionalCameraData>();

            internal Displayed(Texture2D display, float scale, DisplayOptions options)
            {
                _cameras = options.cameras != null && options.cameras.Length > 0 ? (Camera[])options.cameras.Clone() : null;
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _profile.hideFlags = HideFlags.HideAndDontSave;
                _profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.None);
                _profile.Add<ColorAdjustments>(true).postExposure.Override(Mathf.Log(scale, 2f));
                ColorLookup lookup = _profile.Add<ColorLookup>(true);
                lookup.texture.Override(display);
                lookup.contribution.Override(1f);
                if (options.bloomOff) _profile.Add<Bloom>(true).intensity.Override(0f);
                if (options.vignetteOff) _profile.Add<Vignette>(true).intensity.Override(0f);
                _owner = new GameObject("unitytk display transform") { hideFlags = HideFlags.HideAndDontSave };
                Volume volume = _owner.AddComponent<Volume>();
                volume.isGlobal = true;
                volume.priority = options.priority;
                volume.sharedProfile = _profile;
                foreach (Camera camera in _cameras ?? Camera.allCameras) PostOn(camera);
                if (_cameras == null) RenderPipelineManager.beginContextRendering += PostOn;
                _shown.Add(this);
            }

            // Whether it turns post-processing on for camera.
            bool Covers(Camera camera)
                => camera != null && (_cameras == null ? camera.cameraType == CameraType.Game : System.Array.IndexOf(_cameras, camera) >= 0);

            void PostOn(ScriptableRenderContext context, System.Collections.Generic.List<Camera> cameras)
            {
                foreach (Camera camera in cameras) PostOn(camera);
            }

            void PostOn(Camera camera)
            {
                if (camera == null || camera.cameraType != CameraType.Game) return;
                UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
                if (data == null || data.renderPostProcessing) return;
                data.renderPostProcessing = true;
                _turnedOn.Add(data);
            }

            public void Dispose()
            {
                RenderPipelineManager.beginContextRendering -= PostOn;
                _shown.Remove(this);
                foreach (UniversalAdditionalCameraData data in _turnedOn)
                {
                    if (data == null) continue;
                    Displayed still = _shown.Find(other => other.Covers(data.GetComponent<Camera>()));
                    if (still != null) still._turnedOn.Add(data);  // another shows over it: its to give back
                    else data.renderPostProcessing = false;
                }
                _turnedOn.Clear();
                if (_owner != null) Object.DestroyImmediate(_owner);
                if (_profile != null)
                {
                    foreach (VolumeComponent component in _profile.components)
                        if (component != null) Object.DestroyImmediate(component);
                    Object.DestroyImmediate(_profile);
                }
            }
        }

        static void SetIfPresent(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }
    }
}
