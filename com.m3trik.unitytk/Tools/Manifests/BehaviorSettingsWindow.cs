using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Tools.Manifests
{
    /// <summary>
    /// One behavior's own settings, in a small window beside the manifest's: its switch for the whole
    /// manifest, what it does, and the fields of its kit's settings it says are its own
    /// (<see cref="ManifestBehaviorAttribute.Settings"/>), edited in place in the manifest's asset. What
    /// a behavior built for one object is edited on that object's own components
    /// (<see cref="ManifestWindow"/> opens those); this is how every row of it is built.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's property editor (<c>EditorUtility.OpenPropertyEditor</c>) opens a whole
    /// object, here a kit's every setting, where a behavior owns a few fields of it; a
    /// <c>SettingsProvider</c> is project-wide, where these are one manifest's. So the fields are
    /// bound here, by name, through <c>SerializedObject</c>: undo and prefab-style saving are Unity's.
    /// </remarks>
    public sealed class BehaviorSettingsWindow : EditorWindow
    {
        // Kept across a script reload, so an open window still shows its behavior's settings after one.
        [SerializeField] ManifestWindow _owner;
        [SerializeField] string _behavior = "";
        [SerializeField] ScriptableObject _settings;
        [SerializeField] string[] _fields = new string[0];
        // Opened for one object of a step (OpenFor): its own values over its kit's settings and the sheet's row.
        [SerializeField] string _step = "";
        [SerializeField] string _target = "";
        [SerializeField] bool _forObject;
        [SerializeField] string[] _rowFields = new string[0];
        [SerializeField] string[] _rowValues = new string[0];
        [SerializeField] int _row = -1;
        [SerializeField] bool _wholeRow;  // the row's own values as a whole (a take's), opened from one of its objects
        ScriptableObject _own;  // the object's settings as a working copy, made again from the manifest after a reload (FillForObject)
        IVisualElementScheduledItem _save;  // the object's settings kept once a person pauses: one edit per pause, not one per keystroke

        // How long a pause in changing the object's settings is, in milliseconds, before they are kept (an assessment each time).
        const long SaveDelay = 400;

        /// <summary>
        /// Open the values of <paramref name="behavior"/> for one object of a step: its row's fields
        /// (<paramref name="rowFields"/>, the sheet's <paramref name="sheetValues"/> beside them) and the behavior's
        /// settings (<paramref name="settingsFields"/>, as its row of the sheet builds them until changed here), then the
        /// object's own (an edit: marked, reverted with the object's edits).
        /// </summary>
        /// <param name="owner">The manifest window it belongs to.</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="step">The step's ID.</param>
        /// <param name="target">The object, as the sheet's rows name it; empty: the step's own row.</param>
        /// <param name="settingsFields">The settings that are the behavior's own (of its kit's, <see cref="ManifestBehaviors.SettingsOf"/>).</param>
        /// <param name="rowFields">The row's fields a person may set.</param>
        /// <param name="sheetValues">What the sheet gives each of them (empty: nothing, a row added here).</param>
        /// <param name="row">Which of the step's rows of the sheet it is (its place among them); -1: a row added here.</param>
        /// <param name="wholeRow">They are the row's as a whole (<paramref name="target"/> empty), opened from one of its objects:
        /// every object of it builds by them (<see cref="ManifestBehaviorAttribute.RowSettings"/>).</param>
        public static BehaviorSettingsWindow OpenFor(ManifestWindow owner, string behavior, string step, string target,
                                                     string[] settingsFields, string[] rowFields, string[] sheetValues, int row = -1,
                                                     bool wholeRow = false)
        {
            var window = CreateInstance<BehaviorSettingsWindow>();
            window._owner = owner;
            window._behavior = behavior ?? "";
            window._step = step ?? "";
            window._target = target ?? "";
            window._forObject = true;
            window._fields = settingsFields ?? new string[0];
            window._rowFields = rowFields ?? new string[0];
            window._rowValues = sheetValues ?? new string[0];
            window._row = row;
            window._wholeRow = wholeRow;
            string where = wholeRow ? $"its row in {window._step}"
                         : window._target.Length == 0 ? window._step : window._target.Substring(window._target.LastIndexOf('|') + 1);
            window.titleContent = new GUIContent($"{ManifestLines.BehaviorLabel(window._behavior)}: {where}");
            window.minSize = new Vector2(380, 160);
            window.ShowUtility();  // its CreateGUI fills it, once
            return window;
        }

        void OnDestroy()
        {
            if (_save != null)
            {
                _save.Pause();
                Keep();  // what was changed just before it closed
            }
            if (_own != null) DestroyImmediate(_own);
        }

        // Keep the object's settings as the working copy holds them (an edit of the manifest, then an assessment).
        void Keep()
        {
            _save = null;
            if (_owner != null && _own != null) _owner.SetTunes(_step, _target, _behavior, _own, _fields, _row);
        }

        // One object's values: its row's fields over the sheet's, then the behavior's settings over its kit's and its row's.
        void FillForObject(VisualElement root)
        {
            Manifest manifest = _owner != null ? _owner.Manifest : null;
            if (manifest == null) return;
            string where = _wholeRow ? $"its row in {_step}"
                         : _target.Length == 0 ? $"step {_step}" : $"'{_target.Substring(_target.LastIndexOf('|') + 1)}' in {_step}";
            root.Add(new Label($"{ManifestLines.BehaviorLabel(_behavior)} for {where}") { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13 } });
            root.Add(new Label(_wholeRow ? "What is set here is the row's own, which every object of it builds by: kept in the manifest, marked as an edit, and reverted with it."
                                         : "What is set here is this object's own: kept in the manifest, marked as an edit, and reverted with it.")
            { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.7f, marginBottom = 6 } });
            Manifest.Tune tune = manifest.TuneOf(_step, _target, _behavior, _row);
            System.Type type = ManifestBehaviors.SettingsOf(_behavior);
            if (_own == null && type != null) _own = ManifestsActions.OwnSettings(manifest, type, _step, _target, _row, _behavior);
            if (_rowFields.Length > 0)
            {
                root.Add(new Label("Its row") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 } });
                for (int i = 0; i < _rowFields.Length; i++)
                {
                    string field = _rowFields[i], sheet = i < _rowValues.Length ? _rowValues[i] ?? "" : "";
                    string mine = tune == null ? null : tune.Field(field);
                    var text = new TextField(ManifestBehaviors.Mark(_behavior)?.FieldLabel(field) ?? ManifestBehaviorAttribute.DefaultLabel(field)) { value = mine ?? sheet, isDelayed = true,
                        tooltip = sheet.Length > 0 ? $"The sheet: {sheet}" : "The sheet gives none: a row added here needs it." };
                    text.RegisterValueChangedCallback(e => _owner.SetRowField(_step, _target, _behavior, field, e.newValue == sheet ? null : e.newValue, _row));
                    root.Add(text);
                }
            }
            if (_own != null && _fields.Length > 0)
            {
                root.Add(new Label("Its settings") { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 6 } });
                var serialized = new SerializedObject(_own);
                foreach (string field in _fields)
                {
                    SerializedProperty property = serialized.FindProperty(field);
                    if (property != null) root.Add(new PropertyField(property));
                }
                root.Bind(serialized);
                // Kept once a person pauses (a drag or typing changes it at every step), and as the window closes.
                root.TrackSerializedObjectValue(serialized, _ =>
                {
                    if (_save == null) _save = root.schedule.Execute(Keep);
                    _save.ExecuteLater(SaveDelay);
                });
            }
            var back = new Button(() => { _owner.RevertTune(_step, _target, _behavior, _row); Close(); })
            {
                text = "Back to the Sheet",
                tooltip = _wholeRow ? "Drop the row's own values for this behavior: built by its kit's settings and the sheet's row again."
                                    : "Drop this object's own values for this behavior: built by its kit's settings and the sheet's row again.",
                style = { marginTop = 8 },
            };
            back.SetEnabled(tune != null);
            root.Add(back);
        }

        /// <summary>Open the settings of <paramref name="behavior"/> kept in <paramref name="settings"/>.</summary>
        /// <param name="owner">The manifest window it belongs to (its switch is that window's).</param>
        /// <param name="behavior">The behavior's name.</param>
        /// <param name="settings">Its kit's settings, as the manifest keeps them.</param>
        /// <param name="fields">The fields of them that are the behavior's own.</param>
        public static BehaviorSettingsWindow Open(ManifestWindow owner, string behavior, ScriptableObject settings, string[] fields)
        {
            var window = CreateInstance<BehaviorSettingsWindow>();
            window._owner = owner;
            window._behavior = behavior ?? "";
            window._settings = settings;
            window._fields = fields ?? new string[0];
            window.titleContent = new GUIContent($"{ManifestLines.BehaviorLabel(window._behavior)} Settings");
            window.minSize = new Vector2(360, 120);
            window.ShowUtility();  // its CreateGUI fills it, once
            return window;
        }

        void CreateGUI() => Fill();

        // The window's content, in an element of its own under the root: filled again, the old one goes whole, and with it
        // what tracked its settings (a tracker on the root itself outlived a Clear, and two kept every change).
        void Fill()
        {
            rootVisualElement.Clear();
            EditorLook.Apply(rootVisualElement);
            var root = new VisualElement();
            rootVisualElement.Add(root);
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = root.style.paddingBottom = 6;
            if (_forObject)
            {
                FillForObject(root);
                return;
            }
            if (_settings == null) return;

            var name = new Label(ManifestLines.BehaviorLabel(_behavior)) { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13 } };
            root.Add(name);
            string about = ManifestBehaviors.Mark(_behavior)?.About;
            if (!string.IsNullOrEmpty(about)) root.Add(new Label(about) { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.7f, marginBottom = 6 } });

            if (_owner != null && _owner.Manifest != null)
            {
                var on = new Toggle("On for this manifest")
                {
                    value = !_owner.IsOff(_behavior),
                    tooltip = "Off: a build leaves every row of it out, and a step that had one is built anew without it.",
                };
                on.RegisterValueChangedCallback(e => _owner.SetOff(_behavior, !e.newValue));
                root.Add(on);
            }

            var serialized = new SerializedObject(_settings);
            foreach (string field in _fields)
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property != null) root.Add(new PropertyField(property));
            }
            root.Bind(serialized);
            root.Add(new Label("Kept in the manifest: every row of this behavior is built by them at the next Build.")
            {
                style = { whiteSpace = WhiteSpace.Normal, opacity = 0.6f, marginTop = 6 },
            });
        }
    }
}
