// The Shots sample's demo recipe: builds its demo scene from the imported sample.
using System;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Shots;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Samples.Shots
{
    /// <summary>
    /// Builds the Shots demo scene from the imported sample: the model its import
    /// gave a <see cref="ShotList"/>, placed in a new scene with a
    /// <see cref="ShotsDemo"/> that plays its shots in order and shows the playing
    /// shot's description. Enter Play Mode in the scene to watch it.
    /// </summary>
    public static class ShotsDemoRecipe
    {
        /// <summary>The package the sample ships in.</summary>
        public const string Package = "com.m3trik.unitytk";
        /// <summary>The sample's name in the Package Manager's Samples tab.</summary>
        public const string SampleName = "Shots";
        /// <summary>The model, in the sample's folder.</summary>
        public const string ModelFile = "Shots.fbx";
        /// <summary>The scene this builds, in the sample's folder.</summary>
        public const string SceneFile = "ShotsDemo.unity";

        /// <summary>
        /// The folder the sample was imported to (<c>Assets/Samples/unitytk/&lt;version&gt;/Shots</c>),
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
        [MenuItem("Tools/unitytk/Samples/Shots Demo")]
        static void BuildAndOpen()
        {
            string folder = Folder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog("Shots Demo",
                    "Import the Shots sample first: Window > Package Manager > unitytk > Samples > Shots > Import.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(folder);
        }

        /// <summary>
        /// Build the demo scene from the sample in <paramref name="folder"/>, save it
        /// there as <see cref="SceneFile"/> and leave it open.
        /// </summary>
        /// <param name="folder">The imported sample's folder (<see cref="Folder"/>).</param>
        /// <returns>The scene, saved.</returns>
        /// <exception cref="FileNotFoundException">The folder holds no model.</exception>
        /// <exception cref="InvalidOperationException">The model carries no shots: its import wrote no <see cref="ShotList"/>.</exception>
        public static Scene Build(string folder)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{ModelFile}");
            if (model == null) throw new FileNotFoundException($"no {ModelFile} in {folder}: import the Shots sample again");
            if (model.GetComponent<ShotList>() == null)
                throw new InvalidOperationException(
                    $"{folder}/{ModelFile} carries no shots: its import wrote no ShotList (reimport it with unitytk installed)");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            var demo = instance.AddComponent<ShotsDemo>();
            demo.shots = instance.GetComponent<ShotList>();
            EditorSceneManager.SaveScene(scene, $"{folder}/{SceneFile}");
            return scene;
        }
    }
}
