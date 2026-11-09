using System;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace M3trik.Unitytk.Shots
{
    /// <summary>
    /// The Shots feature's actions: what a person's menu item and an agent's code do
    /// to a project. Each takes an optional <see cref="OpContext"/>: a dry run plans, a
    /// menu's run is one undo step, and none just runs.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's inspector adds a list element by hand, with no dry run and
    /// no way for an agent to call it; the kernel's <see cref="OpContext"/> gives a
    /// plain method both, as it does every feature's actions.
    /// </remarks>
    public static class ShotsActions
    {
        /// <summary>
        /// Give <paramref name="clip"/> a shot entry on <paramref name="target"/>'s
        /// <see cref="ShotList"/> (added when it has none): how a project with no DCC
        /// behind it makes a shot, by picking a clip. A clip that has an entry there
        /// already is left alone, so a second run plans nothing. An imported model, its
        /// instance and any prefab whose list the model delivers are refused: the
        /// delivered entries are the source's, rewritten at every import, and an entry
        /// added on an instance would be a prefab override Unity matches by list
        /// position, which the next import that changes the list misaligns.
        /// </summary>
        /// <param name="target">The object whose Animator or Animation plays the clip: a scene object or a prefab of the project's own, never an imported model or its instance.</param>
        /// <param name="clip">The clip the shot is.</param>
        /// <param name="description">What the shot shows, as free text.</param>
        /// <param name="ctx">The run's context: a dry run lists the changes; null just makes them.</param>
        /// <returns>Whether it added the entry (in a dry run, would add).</returns>
        /// <exception cref="ArgumentNullException"><paramref name="target"/> or <paramref name="clip"/> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="target"/> is an imported model, an instance
        /// of one, or holds a <see cref="ShotList"/> a model delivers, whose entries every import rewrites:
        /// add a <see cref="ShotList"/> to an object of your own.</exception>
        public static bool AddEntry(GameObject target, AnimationClip clip, string description = "", OpContext ctx = null)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            ShotList shots = target.GetComponent<ShotList>();
            if (Delivered(target, shots))
                throw new ArgumentException(
                    $"'{target.name}' is an imported model or an instance of one, whose shots every import rewrites " +
                    "from its source (an entry added here would be matched by list position, and misaligned by the " +
                    "next import): add a ShotList to an object of your own, whose Animator or Animation plays the " +
                    "clip. The delivered entries are the source's.", nameof(target));
            if (shots != null && shots.Entry(clip) != null) return false;
            if (shots == null && (ctx == null || ctx.ShouldCreate($"{target.name}/ShotList", "add ShotList")))
                shots = ctx.AddComponent<ShotList>(target);

            string action = $"add the shot entry for clip '{clip.name}'";
            if (shots == null)
            {
                // A dry run that planned the component: plan the entry on it too.
                ctx.ShouldChange(target.name, action);
                return true;
            }
            if (ctx == null || ctx.ShouldChange(shots, action))
            {
                shots.entries.Add(new Shot { clip = clip, description = description ?? "" });
                ctx.Edited(shots);
            }
            return true;
        }

        // Whether the target is an imported model or part of an instance of one (the
        // asset, a scene instance, a nested instance), or holds a list a model
        // delivers through a prefab of the project's own (a variant of the model).
        static bool Delivered(GameObject target, ShotList shots)
        {
            if (PrefabUtility.IsPartOfModelPrefab(target)) return true;
            if (shots == null) return false;
            var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(shots);
            return source != null && source != shots && PrefabUtility.IsPartOfModelPrefab(source);
        }
    }
}
