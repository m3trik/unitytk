using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace M3trik.Unitytk.ShadowPlane
{
    /// <summary>
    /// The horizon planes' shadow cache: ONE R8 atlas of square slots, one per
    /// caching plane, bound globally as <c>_ShadowCache</c> (so the shared material
    /// stays instanced). A plane whose key moved (<see cref="ShadowPlane"/>
    /// stages itself from <c>WriteBlock</c>) is marched over its own UV square into
    /// a rect of its slot sized at <see cref="TexelInMapPixels"/> map pixels a
    /// texel, by the shader's SHADOWPLANECACHE pass; every staged plane goes
    /// through ONE command buffer per frame -- flushed as the frame's first camera
    /// renders (Camera.onPreCull in Built-in, beginContextRendering in a scriptable
    /// pipeline), after every plane's LateUpdate, with no component of its
    /// own -- stalest first, within <see cref="TexelsPerFrame"/>. A plane the budget defers keeps the placement
    /// it was marched at, so its image and its position never disagree; one with
    /// no cache yet marches per pixel until its turn. The WebXR viewer's
    /// shadow_rig.js caches the same way (one pass into one atlas: a target and a
    /// render per plane had cost four times as much, measured there).
    /// </summary>
    public static class ShadowPlaneCache
    {
        /// <summary>A plane's slot, texels square, a one-texel gutter included.</summary>
        public const int Slot = 256;
        public const int MinSide = 16;
        /// <summary>A cache texel, in map pixels: the shadow holds no detail finer
        /// than the map, and the penumbra softens what it does hold.</summary>
        public static float TexelInMapPixels = 3f;
        /// <summary>Texels marched per frame across every plane; the rest wait
        /// their turn. A light moving every frame costs a bounded march whatever
        /// the prop count.</summary>
        public static int TexelsPerFrame = 1 << 16;
        /// <summary>How far a key may drift (relative) before a plane re-marches.</summary>
        public const float Epsilon = 1e-5f;
        internal const int KeyLength = 29;
        internal const string PassName = "SHADOWPLANECACHE";

        static readonly int ID_ShadowCache = Shader.PropertyToID("_ShadowCache");
        static readonly int ID_ShadowCacheTexel = Shader.PropertyToID("_ShadowCacheTexel");
        static readonly int ID_ShadowCacheClip = Shader.PropertyToID("_ShadowCacheClip");

        static RenderTexture _atlas;
        static int _cols;
        static readonly List<ShadowPlane> _slots = new List<ShadowPlane>();
        static readonly List<ShadowPlane> _staged = new List<ShadowPlane>();
        static readonly List<ShadowPlane> _deferred = new List<ShadowPlane>();
        static readonly Predicate<ShadowPlane> Gone = c => c == null || !c.isActiveAndEnabled;
        static readonly Comparison<ShadowPlane> StalestFirst = (a, b) =>
            a.cacheValid != b.cacheValid ? (a.cacheValid ? 1 : -1) : a.cacheStamp.CompareTo(b.cacheStamp);
        static int _r8 = -1;  // SupportsRenderTextureFormat(R8), asked once
        static CommandBuffer _cmd;
        static int _frame;
        static bool _hooked;
        static int _flushedFrame = -1;

        /// <summary>Planes marched, and texels, since the domain loaded.</summary>
        public static int Bakes { get; private set; }
        public static long Texels { get; private set; }
        /// <summary>The atlas (null until a plane caches).</summary>
        public static RenderTexture Atlas => _atlas;

        internal static bool CanCache(ShadowPlane c)
        {
            if (_r8 < 0) _r8 = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8) ? 1 : 0;
            return _r8 == 1 && c.CachePass() >= 0 && c.CacheMesh != null && c.UvExtent() != Vector2.zero;
        }

        internal static bool Moved(float[] a, float[] b)
        {
            for (int i = 0; i < a.Length; i++)
            {
                float x = a[i], y = b[i];
                if (!(Mathf.Abs(x - y) <= Epsilon * Mathf.Max(1f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)))))
                    return true;
            }
            return false;
        }

        internal static void Stage(ShadowPlane c)
        {
            if (!_staged.Contains(c)) _staged.Add(c);
            if (!_hooked)
            {
                _hooked = true;
                Camera.onPreCull += _ => FlushOncePerFrame();
                RenderPipelineManager.beginContextRendering += (_, __) => FlushOncePerFrame();
            }
        }

        static void FlushOncePerFrame()
        {
            if (_staged.Count == 0 || _flushedFrame == Time.frameCount) return;
            _flushedFrame = Time.frameCount;
            Flush();
        }

        internal static void Release(ShadowPlane c)
        {
            _staged.Remove(c);
            if (c.cacheSlot >= 0 && c.cacheSlot < _slots.Count && _slots[c.cacheSlot] == c)
                _slots[c.cacheSlot] = null;
            c.cacheSlot = -1;
            c.cacheValid = false;
        }

        /// <summary>
        /// Free the atlas and the command buffer, every plane's slot given up, so the next
        /// caching plane makes an atlas again. The editor calls it before scripts reload: the
        /// reload empties the static, and an atlas it held (kept alive by its hide flags)
        /// would be orphaned, one per recompile.
        /// </summary>
        internal static void Dispose()
        {
            foreach (var c in _slots)
            {
                if (c == null) continue;
                c.cacheSlot = -1;
                c.cacheValid = false;  // its next apply stages it into a new atlas
            }
            _slots.Clear();
            _staged.Clear();
            _deferred.Clear();
            if (_atlas != null)
            {
                _atlas.Release();
                UnityEngine.Object.DestroyImmediate(_atlas);
            }
            _atlas = null;
            _cols = 0;
            if (_cmd != null) _cmd.Release();
            _cmd = null;
            Shader.SetGlobalTexture(ID_ShadowCache, (Texture)null);
        }

        /// <summary>March every staged plane now, in one command buffer. The
        /// render callbacks call it once a frame, before the first camera;
        /// <see cref="ShadowPlane.ApplyNow"/> calls it at once.</summary>
        public static void Flush()
        {
            _frame++;
            _staged.RemoveAll(Gone);
            if (_staged.Count == 0) return;
            // by index: a grown atlas appends the planes it re-stages
            for (int i = 0; i < _staged.Count; i++)
            {
                var c = _staged[i];
                if (c.cacheSlot >= 0 || Acquire(c)) continue;
                c.SetCacheRect(Vector4.zero, false);  // no slot left: it marches per pixel
                _staged.RemoveAt(i--);
            }
            _staged.Sort(StalestFirst);
            if (_cmd == null) _cmd = new CommandBuffer { name = "ShadowPlaneCache" };
            _cmd.Clear();
            _cmd.SetRenderTarget(_atlas);
            // the pass writes its clip position itself; where a texture's rows
            // start at the top it flips them, as a camera rendering into one does
            _cmd.SetGlobalVector(ID_ShadowCacheClip,
                new Vector4(1f, SystemInfo.graphicsUVStartsAtTop ? -1f : 1f, 0f, 0f));
            int budget = TexelsPerFrame, drawn = 0;
            _deferred.Clear();
            foreach (var c in _staged)
            {
                if (budget <= 0)
                {
                    _deferred.Add(c);
                    if (c.cacheValid) c.RestorePlacement();
                    else c.SetCacheRect(Vector4.zero, false);
                    continue;
                }
                Vector2 extent = c.UvExtent();
                float texel = Mathf.Max(TexelInMapPixels * c.cachePitch, 1e-6f);
                int room = Slot - 2;
                int w = Mathf.Clamp(Mathf.CeilToInt(extent.x / texel), MinSide, room);
                int h = Mathf.Clamp(Mathf.CeilToInt(extent.y / texel), MinSide, room);
                int x0 = (c.cacheSlot % _cols) * Slot + 1, y0 = (c.cacheSlot / _cols) * Slot + 1;
                var rect = new Vector4((float)x0 / _atlas.width, (float)y0 / _atlas.height,
                    (float)w / _atlas.width, (float)h / _atlas.height);
                c.SetCacheRect(rect, true);
                _cmd.DrawMesh(c.CacheMesh, c.transform.localToWorldMatrix,
                    c.CacheRenderer.sharedMaterial, 0, c.CachePass(), c.CacheBlock);
                Array.Copy(c.cacheKeyNext, c.cacheKey, c.cacheKey.Length);
                c.cacheValid = true;
                c.cacheStamp = _frame;
                c.RememberPlacement();
                budget -= w * h;
                Bakes++;
                Texels += w * h;
                drawn++;
            }
            if (drawn > 0) Graphics.ExecuteCommandBuffer(_cmd);
            _staged.Clear();
            _staged.AddRange(_deferred);
            _deferred.Clear();
        }

        /// <summary>A slot for <paramref name="c"/>; the atlas grows (and every
        /// cache re-marches) when the slots run out, up to the GPU's largest
        /// texture -- past that a plane gets no slot (false) and marches per
        /// pixel.</summary>
        static bool Acquire(ShadowPlane c)
        {
            int free = _slots.IndexOf(null);
            if (free < 0)
            {
                free = _slots.Count;
                _slots.Add(null);
            }
            _slots[free] = c;
            c.cacheSlot = free;
            int need = _slots.Count;
            int cols = Mathf.Max(2, Mathf.CeilToInt(Mathf.Sqrt(need)));
            int side = Mathf.Min(Mathf.NextPowerOfTwo(cols) * Slot, SystemInfo.maxTextureSize);
            // grow only to a LARGER side: at the GPU's limit the wanted side stops
            // growing, and recreating it would wipe every cache for nothing
            if (_atlas == null || side > _atlas.width)
            {
                if (_atlas != null)
                {
                    _atlas.Release();
                    UnityEngine.Object.DestroyImmediate(_atlas);
                    foreach (var other in _slots) if (other != null) other.cacheValid = false;
                }
                _atlas = new RenderTexture(side, side, 0, RenderTextureFormat.R8, RenderTextureReadWrite.Linear)
                {
                    name = "ShadowPlaneCache",
                    useMipMap = false,
                    autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _atlas.Create();
                _cols = side / Slot;
                Shader.SetGlobalTexture(ID_ShadowCache, _atlas);
                Shader.SetGlobalVector(ID_ShadowCacheTexel, new Vector4(1f / side, 1f / side, side, side));
                foreach (var other in _slots)
                    if (other != null && !_staged.Contains(other)) _staged.Add(other);
            }
            if (free < _cols * _cols) return true;
            _slots[free] = null;
            c.cacheSlot = -1;
            return false;
        }
    }
}
