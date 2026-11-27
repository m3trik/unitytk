// ----------------------------------------------------------------------------
// The Interaction feature's uGUI door (M3trik.Unitytk.Interaction.UGUI), compiled
// only with com.unity.ugui in the project: Unity's EventSystem, the one router a
// mouse and a touch reach a scene through.
// ----------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.EventSystems;

namespace M3trik.Unitytk.Interaction.UGUI
{
    /// <summary>
    /// A <see cref="Target"/> a pointer selects through Unity's EventSystem: a click on it, or
    /// on any collider under it, with its <see cref="button"/>, selects it for the hand named, else the
    /// scene's switched-on hand; the button held down presses it until let go (a press and its click, one
    /// gesture, are refused once: the target's rule, <see cref="Target.Select"/>). The camera needs a <c>PhysicsRaycaster</c> and the scene an EventSystem
    /// (<c>PointerActions.SetUpPointer</c>). A drag begun on it with the look's button turns the user's eyes, as one begun on
    /// empty space does (<see cref="PointerLook"/>).
    /// </summary>
    /// <remarks>
    /// Prior art: uGUI's <c>EventTrigger</c> routes any pointer event to UnityEvents, but a
    /// click carries no hand and a target selects for one; the XR Interaction Toolkit's
    /// interactables serve a headset's controllers, through the XRI door. The EventSystem is
    /// adopted as the router: it serves a mouse and a touch under either input backend, and a
    /// click on a UI panel stays on the panel instead of reaching the scene behind it.
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/Pointer Select")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Target))]
    public sealed class PointerSelect : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler,
                                        IInitializePotentialDragHandler, IDragHandler
    {
        [Tooltip("The hand a click selects for (the scene's switched-on hand when empty).")]
        public Hand hand;

        [Tooltip("The pointer button that selects and presses it.")]
        public PointerEventData.InputButton button = PointerEventData.InputButton.Left;

        /// <summary>The hand a click selects for: the one named, else the scene's switched-on one; null when none.</summary>
        public Hand Hand => hand != null ? hand : Hand.InScene();

        /// <summary>A click: its button selects the target.</summary>
        /// <param name="eventData">The EventSystem's click.</param>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == button) GetComponent<Target>().Select(Hand);
        }

        /// <summary>Its button down: the target is pressed until it is let go.</summary>
        /// <param name="eventData">The EventSystem's press.</param>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == button) GetComponent<Target>().Press(Hand);
        }

        /// <summary>A drag about to begin on it: the look's button's turns the eyes from the first pixel.</summary>
        /// <param name="eventData">The EventSystem's press.</param>
        public void OnInitializePotentialDrag(PointerEventData eventData)
        {
            PointerLook look = PointerLook.Of(eventData);
            if (look != null) look.OnInitializePotentialDrag(eventData);
        }

        /// <summary>A drag begun on it: the look's button's turns the user's eyes (its own button's is the press).</summary>
        /// <param name="eventData">The EventSystem's drag.</param>
        public void OnDrag(PointerEventData eventData)
        {
            PointerLook look = PointerLook.Of(eventData);
            if (look != null) look.Turn(eventData);
        }

        /// <summary>Its button let go (wherever the pointer is): the press ends.</summary>
        /// <param name="eventData">The EventSystem's release.</param>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != button) return;
            GetComponent<Target>().Release();
        }
    }
}
