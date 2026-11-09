using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Lightmaps
{
    /// <summary>
    /// Who owns a scene's baked lighting, as the editor knows it: Unity's bake, while
    /// the scene has a Lighting Data Asset of the project's, else its <see cref="LightmapSet"/>s. Registered
    /// with the runtime part whenever the editor's domain loads; a bake about to start
    /// takes every set's bindings down first (it bakes the scene as it is), and once a
    /// bake ends -- completed or cancelled -- or a clear changes a scene's owner, every
    /// set binds or stands down again. A player build marks each scene a bake owns
    /// (<see cref="OnProcessScene"/>, <see cref="LightmapBakeMark"/>), since a player has
    /// no Lighting Data Asset to ask.
    /// </summary>
    /// <remarks>
    /// <para>Every bake raises <c>bakeStarted</c>, then <c>bakeCompleted</c> or
    /// <c>bakeCancelled</c> (measured, 6000.3.10f1: a cancelled bake leaves the scene's
    /// last bake in place and raises no <c>bakeCompleted</c>). Unity documents those two
    /// as raised only while <c>LightingSettings.autoGenerate</c> is off, and from 6000.3
    /// it is always off: Auto Generate is no longer supported, and setting it reads back
    /// false (measured, 6000.3.10f1), so no bake goes unseen.</para>
    /// <para>Prior art: Unity's own <c>Lightmapping.GetLightingDataAssetForScene</c>, its bake
    /// events, and <c>IProcessSceneWithReport</c>, the documented hook for changing a
    /// scene as a build processes it.</para>
    /// </remarks>
    internal sealed class LightmapOwner : IProcessSceneWithReport
    {
        [InitializeOnLoadMethod]
        static void Register()
        {
            LightmapSet.BakeOwns = Baked;
            Lightmapping.bakeStarted += ReleaseAll;
            Lightmapping.bakeCompleted += RefreshAll;
            Lightmapping.bakeCancelled += RefreshAll;
            Lightmapping.lightingDataCleared += RefreshAll;
            Lightmapping.lightingDataAssetCleared += RefreshAll;
        }

        /// <summary>
        /// Whether Unity's bake owns <paramref name="scene"/>: it has a Lighting Data Asset of
        /// the project's (<see cref="Bake"/>).
        /// </summary>
        internal static bool Baked(Scene scene) => Bake(scene) != null;

        /// <summary>
        /// The Lighting Data Asset a bake wrote for <paramref name="scene"/>, or null: never
        /// the built-in one a scene no bake ever ran on reads its default environment from
        /// (measured, 6000.3.10f1: a new scene's is <c>LightingData</c> at
        /// <c>Resources/unity_builtin_extra</c>).
        /// </summary>
        internal static LightingDataAsset Bake(Scene scene)
        {
            LightingDataAsset bake = scene.IsValid() ? Lightmapping.GetLightingDataAssetForScene(scene) : null;
            string path = bake != null ? AssetDatabase.GetAssetPath(bake) : null;
            return path != null && (path.StartsWith("Assets/", System.StringComparison.Ordinal)
                                    || path.StartsWith("Packages/", System.StringComparison.Ordinal)) ? bake : null;
        }

        /// <summary>Every enabled set in the open scenes binds, or stands down, as its scene's owner now says.</summary>
        internal static void RefreshAll()
        {
            foreach (LightmapSet set in Object.FindObjectsByType<LightmapSet>(FindObjectsSortMode.None)) set.Refresh();
        }

        internal static void ReleaseAll()
        {
            foreach (LightmapSet set in Object.FindObjectsByType<LightmapSet>(FindObjectsSortMode.None)) set.Release();
        }

        /// <inheritdoc/>
        public int callbackOrder => 0;

        /// <summary>
        /// Mark <paramref name="scene"/>, as a player build processes it, where Unity's bake
        /// owns it (<see cref="Stamp"/>): what every set in it reads in the player. Play
        /// Mode (no <paramref name="report"/>) asks the editor instead, and is left as it is.
        /// </summary>
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            Stamp(scene);
        }

        /// <summary>
        /// Give <paramref name="scene"/> the mark of a scene Unity's bake owns
        /// (<see cref="LightmapBakeMark"/>, a root object) where it has a Lighting
        /// Data Asset, and take it away where it has none.
        /// </summary>
        internal static void Stamp(Scene scene)
        {
            bool baked = Baked(scene);
            var marks = new System.Collections.Generic.List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out LightmapBakeMark _)) marks.Add(root);
            if (baked && marks.Count == 0)
            {
                var mark = new GameObject(LightmapBakeMark.ObjectName);
                SceneManager.MoveGameObjectToScene(mark, scene);
                mark.AddComponent<LightmapBakeMark>();
            }
            else if (!baked)
                foreach (GameObject mark in marks) Object.DestroyImmediate(mark);
        }
    }
}
