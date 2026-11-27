using System.Collections.Generic;
using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// What a flat rectangle standing in front of the eyes (a panel, a sign, a card) hides of the scene:
    /// the one test every part that places a panel clear of what matters asks.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>Plane.Raycast</c>, the sight line from the eyes to a point cut by the panel's
    /// plane; a physics raycast was weighed and not adopted: a panel being placed has no collider where it
    /// is only tried, and the points are a part's centres, not its colliders.
    /// </remarks>
    public static class PanelCover
    {
        /// <summary>
        /// Whether a panel centred at <paramref name="at"/>, turned by <paramref name="facing"/> (its face along
        /// the turn's forward), half its width and height <paramref name="half"/>, stands between
        /// <paramref name="eyes"/> and <paramref name="point"/>.
        /// </summary>
        /// <param name="eyes">Where it is seen from.</param>
        /// <param name="at">The panel's centre.</param>
        /// <param name="facing">The panel's turn.</param>
        /// <param name="half">Half its width (x) and height (y).</param>
        /// <param name="point">What may be hidden.</param>
        public static bool Hides(Vector3 eyes, Vector3 at, Quaternion facing, Vector2 half, Vector3 point)
        {
            var plane = new Plane(facing * Vector3.forward, at);
            var ray = new Ray(eyes, point - eyes);
            if (!plane.Raycast(ray, out float enter) || enter >= Vector3.Distance(eyes, point)) return false;
            Vector3 local = Quaternion.Inverse(facing) * (ray.GetPoint(enter) - at);
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y;
        }

        /// <summary>How many of <paramref name="points"/> such a panel hides (<see cref="Hides"/>); 0 for none.</summary>
        /// <param name="eyes">Where it is seen from.</param>
        /// <param name="at">The panel's centre.</param>
        /// <param name="facing">The panel's turn.</param>
        /// <param name="half">Half its width (x) and height (y).</param>
        /// <param name="points">What may be hidden (null: nothing).</param>
        public static int Count(Vector3 eyes, Vector3 at, Quaternion facing, Vector2 half, IReadOnlyList<Vector3> points)
        {
            if (points == null) return 0;
            int covered = 0;
            for (int i = 0; i < points.Count; i++)
                if (Hides(eyes, at, facing, half, points[i])) covered++;
            return covered;
        }
    }
}
