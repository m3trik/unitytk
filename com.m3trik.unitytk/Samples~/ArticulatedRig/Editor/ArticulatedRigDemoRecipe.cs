// The ArticulatedRig sample's demo recipe: builds its demo scene from the imported sample.
using System;
using System.IO;
using System.Linq;
using M3trik.Unitytk.ArticulatedRig;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Rig = M3trik.Unitytk.ArticulatedRig.ArticulatedRig;

namespace M3trik.Unitytk.Samples.ArticulatedRig
{
    /// <summary>
    /// Builds the ArticulatedRig demo scene from the imported sample: the desk lamp its
    /// import gave an <see cref="Rig"/> on <c>lamp_RIG</c> and an
    /// <see cref="ArticulatedJoint"/> on each joint (the post's swivel and tilt, the upper
    /// arm's hinge, the extension's slide, the shade's hinge), placed in a new scene with an
    /// Animator Controller that plays its take again and again, and a camera in front of it
    /// carrying an <see cref="ArticulatedRigPointerGrab"/>. Enter Play Mode and press on any
    /// part to drag it: the chain from the base solves to follow the pointer within the
    /// joints' limits, the scroll wheel pushes and pulls, and the released pose holds over
    /// the playing take until the rig's <see cref="Rig.ReturnToAnimation"/>.
    /// </summary>
    public static class ArticulatedRigDemoRecipe
    {
        /// <summary>The package the sample ships in.</summary>
        public const string Package = "com.m3trik.unitytk";
        /// <summary>The sample's name in the Package Manager's Samples tab.</summary>
        public const string SampleName = "ArticulatedRig";
        /// <summary>The model, in the sample's folder.</summary>
        public const string ModelFile = "ArticulatedRig.fbx";
        /// <summary>The controller this builds, in the sample's folder.</summary>
        public const string ControllerFile = "ArticulatedRigDemo.controller";
        /// <summary>The scene this builds, in the sample's folder.</summary>
        public const string SceneFile = "ArticulatedRigDemo.unity";

        /// <summary>
        /// The folder the sample was imported to (<c>Assets/Samples/unitytk/&lt;version&gt;/ArticulatedRig</c>),
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
        [MenuItem("Tools/unitytk/Samples/Articulated Rig Demo")]
        static void BuildAndOpen()
        {
            string folder = Folder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog("Articulated Rig Demo",
                    "Import the ArticulatedRig sample first: Window > Package Manager > unitytk > Samples > ArticulatedRig > Import.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(folder);
        }

        /// <summary>
        /// Build the demo scene from the sample in <paramref name="folder"/>: an Animator
        /// Controller (<see cref="ControllerFile"/>) whose one state plays the model's clip (the
        /// take) and plays it again from its end, on the model's Animator (the model's Rig set
        /// to Generic first, as a controller asks); a new scene, the model placed in it, the
        /// camera in front of the lamp with an <see cref="ArticulatedRigPointerGrab"/> for its
        /// rig; saved there as <see cref="SceneFile"/> and left open.
        /// </summary>
        /// <param name="folder">The imported sample's folder (<see cref="Folder"/>).</param>
        /// <returns>The scene, saved.</returns>
        /// <exception cref="FileNotFoundException">The folder holds no model, or the model no clip.</exception>
        /// <exception cref="InvalidOperationException">The model carries no rig: its import read no record.</exception>
        public static Scene Build(string folder)
        {
            string modelPath = $"{folder}/{ModelFile}";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new FileNotFoundException($"no {ModelFile} in {folder}: import the ArticulatedRig sample again");
            if (model.GetComponentInChildren<Rig>(true) == null)
                throw new InvalidOperationException(
                    $"{modelPath} carries no articulated rig: its import read no articulation record (reimport it with unitytk installed)");
            // An Animator Controller plays Generic (or Humanoid) clips, never Legacy ones:
            // the model's Rig tab, the project's half of its import, says which it makes.
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.SaveAndReimport();
                model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            }
            AnimationClip take = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal));
            if (take == null) throw new FileNotFoundException($"{modelPath} holds no clip: import the sample again");

            string controllerPath = $"{folder}/{ControllerFile}";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState state = machine.AddState(take.name);
            state.motion = take;
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
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            Animator animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            Rig rig = instance.GetComponentInChildren<Rig>(true);

            var camera = Camera.main;
            if (camera == null)
            {
                camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
                camera.tag = "MainCamera";
            }
            // In front of the lamp's plane, looking at its arms.
            camera.transform.position = new Vector3(-0.14f, 0.22f, -0.75f);
            camera.transform.LookAt(new Vector3(-0.14f, 0.18f, -0.05f));
            camera.nearClipPlane = 0.01f;
            var grabber = camera.gameObject.AddComponent<ArticulatedRigPointerGrab>();
            grabber.viewCamera = camera;
            grabber.rig = rig;

            EditorSceneManager.SaveScene(scene, $"{folder}/{SceneFile}");
            return scene;
        }
    }
}
