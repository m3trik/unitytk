using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

[assembly: M3trik.Unitytk.Core.ReadsRecord(UnitytkRecords.Animation, typeof(AnimationRecord))]
[assembly: InternalsVisibleTo("M3trik.Unitytk.Shots.Tests")]

namespace M3trik.Unitytk.Shots
{
    /// <summary>
    /// Writes a delivery's shots onto its model: reads the <c>animation</c> record
    /// (<see cref="UnitytkRecords.Animation"/>, on the <c>data_export</c> carrier)
    /// through its generated shape (<see cref="AnimationRecord"/>) at the one version
    /// it declares -- another version, or a record that fails that shape, is refused
    /// with its fix (re-export) -- and resolves each shot to the clip the model
    /// importer made of its take, the take the shot's <c>clip</c> names. The model's
    /// <see cref="ShotList"/> component is rewritten at every import: the delivered
    /// entries are the source's.
    /// </summary>
    /// <remarks>
    /// <para>The join is the clip's take (<see cref="ModelImporterClipAnimation.takeName"/>),
    /// never its name: a clip renamed in the model's Animation tab still plays its
    /// shot, and a take split there into several clips gives each of them the shot's
    /// entry, each a part of the shot. A shot no clip is cut from is warned: its take
    /// is missing (re-export) or the Animation tab cuts no clip from it.</para>
    /// <para>Prior art: Unity's own FBX import builds the clips from the takes and knows
    /// nothing of shots; this reads the DCC's record through the carrier, as every
    /// unitytk importer does (<see cref="IRecordSource"/>), and joins by the take name
    /// Unity keeps on each clip.</para>
    /// </remarks>
    public class ShotsImporter : AssetPostprocessor
    {
        /// <summary>Bump to reimport every model when what the import writes changes.</summary>
        const uint VERSION = 1;

        /// <inheritdoc/>
        public override uint GetVersion() => VERSION;

        // Per model, the record collected in OnPostprocessGameObjectWithUserProperties
        // (the hierarchy is not parented yet) and the clips OnPostprocessAnimation was
        // handed, by name; both consumed in OnPostprocessModel, where the root is final.
        static readonly Dictionary<string, string> _pending = new Dictionary<string, string>();
        static readonly Dictionary<string, Dictionary<string, AnimationClip>> _clips =
            new Dictionary<string, Dictionary<string, AnimationClip>>();

        bool ShouldProcess() => Imports.IsDelivery(assetPath);

        void Forget()
        {
            _pending.Remove(assetPath);
            _clips.Remove(assetPath);
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
            IRecordSource records = Provided.One<IRecordSource>();
            if (records.TryGetRecord(propNames, values, UnitytkRecords.Animation, out string json))
                records.Collect(_pending, assetPath, go, json);
        }

        void OnPostprocessAnimation(GameObject root, AnimationClip clip)
        {
            if (!ShouldProcess()) return;
            if (!_clips.TryGetValue(assetPath, out var built))
                _clips[assetPath] = built = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            built[clip.name] = clip;
        }

        void OnPostprocessModel(GameObject root)
        {
            try
            {
                if (ShouldProcess()) Write(root);
            }
            finally
            {
                Forget();
            }
        }

        void Write(GameObject root)
        {
            var existing = root.GetComponent<ShotList>();
            _pending.TryGetValue(assetPath, out string json);
            var payload = Provided.One<IRecordSource>().Read(
                UnitytkRecords.Animation, json, AnimationRecord.CurrentVersion,
                AnimationRecord.Shape.Payload.Read, "Shots", assetPath);
            if (payload == null || payload.shots.Count == 0)
            {
                if (existing != null)
                {
                    // No current record (absent, emptied or refused): the model matches the source.
                    UnityEngine.Object.DestroyImmediate(existing, true);
                    ImportLog.Info(assetPath, $"removed the stale shots on '{root.name}' (no current animation record)");
                }
                return;
            }

            var importer = assetImporter as ModelImporter;
            if (importer != null && !importer.importAnimation)
            {
                if (existing != null) UnityEngine.Object.DestroyImmediate(existing, true);
                ImportLog.Warning(assetPath, $"carries {payload.shots.Count} shot(s) but imports no animation, so " +
                                             "they have no clips -- turn Import Animation on in the model's Animation tab.");
                return;
            }
            _clips.TryGetValue(assetPath, out var built);
            var resolved = Resolve(payload.shots, ClipsOf(importer), TakesOf(importer),
                                   built ?? new Dictionary<string, AnimationClip>(), out var noTake, out var noClip);
            var shots = existing != null ? existing : root.AddComponent<ShotList>();
            shots.entries = resolved;
            if (noTake.Count > 0)
                ImportLog.Warning(assetPath,
                    $"carries no take for shot(s) {string.Join(", ", noTake)}, so they have no clip -- re-export the " +
                    "file from a current DCC exporter (one take per shot, named after it).");
            if (noClip.Count > 0)
                ImportLog.Warning(assetPath,
                    $"cuts no clip from the take(s) of shot(s) {string.Join(", ", noClip)} -- add a clip from each " +
                    "take in the model's Animation tab, or reset its clip list.");
            ImportLog.Info(assetPath, $"wrote {resolved.Count} shot entr{(resolved.Count == 1 ? "y" : "ies")} onto '{root.name}'");
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

        /// <summary>
        /// Each record shot's entries: one per clip cut from the take the shot names
        /// (<paramref name="clips"/>' take names, ignoring case), in clip order, each
        /// referencing the clip this import built of it (<paramref name="built"/>, by
        /// clip name). A shot no clip is cut from lands in <paramref name="noTake"/>
        /// when <paramref name="takes"/> holds no take of its name, else in
        /// <paramref name="noClip"/>.
        /// </summary>
        internal static List<Shot> Resolve(IList<AnimationRecord.Shape.Shot> record, ModelImporterClipAnimation[] clips,
                                           TakeInfo[] takes, IDictionary<string, AnimationClip> built,
                                           out List<string> noTake, out List<string> noClip)
        {
            var entries = new List<Shot>();
            noTake = new List<string>();
            noClip = new List<string>();
            foreach (var shot in record)
            {
                int found = 0;
                foreach (var cut in clips)
                {
                    if (!string.Equals(cut.takeName, shot.clip, StringComparison.OrdinalIgnoreCase)
                        || !built.TryGetValue(cut.name, out AnimationClip clip) || clip == null)
                        continue;
                    entries.Add(Entry(shot, clip));
                    found++;
                }
                if (found > 0) continue;
                bool hasTake = false;
                foreach (var take in takes)
                    hasTake |= string.Equals(take.name, shot.clip, StringComparison.OrdinalIgnoreCase);
                (hasTake ? noClip : noTake).Add(shot.clip);
            }
            return entries;
        }

        /// <summary>A record shot as the component keeps it, on <paramref name="clip"/>:
        /// its metadata one field per key, each value written back as JSON text.</summary>
        static Shot Entry(AnimationRecord.Shape.Shot shot, AnimationClip clip)
        {
            var entry = new Shot { clip = clip, description = shot.description ?? "" };
            if (shot.metadata != null)
                foreach (var pair in shot.metadata)
                    entry.metadata.Add(new ShotField { key = pair.Key, json = Core.Json.Write(Whole(pair.Value)) });
            return entry;
        }

        /// <summary>
        /// <paramref name="value"/>, a parsed JSON tree (every number a double), with each
        /// whole number a <c>long</c>, so it writes back as the integer the source wrote
        /// (<c>2</c>, not <c>2.0</c>).
        /// </summary>
        static object Whole(object value)
        {
            switch (value)
            {
                case double d when d == Math.Floor(d) && Math.Abs(d) < 9e15:
                    return (long)d;
                case IDictionary<string, object> obj:
                    var copy = new Dictionary<string, object>();
                    foreach (var pair in obj) copy[pair.Key] = Whole(pair.Value);
                    return copy;
                case IList<object> list:
                    var items = new List<object>(list.Count);
                    foreach (object item in list) items.Add(Whole(item));
                    return items;
                default:
                    return value;
            }
        }
    }
}
