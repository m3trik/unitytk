using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// An indicator light seen from where a person stands. An indicator is an <see cref="EmissionTrack"/> with
    /// <see cref="EmissionTrack.states"/> (off, green, amber...). A real one is a few millimetres across, a pixel
    /// or two of a headset's view at a standing distance, and a delivery's look keeps bloom off, so neither its
    /// colour nor its blinking would read from afar. While it is lit, each renderer wearing its material is drawn
    /// a glow card: a soft camera-facing disc in the emission's colour, <see cref="Halo"/> times the light's size
    /// close up and never under <see cref="MinPixels"/> across, drawn for each camera as it renders, a headset's
    /// too: nothing is added to the scene and nothing is saved. Nothing asks for it, as an indicator always needs
    /// to be seen; the WebXR viewer draws the same (pythontk <c>net_utils/preview/kernel/indicators.js</c>), by the
    /// same rule: the light's size is its mesh's bounding sphere (about its box's centre) times its largest scale,
    /// the card is added over the scene in its colour times its opacity, and the three constants are its
    /// <c>MIN_PIXELS</c>, <c>HALO</c> and <c>LIT</c> (held to it by <c>unitytk/tests/test_indicator_glow_twin.py</c>).
    /// </summary>
    /// <remarks>
    /// Each track has its own, made as it is switched on and disposed as it is switched off (its <c>OnEnable</c> and
    /// <c>OnDisable</c>, which a scene's unload and a domain reload send too), so the card's material and disc are its
    /// track's and never outlive it. The card is the pipeline's unlit shader (<see cref="MaterialWriter"/>), made as
    /// its light is first lit before a camera, added over the scene where the pipeline's host has an additive switch
    /// (<see cref="IRenderPipelineHost.SetAdditive"/>: URP's), else blended over it as written (the Built-in pipeline's
    /// unlit shaders blend by their alpha alone): a player build includes it where the project's materials use it.
    /// Where the pipeline has no host or the build no such shader, no card is drawn, said once. A light a Fade or
    /// Highlight covers is that effect's, and has none. A card has no size cap, as the viewer's has none: a part given
    /// states is an indicator, and a large one glows <see cref="Halo"/> times its own size; a cap would change both
    /// twins together, the viewer's first.
    /// </remarks>
    public sealed class IndicatorGlow : IDisposable
    {
        /// <summary>The least a lit indicator's glow covers on screen, in pixels across.</summary>
        public const float MinPixels = 10f;

        /// <summary>The glow's width over its light's, close up: a halo just past the lens.</summary>
        public const float Halo = 2.5f;

        /// <summary>Below this emission (its largest channel, linear) the light reads as off.</summary>
        public const float LitAbove = 0.01f;

        readonly EmissionTrack _track;
        readonly List<(Renderer renderer, Color colour)> _cards = new List<(Renderer renderer, Color colour)>();
        readonly MaterialPropertyBlock _colour = new MaterialPropertyBlock();
        readonly Dictionary<Mesh, (Vector3 centre, float radius)> _spheres = new Dictionary<Mesh, (Vector3, float)>();
        Material _material;
        Texture2D _disc;
        Mesh _quad;
        string _colourProperty;
        bool _tried, _failed;                  // the card was made, or could not be (failed), for _triedFor
        IRenderPipelineHost _triedFor;
        bool _disposed;

        /// <summary>The glow of <paramref name="track"/>, drawn for each camera from now until it is disposed.</summary>
        internal IndicatorGlow(EmissionTrack track)
        {
            _track = track;
            CameraDrawing.Starting += Draw;
        }

        /// <summary>The card's material while it is made; null before its light is first lit, or where it cannot be.</summary>
        internal Material Card => _material;

        /// <summary>Draw it no more (its track switched off): its render callback let go, its card's material and disc destroyed.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CameraDrawing.Starting -= Draw;
            Release();
            _spheres.Clear();
        }

        /// <summary>
        /// Where a light of bounding sphere <paramref name="centre"/>, <paramref name="radius"/> has its card for
        /// <paramref name="camera"/>, and how wide: just in front of the light (by its own radius, so the surface it
        /// sits on clips none of it), <see cref="Halo"/> times its size, at least <see cref="MinPixels"/> across on the
        /// camera's screen at its distance from the eye.
        /// </summary>
        /// <param name="camera">The camera drawing.</param>
        /// <param name="centre">The light's bounding sphere's centre, world space (<see cref="Sphere"/>).</param>
        /// <param name="radius">Its radius, world units.</param>
        /// <param name="position">The card's centre.</param>
        /// <param name="size">Its width, world units.</param>
        public static void Place(Camera camera, Vector3 centre, float radius, out Vector3 position, out float size)
        {
            Vector3 toEye = camera.transform.position - centre;
            float away = toEye.magnitude;
            position = away > radius ? centre + toEye / away * radius : centre;
            float least = 0f;
            if (!camera.orthographic && camera.pixelHeight > 0)
                least = MinPixels / camera.pixelHeight * 2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * Mathf.Max(away - radius, 0f);
            size = Mathf.Max(2f * radius * Halo, least);
        }

        /// <summary>
        /// The bounding sphere of what <paramref name="renderer"/> draws, world space: its mesh's (centred on the mesh's
        /// box, reaching its farthest vertex; the box's corners where a player cannot read the mesh) placed by the
        /// renderer's transform, its radius times the transform's largest scale; a renderer with no mesh, its bounds'.
        /// </summary>
        /// <param name="renderer">The light's renderer.</param>
        /// <param name="centre">The sphere's centre.</param>
        /// <param name="radius">Its radius, world units.</param>
        public static void Sphere(Renderer renderer, out Vector3 centre, out float radius) => Sphere(renderer, null, out centre, out radius);

        // The sphere, each mesh measured once into spheres where one is given (a glow's, measured as it draws).
        static void Sphere(Renderer renderer, Dictionary<Mesh, (Vector3 centre, float radius)> spheres, out Vector3 centre, out float radius)
        {
            Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                      : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
            if (mesh == null)
            {
                Bounds bounds = renderer.bounds;
                centre = bounds.center;
                radius = bounds.extents.magnitude;
                return;
            }
            (Vector3 centre, float radius) local;
            if (spheres == null) local = Measure(mesh);
            else if (!spheres.TryGetValue(mesh, out local)) spheres[mesh] = local = Measure(mesh);
            Transform at = renderer.transform;
            Vector3 scale = at.lossyScale;
            centre = at.TransformPoint(local.centre);
            radius = local.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        }

        // A mesh's bounding sphere in its own space, as three.js's computeBoundingSphere makes it: its box's centre and
        // the farthest vertex from it; the box's corners where its vertices cannot be read (a player, Read/Write off:
        // the editor reads every mesh).
        static (Vector3 centre, float radius) Measure(Mesh mesh)
        {
            Bounds box = mesh.bounds;
            if (!mesh.isReadable && !Application.isEditor) return (box.center, box.extents.magnitude);
            var vertices = new List<Vector3>(mesh.vertexCount);
            mesh.GetVertices(vertices);
            float far = 0f;
            for (int i = 0; i < vertices.Count; i++) far = Mathf.Max(far, (vertices[i] - box.center).sqrMagnitude);
            return (box.center, Mathf.Sqrt(far));
        }

        /// <summary>
        /// The cards drawn now: where its track is lit (its states, its emission above <see cref="LitAbove"/>), each
        /// shown renderer wearing its material, with the colour it glows (its emission, its alpha the emission's
        /// largest channel, at most 1); none where it is not. One list, filled again on each call: a frame allocates
        /// nothing.
        /// </summary>
        internal List<(Renderer renderer, Color colour)> Cards()
        {
            _cards.Clear();
            EmissionTrack track = _track;
            if (track == null || !track.Live || track.states.Count == 0 || track.writer == null) return _cards;
            Color emission = track.Emission;
            float lit = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
            if (lit < LitAbove) return _cards;
            var colour = new Color(emission.r, emission.g, emission.b, Mathf.Min(1f, lit));
            IReadOnlyList<Renderer> renderers = track.writer.Renderers;
            for (int r = 0; r < renderers.Count; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer != null && renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy)
                    _cards.Add((renderer, colour));
            }
            return _cards;
        }

        /// <summary>
        /// The colour its cards are drawn in, <paramref name="colour"/> (linear, its alpha the opacity), as the card's
        /// shader is handed it: stored as a material stores a colour (<see cref="MaterialColor.Encode"/>), which Unity
        /// converts back to linear on upload in a Linear project.
        /// </summary>
        internal MaterialPropertyBlock Colour(Color colour, string property)
        {
            _colour.SetColor(property, MaterialColor.Encode(colour));
            return _colour;
        }

        void Draw(Camera camera)
        {
            if (_disposed) return;  // let go during this camera's callbacks, which were under way
            if (_track == null)
            {
                Dispose();  // its track destroyed with no OnDisable sent (an Edit Mode test's): nothing left behind
                return;
            }
            if (camera == null || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection) return;
            List<(Renderer renderer, Color colour)> cards = Cards();
            if (cards.Count == 0 || !Ready(RenderPipelineHosts.Current)) return;
            Quaternion facing = camera.transform.rotation;  // the quad's front (-Z) toward the camera
            var draw = new RenderParams(_material) { camera = camera, matProps = Colour(cards[0].colour, _colourProperty) };
            for (int i = 0; i < cards.Count; i++)
            {
                Renderer renderer = cards[i].renderer;
                Sphere(renderer, _spheres, out Vector3 centre, out float radius);
                Place(camera, centre, radius, out Vector3 position, out float size);
                draw.layer = renderer.gameObject.layer;
                Graphics.RenderMesh(draw, _quad, 0, Matrix4x4.TRS(position, facing, Vector3.one * size));
            }
        }

        /// <summary>
        /// The card's material and mesh, made once for <paramref name="host"/>'s pipeline, the one drawing (again where
        /// another draws now): its unlit shader, added over the scene where the host has an additive switch, else
        /// blended over it as written, over a soft disc. Where it cannot be made, it is said once and not tried again for
        /// that pipeline; nothing it made is left behind.
        /// </summary>
        internal bool Ready(IRenderPipelineHost host)
        {
            if (_tried && host == _triedFor && (_failed || _material != null)) return !_failed;
            Release();
            _tried = _failed = true;
            _triedFor = host;
            if (host == null || host.Lit == null)
            {
                EffectWriter.Warn($"no render-pipeline host serves the {RenderPipelineHosts.NameOf(host)} pipeline, so no indicator light is drawn its glow");
                return false;
            }
            var material = new Material(Shader.Find("Hidden/InternalErrorShader")) { name = "Indicator Glow", hideFlags = HideFlags.HideAndDontSave };
            Texture2D disc = Disc();
            var surface = new LitSurface { unlit = true, alpha = LitAlpha.Blend, doubleSided = true, baseMap = disc };
            if (!MaterialWriter.Write(material, surface, host))
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(disc);
                EffectWriter.Warn($"the {host.Name} pipeline's unlit shader is not in this project or build, so no indicator light is drawn its glow");
                return false;
            }
            host.SetAdditive(material);  // false where it has none: the material is unchanged, blended as written
            _failed = false;
            _material = material;
            _disc = disc;
            _colourProperty = host.Lit.UnlitColor ?? host.Lit.BaseColor;
            _quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            return true;
        }

        // The card's material and disc destroyed: it is disposed, or another pipeline draws. They are never saved.
        void Release()
        {
            if (_material != null) Object.DestroyImmediate(_material);
            if (_disc != null) Object.DestroyImmediate(_disc);
            _material = null;
            _disc = null;
            _quad = null;
            _tried = _failed = false;
            _triedFor = null;
        }

        // A soft disc, opaque at its centre, gone at its rim (white: the colour is the card's).
        static Texture2D Disc()
        {
            const int size = 64;
            var disc = new Texture2D(size, size, TextureFormat.RGBA32, false, true) { name = "Indicator Glow Disc", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
                    float a = r >= 1f ? 0f : r < 0.25f ? 1f - r * 1.6f : 0.6f * (1f - (r - 0.25f) / 0.75f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
                }
            disc.SetPixels32(pixels);
            disc.Apply(false, true);
            return disc;
        }
    }
}
