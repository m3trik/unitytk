// ----------------------------------------------------------------------------
// The Lightmaps feature's component (M3trik.Unitytk.Lightmaps, the runtime part):
// it ships in players. Its importer, its texture roles and its actions are the
// editor part's (LightmapsImporter, LightmapsTextureRoles, LightmapsActions).
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Lightmaps.Editor")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Lightmaps.Tests")]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Lightmaps.URP.Tests")]

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
    /// <para>Bound, it can also hold the scene to the two rules a baked delivery's hand-off
    /// states, each a switch of its own (off on a set added by hand; its import sets each as the
    /// record says), each given back as it releases, as its switch is turned off, and around every
    /// save of a scene (<see cref="ISaveSuspended"/>): the light its probe captured is the scene's
    /// ambient light and default reflection (<see cref="environment"/>: a part no map lights is lit
    /// by the room the maps were baked in, not by the scene's sky), and a realtime light that lights
    /// its mapped renderers and nothing else is switched off (<see cref="lightsOff"/>: the maps
    /// already hold that light). A light that also lights anything no map lights is never touched.
    /// How a mapped surface reflects is its probe's and its material's (the import sets the probe's
    /// intensity and puts the material on the pipeline's shader for a baked surface, URP's Baked Lit,
    /// from the record), never the set's.</para>
    /// </summary>
    /// <remarks>
    /// <para><b>One owner per scene.</b> While its scene has a Lighting Data Asset,
    /// Unity's own bake owns the scene's baked lighting and the set stands down: it
    /// binds nothing and its probe stays off. Otherwise it binds. Whichever ran last
    /// owns the scene (a bake writes the asset; the Apply action clears it), and the two
    /// never mix. In the editor the editor part answers whether a scene has one; in a
    /// player, the mark its build left in each scene a bake owns does, so a prefab
    /// instantiated into such a scene stands down too.</para>
    /// <para><b>One owner per environment.</b> Sets that light one scene's environment share it: one set's light
    /// shows (the same one whatever order they bind, let go or are saved in), and the scene has its own back once the
    /// last of them lets go, whichever that is. The environment is the active scene's (Unity's <see cref="RenderSettings"/>),
    /// so a set's light follows the active scene: the scene it left is given its own back.</para>
    /// <para>A scene loaded or unloaded around a bound set rebuilds
    /// <see cref="LightmapSettings.lightmaps"/> its own way -- a Single load empties it and
    /// fills it with the new scene's bake, under a set kept by <c>DontDestroyOnLoad</c>
    /// (measured, 6000.3.10f1) -- so the set binds again then.</para>
    /// <para>Prior art: Unity's own lightmaps live in <see cref="LightmapSettings"/>
    /// and a renderer's lightmap index and rect, which Unity fills only from a bake of
    /// its own (the Lighting Data Asset) and empties on every scene load: a lightmap
    /// made elsewhere has no Unity API to keep it. This holds the bindings and puts them
    /// back through those same native fields whenever it is enabled.</para>
    /// <para>The two rules are a hand-off's (its <c>rendering</c> block, where a file has
    /// lightmaps: <c>environment.source</c> the probe, <c>keyLight.disabledWhen</c>
    /// lightmapped), the same every reader of a delivery follows, so a model shows in any
    /// scene as it was approved. Unity's own baked workflow keeps them by baking: a Baked
    /// light lights nothing at run time, and the bake's ambient probe lights what is not
    /// lightmapped. A delivery's maps were baked elsewhere, so the scene it lands in still
    /// has its sun and its sky: measured on a production room (6000.3.10f1), a new scene's
    /// sun lit every mapped wall a second time, and its sky lit the unmapped parts at a
    /// tenth to a twentieth of the room's own light.</para>
    /// </remarks>
    [AddComponentMenu("unitytk/Lightmap Set")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class LightmapSet : MonoBehaviour, ISaveSuspended
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

        [Tooltip("While the set binds, the light its probe captured is the scene's ambient light and its default reflection, " +
                 "so a part no map lights is lit by the room the maps were baked in, not by the scene's sky. Given back as it " +
                 "releases, once turned off, and around every save. Off: the scene's own environment lights those parts. Its " +
                 "import sets it as the hand-off's rendering.environment.source says.")]
        public bool environment;

        [Tooltip("How bright the probe's light is as the scene's environment (1: as captured): the hand-off's " +
                 "rendering.environment.probeIntensity.")]
        [Min(0f)]
        public float environmentIntensity = 1f;

        [Tooltip("While the set binds, each realtime light that lights its mapped renderers and nothing else is switched off " +
                 "(the maps already hold the light), and switched back on as it releases, once turned off, and around every " +
                 "save. A light that also lights anything else is kept. Its import sets it as the hand-off's " +
                 "rendering.keyLight.disabledWhen says.")]
        public bool lightsOff;

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

        // The light the probe's panorama gives a surface facing each way, as spherical
        // harmonics (L2): 27 values, each colour channel's nine coefficients in turn
        // (SphericalHarmonicsL2[channel, coefficient]), set by the import. Empty for a
        // set with no panorama: the scene's ambient light stays its own.
        [SerializeField, HideInInspector] internal float[] ambient = Array.Empty<float>();

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
        [NonSerialized] readonly List<Light> _switchedOff = new List<Light>();
        [NonSerialized] Environment _foundEnvironment;  // its scene's own, as the first set to light it found it
        [NonSerialized] Environment _boundEnvironment;
        [NonSerialized] bool _lit;  // it lights a scene's environment by its probe's light
        [NonSerialized] Scene _litScene;  // which: the scene active as it lit it
        static bool _switching;  // a scene made active for a moment, to give its environment back: no scene change
        [NonSerialized] bool _suspended;  // released for a save, bound again once saved

        // A scene's environment lighting, as RenderSettings holds it.
        struct Environment
        {
            public AmbientMode mode;
            public SphericalHarmonicsL2 probe;
            public float intensity;
            public DefaultReflectionMode reflectionMode;
            public Texture reflection;
            public float reflectionIntensity;

            public static Environment Read() => new Environment
            {
                mode = RenderSettings.ambientMode,
                probe = RenderSettings.ambientProbe,
                intensity = RenderSettings.ambientIntensity,
                reflectionMode = RenderSettings.defaultReflectionMode,
                reflection = RenderSettings.customReflectionTexture,
                reflectionIntensity = RenderSettings.reflectionIntensity,
            };

            public void Write()
            {
                RenderSettings.ambientMode = mode;
                RenderSettings.ambientProbe = probe;
                RenderSettings.ambientIntensity = intensity;
                RenderSettings.defaultReflectionMode = reflectionMode;
                RenderSettings.customReflectionTexture = reflection;
                RenderSettings.reflectionIntensity = reflectionIntensity;
            }

            public bool Same(Environment other)
                => mode == other.mode && probe == other.probe && Mathf.Approximately(intensity, other.intensity)
                   && reflectionMode == other.reflectionMode && reflection == other.reflection
                   && Mathf.Approximately(reflectionIntensity, other.reflectionIntensity);
        }

        /// <summary>Whether the set's maps are bound now.</summary>
        public bool Bound => _bound;

        /// <summary>Whether Unity's own bake owns the set's scene, so the set binds nothing.</summary>
        public bool StandsDown => BakeOwns != null ? BakeOwns(gameObject.scene) : LightmapBakeMark.In(gameObject.scene);

        /// <summary>The realtime lights it switched off as it bound (<see cref="lightsOff"/>), on again as it releases.</summary>
        public IReadOnlyList<Light> SwitchedOff => _switchedOff;

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
            if (_bound && isActiveAndEnabled && !_switching) Refresh();
        }

        /// <summary>
        /// Bind the maps, unless the set is bound, disabled or stands down (its probe off
        /// then, and its rules given back): each map into a slot of
        /// <see cref="LightmapSettings.lightmaps"/> -- one already holding its texture, else a
        /// free one, else a new one at the end -- the mode set, each receiver pointed at its
        /// map's slot and rect, and the probe on, its box sized by its scale. Then its two
        /// rules, each as its switch now says: the realtime lights that reach its renderers
        /// switched off (<see cref="lightsOff"/>), and its probe's light made the scene's
        /// environment (<see cref="environment"/>); a rule switched off gives back what it held.
        /// </summary>
        internal void Bind()
        {
            if (_bound || !isActiveAndEnabled) return;
            if (StandsDown)
            {
                SetProbe(false);
                GiveLightsBack();
                GiveEnvironmentBack();
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
            if (lightsOff) SwitchLightsOff();
            else GiveLightsBack();
            if (environment && Room(out SphericalHarmonicsL2 room)) LightByProbe(room);
            else GiveEnvironmentBack();
        }

        /// <summary>
        /// The realtime lights in <paramref name="scene"/> that reach a renderer one of
        /// <paramref name="sets"/> binds: on, not one Unity baked (which lights nothing at run
        /// time), not an area light (baked only), its culling mask taking the renderer's layer
        /// and, short of a directional light, its range reaching the renderer's bounds.
        /// </summary>
        /// <param name="scene">The scene whose lights are looked at.</param>
        /// <param name="sets">The sets whose receivers they may reach.</param>
        /// <param name="mappedOnly">Only the lights that reach nothing else: no switched-on mesh renderer in the
        /// scene that no set's map lights.</param>
        internal static List<Light> LightsOver(Scene scene, IEnumerable<LightmapSet> sets, bool mappedOnly = false)
        {
            var mapped = new HashSet<Renderer>();
            foreach (LightmapSet set in sets)
                foreach (Receiver receiver in set.receivers)
                    if (receiver != null && receiver.renderer != null) mapped.Add(receiver.renderer);
            var over = new List<Light>();
            if (mapped.Count == 0 || !scene.IsValid() || !scene.isLoaded) return over;
            var others = new List<Renderer>();
            if (mappedOnly)
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
                        if (r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer) && !mapped.Contains(r)) others.Add(r);
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Light light in root.GetComponentsInChildren<Light>())
                {
                    if (!light.isActiveAndEnabled || light.type == LightType.Rectangle || light.type == LightType.Disc) continue;
                    LightBakingOutput baked = light.bakingOutput;
                    if (baked.isBaked && baked.lightmapBakeType == LightmapBakeType.Baked) continue;
                    if (!ReachesAny(light, mapped) || mappedOnly && ReachesAny(light, others)) continue;
                    over.Add(light);
                }
            return over;
        }

        static bool ReachesAny(Light light, IEnumerable<Renderer> renderers)
        {
            foreach (Renderer r in renderers)
                if (Reaches(light, r)) return true;
            return false;
        }

        // Whether light lights r: its culling mask takes r's layer and, short of a directional light, its range reaches r.
        static bool Reaches(Light light, Renderer r)
        {
            if (r == null || (light.cullingMask & (1 << r.gameObject.layer)) == 0) return false;
            return light.type == LightType.Directional || r.bounds.SqrDistance(light.transform.position) <= light.range * light.range;
        }

        // Each realtime light that lights the renderers the maps light and nothing else, switched off: the
        // maps hold that light already, and it would light them a second time. Said by the action that binds
        // (Apply), never on each bind.
        void SwitchLightsOff()
        {
            foreach (Light light in LightsOver(gameObject.scene, new[] { this }, mappedOnly: true))
            {
                light.enabled = false;
                _switchedOff.Add(light);
            }
        }

        /// <summary>
        /// The light <paramref name="ambient"/> holds (its import's 27 values: each colour channel's nine L2
        /// coefficients in turn, <c>SphericalHarmonicsL2[channel, coefficient]</c>); false for none.
        /// </summary>
        internal static bool Room(float[] ambient, out SphericalHarmonicsL2 light)
        {
            light = new SphericalHarmonicsL2();
            if (ambient == null || ambient.Length != 27) return false;
            for (int channel = 0; channel < 3; channel++)
                for (int i = 0; i < 9; i++) light[channel, i] = ambient[channel * 9 + i];
            return true;
        }

        // The light the probe's panorama gives a surface facing each way; false for a set with none.
        bool Room(out SphericalHarmonicsL2 light) => Room(ambient, out light);

        // The probe's light as the active scene's ambient light and default reflection. Each scene's environment has
        // one owner, however many sets light it: the first to light it keeps what stood there (each set joining it
        // takes that), one set's light shows (Shows: the same one whatever order they bind, release or are saved in),
        // and the last to leave gives the scene its own back. A scene no longer active is given its own back first: the
        // set's light follows the scene drawn.
        void LightByProbe(SphericalHarmonicsL2 light)
        {
            Scene active = SceneManager.GetActiveScene();
            if (_lit && _litScene != active) GiveEnvironmentBack();
            List<LightmapSet> lighting = Lighting(active);
            // Bound again in a refresh: what it found first stands; else what the scene's owner found, else what stands.
            if (!_lit) _foundEnvironment = lighting.Count > 0 ? lighting[0]._foundEnvironment : Environment.Read();
            bool reflects = probe != null && probe.customBakedTexture != null;
            float level = Mathf.Max(0f, environmentIntensity);
            _boundEnvironment = new Environment
            {
                mode = AmbientMode.Custom,
                probe = light,
                intensity = level,
                reflectionMode = reflects ? DefaultReflectionMode.Custom : _foundEnvironment.reflectionMode,
                reflection = reflects ? probe.customBakedTexture : _foundEnvironment.reflection,
                reflectionIntensity = reflects ? level : _foundEnvironment.reflectionIntensity,
            };
            _lit = true;
            _litScene = active;
            lighting.Add(this);
            if (Shows(lighting) == this) _boundEnvironment.Write();
        }

        // Its scene's environment given back: where its light shows (nothing else changed it since: another set's, a
        // person's), the light of the set that shows of those still lighting it, else the scene's own as its first
        // set found it. Nothing where it holds none.
        void GiveEnvironmentBack()
        {
            if (!_lit) return;
            _lit = false;
            List<LightmapSet> lighting = Lighting(_litScene);
            Environment back = lighting.Count > 0 ? Shows(lighting)._boundEnvironment : _foundEnvironment;
            Write(_litScene, _boundEnvironment, back);
        }

        // The other sets lighting scene's environment now.
        List<LightmapSet> Lighting(Scene scene)
        {
            var lighting = new List<LightmapSet>();
            foreach (LightmapSet set in FindObjectsByType<LightmapSet>(FindObjectsSortMode.None))
                if (set != this && set._lit && set._litScene == scene) lighting.Add(set);
            return lighting;
        }

        // The set whose light shows of sets lighting one scene: the same one whatever order they came in.
        static LightmapSet Shows(List<LightmapSet> sets)
        {
            LightmapSet shows = sets[0];
            foreach (LightmapSet set in sets)
                if (set.GetInstanceID() < shows.GetInstanceID()) shows = set;
            return shows;
        }

        // Write back into scene's environment where shown stands there. RenderSettings are the active scene's alone, so
        // a scene no longer active is made active for the write and the active one put back; a scene unloaded since
        // took its environment with it.
        static void Write(Scene scene, Environment shown, Environment back)
        {
            Scene active = SceneManager.GetActiveScene();
            if (scene == active)
            {
                if (Environment.Read().Same(shown)) back.Write();
                return;
            }
            if (!scene.IsValid() || !scene.isLoaded) return;
            _switching = true;
            try
            {
                if (!SceneManager.SetActiveScene(scene)) return;
                if (Environment.Read().Same(shown)) back.Write();
                SceneManager.SetActiveScene(active);
            }
            finally
            {
                _switching = false;
            }
        }

        // Every light it switched off, on again.
        void GiveLightsBack()
        {
            foreach (Light light in _switchedOff)
                if (light != null) light.enabled = true;
            _switchedOff.Clear();
        }

        /// <summary>
        /// Give back what <see cref="Bind"/> took: each receiver's index and rect as it
        /// found them (one something else has rebound since is left as it is), each slot
        /// no other bound set reads freed (the free slots at the end dropped) and the
        /// probe off. The mode stays: it is the scene's. Every list is kept.
        /// </summary>
        internal void Release() => Release(false, true);

        /// <summary>
        /// Give back, for a save, every edit of its binding a scene saves (the lights it switched off, the
        /// environment -- the active scene's, whichever scene the set is in -- the renderers' indices and
        /// rects), its probe left as it is: bound again once the scene is saved, so a saved scene holds none of it.
        /// </summary>
        void ISaveSuspended.Suspend()
        {
            if (!_bound) return;
            Release(true, true);
            _suspended = true;
        }

        /// <summary>Bind again after a save took its binding back; nothing for a set not suspended.</summary>
        void ISaveSuspended.Resume()
        {
            if (!_suspended) return;
            _suspended = false;
            Bind();
        }

        void Release(bool keepProbe, bool sceneBack)
        {
            if (!keepProbe) SetProbe(false);
            if (!_bound) return;
            _bound = false;
            if (sceneBack)  // a refresh binds again at once, each rule as its switch then says
            {
                GiveEnvironmentBack();
                GiveLightsBack();
            }
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
            Release(true, false);
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
