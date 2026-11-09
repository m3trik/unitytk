using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// The one writer of a root's renderers: every material slot it covers, what drives
    /// it (its group: the nearest Fade and Highlight above its renderer; its material's
    /// track), and the twin instances it wears -- one per group, material, track and
    /// state, so every renderer of a group wearing one material shares one. Each write
    /// reads the values, swaps a slot only when its state flips (the shared original at
    /// rest), hides a renderer at alpha 0, and writes an instance's values only when they
    /// move: a frame with nothing moving touches no renderer and allocates nothing.
    /// </summary>
    /// <remarks>
    /// The writers holding twins now are listed (<c>_holding</c>), so a slot scanned
    /// while it still wears another writer's twin -- its renderer moved under this root
    /// before its first root let it go, which of the two rebuilds first being Unity's
    /// choice -- records that twin's original, never the twin. The list holds plain
    /// objects, each only while it holds twins: every writer is released before a domain
    /// reload, play mode's by OnDisable and the editor's by its keeper.
    /// </remarks>
    internal sealed class EffectWriter
    {
        sealed class Group
        {
            public Fade fade;
            public Fade[] chain;  // the fade and every Fade above it, up to the root: its alpha's factors
            public Highlight highlight;
            public float alpha = 1f;
            public bool hidden, fading, glowing, dimLit;
            public Color glow = Color.black;
            public int signature = -1;
        }

        struct Slot
        {
            public int renderer, index, group, track;
            public Material original, worn;
            public bool mapped;
            public Color rest;
            public int state;
        }

        sealed class Instance
        {
            public Material twin, original;
            public int group, track;
            public TwinState state;
            public bool mapped;
            public Color rest;
            public float alpha = -1f;
            public Color emission = new Color(-1f, -1f, -1f, -1f);
        }

        readonly List<Renderer> _renderers = new List<Renderer>();
        readonly List<int> _rendererGroup = new List<int>();
        readonly List<int> _firstSlot = new List<int>();
        readonly List<int> _slotCount = new List<int>();
        readonly List<bool> _hid = new List<bool>();
        readonly List<bool> _changed = new List<bool>();
        readonly List<Slot> _slots = new List<Slot>();
        readonly List<Group> _groups = new List<Group>();
        readonly List<EmissionTrack> _tracks = new List<EmissionTrack>();
        readonly List<Color> _trackRest = new List<Color>();
        readonly List<bool> _trackActive = new List<bool>();
        readonly List<Color> _trackEmission = new List<Color>();
        readonly List<MaterialTwins> _tables = new List<MaterialTwins>();
        readonly Dictionary<(Material, int, int, int), Instance> _byKey = new Dictionary<(Material, int, int, int), Instance>();
        readonly List<Instance> _instances = new List<Instance>();
        readonly List<Material> _scratch = new List<Material>();
        IRenderPipelineHost _host;
        bool _fresh = true;
        static readonly HashSet<string> _warned = new HashSet<string>();
        static readonly List<EffectWriter> _holding = new List<EffectWriter>();

        // Unity's: with domain reload off as play mode starts (Enter Play Mode Options), a static
        // outlives edit mode; every edit-mode writer was given back as edit mode ended, so whatever
        // is left is stale, and each warning is said again once a session.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _holding.Clear();
            _warned.Clear();
        }

        /// <summary>How many twin instances it holds: one per group, material, track and state met.</summary>
        internal int InstanceCount => _instances.Count;

        // -- what it covers ---------------------------------------------------------------

        /// <summary>
        /// Cover every renderer under <paramref name="root"/>: each in the group of the
        /// nearest enabled Fade and Highlight at or above it (up to the root), each slot tied
        /// to the track in <paramref name="tracks"/> that animates its material in a scope
        /// holding its renderer.
        /// </summary>
        internal void ScanSubtree(SubtreeEffect root, List<EmissionTrack> tracks)
        {
            Begin();
            Table(root.twins);
            foreach (SubtreeEffect nested in root.GetComponentsInChildren<SubtreeEffect>(true)) Table(nested.twins);
            var scopes = new List<Transform>();
            foreach (EmissionTrack track in tracks)
            {
                AddTrack(track);
                scopes.Add(RenderEffect.ScopeOf(track));
                Table(track.twins);
            }
            var groups = new Dictionary<(Fade, Highlight), int>();
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Fade fade = null;
                Highlight highlight = null;
                for (Transform t = renderer.transform; t != null; t = t.parent)
                {
                    if (fade == null)
                    {
                        Fade f = t.GetComponent<Fade>();
                        if (f != null && f.Live) fade = f;
                    }
                    if (highlight == null)
                    {
                        Highlight h = t.GetComponent<Highlight>();
                        if (h != null && h.Live) highlight = h;
                    }
                    if (t == root.transform) break;
                }
                if (!groups.TryGetValue((fade, highlight), out int group))
                {
                    groups[(fade, highlight)] = group = _groups.Count;
                    _groups.Add(new Group { fade = fade, chain = Chain(fade, root.transform), highlight = highlight });
                }
                Transform at = renderer.transform;
                AddRenderer(renderer, group, material =>
                {
                    for (int i = 0; i < _tracks.Count; i++)
                        if (_tracks[i].material == material && at.IsChildOf(scopes[i])) return i;
                    return -1;
                }, null);
            }
        }

        // The enabled Fades from fade up to the root: what its alpha multiplies.
        static Fade[] Chain(Fade fade, Transform root)
        {
            if (fade == null) return new Fade[0];
            var chain = new List<Fade>();
            for (Transform t = fade.transform; t != null; t = t.parent)
            {
                Fade f = t.GetComponent<Fade>();
                if (f != null && f.Live) chain.Add(f);
                if (t == root) break;
            }
            return chain.ToArray();
        }

        /// <summary>
        /// Cover every slot under <paramref name="scope"/> wearing <paramref name="track"/>'s
        /// material whose renderer no enabled Fade or Highlight covers (those take the
        /// track into their own twins).
        /// </summary>
        internal void ScanTrack(EmissionTrack track, Transform scope)
        {
            Begin();
            Table(track.twins);
            int index = AddTrack(track);
            _groups.Add(new Group());
            foreach (Renderer renderer in scope.GetComponentsInChildren<Renderer>(true))
            {
                if (SubtreeEffect.Covered(renderer.transform)) continue;
                AddRenderer(renderer, 0, material => index, track.material);
            }
        }

        /// <summary>Whether it holds a renderer under <paramref name="node"/>.</summary>
        internal bool HoldsUnder(Transform node)
        {
            foreach (Renderer renderer in _renderers)
                if (renderer != null && renderer.transform.IsChildOf(node)) return true;
            return false;
        }

        void Begin()
        {
            Release();
            _host = RenderPipelineHosts.Current;
        }

        void Table(MaterialTwins table)
        {
            if (table != null && !_tables.Contains(table)) _tables.Add(table);
        }

        int AddTrack(EmissionTrack track)
        {
            _tracks.Add(track);
            _trackRest.Add(MaterialWriter.EmissionOf(track.material, _host));
            _trackActive.Add(false);
            _trackEmission.Add(Color.black);
            return _tracks.Count - 1;
        }

        void AddRenderer(Renderer renderer, int group, System.Func<Material, int> trackOf, Material only)
        {
            Material[] materials = renderer.sharedMaterials;
            int index = -1;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = Authored(materials[i]);
                if (material == null || (only != null && material != only)) continue;
                if (index < 0)
                {
                    index = _renderers.Count;
                    _renderers.Add(renderer);
                    _rendererGroup.Add(group);
                    _hid.Add(false);
                    _changed.Add(false);
                    _firstSlot.Add(_slots.Count);
                    _slotCount.Add(0);
                }
                _slotCount[index]++;
                _slots.Add(new Slot
                {
                    renderer = index, index = i, group = group, track = trackOf(material), original = material,
                    mapped = MaterialWriter.EmissionMapped(material, _host), rest = MaterialWriter.EmissionOf(material, _host),
                });
            }
        }

        // The material as authored behind worn: the original of the twin another writer has on it, else worn.
        static Material Authored(Material worn)
        {
            if (worn == null) return null;
            foreach (EffectWriter writer in _holding)
                foreach (Instance instance in writer._instances)
                    if (instance.twin == worn) return instance.original;
            return worn;
        }

        // -- a write ----------------------------------------------------------------------

        /// <summary>Write every value again on the next write (an inspector edit).</summary>
        internal void Invalidate()
        {
            _fresh = true;
            foreach (Instance instance in _instances) instance.alpha = -1f;
        }

        /// <summary>Put the current values on every renderer it covers; whether it changed a renderer or a twin.</summary>
        internal bool Write()
        {
            bool swap = _fresh;
            foreach (Group g in _groups)
            {
                g.alpha = 1f;
                if (g.chain != null)
                    foreach (Fade f in g.chain)
                        if (f != null && f.Live) g.alpha *= Mathf.Clamp01(f.alpha);
                bool lit = g.highlight != null && g.highlight.Live;
                g.hidden = g.alpha <= 0f;
                g.fading = !g.hidden && g.alpha < 1f;
                g.glowing = lit && g.highlight.On;
                g.dimLit = lit && g.highlight.DimLit;
                g.glow = lit ? g.highlight.Glow : Color.black;
                int signature = (g.hidden ? 1 : 0) | (g.fading ? 2 : 0) | (g.glowing ? 4 : 0) | (g.dimLit ? 8 : 0);
                if (signature != g.signature) swap = true;
                g.signature = signature;
            }
            for (int i = 0; i < _tracks.Count; i++)
            {
                EmissionTrack track = _tracks[i];
                Color emission = track != null && track.Live ? track.Emission : _trackRest[i];
                bool active = !Same(emission, _trackRest[i]);
                if (active != _trackActive[i]) swap = true;
                _trackActive[i] = active;
                _trackEmission[i] = emission;
            }
            if (swap) Swap();
            _fresh = false;
            bool wrote = swap;
            foreach (Instance instance in _instances) wrote |= Values(instance);
            return wrote;
        }

        // Each slot onto the material its state asks, each renderer set once; each hidden at 0.
        void Swap()
        {
            for (int s = 0; s < _slots.Count; s++)
            {
                Slot slot = _slots[s];
                Group g = _groups[slot.group];
                int state = 0;
                if (!g.hidden)
                {
                    if (g.fading) state |= (int)TwinState.Blended;
                    if (g.glowing || (g.dimLit && !slot.mapped)) state |= (int)TwinState.Glowing;
                    else if (slot.track >= 0 && _trackActive[slot.track]) state |= (int)TwinState.Emitting;
                }
                if (state == slot.state) continue;
                slot.state = state;
                slot.worn = state == 0 ? null : InstanceFor(slot).twin;
                _slots[s] = slot;
                _changed[slot.renderer] = true;
            }
            for (int r = 0; r < _renderers.Count; r++)
            {
                Renderer renderer = _renderers[r];
                if (renderer == null) continue;
                bool hide = _groups[_rendererGroup[r]].hidden;
                if (hide != _hid[r])
                {
                    renderer.forceRenderingOff = hide;
                    _hid[r] = hide;
                }
                if (!_changed[r]) continue;
                _changed[r] = false;
                renderer.GetSharedMaterials(_scratch);
                for (int s = _firstSlot[r], end = s + _slotCount[r]; s < end; s++)
                {
                    Slot slot = _slots[s];
                    if (slot.index < _scratch.Count) _scratch[slot.index] = slot.worn != null ? slot.worn : slot.original;
                }
                renderer.SetSharedMaterials(_scratch);
            }
        }

        bool Values(Instance instance)
        {
            if (instance.twin == null) return false;
            Group g = _groups[instance.group];
            float alpha = (instance.state & TwinState.Blended) != 0 ? g.alpha : 1f;
            Color? emission = null;
            bool trackOn = instance.track >= 0 && _trackActive[instance.track];
            if ((instance.state & TwinState.Glowing) != 0)
            {
                // Over a map the glow alone (the map set aside); else added to the uniform emission.
                Color own = instance.mapped ? Color.black : trackOn ? _trackEmission[instance.track] : instance.rest;
                Color sum = own + g.glow;
                sum.a = 1f;
                emission = sum;
            }
            else if ((instance.state & TwinState.Emitting) != 0)
            {
                emission = trackOn ? _trackEmission[instance.track] : instance.rest;
            }
            Color shown = emission ?? Color.clear;
            if (Mathf.Approximately(alpha, instance.alpha) && Same(shown, instance.emission)) return false;
            instance.alpha = alpha;
            instance.emission = shown;
            MaterialWriter.SetTwinValues(instance.twin, instance.original, _host, alpha, emission);
            return true;
        }

        Instance InstanceFor(Slot slot)
        {
            var key = (slot.original, slot.group, slot.track, slot.state);
            if (_byKey.TryGetValue(key, out Instance instance)) return instance;
            var state = (TwinState)slot.state;
            instance = new Instance
            {
                original = slot.original, group = slot.group, track = slot.track, state = state,
                mapped = slot.mapped, rest = slot.rest,
            };
            Material template = null;
            foreach (MaterialTwins table in _tables)
                if ((template = table.Template(slot.original, state)) != null) break;
            if (template != null)
            {
                instance.twin = new Material(template) { name = template.name };
            }
            else
            {
                instance.twin = MaterialWriter.Twin(slot.original, state, _host);
                Warn(instance.twin == null
                    ? $"no unitytk render-pipeline host serves the {RenderPipelineHosts.NameOf(_host)} pipeline, so " +
                      "fades, highlights and emission tracks show only as hidden at 0"
                    : $"'{slot.original.name}' has no prepared twin for {state}, so one was made at runtime: a player " +
                      "build may lack its shader variant -- run Tools > unitytk > Render Effects > Prepare Materials");
            }
            if (instance.twin != null) instance.twin.hideFlags = HideFlags.DontSave;
            _byKey[key] = instance;
            if (_instances.Count == 0) _holding.Add(this);
            _instances.Add(instance);
            return instance;
        }

        /// <summary>Say <paramref name="message"/> once a session (a warning for each thing an effect cannot show).</summary>
        internal static void Warn(string message)
        {
            if (_warned.Add(message)) Debug.LogWarning("Render Effects: " + message + ".");
        }

        static bool Same(Color a, Color b)
            => Mathf.Abs(a.r - b.r) < 1e-4f && Mathf.Abs(a.g - b.g) < 1e-4f && Mathf.Abs(a.b - b.b) < 1e-4f
               && Mathf.Abs(a.a - b.a) < 1e-4f;

        // -- giving back ------------------------------------------------------------------

        /// <summary>
        /// Every slot it changed back on its shared original (one something else changed
        /// since is left as it is), every renderer it hid shown, and its instances freed.
        /// </summary>
        internal void Release()
        {
            for (int r = 0; r < _renderers.Count; r++)
            {
                Renderer renderer = _renderers[r];
                if (renderer == null) continue;
                if (_hid[r]) renderer.forceRenderingOff = false;
                renderer.GetSharedMaterials(_scratch);
                bool any = false;
                for (int s = _firstSlot[r], end = s + _slotCount[r]; s < end; s++)
                {
                    Slot slot = _slots[s];
                    if (slot.worn == null || slot.index >= _scratch.Count) continue;
                    if (_scratch[slot.index] != slot.worn) continue;
                    _scratch[slot.index] = slot.original;
                    any = true;
                }
                if (any) renderer.SetSharedMaterials(_scratch);
            }
            foreach (Instance instance in _instances)
            {
                if (instance.twin == null) continue;
                if (Application.isPlaying) Object.Destroy(instance.twin);
                else Object.DestroyImmediate(instance.twin);
            }
            _renderers.Clear();
            _rendererGroup.Clear();
            _firstSlot.Clear();
            _slotCount.Clear();
            _hid.Clear();
            _changed.Clear();
            _slots.Clear();
            _groups.Clear();
            _tracks.Clear();
            _trackRest.Clear();
            _trackActive.Clear();
            _trackEmission.Clear();
            _tables.Clear();
            _byKey.Clear();
            _instances.Clear();
            _holding.Remove(this);
            _fresh = true;
        }
    }
}
