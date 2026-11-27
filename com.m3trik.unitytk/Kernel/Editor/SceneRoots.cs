using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// A scene's top-level objects by name: the one lookup every action that keeps its parts under a
    /// named top-level object (a sequence, a panel's root, a holder of cues) finds them by.
    /// </summary>
    /// <remarks>
    /// Prior art: <c>GameObject.Find</c> searches every loaded scene, active objects only, and matches a
    /// path from any depth; <c>Scene.GetRootGameObjects</c> is the documented list of one scene's roots,
    /// inactive ones included, which this searches.
    /// </remarks>
    public static class SceneRoots
    {
        /// <summary>
        /// The top-level object of <paramref name="scene"/> named <paramref name="name"/>: the first of a
        /// repeated name, in the scene's root order; null where none is (or the scene is not loaded).
        /// </summary>
        /// <param name="name">Its name, exactly.</param>
        /// <param name="scene">The scene (default: the active one).</param>
        /// <param name="includeInactive">Find a switched-off one too; false: active ones alone.</param>
        public static GameObject Find(string name, Scene scene = default, bool includeInactive = true)
            => Find(name, scene, includeInactive, null);

        /// <summary>
        /// The <typeparamref name="T"/> on the first top-level object of <paramref name="scene"/> named
        /// <paramref name="name"/> that has one; null where none does.
        /// </summary>
        /// <param name="name">Its name, exactly.</param>
        /// <param name="scene">The scene (default: the active one).</param>
        /// <param name="includeInactive">Find one on a switched-off object too; false: active ones alone.</param>
        public static T Find<T>(string name, Scene scene = default, bool includeInactive = true) where T : Component
        {
            GameObject found = Find(name, scene, includeInactive, typeof(T));
            return found != null ? found.GetComponent<T>() : null;
        }

        static GameObject Find(string name, Scene scene, bool includeInactive, Type holding)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (!scene.IsValid()) scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != name || !includeInactive && !root.activeSelf) continue;
                if (holding == null || root.GetComponent(holding) != null) return root;
            }
            return null;
        }
    }
}
