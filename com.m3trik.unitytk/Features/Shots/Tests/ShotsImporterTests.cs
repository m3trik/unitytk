using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using M3trik.Unitytk.Testing;
namespace M3trik.Unitytk.Shots.Tests
{
    /// <summary>
    /// The importer joins each record shot to the clip Unity made of its take, by the
    /// take's name: a clip renamed or split in the Animation tab still joins, a shot
    /// with no take or no clip is warned. A real import of a two-take FBX proves the
    /// component on the model references the model's own clip sub-assets, and keeps
    /// them through a reimport; a v1 record is refused with its fix.
    /// </summary>
    public class ShotsImporterTests
    {
        const string Folder = "Assets/UnitytkShotsTests";
        const string Model = Folder + "/two_shots.fbx";

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Folder);

        // -- the join, over Unity's own clip and take types ------------------------------

        static AnimationRecord.Shape.Shot Recorded(string clip, string description = null,
                                                   IDictionary<string, object> metadata = null)
            => new AnimationRecord.Shape.Shot { clip = clip, description = description, metadata = metadata };

        static ModelImporterClipAnimation Cut(string name, string take)
            => new ModelImporterClipAnimation { name = name, takeName = take };

        static TakeInfo Take(string name) => new TakeInfo { name = name, sampleRate = 30f };

        [Test]
        public void The_importer_declares_its_version_so_a_change_to_what_it_writes_reimports_the_models()
        {
            // Unity keys a cached import on each postprocessor's version; the base's 0 never changes.
            Assert.That(typeof(ShotsImporter).GetMethod(nameof(AssetPostprocessor.GetVersion)).DeclaringType,
                        Is.EqualTo(typeof(ShotsImporter)));
            Assert.That(new ShotsImporter().GetVersion(), Is.GreaterThan(0u));
        }

        [Test]
        public void A_shot_joins_every_clip_cut_from_its_take_whatever_the_clip_is_named()
        {
            var intro = new AnimationClip { name = "Intro" };
            var loopA = new AnimationClip { name = "Outro A" };
            var loopB = new AnimationClip { name = "Outro B" };
            try
            {
                var built = new Dictionary<string, AnimationClip> { ["Intro"] = intro, ["Outro A"] = loopA, ["Outro B"] = loopB };
                var clips = new[] { Cut("Intro", "intro"), Cut("Outro A", "Outro"), Cut("Outro B", "Outro") };
                var entries = ShotsImporter.Resolve(
                    new[] { Recorded("Intro", "the door"), Recorded("Outro", metadata: new Dictionary<string, object> { ["take"] = 2.0 }) },
                    clips, new[] { Take("intro"), Take("Outro") }, built, out var noTake, out var noClip);
                Assert.That(entries.Select(e => e.clip), Is.EqualTo(new[] { intro, loopA, loopB }),
                            "by the take, ignoring case; a split take's parts each get the shot");
                Assert.That(entries[0].description, Is.EqualTo("the door"));
                Assert.That(entries[1].description, Is.Empty, "absent in the record: empty");
                Assert.That(entries[2].metadata.Single().key, Is.EqualTo("take"));
                Assert.That(entries[2].metadata.Single().json, Is.EqualTo("2"), "a whole number writes back as one");
                Assert.That(noTake, Is.Empty);
                Assert.That(noClip, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(intro);
                Object.DestroyImmediate(loopA);
                Object.DestroyImmediate(loopB);
            }
        }

        [Test]
        public void A_shot_with_no_take_or_no_clip_cut_from_it_is_named()
        {
            var entries = ShotsImporter.Resolve(
                new[] { Recorded("Gone"), Recorded("Cut") }, new ModelImporterClipAnimation[0],
                new[] { Take("Cut") }, new Dictionary<string, AnimationClip>(), out var noTake, out var noClip);
            Assert.That(entries, Is.Empty);
            Assert.That(noTake, Is.EqualTo(new[] { "Gone" }), "the file carries no take of its name: re-export");
            Assert.That(noClip, Is.EqualTo(new[] { "Cut" }), "the take is there, the Animation tab cuts no clip of it");
        }

        // -- a real import --------------------------------------------------------------

        const string Record =
            "{\"version\": 3, \"fps\": 24.0, \"shots\": [" +
            "{\"clip\": \"Intro\", \"description\": \"the door opens\", \"metadata\": {\"take\": 2, \"cameras\": [\"A\", \"B\"]}}, " +
            "{\"clip\": \"Outro\"}]}";

        static void Import(string record)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "UnitytkShotsTests");
            File.WriteAllText(Model, TwoTakeFbx.Write("animation", record), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        static Dictionary<string, AnimationClip> Clips()
            => AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>()
                            .Where(c => !c.name.StartsWith("__preview__"))
                            .ToDictionary(c => c.name);

        [Test]
        public void A_two_take_model_references_its_own_clips_and_keeps_them_through_a_reimport()
        {
            Import(Record);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var shots = model.GetComponent<ShotList>();
            Assert.That(shots, Is.Not.Null, "the importer wrote the shots onto the model");
            var clips = Clips();
            Assert.That(clips.Keys, Is.EquivalentTo(new[] { "Intro", "Outro" }), "one clip per take");
            Assert.That(shots.entries.Select(e => e.clip), Is.EqualTo(new[] { clips["Intro"], clips["Outro"] }));
            foreach (var entry in shots.entries)
                Assert.That(AssetDatabase.GetAssetPath(entry.clip), Is.EqualTo(Model), "a sub-asset of the model itself");
            Shot intro = shots.Entry(clips["Intro"]);
            Assert.That(intro.description, Is.EqualTo("the door opens"));
            Assert.That(intro.TryGetMetadata("take", out string take) && take == "2");
            Assert.That(intro.TryGetMetadata("cameras", out string cameras) && cameras == "[\"A\",\"B\"]", cameras);

            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            var outro = importer.importedTakeInfos.Single(t => t.name == "Outro");
            Assert.That(outro.startTime, Is.EqualTo(2f).Within(1e-4), "the take keeps the shot's place on the timeline");
            var cut = importer.defaultClipAnimations.Single(c => c.takeName == "Outro");
            Assert.That(cut.firstFrame / outro.sampleRate, Is.EqualTo(outro.startTime).Within(1e-4),
                        "its clip starts where the take does, in frames at the take's sample rate");
            Assert.That(clips["Outro"].length, Is.EqualTo(2f).Within(1e-3), "in seconds from the shot's start");

            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            clips = Clips();
            shots = model.GetComponent<ShotList>();
            Assert.That(shots.entries.Select(e => e.clip), Is.EqualTo(new[] { clips["Intro"], clips["Outro"] }),
                        "a reimport rewrites the entries onto the clips it rebuilt");
            Assert.That(shots.entries.All(e => e.clip != null));
        }

        [Test]
        public void A_renamed_clip_still_joins_its_shot()
        {
            Import(Record);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            var cuts = importer.defaultClipAnimations;
            cuts.Single(c => c.takeName == "Outro").name = "Outro Loop";
            importer.clipAnimations = cuts;
            importer.SaveAndReimport();
            var shots = AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponent<ShotList>();
            Assert.That(shots.entries.Select(e => e.clip.name), Is.EqualTo(new[] { "Intro", "Outro Loop" }));
        }

        [Test]
        public void A_model_that_imports_no_animation_says_so_and_holds_no_shots()
        {
            Import(Record);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            importer.importAnimation = false;
            LogAssert.Expect(LogType.Warning, new Regex("carries 2 shot\\(s\\) but imports no animation.*Import Animation on"));
            importer.SaveAndReimport();
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponent<ShotList>() == null);
        }

        [Test]
        public void A_version_1_record_is_refused_with_its_fix_and_leaves_no_shots()
        {
            Import(Record);
            LogAssert.Expect(LogType.Warning, new Regex("animation record v1; unitytk reads v3 only.*re-export"));
            Import("{\"version\": 1, \"fps\": 30.0, \"shots\": [{\"clip\": \"Intro\", \"start\": 0, \"end\": 60}]}");
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponent<ShotList>() == null,
                        "a refused record leaves the model no stale shots");
        }

        // -- adding an entry: never on what an import rewrites ----------------------------

        [Test]
        public void An_imported_model_and_its_instance_are_refused_an_entry()
        {
            // An entry added on the instance is a prefab override Unity matches by list
            // position, so the next import that changes the delivered list misaligns it.
            Import(Record);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
            var extra = new AnimationClip { name = "Extra" };
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                foreach (var target in new[] { model, instance })
                {
                    var refused = Assert.Throws<System.ArgumentException>(
                        () => ShotsActions.AddEntry(target, extra, ctx: new Kernel.OpContext()));
                    StringAssert.Contains("add a ShotList to an object of your own", refused.Message);
                    Assert.That(target.GetComponent<ShotList>().entries, Has.Count.EqualTo(2),
                                "refused before anything changed");
                }
                Assert.That(PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false), Is.False);

                // An object of the project's own takes the model's clip.
                var own = new GameObject("Rig");
                try
                {
                    Assert.That(ShotsActions.AddEntry(own, extra), Is.True);
                    Assert.That(own.GetComponent<ShotList>().Entry(extra), Is.Not.Null);
                }
                finally
                {
                    Object.DestroyImmediate(own);
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(extra);
            }
        }

        [Test]
        public void A_shot_whose_take_the_file_lacks_is_warned()
        {
            LogAssert.Expect(LogType.Warning, new Regex("carries no take for shot\\(s\\) Missing.*re-export"));
            Import("{\"version\": 3, \"fps\": 24.0, \"shots\": [{\"clip\": \"Intro\"}, {\"clip\": \"Missing\"}]}");
            var shots = AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponent<ShotList>();
            Assert.That(shots.entries.Select(e => e.clip.name), Is.EqualTo(new[] { "Intro" }));
        }
    }
}
