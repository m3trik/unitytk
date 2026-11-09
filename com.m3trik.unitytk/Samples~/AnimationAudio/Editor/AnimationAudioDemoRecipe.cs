// The Animation Audio sample's demo recipe: builds its demo scene from the imported sample.
using System;
using System.IO;
using System.Linq;
using M3trik.Unitytk.AnimationAudio;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.Samples.AnimationAudio
{
    /// <summary>
    /// Builds the Animation Audio demo scene from the imported sample: the model its
    /// import gave an <see cref="AnimationAudioPlayer"/>, placed in a new scene with an
    /// Animator Controller that plays its two clips in order, again and again. Enter
    /// Play Mode in the scene to hear the chime start in the first clip and play on,
    /// from where it had reached, in the second.
    /// </summary>
    public static class AnimationAudioDemoRecipe
    {
        /// <summary>The package the sample ships in.</summary>
        public const string Package = "com.m3trik.unitytk";
        /// <summary>The sample's name in the Package Manager's Samples tab.</summary>
        public const string SampleName = "AnimationAudio";
        /// <summary>The model, in the sample's folder.</summary>
        public const string ModelFile = "AnimationAudio.fbx";
        /// <summary>The controller this builds, in the sample's folder.</summary>
        public const string ControllerFile = "AnimationAudioDemo.controller";
        /// <summary>The scene this builds, in the sample's folder.</summary>
        public const string SceneFile = "AnimationAudioDemo.unity";
        /// <summary>The model's clips, in the order the demo plays them (one per shot).</summary>
        public static readonly string[] Clips = { "Approach", "Exit" };

        /// <summary>
        /// The folder the sample was imported to (<c>Assets/Samples/unitytk/&lt;version&gt;/AnimationAudio</c>),
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
        [MenuItem("Tools/unitytk/Samples/Animation Audio Demo")]
        static void BuildAndOpen()
        {
            string folder = Folder();
            if (folder == null)
            {
                EditorUtility.DisplayDialog("Animation Audio Demo",
                    "Import the AnimationAudio sample first: Window > Package Manager > unitytk > Samples > AnimationAudio > Import.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(folder);
        }

        /// <summary>
        /// Build the demo scene from the sample in <paramref name="folder"/>: an
        /// Animator Controller (<see cref="ControllerFile"/>) whose states play
        /// <see cref="Clips"/> one after the other, each to its end, on the model's
        /// Animator (the model's Rig set to Generic first, as a controller asks);
        /// saved there as <see cref="SceneFile"/> and left open.
        /// </summary>
        /// <param name="folder">The imported sample's folder (<see cref="Folder"/>).</param>
        /// <returns>The scene, saved.</returns>
        /// <exception cref="FileNotFoundException">The folder holds no model, or the model no clip of <see cref="Clips"/>.</exception>
        /// <exception cref="InvalidOperationException">The model carries no player: its import wrote no cues.</exception>
        public static Scene Build(string folder)
        {
            string modelPath = $"{folder}/{ModelFile}";
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model == null) throw new FileNotFoundException($"no {ModelFile} in {folder}: import the AnimationAudio sample again");
            if (model.GetComponent<AnimationAudioPlayer>() == null)
                throw new InvalidOperationException(
                    $"{modelPath} carries no Animation Audio Player: its import wrote no cues (reimport it with unitytk installed)");
            // An Animator Controller plays Generic (or Humanoid) clips, never Legacy ones:
            // the model's Rig tab, the project's half of its import, says which it makes.
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            if (importer.animationType != ModelImporterAnimationType.Generic)
            {
                importer.animationType = ModelImporterAnimationType.Generic;
                importer.SaveAndReimport();
            }
            var clips = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().ToArray();
            AnimationClip[] played = Clips.Select(n => clips.FirstOrDefault(c => c.name == n)).ToArray();
            if (played.Any(c => c == null))
                throw new FileNotFoundException($"{modelPath} holds no clip {string.Join(" or ", Clips)}: import the sample again");

            string controllerPath = $"{folder}/{ControllerFile}";
            AssetDatabase.DeleteAsset(controllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState[] states = played.Select(c => { var s = machine.AddState(c.name); s.motion = c; return s; }).ToArray();
            machine.defaultState = states[0];
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorStateTransition next = states[i].AddTransition(states[(i + 1) % states.Length]);
                next.hasExitTime = true;
                next.exitTime = 1f;
                next.duration = 0f;
                EditorUtility.SetDirty(next);
            }
            // Set after AddState wrote them: marked so the save writes them, or the
            // next scene's unload reloads the controller from its first write.
            foreach (AnimatorState state in states) EditorUtility.SetDirty(state);
            EditorUtility.SetDirty(machine);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
            Animator animator = instance.GetComponent<Animator>();
            if (animator == null) animator = instance.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            EditorSceneManager.SaveScene(scene, $"{folder}/{SceneFile}");
            return scene;
        }
    }
}
