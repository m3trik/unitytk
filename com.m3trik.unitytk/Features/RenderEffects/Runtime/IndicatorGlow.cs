using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

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
    /// The card is the pipeline's unlit shader (<see cref="MaterialWriter"/>), added over the scene where the
    /// pipeline's host has an additive switch (<see cref="IRenderPipelineHost.SetAdditive"/>: URP's), else blended
    /// over it (the Built-in pipeline's unlit shaders blend by their alpha alone): a player build includes it where
    /// the project's materials use it. Where the pipeline has no host or the build no such shader, no card is drawn,
    /// said once. A light a Fade or Highlight covers is that effect's, and has none. A card has no size cap, as the
    /// viewer's has none: a part given states is an indicator, and a large one glows <see cref="Halo"/> times its own
    /// size; a cap would change both twins together, the viewer's first.
    /// </remarks>
    public static class IndicatorGlow
    {
        /// <summary>The least a lit indicator's glow covers on screen, in pixels across.</summary>
        public const float MinPixels = 10f;

        /// <summary>The glow's width over its light's, close up: a halo just past the lens.</summary>
        public const float Halo = 2.5f;

        /// <summary>Below this emission (its largest channel, linear) the light reads as off.</summary>
        public const float LitAbove = 0.01f;

        static readonly List<EmissionTrack> _tracks = new List<EmissionTrack>();
        static readonly List<(EmissionTrack track, Renderer renderer, Color colour)> _cards =
            new List<(EmissionTrack track, Renderer renderer, Color colour)>();
        static readonly Dictionary<EmissionTrack, MaterialPropertyBlock> _colours = new Dictionary<EmissionTrack, MaterialPropertyBlock>();
        static readonly Dictionary<Mesh, (Vector3 centre, float radius)> _spheres = new Dictionary<Mesh, (Vector3, float)>();
        static readonly List<Vector3> _vertices = new List<Vector3>();
        static Material _material;
        static Texture2D _disc;
        static Mesh _quad;
        static string _colourProperty;
        static bool _tried, _failed;           // the card was made, or could not be (failed), for _triedFor
        static IRenderPipelineHost _triedFor;
        static bool _drawing;                  // it draws for each camera: while any track has joined

        /// <summary>Draw <paramref name="track"/>'s glow while it is lit (an <see cref="EmissionTrack"/> with states, enabled).</summary>
        internal static void Join(EmissionTrack track)
        {
            if (track == null || _tracks.Contains(track)) return;
            _tracks.Add(track);
            if (_drawing) return;
            _drawing = true;
            CameraDrawing.Starting += Draw;
        }

        /// <summary>Draw it no more (disabled, removed); the last to leave takes the card's material with it.</summary>
        internal static void Leave(EmissionTrack track)
        {
            if (!_tracks.Remove(track)) return;
            _colours.Remove(track);
            if (_tracks.Count > 0) return;
            _drawing = false;
            CameraDrawing.Starting -= Draw;
            Release();
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
        /// Each mesh is measured once.
        /// </summary>
        /// <param name="renderer">The light's renderer.</param>
        /// <param name="centre">The sphere's centre.</param>
        /// <param name="radius">Its radius, world units.</param>
        public static void Sphere(Renderer renderer, out Vector3 centre, out float radius)
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
            if (!_spheres.TryGetValue(mesh, out var local)) _spheres[mesh] = local = Measure(mesh);
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
            mesh.GetVertices(_vertices);
            float far = 0f;
            for (int i = 0; i < _vertices.Count; i++) far = Mathf.Max(far, (_vertices[i] - box.center).sqrMagnitude);
            _vertices.Clear();
            return (box.center, Mathf.Sqrt(far));
        }

        /// <summary>
        /// The cards drawn now: each lit indicator's (a joined track with states, its emission above
        /// <see cref="LitAbove"/>) shown renderers wearing its material, with the colour it glows (its emission,
        /// its alpha the emission's largest channel, at most 1). One list, filled again on each call: a frame
        /// allocates nothing.
        /// </summary>
        internal static List<(EmissionTrack track, Renderer renderer, Color colour)> Cards()
        {
            _cards.Clear();
            for (int t = _tracks.Count - 1; t >= 0; t--)
            {
                EmissionTrack track = _tracks[t];
                if (track == null)
                {
                    _tracks.RemoveAt(t);  // destroyed unseen (its OnDisable gives it back): the card stays till the last leaves
                    continue;
                }
                if (!track.Live || track.states.Count == 0 || track.writer == null) continue;
                Color emission = track.Emission;
                float lit = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
                if (lit < LitAbove) continue;
                var colour = new Color(emission.r, emission.g, emission.b, Mathf.Min(1f, lit));
                IReadOnlyList<Renderer> renderers = track.writer.Renderers;
                for (int r = 0; r < renderers.Count; r++)
                {
                    Renderer renderer = renderers[r];
                    if (renderer != null && renderer.enabled && !renderer.forceRenderingOff && renderer.gameObject.activeInHierarchy)
                        _cards.Add((track, renderer, colour));
                }
            }
            return _cards;
        }

        /// <summary>
        /// The colour a card of <paramref name="track"/>'s is drawn in, <paramref name="colour"/> (linear, its alpha the
        /// opacity), as the card's shader is handed it: stored as a material stores a colour
        /// (<see cref="MaterialColor.Encode"/>), which Unity converts back to linear on upload in a Linear project.
        /// </summary>
        internal static MaterialPropertyBlock Colour(EmissionTrack track, Color colour, string property)
        {
            if (!_colours.TryGetValue(track, out MaterialPropertyBlock block)) _colours[track] = block = new MaterialPropertyBlock();
            block.SetColor(property, MaterialColor.Encode(colour));
            return block;
        }

        static void Draw(Camera camera)
        {
            if (camera == null || camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection) return;
            List<(EmissionTrack track, Renderer renderer, Color colour)> cards = Cards();
            if (cards.Count == 0 || !Ready(RenderPipelineHosts.Current)) return;
            Quaternion facing = camera.transform.rotation;  // the quad's front (-Z) toward the camera
            for (int i = 0; i < cards.Count; i++)
            {
                (EmissionTrack track, Renderer renderer, Color colour) = cards[i];
                Sphere(renderer, out Vector3 centre, out float radius);
                Place(camera, centre, radius, out Vector3 position, out float size);
                var draw = new RenderParams(_material) { camera = camera, matProps = Colour(track, colour, _colourProperty), layer = renderer.gameObject.layer };
                Graphics.RenderMesh(draw, _quad, 0, Matrix4x4.TRS(position, facing, Vector3.one * size));
            }
        }

        /// <summary>
        /// The card's material and mesh, made once for <paramref name="host"/>'s pipeline, the one drawing (again where
        /// another draws now): its unlit shader, added over the scene (else blended), over a soft disc. Where it cannot be
        /// made, it is said once and not tried again for that pipeline; nothing it made is left behind.
        /// </summary>
        internal static bool Ready(IRenderPipelineHost host)
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
            host.SetAdditive(material);  // where it has none, blended over the scene
            _failed = false;
            _material = material;
            _disc = disc;
            _colourProperty = host.Lit.UnlitColor ?? host.Lit.BaseColor;
            _quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            return true;
        }

        /// <summary>
        /// The card's material and disc destroyed, the meshes' spheres forgotten: the last track left, or another
        /// pipeline draws. They are never saved, and a domain reload would orphan them.
        /// </summary>
        internal static void Release()
        {
            if (_material != null) Object.DestroyImmediate(_material);
            if (_disc != null) Object.DestroyImmediate(_disc);
            _material = null;
            _disc = null;
            _colours.Clear();
            _spheres.Clear();
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
