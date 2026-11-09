using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using M3trik.Unitytk.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using M3trik.Unitytk.Testing;
namespace M3trik.Unitytk.AnimationAudio.Tests
{
    /// <summary>
    /// The importer writes each delivered cue into the clip cut from its take as
    /// PlayAudio and StopAudio AnimationEvents, at the cue's seconds from the clip's
    /// start, the sound's AudioClip their object: a real import of a two-take ASCII
    /// FBX (contiguous shots, a sound across the cut stopped at it and played on from
    /// its offset in the next, a sound trimmed at the head), through a reimport, a
    /// renamed and a split clip; never in the importer's Events tab, whose own events
    /// stay beside them; and a v2 record refused with its fix.
    /// </summary>
    public class AnimationAudioImporterTests
    {
        const string Folder = "Assets/UnitytkAnimationAudioTests";
        const string Model = Folder + "/two_shots.fbx";

        // Intro is 0-2 s of the timeline, Outro 2-4 s, at 30 samples a second.
        // door.wav (1 s) plays 0.25-0.75 s into Intro from 0.1 s into the file;
        // chime.wav (1.5 s) starts 1.5 s into Intro and runs 1 s past the cut: Intro
        // stops it at its end, Outro plays it on from 0.5 s in, to 1 s.
        const string Record =
            "{\"version\": 3, \"cues\": [" +
            "{\"clip\": \"Intro\", \"sound\": \"door.wav\", \"time\": 0.25, \"offset\": 0.1, \"stop\": 0.75}, " +
            "{\"clip\": \"Intro\", \"sound\": \"chime.wav\", \"time\": 1.5, \"offset\": 0.0, \"stop\": 2.0}, " +
            "{\"clip\": \"outro\", \"sound\": \"chime.wav\", \"time\": 0.0, \"offset\": 0.5, \"stop\": 1.0}]}";

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(Folder);

        static void Import(string record)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "UnitytkAnimationAudioTests");
            Wav.Write(Folder + "/door.wav", 1f);
            Wav.Write(Folder + "/chime.wav", 1.5f);
            AssetDatabase.ImportAsset(Folder + "/door.wav", ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(Folder + "/chime.wav", ImportAssetOptions.ForceSynchronousImport);
            File.WriteAllText(Model, TwoTakeFbx.Write("audio", record), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        static Dictionary<string, AnimationClip> Clips()
            => AssetDatabase.LoadAllAssetsAtPath(Model).OfType<AnimationClip>()
                            .Where(c => !c.name.StartsWith("__preview__"))
                            .ToDictionary(c => c.name);

        static AudioClip Sound(string file) => AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/" + file);

        // A clip's events as (function, sound, time, float), times rounded to the sample.
        static (string fn, string sound, float time, float value)[] Events(AnimationClip clip)
            => AnimationUtility.GetAnimationEvents(clip)
                               .Select(e => (e.functionName,
                                             e.objectReferenceParameter != null ? e.objectReferenceParameter.name : null,
                                             Round(e.time), Round(e.floatParameter)))
                               .ToArray();

        static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;

        const string Play = PersistedStrings.PlayAudioFunction;
        const string Stop = PersistedStrings.StopAudioFunction;

        static readonly (string, string, float, float)[] IntroCues =
        {
            (Play, "door", 0.25f, 0.1f), (Stop, "door", 0.75f, 0f), (Play, "chime", 1.5f, 0f), (Stop, "chime", 2f, 0f),
        };

        static readonly (string, string, float, float)[] OutroCues = { (Play, "chime", 0f, 0.5f), (Stop, "chime", 1f, 0f) };

        [Test]
        public void Each_cue_lands_in_its_takes_clip_at_its_seconds_with_its_sound()
        {
            Import(Record);
            var clips = Clips();
            Assert.That(Events(clips["Intro"]), Is.EqualTo(IntroCues),
                        "a sound across the cut stops at Intro's end; one trimmed at the head starts 0.1 s in");
            Assert.That(Events(clips["Outro"]), Is.EqualTo(OutroCues),
                        "the take named ignoring case; the cut sound plays on from its offset and stops on its own");
            var play = AnimationUtility.GetAnimationEvents(clips["Outro"])[0];
            Assert.That(play.objectReferenceParameter, Is.EqualTo(Sound("chime.wav")), "the project's AudioClip asset");
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponent<AnimationAudioPlayer>(), Is.Not.Null,
                        "the model's root gets the player its Animator's events reach");
        }

        [Test]
        public void The_cues_survive_a_reimport_and_never_enter_the_events_tab()
        {
            Import(Record);
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var clips = Clips();
            Assert.That(Events(clips["Intro"]), Is.EqualTo(IntroCues), "written afresh, not doubled");
            Assert.That(Events(clips["Outro"]), Is.EqualTo(OutroCues));
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            Assert.That(importer.clipAnimations, Is.Empty, "the import wrote no clip list into the .meta");
            Assert.That(importer.defaultClipAnimations.SelectMany(c => c.events), Is.Empty,
                        "the Events tab Unity shows holds none of the delivered cues");
        }

        [Test]
        public void An_events_tab_event_stays_beside_the_delivered_cues()
        {
            Import(Record);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            var cuts = importer.defaultClipAnimations;
            var outro = cuts.Single(c => c.takeName == "Outro");
            // The Events tab states time as a fraction of the clip: 0.75 of 2 s is 1.5 s.
            outro.events = new[]
            {
                new AnimationEvent { time = 0.75f, functionName = Play, objectReferenceParameter = Sound("door.wav") },
            };
            importer.clipAnimations = cuts;
            importer.SaveAndReimport();

            var events = Events(Clips()["Outro"]);
            Assert.That(events, Is.EqualTo(new[] { (Play, "chime", 0f, 0.5f), (Stop, "chime", 1f, 0f), (Play, "door", 1.5f, 0f) }),
                        "Unity's own event kept, in time order with the delivered");
            importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            Assert.That(importer.clipAnimations.Single(c => c.takeName == "Outro").events.Select(e => e.functionName),
                        Is.EqualTo(new[] { Play }), "the Events tab holds only what was authored there");
        }

        [Test]
        public void A_renamed_clip_still_plays_its_takes_cues()
        {
            Import(Record);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            var cuts = importer.defaultClipAnimations;
            cuts.Single(c => c.takeName == "Outro").name = "Outro Loop";
            importer.clipAnimations = cuts;
            importer.SaveAndReimport();
            Assert.That(Events(Clips()["Outro Loop"]), Is.EqualTo(OutroCues));
        }

        [Test]
        public void A_split_clip_gives_each_part_the_cues_inside_it_retimed_from_its_start()
        {
            Import(Record);
            var importer = (ModelImporter)AssetImporter.GetAtPath(Model);
            var cuts = importer.defaultClipAnimations.ToList();
            var outro = cuts.Single(c => c.takeName == "Outro");
            float first = outro.firstFrame;  // 60: the take's start at 30 samples a second
            var a = Copy(outro, "Outro A", first, first + 15f);       // 0-0.5 s of the take
            var b = Copy(outro, "Outro B", first + 15f, first + 60f); // 0.5-2 s
            cuts.Remove(outro);
            cuts.Add(a);
            cuts.Add(b);
            importer.clipAnimations = cuts.ToArray();
            importer.SaveAndReimport();
            var clips = Clips();
            Assert.That(Events(clips["Outro A"]), Is.EqualTo(new[] { (Play, "chime", 0f, 0.5f), (Stop, "chime", 0.5f, 0f) }),
                        "the part the sound runs past stops it at its end");
            Assert.That(Events(clips["Outro B"]), Is.EqualTo(new[] { (Play, "chime", 0f, 1f), (Stop, "chime", 0.5f, 0f) }),
                        "the next part plays it on from where it had reached");
        }

        static ModelImporterClipAnimation Copy(ModelImporterClipAnimation from, string name, float first, float last)
            => new ModelImporterClipAnimation
            {
                name = name, takeName = from.takeName, firstFrame = first, lastFrame = last, loopTime = from.loopTime,
            };

        [Test]
        public void A_version_2_record_is_refused_with_its_fix_and_writes_no_cues()
        {
            LogAssert.Expect(LogType.Warning, new Regex("audio record v2; unitytk reads v3 only.*re-export"));
            Import("{\"version\": 2, \"fps\": 30.0, \"events\": [{\"clip\": \"Intro\", \"frame\": 12, \"name\": \"door\"}]}");
            Assert.That(Clips().Values.SelectMany(AnimationUtility.GetAnimationEvents), Is.Empty);
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(Model).GetComponent<AnimationAudioPlayer>() == null,
                        "no cues, no player");
        }

        [Test]
        public void A_sound_the_project_lacks_is_named_and_its_cues_left_out()
        {
            LogAssert.Expect(LogType.Warning, new Regex("no AudioClip found for 'gone.wav'"));
            Import("{\"version\": 3, \"cues\": [{\"clip\": \"Intro\", \"sound\": \"gone.wav\", \"time\": 0.5, \"offset\": 0, \"stop\": 1}," +
                   " {\"clip\": \"Intro\", \"sound\": \"door.wav\", \"time\": 1, \"offset\": 0, \"stop\": 1.5}]}");
            Assert.That(Events(Clips()["Intro"]), Is.EqualTo(new[] { (Play, "door", 1f, 0f), (Stop, "door", 1.5f, 0f) }));
        }

        // -- the sound a cue binds, and the extraction it asks for ----------------------

        // The record's media digest of a file the test wrote: the SHA-256 of its bytes, hex.
        static string DigestOf(string assetPath)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(assetPath))).Replace("-", "").ToLowerInvariant();
        }

        // A v3 audio record: one cue per sound in Intro, its media digests as given.
        static string MediaRecord(string doorDigest, string chimeDigest)
            => "{\"version\": 3, \"cues\": [" +
               "{\"clip\": \"Intro\", \"sound\": \"door.wav\", \"time\": 0.25, \"offset\": 0, \"stop\": 0.75}, " +
               "{\"clip\": \"Intro\", \"sound\": \"chime.wav\", \"time\": 1.0, \"offset\": 0, \"stop\": 1.5}], " +
               $"\"media\": {{\"door.wav\": \"{doorDigest}\", \"chime.wav\": \"{chimeDigest}\"}}}}";

        // Whether the extraction memo (PersistedStrings.EmbeddedRequestedMemo, in the
        // project's Library) holds the model's current version: it asked of that version.
        static bool Asked()
        {
            var model = new FileInfo(Model);
            string version = Model + "|" + model.LastWriteTimeUtc.Ticks + "|" + model.Length;
            return File.Exists(PersistedStrings.EmbeddedRequestedMemo)
                   && File.ReadAllLines(PersistedStrings.EmbeddedRequestedMemo).Any(l => l.Split('	')[0] == version);
        }

        [Test]
        public void A_same_named_clip_stands_in_only_when_it_hashes_to_the_records_digest()
        {
            // The project's door.wav is the delivered one, byte for byte; its chime.wav is
            // another sound of the same name, which never stands in for the delivered file.
            Import(Record);  // writes the project's clips
            LogAssert.Expect(LogType.Warning, new Regex("no AudioClip found for 'chime.wav'"));
            Import(MediaRecord(DigestOf(Folder + "/door.wav"), new string('0', 64)));
            Assert.That(Events(Clips()["Intro"]), Is.EqualTo(new[] { (Play, "door", 0.25f, 0f), (Stop, "door", 0.75f, 0f) }),
                        "door.wav hashes to its digest and binds; chime.wav does not, and its cue is left out");
        }

        [Test]
        public void A_clip_found_elsewhere_is_not_extracted_again_and_the_memo_outlives_a_session()
        {
            // Every sound found by its digest: nothing is asked of the FBX.
            Import(Record);
            Import(MediaRecord(DigestOf(Folder + "/door.wav"), DigestOf(Folder + "/chime.wav")));
            Assert.That(Events(Clips()["Intro"]).Length, Is.EqualTo(4), "both sounds bound");
            Assert.That(Asked(), Is.False, "a sound the project holds is never extracted again");

            // A sound it lacks is asked for once per version of the model, and the ask is
            // kept in the project's Library -- a later session's reimport asks no more.
            LogAssert.Expect(LogType.Warning, new Regex("no AudioClip found for 'chime.wav'.*extracting the FBX's embedded audio"));
            Import(MediaRecord(DigestOf(Folder + "/door.wav"), new string('0', 64)));
            Assert.That(Asked(), Is.True, "the version asked, in the Library memo");
            LogAssert.Expect(LogType.Warning, new Regex("no AudioClip found for 'chime.wav'.*add the file to the project"));
            AssetDatabase.ImportAsset(Model, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Assert.That(Asked(), Is.True, "the same version asks no more, its ask kept");
        }

        // -- the placement, over Unity's own clip and take types ------------------------

        static AnimationAudioImporter.Cue Cue(string clip, float time, float offset, float stop)
            => new AnimationAudioImporter.Cue { clip = clip, sound = "s.wav", time = time, offset = offset, stop = stop };

        static readonly TakeInfo[] OneTake = { new TakeInfo { name = "Take 001", startTime = 1f, stopTime = 3f, sampleRate = 30f } };

        [Test]
        public void A_shotless_files_cues_play_in_its_one_take_from_where_its_clip_starts()
        {
            var cut = new ModelImporterClipAnimation { name = "Take 001", takeName = "Take 001", firstFrame = 30f, lastFrame = 90f };
            var placed = AnimationAudioImporter.Place(new[] { Cue("", 0.5f, 0f, 1f) }, cut, OneTake, "Take 001", 2f).Single();
            Assert.That((placed.time, placed.offset, placed.stop), Is.EqualTo((0.5f, 0f, 1f)));
        }

        [Test]
        public void A_cue_naming_no_take_plays_in_no_clip_of_a_file_of_several()
        {
            var takes = new[] { OneTake[0], new TakeInfo { name = "Other", startTime = 3f, stopTime = 4f, sampleRate = 30f } };
            Assert.That(AnimationAudioImporter.Place(new[] { Cue("", 0.5f, 0f, 1f) }, null, takes, "Take 001", 2f), Is.Empty);
        }

        [Test]
        public void A_cue_heard_nowhere_in_a_trimmed_clip_is_left_out()
        {
            // The take's 0.5-1 s, trimmed in the Animation tab: a cue over 1.2-1.6 s misses it.
            var cut = new ModelImporterClipAnimation { name = "Trim", takeName = "Take 001", firstFrame = 45f, lastFrame = 60f };
            Assert.That(AnimationAudioImporter.Place(new[] { Cue("", 1.2f, 0f, 1.6f) }, cut, OneTake, "Trim", 0.5f), Is.Empty);
            var kept = AnimationAudioImporter.Place(new[] { Cue("", 0.2f, 0.1f, 0.9f) }, cut, OneTake, "Trim", 0.5f).Single();
            Assert.That((Round(kept.time), Round(kept.offset), Round(kept.stop)), Is.EqualTo((0f, 0.4f, 0.4f)),
                        "it began 0.3 s before the clip: 0.3 s further into the sound, stopped 0.4 s in");
        }

        /// <summary>A silent mono 16-bit WAV of <paramref name="seconds"/>, as a DCC's sound file.</summary>
        internal static class Wav
        {
            public static void Write(string path, float seconds, int rate = 8000)
            {
                int samples = Mathf.RoundToInt(seconds * rate);
                using (var stream = new FileStream(path, FileMode.Create))
                using (var w = new BinaryWriter(stream))
                {
                    w.Write(Encoding.ASCII.GetBytes("RIFF"));
                    w.Write(36 + samples * 2);
                    w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    w.Write(16);
                    w.Write((short)1);
                    w.Write((short)1);
                    w.Write(rate);
                    w.Write(rate * 2);
                    w.Write((short)2);
                    w.Write((short)16);
                    w.Write(Encoding.ASCII.GetBytes("data"));
                    w.Write(samples * 2);
                    w.Write(new byte[samples * 2]);
                }
            }
        }
    }
}
