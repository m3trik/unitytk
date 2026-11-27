using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace M3trik.Unitytk.Tools.Manifests
{
    /// <summary>
    /// A tree row's hierarchy, the Shot Manifest's design: guide lines
    /// that hang each row from its parent's expander, so every level reads as one line down from its parent,
    /// and the expander, a thin-walled box with a plus or a minus filling its cell. Its colour is the look's
    /// text colour (<c>color</c>, <see cref="UssClassName"/>) at <see cref="LineAlpha"/>,
    /// <see cref="BoxAlpha"/> and <see cref="SignAlpha"/>; the expander under the pointer at full strength.
    /// It lies over Unity's own foldout, which stays where it was, unseen and clickable, so a click and the
    /// arrow keys open and close a row as they always did (the window's sheet hides it and sizes the
    /// indentation's cells, <see cref="Cell"/> px each).
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's tree views draw a foldout triangle and no guide lines, and style neither beyond
    /// the triangle's image; an image per combination of guides would need a file for each.
    /// Each row's guides are worked out from its place in the tree (<see cref="Layout"/>) as whole-pixel
    /// rects, none overlapping, and written into the row's mesh a quad each (<c>MeshGenerationContext.Allocate</c>),
    /// so they stay crisp and a faded colour is laid once on every pixel. <c>Painter2D</c> was weighed and
    /// dropped: it tessellates rects that meet edge to edge into one shape and filled half the box with a
    /// triangle, and antialiases edges that already sit on pixels.
    /// </remarks>
    internal sealed class TreeBranches : VisualElement
    {
        /// <summary>The class it wears: the window's sheet gives it its colour.</summary>
        internal const string UssClassName = "unitytk-branches";

        /// <summary>The width of a level's cell, pixels; the window's sheet gives Unity's indentation and foldout the same.</summary>
        internal const float Cell = 16f;

        /// <summary>How much of the text colour the guide lines show.</summary>
        internal const float LineAlpha = 0.3f;

        /// <summary>How much of the text colour the expander's walls show (the one under the pointer: all of it).</summary>
        internal const float BoxAlpha = 0.55f;

        /// <summary>How much of the text colour the expander's sign shows (the one under the pointer: all of it).</summary>
        internal const float SignAlpha = 0.85f;

        int _depth;
        bool _children, _open, _hot;
        bool[] _later = new bool[0];
        VisualElement _toggle;  // Unity's foldout, under the row's own cell

        /// <summary>A row's parts in its indentation, as whole-pixel rects (its top left at 0, 0), no two overlapping.</summary>
        internal sealed class Parts
        {
            /// <summary>The guide lines.</summary>
            public readonly List<Rect> guides = new List<Rect>();

            /// <summary>The expander's four walls; empty for a row without children.</summary>
            public readonly List<Rect> walls = new List<Rect>();

            /// <summary>The expander's inside (its hover ground); zero for a row without children.</summary>
            public Rect inside;

            /// <summary>The sign's strokes: a minus while open, a plus while closed.</summary>
            public readonly List<Rect> sign = new List<Rect>();
        }

        TreeBranches()
        {
            AddToClassList(UssClassName);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        /// <summary>
        /// A row's guides and expander in its indentation, a <paramref name="cell"/>-wide cell a level, its own
        /// last. The row hangs from its parent's expander: an elbow in the parent's cell comes down from the row
        /// above and across to the row, and runs on down while a later sibling of the row is shown; each
        /// ancestor further left runs its own line through the row while one of its children below is shown.
        /// A row with children has its expander in its own cell, a line running on down from it while open.
        /// </summary>
        /// <param name="depth">The row's level, 0 at the top.</param>
        /// <param name="children">Whether it has children (an expander).</param>
        /// <param name="open">Whether it is expanded.</param>
        /// <param name="later">Per cell left of the row's own (<paramref name="depth"/> of them): whether the
        /// line of that cell runs on down: the last, whether the row has a later sibling shown; one further left,
        /// whether the row's ancestor a level below that cell has one.</param>
        /// <param name="cell">A cell's width, pixels.</param>
        /// <param name="height">The row's height, pixels.</param>
        /// <returns>Its parts, as whole-pixel rects.</returns>
        internal static Parts Layout(int depth, bool children, bool open, IReadOnlyList<bool> later, float cell, float height)
        {
            var parts = new Parts();
            int w = Mathf.RoundToInt(cell), h = Mathf.RoundToInt(height);
            int side = Mathf.Min(w, h) - 5;  // 11 in a 16 px cell: room around it
            if (side % 2 == 0) side -= 1;  // odd: the sign sits on a pixel
            int boxX = (w - side) / 2, boxY = (h - side) / 2;
            int midX = boxX + side / 2, midY = boxY + side / 2;
            if (depth >= 1)
            {
                int x = (depth - 1) * w + midX;
                bool runs = later != null && later.Count >= depth && later[depth - 1];
                parts.guides.Add(new Rect(x, 0, 1, runs ? h : midY + 1));
                int end = depth * w + (children ? boxX : w - 2);
                parts.guides.Add(new Rect(x + 1, midY, end - x - 1, 1));  // from beside the line down
                for (int c = depth - 2; c >= 0; c--)
                    if (later != null && later.Count > c && later[c]) parts.guides.Add(new Rect(c * w + midX, 0, 1, h));
            }
            if (!children) return parts;
            int bx = depth * w + boxX;
            parts.walls.Add(new Rect(bx, boxY, side, 1));
            parts.walls.Add(new Rect(bx, boxY + side - 1, side, 1));
            parts.walls.Add(new Rect(bx, boxY + 1, 1, side - 2));
            parts.walls.Add(new Rect(bx + side - 1, boxY + 1, 1, side - 2));
            parts.inside = new Rect(bx + 1, boxY + 1, side - 2, side - 2);
            if (open && boxY + side < h) parts.guides.Add(new Rect(depth * w + midX, boxY + side, 1, h - boxY - side));
            int cx = bx + side / 2, cy = boxY + side / 2, reach = (side - 6) / 2;  // 2 px clear of the walls
            parts.sign.Add(new Rect(cx - reach, cy, 2 * reach + 1, 1));
            if (!open)  // the plus's upright, above and below the bar
            {
                parts.sign.Add(new Rect(cx, cy - reach, 1, reach));
                parts.sign.Add(new Rect(cx, cy + 1, 1, reach));
            }
            return parts;
        }

        /// <summary>
        /// The branches of the tree row whose first cell holds <paramref name="cell"/>, made the first time: over
        /// Unity's own foldout, which keeps the clicks and lights them while the pointer is on it. Null where the
        /// row has no foldout (a cell outside a tree view).
        /// </summary>
        /// <param name="cell">The content a column's <c>makeCell</c> made, in a tree view's tree column.</param>
        /// <returns>Its branches; null where it has no foldout.</returns>
        internal static TreeBranches Of(VisualElement cell)
        {
            VisualElement toggle = null;
            for (VisualElement up = cell?.parent; up != null && toggle == null; up = up.parent)
            {
                toggle = up.Q(className: BaseTreeView.itemToggleUssClassName);
                if (up is BaseTreeView) break;
            }
            if (toggle == null || toggle.parent == null) return null;
            VisualElement row = toggle.parent;
            if (row.Q<TreeBranches>() is TreeBranches made) return made;
            var branches = new TreeBranches { _toggle = toggle };
            row.Insert(0, branches);
            toggle.RegisterCallback<PointerEnterEvent>(_ => branches.SetHot(true));
            toggle.RegisterCallback<PointerLeaveEvent>(_ => branches.SetHot(false));
            toggle.RegisterCallback<GeometryChangedEvent>(_ => branches.Align());
            return branches;
        }

        /// <summary>Show the row at <paramref name="depth"/>, with <paramref name="children"/> or none, open or closed, and its lines running on per <paramref name="later"/> (<see cref="Layout"/>).</summary>
        /// <param name="depth">The row's level, 0 at the top.</param>
        /// <param name="children">Whether it has children.</param>
        /// <param name="open">Whether it is expanded.</param>
        /// <param name="later">Per cell left of the row's own: whether its line runs on down (<see cref="Layout"/>).</param>
        internal void Set(int depth, bool children, bool open, bool[] later)
        {
            _depth = depth;
            _children = children;
            _open = open;
            _later = later ?? new bool[0];
            _hot = false;  // a reused row: the pointer is on another
            style.width = (depth + 1) * Cell;
            Align();
            MarkDirtyRepaint();
        }

        // Its own cell over the foldout, wherever the row lays the foldout out.
        void Align()
        {
            if (_toggle != null) style.left = _toggle.layout.x - _depth * Cell;
        }

        void SetHot(bool hot)
        {
            if (_hot == hot) return;
            _hot = hot;
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext context)
        {
            Parts parts = Layout(_depth, _children, _open, _later, Cell, layout.height);
            Color text = resolvedStyle.color;
            Fill(context, parts.guides, Faded(text, LineAlpha));
            if (parts.walls.Count == 0) return;
            if (_hot) Fill(context, new List<Rect> { parts.inside }, Faded(text, 0.12f));
            Fill(context, parts.walls, _hot ? text : Faded(text, BoxAlpha));
            Fill(context, parts.sign, _hot ? text : Faded(text, SignAlpha));
        }

        static Color Faded(Color color, float alpha) => new Color(color.r, color.g, color.b, color.a * alpha);

        // Each rect a quad of its own, its corners on its pixels' edges, wound clockwise as the mesh expects.
        static void Fill(MeshGenerationContext context, List<Rect> rects, Color color)
        {
            if (rects.Count == 0) return;
            MeshWriteData mesh = context.Allocate(rects.Count * 4, rects.Count * 6);
            Color32 tint = color;
            for (int i = 0; i < rects.Count; i++)
            {
                Rect r = rects[i];
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMax, Vertex.nearZ), tint = tint });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMin, r.yMin, Vertex.nearZ), tint = tint });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMin, Vertex.nearZ), tint = tint });
                mesh.SetNextVertex(new Vertex { position = new Vector3(r.xMax, r.yMax, Vertex.nearZ), tint = tint });
                ushort v = (ushort)(i * 4);
                mesh.SetNextIndex(v);
                mesh.SetNextIndex((ushort)(v + 1));
                mesh.SetNextIndex((ushort)(v + 2));
                mesh.SetNextIndex((ushort)(v + 2));
                mesh.SetNextIndex((ushort)(v + 3));
                mesh.SetNextIndex(v);
            }
        }
    }
}
