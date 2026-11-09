// ----------------------------------------------------------------------------
// The ArticulatedRig feature's XR Interaction Toolkit door, in the XR-only
// assembly M3trik.Unitytk.ArticulatedRig.XRI (see ArticulatedRigXRGrab.cs).
//
// The one component a user adds to make an imported articulated rig grabbable
// by XR hands: on Awake it finds the rig's grab parts and gives each an
// ArticulatedRigXRGrab (and a convex collider where the part has none).
// ----------------------------------------------------------------------------
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace M3trik.Unitytk.ArticulatedRig.XRI
{
    /// <summary>
    /// Add to the model root of an imported articulated rig: at Awake every grab
    /// part of its <see cref="ArticulatedRig"/> becomes an XR
    /// interactable (<see cref="ArticulatedRigXRGrab"/>), so an XR Interaction
    /// Toolkit interactor grabs and poses the rig.
    /// </summary>
    [AddComponentMenu("XR/Articulated Rig XR Setup")]
    public class ArticulatedRigXRSetup : MonoBehaviour
    {
        [Tooltip("The rig to make grabbable (on this object or below when empty).")]
        public ArticulatedRig rig;

        [Tooltip("Pass the hand's rotation to the rig (a link on a ball joint turns with the hand).")]
        public bool useHandRotation = true;

        [Tooltip("Give grab-part meshes without a collider a convex MeshCollider first (the importer " +
                 "already does on import; this covers rigs built or edited at runtime).")]
        public bool addColliders = true;

        readonly List<ArticulatedRigXRGrab> _grabs = new List<ArticulatedRigXRGrab>();

        /// <summary>The interactables <see cref="Build"/> made or found, one per grab part.</summary>
        public IReadOnlyList<ArticulatedRigXRGrab> Grabs => _grabs;

        void Awake()
        {
            Build();
        }

        /// <summary>
        /// Give every grab part an <see cref="ArticulatedRigXRGrab"/> (a part
        /// that already carries an XR interactable of any kind is left as it is,
        /// and counted only when it is ours). Idempotent; returns the
        /// interactables.
        /// </summary>
        public IReadOnlyList<ArticulatedRigXRGrab> Build()
        {
            _grabs.Clear();
            if (rig == null) rig = GetComponentInChildren<ArticulatedRig>(true);
            if (rig == null)
            {
                Debug.LogWarning($"[ArticulatedRig] '{name}': no ArticulatedRig on this object " +
                                 "or below -- nothing to make grabbable.", this);
                return _grabs;
            }
            foreach (ArticulatedRig.GrabPart grab in rig.grabs)
            {
                Transform node = grab != null ? grab.part : null;
                if (node == null) continue;
                if (addColliders) ArticulatedRig.AddGrabColliders(node);
                var existing = node.GetComponent<XRBaseInteractable>();
                if (existing != null)
                {
                    if (existing is ArticulatedRigXRGrab ours) _grabs.Add(ours);
                    continue;
                }
                // XRI's Awake (collider gathering, registration) runs inside
                // AddComponent in play mode; it needs only the colliders made
                // above, so the fields can follow.
                var interactable = node.gameObject.AddComponent<ArticulatedRigXRGrab>();
                interactable.rig = rig;
                interactable.part = node;
                interactable.useHandRotation = useHandRotation;
                _grabs.Add(interactable);
            }
            return _grabs;
        }
    }
}
