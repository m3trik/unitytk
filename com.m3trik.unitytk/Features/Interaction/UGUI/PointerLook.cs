// ----------------------------------------------------------------------------
// The Interaction feature's uGUI door (M3trik.Unitytk.Interaction.UGUI), compiled
// only with com.unity.ugui in the project: Unity's EventSystem, the one router a
// mouse and a touch reach a scene through.
// ----------------------------------------------------------------------------
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace M3trik.Unitytk.Interaction.UGUI
{
    /// <summary>
    /// A desktop user's look: a drag with <see cref="button"/> (the right mouse button) turns the
    /// camera it is on, about where it stands. The drag is the EventSystem's, so it works under
    /// either input backend with no input package named, and a drag begun on a panel stays the
    /// panel's. A drag begun on a <see cref="Target"/> turns the eyes too
    /// (<see cref="PointerSelect"/> hands it on).
    /// </summary>
    /// <remarks>
    /// It is a raycaster that answers every pointer on its camera's view, behind everything
    /// else (the farthest hit there is), so a drag over empty space has something to begin on:
    /// the EventSystem sends a drag only to what the pointer was pressed over. It answers no
    /// click and no press.
    /// <para>Prior art: Cinemachine's input axis controllers and the Starter Assets' look
    /// read the Input System (a package, and a rig of their own); the XR Device Simulator
    /// drives a headset's pose. A desktop user who stands at viewpoints and only turns needs no
    /// more than one drag handler on the camera.</para>
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/Pointer Look")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class PointerLook : BaseRaycaster, IInitializePotentialDragHandler, IDragHandler
    {
        /// <summary>The <see cref="degreesPerPixel"/> of a look just added.</summary>
        public const float DefaultDegreesPerPixel = 0.15f;

        [Tooltip("The pointer button a drag turns the eyes with.")]
        public PointerEventData.InputButton button = PointerEventData.InputButton.Right;

        [Tooltip("Degrees the eyes turn per pixel the pointer moves.")]
        public float degreesPerPixel = DefaultDegreesPerPixel;

        [Tooltip("The furthest the eyes tilt up or down, in degrees.")]
        [Range(0f, 89f)]
        public float maxPitch = 80f;

        Camera _camera;

        /// <inheritdoc/>
        public override Camera eventCamera => _camera != null ? _camera : _camera = GetComponent<Camera>();

        /// <summary>The look on the camera <paramref name="eventData"/> was pressed through; null where it has none.</summary>
        /// <param name="eventData">A pointer's event.</param>
        public static PointerLook Of(PointerEventData eventData)
        {
            Camera pressed = eventData != null ? (eventData.pressEventCamera != null ? eventData.pressEventCamera : eventData.enterEventCamera) : null;
            return pressed != null && pressed.TryGetComponent(out PointerLook look) && look.isActiveAndEnabled ? look : null;
        }

        /// <summary>Answer every pointer on its camera's view, behind every other hit.</summary>
        /// <param name="eventData">The pointer.</param>
        /// <param name="resultAppendList">The hits so far; its own is added.</param>
        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            Camera camera = eventCamera;
            if (camera == null || !camera.pixelRect.Contains(eventData.position)) return;
            resultAppendList.Add(new RaycastResult
            {
                gameObject = gameObject,
                module = this,
                distance = float.MaxValue,
                worldPosition = camera.transform.position + camera.transform.forward * camera.farClipPlane,
                worldNormal = -camera.transform.forward,
                screenPosition = eventData.position,
                displayIndex = camera.targetDisplay,
                index = resultAppendList.Count,
                sortingLayer = 0,
                sortingOrder = 0,
            });
        }

        /// <summary>A drag about to begin: its own button's turns the eyes from the first pixel.</summary>
        /// <param name="eventData">The EventSystem's press.</param>
        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            if (eventData.button == button) eventData.useDragThreshold = false;
        }

        /// <summary>A drag begun on empty space: its own button's turns the eyes.</summary>
        /// <param name="eventData">The EventSystem's drag.</param>
        public void OnDrag(PointerEventData eventData) => Turn(eventData);

        /// <summary>Turn the eyes by a drag with its own button (another button's does nothing).</summary>
        /// <param name="eventData">A pointer's drag.</param>
        public void Turn(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == button) Turn(eventData.delta);
        }

        /// <summary>
        /// Turn the eyes by <paramref name="pixels"/> of pointer travel: across turns them about
        /// the upright, up and down tilts them, no further than <see cref="maxPitch"/>. From the
        /// way they face now, so a viewpoint that turned them is turned from.
        /// </summary>
        /// <param name="pixels">How far the pointer moved, in pixels.</param>
        public void Turn(Vector2 pixels)
        {
            Vector3 now = transform.localEulerAngles;
            float pitch = now.x > 180f ? now.x - 360f : now.x;
            pitch = Mathf.Clamp(pitch - pixels.y * degreesPerPixel, -maxPitch, maxPitch);
            transform.localRotation = Quaternion.Euler(pitch, now.y + pixels.x * degreesPerPixel, 0f);
        }
    }
}
