using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Manifests
{
    /// <summary>
    /// What a kit builds around a plan's rows: the parts its behaviors share (a sequence the steps
    /// run in, a panel their pages are on) before any row is built, and what follows from all of
    /// them after. A class implementing it with a public constructor without parameters is found
    /// wherever it is compiled, and takes part in every build of a plan that holds a row of its own
    /// (<see cref="ManifestBehaviors.Kits"/>: a behavior its assembly builds, or one marked with its
    /// attribute): a plan with none of a kit's rows gets none of its parts.
    /// </summary>
    public interface IManifestKit
    {
        /// <summary>
        /// The kit's own settings (a <see cref="UnityEngine.ScriptableObject"/> type a manifest keeps
        /// beside its plan, <see cref="Manifest.Settings{T}"/>); null where it has none.
        /// </summary>
        Type Settings { get; }

        /// <summary>
        /// Build what the kit's rows build on, before any row is: asking <paramref name="ctx"/> before
        /// each change, each step's own part inside <see cref="ManifestScene.For"/>, so the manifest
        /// counts it for that step.
        /// </summary>
        /// <param name="scene">The delivery the plan is built into (its <see cref="ManifestScene.Plan"/>).</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        void Begin(ManifestScene scene, OpContext ctx);

        /// <summary>Build what follows from the rows, once every row is built (as <see cref="Begin"/> builds).</summary>
        /// <param name="scene">The delivery the plan is built into.</param>
        /// <param name="ctx">The run's context.</param>
        void End(ManifestScene scene, OpContext ctx);

        /// <summary>
        /// Keep what a person adjusted in the open scene of what the kit built (a spot moved), so the
        /// build about to run puts it back there: in a dry run too, where nothing is written and the
        /// build that follows in <paramref name="scene"/> must still plan by what would be kept.
        /// </summary>
        /// <param name="manifest">The manifest about to be built.</param>
        /// <param name="scene">The delivery the build then runs in; null where it runs in a new scene, or the delivery is not placed yet.</param>
        /// <param name="ctx">The run's context.</param>
        void Keep(Manifest manifest, ManifestScene scene, OpContext ctx);

        /// <summary>Discard every adjustment <see cref="Keep"/> kept: the next build puts each part where it finds it best.</summary>
        /// <param name="manifest">The manifest about to be built anew.</param>
        /// <param name="ctx">The run's context.</param>
        void Forget(Manifest manifest, OpContext ctx);

        /// <summary>
        /// What <see cref="Keep"/> keeps (or would) of the open scene, for the manifest to list with its own adjustments
        /// (<see cref="ManifestsActions.Adjustments"/>): each names its part, what was adjusted and what the build gave
        /// (<see cref="ManifestAdjustment.Of"/>). A kit that keeps nothing of its own returns none.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="scene">The delivery it is built into, in the open scene.</param>
        /// <returns>The adjustments the kit keeps.</returns>
        IEnumerable<ManifestAdjustment> Kept(Manifest manifest, ManifestScene scene);

        /// <summary>
        /// Give what <see cref="Kept"/> lists for <paramref name="part"/> (null: for every part) what the build gave it
        /// again, and keep it no longer (<see cref="ManifestsActions.RevertAdjustments"/>).
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="scene">The delivery it is built into, in the open scene.</param>
        /// <param name="part">A part of the open scene; null: every part.</param>
        /// <param name="ctx">The run's context: asked before each change.</param>
        /// <returns>How many went (or would, in a dry run).</returns>
        int Revert(Manifest manifest, ManifestScene scene, Component part, OpContext ctx);

        /// <summary>
        /// Have what the kit placed by a search (a viewpoint, an arrow, a picture's camera) on <paramref name="part"/> (null:
        /// every one) placed anew by the next build: its first placement, and any move a person made of it, go
        /// (<see cref="ManifestsActions.PlaceAgain(Manifest, Component, OpContext)"/>). What someone made and a row names is no search's, and stays.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="scene">The delivery it is built into, in the open scene.</param>
        /// <param name="part">A part of the open scene; null: every part.</param>
        /// <param name="ctx">The run's context: asked before each change.</param>
        /// <returns>How many it will place anew (or would, in a dry run).</returns>
        int PlaceAgain(Manifest manifest, ManifestScene scene, Component part, OpContext ctx);
    }
}
