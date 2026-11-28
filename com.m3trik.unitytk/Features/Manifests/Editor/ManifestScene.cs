using System;
using System.Collections.Generic;
using System.Linq;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// The delivery a plan is built into: the model instance in the scene, its nodes found by
    /// the plan's paths, and the clips its model carries, found by the plan's take names. It is
    /// also what a build's builders share: the plan being built, each kit's settings and what a
    /// kit keeps for the run, and where a builder says what it holds (<see cref="Hold"/>).
    /// </summary>
    /// <remarks>
    /// A plan names a node by its path in the delivery (<c>A|B|C</c>, from the file's top
    /// level down); it is found under <see cref="Root"/> by the rule every reader of a
    /// delivery resolves names by (<see cref="RecordNames"/>: the name, then where it sits; a
    /// tie never guessed), so the instance may be named or parented anyhow. A delivery of
    /// several files gives the others as <see cref="Extras"/>: a path not under the root is
    /// found under each of them in turn.
    /// </remarks>
    public sealed class ManifestScene
    {
        readonly List<Transform> _nodes = new List<Transform>();
        readonly Dictionary<string, RecordNames.Match<Transform>> _found =
            new Dictionary<string, RecordNames.Match<Transform>>(StringComparer.Ordinal);
        readonly Dictionary<string, AnimationClip> _clips =
            new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
        readonly List<GameObject> _extras = new List<GameObject>();
        readonly List<List<Transform>> _extraNodes = new List<List<Transform>>();
        readonly Dictionary<Type, object> _state = new Dictionary<Type, object>();
        readonly Dictionary<Type, ScriptableObject> _settings = new Dictionary<Type, ScriptableObject>();

        /// <summary>The scene of <paramref name="root"/>, with <paramref name="clips"/>.</summary>
        /// <param name="root">The delivery's instance in the scene.</param>
        /// <param name="clips">The takes it plays; null: the clips its model asset carries.</param>
        /// <exception cref="ArgumentNullException"><paramref name="root"/> is null.</exception>
        public ManifestScene(GameObject root, IEnumerable<AnimationClip> clips = null)
        {
            if (root == null) throw new ArgumentNullException(nameof(root), "name the delivery's instance in the scene");
            Root = root;
            foreach (Transform node in root.GetComponentsInChildren<Transform>(true))
                if (node != root.transform) _nodes.Add(node);
            foreach (AnimationClip clip in clips ?? ModelClips(root))
                if (clip != null && !_clips.ContainsKey(clip.name)) _clips[clip.name] = clip;
        }

        /// <summary>
        /// The scene of <paramref name="root"/> and the <paramref name="extras"/> whose nodes join it (an
        /// older delivery's arrows, viewpoints or panel anchors in files of their own), with
        /// <paramref name="clips"/>: a path is found under the root first, then under each extra in order.
        /// </summary>
        /// <param name="root">The delivery's instance in the scene: the one whose takes play.</param>
        /// <param name="extras">Instances whose nodes the plan names but whose takes it plays none of.</param>
        /// <param name="clips">The takes the root plays; null: the clips its model asset carries.</param>
        /// <exception cref="ArgumentNullException"><paramref name="root"/>, or an extra, is null.</exception>
        public ManifestScene(GameObject root, IEnumerable<GameObject> extras, IEnumerable<AnimationClip> clips = null)
            : this(root, clips)
        {
            foreach (GameObject extra in extras ?? Array.Empty<GameObject>())
            {
                if (extra == null) throw new ArgumentNullException(nameof(extras), "name each extra file's instance in the scene");
                var nodes = new List<Transform>();
                foreach (Transform node in extra.GetComponentsInChildren<Transform>(true))
                    if (node != extra.transform) nodes.Add(node);
                _extras.Add(extra);
                _extraNodes.Add(nodes);
            }
        }

        /// <summary>The delivery's instance in the scene.</summary>
        public GameObject Root { get; }

        /// <summary>The delivery's instances: its root, then its extras.</summary>
        public IEnumerable<GameObject> Roots
        {
            get
            {
                yield return Root;
                foreach (GameObject extra in _extras) yield return extra;
            }
        }

        /// <summary>The instances whose nodes join the delivery's, searched after it in this order.</summary>
        public IReadOnlyList<GameObject> Extras => _extras;

        /// <summary>The manifest it is built for; null where a plan is built with none (a script's, a test's).</summary>
        public Manifest Manifest { get; set; }

        /// <summary>
        /// The plan assessed or built into it last (<see cref="ManifestsActions.Assess(SheetPlan.Shape.Payload, ManifestScene, OpContext)"/>
        /// sets it): what a builder reads beyond its own row (what a later row does).
        /// </summary>
        public SheetPlan.Shape.Payload Plan { get; internal set; }

        /// <summary>The assessment the run under way goes by (each row's state before anything is built); null between runs.</summary>
        public ManifestAssessment Assessment => _run?.assessment;

        /// <summary>
        /// The asset folder the files the plan's steps name are found under (<see cref="Asset{T}"/>): the folder the
        /// plan was staged to, those files copied there at their paths (a step's paths are relative to the
        /// project's settings folder, and staging keeps that layout beside the plan).
        /// </summary>
        public string AssetFolder { get; set; } = "";

        /// <summary>
        /// Finds a file the plan names by its path and the asset type asked for, in place of <see cref="AssetFolder"/>
        /// (a caller's own store); null: the asset database under <see cref="AssetFolder"/>.
        /// </summary>
        public Func<string, Type, UnityEngine.Object> FindAsset { get; set; }

        /// <summary>
        /// A kit's settings of type <typeparamref name="T"/>: the ones handed to <see cref="Use"/>, else
        /// the <see cref="Manifest"/>'s (<see cref="Manifest.Settings{T}"/>), else the type's defaults (made once
        /// a run, and gone as it ends).
        /// </summary>
        /// <typeparam name="T">The kit's settings type.</typeparam>
        public T Settings<T>() where T : ScriptableObject
        {
            if (_settings.TryGetValue(typeof(T), out ScriptableObject known) && known != null) return (T)known;
            T found = Manifest != null ? Manifest.Settings<T>() : null;
            if (found == null)
            {
                found = ScriptableObject.CreateInstance<T>();
                found.hideFlags = HideFlags.HideAndDontSave;  // the defaults: nothing keeps them
                _defaults.Add(found);
            }
            _settings[typeof(T)] = found;
            return found;
        }

        readonly Dictionary<(Type, string, string, string), ScriptableObject> _own = new Dictionary<(Type, string, string, string), ScriptableObject>();
        readonly List<ScriptableObject> _defaults = new List<ScriptableObject>();  // the defaults Settings made: destroyed as the run ends

        /// <summary>
        /// The settings of type <typeparamref name="T"/> one object of a step is built by: the kit's
        /// (<see cref="Settings{T}()"/>), with what <paramref name="row"/> says over them (its Settings cell:
        /// <c>trigger=hold</c> a Use held to play), and what the manifest keeps as the object's own for the row's
        /// behavior over those (<see cref="Manifest.Tune.values"/>: an arrow's colour, a button held to play). The kit's
        /// where neither says any. A row's setting is named as its sheet names it, and resolves under the member of
        /// <typeparamref name="T"/> named as the row's behavior where it has one (<c>size</c> on an <c>indicate</c> row:
        /// <c>indicate.size</c>, <see cref="ManifestBehaviors.SettingName"/>), else by that name alone; an object's own
        /// values are kept by those names. A name that is no field of <typeparamref name="T"/> is left out, and said (a
        /// note on the row being built).
        /// </summary>
        /// <typeparam name="T">The kit's settings type.</typeparam>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet's rows name it; empty: the row itself (the step's own row, or a row's
        /// settings as a whole, <see cref="ManifestBehaviorAttribute.RowSettings"/>: its one object's, where the manifest builds
        /// it as that object's row of its own, <see cref="ManifestsActions.IsOwnRow"/>).</param>
        /// <param name="row">The row being built (its settings, <see cref="SheetPlan.Shape.PlanAction.settings"/>; its behavior's own
        /// values alone); null: none, and every behavior's own values.</param>
        public T Settings<T>(string step, string target, SheetPlan.Shape.PlanAction row = null) where T : ScriptableObject
        {
            T kit = Settings<T>();
            int place = row != null && Rows.TryGetValue(row, out int known) ? known : -1;
            if (string.IsNullOrEmpty(target)) target = ManifestsActions.RowTarget(Manifest, step, row, place);  // a row of one object's own: its object's
            List<Manifest.Value> said = ManifestValues.Said(typeof(T), row?.behavior, row?.settings);
            List<Manifest.Value> values = ManifestValues.Over(Manifest, step, target, place, said, Planned, row?.behavior);
            if (values.Count == 0) return kit;
            var key = (typeof(T), step ?? "", target ?? "", string.Join(";", values.Select(v => v.name + "=" + v.value)));
            if (_own.TryGetValue(key, out ScriptableObject made) && made != null) return (T)made;
            T own = UnityEngine.Object.Instantiate(kit);
            own.hideFlags = HideFlags.HideAndDontSave;
            List<string> unknown = ManifestValues.WriteAll(own, values);
            if (unknown.Count > 0)
            {
                string where = string.IsNullOrEmpty(target) ? step : $"{target.Substring(target.LastIndexOf('|') + 1)} in {step}";
                Note($"{where}: {string.Join(", ", unknown)} is no setting of {typeof(T).Name} (or holds no value of its kind): " +
                     "left out; correct its name in the sheet's Settings or the object's own values");
            }
            _own[key] = own;
            return own;
        }

        // Say text of the row or step being built (its notes, where a run is under way; else the Console).
        void Note(string text) => (_run == null ? null : _run.ctx).Raise(ManifestsActions.NoteCode, text);

        /// <summary>
        /// The objects' own settings a dry run keeps but does not write (what a person changed in the scene since the last
        /// build): read over the manifest's (<see cref="Manifest.tunes"/>), so its build plans what the live run's, which
        /// reads them written, makes.
        /// </summary>
        internal readonly List<Manifest.Tune> Planned = new List<Manifest.Tune>();

        /// <summary>Each row of the plan built last, with its place among its step's rows in the sheet (-1: one added here).</summary>
        internal readonly Dictionary<SheetPlan.Shape.PlanAction, int> Rows = new Dictionary<SheetPlan.Shape.PlanAction, int>();

        /// <summary>Build by <paramref name="settings"/> in place of the manifest's of its type (a script's own, a test's).</summary>
        /// <param name="settings">A kit's settings.</param>
        /// <returns>This scene.</returns>
        public ManifestScene Use(ScriptableObject settings)
        {
            if (settings != null) _settings[settings.GetType()] = settings;
            return this;
        }

        /// <summary>
        /// What a kit keeps on the scene for its builders (the name of the sequence its steps run in, a
        /// file it read once): one <typeparamref name="T"/> a scene, made on first use.
        /// </summary>
        /// <typeparam name="T">The kit's own state type.</typeparam>
        public T State<T>() where T : class, new()
        {
            if (_state.TryGetValue(typeof(T), out object known)) return (T)known;
            var made = new T();
            _state[typeof(T)] = made;
            return made;
        }

        /// <summary>
        /// The asset of type <typeparamref name="T"/> at <paramref name="path"/>, a file the plan names (a step's sound):
        /// <see cref="FindAsset"/>'s, else the asset database's under <see cref="AssetFolder"/>; null where there is none.
        /// </summary>
        /// <typeparam name="T">The asset's type (<c>AudioClip</c>, <c>Texture2D</c> ...).</typeparam>
        /// <param name="path">The file's path, relative to the project's settings folder.</param>
        public T Asset<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (FindAsset != null) return FindAsset(path, typeof(T)) as T;
            string asset = string.IsNullOrEmpty(AssetFolder) ? path : AssetFolder.TrimEnd('/') + "/" + path.TrimStart('/');
            return AssetDatabase.LoadAssetAtPath<T>(asset);
        }

        /// <summary>The take names the delivery carries, sorted.</summary>
        public IReadOnlyList<string> ClipNames
        {
            get
            {
                var names = new List<string>(_clips.Keys);
                names.Sort(StringComparer.Ordinal);
                return names;
            }
        }

        /// <summary>The node <paramref name="path"/> names: its match, bound or why not.</summary>
        /// <param name="path">A node's path in the delivery, <c>|</c> joined.</param>
        public RecordNames.Match<Transform> Find(string path)
        {
            if (_found.TryGetValue(path, out var known)) return known;
            string[] hierarchy = path.Split('|');
            var entry = new RecordNames.Entry(hierarchy[hierarchy.Length - 1], hierarchy);
            var match = RecordNames.Resolve(_nodes, new[] { entry }, t => t.name, t => t.parent, Root.transform)[0];
            if (match.Outcome == RecordNames.Outcome.Ambiguous)
            {
                // A path names the delivery's node: an object a build added beside one of the same name (its own helper,
                // made before the delivery brought one under that name) is the build's, never what the sheet names.
                var delivered = RecordNames.Resolve(Delivered(), new[] { entry }, t => t.name, t => t.parent, Root.transform)[0];
                if (delivered.Outcome == RecordNames.Outcome.Bound) match = delivered;
            }
            for (int i = 0; i < _extras.Count && match.Outcome == RecordNames.Outcome.Absent; i++)
                match = RecordNames.Resolve(_extraNodes[i], new[] { entry }, t => t.name, t => t.parent, _extras[i].transform)[0];
            _found[path] = match;
            return match;
        }

        // The delivery's own nodes: those its model brought (an instance's), not an object added beside them in the scene.
        List<Transform> Delivered()
        {
            if (_delivered == null)
                _delivered = _nodes.Where(t => t != null && PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) != null).ToList();
            return _delivered;
        }

        List<Transform> _delivered;

        /// <summary>The node <paramref name="path"/> names; null when none or several do.</summary>
        /// <param name="path">A node's path in the delivery, <c>|</c> joined.</param>
        public Transform Node(string path)
        {
            var match = Find(path);
            return match.Outcome == RecordNames.Outcome.Bound ? match.Item : null;
        }

        /// <summary>The take named <paramref name="name"/>; null when the delivery has none.</summary>
        /// <param name="name">The take's name.</param>
        public AnimationClip Clip(string name)
            => name != null && _clips.TryGetValue(name, out AnimationClip clip) ? clip : null;

        /// <summary>What is wrong with <paramref name="path"/> here, or null when it names one node.</summary>
        /// <param name="path">A node's path in the delivery.</param>
        /// <param name="what">What the path is to the action (a target, the tool).</param>
        public string Problem(string path, string what) => Problem(path, what, out _);

        /// <summary>What is wrong with <paramref name="path"/> here and its <see cref="ManifestStatus"/>, or null when it names one node.</summary>
        /// <param name="path">A node's path in the delivery.</param>
        /// <param name="what">What the path is to the action (a target, the tool).</param>
        /// <param name="status"><see cref="ManifestStatus.MissingObject"/> or <see cref="ManifestStatus.AmbiguousObject"/>; null when it names one node.</param>
        public string Problem(string path, string what, out string status)
        {
            var match = Find(path);
            switch (match.Outcome)
            {
                case RecordNames.Outcome.Bound:
                    status = null;
                    return null;
                case RecordNames.Outcome.Ambiguous:
                    status = ManifestStatus.AmbiguousObject;
                    return $"the {what} '{path}' matches several nodes under '{Root.name}'";
                default:
                    status = ManifestStatus.MissingObject;
                    string under = _extras.Count == 0 ? $"'{Root.name}'" : $"'{Root.name}' or '{string.Join("', '", _extras.ConvertAll(e => e.name))}'";
                    return $"the {what} '{path}' is no node under {under}";
            }
        }

        // What each step is built from: the rows a build wires, as one line of text (ManifestsActions sets it with the plan).
        internal readonly Dictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// What step <paramref name="id"/> is built from: its rows as the manifest builds them (each one's
        /// behavior, objects, take, the step it goes to, every field and setting its words give it), in one line. A kit keeps it on what it makes for the step,
        /// so a later build sees that the step's rows changed (one turned off, one the sheet dropped)
        /// and builds the step anew. Empty before a plan is read.
        /// </summary>
        /// <param name="id">The step's ID.</param>
        public string Source(string id) => id != null && Sources.TryGetValue(id, out string source) ? source : "";

        // -- a run: whose changes and parts are whose ------------------------------------------

        // One assessment or build under way: what is asked and held goes to the innermost scope.
        sealed class Run
        {
            internal ManifestAssessment assessment;
            internal OpContext ctx;
            internal HashSet<string> only;
            internal HashSet<string> locked;
            internal int changes, issues;  // how far ctx's journal is handed out
            internal readonly Stack<(List<Change> changes, List<UnityEngine.Object> parts, List<string> notes)> scopes =
                new Stack<(List<Change>, List<UnityEngine.Object>, List<string>)>();
        }

        sealed class Scope : IDisposable
        {
            readonly ManifestScene _scene;
            internal Scope(ManifestScene scene) => _scene = scene;

            public void Dispose()
            {
                if (_scene._run == null || _scene._run.scopes.Count == 0) return;
                _scene.Flush();
                _scene._run.scopes.Pop();
            }
        }

        sealed class NoScope : IDisposable
        {
            public void Dispose()
            {
            }
        }

        Run _run;

        // The run begins: from here each change asked of ctx, each issue raised and each part held
        // goes to the scope it happens in (a row's, a step's), else to what the steps share.
        internal void Begin(ManifestAssessment assessment, OpContext ctx, IEnumerable<string> only, IEnumerable<string> locked)
        {
            _run = new Run
            {
                assessment = assessment,
                ctx = ctx,
                only = only == null ? null : new HashSet<string>(only, StringComparer.Ordinal),
                locked = new HashSet<string>(locked ?? Array.Empty<string>(), StringComparer.Ordinal),
                changes = ctx == null ? 0 : ctx.Changes.Count,
                issues = ctx == null ? 0 : ctx.Issues.Count,
            };
        }

        // The run ends: what is left goes to what the steps share; the settings copies it made go.
        internal void End()
        {
            foreach (ScriptableObject own in _own.Values)
                if (own != null) UnityEngine.Object.DestroyImmediate(own);
            _own.Clear();
            foreach (ScriptableObject made in _defaults)
            {
                if (made == null) continue;
                _settings.Remove(made.GetType());
                UnityEngine.Object.DestroyImmediate(made);
            }
            _defaults.Clear();
            if (_run == null) return;
            Flush();
            _run = null;
        }

        // What was asked and raised since the last hand-out, handed to the innermost scope.
        internal void Flush()
        {
            if (_run == null || _run.ctx == null) return;
            var (changes, _, notes) = _run.scopes.Count > 0
                ? _run.scopes.Peek()
                : (_run.assessment.shared.changes, _run.assessment.shared.parts, _run.assessment.shared.notes);
            for (; _run.changes < _run.ctx.Changes.Count; _run.changes++) changes.Add(_run.ctx.Changes[_run.changes]);
            for (; _run.issues < _run.ctx.Issues.Count; _run.issues++)
            {
                string said = _run.ctx.Issues[_run.issues].message;
                if (!notes.Contains(said)) notes.Add(said);
            }
        }

        // The issues raised since the last hand-out are no scope's notes (the engine's own, saying what a row already says).
        internal void Skip()
        {
            if (_run == null || _run.ctx == null) return;
            _run.issues = _run.ctx.Issues.Count;
        }

        IDisposable Enter(List<Change> changes, List<UnityEngine.Object> parts, List<string> notes)
        {
            if (_run == null) return new NoScope();
            Flush();
            _run.scopes.Push((changes, parts, notes));
            return new Scope(this);
        }

        /// <summary>
        /// What a kit builds until this is disposed is step <paramref name="id"/>'s own (its object in
        /// the sequence, its page, its sound): the manifest counts its changes and lists its parts
        /// (<see cref="Hold"/>) under that step. Outside a run, and for a step the plan lacks, it does nothing.
        /// </summary>
        /// <param name="id">The step's ID.</param>
        public IDisposable For(string id)
        {
            ManifestAssessment.Step step = _run?.assessment.Find(id);
            return step == null ? new NoScope() : Enter(step.changes, step.parts, step.notes);
        }

        // What is built until this is disposed is row's.
        internal IDisposable For(ManifestAssessment.Row row) => Enter(row.changes, row.parts, row.notes);

        /// <summary>
        /// Say that the row (or the step, inside <see cref="For"/>) being built holds <paramref name="part"/>:
        /// a component or object it made, or found made. The manifest lists it under that row, to select
        /// and to edit. Null (a dry run's planned part) and a part held already are left out.
        /// </summary>
        /// <param name="part">The part.</param>
        public void Hold(UnityEngine.Object part)
        {
            if (_run == null || part == null) return;
            List<UnityEngine.Object> parts = _run.scopes.Count > 0 ? _run.scopes.Peek().parts : _run.assessment.shared.parts;
            if (!parts.Contains(part)) parts.Add(part);
        }

        /// <summary>
        /// Whether the run under way builds step <paramref name="id"/>: not where the step is locked, nor
        /// where the run builds only other steps. A kit asks before building a step's own part.
        /// </summary>
        /// <param name="id">The step's ID.</param>
        public bool Builds(string id)
            => _run == null || (!_run.locked.Contains(id) && (_run.only == null || _run.only.Contains(id)));

        // The clips the root's model asset carries (an imported FBX's takes), its previews left out.
        static IEnumerable<AnimationClip> ModelClips(GameObject root)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(root);
            string path = source == null ? null : AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(path)) yield break;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    yield return clip;
        }
    }
}
