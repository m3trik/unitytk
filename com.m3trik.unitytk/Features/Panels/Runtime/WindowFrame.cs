using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Panels
{
    /// <summary>
    /// What every window in the world shares (the placard, the picture, the debug panel): pressed and
    /// dragged anywhere on its frame but a control, it swings around the eyes as the pointer moves
    /// sideways and rises or lowers as it moves up or down, the point grabbed following the pointer, and
    /// later content keeps that spot; over a movable frame the pointer shows it can be moved (the
    /// <see cref="MovableClass"/> class, whose cursor the panels' style sheet sets); a bare arrow in its
    /// corner folds it to its heading and unfolds it. No bar is drawn for any of it.
    /// </summary>
    /// <remarks>
    /// Prior art: the XR Interaction Toolkit's grab interactables move rigid bodies, not a panel kept
    /// beside the eyes, and need its package; UI Toolkit's own <c>Clickable</c> and drag manipulators
    /// move an element inside a panel, not the panel in the world. A press on the frame captured until
    /// it is released is all a pointer and a controller need, through the panel's own events.
    /// </remarks>
    internal sealed class WindowFrame
    {
        /// <summary>The class a frame carries while it can be dragged (its cursor says so).</summary>
        internal const string MovableClass = "window--movable";

        /// <summary>The class of the fold arrow.</summary>
        internal const string FoldClass = "window-fold";

        /// <summary>The class a frame carries while folded.</summary>
        internal const string FoldedClass = "window--folded";

        // Panel pixels a press moves before it is a drag (a click on the frame stays a click).
        const float Threshold = 4f;

        VisualElement _frame;
        Label _fold;
        int _pointer = -1;
        Vector2 _grab;
        bool _dragging;

        /// <summary>
        /// The layer windows stand on by default: Unity's built-in <c>UI</c> layer, which every project has, every
        /// camera draws by default, and a picture's camera leaves out, so a picture never shows a window.
        /// </summary>
        internal const string DefaultLayer = "UI";

        /// <summary>The index of the layer named <paramref name="layer"/>; -1 where there is none (or no name).</summary>
        internal static int LayerOf(string layer) => string.IsNullOrEmpty(layer) ? -1 : LayerMask.NameToLayer(layer);

        /// <summary>
        /// Put <paramref name="window"/> on the layer named <paramref name="layer"/>, where it still stands on the default
        /// layer (one a person moved stays; a layer the project lacks leaves it).
        /// </summary>
        internal static void PutOnLayer(GameObject window, string layer)
        {
            int at = LayerOf(layer);
            if (window != null && at >= 0 && window.layer == 0) window.layer = at;
        }

        /// <summary>How far it was swung around the eyes, in degrees (right positive).</summary>
        internal float Swing { get; set; }

        /// <summary>How far it was raised, in metres.</summary>
        internal float Lift { get; set; }

        /// <summary>Whether it is folded to its heading.</summary>
        internal bool Folded { get; set; }

        /// <summary>Whether a drag is under way.</summary>
        internal bool Dragging => _dragging;

        /// <summary>
        /// Wire <paramref name="frame"/> (once per element: a document builds its tree anew as it is switched
        /// on) and show its state: movable, its fold arrow while <paramref name="collapsible"/>.
        /// </summary>
        /// <param name="frame">The window's outermost element.</param>
        /// <param name="movable">Whether a drag moves it.</param>
        /// <param name="collapsible">Whether its fold arrow shows.</param>
        /// <param name="drag">Called with the drag since the press, in panel pixels (right and down positive).</param>
        /// <param name="fold">Called with whether to fold, as the arrow is pressed.</param>
        /// <param name="says">The arrow's glyphs and tooltips (null: the defaults).</param>
        internal void Bind(VisualElement frame, bool movable, bool collapsible, Action<Vector2> drag, Action<bool> fold, WindowFold says = null)
        {
            if (frame == null) return;
            if (_frame != frame)
            {
                _frame = frame;
                _pointer = -1;
                _dragging = false;
                frame.RegisterCallback<PointerDownEvent>(e =>
                {
                    if (e.button != 0 || !frame.ClassListContains(MovableClass) || OnControl(e.target as VisualElement, frame)) return;
                    _pointer = e.pointerId;
                    _grab = e.localPosition;
                    _dragging = false;
                    frame.CapturePointer(e.pointerId);
                });
                frame.RegisterCallback<PointerMoveEvent>(e =>
                {
                    if (e.pointerId != _pointer || !frame.HasPointerCapture(e.pointerId)) return;
                    Vector2 moved = (Vector2)e.localPosition - _grab;
                    if (!_dragging && moved.magnitude < Threshold) return;
                    _dragging = true;
                    drag(moved);
                });
                frame.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.pointerId != _pointer) return;
                    if (frame.HasPointerCapture(e.pointerId)) frame.ReleasePointer(e.pointerId);
                    _pointer = -1;
                    _dragging = false;
                });
                _fold = new Label { name = "fold" };
                _fold.AddToClassList(FoldClass);
                _fold.RegisterCallback<PointerUpEvent>(e =>
                {
                    if (e.button != 0) return;
                    fold(!Folded);
                    e.StopPropagation();
                });
                frame.Add(_fold);
            }
            frame.EnableInClassList(MovableClass, movable);
            frame.EnableInClassList(FoldedClass, Folded);
            _fold.style.display = collapsible ? DisplayStyle.Flex : DisplayStyle.None;
            if (says == null) says = new WindowFold();
            _fold.text = Folded ? says.folded : says.open;
            _fold.tooltip = Folded ? says.unfoldTooltip : says.foldTooltip;
            _fold.BringToFront();
        }

        /// <summary>
        /// Move it by a drag of <paramref name="pixels"/> since the press (right and down positive): swung
        /// around the eyes, <paramref name="radius"/> metres off, so the point grabbed follows the pointer, and
        /// raised or lowered, within <paramref name="maxSwing"/> degrees and <paramref name="maxLift"/> metres;
        /// <paramref name="pixelsPerMetre"/> panel pixels to the metre (its panel settings').
        /// </summary>
        internal void Drag(Vector2 pixels, float radius, float maxSwing, float maxLift, float pixelsPerMetre)
        {
            float perMetre = Mathf.Max(1e-3f, pixelsPerMetre);
            Swing = Mathf.Clamp(Swing + pixels.x / perMetre / Mathf.Max(0.1f, radius) * Mathf.Rad2Deg, -maxSwing, maxSwing);
            Lift = Mathf.Clamp(Lift - pixels.y / perMetre, -maxLift, maxLift);
        }

        /// <summary>Where <paramref name="at"/> (beside <paramref name="eyes"/>) stands once swung and lifted as the user left it.</summary>
        internal Vector3 Around(Vector3 eyes, Vector3 at) => eyes + Quaternion.Euler(0f, Swing, 0f) * (at - eyes) + Vector3.up * Lift;

        /// <summary>
        /// The pose of a window put at <paramref name="spot"/> beside <paramref name="eyes"/>: where it stands once swung and
        /// lifted as the user left it (<see cref="Around"/>), facing away from the eyes (<see cref="Toward"/>). Every window's.
        /// </summary>
        /// <param name="eyes">Where the eyes are.</param>
        /// <param name="spot">Where it comes up, before the user moved it.</param>
        /// <param name="turn">Its turn where it stands at the eyes themselves.</param>
        internal Pose Beside(Vector3 eyes, Vector3 spot, Quaternion turn)
        {
            Vector3 at = Around(eyes, spot);
            return new Pose(at, Toward(eyes, at, turn));
        }

        /// <summary>The turn of a window at <paramref name="at"/> facing <paramref name="eyes"/> (its face away from them, upright); <paramref name="turn"/> where it stands at them.</summary>
        internal static Quaternion Toward(Vector3 eyes, Vector3 at, Quaternion turn)
        {
            Vector3 away = at - eyes;
            return away.sqrMagnitude < 1e-8f ? turn : Quaternion.LookRotation(away, Vector3.up);
        }

        /// <summary>
        /// The way <paramref name="eyes"/> are headed, level: a turn about the up axis alone toward where they look; eyes
        /// looking straight down are headed the way the top of their view points, straight up the way its bottom does, so a
        /// window turned with them still comes up ahead.
        /// </summary>
        internal static Quaternion Heading(Transform eyes)
        {
            Vector3 forward = eyes.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-8f) forward = eyes.forward.y < 0f ? eyes.up : -eyes.up;
            forward.y = 0f;
            return forward.sqrMagnitude < 1e-8f ? Quaternion.identity : Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        // Whether element is a control of its own (a button, a field, the fold arrow) between it and the frame.
        static bool OnControl(VisualElement element, VisualElement frame)
        {
            for (VisualElement at = element; at != null && at != frame; at = at.parent)
                if (at is Button || at is TextField || at is Toggle || at is Scroller || at.ClassListContains(FoldClass)) return true;
            return false;
        }
    }
}
