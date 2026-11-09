using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Lightmaps
{
    /// <summary>
    /// The mark a player build leaves in each scene Unity's bake owns (one with a
    /// Lighting Data Asset): a root object holding this, in the built scene alone. A player has no Lighting
    /// Data Asset to ask, so every <see cref="LightmapSet"/> there -- one the scene
    /// held, or one a prefab instantiated into it later -- looks for the mark in its own
    /// scene when it is enabled, and stands down where it finds one.
    /// </summary>
    [AddComponentMenu("")]
    internal sealed class LightmapBakeMark : MonoBehaviour
    {
        /// <summary>The name of the root object holding the mark.</summary>
        internal const string ObjectName = "unitytk: Unity's bake owns this scene";

        /// <summary>Whether <paramref name="scene"/> holds the mark (a root object with it).</summary>
        internal static bool In(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return false;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out LightmapBakeMark _)) return true;
            return false;
        }
    }
}
