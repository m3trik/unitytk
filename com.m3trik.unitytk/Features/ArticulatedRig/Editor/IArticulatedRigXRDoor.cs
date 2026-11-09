using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.ArticulatedRig
{
    /// <summary>
    /// The seam <see cref="ArticulatedRigActions.MakeXRGrabbable"/> reaches the XR
    /// Interaction Toolkit through: the XRI door's editor half (compiled only with the
    /// toolkit in the project) registers one as the editor loads
    /// (<see cref="ArticulatedRigActions.XRDoor"/>), so the feature names no XR type and a
    /// project without the toolkit is told so.
    /// </summary>
    internal interface IArticulatedRigXRDoor
    {
        /// <summary>
        /// Give <paramref name="rig"/>'s root the XR setup, asking <paramref name="ctx"/> first; a
        /// rig that has it gets nothing. Returns whether the rig has it (in a dry run, would).
        /// </summary>
        bool MakeGrabbable(ArticulatedRig rig, OpContext ctx);
    }
}
