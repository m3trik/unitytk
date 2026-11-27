using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Tools.Manifests
{
    /// <summary>
    /// Tools > unitytk > Manifest: the launchpad for a scene a sheet sets up. It opens on a
    /// <see cref="Manifest"/> (its plan, delivery, scene and settings) and shows the plan step by
    /// step, each step's objects under it with the behaviors the sheet gives them and where each
    /// stands in the open scene and why; Assess checks, Build brings the scene in line (keeping what
    /// a person adjusted), a row leads its menu with its own fix, and what a behavior built opens in
    /// its own editor. A click on an object's behaviors gives it one, takes one away or opens one's
    /// settings; the window's menu turns a behavior off for the whole manifest. It changes nothing
    /// itself: every change is a <see cref="ManifestsActions"/> call, the ones an agent makes from code.
    /// </summary>
    /// <remarks>
    /// Prior art: the producer's own manifest panel, the other manifest of the same sheet, whose layout and
    /// words this window shares: the source at the top, Select / Expand / Collapse / Hide / Show
    /// above a table of steps with their objects on a darker ground, an object's behaviors in one
    /// cell (a click ticks them on and off), a row's state as an icon and a few words in its
    /// status's colour with every issue in its tooltip, and the summary beside Assess and Build (N)
    /// at the foot. Its look is that panel's too (<c>ManifestWindow.uss</c>). Two things that panel
    /// does awkwardly are done otherwise here: which behaviors are off is the manifest's, shared with
    /// the project, not each person's own setting; and a behavior says itself what its settings are
    /// (<see cref="ManifestBehaviorAttribute.Settings"/>) and what it built, so the window names no
    /// behavior.
    /// Unity's own pieces do the rest: a multi-column tree view, the property editor a part opens
    /// in (<c>EditorUtility.OpenPropertyEditor</c>), and an inspector element for the manifest's
    /// own fields.
    /// </remarks>
    public sealed class ManifestWindow : EditorWindow
    {
        const string ManifestKey = "unitytk.Manifest.manifest";
        const string IssuesKey = "unitytk.Manifest.issuesOnly";
        const string AutoKey = "unitytk.Manifest.autoAssess";
        const string FollowKey = "unitytk.Manifest.followSelection";
        const string LongNamesKey = "unitytk.Manifest.longNames";
        const string StylePath = "Packages/com.m3trik.unitytk/Tools/Manifests/ManifestWindow.uss";

        /// <summary>
        /// The longest an assessment may take, in seconds, for the window to run it by itself as the
        /// scene changes; past it the foot says the scene changed, and Assess is the person's to press.
        /// </summary>
        public const double AutoAssessBudget = 0.35;

        const double Settle = 0.6;  // seconds the scene rests before an assessment follows a change
        // Seconds after a run of the window's own in which a change is its own: Unity publishes a run's changes
        // (ObjectChangeEvents, hierarchyChanged, projectChanged) on a later editor update, after the run is done. A time,
        // not the event order: no test can show a call deferred past the run (EditorApplication.delayCall) comes after
        // them, since the batch-mode editor the tests run in published them and never ran the deferred call.
        const double Quiet = 0.3;

        [SerializeField] Manifest _manifest;  // serialized: kept over a domain reload, and held where a new scene unloads what nothing holds
        SheetPlan.Shape.Payload _plan;
        string _refusal;
        string _changed = "";  // what changed since the plan was read (ManifestsActions.Stale); empty: nothing
        string _unchecked = "";  // what of it could not be told, where nothing changed (a file held, settings unread); empty: all told
        ManifestAssessment _assessed;
        List<ManifestLines.Line> _lines = new List<ManifestLines.Line>();
        readonly List<ManifestLines.Line> _shown = new List<ManifestLines.Line>();
        readonly Dictionary<int, int> _owners = new Dictionary<int, int>();  // a scene object's instance ID -> its line's tree ID
        readonly Dictionary<ManifestLines.Line, int> _ids = new Dictionary<ManifestLines.Line, int>();
        bool _stale, _busy, _setupOpen, _helpOpen;
        string _setupShown = "";  // what Setup's inspectors were made for
        readonly List<Issue> _said = new List<Issue>();  // what the last run said that no row says (a refusal): notices until the next
        double _assessSeconds, _touchedAt, _quietUntil;

        ObjectField _pick;
        Label _source, _summary;
        VisualElement _notices, _setup, _body, _empty;
        HelpBox _help;
        MultiColumnTreeView _tree;
        ScrollView _detail;
        ToolbarSearchField _search;
        Button _build, _assess;

        /// <summary>Open the manifest window.</summary>
        [MenuItem("Tools/unitytk/Manifest")]
        public static ManifestWindow Open()
        {
            var window = GetWindow<ManifestWindow>();
            window.titleContent = new GUIContent("Manifest");
            window.minSize = new Vector2(520, 320);
            return window;
        }

        /// <summary>The manifest it shows.</summary>
        public Manifest Manifest
        {
            get => _manifest;
            set
            {
                _manifest = value;
                _assessed = null;
                string path = value != null ? AssetDatabase.GetAssetPath(value) : "";
                Remembered = string.IsNullOrEmpty(path) ? "" : AssetDatabase.AssetPathToGUID(path);
                if (_pick != null) _pick.SetValueWithoutNotify(value);
                if (value != null) Assess();
                else
                {
                    Read();
                    Refresh();
                }
            }
        }

        /// <summary>The assessment it shows; null before one (no manifest, or a plan that cannot be read).</summary>
        public ManifestAssessment Assessed => _assessed;

        /// <summary>The lines its table shows now: the plan's steps (their rows under them) the search and the Issues view leave in.</summary>
        public IReadOnlyList<ManifestLines.Line> Lines => _shown;

        /// <summary>Whether the table shows only the steps with something to say (Hide > Steps With Nothing to Do): a view alone, Assess and Build still take every step.</summary>
        public bool IssuesOnly
        {
            get => EditorPrefs.GetBool(IssuesKey, false);
            set
            {
                EditorPrefs.SetBool(IssuesKey, value);
                Refresh();
            }
        }

        /// <summary>Whether it assesses by itself as the scene changes (while an assessment is quick enough: <see cref="AutoAssessBudget"/>).</summary>
        public bool AutoAssess
        {
            get => EditorPrefs.GetBool(AutoKey, true);
            set => EditorPrefs.SetBool(AutoKey, value);
        }

        /// <summary>Whether selecting an object in the scene selects the row that holds it.</summary>
        public bool FollowSelection
        {
            get => EditorPrefs.GetBool(FollowKey, true);
            set => EditorPrefs.SetBool(FollowKey, value);
        }

        /// <summary>Whether an object's row shows its whole path, read by its end: its parents faded (cut from the start where the
        /// column is narrow), its own name bold. Off: its own name alone, plain.</summary>
        public bool LongNames
        {
            get => EditorPrefs.GetBool(LongNamesKey, false);
            set
            {
                EditorPrefs.SetBool(LongNamesKey, value);
                _tree?.RefreshItems();
            }
        }

        // -- the window's own life ------------------------------------------------------------

        void OnEnable()
        {
            ObjectChangeEvents.changesPublished += OnChanges;
            EditorApplication.hierarchyChanged += Touched;
            EditorApplication.projectChanged += Touched;
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            Selection.selectionChanged += OnSceneSelection;
        }

        void OnDisable()
        {
            ObjectChangeEvents.changesPublished -= OnChanges;
            EditorApplication.hierarchyChanged -= Touched;
            EditorApplication.projectChanged -= Touched;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            Selection.selectionChanged -= OnSceneSelection;
        }

        void OnChanges(ref ObjectChangeEventStream stream) => Touched();

        void OnSceneChanged(Scene from, Scene to)
        {
            if (!ShowSceneOwn(to)) Touched();
        }

        // The manifest the window was last set to, by its GUID, kept with the project (its user settings): another
        // project has other manifests. It was an editor preference, one for every project on the machine, so a project
        // opened later came up showing whatever manifest of that GUID it had (a copy from weeks before), or none.
        static string Remembered
        {
            get => EditorUserSettings.GetConfigValue(ManifestKey) ?? "";
            set => EditorUserSettings.SetConfigValue(ManifestKey, value ?? "");
        }

        // Show the manifest whose scene the open one is, where the window shows another's or none: a scene a person
        // opens is the task they work on. False where it shows it already, no manifest names the scene, or two do (a
        // person picks).
        bool ShowSceneOwn(Scene open)
        {
            if (string.IsNullOrEmpty(open.path) || _manifest != null && _manifest.scene == open.path) return false;
            Manifest own = null;
            foreach (string guid in AssetDatabase.FindAssets($"t:{typeof(Manifest).FullName}"))
            {
                Manifest each = AssetDatabase.LoadAssetAtPath<Manifest>(AssetDatabase.GUIDToAssetPath(guid));
                if (each == null || each.scene != open.path) continue;
                if (own != null) return false;
                own = each;
            }
            if (own == null) return false;
            Manifest = own;
            return true;
        }

        void OnPlayMode(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode) Touched();
        }

        // Something the assessment reads changed (the scene, the plan's file, the manifest): it is stale from now.
        void Touched()
        {
            if (_busy || _manifest == null || EditorApplication.timeSinceStartup < _quietUntil) return;
            _stale = true;
            _touchedAt = EditorApplication.timeSinceStartup;
        }

        // Each tick: a stale assessment is run again once the scene has rested, where the window does so by itself.
        void Tick()
        {
            if (!_stale || _busy || _manifest == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.timeSinceStartup - _touchedAt < Settle) return;
            if (AutoAssess && _assessSeconds <= AutoAssessBudget) Assess();
            else if (_summary != null && !_summary.text.EndsWith(StaleNote, StringComparison.Ordinal)) _summary.text += StaleNote;
        }

        const string StaleNote = "  •  the scene changed: Assess";

        // -- what a person does ---------------------------------------------------------------

        /// <summary>Check every row against the open scene (<see cref="ManifestsActions.Assess(Manifest, OpContext)"/>).</summary>
        public void Assess()
        {
            Restore();
            if (_manifest == null) return;
            var watch = Stopwatch.StartNew();
            _busy = true;
            try
            {
                Read();
                _assessed = ManifestsActions.Assess(_manifest, new OpContext());
            }
            finally
            {
                Done();
            }
            _assessSeconds = watch.Elapsed.TotalSeconds;
            Refresh();
        }

        /// <summary>
        /// Build the manifest in the open scene (<see cref="ManifestsActions.Build(Manifest, OpContext, IEnumerable{string})"/>),
        /// keeping what a person adjusted, as one undo step; then assess again.
        /// </summary>
        /// <param name="only">The steps to build, by ID; null: every step.</param>
        public void Build(IEnumerable<string> only = null)
        {
            if (_manifest == null) return;
            List<string> steps = only == null ? null : only.ToList();
            _busy = true;
            try
            {
                Run(steps == null ? "Build Manifest" : $"Build {string.Join(", ", steps)}", run => ManifestsActions.Build(_manifest, run, steps));
            }
            finally
            {
                Done();
            }
            Assess();
        }

        /// <summary>
        /// Read the manifest's sheet again and stage it into this project (<see cref="ManifestsActions.ReadAgain"/>), then
        /// show it as it now reads: what a build would change. No terminal: the reader is run from here; where it cannot
        /// be, the notice says why and the command is there to copy.
        /// </summary>
        public void ReadAgain()
        {
            if (_manifest == null) return;
            _busy = true;
            try
            {
                Run("Read the Sheet Again", run => ManifestsActions.ReadAgain(_manifest, run));
            }
            finally
            {
                Done();
            }
            Assess();
        }

        /// <summary>
        /// Build the manifest anew in a new scene (<see cref="ManifestsActions.Reset"/>), as one undo step; then assess. What
        /// keeps it from being built (a locked step, no delivery, a setup that failed) is said in the window's notices
        /// (<see cref="Reported"/>) and the Console.
        /// </summary>
        /// <param name="discard">Discard a person's adjustments first.</param>
        public void Reset(bool discard)
        {
            if (_manifest == null) return;
            _busy = true;
            try
            {
                Run("Build in a New Scene", run => ManifestsActions.Reset(_manifest, discard, run));
            }
            finally
            {
                Done();
            }
            Assess();
        }

        /// <summary>Lock or unlock steps (<see cref="ManifestsActions.SetLocked"/>): a build leaves a locked step as it is.</summary>
        /// <param name="steps">Step IDs.</param>
        /// <param name="locked">Lock them (true) or unlock them.</param>
        public void SetLocked(IEnumerable<string> steps, bool locked)
        {
            if (_manifest == null) return;
            List<string> ids = steps.ToList();
            Run(locked ? "Lock Steps" : "Unlock Steps", run => ManifestsActions.SetLocked(_manifest, ids, locked, run));
            Assess();
        }

        /// <summary>Whether <paramref name="behavior"/> is off for the manifest (<see cref="Manifest.off"/>).</summary>
        /// <param name="behavior">A behavior's name.</param>
        public bool IsOff(string behavior) => _manifest != null && _manifest.off.Contains(behavior);

        /// <summary>
        /// Turn <paramref name="behavior"/> and the rest of its family (<see cref="ManifestBehaviors.Family"/>: Fade's
        /// in and out) off for the manifest, or on again (<see cref="ManifestsActions.SetOff"/>); then assess.
        /// </summary>
        /// <param name="behavior">A behavior's name.</param>
        /// <param name="off">Turn it off (true) or on again.</param>
        public void SetOff(string behavior, bool off)
        {
            if (_manifest == null) return;
            string label = ManifestLines.BehaviorLabel(ManifestBehaviors.FamilyOf(behavior));
            IReadOnlyList<string> family = ManifestBehaviors.Family(behavior);
            Run(off ? $"Turn {label} Off" : $"Turn {label} On", run => ManifestsActions.SetOff(_manifest, family, off, run));
            Assess();
        }

        // The behaviors the plan uses, one per family (the first it uses, by name): what the menu and Setup each
        // offer one switch for.
        IEnumerable<string> UsedFamilies()
        {
            var seen = new HashSet<string>();
            foreach (string name in ManifestBehaviors.Names())
                if (Uses(name) && seen.Add(ManifestBehaviors.FamilyOf(name))) yield return name;
        }

        // What a family's switch does: each of its behaviors' own words.
        static string AboutFamily(string behavior)
            => string.Join("\n", ManifestBehaviors.Family(behavior).Select(b => ManifestBehaviors.Mark(b)?.About ?? "").Where(a => a.Length > 0));

        /// <summary>
        /// Give the object of <paramref name="line"/> the behavior <paramref name="behavior"/> in its step, or
        /// take it away (<see cref="ManifestsActions.SetAssigned"/>: an edit the manifest keeps); then assess.
        /// </summary>
        /// <param name="line">An object's line.</param>
        /// <param name="behavior">A behavior's name.</param>
        /// <param name="on">The object has it (true) or not.</param>
        public void Assign(ManifestLines.Line line, string behavior, bool on)
            => Assign(line, new Dictionary<string, bool> { [behavior] = on });

        /// <summary>Give <paramref name="line"/>'s object one row of <paramref name="behavior"/>, or take it away (<paramref name="row"/>: its place among the step's rows in the sheet; -1: every row of it), as one edit; then assess.</summary>
        /// <param name="line">The line.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="on">The object has it.</param>
        /// <param name="row">Which of the step's rows of the sheet; -1: every row of it the object has.</param>
        public void Assign(ManifestLines.Line line, string behavior, bool on, int row)
        {
            if (_manifest == null || line == null) return;
            string target = line.IsStep ? "" : line.target;
            Run($"{(on ? "Give " : "Take ")}{ManifestLines.BehaviorLabel(behavior)} {(line.IsStep ? line.step : line.description)}",
                run => ManifestsActions.SetAssigned(_manifest, line.step, target, behavior, on, run, row));
            Assess();
        }

        /// <summary>
        /// Give the object of <paramref name="line"/> each behavior of <paramref name="behaviors"/> marked true and
        /// take away each marked false, as one edit (one undo); then assess.
        /// </summary>
        /// <param name="line">An object's line.</param>
        /// <param name="behaviors">Each behavior's name, and whether the object has it.</param>
        public void Assign(ManifestLines.Line line, IReadOnlyDictionary<string, bool> behaviors)
        {
            if (_manifest == null || line == null || behaviors == null || behaviors.Count == 0) return;
            string what = behaviors.Count == 1
                ? (behaviors.First().Value ? "Give " : "Take ") + ManifestLines.BehaviorLabel(behaviors.First().Key)
                : $"Change {behaviors.Count} Behaviors of";
            string target = line.IsStep ? "" : line.target;  // a step's own: the behaviors that name no object
            Run($"{what} {(line.IsStep ? line.step : line.description)}", run =>
            {
                foreach (var pair in behaviors) ManifestsActions.SetAssigned(_manifest, line.step, target, pair.Key, pair.Value, run);
            });
            Assess();
        }

        // Whether the plan, or an edit of the manifest's, has a row of behavior.
        bool Uses(string behavior)
        {
            if (_plan != null)
                foreach (var step in _plan.steps)
                    if (step.actions != null && step.actions.Exists(a => a.behavior == behavior)) return true;
            return _manifest != null && _manifest.edits.Exists(e => e != null && e.behavior == behavior);
        }

        // Run an action as a menu item does (one undo step, a progress bar, its issues in the Console), keeping what it said
        // that no row says as the window's notices until the next run: each issue but a row's own (unresolved, unsupported,
        // and whatever the run's assessment says on a row, a step or what the steps share: a kit's note on a spot or a fade).
        T Run<T>(string title, Func<OpContext, T> action)
        {
            OpContext used = null;
            T result = OpContext.RunMenu(title, ctx =>
            {
                used = ctx;
                return action(ctx);
            });
            _said.Clear();
            var assessed = result as ManifestAssessment;  // a build's: what it says on its rows is theirs
            if (used != null)
                foreach (Issue issue in used.Issues)
                    if (issue.code != ManifestsActions.UnresolvedCode && issue.code != ManifestsActions.UnsupportedCode && issue.code != ManifestsActions.NoteCode
                        && (assessed == null || !assessed.Says(issue.message)) && !_said.Exists(said => said.message == issue.message))
                        _said.Add(issue);
            return result;
        }

        void Run(string title, Action<OpContext> action) => Run<object>(title, ctx =>
        {
            action(ctx);
            return null;
        });

        /// <summary>
        /// What the window's last run said that no row of the table says (a refusal, a setup that failed): shown as its notices until the
        /// next run, each with its severity's mark.
        /// </summary>
        public IReadOnlyList<Issue> Reported => _said;

        /// <summary>
        /// Open <paramref name="behavior"/>'s own settings: the fields of its kit's settings it says are its
        /// own, kept in the manifest (made its own there first, where it built by the kit's defaults).
        /// </summary>
        /// <param name="behavior">A behavior's name.</param>
        public void OpenBehaviorSettings(string behavior)
        {
            Type type = ManifestBehaviors.SettingsOf(behavior);
            if (_manifest == null || type == null) return;
            ScriptableObject own = _manifest.Settings(type);
            if (own == null)
                own = Run($"Keep {ObjectNames.NicifyVariableName(type.Name)}", run => ManifestsActions.AddSettings(_manifest, type, run));
            if (own != null) BehaviorSettingsWindow.Open(this, behavior, own, ManifestBehaviors.Mark(behavior).Settings);
        }

        // What a line's behaviors offer in menu: each one ticked while the object has it, and what else it can be
        // given, as a checkbox list that stays open (what was ticked is one edit as it closes, as the producer's manifest's
        // local edit is); then each one's editors: what it built, and its settings.
        void BehaviorEntries(ManifestLines.Line line, WindowMenu menu)
        {
            if (_manifest == null || line == null) return;
            var seen = new HashSet<string>();
            {
                var wanted = new Dictionary<string, bool>();
                void Want(string behavior, bool had, bool on)
                {
                    if (on == had) wanted.Remove(behavior);
                    else wanted[behavior] = on;
                }
                foreach (ManifestLines.Token token in line.tokens)
                {
                    if (!seen.Add(token.behavior)) continue;
                    string behavior = token.behavior;
                    bool had = !token.off;
                    if (token.offEverywhere) menu.Item($"Behaviors/{token.label}", null, tooltip: "Turned off for every row: the window's menu, Build These Behaviors.", hint: "off everywhere");
                    else menu.Toggle($"Behaviors/{token.label}", had, on => Want(behavior, had, on), token.tooltip,
                                     token.added ? "added here" : token.off ? "taken away here" : ManifestStatus.Label(token.status));
                }
                AddEntries(menu, line, seen, behavior => Want(behavior, false, true), behavior => Want(behavior, false, false));
                menu.Closed += () =>
                {
                    if (wanted.Count > 0) Assign(line, wanted);
                };
            }
            int own = ManifestsActions.Revert(_manifest, line.step, line.IsStep ? null : line.target, new OpContext(dryRun: true));  // what Revert drops
            if (own > 0)
                menu.Item($"Revert '{(line.IsStep ? line.step : line.description)}'", () => RevertEdits(line.step, line.IsStep ? null : line.target),
                          tooltip: line.IsStep ? "Drop this step's edits: its rows as the sheet writes them, no object's own values."
                                               : "Drop this object's edits in its step: its behaviors as the sheet gives them, no values of its own.",
                          hint: $"{own} edit{(own == 1 ? "" : "s")}");
            if (!line.IsStep)
            {
                var placed = Parts(line).OfType<Component>().ToList();
                int again = placed.Count == 0 ? 0 : ManifestsActions.PlaceAgain(_manifest, placed, new OpContext(dryRun: true));
                if (again > 0)
                    menu.Item("Place Again", () => PlaceAgain(placed),
                              tooltip: "Search anew for where what this row placed stands (a viewpoint, an arrow, a picture's camera), then build: " +
                                       "its first placement, and any move of it, go. Otherwise it keeps where it was first placed.",
                              hint: $"{again} spot{(again == 1 ? "" : "s")}");
            }
            menu.Separator();
            seen.Clear();
            foreach (ManifestLines.Token token in line.tokens)
            {
                if (!seen.Add(token.behavior)) continue;
                foreach (Object part in Parts(line, token).Take(6))
                {
                    Object each = part;
                    menu.Item($"Edit/{token.label}", () => EditorUtility.OpenPropertyEditor(each),
                              tooltip: "Its own editor, in a window of its own.", hint: PartName(each));
                }
                if (ManifestBehaviors.SettingsOf(token.behavior) == null) continue;
                string behavior = token.behavior;
                menu.Item($"Settings/{token.label}…", () => OpenBehaviorSettings(behavior), tooltip: "How every row of it is built: kept in the manifest.");
            }
        }

        /// <summary>
        /// Give <paramref name="target"/> its own setting <paramref name="name"/> for <paramref name="behavior"/> in <paramref name="step"/>
        /// (<see cref="ManifestsActions.SetTune"/>; null: its kit's again), as one undo; then assess.
        /// </summary>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object; empty: the step's own row.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="name">A setting's name.</param>
        /// <param name="value">Its value as text; null: its kit's again.</param>
        /// <param name="row">Which of the step's rows of the sheet; -1: every row, or one added here.</param>
        public void SetTune(string step, string target, string behavior, string name, string value, int row = -1)
        {
            if (_manifest == null) return;
            Run($"Set {ObjectNames.NicifyVariableName(name)}", run => ManifestsActions.SetTune(_manifest, step, target, behavior, name, value, run, row));
            Assess();
        }

        /// <summary>
        /// Give <paramref name="target"/>'s row of <paramref name="behavior"/> in <paramref name="step"/> its own field <paramref name="name"/>
        /// (<see cref="ManifestsActions.SetRowField"/>; null: the sheet's again), as one undo; then assess.
        /// </summary>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object; empty: the step's own row.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="name">A row field's name.</param>
        /// <param name="value">Its value as text; null: the sheet's again.</param>
        /// <param name="row">Which of the step's rows of the sheet; -1: every row, or one added here.</param>
        public void SetRowField(string step, string target, string behavior, string name, string value, int row = -1)
        {
            if (_manifest == null) return;
            Run($"Set {ObjectNames.NicifyVariableName(name)}", run => ManifestsActions.SetRowField(_manifest, step, target, behavior, name, value, run, row));
            Assess();
        }

        /// <summary>Keep what <paramref name="own"/> holds otherwise than the sheet's row builds it as the object's own settings, as one undo; then assess.</summary>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object; empty: the step's own row.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="own">The edited copy of the object's settings.</param>
        /// <param name="settings">The behavior's settings.</param>
        /// <param name="row">Which of the step's rows of the sheet; -1: every row, or one added here.</param>
        public void SetTunes(string step, string target, string behavior, ScriptableObject own, string[] settings, int row = -1)
        {
            if (_manifest == null || own == null) return;
            Run("Set Settings", run => ManifestsActions.SetTunes(_manifest, step, target, behavior, own, settings, run, row));
            Assess();
        }

        /// <summary>Drop the object's own values for <paramref name="behavior"/> (its settings and its row's fields), as one undo; then assess.</summary>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object; empty: the step's own row.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="row">Which of the step's rows of the sheet; -1: every row, or one added here.</param>
        public void RevertTune(string step, string target, string behavior, int row = -1)
        {
            Manifest.Tune tune = _manifest == null ? null : _manifest.TuneOf(step, target, behavior, row);
            if (tune == null) return;
            int kept = tune.row;
            Run("Back to the Sheet", run =>
            {
                foreach (Manifest.Value value in tune.values.ToList())
                    if (value != null) ManifestsActions.SetTune(_manifest, step, target, behavior, value.name, null, run, kept);
                foreach (Manifest.Value value in tune.fields.ToList())
                    if (value != null) ManifestsActions.SetRowField(_manifest, step, target, behavior, value.name, null, run, kept);
            });
            Assess();
        }

        // Open one object's values for a chip's behavior: its row's fields (the sheet's beside them) and its settings; wholeRow:
        // those of the object's row as a whole instead (Offered), every object of it builds by.
        void OpenFor(ManifestLines.Line line, ManifestLines.Token token, bool wholeRow = false)
        {
            string target = line.IsStep || wholeRow ? "" : line.target;
            ManifestBehaviorAttribute mark = ManifestBehaviors.Mark(token.behavior);
            string[] fields = wholeRow ? new string[0] : mark?.Fields ?? new string[0];
            var sheet = new string[fields.Length];
            SheetPlan.Shape.PlanAction action = _plan.Row(line.step, token.source);
            for (int i = 0; i < fields.Length; i++)
                sheet[i] = action == null ? "" : action.Field(fields[i]) ?? "";
            int row = token.added ? -1 : token.source;
            BehaviorSettingsWindow.OpenFor(this, token.behavior, line.step, target, Offered(line, token, wholeRow), fields, sheet, row, wholeRow);
        }

        // What a chip's Settings offer on line (ManifestsActions.Offered): the object's own, or its row's as a whole.
        string[] Offered(ManifestLines.Line line, ManifestLines.Token token, bool wholeRow = false)
            => ManifestsActions.Offered(_manifest, line.step, line.IsStep ? "" : line.target, token.behavior, token.added ? -1 : token.source, wholeRow);

        /// <summary>Have what <paramref name="parts"/> placed by a search placed anew, then build (<see cref="ManifestsActions.PlaceAgain(Manifest, IEnumerable{Component}, OpContext)"/>).</summary>
        /// <param name="parts">Parts of the open scene a row holds.</param>
        public void PlaceAgain(IEnumerable<Component> parts)
        {
            Run("Place Again", run => ManifestsActions.PlaceAgain(_manifest, parts, run));
            Build();
        }

        /// <summary>Drop the manifest's edits of <paramref name="step"/>'s <paramref name="target"/> (null: all of the step's; both null: every edit), as one undo; then assess.</summary>
        /// <param name="step">The step's ID; null: every step.</param>
        /// <param name="target">The object, as the sheet's rows name it; null: every object of the step.</param>
        public void RevertEdits(string step, string target)
        {
            if (_manifest == null) return;
            Run("Revert to Sheet", run => ManifestsActions.Revert(_manifest, step, target, run));
            Assess();
        }

        // Show menu under element: its top left at the element's bottom left.
        void ShowUnder(WindowMenu menu, VisualElement element)
            => menu.Show(rootVisualElement, new Vector2(element.worldBound.xMin, element.worldBound.yMax));

        // The manifest read again where it was unloaded under the window (a new scene unloads an asset nothing in it
        // holds): the one the window was last set to, by its remembered GUID.
        void Restore()
        {
            if (_manifest != null) return;
            string guid = Remembered;
            if (string.IsNullOrEmpty(guid)) return;
            _manifest = AssetDatabase.LoadAssetAtPath<Manifest>(AssetDatabase.GUIDToAssetPath(guid));
            if (_pick != null) _pick.SetValueWithoutNotify(_manifest);
        }

        // The manifest's plan, read again, and whether what it was read from has changed since.
        void Read()
        {
            _refusal = null;
            _plan = _manifest == null ? null : ManifestsActions.Plan(_manifest, out _refusal);
            string why = "";
            bool stale = _manifest != null && ManifestsActions.Stale(_manifest, out why);
            _changed = stale ? why : "";
            _unchecked = stale ? "" : why;
        }

        // A run of the window's own ended: the changes it published are its own, not a person's.
        void Done()
        {
            _busy = false;
            _stale = false;
            _quietUntil = EditorApplication.timeSinceStartup + Quiet;
        }

        void AskReset()
        {
            if (_manifest == null) return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            int choice = EditorUtility.DisplayDialogComplex("Build in a New Scene",
                "Build the manifest anew in a new scene. Keep what you adjusted in this one (the spots its kits placed), or discard it?",
                "Keep adjustments", "Cancel", "Discard adjustments");
            if (choice != 1) Reset(discard: choice == 2);
        }

        void NewFromPlan()
        {
            string picked = EditorUtility.OpenFilePanel("The plan (the JSON the sheet's reader writes)", "Assets", "json");
            if (string.IsNullOrEmpty(picked)) return;
            string full = Path.GetFullPath(picked).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            var plan = full.StartsWith(assets, StringComparison.OrdinalIgnoreCase)
                ? AssetDatabase.LoadAssetAtPath<TextAsset>("Assets" + full.Substring(assets.Length))
                : null;
            if (plan == null)
            {
                EditorUtility.DisplayDialog("New Manifest", "Pick a plan under this project's Assets folder: stage it there first, with its delivery.", "OK");
                return;
            }
            Manifest made = Run("New Manifest", run => ManifestsActions.Create(plan, run));
            if (made != null) Manifest = made;
        }

        // -- the layout -----------------------------------------------------------------------

        void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            EditorLook.Apply(root);
            root.AddToClassList("manifest");
            var look = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            if (look != null) root.styleSheets.Add(look);

            // The window's tab says what it is: its first row is the manifest, its actions, help and the menu.
            var source = new Toolbar();
            source.AddToClassList("manifest__bar");
            _pick = new ObjectField { objectType = typeof(Manifest), allowSceneObjects = false, tooltip = "The manifest: its plan (the sheet as read), its delivery, its scene and settings." };
            _pick.style.flexGrow = 1;
            _pick.RegisterValueChangedCallback(e => Manifest = e.newValue as Manifest);
            source.Add(_pick);
            source.Add(new ToolbarButton(NewFromPlan) { text = "New…", tooltip = "Make a manifest from a plan (the JSON the sheet's reader writes), beside it." });
            source.Add(new ToolbarButton(ToggleSetup)
            { text = "Setup", tooltip = "The manifest's own fields: its plan, delivery, scene, each kit's settings and its behaviors." });
            source.Add(new ToolbarButton(() =>
            {
                _helpOpen = !_helpOpen;
                _help.style.display = _helpOpen ? DisplayStyle.Flex : DisplayStyle.None;
            })
            { text = "?", tooltip = "How the manifest is used, in short." });
            var menu = new ToolbarButton { text = "≡", tooltip = "The manifest's own actions, each behavior's switch and this window's options." };
            menu.clicked += () => ShowUnder(TitleMenu(), menu);
            source.Add(menu);
            root.Add(source);

            _help = new HelpBox(
                $"1. Stage the sheet into this project, in a terminal: {ManifestsActions.SyncCommand("<the sheet's settings file>", "<this project>")}. " +
                "It reads the sheet into a plan and copies it, its delivery and its sounds under Assets; run it again whenever the sheet or the delivery changes.\n" +
                "2. New... picks the plan: its manifest is made beside it. Setup holds its delivery, scene and each kit's settings.\n" +
                "3. Each row shows where it stands in the open scene; hover it for why, select it for its parts. Assess checks again.\n" +
                "4. Build brings the scene in line with the sheet. Move what it placed and Build again: your adjustments are kept. " +
                "A row's right-click menu leads with its own fix; a locked step is left as it is.",
                HelpBoxMessageType.Info);
            _help.style.display = DisplayStyle.None;
            root.Add(_help);

            _source = new Label();
            _source.AddToClassList("manifest__source");
            root.Add(_source);
            _notices = new VisualElement();
            root.Add(_notices);
            _setup = new ScrollView { style = { maxHeight = 260, marginLeft = 4, marginRight = 4 } };
            root.Add(_setup);

            _empty = new Label("Pick a manifest above, or New... to make one from a plan (the JSON the sheet's reader writes, staged under Assets with its delivery).")
            {
                style = { whiteSpace = WhiteSpace.Normal, marginLeft = 8, marginTop = 12, marginRight = 8, opacity = 0.8f },
            };
            root.Add(_empty);

            _body = new VisualElement { style = { flexGrow = 1 } };
            root.Add(_body);

            var view = new Toolbar();
            view.Add(ViewMenu("Select", "Select rows in the table, or the rows' objects in the scene.",
                ("Rows with Issues", () => SelectLines(l => l.label.Length > 0 || !l.Quiet)),
                ("Rows to Build", () => SelectLines(l => l.status == ManifestStatus.Missing || l.status == ManifestStatus.Update)),
                ("All", () => SelectLines(_ => true)),
                ("None", () => _tree.ClearSelection()),
                (null, null),
                ("The Rows' Objects in the Scene", () => SelectInScene(Chosen(), frame: false))));
            view.Add(ViewMenu("Expand", "Expand every step, the steps with issues, or the selected ones.",
                ("All", () => _tree.ExpandAll()),
                ("Steps with Issues", () => Expand(l => !l.Quiet, true)),
                ("Selected", () => ExpandChosen(true))));
            view.Add(ViewMenu("Collapse", "Collapse every step, the steps without issues, or the selected ones.",
                ("All", () => _tree.CollapseAll()),
                ("Steps without Issues", () => Expand(l => l.Quiet, false)),
                ("Selected", () => ExpandChosen(false))));
            view.Add(ViewMenu("Hide", "Hide the steps with nothing to do: a view alone, Assess and Build still take every step.",
                ("Steps With Nothing to Do", () => IssuesOnly = true)));
            view.Add(ViewMenu("Show", "Show every step again.",
                ("Every Step", () => IssuesOnly = false)));
            view.Add(new VisualElement { style = { flexGrow = 1 } });
            _search = new ToolbarSearchField { tooltip = "Show the steps and rows whose words match." };
            _search.RegisterValueChangedCallback(_ => Refresh());
            view.Add(_search);
            _body.Add(view);

            var split = new TwoPaneSplitView(1, 190, TwoPaneSplitViewOrientation.Vertical) { viewDataKey = "unitytk.Manifest.detail" };
            split.style.flexGrow = 1;
            _tree = new MultiColumnTreeView
            {
                fixedItemHeight = 20,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                selectionType = SelectionType.Multiple,
                viewDataKey = "unitytk.Manifest.table",
            };
            _tree.style.minHeight = 60;
            StepColumn();
            StatusColumn();
            Column("Section", 140, l => l.section, quiet: true);
            DescriptionColumn();
            BehaviorsColumn();
            Column("Objects", 80, l => l.acts, quiet: true);
            _tree.selectionChanged += _ => ShowDetail();
            _tree.itemsChosen += _ => SelectInScene(Chosen(), frame: true);
            _tree.RegisterCallback<PointerUpEvent>(e =>
            {
                if (e.button != 1) return;
                WindowMenu menu = RowMenu();
                if (menu == null) return;
                menu.Show(rootVisualElement, e.position);
                e.StopPropagation();
            });
            split.Add(_tree);
            _detail = new ScrollView();
            _detail.AddToClassList("manifest__detail");
            split.Add(_detail);
            _body.Add(split);

            var footer = Row();
            footer.AddToClassList("manifest__foot");
            _summary = new Label { name = "summary" };
            _summary.AddToClassList("manifest__summary");
            footer.Add(_summary);
            var export = new Button { text = "Export", tooltip = "Copy what waits on the sheet's or the delivery's owner, or the table as shown." };
            export.clicked += () => ExportMenu(export);
            footer.Add(export);
            _assess = new Button(Assess) { name = "assess", text = "Assess", tooltip = "Check every row against the open scene: what is built, what a build would change, what waits on a person. Changes nothing." };
            footer.Add(_assess);
            _build = new Button(() => Build()) { name = "build", text = "Build" };
            _build.AddToClassList("manifest__build");
            footer.Add(_build);
            _body.Add(footer);

            root.schedule.Execute(Tick).Every(200);

            string guid = Remembered;
            if (_manifest == null && !string.IsNullOrEmpty(guid))
                _manifest = AssetDatabase.LoadAssetAtPath<Manifest>(AssetDatabase.GUIDToAssetPath(guid));
            _pick.SetValueWithoutNotify(_manifest);
            if (ShowSceneOwn(SceneManager.GetActiveScene())) return;  // the open scene's own, shown and assessed
            if (_manifest != null) Assess();
            else
            {
                Read();
                Refresh();
            }
        }

        static VisualElement Row() => new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.Center } };

        // The manifest's own actions, each behavior's switch for the whole manifest, and the window's options.
        WindowMenu TitleMenu()
        {
            var menu = new WindowMenu();
            bool has = _manifest != null;
            menu.Item("Build in a New Scene…", has ? AskReset : (Action)null, tooltip: "Build the manifest anew in a new scene.");
            string sync = has ? ManifestsActions.SyncCommand(_manifest) : "";
            menu.Item("Read the Sheet Again", sync.Length > 0 ? ReadAgain : (Action)null,
                      tooltip: "Read the manifest's sheet, its settings file and its delivery again and stage them here, as its command does in a terminal. Then Build.");
            menu.Item("Copy the Read Command", sync.Length > 0 ? () => EditorGUIUtility.systemCopyBuffer = sync : (Action)null,
                      tooltip: "The same, to run in a terminal yourself (python -m pythontk ops apply sheet.sync ...).");
            menu.Item("Open Scene", SceneAsset() != null ? OpenScene : (Action)null, tooltip: "Open the scene the manifest builds.");
            menu.Item("Show Asset", has ? () => EditorGUIUtility.PingObject(_manifest) : (Action)null, tooltip: "Show the manifest's asset in the Project window.");
            int edits = has ? ManifestsActions.Revert(_manifest, ctx: new OpContext(dryRun: true)) : 0;  // what Revert drops
            menu.Item("Revert All Edits", edits > 0 ? () => RevertEdits(null, null) : (Action)null,
                      tooltip: "Build every row as the sheet writes it again: drop each behavior given to or taken from an object here, " +
                               "and each object's own values.",
                      hint: edits > 0 ? $"{edits} edit{(edits == 1 ? "" : "s")}" : "none");
            // Each behavior the plan uses, ticked while the manifest builds it: a tick turns every row of it off, or on.
            foreach (string name in has ? UsedFamilies() : Enumerable.Empty<string>())
            {
                string behavior = name;
                menu.Toggle($"Build These Behaviors/{ManifestLines.BehaviorLabel(ManifestBehaviors.FamilyOf(behavior))}", !IsOff(behavior),
                            on => SetOff(behavior, !on), AboutFamily(behavior), IsOff(behavior) ? "off" : "");
            }
            menu.Toggle("Options/Assess as the Scene Changes", AutoAssess, on => AutoAssess = on);
            menu.Toggle("Options/Follow Scene Selection", FollowSelection, on => FollowSelection = on);
            menu.Toggle("Options/Long Names", LongNames, on => LongNames = on,
                        "Show each object's whole path: its parents faded, its own name bold.");
            return menu;
        }

        // A toolbar button opening a menu of entries (a null label: a separator).
        ToolbarButton ViewMenu(string text, string tooltip, params (string label, Action run)[] entries)
        {
            var button = new ToolbarButton { text = text + " ▾", tooltip = tooltip };
            button.AddToClassList("unitytk-menu-button");
            button.clicked += () =>
            {
                var menu = new WindowMenu();
                foreach (var (label, run) in entries)
                {
                    if (label == null) menu.Separator();
                    else menu.Item(label, run);
                }
                ShowUnder(menu, button);
            };
            return button;
        }

        // The Step column: the line's icon (its state's shape) and, for a step, its ID.
        void StepColumn()
        {
            var column = new Column { name = "step", title = "Step", width = 110 };
            column.makeCell = () =>
            {
                var cell = Row();
                cell.Add(Mark(""));
                cell.Add(new Label { name = "text", style = { unityTextAlign = TextAnchor.MiddleLeft, overflow = Overflow.Hidden, flexShrink = 1 } });
                return cell;
            };
            column.bindCell = (element, index) =>
            {
                ManifestLines.Line line = _tree.GetItemDataForIndex<ManifestLines.Line>(index);
                SetMark(element.Q<VectorIcon>("mark"), string.IsNullOrEmpty(line.status) ? "" : ManifestStatus.Level(line.status));
                Label text = element.Q<Label>("text");
                text.text = line.IsStep ? line.step : "";
                text.EnableInClassList("manifest-cell--step", line.IsStep);
                element.tooltip = line.tooltip;
                Tint(element, true);
                TreeBranches branches = TreeBranches.Of(element);
                if (branches != null) Branch(branches, index);
                // The row itself: an object's on a darker ground under its step's.
                for (VisualElement up = element.parent; up != null && up != _tree; up = up.parent)
                {
                    if (!up.ClassListContains("unity-multi-column-view__row-container")) continue;
                    up.EnableInClassList("manifest-row--object", !line.IsStep);
                    up.EnableInClassList("manifest-row--step", line.IsStep);
                    break;
                }
            };
            _tree.columns.Add(column);
        }

        // The row at index's place in the tree, as its branches draw it (TreeBranches.Layout): its level, whether it has
        // children and is open, and per cell left of its own whether that cell's line runs on down past it.
        void Branch(TreeBranches branches, int index)
        {
            var tree = _tree.viewController;
            int id = _tree.GetIdForIndex(index);
            int depth = Math.Max(tree.GetIndentationDepth(id), 0);
            var later = new bool[depth];
            int node = id;
            for (int c = depth - 1; c >= 0; c--)
            {
                int parent = tree.GetParentId(node);
                int last = node;
                foreach (int child in tree.GetChildrenIds(parent)) last = child;
                later[c] = last != node;
                node = parent;
            }
            branches.Set(depth, tree.HasChildren(id), _tree.IsExpanded(id), later);
        }

        // A column's cell on the darker ground the Step and Behaviors columns stand on.
        static void Tint(VisualElement cell, bool on)
        {
            if (cell.parent != null) cell.parent.EnableInClassList("manifest-cell--tint", on);
        }

        static Label Cell()
        {
            // Clipped short of the next column, with an ellipsis: a long cell never runs into its neighbour.
            var label = new Label();
            label.AddToClassList("manifest-cell");
            return label;
        }

        void Column(string title, float width, Func<ManifestLines.Line, string> text, bool stretch = false, bool quiet = false)
        {
            var column = new Column { name = title.ToLowerInvariant(), title = title, width = width, stretchable = stretch };
            column.makeCell = () => Cell();
            column.bindCell = (element, index) =>
            {
                ManifestLines.Line line = _tree.GetItemDataForIndex<ManifestLines.Line>(index);
                var label = (Label)element;
                label.text = text(line);
                label.tooltip = line.tooltip.Length > 0 ? line.tooltip : label.text;
                label.EnableInClassList("manifest-cell--quiet", quiet);
            };
            _tree.columns.Add(column);
        }

        // The Status column: the line's state in a few words, in its severity's colour.
        void StatusColumn()
        {
            var column = new Column { name = "status", title = "Status", width = 100 };
            column.makeCell = () => Cell();
            column.bindCell = (element, index) =>
            {
                ManifestLines.Line line = _tree.GetItemDataForIndex<ManifestLines.Line>(index);
                var label = (Label)element;
                label.text = line.label;
                label.tooltip = line.tooltip.Length > 0 ? line.tooltip : ManifestStatus.Help(line.status);
                string level = string.IsNullOrEmpty(line.status) ? "" : ManifestStatus.Level(line.status);
                foreach (string each in ManifestStatus.Levels) label.EnableInClassList("manifest-status--" + each, each == level);
            };
            _tree.columns.Add(column);
        }

        // The Description column: a step's name; under it each object's own name, set in, or (Long Names) its whole path
        // read by its end: its parents faded and cut from the start where the column is narrow, its own name bold.
        void DescriptionColumn()
        {
            var column = new Column { name = "description", title = "Description", width = 240, stretchable = true };
            column.makeCell = () =>
            {
                var cell = Row();
                cell.AddToClassList("manifest-name");
                var parents = new Label { name = "parents" };
                parents.AddToClassList("manifest-name__parents");
                cell.Add(parents);
                var own = new Label { name = "own" };
                own.AddToClassList("manifest-name__own");
                cell.Add(own);
                return cell;
            };
            column.bindCell = (element, index) =>
            {
                ManifestLines.Line line = _tree.GetItemDataForIndex<ManifestLines.Line>(index);
                int cut = line.IsStep || !LongNames ? -1 : line.target.LastIndexOf('|');
                var parents = element.Q<Label>("parents");
                parents.text = cut > 0 ? line.target.Substring(0, cut + 1) : "";
                parents.style.display = cut > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                var own = element.Q<Label>("own");
                own.text = line.description;
                own.EnableInClassList("manifest-name__own--bold", cut > 0);
                element.tooltip = line.IsStep ? (line.tooltip.Length > 0 ? line.tooltip : line.description) : line.target.Replace("|", " > ");
                element.EnableInClassList("manifest-cell--object", !line.IsStep);
            };
            _tree.columns.Add(column);
        }

        // The Behaviors column: a chip a behavior (a step's own on its line, each object's on its), in its state's
        // colour, with its own tooltip and its own menu; + gives the line one more. Every chip works alike.
        void BehaviorsColumn()
        {
            var column = new Column { name = "behaviors", title = "Behaviors", width = 340, stretchable = true };
            column.makeCell = () =>
            {
                var chips = new VisualElement();
                chips.AddToClassList("manifest-chips");
                return chips;
            };
            column.bindCell = (element, index) =>
            {
                ManifestLines.Line line = _tree.GetItemDataForIndex<ManifestLines.Line>(index);
                element.Clear();
                foreach (ManifestLines.Token token in line.tokens) element.Add(Chip(line, token));
                if (_manifest != null) element.Add(PlusChip(line));
                Tint(element, true);
            };
            _tree.columns.Add(column);
        }

        /// <summary>The chips of the Behaviors cell <paramref name="line"/> shows: a behavior each, then + (what a test reads).</summary>
        /// <param name="line">A line of the table.</param>
        public static IReadOnlyList<(string label, string level, string tooltip)> Chips(ManifestLines.Line line)
            => line.tokens.Select(t => (ChipText(t), ChipLevel(t), ChipTooltip(t))).ToList();

        // A chip's words: its behavior, and what tells its row from the behavior's others ('Yes' → B03_5).
        static string ChipText(ManifestLines.Token token)
            => (token.detail.Length > 0 ? token.label + " " + token.detail : token.label) + (token.variant.Length > 0 ? " · " + token.variant : "");

        // A chip's colour: this project's addition amber, the delivery's teal, else its state's.
        static string ChipLevel(ManifestLines.Token token)
            => (token.added || token.tuned) && !token.off && !IsProblem(token) ? "edit"
             : token.status == ManifestStatus.Upstream ? "upstream" : token.Level;

        // Whether a token's state is a warning or an error: shown so before an edit's amber.
        static bool IsProblem(ManifestLines.Token token) => token.Level == "warn" || token.Level == "error";

        static string ChipTooltip(ManifestLines.Token token)
            => (token.tooltip.Length > 0 ? token.tooltip : token.label) + "\n\nClick: turn it off or on here, edit what it built, its settings.";

        Label Chip(ManifestLines.Line line, ManifestLines.Token token)
        {
            string text = ChipText(token);
            var chip = new Label { enableRichText = true, text = token.off ? $"<s>{text}</s>" : text, tooltip = ChipTooltip(token) };
            chip.AddToClassList("manifest-chip");
            string level = ChipLevel(token);
            if (level.Length > 0) chip.AddToClassList("manifest-chip--" + level);
            chip.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                ShowUnder(ChipMenu(line, token), chip);
                e.StopPropagation();
            });
            return chip;
        }

        Label PlusChip(ManifestLines.Line line)
        {
            var plus = new Label("+")
            {
                tooltip = line.IsStep ? $"Give step {line.step} a behavior the sheet does not (one that names no object: going on, a note)."
                                      : $"Give '{line.description}' a behavior the sheet does not.",
            };
            plus.AddToClassList("manifest-chip");
            plus.AddToClassList("manifest-chip--plus");
            plus.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                ShowUnder(AddMenu(line), plus);
                e.StopPropagation();
            });
            return plus;
        }

        // One behavior's menu, the same for every chip: off or on here, off or on everywhere, its editors.
        WindowMenu ChipMenu(ManifestLines.Line line, ManifestLines.Token token)
        {
            var menu = new WindowMenu();
            // Its words head its section (a slash would start another).
            string label = ChipText(token).Replace('/', '∕'), where = line.IsStep ? line.step : line.description, behavior = token.behavior;
            if (token.status == ManifestStatus.Upstream)
                menu.Item($"{label}/In the Delivery", null, hint: "made in the delivery",
                          tooltip: "The delivery's own build made it on this object: left to it. Turn it off where the delivery is made to build it here.");
            else if (token.offEverywhere)
                menu.Item($"{label}/Turn On Everywhere", () => SetOff(behavior, false), hint: "off for every row",
                          tooltip: "Build every row of it in this manifest again.");
            else
            {
                bool on = !token.off;
                menu.Item(on ? $"{label}/Turn Off for '{where}'" : $"{label}/Turn On for '{where}'",
                          () => Assign(line, behavior, !on, token.added ? -1 : token.source),
                          hint: token.added ? "added here" : token.off ? "the sheet gives it" : "",
                          tooltip: on ? "Leave it out here: kept in the manifest, marked, undone in one click." : "Build it here again, as the sheet says.");
                menu.Item($"{label}/Turn Off Everywhere", () => SetOff(behavior, true), tooltip: "Leave every row of it out of this manifest's builds.");
            }
            foreach (Object part in Parts(line, token).Take(6))
            {
                Object each = part;
                menu.Item($"Edit/{label}", () => EditorUtility.OpenPropertyEditor(each), hint: PartName(each), tooltip: "Its own editor, in a window of its own.");
            }
            ManifestBehaviorAttribute mark = ManifestBehaviors.Mark(behavior);
            bool kit = ManifestBehaviors.SettingsOf(behavior) != null;
            int row = token.added ? -1 : token.source;
            bool settable = (mark?.Fields?.Length ?? 0) > 0 || (kit && Offered(line, token).Length > 0);
            if (settable && token.status != ManifestStatus.Upstream)
                menu.Item($"Settings/For '{where}'…", () => OpenFor(line, token),
                          hint: _manifest != null && _manifest.TuneOf(line.step, line.IsStep ? "" : line.target, behavior, row) != null ? "its own" : "",
                          tooltip: "This object's own values for it: its row's fields over the sheet's, its settings over its kit's (kept as an edit).");
            if (kit && Offered(line, token, wholeRow: true).Length > 0 && token.status != ManifestStatus.Upstream)
                menu.Item("Settings/For Its Row…", () => OpenFor(line, token, wholeRow: true),
                          hint: _manifest != null && _manifest.TuneOf(line.step, "", behavior, row) != null ? "its own" : "",
                          tooltip: "Its row's own values, which every object of the row builds by (its take's window and speed): over its kit's, kept as an edit.");
            if (kit)
                menu.Item($"Settings/For Every Row…", () => OpenBehaviorSettings(behavior), tooltip: "How every row of it is built: its kit's settings, kept in the manifest.");
            // What the sheet's row gives it, a field a line: read here, changed in the sheet.
            foreach (string field in token.fields.Split('\n'))
            {
                int colon = field.IndexOf(": ", StringComparison.Ordinal);
                if (colon > 0) menu.Item($"From the Sheet/{field.Substring(0, colon)}", null, hint: field.Substring(colon + 2),
                                         tooltip: "The sheet's row gives it this: change it in the sheet.");
            }
            return menu;
        }

        // What a line can be given: an object, what its node alone can build; a step, the behaviors that name no object.
        WindowMenu AddMenu(ManifestLines.Line line)
        {
            var menu = new WindowMenu();
            var wanted = new Dictionary<string, bool>();
            AddEntries(menu, line, new HashSet<string>(line.tokens.Select(t => t.behavior)), behavior => wanted[behavior] = true,
                       behavior => wanted.Remove(behavior));
            if (menu.Empty) menu.Item("Add/Nothing more to give", null);
            menu.Closed += () =>
            {
                if (wanted.Count > 0) Assign(line, wanted);
            };
            return menu;
        }

        // The Add section of a line's menu: a box each behavior it can be given beyond have (an object: what its node alone can
        // build; a step: the behaviors that name no object), each tick saying so to add or drop; "Build first" where the
        // delivery is not placed yet.
        void AddEntries(WindowMenu menu, ManifestLines.Line line, HashSet<string> have, Action<string> add, Action<string> drop)
        {
            IEnumerable<string> offer;
            if (line.IsStep) offer = ManifestBehaviors.Names().Where(n => ManifestBehaviors.Mark(n)?.NamesObjects == false);
            else
            {
                ManifestScene scene = ManifestsActions.SceneOf(_manifest);
                if (scene == null)
                {
                    menu.Item("Add/Build first", null, tooltip: "The delivery is not in the open scene: Build, then add.");
                    return;
                }
                offer = ManifestsActions.Assignable(scene, line.target);
            }
            foreach (string name in offer.Where(n => !have.Contains(n) && !IsOff(n)).ToList())
            {
                string behavior = name;
                menu.Toggle($"Add/{ManifestLines.BehaviorLabel(behavior)}", false, on =>
                {
                    if (on) add(behavior);
                    else drop(behavior);
                }, ManifestBehaviors.Mark(behavior)?.About ?? "");
            }
        }

        // -- marks: a state's shape in its colour, the one the table, the notices and the detail share --

        // The mark of a severity (ManifestStatus.Levels): its drawn icon (IconOf, the Shot Manifest's) in its
        // colour (the style sheet's manifest-status--<level>); empty for none. Text keeps its glyph (ManifestLines.Glyph).
        static VectorIcon Mark(string level)
        {
            var mark = new VectorIcon { name = "mark" };
            mark.style.width = 14;
            mark.style.height = 14;
            mark.AddToClassList("manifest-mark");
            SetMark(mark, level);
            return mark;
        }

        static void SetMark(VectorIcon mark, string level)
        {
            mark.icon = IconOf(level);
            foreach (string each in ManifestStatus.Levels) mark.EnableInClassList("manifest-status--" + each, each == level);
        }

        /// <summary>
        /// The icon a severity's mark draws (<see cref="VectorIcons"/>), the one the Shot Manifest draws for the
        /// same state (a check for a row in order): null for none.
        /// </summary>
        /// <param name="level">A severity (<see cref="ManifestStatus.Levels"/>), or empty.</param>
        internal static string IconOf(string level)
        {
            switch (level)
            {
                case "error": return "error";
                case "warn": return "warning";
                case "info": return "info";
                case "locked": return "lock";
                case "edit": return "edit";
                case "valid": return "check";
                case "off": return "remove";
                default: return null;
            }
        }

        // -- refresh ----------------------------------------------------------------------------

        /// <summary>Show the manifest again: its plan's lines, the last assessment's marks, the view's filter.</summary>
        public void Refresh()
        {
            _lines = ManifestLines.Of(_plan, _assessed, _manifest);

            HashSet<(string, int)> chosen = new HashSet<(string, int)>(Chosen().Select(l => (l.step, l.row)));
            string words = _search?.value ?? "";
            bool issuesOnly = IssuesOnly && _assessed != null;
            _shown.Clear();
            _ids.Clear();
            _owners.Clear();
            var items = new List<TreeViewItemData<ManifestLines.Line>>();
            var again = new List<int>();
            int id = 0, hidden = 0;
            foreach (ManifestLines.Line step in _lines)
            {
                if (issuesOnly && step.Quiet)
                {
                    hidden++;
                    continue;
                }
                if (!step.Matches(words)) continue;
                var children = new List<TreeViewItemData<ManifestLines.Line>>();
                int stepId = ++id;
                Track(step, stepId, chosen, again);
                foreach (ManifestLines.Line row in step.rows)
                {
                    children.Add(new TreeViewItemData<ManifestLines.Line>(++id, row));
                    Track(row, id, chosen, again);
                }
                items.Add(new TreeViewItemData<ManifestLines.Line>(stepId, step, children));
                _shown.Add(step);
            }
            if (_tree == null) return;  // not laid out yet: what it shows is ready for when it is
            bool has = _manifest != null;
            _empty.style.display = has ? DisplayStyle.None : DisplayStyle.Flex;
            _body.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            _tree.SetRootItems(items);
            _tree.Rebuild();
            _tree.SetSelectionByIdWithoutNotify(again);

            ShowSource();
            ShowNotices();
            ShowSetup();
            ShowDetail();
            ShowFooter(hidden);
        }

        // A line's tree ID kept, its selection carried over a refresh, and its parts' objects known as its own.
        void Track(ManifestLines.Line line, int id, HashSet<(string, int)> chosen, List<int> again)
        {
            _ids[line] = id;
            if (chosen.Contains((line.step, line.row))) again.Add(id);
            foreach (Object part in Parts(line))
            {
                GameObject node = part is Component component ? component.gameObject : part as GameObject;
                if (node != null && !_owners.ContainsKey(node.GetInstanceID())) _owners[node.GetInstanceID()] = id;
            }
        }

        void ShowSource()
        {
            if (_plan == null || _plan.source == null)
            {
                _source.text = "";
                return;
            }
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(_plan.source.sheet)) parts.Add($"Sheet {_plan.source.sheet}");
            if (!string.IsNullOrEmpty(_plan.source.delivery)) parts.Add($"delivery {_plan.source.delivery}");
            if (!string.IsNullOrEmpty(_manifest.scene)) parts.Add($"scene {_manifest.scene}");
            _source.text = string.Join("  •  ", parts);
        }

        void ShowNotices()
        {
            _notices.Clear();
            if (_manifest == null) return;
            if (_plan == null)
            {
                // The refusal names what to do; the toolbar's Setup sets the Plan. A notice offers only what the toolbar has not.
                Notice("error", _refusal, ManifestsActions.SyncCommand(_manifest).Length > 0 ? "Read the Sheet Again" : null, ReadAgain);
                return;
            }
            if (_manifest.delivery == null)
                Notice("warn", "The manifest names no delivery: set its Delivery in Setup.", null, null);
            else if (ManifestsActions.Placed(_manifest.delivery) == null)
            {
                if (SceneAsset() != null && SceneManager.GetActiveScene().path != _manifest.scene)
                    Notice("info", $"Its scene is {_manifest.scene}; the open scene holds none of it.", "Open Its Scene", OpenScene);
                else
                    Notice("info", "Nothing of it is in the open scene yet: Build places its delivery here and builds every row.", null, null);
            }
            if (_changed.Length > 0)
            {
                Notice("warn", $"{char.ToUpper(_changed[0])}{_changed.Substring(1)}: read it again, then Build.",
                       ManifestsActions.SyncCommand(_manifest).Length > 0 ? "Read the Sheet Again" : null, ReadAgain);
            }
            else if (_unchecked.Length > 0)
                Notice("info", $"{char.ToUpper(_unchecked[0])}{_unchecked.Substring(1)}.", null, null);
            if (_assessed != null && _assessed.planIssues.Count > 0)
                Notice("warn", $"{_assessed.planIssues.Count} issue(s) of the sheet as a whole: {_assessed.planIssues[0]}", "Copy",
                       () => EditorGUIUtility.systemCopyBuffer = string.Join("\n", _assessed.planIssues));
            foreach (Issue said in _said)
                Notice(ManifestStatus.Level(said), said.message, "Dismiss", () =>
                {
                    _said.Remove(said);
                    ShowNotices();
                });
        }

        void Notice(string level, string text, string action, Action run)
        {
            var row = Row();
            row.style.marginLeft = 4;
            row.style.marginRight = 4;
            row.style.marginTop = 2;
            row.Add(Mark(level));
            row.Add(new Label(text) { style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal } });
            if (action != null) row.Add(new Button(run) { text = action });
            _notices.Add(row);
        }

        // The manifest's own fields, shown or hidden: the toolbar's Setup.
        void ToggleSetup()
        {
            _setupOpen = !_setupOpen;
            Refresh();
        }

        // The manifest's own fields, and each of its kits' settings: edited here, kept in the manifest's asset.
        void ShowSetup()
        {
            _setup.style.display = _setupOpen && _manifest != null ? DisplayStyle.Flex : DisplayStyle.None;
            IReadOnlyList<IManifestKit> kits = _plan != null ? ManifestBehaviors.Kits(_plan) : ManifestBehaviors.AllKits();
            string shown = !_setupOpen || _manifest == null ? "" : _manifest.GetInstanceID() + ":" + string.Join(",",
                kits.Select(k => k.Settings == null ? "" : k.Settings.Name + (_manifest.Settings(k.Settings) != null ? "+" : "-")))
                + "|" + string.Join(",", _manifest.off) + "|" + (_plan != null ? _plan.steps.Count : 0);
            if (shown == _setupShown) return;  // the same inspectors: left as they are, a field being edited keeps its focus
            _setupShown = shown;
            _setup.Clear();
            if (shown.Length == 0) return;
            _setup.Add(new InspectorElement(_manifest));
            foreach (IManifestKit kit in kits)
            {
                Type type = kit.Settings;
                if (type == null) continue;
                string name = ObjectNames.NicifyVariableName(type.Name);
                ScriptableObject own = _manifest.Settings(type);
                if (own == null)
                {
                    _setup.Add(new Button(() =>
                    {
                        Run($"Keep {name}", run => ManifestsActions.AddSettings(_manifest, type, run));
                        Refresh();
                    })
                    { text = $"Customize {name}", tooltip = $"Give this manifest its own {name} (it builds by the defaults until then)." });
                    continue;
                }
                var fold = new Foldout { text = name, value = true };
                fold.Add(new InspectorElement(own));
                _setup.Add(fold);
            }
            // Each behavior the plan uses: on or off for the whole manifest, what it does, and its own settings.
            var behaviors = new Foldout { text = "Behaviors", value = true, tooltip = "Each behavior the plan uses: off, a build leaves every row of it out." };
            foreach (string name in UsedFamilies())
            {
                string behavior = name;
                var line = Row();
                var on = new Toggle { value = !IsOff(behavior), tooltip = "On: every row of it is built. Off: a build leaves them out, and a step that had one is built anew without it." };
                on.RegisterValueChangedCallback(e => SetOff(behavior, !e.newValue));
                line.Add(on);
                line.Add(new Label(ManifestLines.BehaviorLabel(ManifestBehaviors.FamilyOf(behavior))) { style = { width = 110, unityFontStyleAndWeight = FontStyle.Bold } });
                var about = new Label(AboutFamily(behavior));
                about.AddToClassList("manifest-behavior__about");
                about.style.flexGrow = 1;
                about.style.flexShrink = 1;
                line.Add(about);
                // A family's settings are its member's that has them (Fade · out's, Fade · in having none).
                string settled = ManifestBehaviors.Family(behavior).FirstOrDefault(b => ManifestBehaviors.SettingsOf(b) != null);
                if (settled != null)
                    line.Add(new Button(() => OpenBehaviorSettings(settled)) { text = "Settings...", tooltip = "How every row of it is built: its own settings, kept in the manifest." });
                behaviors.Add(line);
            }
            if (behaviors.childCount > 0) _setup.Add(behaviors);
        }

        void ShowFooter(int hidden)
        {
            if (_manifest == null || _plan == null)
            {
                _summary.text = "";
                _build.text = "Build";
                _build.SetEnabled(false);
                _assess.SetEnabled(_manifest != null);
                return;
            }
            _assess.SetEnabled(true);
            if (_assessed == null)
            {
                _summary.text = $"{_plan.steps.Count} steps: not assessed yet.";
                _build.text = "Build";
                _build.SetEnabled(false);
                return;
            }
            ManifestAssessment.BuildPlan plan = _assessed.Plan();
            _summary.text = _assessed.Summary() + (hidden > 0 ? $"  •  {hidden} step(s) with nothing to do hidden" : "");
            _build.text = plan.Any ? $"Build ({plan.Count})" : "Build";
            _build.SetEnabled(plan.Any && _manifest.delivery != null);
            var why = new StringBuilder(plan.Text());
            if (!_assessed.shared.made)
                foreach (Change change in _assessed.shared.changes.Take(6))
                    why.Append('\n').Append("• ").Append(change.kind).Append(' ').Append(change.target)
                       .Append(string.IsNullOrEmpty(change.action) ? "" : $": {change.action}");
            _build.tooltip = why.ToString();
        }

        // -- the detail: the selected line's words, issues, parts and what a build would do --------

        /// <summary>The lines selected in the table.</summary>
        public List<ManifestLines.Line> Chosen()
            => _tree == null ? new List<ManifestLines.Line>() : _tree.selectedItems.OfType<ManifestLines.Line>().ToList();

        /// <summary>Select the line of step <paramref name="step"/> (its row <paramref name="row"/>; -1: the step's own) in the table.</summary>
        /// <param name="step">A step's ID.</param>
        /// <param name="row">The row's place in its step, or -1.</param>
        /// <returns>Whether the table shows that line.</returns>
        public bool Choose(string step, int row = -1)
        {
            if (_tree == null) return false;
            foreach (var pair in _ids)
                if (pair.Key.step == step && pair.Key.row == row)
                {
                    _tree.SetSelectionById(pair.Value);
                    _tree.ScrollToItemById(pair.Value);
                    return true;
                }
            return false;
        }

        /// <summary>What <paramref name="line"/> holds in the scene, by the shown assessment: a row's parts, a step's own.</summary>
        /// <param name="line">A line of the table.</param>
        public IReadOnlyList<Object> Parts(ManifestLines.Line line)
        {
            ManifestAssessment.Step step = _assessed?.Find(line.step);
            if (step == null) return Array.Empty<Object>();
            if (line.IsStep) return step.parts.Where(p => p != null).ToList();
            var parts = new List<Object>();
            foreach (ManifestLines.Token token in line.tokens)
                foreach (Object part in Parts(line, token))
                    if (!parts.Contains(part)) parts.Add(part);
            return parts;
        }

        /// <summary>
        /// What <paramref name="token"/> (a behavior of <paramref name="line"/>) holds in the scene: its
        /// row's parts, those on the line's object where the row names several.
        /// </summary>
        /// <param name="line">A line of the table.</param>
        /// <param name="token">One of its behaviors.</param>
        public IReadOnlyList<Object> Parts(ManifestLines.Line line, ManifestLines.Token token)
        {
            ManifestAssessment.Step step = _assessed?.Find(line.step);
            if (step == null || token.row < 0 || token.row >= step.rows.Count) return Array.Empty<Object>();
            ManifestAssessment.Row row = step.rows[token.row];
            List<Object> all = row.parts.Where(p => p != null).ToList();
            if (line.IsStep || row.targets.Count < 2) return all;
            string leaf = ManifestLines.Leaf(line.target);
            List<Object> own = all.Where(p => On(p, leaf)).ToList();
            return own.Count > 0 ? own : all;
        }

        // Whether a part is on the object named leaf, or under it.
        static bool On(Object part, string leaf)
        {
            Transform at = part is Component component ? component.transform : part is GameObject node ? node.transform : null;
            for (; at != null; at = at.parent)
                if (at.name == leaf) return true;
            return false;
        }

        // The own parts of the step a line is in (its object in the sequence, its page, its sound).
        IReadOnlyList<Object> StepParts(ManifestLines.Line line)
        {
            ManifestAssessment.Step step = _assessed?.Find(line.step);
            return step == null ? (IReadOnlyList<Object>)Array.Empty<Object>() : step.parts.Where(p => p != null).ToList();
        }

        // What a line offers to edit: its parts, else (a row that only sets its step up) its step's own.
        IReadOnlyList<Object> Editable(ManifestLines.Line line)
        {
            IReadOnlyList<Object> parts = Parts(line);
            return parts.Count > 0 || line.IsStep ? parts : StepParts(line);
        }

        void ShowDetail()
        {
            if (_detail == null) return;
            _detail.Clear();
            if (_plan == null) return;
            List<ManifestLines.Line> chosen = Chosen();
            if (chosen.Count == 0)
            {
                Overview();
                return;
            }
            ManifestLines.Line line = chosen[0];
            ManifestAssessment.Step step = _assessed?.Find(line.step);

            string head = line.IsStep ? $"{line.step}  •  {line.description}" : $"{line.step}  •  {line.description}";
            if (line.label.Length > 0) head += $"  —  {line.label}";
            if (chosen.Count > 1) head += $"   (+{chosen.Count - 1} more selected)";
            var top = Row();
            top.Add(Mark(string.IsNullOrEmpty(line.status) ? "" : ManifestStatus.Level(line.status)));
            var said = new Label(head);
            said.AddToClassList("manifest-detail__head");
            top.Add(said);
            _detail.Add(top);

            if (line.IsStep)
            {
                var planned = _plan.steps.FirstOrDefault(s => s.id == line.step);
                if (planned != null && planned.fields != null)
                    foreach (var pair in planned.fields)
                        Said(ObjectNames.NicifyVariableName(pair.Key), pair.Value as string);
                if (step != null)
                    foreach (var (status, text) in ManifestLines.StepIssues(step)) Issue(line, null, status, text);
            }
            else Said(null, line.target.Replace("|", " > "));

            // Each behavior of the line: what the sheet says, where it stands, what it built, and its switches.
            foreach (ManifestLines.Token token in line.tokens) BehaviorCard(line, token, step);

            if (line.IsStep)
            {
                IReadOnlyList<Object> parts = Parts(line);
                if (parts.Count > 0) Heading("The step's own parts");
                foreach (Object part in parts) PartRow(_detail, part);
                if (step != null && step.changes.Count > 0 && !step.made) Would(_detail, step.changes);
            }
        }

        // One behavior of the selected line, as a card: its name and state, the sheet's words, its issues, what it
        // built (each part selected or edited), what a build would change, and its switches.
        void BehaviorCard(ManifestLines.Line line, ManifestLines.Token token, ManifestAssessment.Step step)
        {
            ManifestAssessment.Row row = step != null && token.row >= 0 && token.row < step.rows.Count ? step.rows[token.row] : null;
            string level = token.Level;
            var card = new VisualElement();
            card.AddToClassList("manifest-behavior");
            if (level.Length > 0) card.AddToClassList("manifest-behavior--" + level);

            var top = Row();
            if (level.Length > 0) top.Add(Mark(level));
            var name = new Label(token.label + (token.off ? "   <i>off</i>" : token.added ? "   <i>added here</i>"
                                    : ManifestStatus.Label(token.status).Length > 0 ? $"   <i>{ManifestStatus.Label(token.status)}</i>" : ""))
            { enableRichText = true };
            name.AddToClassList("manifest-behavior__name");
            top.Add(name);
            string behavior = token.behavior;
            if (!line.IsStep && !token.offEverywhere)
                top.Add(new Button(() => Assign(line, behavior, token.off))
                {
                    text = token.off ? "Turn On Here" : token.added ? "Remove" : "Turn Off Here",
                    tooltip = token.off ? "Build it for this object again, as the sheet says."
                        : token.added ? "Take away what this project added."
                        : "Leave it out for this object in this step: an edit the manifest keeps (the sheet is not changed).",
                });
            if (token.offEverywhere)
                top.Add(new Button(() => SetOff(behavior, false)) { text = "Turn On", tooltip = "Its behavior is off for the whole manifest: turn every row of it on again." });
            if (ManifestBehaviors.SettingsOf(behavior) != null)
                top.Add(new Button(() => OpenBehaviorSettings(behavior)) { text = "Settings...", tooltip = "How every row of it is built: its own settings, kept in the manifest." });
            card.Add(top);

            if (!string.IsNullOrWhiteSpace(token.text) && !token.added)
            {
                var sheet = new Label(token.text) { selection = { isSelectable = true } };
                sheet.AddToClassList("manifest-detail__text");
                card.Add(sheet);
            }
            string about = ManifestBehaviors.Mark(behavior)?.About;
            if (!string.IsNullOrEmpty(about))
            {
                var what = new Label(about);
                what.AddToClassList("manifest-behavior__about");
                card.Add(what);
            }
            if (row != null)
                foreach (var (status, text) in ManifestLines.RowIssues(row)) Issue(line, card, status, text, token.text);
            foreach (Object part in Parts(line, token)) PartRow(card, part);
            if (row != null && row.changes.Count > 0 && !row.made && row.state == ManifestAssessment.State.Ready) Would(card, row.changes);
            _detail.Add(card);
        }

        // One issue: its severity's icon, what it says, and Copy where its fix is another's.
        void Issue(ManifestLines.Line line, VisualElement into, string status, string text, string sheet = null)
        {
            var issue = Row();
            issue.style.marginTop = 2;
            issue.Add(Mark(ManifestStatus.Level(status)));
            issue.Add(new Label(text) { style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal }, selection = { isSelectable = true } });
            if (ManifestStatus.NeedsYou(status))
                issue.Add(new Button(() => EditorGUIUtility.systemCopyBuffer = $"{line.step}: \"{sheet ?? line.description}\": {text}")
                { text = "Copy", tooltip = "Copy it for whoever owns the sheet or the delivery: its fix is theirs." });
            (into ?? _detail).Add(issue);
        }

        // What a build would change, a line each (the first dozen).
        void Would(VisualElement into, List<Change> changes)
        {
            var heading = new Label("Build would");
            heading.AddToClassList("manifest-detail__heading");
            into.Add(heading);
            foreach (Change change in changes.Take(12))
                into.Add(new Label($"• {change.kind} {change.target}{(string.IsNullOrEmpty(change.action) ? "" : $": {change.action}")}")
                { style = { whiteSpace = WhiteSpace.Normal, marginLeft = 4 } });
            if (changes.Count > 12) into.Add(new Label($"  and {changes.Count - 12} more") { style = { marginLeft = 4, opacity = 0.7f } });
        }

        // Nothing selected: what a build would do as a whole, and what the steps share.
        void Overview()
        {
            if (_assessed == null) return;
            _detail.Add(new Label(_assessed.Plan().Text()) { style = { whiteSpace = WhiteSpace.Normal, unityFontStyleAndWeight = FontStyle.Bold } });
            IEnumerable<Object> shared = _assessed.shared.parts.Where(p => p != null);
            if (shared.Any()) Heading("What the steps share");
            foreach (Object part in shared) PartRow(_detail, part);
            foreach (string note in _assessed.shared.notes)
                _detail.Add(new Label($"▲ {note}") { style = { whiteSpace = WhiteSpace.Normal } });
        }

        void Heading(string text)
        {
            var heading = new Label(text);
            heading.AddToClassList("manifest-detail__heading");
            _detail.Add(heading);
        }

        void Said(string what, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var said = new Label(what == null ? text : $"{what}: {text}") { selection = { isSelectable = true } };
            said.AddToClassList("manifest-detail__text");
            _detail.Add(said);
        }

        /// <summary>A part's name as the window lists it: its kind, and the object it is on.</summary>
        /// <param name="part">A part a row holds.</param>
        public static string PartName(Object part)
            => part is Component component
                ? $"{ObjectNames.NicifyVariableName(component.GetType().Name)}  ({component.gameObject.name})"
                : part != null ? $"{ObjectNames.NicifyVariableName(part.GetType().Name)}  ({part.name})" : "";

        // One part: its kind and object, Select (in the scene) and Edit (its own editor, beside this window).
        void PartRow(VisualElement into, Object part)
        {
            var row = Row();
            row.AddToClassList("manifest-part");
            row.Add(new Image { image = AssetPreview.GetMiniThumbnail(part), style = { width = 16, height = 16, marginRight = 4, flexShrink = 0 } });
            row.Add(new Label(PartName(part)) { style = { flexGrow = 1, flexShrink = 1, overflow = Overflow.Hidden } });
            row.Add(new Button(() => SelectObjects(new[] { part }, frame: true)) { text = "Select", tooltip = "Select it in the scene and frame it." });
            row.Add(new Button(() => EditorUtility.OpenPropertyEditor(part)) { text = "Edit", tooltip = "Open its own editor in a window of its own." });
            // What a person adjusted on it in the scene: each field with what the build gave it, and Revert, which puts those back.
            List<ManifestAdjustment> own = _assessed == null || !(part is Component) ? null : _assessed.adjusted.FindAll(each => each.part == part);
            if (own != null && own.Count > 0)
                row.Add(new Button(() => RevertAdjusted((Component)part))
                {
                    text = "Revert",
                    tooltip = "Give it what the build gave it again: " + string.Join("; ", own.ConvertAll(each => each.Text())),
                });
            into.Add(row);
            if (own == null) return;
            foreach (ManifestAdjustment each in own)
            {
                var said = new Label($"Adjusted in the scene: {each.Text()}") { selection = { isSelectable = true } };
                said.AddToClassList("manifest-detail__text");
                into.Add(said);
            }
        }

        /// <summary>Give what a person adjusted on <paramref name="part"/> in the scene what the build gave it again, as one undo; then assess.</summary>
        /// <param name="part">A part a row holds.</param>
        public void RevertAdjusted(Component part)
        {
            if (_manifest == null || part == null) return;
            Run("Revert Adjustments", run => ManifestsActions.RevertAdjustments(_manifest, part, run));
            Assess();
        }

        // -- selection, both ways -----------------------------------------------------------------

        // The lines' objects selected in the scene: their parts, else (nothing built) the nodes they name.
        void SelectInScene(List<ManifestLines.Line> lines, bool frame)
        {
            var objects = new List<Object>();
            ManifestScene scene = _manifest == null ? null : ManifestsActions.SceneOf(_manifest);
            foreach (ManifestLines.Line line in lines)
            {
                IReadOnlyList<Object> parts = Parts(line);
                if (parts.Count > 0)
                {
                    objects.AddRange(parts);
                    continue;
                }
                if (scene == null || line.IsStep) continue;
                Transform node = scene.Node(line.target);
                if (node != null) objects.Add(node.gameObject);
            }
            SelectObjects(objects, frame);
        }

        static void SelectObjects(IEnumerable<Object> parts, bool frame)
        {
            Object[] nodes = parts.Select(p => p is Component component ? component.gameObject : p).Where(p => p != null).Distinct().ToArray();
            if (nodes.Length == 0) return;
            Selection.objects = nodes;
            EditorGUIUtility.PingObject(nodes[0]);
            SceneView view = SceneView.lastActiveSceneView;
            if (frame && view != null) view.FrameSelected();
        }

        // The scene's selection changed: the row that holds the object is selected here.
        void OnSceneSelection()
        {
            if (_tree == null || !FollowSelection || _busy || focusedWindow == this) return;
            GameObject active = Selection.activeGameObject;
            if (active == null || !_owners.TryGetValue(active.GetInstanceID(), out int id)) return;
            _tree.SetSelectionById(id);
            _tree.ScrollToItemById(id);
        }

        void SelectLines(Func<ManifestLines.Line, bool> which)
        {
            var ids = new List<int>();
            foreach (var pair in _ids)
                if (which(pair.Key)) ids.Add(pair.Value);
            _tree.SetSelectionById(ids);
        }

        void Expand(Func<ManifestLines.Line, bool> which, bool open)
        {
            foreach (ManifestLines.Line step in _shown)
            {
                if (!which(step) || !_ids.TryGetValue(step, out int id)) continue;
                if (open) _tree.ExpandItem(id);
                else _tree.CollapseItem(id);
            }
        }

        void ExpandChosen(bool open)
        {
            foreach (ManifestLines.Line line in Chosen())
            {
                ManifestLines.Line step = line.IsStep ? line : _shown.FirstOrDefault(s => s.step == line.step);
                if (step == null || !_ids.TryGetValue(step, out int id)) continue;
                if (open) _tree.ExpandItem(id);
                else _tree.CollapseItem(id);
            }
        }

        // -- a row's menu: its own fix first ---------------------------------------------------------

        // The selected lines' menu; null with nothing selected.
        WindowMenu RowMenu()
        {
            List<ManifestLines.Line> chosen = Chosen();
            if (chosen.Count == 0 || _assessed == null) return null;
            var menu = new WindowMenu();
            List<string> steps = chosen.Select(l => l.step).Distinct().ToList();
            List<string> toBuild = steps.Where(id => _assessed.Find(id) != null && _assessed.Find(id).NeedsBuild).ToList();
            if (toBuild.Count > 0)
                menu.Item(toBuild.Count == 1 ? $"Build {toBuild[0]}" : $"Build {toBuild.Count} Steps", () => Build(toBuild),
                          tooltip: "Build only these steps (and what the steps share).");
            string fix = Fix(chosen);
            if (fix.Length > 0)
                menu.Item("Copy Fix", () => EditorGUIUtility.systemCopyBuffer = fix, tooltip: "What waits on the sheet's or the delivery's owner, as text for them.",
                          hint: "for the sheet's owner");
            menu.Separator();
            string them = chosen.Count == 1 ? $"'{(chosen[0].IsStep ? chosen[0].step : chosen[0].description)}'" : $"{chosen.Count} Rows";
            menu.Item($"Select {them} in Scene", () => SelectInScene(chosen, frame: false), tooltip: "Select what the rows act on in the scene (and show it in the Hierarchy).");
            menu.Item($"Frame {them} in Scene", () => SelectInScene(chosen, frame: true), tooltip: "Select it and frame it in the Scene view.", hint: "double-click");
            if (chosen.Count == 1)
            {
                // The line's behaviors: a tick gives or takes one, then each one's editors (what it built, its settings).
                menu.Separator();
                BehaviorEntries(chosen[0], menu);
            }
            else
                foreach (Object part in chosen.SelectMany(Editable).Distinct().Take(10))
                {
                    Object each = part;
                    menu.Item($"Edit/{PartName(each).Replace("/", "∕")}", () => EditorUtility.OpenPropertyEditor(each));
                }
            menu.Separator();
            bool anyLocked = steps.Any(id => _manifest.locked.Contains(id)), anyOpen = steps.Any(id => !_manifest.locked.Contains(id));
            string these = steps.Count == 1 ? $"'{steps[0]}'" : $"{steps.Count} Steps";
            if (anyOpen) menu.Item($"Lock {these}", () => SetLocked(steps, true), tooltip: "Locked: Build leaves the step as it is. Unlock to revise it.");
            if (anyLocked) menu.Item($"Unlock {these}", () => SetLocked(steps, false), tooltip: "Let Build bring the step in line with the sheet again.");
            menu.Item(chosen.Count == 1 ? $"Copy {them} to Clipboard" : $"Copy {chosen.Count} Names to Clipboard",
                      () => EditorGUIUtility.systemCopyBuffer = string.Join("\n", chosen.Select(l => l.IsStep ? l.step : l.description)),
                      tooltip: "The selected steps' IDs and objects' names, a line each.");
            return menu;
        }

        // What the chosen lines wait on a person for, as text for the sheet's or delivery's owner; empty when nothing.
        string Fix(List<ManifestLines.Line> chosen)
        {
            var text = new StringBuilder();
            foreach (ManifestLines.Line line in chosen)
            {
                ManifestAssessment.Step step = _assessed.Find(line.step);
                if (step == null) continue;
                IEnumerable<ManifestAssessment.Row> rows = line.IsStep
                    ? step.rows
                    : line.tokens.Where(k => k.row >= 0 && k.row < step.rows.Count).Select(k => step.rows[k.row]).Distinct();
                foreach (ManifestAssessment.Row row in rows)
                {
                    if (!ManifestStatus.NeedsYou(row.Status)) continue;
                    text.Append(row.step).Append(" row ").Append(row.source + 1).Append(": \"").Append(row.text).Append("\"\n");
                    foreach (string problem in row.problems) text.Append("  ").Append(problem).Append('\n');
                    if (row.state == ManifestAssessment.State.Unsupported)
                        text.Append("  ").Append(row.behavior).Append(": ").Append(ManifestStatus.Help(row.Status)).Append('\n');
                }
            }
            return text.ToString();
        }

        void ExportMenu(VisualElement under)
        {
            var menu = new WindowMenu();
            string report = ManifestLines.IssuesReport(_plan, _assessed, _manifest);
            menu.Item("Copy Issues", report.Length > 0 ? () => EditorGUIUtility.systemCopyBuffer = report : (Action)null,
                      tooltip: "What waits on the sheet's or the delivery's owner, and what this project changed, for them.", hint: "Markdown");
            menu.Item("Copy Table", () => EditorGUIUtility.systemCopyBuffer = Table(), tooltip: "The table as shown.", hint: "tab separated");
            // Above the button: it stands at the window's foot.
            menu.Show(rootVisualElement, new Vector2(under.worldBound.xMin, under.worldBound.yMin - 48f));
        }

        /// <summary>The table as shown, tab separated: a line a step and a line each of its objects, with its status and behaviors.</summary>
        public string Table()
        {
            var text = new StringBuilder("Step\tStatus\tSection\tDescription\tBehaviors\tObjects\n");
            foreach (ManifestLines.Line step in _shown)
            {
                text.Append(string.Join("\t", step.step, step.label, step.section, step.description, step.behaviors, step.acts)).Append('\n');
                foreach (ManifestLines.Line row in step.rows)
                    text.Append(string.Join("\t", "", row.label, "", row.description, row.behaviors, "")).Append('\n');
            }
            return text.ToString();
        }

        // -- the manifest's scene ---------------------------------------------------------------------

        SceneAsset SceneAsset()
            => _manifest == null || string.IsNullOrEmpty(_manifest.scene) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(_manifest.scene);

        void OpenScene()
        {
            if (SceneAsset() == null || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(_manifest.scene);
            Assess();
        }
    }
}
