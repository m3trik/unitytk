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

        [Test]
        public void A_sound_cue_is_planned_then_made_off_and_flat_and_plays_its_sound_once_on()
        {
            var dry = new OpContext(dryRun: true);
            Assert.That(AnimationAudioActions.AddSoundCue("Voices", "A01", _sound, ctx: dry), Is.Null, "a dry run makes nothing");
            Assert.That(AnimationAudioActions.AddSoundCue("Voices", "A01", _sound, ctx: dry), Is.Null, "planned once");
            var live = new OpContext();
            SoundCue cue = AnimationAudioActions.AddSoundCue("Voices", "A01", _sound, ctx: live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(dry.Changes.Select(c => c.target), Is.EqualTo(new[] { "Voices", "Voices/A01/SoundCue" }), "the holder and the cue, by their paths");
            Assert.That(cue.transform.parent.name, Is.EqualTo("Voices"));
            Assert.That((cue.enabled, cue.clip, cue.Length), Is.EqualTo((false, _sound, 1f)), "made off, for a step to switch on");
            Assert.That(cue.Source.spatialBlend, Is.Zero, "a voice in the ears, not in the room");
            Assert.That(cue.Source.playOnAwake, Is.False, "its source waits for it (the cue sets it up, not the action)");
            Assert.That(AnimationAudioActions.AddSoundCue("Voices", "A01", _sound, ctx: new OpContext(dryRun: true)), Is.SameAs(cue));
            Assert.That(cue.Play(), Is.True);
            Assert.That((cue.Source.clip, cue.Source.loop), Is.EqualTo((_sound, false)));
        }

        [Test]
        public void Two_cues_of_one_name_under_two_holders_are_planned_apart_as_they_are_made()
        {
            void Run(OpContext ctx)
            {
                AnimationAudioActions.AddSoundCue("Left", "A01", _sound, ctx: ctx);
                AnimationAudioActions.AddSoundCue("Right", "A01", _sound, ctx: ctx);
            }
            var dry = new OpContext(dryRun: true);
            Run(dry);
            var live = new OpContext();
            Run(live);
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(dry.Changes.Select(c => (c.kind, c.target, c.action))));
            Assert.That(dry.Changes.Count, Is.EqualTo(4), "each holder and its own cue");
        }

        [Test]
        public void A_sound_cue_switched_on_plays_its_sound_and_switched_off_stops_it()
        {
            SoundCue cue = AnimationAudioActions.AddSoundCue("Voices", "A01", _sound, spatialBlend: 1f);
            Assert.That(cue.Play(), Is.True, "what switching it on does");
            Assert.That((cue.Source.isPlaying, cue.Source.clip, cue.Source.spatialBlend), Is.EqualTo((true, _sound, 1f)), "its sound, set on its source as it plays");
            cue.Stop();
            Assert.That(cue.Source.isPlaying, Is.False, "what switching it off does");
            cue.clip = null;
            Assert.That(cue.Play(), Is.False, "no sound: nothing plays");
        }

        [Test]
        public void A_sound_cue_plays_placed_looping_or_as_its_source_is_set_up_as_asked()
        {
            SoundCue cue = AnimationAudioActions.AddSoundCue("Voices", "A01", _sound, startEnabled: true, spatialBlend: 1f, loop: true);
            Assert.That((cue.enabled, cue.spatialBlend, cue.loop), Is.EqualTo((true, 1f, true)), "switched on from the start, placed, looping");
            cue.Play();
            Assert.That((cue.Source.spatialBlend, cue.Source.loop), Is.EqualTo((1f, true)));

            var plan = new OpContext(dryRun: true);
            AnimationAudioActions.AddSoundCue("Voices", "A01", _sound, ctx: plan);
            Assert.That(plan.Changes.Single().kind, Is.EqualTo("change"), "played another way: brought up to date");

            var own = AudioClip.Create("own", 4000, 1, 8000, false);
            try
            {
                cue.setSource = false;
                cue.clip = null;
                cue.Source.clip = own;
                cue.Source.loop = false;
                cue.Source.spatialBlend = 0.5f;
                Assert.That(cue.Play(), Is.True);
                Assert.That((cue.Source.clip, cue.Source.loop, cue.Source.spatialBlend), Is.EqualTo((own, false, 0.5f)),
                            "the source played as it is set up");
                Assert.That(cue.Length, Is.EqualTo(0.5f), "the source's own clip's length");
            }
            finally
            {
                Object.DestroyImmediate(own);
            }
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
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target, c.action))));
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
            Assert.That(live.Changes.Select(c => (c.kind, c.target, c.action)), Is.EqualTo(plan.Changes.Select(c => (c.kind, c.target, c.action))));
            var again = new OpContext(dryRun: true);
            Assert.That(AnimationAudioActions.AddPlayer(actor, again), Is.False);
            Assert.That(again.Changes, Is.Empty);
        }
    }
}
