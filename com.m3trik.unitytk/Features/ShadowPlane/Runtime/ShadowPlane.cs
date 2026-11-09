// ----------------------------------------------------------------------------
// The ShadowPlane feature's runtime part (M3trik.Unitytk.ShadowPlane): a ground
// shadow drawn on a quad from a height-field map of its target (the horizon
// map), re-placed and faded every frame from its source and contact, as a
// player runs it. Every field is in Unity's terms: the nodes are references,
// the lengths metres in the plane's space, the source a Light. Nothing here
// reads a record: the importer (ShadowPlaneImporter, the editor part) fills the
// component from a delivery's ``shadows`` record, and the actions
// (ShadowPlaneActions) make and bake one in Unity.
//
// Each frame (LateUpdate) it evaluates the projection model (pythontk
// ShadowProjection.model, ported in ShadowPlaneModel, the model part) in its
// SPACE's frame, so a moved, turned or scaled instance carries its shadow:
// it places the plane while followSource is on and both the contact and a
// source resolve, and computes the fade (ShadowProjection.fade, ported in the
// same class) that the shader multiplies the march by. The march is CACHED
// (ShadowPlaneCache, cacheShadow): marched over the plane's own UVs into its
// rect of one global R8 atlas only when the source, the placement or the
// ground moved in the contact's frame -- every due plane in one command
// buffer, within a per-frame texel budget -- and sampled at one fetch a pixel;
// measured in the WebXR twin of this cache, a per-pixel march cost 1.5-6x a
// PCF shadow map's frame time, the cache nothing while the light holds still.
//
// Behavior-verified by ShadowPlaneTests (Edit Mode: placement and fade against
// the model, the horizon texels against HeightFieldMap.Alpha) and
// test_shadow_plane_runtime_integration.py (a batch Unity: the pixels against
// pythontk's oracle).
// ----------------------------------------------------------------------------
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("M3trik.Unitytk.ShadowPlane.Editor")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.ShadowPlane.Tests")]

namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// A ground shadow of <see cref="target"/>, cast by <see cref="source"/> (the
    /// scene's sun when none is set) from its horizon map, re-placed and faded each
    /// frame from where the source and the <see cref="contact"/> stand. Lengths are
    /// metres in its <see cref="space"/>. <see cref="ApplyNow"/> places it at once.
    /// </summary>
    /// <remarks>
    /// Prior art: a <c>Projector</c>, URP's decal projector and Unity's shadow maps
    /// cast the shadow of the mesh a light sees, re-rendered every frame per light;
    /// this one is baked once into a height field the WebXR viewer and the producers'
    /// previews march too, so a delivery shadows alike everywhere, and it is placed
    /// by the ported model (<see cref="ShadowPlaneModel"/>). The per-plane values
    /// ride a <c>MaterialPropertyBlock</c>, Unity's own per-renderer override that
    /// keeps one material instanced.
    /// </remarks>
    [AddComponentMenu("Rendering/Shadow Plane")]
    [DisallowMultipleComponent]
    public class ShadowPlane : MonoBehaviour
    {
        // ------------------------------------------------------------- Nodes
        [Tooltip("The occluder: the root whose meshes the horizon map is baked from (Bake Horizon).")]
        public Transform target;

        [Tooltip("The light the shadow is cast by: a Directional light shines along its +Z, a Point " +
                 "or Spot light from its position. Empty: the scene's sun (Lighting > Sun Source).")]
        public Light source;

        [Tooltip("The footprint's centre on the target's underside: the shadow is anchored here, " +
                 "and the horizon map is baked and marched in its frame.")]
        public Transform contact;

        [Tooltip("The transform whose frame the ground and every length are in (the imported " +
                 "model's root, or what the target stands on): moving, turning or scaling it " +
                 "carries the shadow, its ground included. Empty: the world.")]
        public Transform space;

        /// <summary>The space's matrix when it was bound: where its frame's rest sits.</summary>
        [SerializeField, HideInInspector] Matrix4x4 spaceRest = Matrix4x4.identity;

        /// <summary>The space <see cref="spaceRest"/> was taken from.</summary>
        [SerializeField, HideInInspector] Transform spaceBound;

        // ------------------------------------------------------------- Shape
        [Header("Shape (metres)")]
        [Tooltip("The target's footprint radius: half its footprint's diagonal.")]
        [Min(0f)] public float radius = 0.5f;

        [Tooltip("The target's height above its contact.")]
        [Min(0f)] public float height = 1f;

        [Tooltip("The ground's height in the space's frame.")]
        public float ground;

        [Tooltip("The cap on the shadow's length past the target, in target heights (6 is the " +
                 "length a sun 9.5 degrees up casts).")]
        [Min(0f)] public float maxStretch = (float)ShadowPlaneModel.DefaultMaxStretch;

        [Tooltip("Where the quad's edges sit around the projected shadow, (u0, u1, w0, w1): u1 the far " +
                 "edge in top radii past where the target's top lands, u0 the near edge as a fraction of " +
                 "that far edge's distance from the anchor, w0 and w1 the sides as fractions of the " +
                 "shadow's width along the plane's local +X (an XZ quad's; the import turns a record's " +
                 "into these terms).")]
        public Vector4 canvas = new Vector4((float)ShadowPlaneModel.DefaultCanvas[0], (float)ShadowPlaneModel.DefaultCanvas[1],
                                            (float)ShadowPlaneModel.DefaultCanvas[2], (float)ShadowPlaneModel.DefaultCanvas[3]);

        // ------------------------------------------------------------- Fade
        [Header("Fade")]
        [Tooltip("The shadow's darkness, 0 to 1: the fade's ceiling.")]
        [Range(0f, 1f)] public float intensity = 1f;

        [Tooltip("How fast the shadow lightens as it stretches past its footprint: the fade's " +
                 "exponent on the stretch.")]
        [Min(0f)] public float falloffPower = (float)ShadowPlaneModel.DefaultFalloffPower;

        [Tooltip("The target's rise off the ground at which its shadow has faded out.")]
        [Min(0f)] public float fadeHeight = 2f;

        [Tooltip("The source's height over the contact at which the shadow is all in, in metres: the " +
                 "fade ramps in from none with the source at the contact's height (a producer's rig " +
                 "unit: 0.01 for a centimetre rig).")]
        [Min(1e-6f)] public float sourceRamp = 1f;

        [Tooltip("Metres per unit of the scene the plane was authored in (0.01 for centimetres): " +
                 "the plane lifts a hundredth of it off the ground.")]
        [Min(1e-6f)] public float unit = 1f;

        // ------------------------------------------------------------- Source
        [Header("Source")]
        [Tooltip("A Point or Spot light's diameter, in metres: the penumbra (0: a sharp edge).")]
        [Min(0f)] public float sourceSize;

        [Tooltip("A Directional light's angular diameter, in degrees: the penumbra (the sun: 0.53).")]
        [Range(0f, 90f)] public float sourceAngle;

        [Tooltip("Re-place the plane every frame from the source and the contact; off: its own " +
                 "transform (an imported animation) stays, and only the fade follows them.")]
        public bool followSource = true;

        // ------------------------------------------------------------- Horizon
        [Header("Horizon Map")]
        [Tooltip("The height-field map the shadow is marched from (data: linear, no mips, clamp, point).")]
        public Texture2D horizonMap;

        [Tooltip("Footprint pixels per side of the map (S).")]
        public int horizonSize = 128;

        [Tooltip("Solid spans kept per pixel column (K).")]
        public int horizonSpans = 2;

        [Tooltip("The footprint the map covers, (a0, a1, b0, b1) along Frame A and Frame B from " +
                 "the contact, in metres.")]
        public Vector4 horizonBounds = new Vector4(-1f, 1f, -1f, 1f);

        [Tooltip("The height above the ground a 16-bit value of 65535 stands for, in metres.")]
        public float horizonHeightScale = 1f;

        [Tooltip("The footprint's first axis (bearing zero), in the contact's local frame.")]
        public Vector3 horizonFrameA = Vector3.right;

        [Tooltip("The footprint's second axis, in the contact's local frame.")]
        public Vector3 horizonFrameB = Vector3.forward;

        [Tooltip("The contact's frame is a Z-up producer's (the import sets it from the contact's own frame): " +
                 "the frame vectors, written in FBX's Y-up axes, are read on its +Z as up. Off: its +Y is up.")]
        public bool zUpFrames;

        [Tooltip("The map's rect in its image (scale xy, offset zw; origin bottom-left): a shared atlas's tile.")]
        public Vector4 horizonRect = new Vector4(1f, 1f, 0f, 0f);

        /// <summary>The target's geometry hash the map was baked from (empty: unknown), which Rebake Stale compares.</summary>
        [HideInInspector] public string horizonHash = "";

        // ------------------------------------------------------------- Cache
        [Header("Cache")]
        [Tooltip("March the shadow into the shared atlas only when the source, the placement or " +
                 "the ground moves in the contact's frame, and sample it at one fetch a pixel " +
                 "(ShadowPlaneCache). Off: march every pixel, every frame.")]
        public bool cacheShadow = true;

        // ------------------------------------------------------------- Runtime
        static readonly int ID_Fade = Shader.PropertyToID("_Fade");
        static readonly int ID_HorizonMap = Shader.PropertyToID("_HorizonMap");
        static readonly int ID_HorizonRect = Shader.PropertyToID("_HorizonRect");
        static readonly int ID_HorizonParams = Shader.PropertyToID("_HorizonParams");
        static readonly int ID_HorizonBounds = Shader.PropertyToID("_HorizonBounds");
        static readonly int ID_HorizonMeta = Shader.PropertyToID("_HorizonMeta");
        static readonly int ID_Origin = Shader.PropertyToID("_Origin");
        static readonly int ID_AxisA = Shader.PropertyToID("_AxisA");
        static readonly int ID_AxisB = Shader.PropertyToID("_AxisB");
        static readonly int ID_AxisUp = Shader.PropertyToID("_AxisUp");
        static readonly int ID_Source = Shader.PropertyToID("_Source");
        static readonly int ID_SourceRadius = Shader.PropertyToID("_SourceRadius");
        static readonly int ID_CacheRect = Shader.PropertyToID("_CacheRect");
        static readonly int ID_Cached = Shader.PropertyToID("_Cached");

        // ---- the cache (ShadowPlaneCache) -------------------------------------
        /// <summary>What the cached march depends on, in the contact's frame
        /// (<see cref="WriteBlock"/>): the plane's placement, the source, the
        /// ground, the source's size and the map. A rigid motion of all of them
        /// together leaves it unchanged.</summary>
        internal readonly float[] cacheKey = new float[ShadowPlaneCache.KeyLength];
        internal readonly float[] cacheKeyNext = new float[ShadowPlaneCache.KeyLength];
        internal int cacheSlot = -1;
        internal bool cacheValid;
        internal int cacheStamp;
        internal float cachePitch;              // a map pixel, world units
        internal Vector3 placedPosition, placedScale;
        internal Quaternion placedRotation = Quaternion.identity;
        Mesh _uvMesh;
        Vector3 _uvDu, _uvDv;
        bool _uvOk;
        /// <summary>The horizon map the cache key last saw, and how many times it
        /// changed: the key's map term. An instance id would not do -- the key's
        /// relative tolerance (and a float's 24-bit mantissa) cannot tell two
        /// large ids a few apart.</summary>
        Texture2D _keyedMap;
        int _mapSerial;

        Renderer _rend;
        MeshFilter _filter;
        MaterialPropertyBlock _block;

        /// <summary>
        /// Why a horizon layout is not one the shader marches, or null when it is --
        /// the WebXR viewer's <c>horizonSpec</c>, term for term: a footprint of at
        /// least 8 pixels, at least one span, a positive height scale and ordered bounds.
        /// </summary>
        public static string HorizonRefusal(int size, int spans, float heightScale, Vector4 bounds)
        {
            if (size < 8) return $"size {size} (under 8 pixels)";
            if (spans < 1) return $"{spans} spans";
            if (!(heightScale > 0f)) return $"height scale {heightScale}";
            if (!(bounds.y > bounds.x && bounds.w > bounds.z)) return $"bounds {bounds} (empty or out of order)";
            return null;
        }

        /// <summary>The plane marches its map: one is bound and its layout is one the shader marches.</summary>
        public bool CanMarch =>
            horizonMap != null && HorizonRefusal(horizonSize, horizonSpans, horizonHeightScale, horizonBounds) == null;

        /// <summary>The light the shadow is cast by now: <see cref="source"/>, else the scene's sun (null when neither).</summary>
        public Light SourceLight => source != null ? source : RenderSettings.sun;

        /// <summary>The source shines along one direction rather than from its position (a Directional light).</summary>
        public bool IsDirectional
        {
            get
            {
                Light light = SourceLight;
                return light != null && light.type == LightType.Directional;
            }
        }

        /// <summary>
        /// The fade the plane drew at on its last apply, 0 to <see cref="intensity"/>
        /// (<see cref="ShadowPlaneModel.Fade(ShadowPlaneModel.Result, double, double, double, double, double, double, double, double)"/>):
        /// <see cref="intensity"/> itself while the contact or a source is missing.
        /// </summary>
        public float Fade { get; private set; } = 1f;

        /// <summary>
        /// Measure the ground and every length in <paramref name="frame"/>'s frame from
        /// now on. Its matrix NOW is taken as its rest, so a later move, turn or scale of
        /// it is the instance's placement, which the plane follows. Null: the world.
        /// </summary>
        public void BindSpace(Transform frame)
        {
            space = frame;
            spaceBound = frame;
            spaceRest = frame != null ? frame.localToWorldMatrix : Matrix4x4.identity;
        }

        /// <summary>
        /// The space's frame -- where the ground and the lengths are measured, at its
        /// rest -- to world, now. The identity when no space is bound.
        /// </summary>
        public Matrix4x4 SpaceToWorld()
        {
            if (space != spaceBound) BindSpace(space);
            if (space == null) return Matrix4x4.identity;
            Matrix4x4 rest = spaceRest;
            if (Mathf.Abs(rest.determinant) < 1e-12f) rest = Matrix4x4.identity;
            return space.localToWorldMatrix * rest.inverse;
        }

        /// <summary>The way a directional source shines, world space: a Light's +Z (noon when there is none).</summary>
        public Vector3 ShineDirection()
        {
            Light light = SourceLight;
            return light != null ? light.transform.forward : Vector3.down;
        }

        void OnEnable()
        {
            Init();
        }

        void Init()
        {
            _rend = GetComponent<Renderer>();
            _filter = GetComponent<MeshFilter>();
            if (_block == null) _block = new MaterialPropertyBlock();  // reuse
        }

        void LateUpdate()
        {
            // stage only: ShadowPlaneCache marches every staged plane in one
            // command buffer before the frame's first camera renders
            Apply(false);
        }

        void OnDisable()
        {
            ShadowPlaneCache.Release(this);
            // The slot is free for the next plane that needs one: a renderer still
            // drawn (only this component switched off) must stop sampling it.
            if (_rend != null && _block != null) SetCacheRect(Vector4.zero, false);
        }

        /// <summary>
        /// Place the plane, compute its fade and push its block now instead of
        /// waiting for the next LateUpdate -- for scripts that moved the source and
        /// need it on screen this frame, the inspector's preview and editor tests.
        /// Self-initializing, so it is also safe before OnEnable has run.
        /// </summary>
        public void ApplyNow()
        {
            Apply(true);
        }

        /// <summary>Whether an apply places the plane: <see cref="followSource"/> on, a contact and a source.</summary>
        internal bool Places => followSource && contact != null && SourceLight != null;

        /// <summary>
        /// The space's matrix at its rest (<see cref="BindSpace"/>), rebound first where the
        /// space changed; the identity where it cannot be inverted, as <see cref="SpaceToWorld"/> reads it.
        /// </summary>
        internal Matrix4x4 SpaceRest
        {
            get
            {
                SpaceToWorld();
                return space == null || Mathf.Abs(spaceRest.determinant) < 1e-12f ? Matrix4x4.identity : spaceRest;
            }
        }

        /// <summary>
        /// The bound map's pixels changed in place (written over and reimported: the same
        /// <c>Texture2D</c>), so the cached march of the old ones goes on the next apply.
        /// </summary>
        internal void ForgetMap()
        {
            _keyedMap = null;
            cacheValid = false;
        }

        void Apply(bool flush)
        {
            if (_rend == null || _block == null) Init();
            Light light = SourceLight;
            if (contact != null && light != null)
            {
                Evaluate(light, out ShadowPlaneModel.Result m, out double contactUp, out double lightUp);
                if (followSource) Place(m);
                Fade = (float)ShadowPlaneModel.Fade(m, lightUp, contactUp, ground, radius, intensity,
                    falloffPower, fadeHeight, sourceRamp);
            }
            else
            {
                Fade = intensity;
            }
            if (_rend == null) return;
            WriteBlock(light);
            if (flush) ShadowPlaneCache.Flush();
        }

        /// <summary>
        /// The model at the contact and the source (a sun's far point, kept in double:
        /// a float loses the reach there), both in the space's frame, and their heights.
        /// </summary>
        void Evaluate(Light light, out ShadowPlaneModel.Result m, out double contactUp, out double lightUp)
        {
            Matrix4x4 toSpace = SpaceToWorld().inverse;
            Vector3 c = toSpace.MultiplyPoint3x4(contact.position);
            double x, y, z;
            if (light.type == LightType.Directional)
            {
                Vector3 d = toSpace.MultiplyVector(light.transform.forward);
                ShadowPlaneModel.FarPoint(c.x, c.y, c.z, d.x, d.y, d.z, Mathf.Max(height, 2f * radius),
                    out x, out y, out z);
            }
            else
            {
                Vector3 l = toSpace.MultiplyPoint3x4(light.transform.position);
                x = l.x;
                y = l.y;
                z = l.z;
            }
            m = ShadowPlaneModel.Evaluate(c.x, c.y, c.z, x, y, z, ground, radius, height, maxStretch);
            contactUp = c.y;
            lightUp = y;
        }

        /// <summary>
        /// An XY quad's axes laid onto the Y-up plane <see cref="Place"/> turns:
        /// the quad's +Y (away from the source) onto the plane's +Z and its +Z
        /// (the normal) onto the plane's +Y -- a Z-up producer's quad, which its FBX
        /// exporter leaves in its own Z-up frame, or Unity's Quad (see <see cref="LiesInXY"/>).
        /// </summary>
        static readonly Quaternion ZUpPlaneAxes = Quaternion.LookRotation(Vector3.up, Vector3.forward);

        /// <summary>
        /// A Z-up node's frame read in FBX (Y-up) axes: the inverse of the turn a
        /// Z-up exporter gives its roots (FBX +Y is the node's +Z, FBX -Z its +Y), so
        /// the frame vectors -- written in FBX axes -- land on the contact's own axes
        /// (<see cref="zUpFrames"/>).
        /// </summary>
        static readonly Quaternion ZUpFrameAxes = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>
        /// The plane's quad lies in its local XY (normal along Z), as a Z-up producer's
        /// arrives -- its FBX exporter turns only the ROOT nodes upright (-90 deg about X),
        /// so the plane below them keeps its Z-up local frame -- and as Unity's own Quad
        /// does; a Y-up producer's quad, and <c>Create</c>'s, lie in XZ. How the quad is laid
        /// is the mesh's own, so it is read off the mesh; the contact's frame is not, and
        /// is <see cref="zUpFrames"/>.
        /// </summary>
        internal bool LiesInXY
        {
            get
            {
                var mesh = _filter != null ? _filter.sharedMesh : null;
                if (mesh == null)
                {
                    var filter = GetComponent<MeshFilter>();
                    mesh = filter != null ? filter.sharedMesh : null;
                }
                if (mesh == null) return false;
                Vector3 size = mesh.bounds.size;
                return size.z < size.y;
            }
        }

        /// <summary>
        /// Place the plane at <paramref name="m"/>, in the SPACE's frame
        /// (<see cref="SpaceToWorld"/>): centred on its <see cref="canvas"/>, lifted a
        /// hundredth of a <see cref="unit"/> above the ground; yaw = atan2(bearing.x,
        /// bearing.z), so local +Z runs along the bearing and local +X across it -- a
        /// Z-up quad's +Y along it instead (<see cref="ZUpPlaneAxes"/>); scaled to the
        /// canvas's extents over the mesh's own size. The result goes through the
        /// space's frame and back through the plane's parents, so a moved, turned or
        /// scaled instance carries it.
        /// </summary>
        void Place(ShadowPlaneModel.Result m)
        {
            m.Placement(canvas.x, canvas.y, canvas.z, canvas.w,
                out double cx, out double cz, out double along, out double across);
            var heading = Quaternion.Euler(0f, (float)m.YawDegrees, 0f);
            bool zUp = LiesInXY;
            var rotation = zUp ? heading * ZUpPlaneAxes : heading;
            // The quad's own footprint (its bounds), so the extents are the canvas's:
            // across on local X, along on local Z -- local Y on a Z-up quad. The 1e-4
            // floor is the producers'.
            Vector3 size = MeshSize();
            float acrossScale = Mathf.Max(1e-4f, (float)across / size.x);
            float alongScale = Mathf.Max(1e-4f, (float)along / (zUp ? size.y : size.z));
            Matrix4x4 inSpace = Matrix4x4.TRS(
                new Vector3((float)cx, (float)(ground + ShadowPlaneModel.GroundOffset * unit), (float)cz),
                rotation,
                zUp ? new Vector3(acrossScale, alongScale, 1f) : new Vector3(acrossScale, 1f, alongScale));
            Matrix4x4 parentToWorld = transform.parent != null
                ? transform.parent.localToWorldMatrix : Matrix4x4.identity;
            SetLocal(parentToWorld.inverse * SpaceToWorld() * inSpace, zUp);
        }

        /// <summary>
        /// The plane's local transform from its matrix in its parent's frame:
        /// the position, the rotation of its orthonormalised axes and each axis's
        /// length -- the quad's normal axis held at 1 (a parent's scale would
        /// otherwise land there).
        /// </summary>
        void SetLocal(Matrix4x4 local, bool zUp)
        {
            Vector3 x = local.GetColumn(0), y = local.GetColumn(1), z = local.GetColumn(2);
            float sx = x.magnitude, sy = y.magnitude, sz = z.magnitude;
            if (Vector3.Dot(Vector3.Cross(x, y), z) < 0f) sx = -sx;  // a mirroring parent
            transform.localPosition = local.GetColumn(3);
            transform.localRotation = Quaternion.LookRotation(
                z / Mathf.Max(sz, 1e-12f), y / Mathf.Max(sy, 1e-12f));
            transform.localScale = zUp ? new Vector3(sx, sy, 1f) : new Vector3(sx, 1f, sz);
        }

        Vector3 MeshSize()
        {
            var mesh = _filter != null ? _filter.sharedMesh : null;
            Vector3 size = mesh != null ? mesh.bounds.size : Vector3.one;
            // A quad has no extent along its normal; a degenerate one reads as 1.
            if (size.x < 1e-6f) size.x = 1f;
            if (size.y < 1e-6f) size.y = 1f;
            if (size.z < 1e-6f) size.z = 1f;
            return size;
        }

        /// <summary>
        /// The frame the horizon map is baked and marched in, world space: the
        /// contact's position and rotation, unit scale (the map's lengths are
        /// metric, whatever the node's scale); a Z-up contact's rotation
        /// (<see cref="zUpFrames"/>) is read in the FBX axes its frame vectors are
        /// written in (<see cref="ZUpFrameAxes"/>).
        /// </summary>
        internal Matrix4x4 ContactFrame()
        {
            if (contact != null)
            {
                var rotation = contact.rotation;
                if (zUpFrames) rotation *= ZUpFrameAxes;
                return Matrix4x4.TRS(contact.position, rotation, Vector3.one);
            }
            return Matrix4x4.TRS(transform.position, Quaternion.identity, Vector3.one);
        }

        /// <summary>
        /// The map's frame, world space: its origin and three unit axes (Frame A,
        /// Frame B, and the contact's own up -- never a cross product of A and B: two
        /// producers bake with opposite bearing senses), plus the ground's height
        /// along that up and the world length of one space unit along each axis.
        /// </summary>
        internal void HorizonAxes(out Vector3 origin, out Vector3 axisA, out Vector3 axisB, out Vector3 axisUp,
            out float groundInFrame, out float sA, out float sB, out float sUp)
        {
            Matrix4x4 frame = ContactFrame();
            origin = frame.GetColumn(3);
            axisA = frame.MultiplyVector(horizonFrameA).normalized;
            axisB = frame.MultiplyVector(horizonFrameB).normalized;
            axisUp = frame.MultiplyVector(Vector3.up).normalized;
            // The map's heights were baked above the ground plane, and the march
            // projects every fragment onto it; the plane's own lift must not enter.
            // The ground is the space's, carried to world, under the contact.
            Matrix4x4 toWorld = SpaceToWorld();
            Matrix4x4 toSpace = toWorld.inverse;
            Vector3 c = toSpace.MultiplyPoint3x4(origin);
            Vector3 groundPoint = toWorld.MultiplyPoint3x4(new Vector3(c.x, ground, c.z));
            groundInFrame = Vector3.Dot(groundPoint - origin, axisUp);
            sA = WorldPerSpace(toSpace, axisA);
            sB = WorldPerSpace(toSpace, axisB);
            sUp = WorldPerSpace(toSpace, axisUp);
        }

        /// <summary>Read-modify-write the renderer's block with this plane's values.</summary>
        void WriteBlock(Light light)
        {
            var mat = _rend.sharedMaterial;
            _rend.GetPropertyBlock(_block);
            bool march = CanMarch;
            _block.SetFloat(ID_Fade, march ? Fade : 0f);
            if (march)
            {
                // The map is this plane's own: the shared material binds ONE plane's,
                // and a plane must never draw another's.
                SetMap(mat, ID_HorizonMap, horizonMap);
                _block.SetVector(ID_HorizonRect, horizonRect);
                int levels = Mathf.RoundToInt(Mathf.Log(Mathf.Max(1, horizonSize), 2f));
                _block.SetVector(ID_HorizonParams, new Vector4(horizonSize, horizonSpans, levels, 0f));
                // The frame, in WORLD space: an origin and three axes. The shared body
                // takes no matrix and no unit slot, so the footprint and the height
                // scale arrive in world units and the ground as a height along up.
                HorizonAxes(out Vector3 origin, out Vector3 axisA, out Vector3 axisB, out Vector3 axisUp,
                    out float groundInFrame, out float sA, out float sB, out float sUp);
                Vector4 bounds = new Vector4(horizonBounds.x * sA, horizonBounds.y * sA,
                                             horizonBounds.z * sB, horizonBounds.w * sB);
                _block.SetVector(ID_HorizonBounds, bounds);
                _block.SetVector(ID_HorizonMeta, new Vector4(horizonHeightScale * sUp, groundInFrame, 0f, 0f));
                _block.SetVector(ID_Origin, new Vector4(origin.x, origin.y, origin.z, 1f));
                _block.SetVector(ID_AxisA, axisA);
                _block.SetVector(ID_AxisB, axisB);
                _block.SetVector(ID_AxisUp, axisUp);
                Vector4 src;
                if (light == null)
                {
                    src = new Vector4(0f, -1f, 0f, 0f);  // no source: noon
                }
                else if (light.type == LightType.Directional)
                {
                    // The uniform holds the direction the source SHINES (w = 0), the
                    // reference's vocabulary; the shader negates it.
                    Vector3 d = light.transform.forward.normalized;
                    src = new Vector4(d.x, d.y, d.z, 0f);
                }
                else
                {
                    Vector3 p = light.transform.position;
                    src = new Vector4(p.x, p.y, p.z, 1f);
                }
                _block.SetVector(ID_Source, src);
                // FULL widths: the shared body halves them once, in the shader. A
                // diameter is a space length too (the instance's mean scale).
                Matrix4x4 toWorld = SpaceToWorld();
                float sMean = Mathf.Pow(Mathf.Max(Mathf.Abs(toWorld.determinant), 1e-12f), 1f / 3f);
                float angle = sourceAngle * Mathf.Deg2Rad;
                _block.SetVector(ID_SourceRadius, new Vector4(sourceSize * sMean, angle, 0f, 0f));

                // The cache's key, in the contact's frame (the frame is rigid).
                Matrix4x4 toContact = ContactFrame().inverse;
                Matrix4x4 local = toContact * transform.localToWorldMatrix;
                float[] k = cacheKeyNext;
                for (int i = 0; i < 16; i++) k[i] = local[i];
                Vector3 srcInFrame = src.w > 0.5f
                    ? toContact.MultiplyPoint3x4(src)
                    : toContact.MultiplyVector(src);
                k[16] = srcInFrame.x; k[17] = srcInFrame.y; k[18] = srcInFrame.z; k[19] = src.w;
                k[20] = groundInFrame;
                k[21] = sourceSize * sMean; k[22] = angle;
                k[23] = bounds.x; k[24] = bounds.y; k[25] = bounds.z; k[26] = bounds.w;
                k[27] = horizonHeightScale * sUp;
                if (horizonMap != _keyedMap)
                {
                    _keyedMap = horizonMap;
                    _mapSerial++;
                }
                k[28] = _mapSerial;
                cachePitch = Mathf.Min(bounds.y - bounds.x, bounds.w - bounds.z) / Mathf.Max(horizonSize, 1);
            }
            bool cache = march && cacheShadow && ShadowPlaneCache.CanCache(this);
            if (!cache)
            {
                ShadowPlaneCache.Release(this);
                _block.SetFloat(ID_Cached, 0f);
            }
            else if (!cacheValid || ShadowPlaneCache.Moved(cacheKey, cacheKeyNext))
            {
                ShadowPlaneCache.Stage(this);
            }
            _rend.SetPropertyBlock(_block);
        }

        // ---- the cache's view of the plane ------------------------------------
        internal Renderer CacheRenderer => _rend;
        internal MaterialPropertyBlock CacheBlock => _block;
        internal Mesh CacheMesh => _filter != null ? _filter.sharedMesh : null;

        Material _passMaterial;
        int _pass = -1;

        /// <summary>The material's SHADOWPLANECACHE pass (-1: none, e.g. a plane on the
        /// pipeline's unlit fallback), looked up once per material rather than every frame.</summary>
        internal int CachePass()
        {
            var mat = _rend != null ? _rend.sharedMaterial : null;
            if (mat != _passMaterial)
            {
                _passMaterial = mat;
                _pass = mat != null ? mat.FindPass(ShadowPlaneCache.PassName) : -1;
            }
            return _pass;
        }

        /// <summary>The world size of the plane's two UV axes, off the mesh's
        /// first triangle: the cache rect's two sides, whatever way the quad's UVs
        /// are laid out. Zero when the mesh has no usable triangle.</summary>
        internal Vector2 UvExtent()
        {
            Mesh mesh = CacheMesh;
            if (mesh != _uvMesh)
            {
                _uvMesh = mesh;
                _uvOk = false;
                if (mesh != null && mesh.isReadable && mesh.vertexCount >= 3)
                {
                    int[] tri = mesh.GetTriangles(0);
                    var verts = mesh.vertices;
                    var uvs = mesh.uv;
                    if (tri.Length >= 3 && uvs.Length == verts.Length)
                    {
                        Vector3 e1 = verts[tri[1]] - verts[tri[0]], e2 = verts[tri[2]] - verts[tri[0]];
                        Vector2 d1 = uvs[tri[1]] - uvs[tri[0]], d2 = uvs[tri[2]] - uvs[tri[0]];
                        float det = d1.x * d2.y - d2.x * d1.y;
                        if (Mathf.Abs(det) > 1e-12f)
                        {
                            _uvDu = (e1 * d2.y - e2 * d1.y) / det;
                            _uvDv = (e2 * d1.x - e1 * d2.x) / det;
                            _uvOk = true;
                        }
                    }
                }
            }
            Matrix4x4 m = transform.localToWorldMatrix;
            if (_uvOk)
                return new Vector2(m.MultiplyVector(_uvDu).magnitude, m.MultiplyVector(_uvDv).magnitude);
            if (mesh == null) return Vector2.zero;
            // An imported mesh is rarely CPU-readable: the quad's bounds, its
            // larger in-plane side for both axes -- more texels than the shorter
            // needs, never fewer.
            Vector3 size = mesh.bounds.size;
            float sx = m.MultiplyVector(new Vector3(size.x, 0f, 0f)).magnitude;
            float sy = m.MultiplyVector(new Vector3(0f, size.y, 0f)).magnitude;
            float sz = m.MultiplyVector(new Vector3(0f, 0f, size.z)).magnitude;
            float side = Mathf.Max(sx, Mathf.Max(sy, sz));
            return side > 1e-9f ? new Vector2(side, side) : Vector2.zero;
        }

        internal void SetCacheRect(Vector4 rect, bool cached)
        {
            _rend.GetPropertyBlock(_block);
            _block.SetVector(ID_CacheRect, rect);
            _block.SetFloat(ID_Cached, cached ? 1f : 0f);
            _rend.SetPropertyBlock(_block);
        }

        internal void RestorePlacement()
        {
            transform.localPosition = placedPosition;
            transform.localRotation = placedRotation;
            transform.localScale = placedScale;
        }

        internal void RememberPlacement()
        {
            placedPosition = transform.localPosition;
            placedRotation = transform.localRotation;
            placedScale = transform.localScale;
        }

        /// <summary>World units per space unit along the world direction
        /// <paramref name="axis"/> (a unit vector).</summary>
        static float WorldPerSpace(Matrix4x4 toSpace, Vector3 axis)
        {
            return 1f / Mathf.Max(toSpace.MultiplyVector(axis).magnitude, 1e-12f);
        }

        /// <summary>
        /// Bind <paramref name="map"/> to <paramref name="id"/> for this plane.
        /// Textures cannot be instanced, so the block overrides only a map the
        /// shared material does not already bind -- and an override the plane no
        /// longer needs is re-pointed at the material's own, never left stale
        /// (a block cannot drop one property).
        /// </summary>
        void SetMap(Material mat, int id, Texture map)
        {
            if (!MaterialBinds(mat, id, map) || _block.HasTexture(id))
                _block.SetTexture(id, map);
        }

        static bool MaterialBinds(Material mat, int id, Texture tex)
        {
            return mat != null && mat.HasProperty(id) && mat.GetTexture(id) == tex;
        }
    }
}
