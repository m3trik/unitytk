using System.Collections.Generic;
using M3trik.Unitytk.Kernel;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// What builds one behavior of a plan (<c>use</c>, <c>highlight</c>, a project's own) into a
    /// scene, through the features' own actions: the manifest's unit of extension. A class
    /// implementing it, marked <see cref="ManifestBehaviorAttribute"/> with the behavior's name and
    /// holding a public constructor without parameters, is found wherever it is compiled (a kit's,
    /// or a project's beside a reading of its own wording).
    /// </summary>
    public interface IManifestBehavior
    {
        /// <summary>What keeps <paramref name="action"/> from being built here: empty when nothing does.</summary>
        /// <param name="action">The plan's action, of this behavior.</param>
        /// <param name="scene">The delivery it is built into.</param>
        IEnumerable<string> Check(SheetPlan.Shape.PlanAction action, ManifestScene scene);

        /// <summary>
        /// Build <paramref name="action"/> into <paramref name="scene"/>, asking <paramref name="ctx"/>
        /// before each change, and saying what it holds there (<see cref="ManifestScene.Hold"/>: each
        /// part it made or found made, which the manifest then lists under the row). What it raises on
        /// <paramref name="ctx"/> as it builds is the row's warning, said on the row alone: raise only
        /// what may be wrong, with its fix.
        /// </summary>
        /// <param name="action">The plan's action, of this behavior, which <see cref="Check"/> passed.</param>
        /// <param name="step">The step it is in.</param>
        /// <param name="scene">The delivery it is built into.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        void Apply(SheetPlan.Shape.PlanAction action, SheetPlan.Shape.PlanStep step, ManifestScene scene, OpContext ctx);
    }
}
