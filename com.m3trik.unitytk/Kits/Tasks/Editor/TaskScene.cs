using System.Collections.Generic;
using M3trik.Unitytk.Interaction;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.Manifests;
using M3trik.Unitytk.Placement;
using M3trik.Unitytk.Steps;
using UnityEngine;

namespace M3trik.Unitytk.Kits.Tasks
{
    /// <summary>
    /// A <see cref="ManifestScene"/> as the Tasks kit's builders use it: the sequence its steps run
    /// in, the adjustments its spots are put back to, and a part switched on by a step, which is also
    /// said to be the row's (<see cref="ManifestScene.Hold"/>), so the manifest lists it there.
    /// </summary>
    public static class TaskScene
    {
        /// <summary>The name of the sequence the plan's steps run in.</summary>
        /// <param name="scene">The delivery.</param>
        public static string Sequence(this ManifestScene scene) => scene.State<TaskState>().Sequence;

        /// <summary>The adjustments the build puts its spots back to: its manifest's moved helpers (<see cref="Kept"/>), read once.</summary>
        /// <param name="scene">The delivery.</param>
        public static Placements.Tuning Tuning(this ManifestScene scene)
        {
            TaskState state = scene.State<TaskState>();
            return state.Tuning ?? (state.Tuning = Kept(scene.Manifest, out _));
        }

        /// <summary>
        /// The moved helpers <paramref name="manifest"/> keeps (<see cref="Manifest.moved"/>), as the build places by them:
        /// each by its key, where a person put it relative to what it rides; none for no manifest.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        /// <param name="unread">The entries that read as no place (a hand edit), kept as they are.</param>
        public static Placements.Tuning Kept(Manifest manifest, out List<Manifest.Value> unread)
        {
            var tuning = new Placements.Tuning();
            unread = new List<Manifest.Value>();
            if (manifest == null) return tuning;
            foreach (Manifest.Value value in manifest.moved)
            {
                if (Placements.TryRead(value.name, value.value, out Placements.Tuned tuned)) tuning.spots.Add(tuned);
                else unread.Add(value);
            }
            return tuning;
        }

        /// <summary>
        /// Where a manifest's build keeps the twins its fades and highlights draw with
        /// (<see cref="RenderEffects.RenderEffectsActions.PrepareMaterials"/>): <c>&lt;plan&gt;.twins.asset</c> beside its plan,
        /// named for the plan as its other files are; empty for no manifest, or one with no plan.
        /// </summary>
        /// <param name="manifest">The manifest.</param>
        public static string TwinsPath(Manifest manifest)
        {
            string stem = ManifestsActions.PlanStem(manifest);
            return stem.Length == 0 ? "" : stem + ".twins.asset";
        }

        // A node named as the plan names it: its path under the delivery (its own name where it is a root of it), so the
        // delivery's own name, which a re-export under a new (dated) file name changes, is no part of what is keyed or
        // named by it; a node outside the delivery goes by where it stands in the scene.
        internal static string Under(this ManifestScene scene, Transform node)
        {
            var path = new System.Collections.Generic.List<string>();
            for (Transform t = node; t != null; t = t.parent)
            {
                foreach (GameObject root in scene.Roots)
                    if (root != null && root.transform == t)
                    {
                        path.Reverse();
                        return string.Join("|", path);
                    }
                path.Add(t.name);
            }
            return OpContextEdits.Label(node.gameObject);
        }

        // Interactions.MakeHoldable; the row holds the holdable. What a person sets on it after a build (where a tool's tip
        // is and how it is turned, for a tool rigged off its work; how it travels) is then theirs: kept by every build,
        // listed on the rows that put it in hand or work with it, carried into a scene built anew, put back by Revert.
        internal static Holdable MakeHoldable(this ManifestScene scene, GameObject node, OpContext ctx)
        {
            Holdable made = Interactions.MakeHoldable(node, ctx);
            scene.Hold(made);
            return made;
        }

        /// <summary><see cref="StepsActions.EnableDuring{T}(string, string, GameObject, T, OpContext)"/> in the scene's sequence; the row holds <paramref name="part"/>.</summary>
        public static bool EnableDuring<T>(this ManifestScene scene, string id, GameObject node, T part, OpContext ctx) where T : Behaviour
        {
            scene.Hold(part);
            return StepsActions.EnableDuring(scene.Sequence(), id, node, part, ctx);
        }

        /// <summary><see cref="StepsActions.EnableDuring{T}(string, string, PlannedObject, OpContext)"/> in the scene's sequence: a part a dry run only planned.</summary>
        public static bool EnableDuring<T>(this ManifestScene scene, string id, PlannedObject node, OpContext ctx) where T : Behaviour
            => StepsActions.EnableDuring<T>(scene.Sequence(), id, node, ctx);

        /// <summary><see cref="StepsActions.EnableAtEnd{T}(string, string, GameObject, T, OpContext)"/> in the scene's sequence; the row holds <paramref name="part"/>.</summary>
        public static bool EnableAtEnd<T>(this ManifestScene scene, string id, GameObject node, T part, OpContext ctx) where T : Behaviour
        {
            scene.Hold(part);
            return StepsActions.EnableAtEnd(scene.Sequence(), id, node, part, ctx);
        }

        /// <summary><see cref="StepsActions.EnableAtEnd{T}(string, string, PlannedObject, OpContext)"/> in the scene's sequence: a part a dry run only planned.</summary>
        public static bool EnableAtEnd<T>(this ManifestScene scene, string id, PlannedObject node, OpContext ctx) where T : Behaviour
            => StepsActions.EnableAtEnd<T>(scene.Sequence(), id, node, ctx);

        /// <summary><see cref="StepsActions.EnableFrom{T}(string, string, GameObject, T, OpContext)"/> in the scene's sequence; the row holds <paramref name="part"/>.</summary>
        public static bool EnableFrom<T>(this ManifestScene scene, string id, GameObject node, T part, OpContext ctx) where T : Behaviour
        {
            scene.Hold(part);
            return StepsActions.EnableFrom(scene.Sequence(), id, node, part, ctx);
        }

        /// <summary><see cref="StepsActions.EnableFrom{T}(string, string, PlannedObject, OpContext)"/> in the scene's sequence: a part a dry run only planned.</summary>
        public static bool EnableFrom<T>(this ManifestScene scene, string id, PlannedObject node, OpContext ctx) where T : Behaviour
            => StepsActions.EnableFrom<T>(scene.Sequence(), id, node, ctx);

        /// <summary><see cref="StepsActions.DisableFrom{T}(string, string, GameObject, T, OpContext)"/> in the scene's sequence; the row holds <paramref name="part"/>.</summary>
        public static bool DisableFrom<T>(this ManifestScene scene, string id, GameObject node, T part, OpContext ctx) where T : Behaviour
        {
            scene.Hold(part);
            return StepsActions.DisableFrom(scene.Sequence(), id, node, part, ctx);
        }

        /// <summary><see cref="StepsActions.DisableFrom{T}(string, string, PlannedObject, OpContext)"/> in the scene's sequence: a part a dry run only planned.</summary>
        public static bool DisableFrom<T>(this ManifestScene scene, string id, PlannedObject node, OpContext ctx) where T : Behaviour
            => StepsActions.DisableFrom<T>(scene.Sequence(), id, node, ctx);
    }
}
