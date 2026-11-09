using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

[assembly: ReadsRecord(UnitytkRecords.Audio, typeof(AudioRecord))]
[assembly: InternalsVisibleTo("M3trik.Unitytk.AnimationAudio.Tests")]

namespace M3trik.Unitytk.AnimationAudio
{
    /// <summary>
    /// Writes a delivery's sound cues into its clips: reads the <c>audio</c> record
    /// (<see cref="UnitytkRecords.Audio"/>, on the <c>data_export</c> carrier) through
    /// its generated shape (<see cref="AudioRecord"/>) at the one version it declares
    /// -- another version, or a record that fails that shape, is refused with its fix
    /// (re-export) -- and writes each cue into the clip the model importer cuts from
    /// the cue's take as two plain <see cref="AnimationEvent"/>s: <c>PlayAudio</c> at
    /// its time, its object the sound's <see cref="AudioClip"/> and its float the
    /// offset into the sound, and <c>StopAudio</c> at its stop, with the same clip.
    /// The model's root gets an <see cref="AnimationAudioPlayer"/> to play them.
    /// </summary>
    /// <remarks>
    /// <para><b>Whoever owns the animation owns its cues</b>, Unity's own rule for an
    /// imported asset: what the delivery holds is the source's, what the import
    /// settings hold is Unity's. The cues are written into the clip object the import
    /// builds (<c>OnPostprocessAnimation</c>), never into the model importer's clip
    /// list (the <c>.meta</c>), so the Events tab stays the project's, for its own
    /// additions, and every import writes the delivered cues afresh: a re-exported
    /// timing never drifts from its sound, and a delivered cue is changed at the
    /// source. The Events tab's events arrive on the clip before this runs and stay
    /// beside the delivered ones; one player plays both.</para>
    /// <para><b>The join</b> is the clip's take, as the Shots importer's
    /// (<see cref="ModelImporterClipAnimation.takeName"/>, ignoring case), never its
    /// name: a clip renamed in the Animation tab still plays its take's cues, and a
    /// take split there into several clips gives each part the cues inside its range,
    /// re-timed from its own start (<see cref="Place"/>): a sound that began before a
    /// part starts it at the offset it has reached, and one that runs past a part's
    /// end stops there. A shotless file's cues (<c>clip: ""</c>) play in its one
    /// take.</para>
    /// <para><b>The sound</b> is the audio file the record names (<c>sound</c>,
    /// extension included), as the delivery embeds it: its own copy, extracted beside
    /// the model on the import that finds it missing, or a same-named clip nothing
    /// rewrites whose bytes hash to the record's <c>media</c> digest (a library's).
    /// A cue whose sound is not found yet is left out and named, and lands on the
    /// reimport the extraction brings.</para>
    /// <para>Prior art: Unity's model importer events (the Events tab,
    /// <see cref="ModelImporterClipAnimation.events"/>) were weighed as the home and
    /// not adopted: they live in the <c>.meta</c>, the project's, so a re-export would
    /// leave the old timing there, or overwrite what a sound designer added.
    /// <see cref="AnimationUtility.SetAnimationEvents"/> on the clip in
    /// <c>OnPostprocessAnimation</c> is Unity's documented way to give an imported
    /// clip events at import. The sound files ride inside the FBX and come out through
    /// the carrier's extraction (<see cref="IModelMedia"/>).</para>
    /// </remarks>
    public class AnimationAudioImporter : AssetPostprocessor
    {
        static IRecordSource Records => Provided.One<IRecordSource>();

        static IModelMedia Media => Provided.One<IModelMedia>();

        // The audio folders looked in beside the model and at Assets/, before the
        // whole project.
        static readonly string[] AudioFolderNames = { "Audio", "Sounds", "SFX" };

        // Per model, through one import: the record collected off the carrier (the
        // hierarchy is not parented yet), its cues parsed once, each sound resolved
        // once, and whether any clip cues a sound (delivered or Events tab); all
        // consumed in OnPostprocessModel, where the root is final.
        static readonly Dictionary<string, string> _pending = new Dictionary<string, string>();
        static readonly Dictionary<string, Import> _imports = new Dictionary<string, Import>();

        sealed class Import
        {
            public List<Cue> cues;
            public Dictionary<string, string> digests;
            public readonly Dictionary<string, Sound> sounds =
                new Dictionary<string, Sound>(StringComparer.OrdinalIgnoreCase);
            public bool cued;
            public int placed;
            public int written;
        }

        struct Sound
        {
            public AudioClip clip;
            public bool held;
        }

        /// <summary>One cue of the record, as the importer places it.</summary>
        internal struct Cue
        {
            /// <summary>The take that plays it; "" for a shotless file's one take.</summary>
            public string clip;
            /// <summary>The sound's file name, extension included.</summary>
            public string sound;
            /// <summary>Seconds from the take's start.</summary>
            public float time;
            /// <summary>Seconds into the sound.</summary>
            public float offset;
            /// <summary>Seconds from the take's start.</summary>
            public float stop;
        }

        /// <summary>
        /// What this import writes, versioned: Unity reimports every model it
        /// processed when the number changes, so a model imported by an earlier
        /// rule is never left with its cues. Raise it when what the import writes
        /// changes.
        /// </summary>
        public override uint GetVersion() => 2;

        bool ShouldProcess() => Imports.IsDelivery(assetPath);

        void Forget()
        {
            _pending.Remove(assetPath);
            _imports.Remove(assetPath);
        }

        void OnPreprocessModel()
        {
            // Fresh-import reset: state stranded by an import that aborted between the
            // callbacks must not resurrect on a re-export without the carrier.
            Forget();
        }

        void OnPostprocessGameObjectWithUserProperties(GameObject go, string[] propNames, object[] values)
        {
            if (!ShouldProcess()) return;
            if (Records.TryGetRecord(propNames, values, UnitytkRecords.Audio, out string json))
                Records.Collect(_pending, assetPath, go, json);
        }

        Import Current()
        {
            if (_imports.TryGetValue(assetPath, out Import import)) return import;
            import = new Import();
            _pending.TryGetValue(assetPath, out string json);
            import.cues = Parse(json, assetPath, out import.digests);
            _imports[assetPath] = import;
            return import;
        }

        void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!ShouldProcess()) return;
            Import import = Current();
            var events = new List<AnimationEvent>(AnimationUtility.GetAnimationEvents(clip));
            if (import.cues.Count > 0)
            {
                var importer = assetImporter as ModelImporter;
                ModelImporterClipAnimation cut = null;
                foreach (ModelImporterClipAnimation c in ClipsOf(importer))
                    if (string.Equals(c.name, clip.name, StringComparison.Ordinal))
                    {
                        cut = c;
                        break;
                    }
                int added = 0;
                foreach (Placed placed in Place(import.cues, cut, TakesOf(importer), clip.name, clip.length))
                {
                    import.placed++;
                    Sound sound = SoundOf(import, placed.cue.sound);
                    if (sound.clip == null) continue;
                    events.AddRange(AnimationAudioActions.Pair(sound.clip, placed.time, placed.stop, placed.offset));
                    added++;
                }
                if (added > 0)
                {
                    // By time, stably: the Events tab's events keep their order among the delivered.
                    AnimationUtility.SetAnimationEvents(clip, events.OrderBy(e => e.time).ToArray());
                    import.written += added;
                    ImportLog.Info(assetPath, $"wrote {added} cue(s) into '{clip.name}'");
                }
            }
            foreach (AnimationEvent e in events)
                import.cued |= e.functionName == PersistedStrings.PlayAudioFunction
                               || e.functionName == PersistedStrings.StopAudioFunction;
        }

        void OnPostprocessModel(GameObject root)
        {
            try
            {
                if (ShouldProcess()) Finish(root);
            }
            finally
            {
                Forget();
            }
        }

        void Finish(GameObject root)
        {
            Import import = Current();
            if (import.cues.Count > 0)
            {
                var importer = assetImporter as ModelImporter;
                if (importer != null && !importer.importAnimation)
                    ImportLog.Warning(assetPath, $"carries {import.cues.Count} audio cue(s) but imports no animation, so " +
                                                 "no clip plays them -- turn Import Animation on in the model's Animation tab.");
                else if (import.placed == 0)
                    ImportLog.Warning(assetPath, $"carries {import.cues.Count} audio cue(s) no clip plays: no clip is cut " +
                                                 "from their takes over their times -- re-export the file, or add those clips " +
                                                 "in the model's Animation tab.");
                // The sounds the delivery embeds (the record's media) and the model does
                // not hold yet: extracted beside it, and the model reimported once.
                var embedded = new List<string>();
                foreach (Cue cue in import.cues)
                    if (import.digests.ContainsKey(cue.sound) && !embedded.Contains(cue.sound, StringComparer.OrdinalIgnoreCase))
                        embedded.Add(cue.sound);
                bool extracting = Media.RequestEmbeddedFiles(assetPath, embedded, n => SoundOf(import, n).held);
                foreach (var pair in import.sounds)
                    if (pair.Value.clip == null)
                        ImportLog.Warning(assetPath, $"no AudioClip found for '{pair.Key}', so its cues are left out -- " +
                                                     (extracting
                                                         ? "extracting the FBX's embedded audio beside it and reimporting once."
                                                         : "add the file to the project (beside the model, or in an Audio folder) and reimport the model."));
            }
            if (!import.cued) return;
            // Unity sends a clip's events to the GameObject whose Animator plays it: the root.
            if (root.GetComponent<AnimationAudioPlayer>() != null) return;
            AudioSource source = root.GetComponent<AudioSource>();
            if (source == null) source = root.AddComponent<AudioSource>();
            AnimationAudioPlayer.Defaults(source);
            root.AddComponent<AnimationAudioPlayer>();
        }

        /// <summary>The clips this import builds: the importer's own list, else the one
        /// Unity derives from the file's takes; empty when it imports no animation.</summary>
        static ModelImporterClipAnimation[] ClipsOf(ModelImporter importer)
        {
            if (importer == null || !importer.importAnimation) return new ModelImporterClipAnimation[0];
            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
            return clips ?? new ModelImporterClipAnimation[0];
        }

        static TakeInfo[] TakesOf(ModelImporter importer)
            => importer == null ? new TakeInfo[0] : importer.importedTakeInfos ?? new TakeInfo[0];

        // --- each cue's place in a clip ---------------------------------------------

        /// <summary>A cue placed in one clip: when it starts and stops there, in seconds
        /// from the clip's start, and the offset into the sound it starts at.</summary>
        internal struct Placed
        {
            public Cue cue;
            public float time;
            public float offset;
            public float stop;
        }

        /// <summary>
        /// The cues the clip <paramref name="clipName"/> (<paramref name="length"/>
        /// seconds long, cut as <paramref name="cut"/> says) plays, each re-timed from
        /// the clip's start. The clip plays the cues of the take it is cut from (its
        /// take name, ignoring case; a cue naming no take plays in a file of one take),
        /// each placed from where the clip starts in its take: its first frame at the
        /// take's sample rate, less the take's start (<paramref name="takes"/>). A cue
        /// that began before the clip starts at its start, that much further into the
        /// sound; one that runs past the clip's end stops there; one heard nowhere in
        /// the clip (within half a sample) is left out. A clip with no
        /// <paramref name="cut"/> is the whole take of its name.
        /// </summary>
        internal static List<Placed> Place(IList<Cue> cues, ModelImporterClipAnimation cut, TakeInfo[] takes,
                                           string clipName, float length)
        {
            string take = cut != null ? cut.takeName : clipName;
            float start = 0f;   // seconds into its take the clip starts
            float slack = 1e-4f; // half a sample: how far a cue may miss and still be the clip's
            foreach (TakeInfo info in takes ?? new TakeInfo[0])
            {
                if (!string.Equals(info.name, take, StringComparison.OrdinalIgnoreCase) || !(info.sampleRate > 0f))
                    continue;
                if (cut != null) start = cut.firstFrame / info.sampleRate - info.startTime;
                slack = 0.5f / info.sampleRate;
                break;
            }
            bool oneTake = takes == null || takes.Length <= 1;
            var placed = new List<Placed>();
            foreach (Cue cue in cues)
            {
                bool joins = cue.clip.Length > 0
                    ? string.Equals(cue.clip, take, StringComparison.OrdinalIgnoreCase)
                    : oneTake;
                if (!joins) continue;
                float time = cue.time - start;
                float stop = cue.stop - start;
                if (stop <= slack || time >= length - slack) continue;
                float offset = cue.offset;
                if (time < 0f)
                {
                    offset -= time;  // the part starts this far into the sound
                    time = 0f;
                }
                placed.Add(new Placed { cue = cue, time = time, offset = offset, stop = Mathf.Clamp(stop, time, length) });
            }
            return placed;
        }

        // --- the record --------------------------------------------------------------

        /// <summary>
        /// The record's cues, read the one way a record is read
        /// (<see cref="IRecordSource.Read{T}"/>: its generated shape, at
        /// <see cref="AudioRecord.CurrentVersion"/> alone), and its <c>media</c>
        /// digests by sound name in <paramref name="digests"/>. Empty when there is
        /// none, or it is refused (re-export).
        /// </summary>
        internal static List<Cue> Parse(string json, string assetPath, out Dictionary<string, string> digests)
        {
            var cues = new List<Cue>();
            digests = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var payload = Records.Read(UnitytkRecords.Audio, json, AudioRecord.CurrentVersion,
                                       AudioRecord.Shape.Payload.Read, "AnimationAudio", assetPath);
            if (payload == null) return cues;
            if (payload.media != null)
                foreach (var pair in payload.media)
                    if (pair.Value is string digest && digest.Length > 0)
                        digests[pair.Key.Trim()] = digest;
            foreach (AudioRecord.Shape.Cue cue in payload.cues)
            {
                string sound = cue.sound.Trim();
                if (sound.Length == 0) continue;
                cues.Add(new Cue
                {
                    clip = cue.clip.Trim(),
                    sound = sound,
                    time = (float)cue.time.Value,
                    offset = (float)Math.Max(0.0, cue.offset.Value),
                    stop = (float)cue.stop.Value,
                });
            }
            return cues;
        }

        // --- the sound ---------------------------------------------------------------

        Sound SoundOf(Import import, string name)
        {
            if (import.sounds.TryGetValue(name, out Sound sound)) return sound;
            import.digests.TryGetValue(name, out string digest);
            sound.clip = ResolveClip(name, assetPath, UsableDigest(name, digest), out sound.held);
            // The clip the events bind: the model reimports when its file changes or goes.
            string path = sound.clip != null ? AssetDatabase.GetAssetPath(sound.clip) : null;
            if (!string.IsNullOrEmpty(path) && !path.Contains(".fbm/")) context.DependsOnSourceAsset(path);
            import.sounds[name] = sound;
            return sound;
        }

        /// <summary>
        /// The record's <paramref name="digest"/> of the sound <paramref name="name"/>,
        /// or null when the model's own copy of its file is not an AudioClip Unity
        /// imports -- an .m4a (measured, 6000.3: a plain asset; FLAC, MP3, Ogg, AIFF
        /// and WAV import): no clip can hash to it, so the sound binds by name as one
        /// with no digest does (<see cref="ResolveClip"/>).
        /// </summary>
        string UsableDigest(string name, string digest)
        {
            if (string.IsNullOrEmpty(digest)) return null;
            bool any = false;
            foreach (var path in Media.OwnMediaCopies(assetPath, name))
            {
                if (AssetDatabase.LoadAssetAtPath<AudioClip>(path) != null) return digest;
                any = true;
            }
            return any ? null : digest;
        }

        /// <summary>
        /// The clip the model at <paramref name="modelPath"/> binds for the sound
        /// <paramref name="name"/> (a file name; its extension, when it has one, must
        /// match too -- never a partial match), and whether the model
        /// <paramref name="held"/> it. Two kinds of clip qualify: the model's own copy
        /// in its media folder (<see cref="IModelMedia.MediaFolder"/>,
        /// <see cref="IModelMedia.IsOwnMedia"/>), and a same-named clip nothing will
        /// rewrite or take away (<see cref="IModelMedia.IsReusableBy"/>: a library's,
        /// never another model's extraction). With the record's
        /// <paramref name="digest"/>: the own copy, or else the first such clip, whose
        /// bytes hash to it, held; else the own copy as it is (one edited after
        /// extraction) or none. Without one (the delivery carries no file for the
        /// sound): the own copy, else such a clip in the model's media folder, else
        /// the first the lookup finds (beside the model, an Audio, Sounds or SFX
        /// folder, the project).
        /// </summary>
        static AudioClip ResolveClip(string name, string modelPath, string digest, out bool held)
        {
            held = false;
            var paths = AudioClipPaths(name, SearchFolders(modelPath));
            string media = Media.MediaFolder(modelPath);
            string own = paths.FirstOrDefault(p => FolderOf(p) == media && Media.IsOwnMedia(modelPath, p));
            var reusable = paths.Where(p => p != own && Media.IsReusableBy(modelPath, p)).ToList();
            string bound;
            if (string.IsNullOrEmpty(digest))
            {
                held = Media.HoldsCurrent(modelPath, name);
                bound = own ?? reusable.FirstOrDefault(p => FolderOf(p) == media) ?? reusable.FirstOrDefault();
            }
            else
            {
                string match = own != null && HashesTo(own, digest)
                    ? own
                    : reusable.FirstOrDefault(p => HashesTo(p, digest));
                held = match != null;
                bound = match ?? own;
            }
            return bound != null ? AssetDatabase.LoadAssetAtPath<AudioClip>(bound) : null;
        }

        /// <summary>Where a model's sounds are looked for, nearest first: its folder,
        /// the Audio, Sounds and SFX folders beside it, those at Assets/, then the
        /// whole project.</summary>
        static string[] SearchFolders(string modelPath)
        {
            var dirs = new List<string>();
            string folder = FolderOf(modelPath);
            if (AssetDatabase.IsValidFolder(folder)) dirs.Add(folder);
            foreach (string name in AudioFolderNames)
                if (AssetDatabase.IsValidFolder(folder + "/" + name)) dirs.Add(folder + "/" + name);
            foreach (string name in AudioFolderNames)
                if (!dirs.Contains("Assets/" + name) && AssetDatabase.IsValidFolder("Assets/" + name)) dirs.Add("Assets/" + name);
            if (!dirs.Contains("Assets")) dirs.Add("Assets");
            return dirs.ToArray();
        }

        /// <summary>The asset path of every AudioClip named <paramref name="name"/>
        /// under <paramref name="folders"/> (each folder and every folder below it),
        /// each once, nearest folder first.</summary>
        static List<string> AudioClipPaths(string name, string[] folders)
        {
            string ext = System.IO.Path.GetExtension(name);
            string stem = System.IO.Path.GetFileNameWithoutExtension(name);
            var cmp = StringComparison.OrdinalIgnoreCase;
            var paths = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets($"t:AudioClip {stem}", folders))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (paths.Contains(path)
                    || !string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), stem, cmp)
                    || (ext.Length > 0 && !string.Equals(System.IO.Path.GetExtension(path), ext, cmp)))
                    continue;
                paths.Add(path);
            }
            return paths;
        }

        static string FolderOf(string assetPath)
            => (System.IO.Path.GetDirectoryName(assetPath) ?? "").Replace('\\', '/');

        /// <summary>Whether the file at <paramref name="assetPath"/> hashes to
        /// <paramref name="digest"/>: the record's <c>media</c> digest, the SHA-256 of
        /// the sound file's bytes in hex, which Unity's extraction writes out byte for
        /// byte.</summary>
        static bool HashesTo(string assetPath, string digest)
            => string.Equals(Digest(assetPath), digest, StringComparison.OrdinalIgnoreCase);

        // Each clip file's digest, kept by its write time and size: every import of a
        // model asks for each of its sounds, and a library clip is hashed once.
        static readonly Dictionary<string, KeyValuePair<string, string>> _digests =
            new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The SHA-256 of the file at <paramref name="assetPath"/>'s bytes,
        /// lowercase hex; null when it cannot be read.</summary>
        static string Digest(string assetPath)
        {
            try
            {
                var info = new System.IO.FileInfo(assetPath);
                if (!info.Exists) return null;
                string stamp = info.LastWriteTimeUtc.Ticks + ":" + info.Length;
                if (_digests.TryGetValue(assetPath, out var kept) && kept.Key == stamp)
                    return kept.Value;
                string digest;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var stream = info.OpenRead())
                    digest = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                _digests[assetPath] = new KeyValuePair<string, string>(stamp, digest);
                return digest;
            }
            catch (System.IO.IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
