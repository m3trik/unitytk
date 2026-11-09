// The Lightmaps sample's demo recipe: builds its demo scene from the imported sample.
using System;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Lightmaps;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Samples.Lightmaps
{
    /// <summary>
    /// Builds the Lightmaps demo scene from the imported sample: the model its import
    /// gave a <see cref="LightmapSet"/> (the floor's map and the boxes' atlas, each box
    /// its own rect, and the bake's reflection probe), placed in a new scene that no bake
    /// of Unity's owns, so the set binds the moment it is placed. The default scene's sun
    /// is taken out -- the maps hold the light, and a realtime light would light the
    /// boxes a second time -- and <see cref="LightmapsActions.Apply"/> makes the set the
    /// scene's owner, as a person runs it from Tools > unitytk > Lightmaps. Open the scene
    /// to see the boxes lit by the set alone, the metal box reflecting the probe and the
    /// lamp drawn into it; Generate Lighting there writes a Lighting Data Asset, and the
    /// set stands down.
    /// </summary>
    public static class LightmapsDemoRecipe
    {
        /// <summary>The package the sample ships in.</summary>
        public const string Package = "com.m3trik.unitytk";
        /// <summary>The sample's name in the Package Manager's Samples tab.</summary>
        public const string SampleName = "Lightmaps";
        /// <summary>The model, in the sample's folder.</summary>
        public const string ModelFile = "Lightmaps.fbx";
        /// <summary>The scene this builds, in the sample's folder.</summary>
        public const string SceneFile = "LightmapsDemo.unity";

        /// <summary>
        /// The folder the sample was imported to (<c>Assets/Samples/unitytk/&lt;version&gt;/Lightmaps</c>),
        /// or null when it is not imported.
        /// </summary>
        public static string Folder()
        {
            Sample sample = Sample.FindByPackage(Package, null).FirstOrDefault(s => s.displayName == SampleName);
            if (sample.displayName == null || !sample.isImported) return null;
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return "Assets" + full.Substring(assets.Length);
        }

        /// <summary>Build the demo scene, save it beside the model and open it.</summary>
        [MenuItem("Tools/unitytk/Samples/Lightmaps Demo")]
        static void BuildAndOpen()
        {
            string folder = Folder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog("Lightmaps Demo",
                    "Import the Lightmaps sample first: Window > Package Manager > unitytk > Samples > Lightmaps > Import.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(folder);
        }

        /// <summary>
        /// Build the demo scene from the sample in <paramref name="folder"/>: a new scene
        /// without the default sun, the model placed in it (its set binds), the set made the
        /// scene's owner (<see cref="LightmapsActions.Apply"/>) and the camera looking over the
        /// boxes toward the metal one; saved there as <see cref="SceneFile"/> and left open.
        /// </summary>
        /// <param name="folder">The imported sample's folder (<see cref="Folder"/>).</param>
        /// <returns>The scene, saved.</returns>
        /// <exception cref="FileNotFoundException">The folder holds no model.</exception>
        /// <exception cref="InvalidOperationException">The model carries no lightmap set: its import read no record.</exception>
        public static Scene Build(string folder)
        {
            string modelPath = $"{folder}/{ModelFile}";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new FileNotFoundException($"no {ModelFile} in {folder}: import the Lightmaps sample again");
            if (model.GetComponent<LightmapSet>() == null)
                throw new InvalidOperationException(
                    $"{modelPath} carries no Lightmap Set: its import read no lightmaps record (reimport it with unitytk installed)");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            // The maps hold the light: the default scene's sun would light the boxes again.
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(light.gameObject);
            PrefabUtility.InstantiatePrefab(model, scene);
            // The new scene has no bake of Unity's to clear: the set owns it, its maps as lightmaps.
            LightmapsActions.Apply();

            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = new Vector3(1.2f, 1.6f, -3.4f);
                camera.transform.LookAt(new Vector3(-0.1f, 0.3f, 0.2f));
            }
            EditorSceneManager.SaveScene(scene, $"{folder}/{SceneFile}");
            return scene;
        }
    }
}
