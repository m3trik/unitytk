using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Tools.Manifests
{
    /// <summary>
    /// A menu drawn inside its window, in the manifest windows' look (<see cref="EditorLook"/>, the
    /// <c>window-menu</c> classes): a dark menu, never the system's white one. It holds actions (one closes
    /// it), toggles (it stays open while they are ticked, as a checkbox menu does) and sections (an entry
    /// named <c>Section/Entry</c> is listed under a heading). It closes on a click outside it, on Escape,
    /// or once the pointer has left it.
    /// </summary>
    /// <remarks>
    /// Prior art: <c>GenericMenu</c> and a <c>ContextualMenuManipulator</c>'s menu are the operating
    /// system's (white on Windows, no style sheet reaches them); UI Toolkit's <c>GenericDropdownMenu</c>
    /// closes on every pick, so a checkbox list would reopen after each tick, and its parts are styled by
    /// Unity's own classes. A small overlay in the window is all of it.
    /// </remarks>
    internal sealed class WindowMenu
    {
        // How far (pixels) the pointer may stray from the menu before it closes.
        const float Leave = 28f;

        readonly List<VisualElement> _entries = new List<VisualElement>();
        readonly HashSet<string> _sections = new HashSet<string>();
        VisualElement _overlay, _box;

        /// <summary>Whether it holds no entry yet.</summary>
        public bool Empty => _entries.Count == 0;

        /// <summary>Raised once as it closes, however it was closed.</summary>
        public event Action Closed;

        /// <summary>Add an action: a click runs it and closes the menu. A null <paramref name="run"/> is said, not done.</summary>
        /// <param name="path">Its text; <c>Section/Text</c> lists it under the section's heading.</param>
        /// <param name="run">What a click does; null: shown greyed.</param>
        /// <param name="on">Whether it is shown ticked.</param>
        /// <param name="tooltip">What a hover says.</param>
        /// <param name="hint">A few quiet words at its right (a count, a state, a name); empty: none.</param>
        public void Item(string path, Action run, bool on = false, string tooltip = "", string hint = "")
        {
            VisualElement row = Row(path, tooltip, run != null);
            row.Add(Check(on, false));
            row.Add(Text(path));
            AddHint(row, hint);
            if (run != null)
                row.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button != 0) return;
                    Close();
                    run();
                });
            _entries.Add(row);
        }

        /// <summary>Add a toggle: a click ticks it or clears it and the menu stays open.</summary>
        /// <param name="path">Its text; <c>Section/Text</c> lists it under the section's heading.</param>
        /// <param name="on">Whether it starts ticked.</param>
        /// <param name="changed">Called with its new state on each click; null: shown greyed.</param>
        /// <param name="tooltip">What a hover says.</param>
        /// <param name="hint">A few quiet words at its right (a state); empty: none.</param>
        public void Toggle(string path, bool on, Action<bool> changed, string tooltip = "", string hint = "")
        {
            VisualElement row = Row(path, tooltip, changed != null);
            VisualElement check = Check(on, true);
            row.Add(check);
            row.Add(Text(path));
            AddHint(row, hint);
            if (changed != null)
                row.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button != 0) return;
                    on = !on;
                    check.EnableInClassList("window-menu__check--on", on);
                    changed(on);
                });
            _entries.Add(row);
        }

        /// <summary>Add a line between entries (none at its top, none twice).</summary>
        public void Separator()
        {
            if (_entries.Count == 0 || _entries[_entries.Count - 1].ClassListContains("window-menu__separator")) return;
            var line = new VisualElement();
            line.AddToClassList("window-menu__separator");
            _entries.Add(line);
        }

        /// <summary>Show it in <paramref name="window"/> (the window's root), its top left at <paramref name="at"/> (panel coordinates), kept inside.</summary>
        /// <param name="window">The window's root, wearing <see cref="EditorLook"/>.</param>
        /// <param name="at">Where its top left goes, in panel coordinates (an element's <c>worldBound</c>, a pointer's position).</param>
        public void Show(VisualElement window, Vector2 at)
        {
            if (window == null || _entries.Count == 0) return;
            if (_entries[_entries.Count - 1].ClassListContains("window-menu__separator")) _entries.RemoveAt(_entries.Count - 1);
            _overlay = new VisualElement { focusable = true };
            _overlay.AddToClassList("window-menu__overlay");
            _box = new VisualElement();
            _box.AddToClassList("window-menu");
            foreach (VisualElement entry in _entries) _box.Add(entry);
            _overlay.Add(_box);
            window.Add(_overlay);
            Vector2 local = window.WorldToLocal(at);
            _box.style.left = local.x;
            _box.style.top = local.y;
            // Inside the window: moved back once its size is known.
            _box.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                Rect room = window.contentRect;
                float x = Mathf.Clamp(local.x, 0f, Mathf.Max(0f, room.width - _box.layout.width - 2f));
                float y = local.y + _box.layout.height > room.height ? Mathf.Max(0f, local.y - _box.layout.height) : local.y;
                _box.style.left = x;
                _box.style.top = y;
            });
            _overlay.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _overlay) Close();
            });
            _overlay.RegisterCallback<PointerMoveEvent>(e =>
            {
                Rect near = _box.worldBound;
                near.xMin -= Leave;
                near.yMin -= Leave;
                near.xMax += Leave;
                near.yMax += Leave;
                if (!near.Contains(e.position)) Close();
            });
            _overlay.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Escape) Close();
            });
            VisualElement overlay = _overlay;
            overlay.schedule.Execute(() => overlay.Focus());
        }

        /// <summary>Close it, if it is open.</summary>
        public void Close()
        {
            if (_overlay == null) return;
            _overlay.RemoveFromHierarchy();
            _overlay = null;
            Closed?.Invoke();
            Closed = null;
        }

        // A row for an entry, under its section's heading where its path names one.
        VisualElement Row(string path, string tooltip, bool enabled)
        {
            int slash = path.IndexOf('/');
            if (slash > 0 && _sections.Add(path.Substring(0, slash)))
            {
                Separator();
                var heading = new Label(path.Substring(0, slash));
                heading.AddToClassList("window-menu__heading");
                _entries.Add(heading);
            }
            var row = new VisualElement { tooltip = tooltip ?? "" };
            row.AddToClassList("window-menu__item");
            if (!enabled) row.AddToClassList("window-menu__item--disabled");
            return row;
        }

        static void AddHint(VisualElement row, string hint)
        {
            if (string.IsNullOrEmpty(hint)) return;
            var quiet = new Label(hint);
            quiet.AddToClassList("window-menu__hint");
            row.Add(quiet);
        }

        static Label Text(string path)
        {
            int slash = path.IndexOf('/');
            var text = new Label(slash > 0 ? path.Substring(slash + 1) : path);
            text.AddToClassList("window-menu__text");
            return text;
        }

        // A toggle's box, or an action's tick where it is on: the drawn check (VectorIcons), coloured by the style sheet.
        static VisualElement Check(bool on, bool box)
        {
            var check = new M3trik.Unitytk.Kernel.VectorIcon("check");
            check.style.width = 13;
            check.style.height = 13;
            check.AddToClassList("window-menu__check");
            if (box) check.AddToClassList("window-menu__check--box");
            check.EnableInClassList("window-menu__check--on", on);
            return check;
        }
    }
}
