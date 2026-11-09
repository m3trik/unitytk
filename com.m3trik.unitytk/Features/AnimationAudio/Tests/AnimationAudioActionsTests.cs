using System;
using System.IO;
using System.Linq;
using System.Text;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

using M3trik.Unitytk.Testing;
namespace M3trik.Unitytk.AnimationAudio.Tests
{
    /// <summary>
    /// The Animation Audio actions held to the kernel's three checks: one Ctrl+Z after
    /// a run restores what it changed, the dry run's plan names every change the run
    /// makes, and a second run plans nothing. A cue added to a model's clip lands in
    /// its Events tab, the project's half of the import, and plays after the reimport.
    /// </summary>
    public class AnimationAudioActionsTests
    {
        AnimationClip _clip;
        AudioClip _sound;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _clip = new AnimationClip { name = "Walk" };
            _clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 2f, 1f));
            _sound = AudioClip.Create("step", 8000, 1, 8000, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_clip);
            Object.DestroyImmediate(_sound);
            AssetDatabase.DeleteAsset(Folder);
        }

        static string[] Functions(AnimationClip clip)
            => AnimationUtility.GetAnimationEvents(clip).Select(e => $"{e.functionName}@{e.time:F2}").ToArray();

        // -- AddCue -----------------------------------------------------------------------

        [Test]
        public void Add_cue_is_undone_by_one_ctrl_z()
        {
            bool added = OpContext.RunMenu("Add Cue", ctx => AnimationAudioActions.AddCue(_clip, _sound, 0.5f, 1f, 0.25f, ctx));
            Assert.That(added, Is.True);
            var events = AnimationUtility.GetAnimationEvents(_clip);
            Assert.That(Functions(_clip), Is.EqualTo(new[] { "PlayAudio@0.50", "StopAudio@1.00" }));
            Assert.That(events[0].objectReferenceParameter, Is.EqualTo(_sound));
            Assert.That(events[0].floatParameter, Is.EqualTo(0.25f), "the offset into the sound");
            Undo.PerformUndo();
            Assert.That(AnimationUtility.GetAnimationEvents(_clip), Is.Empty, "one Ctrl+Z took the cue away");
        }

        [Test]
        public void Add_cue_plans_what_it_then_does_and_a_second_run_plans_nothing()
        {
            var plan = new OpContext(dryRun: true);
            Assert.That(AnimationAudioActions.AddCue(_clip, _sound, 0.5f, ctx: plan), Is.True);
            Assert.That(AnimationUtility.GetAnimationEvents(_clip), Is.Empty, "a dry run adds nothing");
            var live = new OpContext();
            AnimationAudioActions.AddCue(_clip, _sound, 0.5f, ctx: live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target))));
            Assert.That(Functions(_clip), Is.EqualTo(new[] { "PlayAudio@0.50" }), "no stop: it plays to its end or its clip's");

            var again = new OpContext(dryRun: true);
            Assert.That(AnimationAudioActions.AddCue(_clip, _sound, 0.5f, ctx: again), Is.False);
            Assert.That(again.Changes, Is.Empty);
        }

        [Test]
        public void Add_cue_refuses_a_time_outside_the_clip_or_a_stop_before_it()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => AnimationAudioActions.AddCue(_clip, _sound, 3f));
            Assert.Throws<ArgumentOutOfRangeException>(() => AnimationAudioActions.AddCue(_clip, _sound, 1f, 0.5f));
            Assert.Throws<ArgumentOutOfRangeException>(() => AnimationAudioActions.AddCue(_clip, _sound, 1f, offset: 2f));
            Assert.That(AnimationUtility.GetAnimationEvents(_clip), Is.Empty, "refused before anything changed");
        }

        const string Folder = "Assets/UnitytkAnimationAudioActionsTests";
        const string Model = Folder + "/two_shots.fbx";

        [Test]
        public void A_cue_added_to_a_models_clip_lands_in_its_events_tab_and_plays_after_the_reimport()
        {
            AssetDatabase.CreateFolder("Assets", "UnitytkAnimationAudioActionsTests");
            AnimationAudioImporterTests.Wav.Write(Folder + "/door.wav", 1f);
            AssetDatabase.ImportAsset(Folder + "/door.wav", ImportAssetOptions.ForceSynchronousImport);
            File.WriteAllText(Model, TwoTakeFbx.Write("audio", "{\"version\": 3, \"cues\": []}"),
                              new UTF8Encoding(false));
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var door = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/door.wav");
            AnimationClip Outro() => AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>().Single(c => c.name == "Outro");

            var plan = new OpContext(dryRun: true);
            Assert.That(AnimationAudioActions.AddCue(Outro(), door, 1f, 1.5f, ctx: plan), Is.True);
            Assert.That(plan.Changes.Single().kind, Is.EqualTo("write"), "the model's import settings, a file");
            Assert.That(((ModelImporter)AssetImporter.GetAtPath(Model)).clipAnimations, Is.Empty, "a dry run writes nothing");

            Assert.That(AnimationAudioActions.AddCue(Outro(), door, 1f, 1.5f, ctx: new OpContext()), Is.True);
            var tab = ((ModelImporter)AssetImporter.GetAtPath(Model)).clipAnimations.Single(c => c.name == "Outro").events;
            Assert.That(tab.Select(e => (e.functionName, e.time)),
                        Is.EqualTo(new[] { (PersistedStrings.PlayAudioFunction, 0.5f), (PersistedStrings.StopAudioFunction, 0.75f) }),
                        "the Events tab states a fraction of the 2 s clip");
            Assert.That(Functions(Outro()), Is.EqualTo(new[] { "PlayAudio@1.00", "StopAudio@1.50" }), "the reimported clip plays it");
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponent<AnimationAudioPlayer>(), Is.Not.Null,
                        "a model whose clips cue sounds gets the player, delivered or not");

            var again = new OpContext(dryRun: true);
            Assert.That(AnimationAudioActions.AddCue(Outro(), door, 1f, 1.5f, ctx: again), Is.False);
            Assert.That(again.Changes, Is.Empty);
        }

        // -- AddPlayer --------------------------------------------------------------------

        [Test]
        public void Add_player_is_undone_by_one_ctrl_z_and_sets_a_new_source_to_3d()
        {
            var actor = new GameObject("Actor");
            Assert.That(OpContext.RunMenu("Add Player", ctx => AnimationAudioActions.AddPlayer(actor, ctx)), Is.True);
            Assert.That(actor.GetComponent<AnimationAudioPlayer>(), Is.Not.Null);
            Assert.That(actor.GetComponent<AudioSource>().spatialBlend, Is.EqualTo(1f));
            Undo.PerformUndo();
            Assert.That(actor.GetComponent<AnimationAudioPlayer>() == null, "one Ctrl+Z took the player away");
        }

        [Test]
        public void Add_player_plans_what_it_then_does_and_a_second_run_plans_nothing()
        {
            var actor = new GameObject("Actor");
            var plan = new OpContext(dryRun: true);
            Assert.That(AnimationAudioActions.AddPlayer(actor, plan), Is.True);
            Assert.That(actor.GetComponent<AnimationAudioPlayer>() == null, "a dry run adds nothing");
            var live = new OpContext();
            AnimationAudioActions.AddPlayer(actor, live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target))));
            var again = new OpContext(dryRun: true);
            Assert.That(AnimationAudioActions.AddPlayer(actor, again), Is.False);
            Assert.That(again.Changes, Is.Empty);
        }
    }
}
