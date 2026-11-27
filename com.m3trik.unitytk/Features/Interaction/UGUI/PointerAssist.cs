using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace M3trik.Unitytk.Interaction.UGUI
{
    /// <summary>
    /// A camera's aim assist for the EventSystem: a pointer that misses every target in play
    /// (a click just off a screw, a part a few pixels wide) reaches the one nearest it within
    /// <see cref="angle"/> degrees that the eye can see, so one click selects it. A pointer on
    /// a target, a panel (a trigger collider) or nothing near a target is left to the other
    /// raycasters. It sits beside the camera's <c>PhysicsRaycaster</c>
    /// (<c>PointerActions.SetUpPointer</c>).
    /// </summary>
    /// <remarks>
    /// Prior art: the XR Interaction Toolkit's ray interactor casts a cone
    /// (<c>XRRayInteractor.hitDetectionType = ConeCast</c>) for the same reason, a headset's
    /// share of it; games call it target magnetism. The EventSystem picks the nearest result
    /// across raycasters, so this adds one only when no target was hit, at distance 0, and
    /// only targets switched on take part (<see cref="Target.Enabled"/>): a step's own.
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/Pointer Assist")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class PointerAssist : BaseRaycaster
    {
        /// <summary>The <see cref="angle"/> of an assist just added, in degrees.</summary>
        public const float DefaultAngle = 4f;

        [Tooltip("How far off a target, in degrees from the pointer's ray, a click still selects it (0: no assist).")]
        [Range(0f, 15f)]
        public float angle = DefaultAngle;

        [Tooltip("The layers it sees targets and what hides them on.")]
        public LayerMask layers = Physics.DefaultRaycastLayers;

        Camera _camera;
        readonly List<Collider> _colliders = new List<Collider>();

        /// <inheritdoc/>
        public override Camera eventCamera => _camera != null ? _camera : _camera = GetComponent<Camera>();

        /// <inheritdoc/>
        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            Camera camera = eventCamera;
            if (camera == null || angle <= 0f || !camera.pixelRect.Contains(eventData.position)) return;
            Ray ray = camera.ScreenPointToRay(eventData.position);
            float far = camera.farClipPlane;
            // On a target, or on a panel: those raycasters have it.
            if (Physics.Raycast(ray, out RaycastHit hit, far, layers, QueryTriggerInteraction.Collide)
                && (hit.collider.isTrigger || hit.collider.GetComponentInParent<Target>() != null)) return;
            Target best = Pick(ray, Target.Enabled, angle, far, layers, out Vector3 point, out float _, _colliders);
            if (best == null) return;
            resultAppendList.Add(new RaycastResult
            {
                gameObject = best.gameObject,
                module = this,
                distance = 0f,
                worldPosition = point,
                worldNormal = -ray.direction,
                screenPosition = eventData.position,
                displayIndex = camera.targetDisplay,
                index = resultAppendList.Count,
                sortingLayer = 0,
                sortingOrder = 0,
            });
        }

        /// <summary>
        /// The target among <paramref name="among"/> nearest <paramref name="ray"/>, by the angle to
        /// the nearest point of its colliders' bounds, within <paramref name="maxAngle"/> degrees and
        /// <paramref name="far"/> metres, seen from the ray's origin (nothing solid on
        /// <paramref name="layers"/> stands in front of it); the nearer wins a tie. Null when none is.
        /// </summary>
        /// <param name="ray">The pointer's ray.</param>
        /// <param name="among">The targets to pick from (those switched on).</param>
        /// <param name="maxAngle">How far off the ray, in degrees.</param>
        /// <param name="far">How far along it.</param>
        /// <param name="layers">The layers what hides a target is on.</param>
        /// <param name="point">The picked target's nearest point.</param>
        /// <param name="distance">Its distance from the ray's origin.</param>
        /// <param name="buffer">A list to gather colliders in (a per-frame caller passes its own; null: a new one).</param>
        public static Target Pick(Ray ray, IReadOnlyList<Target> among, float maxAngle, float far, LayerMask layers,
                                  out Vector3 point, out float distance, List<Collider> buffer = null)
        {
            Target best = null;
            float bestAngle = maxAngle;
            point = default;
            distance = 0f;
            List<Collider> colliders = buffer ?? new List<Collider>();
            for (int i = 0; i < among.Count; i++)
            {
                Target target = among[i];
                if (target == null || !target.isActiveAndEnabled) continue;
                target.GetComponentsInChildren(false, colliders);
                foreach (Collider collider in colliders)
                {
                    if (!collider.enabled || collider.isTrigger || collider.GetComponentInParent<Target>() != target) continue;
                    Bounds bounds = collider.bounds;
                    float along = Mathf.Max(0f, Vector3.Dot(bounds.center - ray.origin, ray.direction));
                    Vector3 near = bounds.ClosestPoint(ray.origin + ray.direction * along);
                    Vector3 to = near - ray.origin;
                    float d = to.magnitude;
                    if (d > far || d <= 0f) continue;
                    float off = Vector3.Angle(ray.direction, to);
                    if (off > bestAngle || off == bestAngle && best != null && d >= distance) continue;
                    if (Hidden(ray.origin, near, d, target, layers)) continue;
                    best = target;
                    bestAngle = off;
                    point = near;
                    distance = d;
                }
            }
            return best;
        }

        // Whether something solid, not the target's own, stands between the eye and a point of it.
        static bool Hidden(Vector3 eye, Vector3 point, float d, Target target, LayerMask layers)
        {
            if (!Physics.Raycast(eye, (point - eye) / d, out RaycastHit hit, d * 0.98f, layers, QueryTriggerInteraction.Ignore))
                return false;
            return hit.collider.GetComponentInParent<Target>() != target;
        }
    }
}
