using System;
using System.IO;
using System.Linq;
using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace M3trik.Unitytk.AnimationAudio.Tests
{
    /// <summary>
    /// The Animation Audio sample, imported as the Package Manager imports it: a box
    /// over two shots, Approach (frames 1-24) and Exit (25-48) at 24 fps, and a
    /// half-second chime keyed on frame 18, across the cut. Approach cues it from
    /// 17/24 s to its end, the cut; Exit plays it on from its start, 7/24 s into the
    /// sound, to 5/24 s, where it ends -- each as PlayAudio and StopAudio events whose
    /// object is the chime's AudioClip, extracted from the FBX beside it. The demo
    /// recipe builds its scene. The project these run in imports every sample of the
    /// package, so a sample that is not there fails: it is never skipped.
    /// </summary>
    public class AnimationAudioSampleTests
    {
        const string Package = "com.m3trik.unitytk";
        const string SampleName = "AnimationAudio";
        const string Recipe = "M3trik.Unitytk.Samples.AnimationAudio.AnimationAudioDemoRecipe, M3trik.Unitytk.Samples.AnimationAudio.Editor";
        const float Fps = 24f;

        static string Folder()
        {
            Sample sample = Sample.FindByPackage(Package, null).FirstOrDefault(s => s.displayName == SampleName);
            Assert.That(sample.displayName, Is.EqualTo(SampleName),
                        "the package lists no AnimationAudio sample: its package.json's samples are written by the workspace's sample sync");
            Assert.That(sample.isImported, Is.True,
                        $"the AnimationAudio sample is not imported at {sample.importPath}: import it from the Package Manager's " +
                        "Samples tab (the owned project's builder copies every sample in)");
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return "Assets" + full.Substring(assets.Length);
        }

        static string Model() => Folder() + "/AnimationAudio.fbx";

        static AnimationClip Clip(string model, string name)
            => AssetDatabase.LoadAllAssetsAtPath(model).OfType<AnimationClip>().Single(c => c.name == name);

        static (string fn, float time, float offset)[] Events(AnimationClip clip)
            => AnimationUtility.GetAnimationEvents(clip)
                               .Select(e => (e.functionName, Mathf.Round(e.time * 1e4f) / 1e4f, Mathf.Round(e.floatParameter * 1e4f) / 1e4f))
                               .ToArray();

        static float R(float v) => Mathf.Round(v * 1e4f) / 1e4f;

        [Test]
        public void Approach_cues_the_chime_from_its_frame_to_the_cut()
        {
            Assert.That(Events(Clip(Model(), "Approach")), Is.EqualTo(new[]
            {
                (PersistedStrings.PlayAudioFunction, R(17f / Fps), 0f),
                (PersistedStrings.StopAudioFunction, R(23f / Fps), 0f),
            }), "frame 18 is 17 frames into a clip starting on 1; the cut, its end, 23 frames in");
            Assert.That(Clip(Model(), "Approach").length, Is.EqualTo(23f / Fps).Within(1e-3));
        }

        [Test]
        public void Exit_plays_the_chime_on_from_where_it_had_reached()
        {
            Assert.That(Events(Clip(Model(), "Exit")), Is.EqualTo(new[]
            {
                (PersistedStrings.PlayAudioFunction, 0f, R(7f / Fps)),
                (PersistedStrings.StopAudioFunction, R(5f / Fps), 0f),
            }), "seven frames after it began; it ends on frame 30, five frames into Exit");
        }

        [Test]
        public void Every_cue_plays_the_chimes_own_clip_and_the_model_has_its_player()
        {
            string model = Model();
            var cues = new[] { "Approach", "Exit" }.SelectMany(n => AnimationUtility.GetAnimationEvents(Clip(model, n))).ToArray();
            Assert.That(cues.Select(e => e.objectReferenceParameter).Distinct().Count(), Is.EqualTo(1));
            var chime = cues[0].objectReferenceParameter as AudioClip;
            Assert.That(chime, Is.Not.Null, "the cue's sound is an AudioClip asset");
            Assert.That(Path.GetFileName(AssetDatabase.GetAssetPath(chime)), Is.EqualTo("chime.wav"),
                        "the delivered file, by the record's sound name");
            Assert.That(chime.length, Is.EqualTo(0.5f).Within(1e-3));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(model).GetComponent<AnimationAudioPlayer>(), Is.Not.Null);
            var importer = (ModelImporter)AssetImporter.GetAtPath(model);
            Assert.That(importer.clipAnimations.SelectMany(c => c.events), Is.Empty, "the Events tab holds none of them");
        }

        [Test]
        public void The_demo_recipe_builds_its_scene_with_no_errors()
        {
            string folder = Folder();
            Type recipe = Type.GetType(Recipe);
            Assert.That(recipe, Is.Not.Null, "the sample's editor assembly is missing: it did not compile, or the sample is not imported");
            try
            {
                var scene = (Scene)recipe.GetMethod("Build").Invoke(null, new object[] { folder });
                Assert.That(scene.path, Is.EqualTo(folder + "/AnimationAudioDemo.unity"), "saved beside the model");
                GameObject model = scene.GetRootGameObjects().Single(go => go.GetComponent<AnimationAudioPlayer>() != null);
                var animator = model.GetComponent<Animator>();
                Assert.That(animator.runtimeAnimatorController, Is.Not.Null, "an Animator Controller plays the clips");
                var machine = ((UnityEditor.Animations.AnimatorController)animator.runtimeAnimatorController).layers[0].stateMachine;
                Assert.That(machine.states.Select(s => s.state.motion), Is.EqualTo(new[] { Clip(Model(), "Approach"), Clip(Model(), "Exit") }),
                            "a state per clip, in shot order, each playing the model's own clip");
                Assert.That(machine.defaultState.motion, Is.EqualTo(Clip(Model(), "Approach")));
                // An error logged fails the test (the Test Framework's own rule); the import's
                // lines, from the Rig switch's reimport, are information.
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
