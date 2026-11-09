// ----------------------------------------------------------------------------
// The RenderEffects feature's editor part (M3trik.Unitytk.RenderEffects.Editor):
// the import of a delivery's render_effects record. The record names which nodes
// carry which channels (visibility, opacity, highlight) and which materials an
// animated emission; each curve rides the file's animated-value transport (a
// curve proxy per channel and take: a transform '<target>__<channel>' whose
// scale.x is the curve, a child of its node, or of the carrier for a material).
// This rebinds each proxy's curve in each clip onto the component's own field,
// deletes the proxies, adds the components and makes the twins their materials
// need. The record is collected off the carrier's user properties, which reach
// the importers before the clips; the import settings the curves need are asked
// of the carrier
// (IModelImportSettings); nothing here writes the model's import settings.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;
using Payload = RenderEffectsRecord.Shape.Payload;
using Track = RenderEffectsRecord.Shape.Track;
using Entry = RenderEffectsRecord.Shape.Material;

[assembly: ReadsRecord(UnitytkRecords.RenderEffects, typeof(RenderEffectsRecord))]
[assembly: InternalsVisibleTo("M3trik.Unitytk.RenderEffects.Tests")]

namespace M3trik.Unitytk.RenderEffects
{
    /// <summary>
    /// The importer of a delivery's <c>render_effects</c> record (<see cref="UnitytkRecords.RenderEffects"/>,
    /// read through <see cref="RenderEffectsRecord"/> at its one version): each node the
    /// record names gets a <see cref="Fade"/> for an <c>opacity</c> channel and a
    /// <see cref="Highlight"/> (its colours from the record) for a <c>highlight</c> one;
    /// each material entry an <see cref="EmissionTrack"/> on an object of its own under
    /// the carrier. In every clip, each channel's proxy curve is rebound onto the
    /// component's own field (<c>Fade.alpha</c>, <c>Highlight.intensity</c>,
    /// <c>EmissionTrack.weight</c>), and a <c>visibility</c> one onto the node's own
    /// active state (<c>GameObject.m_IsActive</c>, Unity's native binding); the proxies'
    /// other curves and the proxies themselves are dropped, and so are Unity's own
    /// renderer-enabled curves under a node whose effect now owns its presence. The
    /// twins every effect can ask for are made once per material and state, added to
    /// the model with the table its effects reference (<see cref="MaterialTwins"/>).
    /// A file with no record, or one the records provider refuses, keeps Unity's import.
    /// </summary>
    /// <remarks>
    /// <para><b>Visibility, measured</b> (2026-11-07, 6000.3.10f1, a real two-take DCC
    /// export): Unity turns an FBX node's own Visibility into a <c>Renderer.m_Enabled</c>
    /// curve on every renderer under the node (a group's on each child's renderer), so it
    /// hides renderers alone, one curve per renderer. The record's channel hides the node's
    /// subtree; one <c>m_IsActive</c> curve on the node does that, lights, colliders and
    /// children included: over 2,000 renderers it evaluated in 0.015 to 0.05 ms a frame
    /// against 0.05 ms for the 2,000 <c>m_Enabled</c> curves, and 0.89 against 0.64 ms on
    /// a frame it switches. So the proxy is bound to <c>m_IsActive</c> and Unity's
    /// <c>m_Enabled</c> curves under the node are dropped.</para>
    /// <para><b>A node folded into the root, measured</b> (2026-11-07, 6000.3.10f1): a file
    /// whose one top-level node holds the carrier imports with that node as the model's
    /// root, named after the file, its proxies and the carrier its children and its own
    /// Visibility a <c>Renderer.m_Enabled</c> curve on the root. Its record's node name
    /// names no node then, and the root carries the Animator, which a curve on its own
    /// active state would switch off for good; so its channels are left out, its proxies
    /// and their curves deleted, Unity's own curves kept and the import log says so. A
    /// delivery whose carrier sits at the top level beside its nodes (a DCC's export
    /// layout) is never folded.</para>
    /// <para><b>Curves as keyed</b>: with Resample Curves on (Unity's default) a stepped key
    /// imports as a one-frame ramp, so a model carrying the record asks the carrier for
    /// unresampled curves (<see cref="IModelImportSettings.RequestSparseCurves"/>), applied
    /// with one reimport; the first pass is a detection pass and converts nothing.</para>
    /// <para>Prior art: Unity's FBX importer flattens every animated custom property onto
    /// the root under its name (Animated Custom Properties), so objects sharing a channel
    /// collapse into one curve; the transport keeps each on its node, and those flattened
    /// curves, where a project turned the setting on, are dropped where the record names
    /// their channel. The clips are rewritten in <c>OnPostprocessAnimation</c> through
    /// <see cref="AnimationUtility"/>, the documented way to change an imported clip, as
    /// the Animation Audio feature writes its events.</para>
    /// </remarks>
    public class RenderEffectsImporter : AssetPostprocessor
    {
        const string Tag = "RenderEffects";

        /// <summary>Bump to reimport every model when what the import writes changes.</summary>
        const uint VERSION = 2;

        /// <inheritdoc/>
        public override uint GetVersion() => VERSION;

        /// <summary>
        /// After every importer that writes or swaps a model's materials (the material
        /// description rewriters, and any importer at the default order that swaps a
        /// renderer's material in its own <c>OnPostprocessModel</c>): a twin's template is a
        /// copy of the material the import keeps.
        /// </summary>
        public override int GetPostprocessOrder() => ImportOrder.AfterMaterialDescriptions;

        /// <summary>The transport's separator between a proxy's target and its channel.</summary>
        internal const string Separator = "__";

        internal const string Visibility = "visibility", Opacity = "opacity", Highlighted = "highlight", Emission = "emission";

        static IRecordSource Records => Provided.One<IRecordSource>();
        static IModelImportSettings Settings => Provided.One<IModelImportSettings>();

        /// <summary>One model's record and what its import made of it.</summary>
        internal sealed class Delivery
        {
            /// <summary>Its node tracks by node name (the name after any namespace).</summary>
            public readonly Dictionary<string, Track> tracks = new Dictionary<string, Track>(StringComparer.Ordinal);
            /// <summary>Its material entries by material name (after any namespace).</summary>
            public readonly Dictionary<string, Entry> materials = new Dictionary<string, Entry>(StringComparer.Ordinal);
            /// <summary>
            /// The node names several tracks share after their namespaces, each with the names as the
            /// record states them: a tie, joined by neither, and said on the import that assembles.
            /// </summary>
            public readonly Dictionary<string, List<string>> tiedNodes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            /// <summary>The material names several entries share after their namespaces, as <see cref="tiedNodes"/>.</summary>
            public readonly Dictionary<string, List<string>> tiedMaterials = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            /// <summary>This pass is a detection pass: the clips are left to the reimport.</summary>
            public bool detecting;
            /// <summary>Curves rebound onto a field, curves dropped, across the clips.</summary>
            public int rebound, dropped;
        }

        // This model's import, OnPreprocessModel to OnPostprocessModel (one postprocessor per
        // import): the record collected off its carrier, then read once.
        readonly Dictionary<string, string> _collected = new Dictionary<string, string>(StringComparer.Ordinal);
        Delivery _delivery;
        bool _read;

        /// <summary>
        /// The record <paramref name="json"/> (the model at <paramref name="assetPath"/>'s)
        /// read through <paramref name="records"/> (default: the carrier) at
        /// <see cref="RenderEffectsRecord.CurrentVersion"/> alone; null for none, or a
        /// refused one (the provider says why: re-export).
        /// </summary>
        internal static Delivery Read(string json, string assetPath, IRecordSource records = null)
        {
            Payload record = (records ?? Records).Read(UnitytkRecords.RenderEffects, json, RenderEffectsRecord.CurrentVersion,
                                                       Payload.Read, Tag, assetPath);
            return record == null ? null : From(record);
        }

        /// <summary>
        /// A delivery of <paramref name="record"/>: its entries by name, after any namespace. Two
        /// entries of one such name are a tie, kept by neither (a guess would fade one node with
        /// another's curve), as every reader of the delivery leaves a tie unbound.
        /// </summary>
        internal static Delivery From(Payload record)
        {
            var delivery = new Delivery();
            ByLeaf(record.tracks ?? new List<Track>(), t => t.node, delivery.tracks, delivery.tiedNodes);
            ByLeaf(record.materials ?? new List<Entry>(), e => e.material, delivery.materials, delivery.tiedMaterials);
            return delivery;
        }

        // Each entry named, by its name's leaf: one alone is joined, several are a tie (their names kept for the report).
        static void ByLeaf<T>(List<T> entries, Func<T, string> name, Dictionary<string, T> joined,
                              Dictionary<string, List<string>> tied)
        {
            foreach (T entry in entries)
            {
                string named = name(entry);
                if (string.IsNullOrEmpty(named)) continue;
                string leaf = RecordNames.Leaf(named);
                if (tied.TryGetValue(leaf, out List<string> names))
                    names.Add(named);
                else if (joined.TryGetValue(leaf, out T first))
                {
                    joined.Remove(leaf);
                    tied[leaf] = new List<string> { name(first), named };
                }
                else
                    joined[leaf] = entry;
            }
        }

        void OnPreprocessModel()
        {
            _collected.Clear();
            _delivery = null;
            _read = false;
        }

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] names, object[] values)
        {
            if (!Imports.IsDelivery(assetPath)) return;
            if (!Records.TryGetRecord(names, values, UnitytkRecords.RenderEffects, out string json)) return;
            Records.Collect(_collected, assetPath, go, json);
        }

        Delivery Current()
        {
            if (_read) return _delivery;
            _read = true;
            if (_collected.TryGetValue(assetPath, out string json)) _delivery = Read(json, assetPath);
            // Keys as authored: a stepped key resampled is a one-frame ramp (measured). A
            // refused record asks nothing of the model's settings.
            if (_delivery != null) Settings.RequestSparseCurves(assetPath);
            return _delivery;
        }

        void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            Delivery delivery = Current();
            if (delivery == null) return;
            if (Settings.IsDetectionPass(assetPath, assetImporter))
            {
                delivery.detecting = true;
                return;
            }
            Rebind(delivery, root.transform, clip);
        }

        void OnPostprocessModel(GameObject root)
        {
            Delivery delivery = Current();
            _delivery = null;
            _collected.Clear();
            if (delivery == null || delivery.detecting) return;
            Assemble(delivery, root.transform, assetPath, RenderPipelineHosts.Current,
                     (name, made) => context.AddObjectToAsset(name, made));
        }

        // -- the clips ---------------------------------------------------------------------

        /// <summary>
        /// Rewrite <paramref name="clip"/> for <paramref name="delivery"/>'s record: each
        /// proxy curve the record names moved onto its component's field (or the node's
        /// active state), every other curve on a proxy dropped, Unity's renderer-enabled
        /// curves under a node whose opacity or visibility the record names dropped, and the
        /// root-flattened custom-property curves named for a channel the record names dropped.
        /// </summary>
        /// <returns>How many curves it rebound.</returns>
        internal static int Rebind(Delivery delivery, Transform root, AnimationClip clip)
        {
            var proxies = new HashSet<string>(StringComparer.Ordinal);
            var moved = new List<(EditorCurveBinding to, AnimationCurve curve)>();
            var presence = new List<string>();
            Dictionary<string, Material> worn = Worn(delivery, root);
            HashSet<string> nodes = NodeNames(root);
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type != typeof(Transform)) continue;
                if (Folded(delivery, nodes, b.path, out _))
                {
                    proxies.Add(b.path);  // its node is the model's root: its curves go with it (Assemble says so)
                    continue;
                }
                if (!Proxy(delivery, b.path, out string node, out string channel)) continue;
                proxies.Add(b.path);
                if (b.propertyName != "m_LocalScale.x") continue;
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, b);
                if (curve == null || curve.length == 0) continue;
                string at = Parent(b.path);
                switch (channel)
                {
                    case Opacity:
                        moved.Add((EditorCurveBinding.FloatCurve(at, typeof(Fade), nameof(Fade.alpha)), curve));
                        break;
                    case Highlighted:
                        moved.Add((EditorCurveBinding.FloatCurve(at, typeof(Highlight), nameof(Highlight.intensity)), curve));
                        break;
                    case Visibility:
                        moved.Add((EditorCurveBinding.FloatCurve(at, typeof(GameObject), "m_IsActive"), Stepped(curve)));
                        break;
                    case Emission:
                        if (!worn.ContainsKey(node)) break;  // no renderer wears it: no track to bind (Assemble says so)
                        moved.Add((EditorCurveBinding.FloatCurve(TrackPath(at, node), typeof(EmissionTrack),
                                                                 nameof(EmissionTrack.weight)), curve));
                        break;
                }
            }
            // The nodes whose effect owns their presence: Unity's renderer curves under them go.
            foreach (var (node, _, track) in Nodes(delivery, root))
                if (Names(track, Opacity) || Names(track, Visibility))
                    presence.Add(AnimationUtility.CalculateTransformPath(node, root));
            int dropped = 0;
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
            {
                bool drop = proxies.Contains(b.path)
                            || (typeof(Renderer).IsAssignableFrom(b.type) && b.propertyName == "m_Enabled" && Under(b.path, presence))
                            || (b.path.Length == 0 && b.type == typeof(Animator) && Channel(delivery, b.propertyName));
                if (!drop) continue;
                AnimationUtility.SetEditorCurve(clip, b, null);
                dropped++;
            }
            foreach (var (to, curve) in moved) AnimationUtility.SetEditorCurve(clip, to, curve);
            delivery.rebound += moved.Count;
            delivery.dropped += dropped;
            return moved.Count;
        }

        /// <summary>
        /// Whether the transform at <paramref name="path"/> is a proxy the record names: its
        /// name <c>&lt;target&gt;__&lt;channel&gt;</c>, its target a node whose track names the
        /// channel (the proxy its child) or a material whose entry does.
        /// </summary>
        internal static bool Proxy(Delivery delivery, string path, out string target, out string channel)
        {
            if (!Split(path, out target, out channel)) return false;
            if (channel == Emission) return delivery.materials.TryGetValue(target, out Entry entry) && Names(entry, Emission);
            return delivery.tracks.TryGetValue(target, out Track track) && Names(track, channel)
                   && RecordNames.Leaf(LeafOfPath(Parent(path))) == target;
        }

        /// <summary>
        /// Whether the transform at <paramref name="path"/> is a proxy of a node Unity folded
        /// into the model's root (the file's one top-level node; measured, the class's
        /// remarks): a child of the root named <c>&lt;node&gt;__&lt;channel&gt;</c>, its node one
        /// whose track names the channel and which no transform among <paramref name="nodes"/>
        /// (the names under the root, <see cref="NodeNames"/>) is. Its effect has no node of
        /// its own to go on, so it is left out and the proxy deleted.
        /// </summary>
        internal static bool Folded(Delivery delivery, HashSet<string> nodes, string path, out string node)
        {
            node = null;
            if (Parent(path).Length != 0 || !Split(path, out string target, out string channel) || channel == Emission)
                return false;
            if (!delivery.tracks.TryGetValue(target, out Track track) || !Names(track, channel) || nodes.Contains(target))
                return false;
            node = target;
            return true;
        }

        /// <summary>
        /// Which transform under <paramref name="root"/> each node track of
        /// <paramref name="delivery"/> goes on, by the rule every reader of the record
        /// follows: the parent of each of its curve proxies (<c>&lt;node&gt;__&lt;channel&gt;</c>)
        /// wherever the file carries one -- the node its curve was written under, whatever
        /// other node shares its name -- and, for a track with no proxy anywhere, the node
        /// its name denotes (<see cref="RecordNames"/>: the exact name, then the one node of
        /// its leaf). A tie is never guessed: its candidates go in <paramref name="tied"/>, by
        /// the track's key. A track whose proxy sits on a node folded into the root
        /// (<see cref="Folded"/>) goes on none.
        /// </summary>
        /// <returns>Each (node, track key, track), a node once per track.</returns>
        internal static List<(Transform node, string key, Track track)> Nodes(Delivery delivery, Transform root,
                                                                              Dictionary<string, string[]> tied = null)
        {
            var joined = new List<(Transform node, string key, Track track)>();
            var proxied = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<(Transform, string)>();
            var candidates = new List<Transform>();
            HashSet<string> names = NodeNames(root);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string path = AnimationUtility.CalculateTransformPath(t, root);
                if (Folded(delivery, names, path, out string folded))
                {
                    proxied.Add(folded);
                    continue;
                }
                if (Proxy(delivery, path, out string target, out string channel))
                {
                    // A material's proxy hangs under the carrier: its node is no track's.
                    if (channel == Emission) continue;
                    proxied.Add(target);
                    if (seen.Add((t.parent, target))) joined.Add((t.parent, target, delivery.tracks[target]));
                    continue;
                }
                candidates.Add(t);
            }
            var keys = new List<string>();
            foreach (string key in delivery.tracks.Keys)
                if (!proxied.Contains(key)) keys.Add(key);
            if (keys.Count == 0) return joined;
            RecordNames.Match<Transform>[] matches = RecordNames.Resolve(
                candidates, keys.ConvertAll(k => new RecordNames.Entry(delivery.tracks[k].node)), t => t.name, t => t.parent, root);
            for (int i = 0; i < keys.Count; i++)
            {
                if (matches[i].Outcome == RecordNames.Outcome.Bound) joined.Add((matches[i].Item, keys[i], delivery.tracks[keys[i]]));
                else if (matches[i].Outcome == RecordNames.Outcome.Ambiguous && tied != null) tied[keys[i]] = matches[i].Candidates;
            }
            return joined;
        }

        /// <summary>The name of every transform under <paramref name="root"/> (not the root's), without its namespace.</summary>
        internal static HashSet<string> NodeNames(Transform root)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (t != root) names.Add(RecordNames.Leaf(t.name));
            return names;
        }

        // A proxy's name, '<target>__<channel>', split; false for another name.
        static bool Split(string path, out string target, out string channel)
        {
            string leaf = RecordNames.Leaf(LeafOfPath(path));
            int split = leaf.LastIndexOf(Separator, StringComparison.Ordinal);
            target = split > 0 ? leaf.Substring(0, split) : null;
            channel = split > 0 ? leaf.Substring(split + Separator.Length) : null;
            return target != null && channel.Length > 0;
        }

        // A visibility as a switch: every key held until the next (on at 1, off at 0).
        static AnimationCurve Stepped(AnimationCurve curve)
        {
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value = keys[i].value >= 0.5f ? 1f : 0f;
                keys[i].inTangent = keys[i].outTangent = float.PositiveInfinity;
            }
            return new AnimationCurve(keys);
        }

        static bool Under(string path, List<string> nodes)
        {
            foreach (string node in nodes)
                if (path == node || path.StartsWith(node + "/", StringComparison.Ordinal)) return true;
            return false;
        }

        static bool Channel(Delivery delivery, string name)
        {
            foreach (Track track in delivery.tracks.Values)
                if (Names(track, name)) return true;
            foreach (Entry entry in delivery.materials.Values)
                if (Names(entry, name)) return true;
            return false;
        }

        static bool Names(Track track, string channel) => track.channels != null && track.channels.Contains(channel);

        static bool Names(Entry entry, string channel) => entry.channels != null && entry.channels.Contains(channel);

        /// <summary>
        /// The material a renderer under <paramref name="root"/> wears for each of
        /// <paramref name="delivery"/>'s material entries, by the entry's key (the leaf its
        /// proxy is named by), joined as the materials record joins
        /// (<see cref="MaterialRecord.Find{T}"/>: Unity's import renames 'ns:Mat' 'ns_Mat');
        /// an entry no renderer wears is absent.
        /// </summary>
        internal static Dictionary<string, Material> Worn(Delivery delivery, Transform root)
        {
            var byName = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                    if (material != null && !byName.ContainsKey(material.name)) byName[material.name] = material;
            var worn = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var pair in delivery.materials)
            {
                Material material = MaterialRecord.Find(byName, pair.Value.material);
                if (material != null) worn[pair.Key] = material;
            }
            return worn;
        }

        /// <summary>The path of the object a material's track lives on: beside its proxy, named for the material.</summary>
        internal static string TrackPath(string carrier, string material) => carrier.Length == 0 ? material : carrier + "/" + material;

        // -- the model ---------------------------------------------------------------------

        /// <summary>
        /// Give the model under <paramref name="root"/> what <paramref name="delivery"/>'s
        /// record asks: its components, its twins (each handed to <paramref name="add"/>, the
        /// import's sub-asset, with the table that lists them; none where
        /// <paramref name="host"/> is null: a pipeline no door serves), and no proxies.
        /// </summary>
        /// <returns>How many twins it made.</returns>
        internal static int Assemble(Delivery delivery, Transform root, string assetPath, IRenderPipelineHost host,
                                     Action<string, UnityEngine.Object> add)
        {
            var proxies = new List<Transform>();
            var effects = new List<RenderEffect>();
            var found = new HashSet<string>(StringComparer.Ordinal);
            var folded = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> nodes = NodeNames(root);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == root) continue;
                string path = AnimationUtility.CalculateTransformPath(t, root);
                if (Folded(delivery, nodes, path, out string rootNode))
                {
                    folded.Add(rootNode);
                    proxies.Add(t);
                    continue;
                }
                if (Proxy(delivery, path, out _, out _)) proxies.Add(t);
            }
            var tied = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var (node, key, track) in Nodes(delivery, root, tied))
            {
                found.Add(key);
                // A node two tracks reach (one by its proxy, one by name) holds each component once.
                if (Names(track, Opacity))
                {
                    var fade = Ensure<Fade>(node.gameObject);
                    if (!effects.Contains(fade)) effects.Add(fade);
                }
                if (Names(track, Highlighted))
                {
                    var highlight = Ensure<Highlight>(node.gameObject);
                    highlight.color = MaterialColor.Encode(ColorOf(track.highlight_color, Color.white));
                    highlight.colorDim = MaterialColor.Encode(ColorOf(track.highlight_color_dim, Color.black));
                    if (!effects.Contains(highlight)) effects.Add(highlight);
                }
            }
            foreach (string node in delivery.tracks.Keys)
            {
                if (found.Contains(node)) continue;
                if (tied.TryGetValue(node, out string[] candidates))
                    RecordNamesLog.Unbound(assetPath, $"its render_effects record's node '{delivery.tracks[node].node}' (no curve " +
                                                      "proxy says which)", RecordNames.Outcome.Ambiguous, candidates,
                                           "its effects are left out");
                else if (folded.Contains(node))
                    ImportLog.Warning(assetPath, $"its render_effects record names the node '{node}', the file's one top-level " +
                                                 "node, which Unity's import folds into the model's root (named after the file): " +
                                                 $"its {string.Join(", ", delivery.tracks[node].channels)} are left out and their " +
                                                 "curve proxies deleted, Unity's own curves kept. Re-export with the data_export " +
                                                 "carrier at the top level beside the node, so the node keeps one of its own.");
                else
                    ImportLog.Warning(assetPath, $"its render_effects record names the node '{node}', which the model has " +
                                                 "none of: its effects are left out (re-export the file).");
            }

            foreach (var tie in delivery.tiedNodes)
                ImportLog.Warning(assetPath, $"its render_effects record names several nodes '{tie.Key}' after their namespaces " +
                                             $"({string.Join(", ", tie.Value)}), which each node of that name would join alike: " +
                                             "their effects are left out. Give them names that tell them apart, and re-export.");
            foreach (var tie in delivery.tiedMaterials)
                ImportLog.Warning(assetPath, $"its render_effects record animates several materials '{tie.Key}' after their " +
                                             $"namespaces ({string.Join(", ", tie.Value)}), which each material of that name " +
                                             "would join alike: their emission is left out. Give them names that tell them " +
                                             "apart, and re-export.");

            Dictionary<string, Material> worn = Worn(delivery, root);
            foreach (var pair in delivery.materials)
            {
                if (!worn.TryGetValue(pair.Key, out Material material))
                {
                    ImportLog.Warning(assetPath, $"its render_effects record animates the emission of '{pair.Key}', which no " +
                                                 "renderer of the model wears: it is left out (re-export the file).");
                    continue;
                }
                Transform proxy = proxies.Find(p => RecordNames.Leaf(p.name) == pair.Key + Separator + Emission);
                Transform carrier = proxy != null && proxy.parent != null ? proxy.parent : root;
                var holder = new GameObject(pair.Key);
                holder.transform.SetParent(carrier, false);
                var track = holder.AddComponent<EmissionTrack>();
                Fill(track, material, pair.Value, host);
                effects.Add(track);
            }
            foreach (Transform proxy in proxies)
                if (proxy != null) UnityEngine.Object.DestroyImmediate(proxy.gameObject);

            int made = Twins(root, effects, assetPath, host, add);
            if (effects.Count > 0)
                ImportLog.Info(assetPath, $"render effects: {effects.Count} component(s), {made} twin(s), {delivery.rebound} " +
                                          $"curve(s) rebound and {delivery.dropped} dropped across its clips, {proxies.Count} " +
                                          "curve proxy node(s) deleted.");
            return made;
        }

        /// <summary>
        /// A track's colour, strength and resting weight from its record entry: the
        /// record's colour (linear, encoded as the colour field stores it) or the material's
        /// own, its strength or 1, and the weight at which it shows the material as
        /// delivered, so the model at rest wears its shared original.
        /// </summary>
        internal static void Fill(EmissionTrack track, Material material, Entry entry, IRenderPipelineHost host)
        {
            track.material = material;
            Color own = MaterialWriter.EmissionOf(material, host);
            track.color = entry.emission_color != null ? MaterialColor.Encode(ColorOf(entry.emission_color, Color.white))
                                                       : MaterialColor.Encode(own);
            track.strength = entry.emission_strength.HasValue ? (float)entry.emission_strength.Value : 1f;
            // The weight at which colour x strength x weight is the delivered emission.
            float full = (MaterialColor.Decode(track.color) * track.strength).maxColorComponent;
            track.weight = full > 0f ? Mathf.Clamp01(own.maxColorComponent / full) : 0f;
        }

        /// <summary>
        /// Make the twins <paramref name="effects"/> under <paramref name="root"/> can ask
        /// for (<see cref="TwinPlan"/>), each handed to <paramref name="add"/> with a table
        /// listing them, and give every effect the table. Returns how many it made.
        /// </summary>
        internal static int Twins(Transform root, List<RenderEffect> effects, string assetPath, IRenderPipelineHost host,
                                  Action<string, UnityEngine.Object> add)
        {
            if (effects.Count == 0) return 0;
            if (host == null)
            {
                ImportLog.Warning(assetPath, $"the {RenderPipelineHosts.NameOf(null)} render pipeline has no unitytk host (no " +
                                             "door serves it), so no twins are made: its fades, highlights and emission tracks " +
                                             "show only as hidden at 0.");
                return 0;
            }
            var table = ScriptableObject.CreateInstance<MaterialTwins>();
            table.name = "Render Effects Twins";
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var losses = new List<string>();
            foreach (var (material, state) in TwinPlan.Needed(root))
            {
                Material twin = MaterialWriter.Twin(material, state, host, losses);
                if (twin == null) continue;
                string id = "RenderEffects/" + twin.name;
                for (int n = 2; !ids.Add(id); n++) id = "RenderEffects/" + twin.name + " " + n;
                add(id, twin);
                table.twins.Add(new MaterialTwins.Twin { original = material, state = state, template = twin });
            }
            add("RenderEffects/twins", table);
            foreach (RenderEffect effect in effects) effect.twins = table;
            foreach (string loss in new HashSet<string>(losses)) ImportLog.Warning(assetPath, loss + ".");
            return table.twins.Count;
        }

        static T Ensure<T>(GameObject go) where T : Component
        {
            T component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }

        static Color ColorOf(double[] rgb, Color absent)
            => rgb == null || rgb.Length < 3 ? absent : new Color((float)rgb[0], (float)rgb[1], (float)rgb[2], 1f);

        static string LeafOfPath(string path) => path.Substring(path.LastIndexOf('/') + 1);

        static string Parent(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash < 0 ? "" : path.Substring(0, slash);
        }
    }
}
