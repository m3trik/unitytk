using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Viewpoints
{
    /// <summary>
    /// A viewpoint: where the user is brought to stand, each time this is enabled (a step enables
    /// it as it runs), facing <see cref="facing"/> as it then stands (a part a clip has moved is faced
    /// where it is), else its object's forward (+Z). The user stands on the floor its
    /// <see cref="floorRule"/> finds under it (by default its object's own mesh where it has one: a
    /// destination disc, its pivot often below it; else its position: <see cref="Floor"/>). Its
    /// <see cref="Traveler"/> is the one named, else the scene's, so one scene serves whichever rig is
    /// switched on, a desktop's or a headset's.
    /// </summary>
    /// <remarks>Prior art: as <see cref="Viewpoints.Traveler"/>'s.</remarks>
    [AddComponentMenu("unitytk/Viewpoints/Travel To")]
    [DisallowMultipleComponent]
    public sealed class TravelTo : MonoBehaviour
    {
        [Tooltip("Who is brought here (the scene's switched-on Traveler when empty).")]
        public Traveler traveler;

        [Tooltip("What the user faces once here, where it then stands (this object's forward when empty).")]
        public Transform facing;

        [Tooltip("Metres the user stands from what they face, brought toward it from here when farther (0: here, as placed).")]
        [Min(0f)]
        public float standOff;

        [Tooltip("Where the user stands under this spot: on its mesh's top (or its parent's), at its pivot, on the first " +
                 "surface below it (a floor under a roof), or at a height given.")]
        public TravelFloor floorRule = TravelFloor.Mesh;

        [Tooltip("With the Height rule: the floor's height in the world, in metres.")]
        public float floorHeight;

        /// <summary>The <see cref="rayFrom"/> of a viewpoint just added, in metres.</summary>
        public const float DefaultRayFrom = 0.25f;

        [Tooltip("With the Below rule: how far above the spot the ray down starts, in metres (under a low ceiling, keep it below it).")]
        [Min(0f)]
        public float rayFrom = DefaultRayFrom;

        /// <summary>Who is brought here: the one named, else the scene's switched-on one; null when none.</summary>
        public Traveler Traveler => traveler != null ? traveler : SceneObjects.FirstEnabled<Traveler>();

        /// <summary>
        /// Where the user stands: on the floor <see cref="floorRule"/> finds under this object. With a
        /// <see cref="standOff"/>, brought toward what they face until that far from its nearest side,
        /// over the ground.
        /// </summary>
        public Vector3 Floor
        {
            get
            {
                Stand(transform, facing, standOff, out Vector3 floor, out _, floorRule, floorHeight, rayFrom);
                return floor;
            }
        }

        /// <summary>The way the user faces once here: toward <see cref="facing"/> as it stands now, level; else this object's.</summary>
        public Quaternion Heading
        {
            get
            {
                Stand(transform, facing, standOff, out _, out Quaternion heading, floorRule, floorHeight, rayFrom);
                return heading;
            }
        }

        /// <summary>
        /// Where a user brought to <paramref name="viewpoint"/> stands and the way they face, as the
        /// scene stands now: on the floor <paramref name="floorRule"/> finds under it, brought toward
        /// <paramref name="facing"/> to <paramref name="standOff"/> from it when farther, facing its
        /// middle, level; with nothing to face, the viewpoint's own forward. The one rule
        /// <see cref="Travel"/> goes by, for a builder that places what the user then sees.
        /// </summary>
        /// <param name="viewpoint">Where the user is brought.</param>
        /// <param name="facing">What they face there (null: the viewpoint's forward).</param>
        /// <param name="standOff">Metres they stand from what they face (0: on the viewpoint).</param>
        /// <param name="floor">Where they stand.</param>
        /// <param name="heading">The way they face.</param>
        /// <param name="floorRule">How the floor under the viewpoint is found (<see cref="TravelFloor.Mesh"/>: its mesh's top, or its parent's).</param>
        /// <param name="floorHeight">With <see cref="TravelFloor.Height"/>: the floor's height in the world.</param>
        /// <param name="rayFrom">With <see cref="TravelFloor.Below"/>: how far above the viewpoint the ray down starts.</param>
        public static void Stand(Transform viewpoint, Transform facing, float standOff, out Vector3 floor, out Quaternion heading,
                                 TravelFloor floorRule = TravelFloor.Mesh, float floorHeight = 0f, float rayFrom = DefaultRayFrom)
        {
            floor = FloorUnder(viewpoint, floorRule, floorHeight, rayFrom);
            heading = viewpoint.rotation;
            if (facing == null) return;
            // What is seen of it, gathered once: where they are brought toward, and what they face (its position where it shows nothing).
            bool shown = SceneBounds.TryOf(facing, out Bounds seen, includeDisabled: true);
            if (standOff > 0f && shown)
            {
                Vector3 nearest = seen.ClosestPoint(new Vector3(floor.x, seen.center.y, floor.z));
                Vector3 toward = nearest - floor;
                toward.y = 0f;
                float far = toward.magnitude;
                if (far > standOff) floor += toward / far * (far - standOff);
            }
            Vector3 to = (shown ? seen.center : facing.position) - floor;
            to.y = 0f;
            if (to.sqrMagnitude > 1e-8f) heading = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        /// <summary>
        /// The floor under <paramref name="viewpoint"/> by <paramref name="floorRule"/>: the top of its own mesh (or, for
        /// a viewpoint made beside another, its parent's), its pivot, the first surface below it, or a height given.
        /// </summary>
        /// <param name="viewpoint">A viewpoint.</param>
        /// <param name="floorRule">How the floor is found.</param>
        /// <param name="floorHeight">With <see cref="TravelFloor.Height"/>: the floor's height in the world.</param>
        /// <param name="rayFrom">With <see cref="TravelFloor.Below"/>: how far above the viewpoint the ray down starts.</param>
        public static Vector3 FloorUnder(Transform viewpoint, TravelFloor floorRule, float floorHeight = 0f, float rayFrom = DefaultRayFrom)
        {
            Vector3 floor = viewpoint.position;
            switch (floorRule)
            {
                case TravelFloor.Mesh:
                    if (TopOf(viewpoint, out float top) || (viewpoint.parent != null && TopOf(viewpoint.parent, out top))) floor.y = top;
                    break;
                case TravelFloor.Below:
                    Vector3 from = floor + Vector3.up * Mathf.Max(0f, rayFrom);
                    if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, Mathf.Infinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                        floor.y = hit.point.y;
                    break;
                case TravelFloor.Height:
                    floor.y = floorHeight;
                    break;
            }
            return floor;
        }

        /// <summary>Bring the user here (what enabling it does; a UnityEvent can call it); said when there is no traveler.</summary>
        public void Travel() => TryTravel();

        /// <summary>Bring the user here; false, said, when there is no traveler.</summary>
        public bool TryTravel()
        {
            Traveler who = Traveler;
            if (who == null)
            {
                Debug.LogWarning($"{name}: no Traveler in the scene to bring here; add one to the user's rig (the camera on a desktop, the origin in a headset).", this);
                return false;
            }
            Stand(transform, facing, standOff, out Vector3 floor, out Quaternion heading, floorRule, floorHeight, rayFrom);
            who.GoTo(floor, heading);
            return true;
        }

        // The highest point of the mesh on node itself (shown or hidden); false where it has none.
        static bool TopOf(Transform node, out float top)
        {
            top = 0f;
            if (!node.TryGetComponent(out MeshFilter filter)
                || !SceneBounds.TryOfMeshes(new[] { (filter.sharedMesh, node.localToWorldMatrix) }, out Bounds seen)) return false;
            top = seen.max.y;
            return true;
        }

        void OnEnable() => Travel();

        // Where the user stands, and the way they face: one Stand a repaint.
        void OnDrawGizmos()
        {
            Stand(transform, facing, standOff, out Vector3 at, out Quaternion heading, floorRule, floorHeight, rayFrom);
            Vector3 forward = heading * Vector3.forward;
            Gizmos.color = new Color(0.3f, 0.8f, 1f);
            Gizmos.DrawWireCube(at, new Vector3(0.5f, 0.01f, 0.5f));
            Gizmos.DrawLine(at, at + new Vector3(forward.x, 0f, forward.z).normalized * 0.5f);
        }
    }
}
