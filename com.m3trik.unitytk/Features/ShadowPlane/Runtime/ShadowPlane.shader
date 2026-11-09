// ----------------------------------------------------------------------------
// ShadowPlane.shader -- the unlit-transparent material every shadow plane
// draws with (the ShadowPlane component beside this file; ShadowPlaneImporter,
// the feature's Editor/ part, rewires an imported plane to it).
//
// Legacy CG on purpose: the drawing pass has no LightMode tag and no pipeline
// includes, so it renders in Built-in and in URP's SRPDefaultUnlit pass, which
// the URP host's Draws counts (HDRP, which no host serves, keeps Unity's
// imported material). GPU-instanced: one material per model, every per-plane
// value in the instancing buffer, written by ShadowPlane through a
// MaterialPropertyBlock.
//
// One mode, the horizon map: the target's height-field map, marched toward the
// source; alpha = the march x _Fade, the fade the component computes each frame
// from the placement (pythontk ShadowProjection.fade, ported in
// ShadowPlaneModel), never above the plane's intensity. The evaluation is NOT
// written here: it is `ShadowPlaneHorizon.hlsl`, a generated mirror of
// pythontk's `geo_utils/shadow_horizon.glsl` (the single source of truth, run
// by the producers' previews and the WebXR viewer too), spliced by
// `m3trik/scripts/sync_shadow_shaders.py` and guarded by its `--check`.
// `HeightFieldMap.alpha` beside that source is the numeric oracle the Unity
// pixels are compared against (its C# port, `HeightFieldMap.Alpha`, in
// ShadowPlaneActionsTests; `unitytk/test/test_shadow_plane_runtime_integration.py`).
//
// The march does not run per screen pixel: with _Cached on, the plane samples
// its rect (_CacheRect) of the global R8 atlas _ShadowCache, into which
// ShadowPlaneCache marches the plane's own UV square through the
// SHADOWPLANECACHE pass below -- only when the source, the placement or the
// ground moved in the contact's frame, every due plane in one command buffer.
// A frame then costs one bilinear fetch a pixel, both eyes of a headset share
// one march, and the atlas being one global texture keeps the planes
// instanced. _Cached off (a plane not yet marched, or cacheShadow off)
// marches per pixel.
//
// What IS this file's own is the host half: the instanced property block and
// `SH_Fetch`, which owns the one row flip Unity needs (a tile's row 0 is the
// PNG's top row, while `Texture2D.Load` counts texel rows from the bottom).
// The frame reaches the shader as an origin plus three WORLD axes, not a
// matrix, so every per-plane binding fits in the instancing buffer as a Vector.
Shader "m3trik/ShadowPlane"
{
    Properties
    {
        _Fade("Fade (the component's, each frame; 0 until it writes one)", Range(0.0, 1.0)) = 0.0
        _HorizonMap("Horizon Map (data: linear, no mips, uncompressed)", 2D) = "black" {}
        _HorizonRect("Horizon Rect (scale xy, offset zw)", Vector) = (1, 1, 0, 0)
        _HorizonParams("Horizon Params (footprint pixels, spans, pyramid levels, 0)", Vector) = (128, 2, 7, 0)
        _HorizonBounds("Horizon Bounds (a0, a1, b0, b1 in the frame, scene units)", Vector) = (-1, 1, -1, 1)
        _HorizonMeta("Horizon Meta (height scale scene units, ground in frame, 0, 0)", Vector) = (1, 0, 0, 0)
        _Origin("Contact Origin (world)", Vector) = (0, 0, 0, 1)
        _AxisA("Frame A (bearing zero, WORLD)", Vector) = (1, 0, 0, 0)
        _AxisB("Frame B (bearing increasing toward, WORLD)", Vector) = (0, 0, 1, 0)
        _AxisUp("Frame Up (WORLD)", Vector) = (0, 1, 0, 0)
        _Source("Source (xyz WORLD; w 1 = position, 0 = shine direction)", Vector) = (0, 10, 0, 1)
        _SourceRadius("Source Size (x diameter in scene units, y angular diameter rad)", Vector) = (0, 0, 0, 0)
        _CacheRect("Cache Rect (x0, y0, w, h in the atlas, normalised)", Vector) = (0, 0, 1, 1)
        [Toggle] _Cached("Draw the cached march", Float) = 0
        // The frame travels as an origin and three world-space axes rather
        // than a world-to-contact matrix: every binding is then a Vector, so
        // none of them needs the Properties-block exemption a float4x4 does,
        // and the instancing buffer loses fifteen floats per plane.
        // _ShadowCache is deliberately NOT a property: it is the global the
        // controller binds (Shader.SetGlobalTexture), and a declared property
        // would let every material's default shadow it.
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }
        LOD 100

        CGINCLUDE
        #include "UnityCG.cginc"

        Texture2D<float4> _HorizonMap;   // loaded by texel, never sampled
        sampler2D _ShadowCache;          // the global cache atlas (R)
        float4 _ShadowCacheTexel;        // its texel size: 1/w, 1/h (global)
        float4 _ShadowCacheClip;         // y: -1 where a texture's rows start at the top (global)

        UNITY_INSTANCING_BUFFER_START(Props)
            UNITY_DEFINE_INSTANCED_PROP(float, _Fade)
            UNITY_DEFINE_INSTANCED_PROP(float4, _HorizonRect)
            UNITY_DEFINE_INSTANCED_PROP(float4, _HorizonParams)
            UNITY_DEFINE_INSTANCED_PROP(float4, _HorizonBounds)
            UNITY_DEFINE_INSTANCED_PROP(float4, _HorizonMeta)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Origin)
            UNITY_DEFINE_INSTANCED_PROP(float4, _AxisA)
            UNITY_DEFINE_INSTANCED_PROP(float4, _AxisB)
            UNITY_DEFINE_INSTANCED_PROP(float4, _AxisUp)
            UNITY_DEFINE_INSTANCED_PROP(float4, _Source)
            UNITY_DEFINE_INSTANCED_PROP(float4, _SourceRadius)
            UNITY_DEFINE_INSTANCED_PROP(float4, _CacheRect)
            UNITY_DEFINE_INSTANCED_PROP(float, _Cached)
        UNITY_INSTANCING_BUFFER_END(Props)

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv     : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 pos      : SV_POSITION;
            float2 uv       : TEXCOORD0;
            float3 worldPos : TEXCOORD1;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        // ---- the shared horizon body -----------------------------------
        // Everything between here and HorizonAlpha is Unity's HOST half: the
        // texel address, which the shared body deliberately never computes.
        // The evaluation itself lives in ShadowPlaneHorizon.hlsl, generated
        // from pythontk/geo_utils/shadow_horizon.glsl by
        // m3trik/scripts/sync_shadow_shaders.py -- never hand-edited, and
        // pinned to HeightFieldMap.alpha by the runtime integration suite.

        // The map block's place in its texture and the footprint's size, per
        // draw: HorizonAlpha works them out once (ShAddress), not on every
        // fetch of the march.
        static int2 shBlock;
        static int shS;

        void ShAddress()
        {
            float4 hp = UNITY_ACCESS_INSTANCED_PROP(Props, _HorizonParams);
            float4 hrect = UNITY_ACCESS_INSTANCED_PROP(Props, _HorizonRect);
            shS = max(1, (int)round(hp.x));
            int spans = max(1, (int)round(hp.y));
            // The block is (K + 2) x S by S texels; its rect -- exact, never
            // gutter-inset: a data map is addressed by texel -- places it in
            // the atlas.
            float2 texSize = float2((spans + 2) * shS, shS) / max(hrect.xy, 1e-6);
            shBlock = (int2)round(hrect.zw * texSize);
        }

        // One texel of tile `tile` (0 .. K - 1 the spans, K the aux tile,
        // K + 1 the pyramid), texel (xi, yi) inside it with (0, 0) the tile's
        // top-left -- the frame's (a0, b0) corner. ONE row flip: row 0 of a
        // tile is the PNG's top row, while Unity's Load counts texel rows from
        // the bottom.
        float4 SH_Fetch(int tile, int xi, int yi)
        {
            int px = shBlock.x + tile * shS + clamp(xi, 0, shS - 1);
            int py = shBlock.y + (shS - 1 - clamp(yi, 0, shS - 1));
            return _HorizonMap.Load(int3(px, py, 0));
        }

        #define SH_HLSL 1
        #include "ShadowPlaneHorizon.hlsl"

        // Alpha of the horizon map at a world-space fragment.
        float HorizonAlpha(float3 worldPos)
        {
            ShAddress();
            float4 hp = UNITY_ACCESS_INSTANCED_PROP(Props, _HorizonParams);
            float4 meta = UNITY_ACCESS_INSTANCED_PROP(Props, _HorizonMeta);
            ShField g = ShMakeField(
                (int)round(hp.x), (int)round(hp.y), (int)round(hp.z),
                UNITY_ACCESS_INSTANCED_PROP(Props, _HorizonBounds),
                meta.x, meta.y);
            return ShAlpha(g, worldPos,
                UNITY_ACCESS_INSTANCED_PROP(Props, _Origin).xyz,
                UNITY_ACCESS_INSTANCED_PROP(Props, _AxisA).xyz,
                UNITY_ACCESS_INSTANCED_PROP(Props, _AxisB).xyz,
                UNITY_ACCESS_INSTANCED_PROP(Props, _AxisUp).xyz,
                UNITY_ACCESS_INSTANCED_PROP(Props, _Source),
                UNITY_ACCESS_INSTANCED_PROP(Props, _SourceRadius).xy);
        }
        ENDCG

        // ---- the plane --------------------------------------------------
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // 3.5: integer bit ops and Texture2D.Load (GLES 3 / SM 4 class).
            #pragma target 3.5

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float4 cacheRect = UNITY_ACCESS_INSTANCED_PROP(Props, _CacheRect);
                float cached = UNITY_ACCESS_INSTANCED_PROP(Props, _Cached);
                float alpha;
                if (cached > 0.5)
                {
                    // half a texel in from the rect's edge: the gutter never
                    // bleeds in (one level: no mips, no gradients)
                    float2 inset = 0.5 * _ShadowCacheTexel.xy;
                    float2 at = cacheRect.xy
                        + clamp(i.uv * cacheRect.zw, inset, cacheRect.zw - inset);
                    alpha = tex2Dlod(_ShadowCache, float4(at, 0.0, 0.0)).r;
                }
                else
                {
                    alpha = HorizonAlpha(i.worldPos);
                }
                // A shadow: black, at the march times the fade (which the
                // plane's intensity caps).
                return fixed4(0.0, 0.0, 0.0, alpha * UNITY_ACCESS_INSTANCED_PROP(Props, _Fade));
            }
            ENDCG
        }

        // ---- the cache: the plane's own UV square marched into its rect of
        // the atlas. Never drawn by a camera (a LightMode no pipeline renders);
        // ShadowPlaneCache draws it through a command buffer. The clip
        // position is written outright, the one platform fact applied by
        // hand: where a texture's rows start at the top (D3D, Metal, Vulkan)
        // the rows go in flipped, as a camera's projection flips them, so the
        // atlas samples the right way up. (UNITY_MATRIX_VP is no help: a
        // command buffer's SetViewProjectionMatrices never reached it outside
        // a camera's render -- measured, the bake drew the quad through the
        // last camera's view, edge-on, one line of texels.)
        Pass
        {
            Name "SHADOWPLANECACHE"
            Tags { "LightMode" = "ShadowPlaneCache" }
            Blend Off
            ZWrite Off
            ZTest Always
            Cull Off

            CGPROGRAM
            #pragma vertex vertCache
            #pragma fragment fragCache
            #pragma target 3.5

            v2f vertCache(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float4 r = UNITY_ACCESS_INSTANCED_PROP(Props, _CacheRect);
                float2 c = (r.xy + v.uv * r.zw) * 2.0 - 1.0;
                o.pos = float4(c.x, c.y * _ShadowCacheClip.y, 0.5, 1.0);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.uv = v.uv;
                return o;
            }

            float4 fragCache(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                return float4(HorizonAlpha(i.worldPos), 0.0, 0.0, 1.0);
            }
            ENDCG
        }
    }
    // No FallBack: nothing simpler renders this correctly, and a named
    // fallback that fails to resolve only logs noise.
}
