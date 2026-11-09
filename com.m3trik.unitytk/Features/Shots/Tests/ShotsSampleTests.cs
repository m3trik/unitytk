using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace M3trik.Unitytk.Shots.Tests
{
    /// <summary>
    /// The Shots sample, imported as the Package Manager imports it: one clip per
    /// shot, named after it and as long as the shot; a <see cref="ShotList"/> entry
    /// per shot, referencing its clip with its description and section; and the
    /// sample's demo recipe building its scene with no error. The project these
    /// run in imports every sample of the package (the owned project's builder
    /// copies them in), so a sample that is not there fails: it is never skipped.
    /// </summary>
    public class ShotsSampleTests
    {
        const string Package = "com.m3trik.unitytk";
        const string SampleName = "Shots";
        const string Recipe = "M3trik.Unitytk.Samples.Shots.ShotsDemoRecipe, M3trik.Unitytk.Samples.Shots.Editor";
        const float Fps = 24f;

        // The sample's shots, as its recipe makes them: the take, its first and
        // last frame, its description and its one shipped metadata key, section.
        static readonly (string clip, int first, int last, string description, string section)[] Expected =
        {
            ("Approach", 1, 24, "The box slides in from the left.", "intro"),
            ("Turn", 25, 48, "The box turns half way round.", "middle"),
            ("Exit", 49, 72, "The box slides out to the right.", "outro"),
        };

        /// <summary>The imported sample's folder, project-relative; a failure when it is not imported.</summary>
        static string Folder()
        {
            Sample sample = Sample.FindByPackage(Package, null).FirstOrDefault(s => s.displayName == SampleName);
            Assert.That(sample.displayName, Is.EqualTo(SampleName),
                        "the package lists no Shots sample: its package.json's samples are written by the workspace's sample sync");
            Assert.That(sample.isImported, Is.True,
                        $"the Shots sample is not imported at {sample.importPath}: import it from the Package Manager's " +
                        "Samples tab (the owned project's builder copies every sample in)");
            string full = Path.GetFullPath(sample.importPath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return "Assets" + full.Substring(assets.Length);
        }

        static string Model() => Folder() + "/Shots.fbx";

        static System.Collections.Generic.Dictionary<string, AnimationClip> Clips(string model)
            => AssetDatabase.LoadAllAssetsAtPath(model).OfType<AnimationClip>()
                            .Where(c => !c.name.StartsWith("__preview__"))
                            .ToDictionary(c => c.name);

        [Test]
        public void The_sample_holds_one_clip_per_shot_named_after_it()
        {
            string model = Model();
            Assert.That(Clips(model).Keys, Is.EquivalentTo(Expected.Select(e => e.clip)));
            var importer = (ModelImporter)AssetImporter.GetAtPath(model);
            Assert.That(importer.importedTakeInfos.Select(t => t.name), Is.EqualTo(Expected.Select(e => e.clip)),
                        "one take per shot, in shot order, and no other");
        }

        [Test]
        public void Each_shot_entry_references_its_clip_with_its_description_and_section()
        {
            string model = Model();
            var clips = Clips(model);
            var shots = AssetDatabase.LoadAssetAtPath<GameObject>(model).GetComponent<ShotList>();
            Assert.That(shots, Is.Not.Null, "the import wrote the shots onto the model");
            Assert.That(shots.entries.Select(e => e.clip), Is.EqualTo(Expected.Select(e => clips[e.clip])));
            foreach (var expected in Expected)
            {
                Shot shot = shots.Entry(clips[expected.clip]);
                Assert.That(shot.description, Is.EqualTo(expected.description), expected.clip);
                Assert.That(shot.TryGetMetadata("section", out string section), Is.True, expected.clip);
                Assert.That(section, Is.EqualTo($"\"{expected.section}\""), expected.clip);
                Assert.That(shot.metadata, Has.Count.EqualTo(1), "only the keys the source ships");
            }
        }

        [Test]
        public void Each_clip_lasts_its_shot()
        {
            string model = Model();
            var clips = Clips(model);
            var takes = ((ModelImporter)AssetImporter.GetAtPath(model)).importedTakeInfos.ToDictionary(t => t.name);
            foreach (var expected in Expected)
            {
                float length = (expected.last - expected.first) / Fps;
                Assert.That(clips[expected.clip].length, Is.EqualTo(length).Within(1e-3), expected.clip);
                var take = takes[expected.clip];
                Assert.That(take.startTime, Is.EqualTo(expected.first / Fps).Within(1e-3),
                            "the take keeps the shot's place on the timeline");
                Assert.That(take.stopTime - take.startTime, Is.EqualTo(length).Within(1e-3), expected.clip);
            }
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
                Assert.That(scene.path, Is.EqualTo(folder + "/ShotsDemo.unity"), "saved beside the model");
                GameObject model = scene.GetRootGameObjects().Single(go => go.GetComponent<ShotList>() != null);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(model),
                            Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/Shots.fbx")), "the sample's model, placed");
                Component demo = model.GetComponents<Component>().Single(c => c != null && c.GetType().Name == "ShotsDemo");
                Assert.That(demo.GetType().GetField("shots").GetValue(demo), Is.EqualTo(model.GetComponent<ShotList>()),
                            "the demo plays the model's own shots");
                Assert.That(model.GetComponent<Animator>(), Is.Not.Null, "an Animator plays the clips");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
