using UnityEngine;

namespace M3trik.Unitytk.Placement
{
    /// <summary>
    /// A spot a build placed (<see cref="Placer"/>): where a person stands, a camera looks from, an
    /// arrow points from. Its <see cref="key"/> names it from build to build, so an adjustment made
    /// to it (moved, turned) is kept: captured (<c>Placements.Capture</c>) where the build keeps its adjustments, it is where the next build puts it.
    /// It is kept relative to <see cref="relativeTo"/> (the part it is placed by), so it follows the
    /// part when a new delivery moves it.
    /// </summary>
    /// <remarks>Prior art: as <see cref="Placer"/>'s.</remarks>
    [AddComponentMenu("unitytk/Placement/Placed Spot")]
    [DisallowMultipleComponent]
    public sealed class PlacedSpot : MonoBehaviour
    {
        [Tooltip("Its name from build to build: the key its adjustment is kept under.")]
        public string key = "";

        [Tooltip("The part it is placed by: it is kept relative to it (the scene's origin when empty).")]
        public Transform relativeTo;

        [Tooltip("Where the build put it, relative to the part: a spot moved from here is an adjustment to keep.")]
        public Vector3 placedPosition;

        [Tooltip("How the build turned it, relative to the part.")]
        public Quaternion placedRotation = Quaternion.identity;

        [Tooltip("What the search that placed it was asked, in the plan's terms (its part, what is acted on there, the side named): " +
                 "a build keeps its first placement while this stays, and places it anew where it changes. Where things stand is no part of it.")]
        public string basis = "";

        /// <summary>Its pose relative to <see cref="relativeTo"/> now (its turn and offset in the part's axes, not scaled).</summary>
        public Pose Relative => ToRelative(relativeTo, new Pose(transform.position, transform.rotation));

        /// <summary>Whether it stands somewhere other than where the build put it.</summary>
        public bool Adjusted
        {
            get
            {
                Pose now = Relative;
                return (now.position - placedPosition).sqrMagnitude > 1e-8f || !Same(now.rotation, placedRotation);
            }
        }

        /// <summary>
        /// Whether <paramref name="a"/> and <paramref name="b"/> turn alike, within about 0.2 degrees:
        /// by their dot, as <see cref="Quaternion.Angle"/> reads float noise between equal turns as a
        /// few hundredths of a degree.
        /// </summary>
        /// <param name="a">A turn.</param>
        /// <param name="b">Another.</param>
        public static bool Same(Quaternion a, Quaternion b) => Mathf.Abs(Quaternion.Dot(a, b)) > 1f - 1e-6f;

        /// <summary><paramref name="world"/> as a pose relative to <paramref name="part"/> (its turn and offset, not scaled; the world's when null).</summary>
        /// <param name="part">What it is relative to.</param>
        /// <param name="world">A pose in the world.</param>
        public static Pose ToRelative(Transform part, Pose world)
        {
            if (part == null) return world;
            Quaternion inverse = Quaternion.Inverse(part.rotation);
            return new Pose(inverse * (world.position - part.position), inverse * world.rotation);
        }

        /// <summary><paramref name="relative"/>, a pose relative to <paramref name="part"/>, in the world.</summary>
        /// <param name="part">What it is relative to (the world when null).</param>
        /// <param name="relative">A pose relative to it.</param>
        public static Pose ToWorld(Transform part, Pose relative)
        {
            if (part == null) return relative;
            return new Pose(part.position + part.rotation * relative.position, part.rotation * relative.rotation);
        }
    }
}
