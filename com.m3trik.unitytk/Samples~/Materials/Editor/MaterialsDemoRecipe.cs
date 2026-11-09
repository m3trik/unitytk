// The Materials sample's demo recipe: builds its demo scene from the imported sample.
using System.IO;
using System.Linq;
using M3trik.Unitytk.Materials;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Samples.Materials
{
    /// <summary>
    /// Builds the Materials demo scene from the imported sample: the delivery's four
    /// spheres, whose materials its import wrote onto the pipeline's own Lit from the
    /// materials record, under the scene's light; and a fifth sphere of the scene's
    /// own whose material <see cref="MaterialsActions.Apply"/> writes from a glTF
    /// material, as a person with no DCC behind them would.
    /// </summary>
    public static class MaterialsDemoRecipe
    {
        /// <summary>The package the sample ships in.</summary>
        public const string Package = "com.m3trik.unitytk";
        /// <summary>The sample's name in the Package Manager's Samples tab.</summary>
        public const string SampleName = "Materials";
        /// <summary>The model, in the sample's folder.</summary>
        public const string ModelFile = "Materials.fbx";
        /// <summary>The scene this builds, in the sample's folder.</summary>
        public const string SceneFile = "MaterialsDemo.unity";
        /// <summary>The fifth sphere's material, in the sample's folder.</summary>
        public const string GoldFile = "MaterialsDemo_Gold.mat";

        /// <summary>The fifth sphere's glTF material: gold (glTF's sample values), metal, fairly smooth.</summary>
        public const string Gold = @"{""asset"": {""version"": ""2.0""}, ""materials"": [{""name"": ""Gold"",
            ""pbrMetallicRoughness"": {""baseColorFactor"": [1.0, 0.766, 0.336, 1.0], ""metallicFactor"": 1.0, ""roughnessFactor"": 0.3}}]}";

        /// <summary>
        /// The folder the sample was imported to (<c>Assets/Samples/unitytk/&lt;version&gt;/Materials</c>),
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
        [MenuItem("Tools/unitytk/Samples/Materials Demo")]
        static void BuildAndOpen()
        {
            string folder = Folder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog("Materials Demo",
                    "Import the Materials sample first: Window > Package Manager > unitytk > Samples > Materials > Import.", "OK");
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
        public static Scene Build(string folder)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{folder}/{ModelFile}");
            if (model == null) throw new FileNotFoundException($"no {ModelFile} in {folder}: import the Materials sample again");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(model, scene);

            string goldPath = $"{folder}/{GoldFile}";
            var gold = AssetDatabase.LoadAssetAtPath<Material>(goldPath);
            if (gold == null)
            {
                AssetDatabase.CreateAsset(new Material(Shader.Find("Standard")) { name = "Gold" }, goldPath);
                gold = AssetDatabase.LoadAssetAtPath<Material>(goldPath);
            }
            MaterialsActions.Apply(gold, Gold, "Gold");
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Gold";
            sphere.transform.position = new Vector3(0f, 0.4f, 1.2f);
            sphere.transform.localScale = Vector3.one * 0.8f;
            sphere.GetComponent<Renderer>().sharedMaterial = gold;

            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = new Vector3(0f, 1.2f, -4f);
                camera.transform.LookAt(new Vector3(0f, 0.4f, 0.4f));
            }
            EditorSceneManager.SaveScene(scene, $"{folder}/{SceneFile}");
            return scene;
        }
    }
}
