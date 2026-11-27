using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Placement;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// What the Tasks kit keeps on a scene while a plan is built into it
    /// (<see cref="Manifests.ManifestScene.State{T}"/>): the sequence its steps run in, and the
    /// adjustments its spots are put back to.
    /// </summary>
    public sealed class TaskState
    {
        /// <summary>The name of the sequence the plan's steps run in (<see cref="TaskBuild"/> names it before any row is built).</summary>
        public string Sequence = "";

        /// <summary>
        /// The adjustments the build puts its spots back to (where the user stands, where a picture is taken from, where an
        /// arrow points from): its manifest's moved helpers; null until read (<see cref="TaskScene.Tuning"/>).
        /// </summary>
        public Placements.Tuning Tuning;

        /// <summary>
        /// The camera this build made (or, in a dry run, plans to make) the user's rig, by its path (a planned object,
        /// which names a made one as its plan does); null where the scene had a rig, or none was asked for.
        /// </summary>
        public PlannedObject Rig;

        // The spots where tools work that this run placed, by their keys: a tool works at one spot on a part, which the
        // first row to use it there places and the others leave (Spots.Work).
        internal readonly System.Collections.Generic.HashSet<string> Worked = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);

        // The task's pattern settings parsed this run, by their options and text (null: one that does not parse): TaskKit.Pattern.
        internal readonly System.Collections.Generic.Dictionary<string, System.Text.RegularExpressions.Regex> Patterns =
            new System.Collections.Generic.Dictionary<string, System.Text.RegularExpressions.Regex>(System.StringComparer.Ordinal);
    }
}
