// ----------------------------------------------------------------------------
// The ArticulatedRig feature's XR Interaction Toolkit door, its editor half
// (M3trik.Unitytk.ArticulatedRig.XRI.Editor), compiled only with the toolkit
// 3.x in the project, as the door's runtime half is: it registers itself as the
// editor loads, so ArticulatedRigActions.MakeXRGrabbable reaches the toolkit's
// types without the feature naming one.
// ----------------------------------------------------------------------------
using M3trik.Unitytk.Kernel;
using UnityEditor;

namespace M3trik.Unitytk.ArticulatedRig.XRI
{
    /// <summary>
    /// <see cref="ArticulatedRigActions.MakeXRGrabbable"/>'s XR half: an
    /// <see cref="ArticulatedRigXRSetup"/> on the rig's root, holding the rig, which at Awake
    /// makes each grab part an <see cref="ArticulatedRigXRGrab"/>.
    /// </summary>
    /// <remarks>
    /// Prior art: XRI's own setup is a component added by hand on each interactable (Add
    /// Component, or its Starter Assets' prefabs); the setup on the rig's root does it for
    /// every grab part at once, as the rig lists them.
    /// </remarks>
    [InitializeOnLoad]
    sealed class ArticulatedRigXRDoor : IArticulatedRigXRDoor
    {
        static ArticulatedRigXRDoor() => ArticulatedRigActions.XRDoor = new ArticulatedRigXRDoor();

        public bool MakeGrabbable(ArticulatedRig rig, OpContext ctx)
        {
            // A node may hold several rigs, each with its own setup.
            foreach (ArticulatedRigXRSetup held in rig.GetComponents<ArticulatedRigXRSetup>())
                if (held.rig == rig) return true;
            if (ctx != null && !ctx.ShouldCreate($"{rig.name}/{nameof(ArticulatedRigXRSetup)}",
                                                 "add an XR setup: each grab part becomes an XR interactable")) return ctx.DryRun;
            ArticulatedRigXRSetup setup = ctx.AddComponent<ArticulatedRigXRSetup>(rig.gameObject);
            setup.rig = rig;
            ctx.Log($"Make XR Grabbable: an XR setup on '{rig.name}' for its {rig.grabs.Count} grab part(s).");
            return true;
        }
    }
}
