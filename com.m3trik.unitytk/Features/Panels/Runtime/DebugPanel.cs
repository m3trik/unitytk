using System;
using System.Collections.Generic;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Kernel;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// A debug panel for trying a scene, on a desktop and in a headset alike: a world-space card
    /// that rides beside the user's eyes, a status line, and a button per command something put on it
    /// (<see cref="Add"/>: a sequence's Skip, Back, Restart, Autopilot; a <see cref="DebugCommand"/>'s), the surface
    /// a scene's commands go on (<see cref="ICommandSurface"/>, read off it beside them).
    /// The arrow in its corner folds its commands away and brings them back; dragged anywhere but a
    /// button it moves as every window does (the windows' shared frame). A reviewer points at it as at a placard: the
    /// scene's pointer and controllers press it, with no input of its own. A release build shows it only where
    /// <see cref="releaseBuilds"/> says so (the editor and a development build always do).
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's Rendering Debugger is a render pipeline's (no scene commands, no world
    /// space); IMGUI's OnGUI draws only on a screen, so a headset never sees it; asset-store
    /// consoles (in-game consoles, runtime inspectors) are screen overlays and a dependency. A
    /// world-space UI Toolkit document, as the placard is, works for both with what the package has.
    /// </remarks>
    [AddComponentMenu("unitytk/Panels/Debug Panel")]
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class DebugPanel : MonoBehaviour, ICommandSurface
    {
        /// <summary>The USS class of its card.</summary>
        public const string CardClass = "debug";
        /// <summary>The USS class of its title.</summary>
        public const string TabClass = "debug-tab";
        /// <summary>The USS class of its status line.</summary>
        public const string StatusClass = "debug-status";
        /// <summary>The USS class of a command's button.</summary>
        public const string CommandClass = "debug-command";

        [Tooltip("The user's eyes (the main camera when empty).")]
        public Transform eyes;

        [Tooltip("Where it rides from the eyes, in metres: right, up and ahead, turned with them.")]
        public Vector3 offset = new Vector3(-0.35f, 0.12f, 0.9f);

        [Tooltip("Its title.")]
        public string title = "Debug";

        [Tooltip("Show its commands; off, only its title shows (its arrow opens them again).")]
        public bool open = true;

        [Tooltip("Dragged anywhere but a button, it swings around the eyes (sideways) and rises or lowers (up or down), and keeps that spot. The pointer shows it can move.")]
        public bool movable = true;

        [Tooltip("How far a drag may swing it from where it comes up, in degrees either way.")]
        public float maxSwing = 150f;

        [Tooltip("How far a drag may raise or lower it, in metres either way.")]
        public float maxLift = 0.5f;

        [Tooltip("Panel pixels to the metre: its document's panel settings' (the package's world panel: 1000).")]
        public float pixelsPerMetre = Placard.DefaultPixelsPerMetre;

        [Tooltip("The layer it stands on, where it stands on the default one (a picture's camera leaves its window layer out); empty: as it is.")]
        public string windowLayer = WindowFrame.DefaultLayer;

        [Tooltip("What its fold arrow shows and says.")]
        public WindowFold fold = new WindowFold("Fold its commands away");

        [Tooltip("Show it in a release build too; off: in the editor and a development build only (it switches itself off in a release build).")]
        public bool releaseBuilds;

        readonly WindowFrame _frame = new WindowFrame();

        /// <summary>Move it as it is dragged <paramref name="pixels"/> across its card since the press (as the placard's <see cref="Placard.Drag"/>).</summary>
        /// <param name="pixels">The drag, in panel pixels (right and down positive).</param>
        public void Drag(Vector2 pixels)
        {
            Transform view = Eyes;
            float radius = view == null ? offset.magnitude : Vector3.Distance(view.position, transform.position);
            _frame.Drag(pixels, radius, maxSwing, maxLift, pixelsPerMetre);
            Place();
        }

        [Tooltip("A style sheet over its panel settings' theme (the package's theme holds Panels.uss); empty: the theme alone.")]
        public StyleSheet look;

        readonly List<(Object owner, string label, Action run)> _commands = new List<(Object, string, Action)>();
        bool _stale = true;

        /// <summary>What its status line says.</summary>
        public string Status { get; private set; } = "";

        /// <summary>The labels of its commands, in the order they were put on it.</summary>
        public IReadOnlyList<string> Commands
        {
            get
            {
                var labels = new List<string>(_commands.Count);
                foreach (var command in _commands) labels.Add(command.label);
                return labels;
            }
        }

        /// <summary>The user's eyes: the ones named, else the main camera's; null when there is none.</summary>
        public Transform Eyes => Viewer.Eyes(eyes);

        /// <summary>Whether it shows in a build that is a development one or not (<paramref name="debugBuild"/>): always in one, in a release build only where <see cref="releaseBuilds"/>.</summary>
        /// <param name="debugBuild">Whether the build is the editor or a development build (<see cref="Debug.isDebugBuild"/>).</param>
        public bool ShowsIn(bool debugBuild) => debugBuild || releaseBuilds;

        /// <summary>The first switched-on debug panel in the loaded scenes; null when there is none.</summary>
        public static DebugPanel InScene() => SceneObjects.FirstEnabled<DebugPanel>();

        /// <summary>Put a button labelled <paramref name="label"/> on it that runs <paramref name="run"/>; one <paramref name="owner"/> put there under that label is replaced.</summary>
        /// <param name="owner">What the command belongs to (it takes its commands away with <see cref="Remove"/>).</param>
        /// <param name="label">The button's label.</param>
        /// <param name="run">What pressing it does.</param>
        /// <exception cref="ArgumentException"><paramref name="label"/> is empty.</exception>
        /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
        public void Add(Object owner, string label, Action run)
        {
            if (string.IsNullOrEmpty(label)) throw new ArgumentException("label the command", nameof(label));
            if (run == null) throw new ArgumentNullException(nameof(run), "say what the command does");
            int at = _commands.FindIndex(c => c.owner == owner && c.label == label);
            if (at >= 0) _commands[at] = (owner, label, run);
            else _commands.Add((owner, label, run));
            _stale = true;
        }

        /// <summary>Take away every command <paramref name="owner"/> put on it.</summary>
        /// <param name="owner">What the commands belong to.</param>
        public void Remove(Object owner)
        {
            if (_commands.RemoveAll(c => c.owner == owner) > 0) _stale = true;
        }

        /// <summary>Run the command labelled <paramref name="label"/> (what pressing its button does).</summary>
        /// <param name="label">The command's label.</param>
        /// <returns>Whether there is one.</returns>
        public bool Run(string label)
        {
            foreach (var command in _commands)
                if (command.label == label)
                {
                    command.run();
                    return true;
                }
            return false;
        }

        /// <summary>Say <paramref name="text"/> on its status line (the step a sequence is on).</summary>
        /// <param name="text">The status; null: none.</param>
        public void SetStatus(string text)
        {
            text ??= "";
            if (text == Status) return;
            Status = text;
            _stale = true;
        }

        /// <summary>Fold its commands away, or bring them back.</summary>
        public void Toggle()
        {
            open = !open;
            _stale = true;
        }

        /// <summary>Bring it up beside the eyes, turned with them, facing them.</summary>
        public void Place()
        {
            Transform view = Eyes;
            if (view == null) return;
            Quaternion turn = WindowFrame.Heading(view);
            Pose pose = _frame.Beside(view.position, view.position + turn * offset, turn);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
        }

        void OnEnable()
        {
            if (!ShowsIn(Debug.isDebugBuild))
            {
                // A release build that asks for none: its document and itself off, so nothing of it shows or runs.
                if (TryGetComponent(out UIDocument document)) document.enabled = false;
                enabled = false;
                return;
            }
            _stale = true;  // the document builds its tree anew as it is switched on
            WindowFrame.PutOnLayer(gameObject, windowLayer);
        }

        void LateUpdate()
        {
            Place();
            if (_stale) Render();
        }

        // Its card on the document's tree; stale until the root is there.
        void Render()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _stale = root == null;
            if (_stale) return;
            if (look != null && !root.styleSheets.Contains(look)) root.styleSheets.Add(look);
            VisualElement card = root.Q(className: CardClass);
            if (card == null)
            {
                card = new VisualElement { name = "debug" };
                card.AddToClassList(CardClass);
                var tab = new Label { name = "tab" };
                tab.AddToClassList(TabClass);
                card.Add(tab);
                var status = new Label { name = "status" };
                status.AddToClassList(StatusClass);
                card.Add(status);
                card.Add(new VisualElement { name = "commands" });
                root.Add(card);
            }
            card.Q<Label>("tab").text = title;
            _frame.Folded = !open;
            _frame.Bind(card, movable, true, Drag, on =>
            {
                if (on == open) Toggle();
            }, fold);
            Label line = card.Q<Label>("status");
            line.text = Status;
            line.style.display = open && Status.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            VisualElement buttons = card.Q("commands");
            buttons.Clear();
            buttons.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var command in _commands)
            {
                string label = command.label;
                var button = new Button(() => Run(label)) { text = label };
                button.AddToClassList(CommandClass);
                buttons.Add(button);
            }
        }
    }
}
