using UnityEngine;

namespace M3trik.Unitytk.Viewpoints
{
    /// <summary>
    /// The user's body, as a viewpoint moves it: on the user's rig (a headset's origin, a
    /// desktop rig), or on a desktop camera that is its own eyes. <see cref="GoTo"/> stands the
    /// eyes over a viewpoint, facing its way: a rig is turned about its eyes and moved, its
    /// floor (<see cref="feet"/>, its origin by default) put at the viewpoint's and its eyes keeping
    /// their height over it (a headset tracks them); a camera that is its own eyes stands
    /// <see cref="eyeHeight"/> over the viewpoint, looking level.
    /// </summary>
    /// <remarks>
    /// Prior art: XR Interaction Toolkit's <c>TeleportationAnchor</c> moves an XR Origin
    /// through its locomotion system, and XR Core Utils' <c>XROrigin.MoveCameraToWorldLocation</c>
    /// is this same move for an XR Origin alone; neither moves a desktop camera, and both
    /// need their packages, so the move is done here on any rig (a headset's origin is a
    /// rig like any other). Cinemachine blends cameras, not the user's body; a project's
    /// Cinemachine rig is carried by putting this on what its camera follows.
    /// A character controller on the rig takes the new place: the move ends by syncing
    /// the physics scene's transforms.
    /// </remarks>
    [AddComponentMenu("unitytk/Viewpoints/Traveler")]
    [DisallowMultipleComponent]
    public sealed class Traveler : MonoBehaviour
    {
        [Tooltip("The user's eyes (this object's camera, else the first camera under it, else this object, when empty).")]
        public Transform head;

        [Tooltip("How high the eyes stand over a viewpoint, in metres, when this object is its own eyes (a desktop camera); a rig's eyes keep their own height.")]
        public float eyeHeight = 1.6f;

        [Tooltip("A rig's floor: the point that stands on a viewpoint's floor (this object's origin when empty). Name it where the " +
                 "rig's origin is not its floor (an origin tracked from the eyes' height, a rig whose floor is a child).")]
        public Transform feet;

        /// <summary>The user's eyes.</summary>
        public Transform Head
        {
            get
            {
                if (head != null) return head;
                if (TryGetComponent(out Camera own)) return own.transform;
                Camera under = GetComponentInChildren<Camera>(true);
                return under != null ? under.transform : transform;
            }
        }

        /// <summary>
        /// Stand the user's eyes over <paramref name="floor"/>, facing <paramref name="facing"/>'s
        /// way (its heading: a viewpoint's tilt is left to the user): this object put where
        /// <see cref="Solve"/> says.
        /// </summary>
        /// <param name="floor">Where the user stands.</param>
        /// <param name="facing">Which way they face.</param>
        public void GoTo(Vector3 floor, Quaternion facing)
        {
            Solve(floor, facing, out Vector3 position, out Quaternion rotation);
            transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();
        }

        /// <summary>
        /// Where <see cref="GoTo"/> puts this object, moving nothing: a rig turned about its eyes to face
        /// <paramref name="facing"/>'s heading, its floor on <paramref name="floor"/>, its eyes keeping their height
        /// over it; a camera that is its own eyes <see cref="eyeHeight"/> over the floor, looking level.
        /// </summary>
        /// <param name="floor">Where the user stands.</param>
        /// <param name="facing">Which way they face.</param>
        /// <param name="position">This object's position there, in world space.</param>
        /// <param name="rotation">Its rotation there, in world space.</param>
        public void Solve(Vector3 floor, Quaternion facing, out Vector3 position, out Quaternion rotation)
        {
            Vector3 heading = Heading(facing);
            Transform eyes = Head;
            if (eyes == transform)
            {
                position = floor + Vector3.up * eyeHeight;
                rotation = Quaternion.LookRotation(heading, Vector3.up);
                return;
            }
            // Turned about the eyes, which stay where they are; a turn about the vertical keeps every height.
            Quaternion turn = Quaternion.AngleAxis(Vector3.SignedAngle(Heading(eyes.rotation), heading, Vector3.up), Vector3.up);
            rotation = turn * transform.rotation;
            Vector3 offset = turn * (eyes.position - transform.position);  // the eyes from this object, once turned
            float drop = feet != null && feet != transform ? feet.position.y - transform.position.y : 0f;
            position = new Vector3(floor.x - offset.x, floor.y - drop, floor.z - offset.z);
        }

        // The way a rotation faces over the ground; looking straight down (or up), the way
        // the top of the head points (or its back).
        static Vector3 Heading(Quaternion rotation)
        {
            Vector3 forward = rotation * Vector3.forward;
            Vector3 flat = new Vector3(forward.x, 0f, forward.z);
            if (flat.sqrMagnitude < 1e-8f)
            {
                Vector3 up = rotation * Vector3.up * (forward.y < 0f ? 1f : -1f);
                flat = new Vector3(up.x, 0f, up.z);
            }
            return flat.normalized;
        }
    }
}
