using System.Collections.Generic;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// A behavior whose settings are properties of what it builds for an object (an arrow's size and colour):
    /// a person's change to one in the scene is kept as the object's own value (<see cref="Manifest.tunes"/>) at
    /// the next build (<see cref="ManifestsActions.Build(Manifest, Kernel.OpContext, IEnumerable{string})"/>), so
    /// what they adjusted after a build stays, as what they set before one does. Optional beside
    /// <see cref="IManifestBehavior"/>.
    /// </summary>
    public interface IManifestBound
    {
        /// <summary>
        /// Each setting of <paramref name="action"/>'s behavior that a part it built for <paramref name="target"/>
        /// holds, with the part and its serialized property's name; none where it built nothing yet.
        /// </summary>
        /// <param name="action">The row.</param>
        /// <param name="step">Its step.</param>
        /// <param name="scene">The delivery it was built into.</param>
        /// <param name="target">One of the row's objects, as it names it; empty: the row itself (the step's own row, or, asked of
        /// a behavior with <see cref="ManifestBehaviorAttribute.RowSettings"/>, the row's settings as a whole, never an object's).</param>
        IEnumerable<(string setting, UnityEngine.Object part, string property)> Bound(SheetPlan.Shape.PlanAction action,
                                                                                      SheetPlan.Shape.PlanStep step, ManifestScene scene, string target);
    }
}
