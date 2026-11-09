// ----------------------------------------------------------------------------
// The Lightmaps feature's component (M3trik.Unitytk.Lightmaps, the runtime part):
// it ships in players. Its importer, its texture roles and its actions are the
// editor part's (LightmapsImporter, LightmapsTextureRoles, LightmapsActions).
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Lightmaps.Editor")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Lightmaps.Tests")]

namespace M3trik.Unitytk.Lightmaps
{
    /// <summary>
    /// A lightmapped prefab's lightmaps, in Unity's own terms: the maps, each renderer
    /// they light (a reference, with its map and its rect in it), the lightmap mode the
    /// maps are read in and the reflection probe that goes with them. Whenever it is
    /// enabled it binds them through Unity's native fields --
    /// <see cref="LightmapSettings.lightmaps"/>, <see cref="Renderer.lightmapIndex"/>,
    /// <see cref="Renderer.lightmapScaleOffset"/> -- sets the mode and switches its probe
    /// on; disabled, it gives each slot and each renderer's index and rect back and
    /// switches the probe off. The mode is the scene's (one scene reads its lightmaps in
    /// one): it stays as the set left it. No material or property block is touched: the
    /// pipeline's own Lit reads the lightmaps and the probe.
    /// </summary>
    /// <remarks>
    /// <para><b>One owner per scene.</b> While its scene has a Lighting Data Asset,
    /// Unity's own bake owns the scene's baked lighting and the set stands down: it
    /// binds nothing and its probe stays off. Otherwise it binds. Whichever ran last
    /// owns the scene (a bake writes the asset; the Apply action clears it), and the two
    /// never mix. In the editor the editor part answers whether a scene has one; in a
    /// player, the mark its build left in each scene a bake owns does, so a prefab
    /// instantiated into such a scene stands down too.</para>
    /// <para>A scene loaded or unloaded around a bound set rebuilds
    /// <see cref="LightmapSettings.lightmaps"/> its own way -- a Single load empties it and
    /// fills it with the new scene's bake, under a set kept by <c>DontDestroyOnLoad</c>
    /// (measured, 6000.3.10f1) -- so the set binds again then.</para>
    /// <para>Prior art: Unity's own lightmaps live in <see cref="LightmapSettings"/>
    /// and a renderer's lightmap index and rect, which Unity fills only from a bake of
    /// its own (the Lighting Data Asset) and empties on every scene load: a lightmap
    /// made elsewhere has no Unity API to keep it. This holds the bindings and puts them
    /// back through those same native fields whenever it is enabled.</para>
    /// </remarks>
    [AddComponentMenu("unitytk/Lightmap Set")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class LightmapSet : MonoBehaviour
    {
        /// <summary>One lightmap: Unity's <see cref="LightmapData"/>, held as asset references.</summary>
        [Serializable]
        public sealed class Map
        {
            [Tooltip("The lightmap's light (LightmapData.lightmapColor): a texture imported as a Lightmap.")]
            public Texture2D color;

            [Tooltip("Its dominant directions (LightmapData.lightmapDir), read in the Combined Directional mode; none for a non-directional map.")]
            public Texture2D direction;

            [Tooltip("Its shadow mask (LightmapData.shadowMask), where the bake that made it had one.")]
            public Texture2D shadowMask;
        }

        /// <summary>One renderer a map lights: which map, and its rect in it.</summary>
        [Serializable]
        public sealed class Receiver
        {
            [Tooltip("The renderer the map lights, read through its mesh's second UV set (uv2).")]
            public Renderer renderer;

            [Tooltip("Its map: an index into the set's maps.")]
            public int map;

            [Tooltip("Its rect in the map (Renderer.lightmapScaleOffset): uv2 * (x, y) + (z, w).")]
            public Vector4 scaleOffset = new Vector4(1f, 1f, 0f, 0f);
        }

        [Tooltip("The lightmap mode the maps are read in (LightmapSettings.lightmapsMode, the scene's): Non-Directional for a delivery's maps.")]
        public LightmapsMode mode = LightmapsMode.NonDirectional;

        [Tooltip("The lightmaps, each bound into LightmapSettings.lightmaps while the set is enabled.")]
        public List<Map> maps = new List<Map>();

        [Tooltip("Each renderer the maps light, with its map and rect.")]
        public List<Receiver> receivers = new List<Receiver>();

        [Tooltip("The reflection probe that goes with the maps: on while the set binds, off while it stands down.")]
        public ReflectionProbe probe;

        // The probe's box as its import placed it, at a scale of 1 (ReflectionProbe.center
        // and size): a probe's box is world-sized, and no transform scale reaches it
        // (measured 2026-10), so each bind sizes it by the probe's lossyScale. Zero for a
        // probe no import placed, whose box is left as it is.
        [SerializeField, HideInInspector] internal Vector3 probeCenter;
        [SerializeField, HideInInspector] internal Vector3 probeSize;

        // The asset GUID of the panorama the probe's cubemap is made of, set by the import:
        // the editor's Apply gives it the import the cubemap needs. A GUID, not a
        // reference, which would ship the panorama in every player.
        [SerializeField, HideInInspector] internal string probeSource;

        /// <summary>
        /// Whether Unity's bake owns a scene (it has a Lighting Data Asset): the editor
        /// part's answer, registered whenever its domain loads; null in a player, which
        /// reads the mark its build left in the scene (<see cref="LightmapBakeMark"/>).
        /// </summary>
        internal static Func<Scene, bool> BakeOwns;

        // What this set bound, for its release: each slot it holds and each renderer's
        // fields as it found them. Runtime state: Unity keeps no lightmap it did not bake.
        [NonSerialized] bool _bound;
        [NonSerialized] readonly List<Texture2D> _held = new List<Texture2D>();
        [NonSerialized] readonly List<(Renderer renderer, int index, Vector4 rect, int boundIndex, Vector4 boundRect)> _found =
            new List<(Renderer, int, Vector4, int, Vector4)>();

        /// <summary>Whether the set's maps are bound now.</summary>
        public bool Bound => _bound;

        /// <summary>Whether Unity's own bake owns the set's scene, so the set binds nothing.</summary>
        public bool StandsDown => BakeOwns != null ? BakeOwns(gameObject.scene) : LightmapBakeMark.In(gameObject.scene);

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Bind();
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Release();
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode loaded) => ScenesChanged();

        void OnSceneUnloaded(Scene scene) => ScenesChanged();

        void OnActiveSceneChanged(Scene from, Scene to) => ScenesChanged();

        /// <summary>
        /// A scene loaded, unloaded or made active: a bound set binds again, since Unity
        /// rebuilds <see cref="LightmapSettings.lightmaps"/> and sets the active scene's
        /// mode then, whatever the set put there.
        /// </summary>
        internal void ScenesChanged()
        {
            if (_bound && isActiveAndEnabled) Refresh();
        }

        /// <summary>
        /// Bind the maps, unless the set is bound, disabled or stands down (its probe off
        /// then): each map into a slot of <see cref="LightmapSettings.lightmaps"/> -- one
        /// already holding its texture, else a free one, else a new one at the end -- the
        /// mode set, each receiver pointed at its map's slot and rect, and the probe on,
        /// its box sized by its scale.
        /// </summary>
        internal void Bind()
        {
            if (_bound || !isActiveAndEnabled) return;
            if (StandsDown)
            {
                SetProbe(false);
                return;
            }
            var all = new List<LightmapData>(LightmapSettings.lightmaps ?? Array.Empty<LightmapData>());
            var slots = new int[maps.Count];
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                slots[i] = -1;
                if (map == null || map.color == null) continue;
                int slot = all.FindIndex(d => d != null && d.lightmapColor == map.color);
                if (slot < 0) slot = all.FindIndex(Free);
                var data = new LightmapData { lightmapColor = map.color, lightmapDir = map.direction, shadowMask = map.shadowMask };
                if (slot < 0)
                {
                    all.Add(data);
                    slot = all.Count - 1;
                }
                else if (all[slot] == null || all[slot].lightmapColor != map.color)
                {
                    all[slot] = data;
                }
                slots[i] = slot;
                _held.Add(map.color);
            }
            if (_held.Count > 0)
            {
                if (LightmapSettings.lightmapsMode != mode) LightmapSettings.lightmapsMode = mode;
                LightmapSettings.lightmaps = all.ToArray();
            }
            foreach (Receiver receiver in receivers)
            {
                Renderer r = receiver != null ? receiver.renderer : null;
                if (r == null || receiver.map < 0 || receiver.map >= slots.Length || slots[receiver.map] < 0) continue;
                _found.Add((r, r.lightmapIndex, r.lightmapScaleOffset, slots[receiver.map], receiver.scaleOffset));
                r.lightmapIndex = slots[receiver.map];
                r.lightmapScaleOffset = receiver.scaleOffset;
            }
            _bound = true;
            SizeProbe();
            SetProbe(true);
        }

        /// <summary>
        /// Give back what <see cref="Bind"/> took: each receiver's index and rect as it
        /// found them (one something else has rebound since is left as it is), each slot
        /// no other bound set reads freed (the free slots at the end dropped) and the
        /// probe off. The mode stays: it is the scene's. Every list is kept.
        /// </summary>
        internal void Release() => Release(false);

        void Release(bool keepProbe)
        {
            if (!keepProbe) SetProbe(false);
            if (!_bound) return;
            _bound = false;
            foreach (var (r, index, rect, boundIndex, boundRect) in _found)
            {
                if (r == null || r.lightmapIndex != boundIndex || r.lightmapScaleOffset != boundRect) continue;
                r.lightmapIndex = index;
                r.lightmapScaleOffset = rect;
            }
            _found.Clear();
            if (_held.Count == 0) return;
            var others = new HashSet<Texture2D>();
            foreach (LightmapSet set in FindObjectsByType<LightmapSet>(FindObjectsSortMode.None))
                if (set != this && set._bound)
                    foreach (Texture2D held in set._held) others.Add(held);
            var all = new List<LightmapData>(LightmapSettings.lightmaps ?? Array.Empty<LightmapData>());
            bool changed = false;
            for (int i = 0; i < all.Count; i++)
            {
                LightmapData d = all[i];
                if (d == null || d.lightmapColor == null || !_held.Contains(d.lightmapColor) || others.Contains(d.lightmapColor)) continue;
                all[i] = new LightmapData();
                changed = true;
            }
            _held.Clear();
            while (all.Count > 0 && Free(all[all.Count - 1]))
            {
                all.RemoveAt(all.Count - 1);
                changed = true;
            }
            if (changed) LightmapSettings.lightmaps = all.ToArray();
        }

        /// <summary>
        /// Release, then bind again: what the editor runs once the scene's owner, or the
        /// set's lists, may have changed. The probe is switched only where the outcome changes.
        /// </summary>
        internal void Refresh()
        {
            Release(true);
            Bind();
        }

#if UNITY_EDITOR
        // Undo, or an edit in the inspector, changes the lists and never a renderer's
        // binding (runtime state no undo restores): a bound set binds its lists as they
        // are now, so one Ctrl+Z after an action takes its lightmaps off the renderers too.
        void OnValidate()
        {
            if (_bound && isActiveAndEnabled) Refresh();
        }
#endif

        // A slot no map fills: one a released set freed.
        static bool Free(LightmapData d) => d == null || (d.lightmapColor == null && d.lightmapDir == null && d.shadowMask == null);

        // The probe's box as its import placed it, through the probe's scale: Unity's box
        // projection is world-sized and axis-aligned, so a scaled instance projects onto
        // its own size (a rotated one cannot: Validate warns).
        void SizeProbe()
        {
            if (probe == null || probeSize == Vector3.zero) return;
            Vector3 scale = probe.transform.lossyScale;
            Vector3 center = Vector3.Scale(probeCenter, scale);
            Vector3 size = Vector3.Scale(probeSize, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            if (probe.center != center) probe.center = center;
            if (probe.size != size) probe.size = size;
        }

        void SetProbe(bool on)
        {
            if (probe != null && probe.enabled != on) probe.enabled = on;
        }
    }
}
