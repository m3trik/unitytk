using System;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.RenderEffects;
using UnityEditor;
using UnityEngine;

[assembly: InternalsVisibleTo("M3trik.Unitytk.Tools.RenderEffects.Tests")]

namespace M3trik.Unitytk.Tools.RenderEffects
{
    /// <summary>
    /// Tools ▸ unitytk ▸ Render Effects: a line per menu item over the feature's
    /// actions (<see cref="RenderEffectsActions"/>), run as a person's menu runs one: one undo
    /// step, progress with Cancel, and its issues in the Console.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>MenuItem</c>, the convention every Unity tool follows;
    /// Unity Search already finds each item, so there is no palette to build.
    /// </remarks>
    static class RenderEffectsMenu
    {
        const string Prepare = "Tools/unitytk/Render Effects/Prepare Materials";

        [MenuItem(Prepare)]
        static void PrepareMaterials() => PrepareSelected(Selection.gameObjects);

        /// <summary>
        /// The menu's Prepare Materials over <paramref name="selected"/>, run as a person's menu
        /// runs an action: a model's own object among them is passed over, said (its import
        /// makes its twins, and the action refuses it), so it never fails the rest.
        /// </summary>
        /// <returns>How many changes it made.</returns>
        internal static int PrepareSelected(GameObject[] selected)
            => OpContext.RunMenu("Prepare Materials", ctx =>
            {
                GameObject[] roots = Preparable(selected);
                int count = 0;
                foreach (GameObject root in roots)
                    count += RenderEffectsActions.PrepareMaterials(root, ctx: ctx);
                int passed = selected.Length - roots.Length;
                ctx.Log($"Prepare Materials: {count} change(s) for the {roots.Length} selected object(s)" +
                        (passed > 0 ? $"; {passed} model object(s) passed over (reimport a model to remake its twins)." : "."));
                return count;
            });

        [MenuItem(Prepare, true)]
        static bool CanPrepareMaterials() => Preparable(Selection.gameObjects).Length > 0;

        // The selected objects the action prepares: a scene's, or a prefab of the project's own; never a model's.
        static GameObject[] Preparable(GameObject[] selected)
            => Array.FindAll(selected, go => go != null && !(AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(go)) is ModelImporter));
    }
}
