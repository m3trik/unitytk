using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// A panel in the world that tells the user what to do: a heading, a text, a button per
    /// choice, a key pad and a list, on a UI Toolkit document (its layout <c>Placard.uxml</c>:
    /// elements named <c>heading</c>, <c>text</c>, <c>choices</c>, <c>entry</c>, <c>keys</c> and
    /// <c>list</c>; the last three optional). <see cref="Show"/> puts content on it and, while
    /// <see cref="follow"/> is on, brings it up in front of the user's eyes, so a viewpoint that
    /// moves them never leaves it behind; a button pressed raises <see cref="onChosen"/> with its
    /// label, and the key pad's Enter with what was keyed in, after which a reply the content
    /// gives that label is shown in place of its text (a wrong answer's "Try again"). The list
    /// (<see cref="ShowList"/>) stays as pages change, its items ticked one by one. Content may
    /// name what the user is to act on (<see cref="KeepClear"/>): the placard comes up on whichever
    /// side of the eyes covers the fewest of them. Dragged anywhere but a button (<see cref="movable"/>,
    /// the windows' shared frame) it swings around the eyes as the pointer moves sideways and rises or
    /// lowers as it moves up or down (<see cref="Drag"/>), and later content keeps that spot, turned with
    /// the eyes; a bare arrow in its corner (<see cref="collapsible"/>) folds it to its heading
    /// (<see cref="Collapse"/>). It is
    /// pressed as any world-space panel is: a pointer through the camera, a headset's
    /// controller through the XR Interaction Toolkit's UI Toolkit support.
    /// </summary>
    /// <remarks>
    /// Prior art: UI Toolkit's world-space panels (Unity 6.2 and newer) are adopted as the
    /// surface, with a UXML layout and a USS theme a person edits in UI Builder, as a desktop
    /// toolkit's panels are a designer's layout with a stylesheet; they take a mouse through the
    /// camera and a controller through the XR Interaction Toolkit (3.2 and newer) alike, where
    /// a uGUI canvas would be a second UI system beside the editor's and a panel of 3D buttons
    /// would leave text and layout to be built by hand. The toolkit's lazy-follow components
    /// move a panel continuously and need its package; this moves it only as content is shown.
    /// A project's own layout keeps the behaviour: a subclass draws the content its way
    /// (<see cref="Render"/>), or a layout of its own names its elements as the package's does.
    /// </remarks>
    [AddComponentMenu("unitytk/Panels/Placard")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public class Placard : MonoBehaviour
    {
        /// <summary>The class an element of a choice carries (each choice's button).</summary>
        public const string ChoiceClass = "placard-choice";
        /// <summary>The class the panel carries while it shows nothing.</summary>
        public const string EmptyClass = "placard--empty";
        /// <summary>The class each key pad button carries (a key's, Clear's, Enter's).</summary>
        public const string KeyClass = "placard-key";
        /// <summary>The class each list item carries.</summary>
        public const string ItemClass = "placard-item";
        /// <summary>The class a ticked list item carries besides.</summary>
        public const string TickedClass = "placard-item--ticked";
        /// <summary>The class the panel carries while folded to its heading.</summary>
        public const string CollapsedClass = "placard--collapsed";
        /// <summary>The class the key pad's Enter button carries besides <see cref="KeyClass"/>.</summary>
        public const string EnterClass = "placard-key--enter";
        /// <summary>The <see cref="pixelsPerMetre"/> of a window just made: the package's world panel settings' (a pixel is a millimetre).</summary>
        public const float DefaultPixelsPerMetre = 1000f;

        [Tooltip("The user's eyes, which the placard comes up in front of (the main camera when empty).")]
        public Transform eyes;

        [Tooltip("Bring the placard up in front of the eyes each time it shows something.")]
        public bool follow = true;

        [Tooltip("Where it comes up from the eyes, in metres: right, up and ahead, turned with the way they face (mirrored left to right where that covers less of what the content keeps clear of).")]
        public Vector3 offset = DefaultOffset;

        /// <summary>The <see cref="offset"/> of a placard just made: left of the eyes, a little below, 0.8 m ahead.</summary>
        public static Vector3 DefaultOffset => new Vector3(-0.4f, -0.2f, 0.8f);

        [Tooltip("Dragged anywhere but a button, it swings around the eyes (sideways) and rises or lowers (up or down); later content keeps the spot. The pointer shows it can move.")]
        public bool movable = true;

        [Tooltip("Show an arrow in its corner that folds the placard to its heading, and unfolds it again.")]
        public bool collapsible = true;

        [Tooltip("How far a drag may swing it from where it comes up, in degrees either way.")]
        public float maxSwing = 150f;

        [Tooltip("How far a drag may raise or lower it, in metres either way.")]
        public float maxLift = 0.5f;

        [Tooltip("Panel pixels to the metre: its document's panel settings' (the package's world panel: 1000).")]
        public float pixelsPerMetre = DefaultPixelsPerMetre;

        [Tooltip("The layer it stands on, where it stands on the default one (a picture's camera leaves its own window layer out); empty: as it is.")]
        public string windowLayer = WindowFrame.DefaultLayer;

        [Tooltip("What its fold arrow shows and says.")]
        public WindowFold fold = new WindowFold("Fold to its heading");

        [Tooltip("The key pad's button that sends what was keyed in.")]
        public string enterLabel = "Enter";

        [Tooltip("The key pad's button that clears what was keyed in.")]
        public string clearLabel = "Clear";

        [Tooltip("Raised with a choice's label as its button is pressed, and with what was keyed in as Enter is.")]
        public UnityEvent<string> onChosen = new UnityEvent<string>();

        readonly List<string> _choices = new List<string>();
        readonly Dictionary<string, string> _replies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly List<string> _items = new List<string>();
        readonly HashSet<string> _ticked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly List<Transform> _keepClear = new List<Transform>();
        Vector3? _spot;  // the shown content's own spot (its page's), in place of offset
        bool _stale = true;

        /// <summary>What the content shown asks the user to act on, which the placard keeps from covering.</summary>
        public IReadOnlyList<Transform> KeepClear => _keepClear;

        readonly WindowFrame _frame = new WindowFrame();

        /// <summary>How far the user swung it around the eyes, in degrees (right positive).</summary>
        public float Swing => _frame.Swing;

        /// <summary>How far the user raised it, in metres.</summary>
        public float Lift => _frame.Lift;

        /// <summary>Whether it is folded to its heading.</summary>
        public bool Collapsed => _frame.Folded;

        /// <summary>
        /// Move it as it is dragged <paramref name="pixels"/> across the panel since the press (right and
        /// down positive, as the panel's pointer moves): swung around the eyes so the point grabbed
        /// follows the pointer, and raised or lowered; the spot is kept for later content.
        /// </summary>
        /// <param name="pixels">The drag, in panel pixels.</param>
        public void Drag(Vector2 pixels)
        {
            Transform from = Eyes;
            float radius = from == null ? offset.magnitude : Mathf.Max(0.1f, Vector3.Distance(from.position, transform.position));
            _frame.Drag(pixels, radius, maxSwing, maxLift, pixelsPerMetre);
            Place();
        }

        /// <summary>Fold it to its heading (<paramref name="on"/>), or unfold it; it stays so for later content.</summary>
        /// <param name="on">Fold it.</param>
        public void Collapse(bool on)
        {
            _frame.Folded = on;
            Refresh();
        }

        /// <summary>Put it back where it comes up by itself (no swing, no lift).</summary>
        public void ResetSpot()
        {
            _frame.Swing = _frame.Lift = 0f;
            Place();
        }

        /// <summary>The heading shown; empty when it shows nothing.</summary>
        public string Heading { get; private set; } = "";

        /// <summary>The text shown.</summary>
        public string Text { get; private set; } = "";

        /// <summary>The choices offered, in order.</summary>
        public IReadOnlyList<string> Choices => _choices;

        /// <summary>What put the content on it (a page); null when it shows nothing, or something unnamed does.</summary>
        public UnityEngine.Object ShownBy { get; private set; }

        /// <summary>The keys its key pad offers, one a character (none: no key pad).</summary>
        public string Keys { get; private set; } = "";

        /// <summary>What has been keyed in since the key pad showed, or was cleared or sent.</summary>
        public string Entry { get; private set; } = "";

        /// <summary>The items of the list shown, in order.</summary>
        public IReadOnlyList<string> Items => _items;

        /// <summary>What put the list on it; null when it shows none, or something unnamed does.</summary>
        public UnityEngine.Object ListedBy { get; private set; }

        /// <summary>Whether the list's item <paramref name="item"/> is ticked (ignoring case).</summary>
        /// <param name="item">An item.</param>
        public bool IsTicked(string item) => item != null && _ticked.Contains(item);

        /// <summary>The user's eyes: <see cref="eyes"/>, else the main camera's; null when there is none.</summary>
        public Transform Eyes => Viewer.Eyes(eyes);

        /// <summary>The first switched-on placard in the loaded scenes; null when there is none.</summary>
        public static Placard InScene() => SceneObjects.FirstEnabled<Placard>();

        /// <summary>
        /// Show <paramref name="heading"/>, <paramref name="text"/> and a button per choice,
        /// and bring the placard up in front of the eyes while <see cref="follow"/> is on.
        /// </summary>
        /// <param name="heading">The heading (a step's title).</param>
        /// <param name="text">What the user is told.</param>
        /// <param name="choices">The choices offered; null: none.</param>
        /// <param name="by">What puts it there (a page), so that only it takes it away again.</param>
        /// <param name="keys">The keys of a key pad, one a character (<c>0123456789</c>); null or empty: none.</param>
        /// <param name="replies">What to say once a label is chosen or keyed in that leaves this content
        /// shown (a wrong answer's "Try again"), by label; null: nothing.</param>
        /// <param name="keepClear">What the user is to act on, which the placard keeps from covering; null: nothing.</param>
        /// <param name="spot">Where it comes up for this content, as <see cref="offset"/> is (a page placed clear
        /// of the scene from where its step stands the user); null: <see cref="offset"/>, mirrored where that covers less.</param>
        public void Show(string heading, string text, IReadOnlyList<string> choices, UnityEngine.Object by = null,
                         string keys = null, IReadOnlyDictionary<string, string> replies = null,
                         IReadOnlyList<Transform> keepClear = null, Vector3? spot = null)
        {
            _spot = spot;
            _keepClear.Clear();
            if (keepClear != null)
                foreach (Transform each in keepClear)
                    if (each != null) _keepClear.Add(each);
            Heading = heading ?? "";
            Text = text ?? "";
            _choices.Clear();
            if (choices != null)
                foreach (string choice in choices)
                    if (!string.IsNullOrEmpty(choice)) _choices.Add(choice);
            Keys = keys ?? "";
            Entry = "";
            _replies.Clear();
            if (replies != null)
                foreach (KeyValuePair<string, string> reply in replies)
                    if (!string.IsNullOrEmpty(reply.Key)) _replies[reply.Key] = reply.Value ?? "";
            ShownBy = by;
            Refresh();
            if (follow) Place();
        }

        /// <summary>Show nothing, unless <paramref name="by"/> names something other than what put the content there.</summary>
        /// <param name="by">What takes it away (a page); null: whatever put it there.</param>
        public void Clear(UnityEngine.Object by = null)
        {
            if (by != null && by != ShownBy) return;
            Heading = Text = Keys = Entry = "";
            _choices.Clear();
            _replies.Clear();
            ShownBy = null;
            Refresh();
        }

        /// <summary>
        /// Show the list <paramref name="items"/>, those in <paramref name="ticked"/> ticked, below
        /// whatever page is shown; it stays as pages change, until it is cleared or another replaces it.
        /// </summary>
        /// <param name="items">The items, in order.</param>
        /// <param name="ticked">The items ticked; null: none.</param>
        /// <param name="by">What puts it there (a list), so that only it takes it away again.</param>
        public void ShowList(IReadOnlyList<string> items, IEnumerable<string> ticked = null, UnityEngine.Object by = null)
        {
            _items.Clear();
            if (items != null)
                foreach (string item in items)
                    if (!string.IsNullOrEmpty(item)) _items.Add(item);
            _ticked.Clear();
            if (ticked != null)
                foreach (string item in ticked)
                    if (!string.IsNullOrEmpty(item)) _ticked.Add(item);
            ListedBy = by;
            Refresh();
        }

        /// <summary>Show no list, unless <paramref name="by"/> names something other than what put it there.</summary>
        /// <param name="by">What takes it away (a list); null: whatever put it there.</param>
        public void ClearList(UnityEngine.Object by = null)
        {
            if (by != null && by != ListedBy) return;
            _items.Clear();
            _ticked.Clear();
            ListedBy = null;
            Refresh();
        }

        /// <summary>Key in <paramref name="key"/>, as its button does: one of <see cref="Keys"/>.</summary>
        /// <param name="key">A key, one character.</param>
        /// <returns>Whether the key pad offers it.</returns>
        public bool Press(char key)
        {
            if (Keys.IndexOf(key) < 0) return false;
            Entry += key;
            Refresh();
            return true;
        }

        /// <summary>Clear what was keyed in, as the Clear button does.</summary>
        public void Erase()
        {
            Entry = "";
            Refresh();
        }

        /// <summary>
        /// Send what was keyed in, as the Enter button does: <see cref="onChosen"/> is raised with it,
        /// and the entry cleared; a reply the content gives it is shown while the content stays.
        /// </summary>
        /// <returns>Whether anything was keyed in to send.</returns>
        public bool Enter()
        {
            if (Keys.Length == 0 || Entry.Length == 0) return false;
            string value = Entry;
            UnityEngine.Object by = ShownBy;
            Entry = "";
            Refresh();
            onChosen.Invoke(value);
            Reply(value, by);
            return true;
        }

        /// <summary>Take the choice labelled <paramref name="label"/>, as its button does: <see cref="onChosen"/> is raised when it is offered.</summary>
        /// <param name="label">The choice's label (ignoring case).</param>
        /// <returns>Whether it is offered.</returns>
        public bool Choose(string label)
        {
            foreach (string choice in _choices)
            {
                if (!string.Equals(choice, label, StringComparison.OrdinalIgnoreCase)) continue;
                UnityEngine.Object by = ShownBy;
                onChosen.Invoke(choice);
                Reply(choice, by);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Say <paramref name="text"/> in place of the text while what it shows stays, until the content
        /// changes: a reply to something the user did (a part selected without the tool it needs).
        /// </summary>
        /// <param name="text">What to say; empty says nothing.</param>
        public void Tell(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            Text = text;
            Refresh();
        }

        // A reply to label, shown in place of the text while the content that offered it stays.
        void Reply(string label, UnityEngine.Object by)
        {
            if (ShownBy != by || !_replies.TryGetValue(label, out string reply)) return;
            Text = reply;
            Refresh();
        }

        /// <summary>
        /// Bring the placard up at <see cref="offset"/> from the eyes, turned with their heading, facing
        /// them; mirrored left to right where that covers fewer of what it keeps clear of.
        /// </summary>
        public void Place()
        {
            Transform from = Eyes;
            if (from == null) return;
            Quaternion turn = WindowFrame.Heading(from);
            Vector3 side = _spot ?? offset;
            var mirrored = new Vector3(-side.x, side.y, side.z);
            bool moved = Swing != 0f || Lift != 0f;  // a spot the user chose is theirs
            if (!moved && _spot == null && _keepClear.Count > 0
                && Covered(from.position, from.position + turn * mirrored) < Covered(from.position, from.position + turn * side))
                side = mirrored;
            Pose pose = _frame.Beside(from.position, from.position + turn * side, turn);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            Physics.SyncTransforms();  // its collider where it now is, for a click this very frame
        }

        // How many of what it keeps clear of the placard would hide from the eyes, standing at at.
        int Covered(Vector3 eyes, Vector3 at)
        {
            Quaternion facing = WindowFrame.Toward(eyes, at, Quaternion.identity);
            Vector2 half = HalfSize();
            int covered = 0;
            foreach (Transform each in _keepClear)
                if (each != null && PanelCover.Hides(eyes, at, facing, half, SceneBounds.Of(each, includeDisabled: true).center)) covered++;
            return covered;
        }

        /// <summary>Half its width and height, in metres: its collider's (the document's rect), else a typical placard's.</summary>
        public Vector2 HalfExtents => HalfSize();

        /// <summary>Half a typical placard's width and height, in metres (what <see cref="HalfExtents"/> is before its document lays out).</summary>
        public static Vector2 DefaultHalfExtents => new Vector2(0.3f, 0.2f);

        // Half its width and height in metres: its collider's (the document's rect), else a typical placard's.
        Vector2 HalfSize()
        {
            if (TryGetComponent(out BoxCollider box))
            {
                Vector3 size = Vector3.Scale(box.size, transform.lossyScale);
                if (size.x > 0f && size.y > 0f) return new Vector2(size.x, size.y) * 0.5f;
            }
            return DefaultHalfExtents;
        }

        /// <summary>Unity's: the document builds its tree anew as it is switched on, so the content is put on it again.</summary>
        protected virtual void OnEnable()
        {
            _stale = true;
            WindowFrame.PutOnLayer(gameObject, windowLayer);
        }

        /// <summary>Unity's: the content put on a tree that was not there yet.</summary>
        protected virtual void LateUpdate()
        {
            if (_stale) Refresh();
        }

        // Put the content on the document's tree; stale until Render finds its elements there.
        void Refresh()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _stale = root == null || !Render(root);
        }

        /// <summary>
        /// Put what it shows (<see cref="Heading"/>, <see cref="Text"/>, <see cref="Choices"/>, <see cref="Keys"/> and
        /// <see cref="Entry"/>, <see cref="Items"/>, <see cref="Collapsed"/>) on the document's tree <paramref name="root"/>:
        /// the package's layout's elements by name, its buttons wired to <see cref="Choose"/>, <see cref="Press"/>,
        /// <see cref="Erase"/> and <see cref="Enter"/>. A subclass draws its own layout here (calling this for the
        /// package's part where it keeps it); called as the content changes, and until it returns true.
        /// </summary>
        /// <param name="root">The document's root element.</param>
        /// <returns>Whether the tree had what it draws on (false: it is tried again next frame).</returns>
        protected virtual bool Render(VisualElement root)
        {
            Label heading = root.Q<Label>("heading");
            Label text = root.Q<Label>("text");
            VisualElement choices = root.Q<VisualElement>("choices");
            if (heading == null || text == null || choices == null) return false;
            heading.text = Heading;
            text.text = Text;
            text.style.display = Text.Length > 0 && !Collapsed ? DisplayStyle.Flex : DisplayStyle.None;
            _frame.Bind(root.Q("placard") ?? root, movable, collapsible, Drag, Collapse, fold);
            choices.Clear();
            for (int i = 0; i < _choices.Count; i++)
            {
                string label = _choices[i];
                // Named by its place, not its label: a label is any text (spaces, a second choice of the same words).
                var button = new Button(() => Choose(label)) { text = label, name = $"choice-{i}" };
                button.AddToClassList(ChoiceClass);
                choices.Add(button);
            }
            RenderKeys(root);
            RenderList(root);
            choices.style.display = Collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            if (Collapsed)
                foreach (string part in new[] { "entry", "keys", "list" })
                    if (root.Q(part) is VisualElement folded) folded.style.display = DisplayStyle.None;
            root.EnableInClassList(CollapsedClass, Collapsed);
            root.EnableInClassList(EmptyClass, Heading.Length == 0 && Text.Length == 0 && _choices.Count == 0
                                               && Keys.Length == 0 && _items.Count == 0);
            return true;
        }

        void RenderKeys(VisualElement root)
        {
            Label entry = root.Q<Label>("entry");
            VisualElement keys = root.Q<VisualElement>("keys");
            if (entry != null)
            {
                entry.text = Entry;
                entry.style.display = Keys.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (keys == null) return;
            keys.Clear();
            keys.style.display = Keys.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (char key in Keys)
            {
                char pressed = key;
                keys.Add(Key($"key-{key}", key.ToString(), () => Press(pressed)));
            }
            if (Keys.Length == 0) return;
            keys.Add(Key("key-clear", clearLabel, Erase));
            Button enter = Key("key-enter", enterLabel, () => Enter());
            enter.AddToClassList(EnterClass);
            keys.Add(enter);
        }

        static Button Key(string name, string label, Action pressed)
        {
            var button = new Button(pressed) { text = label, name = name };
            button.AddToClassList(KeyClass);
            return button;
        }

        void RenderList(VisualElement root)
        {
            VisualElement list = root.Q<VisualElement>("list");
            if (list == null) return;
            list.Clear();
            list.style.display = _items.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (string item in _items)
            {
                var row = new VisualElement { name = item };
                row.AddToClassList(ItemClass);
                row.EnableInClassList(TickedClass, _ticked.Contains(item));
                var box = new VisualElement();
                box.AddToClassList(ItemClass + "-box");
                row.Add(box);
                row.Add(new Label(item));
                list.Add(row);
            }
        }
    }
}
