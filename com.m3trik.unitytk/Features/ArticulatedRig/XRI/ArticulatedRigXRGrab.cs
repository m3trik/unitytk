// ----------------------------------------------------------------------------
// The ArticulatedRig feature's XR Interaction Toolkit door, its own assembly
// (M3trik.Unitytk.ArticulatedRig.XRI), which compiles ONLY when the XR
// Interaction Toolkit 3.x is in the project (the asmdef's versionDefines set
// UNITYTK_XRI and its defineConstraints require it) -- a project without XRI
// carries this folder inert, with no package dependency and no compile error.
//
// An XR Interaction Toolkit interactable that hands a grabbed part to the
// rig: select -> ArticulatedRig.BeginGrab at the interactor's
// attach pose, every Dynamic update -> UpdateGrab, select exit -> EndGrab.
// Deliberately an XRBaseInteractable and NOT an XRGrabInteractable: the part
// must not move itself (no Rigidbody, no attach) -- the rig's solve moves it
// through its joints.
// ----------------------------------------------------------------------------
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace M3trik.Unitytk.ArticulatedRig.XRI
{
    /// <summary>
    /// Makes one grab part of an articulated rig selectable by XR interactors;
    /// the selecting interactor's attach transform drives the rig's grab.
    /// <c>ArticulatedRigXRSetup</c> adds one to every grab part.
    /// </summary>
    /// <remarks>
    /// Prior art: XRI's <c>XRGrabInteractable</c> moves the grabbed object itself
    /// (a Rigidbody, an attach point), which would tear the part off its joint;
    /// <c>XRBaseInteractable</c> gives the select / update / exit events alone, so
    /// the rig's solve moves the part through its joints.
    /// </remarks>
    [AddComponentMenu("XR/Articulated Rig Grab")]
    public class ArticulatedRigXRGrab : XRBaseInteractable
    {
        [Tooltip("The rig this part belongs to (when empty, the nearest ArticulatedRig above that lists it as a grab part).")]
        public ArticulatedRig rig;

        [Tooltip("The grab part this interactable holds (this transform when empty).")]
        public Transform part;

        [Tooltip("Pass the hand's rotation to the rig (a link on a ball joint turns with the hand); " +
                 "off: the link keeps the rotation it was taken with.")]
        public bool useHandRotation = true;

        IXRSelectInteractor _holder;

        /// <summary>The resolved rig (null when the part is not under one).</summary>
        public ArticulatedRig Rig
        {
            get
            {
                if (rig != null) return rig;
                foreach (ArticulatedRig candidate in GetComponentsInParent<ArticulatedRig>())
                    if (candidate.FindGrab(Part, out _, out _))
                    {
                        rig = candidate;
                        break;
                    }
                return rig;
            }
        }

        /// <summary>The grab part this interactable holds.</summary>
        public Transform Part => part != null ? part : transform;

        /// <summary>True while the rig holds the grab part this interactable takes (never while
        /// another part of the rig is held, even one above it).</summary>
        public bool IsHolding
        {
            get
            {
                ArticulatedRig r = Rig;
                return r != null && r.IsGrabbing && r.FindGrab(Part, out Transform taken, out _) && taken == r.GrabbedPart;
            }
        }

        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args);
            var attach = AttachOf(args.interactorObject);
            if (attach != null && GrabAt(attach.position, attach.rotation))
                _holder = args.interactorObject;
        }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (_holder == null || updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic) return;
            var attach = AttachOf(_holder);
            if (attach != null) FollowTo(attach.position, attach.rotation);
        }

        protected override void OnSelectExited(SelectExitEventArgs args)
        {
            base.OnSelectExited(args);
            if (args.interactorObject == _holder) Release();
        }

        Transform AttachOf(IXRSelectInteractor interactor)
        {
            if (interactor == null) return null;
            var attach = interactor.GetAttachTransform(this);
            return attach != null ? attach : interactor.transform;
        }

        /// <summary>Take hold of the part at <paramref name="worldPoint"/>, the hand at
        /// <paramref name="handRotation"/> -- what a select does. Returns false when the rig refuses.</summary>
        public bool GrabAt(Vector3 worldPoint, Quaternion handRotation)
        {
            ArticulatedRig r = Rig;
            if (r == null) return false;
            return useHandRotation
                ? r.BeginGrab(Part, worldPoint, handRotation)
                : r.BeginGrab(Part, worldPoint);
        }

        /// <summary>Move the held point to <paramref name="worldPoint"/> -- what each update does while selected.</summary>
        public void FollowTo(Vector3 worldPoint, Quaternion handRotation)
        {
            // Another hand may have taken the rig since: only the holder steers.
            if (!IsHolding) return;
            if (useHandRotation) Rig.UpdateGrab(worldPoint, handRotation);
            else Rig.UpdateGrab(worldPoint);
        }

        /// <summary>Let go (the pose holds until the rig returns to its animation) -- what a select exit does.</summary>
        public void Release()
        {
            _holder = null;
            if (IsHolding) Rig.EndGrab();
        }
    }
}
