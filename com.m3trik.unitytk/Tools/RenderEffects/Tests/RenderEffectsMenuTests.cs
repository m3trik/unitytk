using System.IO;
using M3trik.Unitytk.Kernel;
using M3trik.Unitytk.RenderEffects;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Tools.RenderEffects.Tests
{
    /// <summary>
    /// The menu's Prepare Materials over the selection: a model's own object among it
    /// (whose import makes its twins, and which the action refuses) is passed over, never
    /// a failure that undoes the rest; the selection's other objects are prepared.
    /// </summary>
    public class RenderEffectsMenuTests
    {
        const string Folder = "Assets/UnitytkRenderEffectsMenuTests";
        const string Model = Folder + "/tri.obj";

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(Folder));
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), Folder + "/Scene.unity");
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void A_models_own_object_in_the_selection_is_passed_over_and_the_rest_prepared()
        {
            IRenderPipelineHost host = RenderPipelineHosts.Current;
            Assume.That(host, Is.Not.Null, "a unitytk render-pipeline host serves this project's pipeline");
            // A model asset (OBJ, which Unity imports through its ModelImporter), selected in the Project window.
            File.WriteAllLines(Model, new[] { "v 0 0 0", "v 1 0 0", "v 0 1 0", "f 1 2 3" });
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            Assert.That(AssetImporter.GetAtPath(Model), Is.InstanceOf<ModelImporter>());

            var material = new Material(Shader.Find(host.LitShader)) { name = "Crate_MAT" };
            Assert.That(MaterialWriter.Write(material, new LitSurface(), host), Is.True);
            AssetDatabase.CreateAsset(material, Folder + "/Crate_MAT.mat");
            var crate = new GameObject("Crate");
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.transform.SetParent(crate.transform, false);
            body.GetComponent<Renderer>().sharedMaterial = material;
            var fade = crate.AddComponent<Fade>();

            Assert.That(RenderEffectsMenu.PrepareSelected(new[] { model, crate }), Is.GreaterThan(0), "the scene's object prepared");
            Assert.That(fade.twins, Is.Not.Null, "its effect given the twins");
        }
    }
}
