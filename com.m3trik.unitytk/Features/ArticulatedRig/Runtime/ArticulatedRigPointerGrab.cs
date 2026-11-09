// ----------------------------------------------------------------------------
// The ArticulatedRig feature's desktop / editor-play-mode grabber: press over a
// part (a raycast against the colliders the importer put on the grab nodes),
// drag it in the camera-facing plane through the grab point, and roll the
// scroll wheel to push it away along the view ray or pull it closer. It only
// turns pointer input into world poses; ArticulatedRig does the rest, so it is also the reference for driving the grab from another input.
//
// The pointer is a seam (IPointerInput), so this part needs no input package:
// the Input System door (InputSystem/, compiled only with com.unity.inputsystem)
// registers the Input System's mouse where it is the active handler; with
// nothing registered the legacy Input Manager is read, and with neither the
// component does nothing.
// ----------------------------------------------------------------------------
using UnityEngine;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// Grab articulated-rig parts with the mouse: press on a part, drag in the
    /// camera-facing plane through the grab point, scroll to push / pull along the
    /// view ray. The input layer is thin -- <see cref="TryBegin"/>,
    /// <see cref="Drag"/> and <see cref="End"/> take rays, so a touch or a
    /// custom pointer drives the same code -- and the pointer it reads is
    /// <see cref="Pointer"/>, which a door or a script replaces
    /// (<see cref="UsePointer"/>).
    /// </summary>
    [AddComponentMenu("Animation/Articulated Rig Pointer Grab")]
    public class ArticulatedRigPointerGrab : MonoBehaviour
    {
        [Tooltip("The camera the pointer looks through (Camera.main when empty).")]
        public Camera viewCamera;

        [Tooltip("Mouse button that grabs: 0 left, 1 right, 2 middle.")]
        [Range(0, 2)]
        public int button;

        [Tooltip("Layers the grab ray hits.")]
        public LayerMask layers = ~0;

        [Tooltip("Farthest a grab ray reaches (world units).")]
        public float maxDistance = 1000f;

        [Tooltip("Push / pull per scroll step, as a fraction of the grab point's distance from the camera.")]
        [Range(0f, 1f)]
        public float scrollStep = 0.1f;

        [Tooltip("The nearest the scroll pulls a grab point to the camera (world units).")]
        public float minDepth = 0.01f;

        [Tooltip("Grab only this rig (any ArticulatedRig hit when empty).")]
        public ArticulatedRig rig;

        ArticulatedRig _held;
        float _depth;

        // The registry of one the input door fills on load (a domain reload clears
        // it, and the door registers again as the player loads).
        static IPointerInput _pointer;

        /// <summary>The rig being dragged (null when none).</summary>
        public ArticulatedRig Held => _held;

        /// <summary>The grab point's distance in front of the camera, along its forward axis.</summary>
        public float Depth => _depth;

        /// <summary>
        /// The pointer every grabber reads: the one last registered with
        /// <see cref="UsePointer"/>, else the legacy Input Manager's mouse (which
        /// reads nothing where the project has the legacy handler off).
        /// </summary>
        public static IPointerInput Pointer => _pointer ?? LegacyPointer.Instance;

        /// <summary>
        /// Read the pointer from <paramref name="pointer"/> from now on (null: the
        /// legacy Input Manager again). The Input System door registers the Input
        /// System's mouse as the player loads; a touch or a custom pointer
        /// registers its own.
        /// </summary>
        public static void UsePointer(IPointerInput pointer)
        {
            _pointer = pointer;
        }

        Camera ViewCamera => viewCamera != null ? viewCamera : Camera.main;

        void Update()
        {
            var cam = ViewCamera;
            if (cam == null) return;
            IPointerInput pointer = Pointer;
            if (!pointer.Available(button)) return;
            if (_held == null)
            {
                if (pointer.Pressed(button))
                    TryBegin(cam.ScreenPointToRay(pointer.Position), cam.transform);
                return;
            }
            if (!pointer.Held(button))
            {
                End();
                return;
            }
            Drag(cam.ScreenPointToRay(pointer.Position), cam.transform, pointer.Scroll);
        }

        void OnDisable()
        {
            End();
        }

        /// <summary>
        /// Raycast <paramref name="ray"/> and take hold of the grab part it hits.
        /// <paramref name="view"/> is the camera: the drag plane faces it.
        /// Returns true when a part was taken.
        /// </summary>
        public bool TryBegin(Ray ray, Transform view)
        {
            End();
            if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, layers, QueryTriggerInteraction.Collide))
                return false;
            Transform part = hit.collider.transform;
            ArticulatedRig target = null;
            if (rig != null)
            {
                if (rig.BeginGrab(part, hit.point)) target = rig;
            }
            else
            {
                // The nearest rig above the hit that has it as a grab part (a node may hold several rigs).
                foreach (ArticulatedRig candidate in part.GetComponentsInParent<ArticulatedRig>())
                    if (candidate.BeginGrab(part, hit.point))
                    {
                        target = candidate;
                        break;
                    }
            }
            if (target == null) return false;
            _held = target;
            _depth = Vector3.Dot(hit.point - view.position, view.forward);
            return true;
        }

        /// <summary>
        /// Move the held point to where <paramref name="ray"/> crosses the
        /// camera-facing plane at the grab depth, after moving that plane
        /// <paramref name="scroll"/> steps away from the camera (negative: toward).
        /// </summary>
        public void Drag(Ray ray, Transform view, float scroll)
        {
            if (_held == null) return;
            if (scroll != 0f)
                _depth = Mathf.Max(minDepth, _depth * (1f + scrollStep * Mathf.Sign(scroll)));
            _held.UpdateGrab(PlanePoint(ray, view.position, view.forward, _depth));
        }

        /// <summary>Let go of the held part (its pose holds until the rig returns to its animation).</summary>
        public void End()
        {
            if (_held != null) _held.EndGrab();
            _held = null;
        }

        /// <summary>
        /// Where <paramref name="ray"/> crosses the plane facing
        /// <paramref name="viewForward"/> at <paramref name="depth"/> in front of
        /// <paramref name="viewPosition"/> (the ray's point at that depth when
        /// it runs parallel to the plane).
        /// </summary>
        public static Vector3 PlanePoint(Ray ray, Vector3 viewPosition, Vector3 viewForward, float depth)
        {
            float along = Vector3.Dot(ray.direction, viewForward);
            float from = Vector3.Dot(ray.origin - viewPosition, viewForward);
            if (Mathf.Abs(along) < 1e-6f) return ray.origin + ray.direction * depth;
            return ray.GetPoint((depth - from) / along);
        }

        /// <summary>The legacy Input Manager's mouse: the pointer when none is registered.</summary>
        sealed class LegacyPointer : IPointerInput
        {
            public static readonly LegacyPointer Instance = new LegacyPointer();

#if ENABLE_LEGACY_INPUT_MANAGER
            public bool Available(int button) => Input.mousePresent;
            public bool Pressed(int button) => Input.GetMouseButtonDown(button);
            public bool Held(int button) => Input.GetMouseButton(button);
            public Vector3 Position => Input.mousePosition;
            public float Scroll => Input.mouseScrollDelta.y;
#else
            public bool Available(int button) => false;
            public bool Pressed(int button) => false;
            public bool Held(int button) => false;
            public Vector3 Position => Vector3.zero;
            public float Scroll => 0f;
#endif
        }
    }
}
