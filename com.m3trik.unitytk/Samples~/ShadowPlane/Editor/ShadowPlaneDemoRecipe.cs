// The ShadowPlane sample's demo recipe: builds its demo scene from the imported sample.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Samples.ShadowPlane
{
    /// <summary>
    /// Builds the ShadowPlane demo scene from the imported sample: the model its import
    /// gave three <see cref="M3trik.Unitytk.ShadowPlane.ShadowPlane"/> components (the
    /// box's and the awning's cast by the lamp, the chair's by the sun, each marching
    /// its delivered horizon map), placed in a new scene with an Animator Controller that
    /// plays the lamp's sweep again and again. The planes' sources are the model's own
    /// Lights, switched on to light the scene and kept from casting Unity's shadow maps
    /// (the planes are the shadows); the default scene's sun is taken out, so the model's
    /// sun lights the scene alone. Enter Play Mode to watch the lamp's two shadows swing,
    /// lengthen and fade as it sweeps over, and the chair's stay where the sun casts it.
    /// </summary>
    public static class ShadowPlaneDemoRecipe
    {
        /// <summary>The package the sample ships in.</summary>
        public const string Package = "com.m3trik.unitytk";
        /// <summary>The sample's name in the Package Manager's Samples tab.</summary>
        public const string SampleName = "ShadowPlane";
        /// <summary>The model, in the sample's folder.</summary>
        public const string ModelFile = "ShadowPlane.fbx";
        /// <summary>The controller this builds, in the sample's folder.</summary>
        public const string ControllerFile = "ShadowPlaneDemo.controller";
        /// <summary>The scene this builds, in the sample's folder.</summary>
        public const string SceneFile = "ShadowPlaneDemo.unity";

        /// <summary>
        /// The folder the sample was imported to (<c>Assets/Samples/unitytk/&lt;version&gt;/ShadowPlane</c>),
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
        [MenuItem("Tools/unitytk/Samples/Shadow Plane Demo")]
        static void BuildAndOpen()
        {
            string folder = Folder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog("Shadow Plane Demo",
                    "Import the ShadowPlane sample first: Window > Package Manager > unitytk > Samples > ShadowPlane > Import.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(folder);
        }

        /// <summary>
        /// Build the demo scene from the sample in <paramref name="folder"/>: an Animator
        /// Controller (<see cref="ControllerFile"/>) whose one state plays the model's clip
        /// (the lamp's sweep) and plays it again from its end, on the model's Animator (the
        /// model's Rig set to Generic first, as a controller asks); a new scene without the
        /// default sun, the model placed in it, each plane's source Light switched on with
        /// Unity's shadows off, every plane placed and faded at once
        /// (<see cref="M3trik.Unitytk.ShadowPlane.ShadowPlane.ApplyNow"/>), and the camera
        /// looking over the three objects; saved there as <see cref="SceneFile"/> and left open.
        /// </summary>
        /// <param name="folder">The imported sample's folder (<see cref="Folder"/>).</param>
        /// <returns>The scene, saved.</returns>
        /// <exception cref="FileNotFoundException">The folder holds no model, or the model no clip.</exception>
        /// <exception cref="InvalidOperationException">The model carries no shadow plane: its import read no record.</exception>
        public static Scene Build(string folder)
        {
            string modelPath = $"{folder}/{ModelFile}";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new FileNotFoundException($"no {ModelFile} in {folder}: import the ShadowPlane sample again");
            if (model.GetComponentInChildren<M3trik.Unitytk.ShadowPlane.ShadowPlane>(true) == null)
                throw new InvalidOperationException(
                    $"{modelPath} carries no shadow plane: its import read no shadows record (reimport it with unitytk installed)");
            // An Animator Controller plays Generic (or Humanoid) clips, never Legacy ones:
            // the model's Rig tab, the project's half of its import, says which it makes.
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.SaveAndReimport();
                model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            }
            AnimationClip sweep = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (sweep == null) throw new FileNotFoundException($"{modelPath} holds no clip: import the sample again");

            string controllerPath = $"{folder}/{ControllerFile}";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState state = machine.AddState(sweep.name);
            state.motion = sweep;
            machine.defaultState = state;
            AnimatorStateTransition again = state.AddTransition(state);
            again.hasExitTime = true;
            again.exitTime = 1f;
            again.duration = 0f;
            // Set after AddState wrote them: marked so the save writes them, or the
            // next scene's unload reloads the controller from its first write.
            EditorUtility.SetDirty(again);
            EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            // The model's sun lights the scene: the default one would light it twice.
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(light.gameObject);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            Animator animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;

            var planes = instance.GetComponentsInChildren<M3trik.Unitytk.ShadowPlane.ShadowPlane>(true);
            foreach (var plane in planes)
            {
                Light source = plane.source;
                if (source == null) continue;
                // A source the delivery carried as a bare node is a switched-off stand-in:
                // on, it lights the scene as the plane's shadow says it does.
                source.enabled = true;
                source.shadows = LightShadows.None;  // the plane is the shadow
                if (source.type == LightType.Directional) RenderSettings.sun = source;
            }
            foreach (var plane in planes) plane.ApplyNow();

            var camera = Camera.main;
            if (camera != null)
            {
                camera.transform.position = new Vector3(0.3f, 2.4f, -4.4f);
                camera.transform.LookAt(new Vector3(0f, 0.3f, 0.1f));
            }
            EditorSceneManager.SaveScene(scene, $"{folder}/{SceneFile}");
            return scene;
        }
    }
}
