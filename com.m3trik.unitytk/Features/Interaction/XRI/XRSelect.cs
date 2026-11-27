// ----------------------------------------------------------------------------
// The Interaction feature's XR Interaction Toolkit door (M3trik.Unitytk.Interaction.XRI),
// compiled only with the toolkit 3.x in the project: a headset's controllers.
// ----------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace M3trik.Unitytk.Interaction.XRI
{
    /// <summary>
    /// A <see cref="Target"/> a headset's controller selects: its <see cref="XRSimpleInteractable"/>'s
    /// select (a ray's, a poke's, a hand's touch, by the toolkit's own rules) selects the target
    /// for the <see cref="Hand"/> on or above the controller that selected, else the scene's
    /// switched-on hand, pressing it until the select ends (<see cref="OnSelectExited"/>): a press, then the selection
    /// ending its gesture, as a pointer's button down and click, so one select refused says so once (<see cref="Target.Select"/>). The
    /// interactable's select and its end call these through persistent calls, which the
    /// inspector shows. A target inside another (a screw on a panel) is selected as itself: each
    /// interactable is given only the colliders no target inside it owns, before the toolkit
    /// gathers every collider under it.
    /// </summary>
    /// <remarks>
    /// Prior art: the toolkit's <c>XRSimpleInteractable</c> is adopted as the thing a controller
    /// selects (its hover, select and feedback are the toolkit's, as XR users expect); it knows
    /// no hand of ours, and a target selects for one, so this passes the controller's hand.
    /// The toolkit's grab interactable moves what it grabs itself, which a <see cref="Holdable"/>
    /// does (posed each frame, lent to clips): a target takes a holdable into the hand on select.
    /// </remarks>
    [AddComponentMenu("unitytk/Interaction/XR Select")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]  // before the toolkit's interactables gather their colliders
    [RequireComponent(typeof(Target))]
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class XRSelect : MonoBehaviour
    {
        void Awake() => ClaimColliders();

        /// <summary>
        /// Give the interactable the colliders under it that no target inside it owns (what its
        /// <c>Awake</c> does, before the toolkit's interactable gathers every collider under it);
        /// colliders given it by hand are kept.
        /// </summary>
        public void ClaimColliders()
        {
            var interactable = GetComponent<XRBaseInteractable>();
            if (interactable == null || interactable.colliders.Count > 0) return;  // given by hand: kept
            foreach (Collider each in GetComponentsInChildren<Collider>(true))
                if (each.GetComponentInParent<XRBaseInteractable>(true) == interactable) interactable.colliders.Add(each);
        }

        /// <summary>A controller selected the interactable: select the target for its hand.</summary>
        /// <param name="args">The toolkit's select.</param>
        public void OnSelectEntered(SelectEnterEventArgs args)
        {
            Hand hand = null;
            if (args != null && args.interactorObject != null && args.interactorObject.transform != null)
                hand = args.interactorObject.transform.GetComponentInParent<Hand>();
            if (hand == null) hand = Hand.InScene();
            Target target = GetComponent<Target>();
            target.Press(hand);
            target.Select(hand);
        }

        /// <summary>The controller let go: the press its select began ends.</summary>
        /// <param name="args">The toolkit's select end.</param>
        public void OnSelectExited(SelectExitEventArgs args) => GetComponent<Target>().Release();
    }
}
