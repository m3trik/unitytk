// ----------------------------------------------------------------------------
// The record carrier's readers during an import (the carrier feature's editor
// part, M3trik.Unitytk.Carrier.Editor): a data_export node's channels by the
// precedence pythontk reads them by, a model's media folder and the embedded
// files a channel names, and the model importer settings a channel asks for,
// applied after the batch (UnitytkImportConfigurator). Its provider,
// CarrierRecords, serves every importer through them. Every record's key and
// the one way a record is read are the core's (Core/UnitytkRecords.cs).
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The carrier's readers during an import: a <c>data_export</c> node's channels,
/// a model's media folder and its embedded files, and the model importer settings
/// a channel asks for, applied after the batch with one reimport
/// (<see cref="ApplyPendingImportSettings"/>, from <see cref="UnitytkImportConfigurator"/>).
/// The carrier feature's provider (<see cref="M3trik.Unitytk.Carrier.CarrierRecords"/>)
/// serves every importer through them, as the records, media and import-settings
/// contracts; the global name is kept, since verifiers and project scripts name it.
/// </summary>
/// <remarks>
/// Prior art: Unity hands a model's user properties to
/// <c>OnPostprocessGameObjectWithUserProperties</c> and extracts its embedded media
/// with <c>ModelImporter.ExtractTextures</c>, which writes over a same-named file in
/// place; a reimport is legal only from <c>OnPostprocessAllAssets</c>. This keeps the
/// carrier precedence pythontk reads by, and a folder of the model's own so an
/// extraction never takes another file's GUID.
/// </remarks>
public static class CarrierImport
{
    /// <summary>Case-insensitive presence check on an FBX user-property array.</summary>
    public static bool HasProp(string[] names, string attr)
    {
        for (int i = 0; i < names.Length; i++)
            if (string.Equals(names[i], attr, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>The scene's own carrier node: <c>data_export</c>, no namespace, at the
    /// root of the file (<see cref="CarrierFile.IsSceneCarrier"/>).</summary>
    public const string CARRIER = PersistedStrings.Carrier;

    /// <summary>
    /// Keep <paramref name="value"/>, read off <paramref name="carrier"/>, as
    /// <paramref name="assetPath"/>'s channel value in <paramref name="pending"/>
    /// -- by the reader precedence the GLB applier follows (pythontk
    /// <c>_Sidecar._data_export_carrier</c>), so the two deliverables of one
    /// export read the same record. An assembly export carries one
    /// <c>NS:data_export</c> per referenced module (or a grouped module's own-named
    /// <c>data_export</c> under its group) beside the scene's own, in whatever
    /// order the writer put them: the scene's own wins
    /// (<see cref="CarrierFile.IsSceneCarrier"/>: the root-level one, a top-level
    /// child of the model) -- the first of them, as pythontk keeps it
    /// (<c>FbxMedia._applied_channels</c>) and <see cref="CarrierFile"/> -- and a
    /// module's is kept only where the scene's carries no such channel, the first
    /// met. Unity hands the nodes over before it parents the model's hierarchy, so
    /// an own-named node is the scene's only where the file's root carrier holds
    /// <paramref name="value"/> too (<see cref="CarrierFile.AtRoot"/>, which reads
    /// the file's hierarchy). A node that is no carrier (<see cref="CarrierFile.IsCarrier"/>:
    /// an artist's custom attribute of a record's name) is never read, on any channel.
    /// </summary>
    public static void Collect(
        Dictionary<string, string> pending, string assetPath, GameObject carrier, string value)
    {
        if (carrier == null || !CarrierFile.IsCarrier(carrier.name)) return;
        var parent = carrier.transform.parent;
        bool own = CarrierFile.IsSceneCarrier(carrier.name, parent == null || parent.parent == null)
                   && CarrierFile.AtRoot(assetPath, value);
        if (pending.TryGetValue(assetPath, out var held)
            && (!own || (held != null && _fromOwn.TryGetValue(held, out _))))
            return;  // held already: a module's yields to the scene's own, the first own stands
        pending[assetPath] = value;
        if (own && value != null && !_fromOwn.TryGetValue(value, out _))
            _fromOwn.Add(value, _fromOwn);
    }

    // The values Collect kept from the scene's own carrier, by the string itself:
    // whether the one a channel holds came from it is what decides whether a later
    // own carrier may replace it -- every importer resets its pending value per
    // import, so nothing of an earlier import is consulted. Weak, so a value an
    // importer drops is never held here.
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<string, object> _fromOwn =
        new System.Runtime.CompilerServices.ConditionalWeakTable<string, object>();

    /// <summary>Case-insensitive string read from an FBX user-property array.</summary>
    public static bool TryGetProp(
        string[] names, object[] values, string attr, out string value)
        => UnitytkRecords.TryGetProperty(names, values, attr, out value);

    // ---- Per-asset import-settings scoping -------------------------------
    //
    // Channel DETECTION needs no importer settings: FBX user properties reach
    // OnPostprocessGameObjectWithUserProperties even with Unity's defaults
    // (verified against a live batch 6000.3). So unitytk never touches a model
    // importer up front; a channel importer that discovers it needs a setting
    // REQUESTS it here for that one asset, and the pending set is applied
    // (with a single reimport of just that asset) after the import batch.
    // Unrelated FBX are never modified, and nothing is ever switched OFF —
    // the user's own import settings are left exactly as they had them.

    static readonly HashSet<string> _needSparseCurves = new HashSet<string>();
    static readonly HashSet<string> _needExtractedTextures = new HashSet<string>();
    static bool _applying;

    /// <summary>This asset needs sparse (unresampled) curves — the opacity
    /// fade windows live in the gaps between keys.</summary>
    public static void RequestSparseCurves(string assetPath)
        => _needSparseCurves.Add(assetPath);

    /// <summary>This asset references media a channel needs that is not in
    /// the project. Unity holds an FBX's EMBEDDED media only once extracted
    /// -- never as sub-assets, and stock import binds none (measured: Built-in
    /// import 2026-08-25 / 2026-09-02; 2022.3 and 6000.3, 2026-10-05) -- so
    /// the batch end extracts them beside the model and reimports it once;
    /// the by-name lookup then finds the loose file. ExtractTextures reports
    /// success for an FBX that embeds nothing too (measured), so the reimport
    /// waits on the folder actually changing: a missing loose PNG can't loop.
    /// A channel importer normally asks through
    /// <see cref="RequestEmbeddedFiles"/>.</summary>
    public static void RequestTextureExtraction(string assetPath)
        => _needExtractedTextures.Add(assetPath);

    // Model versions (path, write time, size) this PROJECT already asked to
    // have extracted, each with the files its extraction produced: a name the
    // FBX does not carry stays missing after the extraction, and must not ask
    // again on every reimport. Kept in Library/ (per project, never versioned)
    // rather than for one editor session: a later session's reimport (a
    // package update, Reimport All) asked again, and each extraction overwrote
    // the files beside the model, a touched-up one included. One line per
    // model, its latest version, then a tab before each file its extraction
    // wrote (a tab alone: it wrote none). A version asks once more only when a
    // file it wrote is gone -- a sample folder re-imported whole takes the
    // files and keeps the model's bytes (measured 2026-11-07) -- or when the
    // line names no files yet (its extraction has not run). A Library rebuild
    // forgets it and extracts once more.
    const string EMBEDDED_REQUESTED = PersistedStrings.EmbeddedRequestedMemo;
    // Version key -> the files its extraction wrote; null while none are recorded.
    static Dictionary<string, string[]> _embeddedRequested;

    static Dictionary<string, string[]> Requested()
    {
        if (_embeddedRequested != null) return _embeddedRequested;
        _embeddedRequested = new Dictionary<string, string[]>(StringComparer.Ordinal);
        try
        {
            if (File.Exists(EMBEDDED_REQUESTED))
                foreach (var line in File.ReadAllLines(EMBEDDED_REQUESTED))
                {
                    if (line.Length == 0) continue;
                    string[] parts = line.Split('\t');
                    var wrote = new List<string>();
                    for (int i = 1; i < parts.Length; i++)
                        if (parts[i].Length > 0) wrote.Add(parts[i]);
                    _embeddedRequested[parts[0]] = parts.Length == 1 ? null : wrote.ToArray();
                }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return _embeddedRequested;
    }

    static void SaveRequested()
    {
        var lines = new List<string>();
        foreach (var pair in Requested())
            lines.Add(pair.Value == null ? pair.Key : pair.Key + "\t" + string.Join("\t", pair.Value));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EMBEDDED_REQUESTED));
            File.WriteAllLines(EMBEDDED_REQUESTED, lines);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Remember that <paramref name="assetPath"/>'s version
    /// <paramref name="key"/> asked; false when it already had and every file
    /// its extraction wrote is still there.</summary>
    static bool RememberRequest(string assetPath, string key)
    {
        var requested = Requested();
        if (requested.TryGetValue(key, out var wrote) && wrote != null
            && Array.TrueForAll(wrote, File.Exists))
            return false;
        string prefix = assetPath + "|";
        foreach (var old in new List<string>(requested.Keys))
            if (old.StartsWith(prefix, StringComparison.Ordinal)) requested.Remove(old);
        requested[key] = null;
        SaveRequested();
        return true;
    }

    /// <summary>Record the files the extraction of the model at
    /// <paramref name="assetPath"/> wrote, on its version's line.</summary>
    static void RememberWritten(string assetPath, IEnumerable<string> written)
    {
        string prefix = assetPath + "|";
        var requested = Requested();
        foreach (var key in new List<string>(requested.Keys))
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            requested[key] = new List<string>(written).ToArray();
            SaveRequested();
            return;
        }
    }

    // The files this import's channels named (RequestEmbeddedFiles), per model:
    // what its import depends on, there yet or not (AskedPaths).
    static readonly Dictionary<string, List<string>> _named =
        new Dictionary<string, List<string>>(StringComparer.Ordinal);

    // The model whose extraction runs now: the imports it brings on its way
    // (a file landing is a dependency changing) never ask again meanwhile.
    static string _extracting;

    /// <summary>Forget what an earlier import of the model at <paramref name="assetPath"/> named: its import starts.</summary>
    public static void BeginImport(string assetPath) => _named.Remove(assetPath);

    /// <summary>
    /// Each path where a file this import of the model at
    /// <paramref name="assetPath"/> named lands or is looked for -- each of its
    /// <see cref="MediaFolders"/> -- whether it is there yet or not; the list
    /// forgotten. The configurator makes the import depend on each
    /// (<see cref="UnitytkImportConfigurator"/>), so the import that asked for
    /// the files depends on them as the one that binds them does: Unity reuses
    /// a cached import whose dependencies still match (measured 2026-11-07),
    /// and an import that depended on none of them came back from the cache --
    /// its set empty, no import run -- whenever one the next import bound
    /// changed (its import settings), across editor sessions alike. A name
    /// with no extension (a stem) has no one path, and is left out.
    /// </summary>
    public static List<string> AskedPaths(string assetPath)
    {
        var paths = new List<string>();
        if (!_named.TryGetValue(assetPath, out var names)) return paths;
        _named.Remove(assetPath);
        foreach (var dir in MediaFolders(assetPath))
            foreach (var name in names)
                if (Path.HasExtension(name))
                    paths.Add((dir.Length > 0 ? dir + "/" : "") + Path.GetFileName(name));
        return paths;
    }

    /// <summary>
    /// The models whose extraction wrote one of <paramref name="deleted"/>
    /// (asset paths) and whose import has not asked for them again since,
    /// reimported: one Unity took from its cache as it stands (a sample folder
    /// re-imported whole while no editor ran, the model's bytes unchanged:
    /// measured 2026-11-07) runs no importer to ask. Their import asks once
    /// more (<see cref="RememberRequest"/>). Called from
    /// <see cref="UnitytkImportConfigurator"/>, where a reimport is legal.
    /// </summary>
    public static void ReimportModelsMissing(string[] deleted)
    {
        if (deleted == null || deleted.Length == 0) return;
        var gone = new HashSet<string>(deleted, StringComparer.OrdinalIgnoreCase);
        var models = new List<string>();
        foreach (var pair in Requested())
        {
            string model = pair.Key.Substring(0, pair.Key.IndexOf('|'));
            // A line that names no files has asked again already (or never extracted).
            if (pair.Value != null && Array.Exists(pair.Value, gone.Contains) && File.Exists(model) && !models.Contains(model))
                models.Add(model);
        }
        foreach (var model in models)
        {
            ImportLog.Info(model, "a file its extraction wrote was deleted -- reimporting it, which extracts it again.");
            AssetDatabase.ImportAsset(model, ImportAssetOptions.ForceUpdate);
        }
    }

    /// <summary>
    /// The channels of the model at <paramref name="assetPath"/> name these
    /// files, which its FBX embeds (the DCC Scene Exporter puts every file a
    /// record names inside: pythontk FbxMedia.embed_dependencies). Ask for
    /// them to be extracted into the model's media folder -- beside it, unless
    /// it shares its folder (<see cref="MediaFolder"/>; <see cref="RequestTextureExtraction"/>)
    /// -- when the model does not hold one: <paramref name="holds"/> says, for
    /// an importer with a rule of its own (the audio importer's content
    /// digests), else <see cref="HoldsCurrent"/> -- its own copy there, no
    /// older than the model; an older one is a re-export's. Where the files
    /// land is the extraction's to decide, from the names that land
    /// (<see cref="ApplyPendingImportSettings"/>): never over a file the
    /// model did not extract. A name with no extension matches the file of
    /// that stem (an audio cue's sound names its file, extension included).
    /// Once per version of the model (and media folder) per project, and once
    /// more whenever a file its extraction wrote is gone; true when this call asked.
    /// </summary>
    public static bool RequestEmbeddedFiles(
        string assetPath, IEnumerable<string> names, Func<string, bool> holds = null)
    {
        var model = new FileInfo(assetPath);
        if (!model.Exists) return false;
        var asked = new List<string>();
        foreach (var name in names)
            if (!string.IsNullOrEmpty(name)) asked.Add(name);
        if (!_named.TryGetValue(assetPath, out var named)) _named[assetPath] = named = new List<string>();
        foreach (var name in asked)
            if (!named.Contains(name)) named.Add(name);
        string media = MediaFolder(assetPath);
        holds = holds ?? (n => HoldsCurrent(assetPath, n));
        bool stale = false;
        foreach (var name in asked)
        {
            if (holds(name)) continue;
            stale = true;
            break;
        }
        // Its extraction is under way: the files land one by one.
        if (stale && _extracting == assetPath) return true;
        // A media folder apart (the model shares its folder, or has one of its
        // own) is part of the key: a model that comes to share its folder asks
        // once more for the same version, into its new folder -- the version
        // alone answered "asked" and its media stayed where a neighbour's could
        // be bound. A model alone keeps the key it always had.
        string folder = FolderOf(assetPath);
        string key = assetPath + "|" + model.LastWriteTimeUtc.Ticks + "|" + model.Length
                     + (media == folder ? "" : "|" + media);
        if (!stale || !RememberRequest(assetPath, key))
            return false;
        RequestTextureExtraction(assetPath);
        return true;
    }

    /// <summary>
    /// True when the media folder of the model at <paramref name="assetPath"/>
    /// (<see cref="MediaFolder"/>) holds its own copy (<see cref="IsOwnMedia"/>)
    /// of the file <paramref name="name"/> -- by stem when the name has no
    /// extension, any of that stem -- no older than the model: a copy an older
    /// version of it extracted is a re-export's, replaced. A same-named file
    /// it did not extract -- a library's, newer than it or not -- is not its
    /// own: it is asked for, and lands in its folder of its own.
    /// </summary>
    public static bool HoldsCurrent(string assetPath, string name)
    {
        string folder = MediaFolder(assetPath);
        var model = File.GetLastWriteTimeUtc(assetPath);
        foreach (var copy in Copies(folder, Path.GetFileName(name)))
            if (copy.LastWriteTimeUtc >= model && IsOwnMedia(assetPath, folder + "/" + copy.Name))
                return true;
        return false;
    }

    /// <summary>The suffix of a model's own media folder (<see cref="MediaFolder"/>).</summary>
    public const string MEDIA_FOLDER_SUFFIX = PersistedStrings.MediaFolderSuffix;

    /// <summary>
    /// Where the embedded media of the model at <paramref name="assetPath"/> are
    /// extracted: beside it when it is the only FBX in its folder, else in a
    /// folder of its own beside it, <c>&lt;model&gt;_Media</c>, and there once
    /// it has one. Records name their files by file name, and two deliverables
    /// in one folder can name different files alike -- a standard and a moody
    /// bake of one room both embed <c>Room_Lightmap.exr</c> -- so beside them
    /// the second bound the first's, its walls sampling another bake's cells
    /// (2026-10-19). A model that comes to share its folder moves its media on
    /// its next import; so does one whose media would land beside it on a
    /// file it did not extract (<see cref="ApplyPendingImportSettings"/>).
    /// </summary>
    public static string MediaFolder(string assetPath)
    {
        string own = OwnMediaFolder(assetPath);
        return SharesFolder(assetPath) || Directory.Exists(own) ? own : FolderOf(assetPath);
    }

    /// <summary>The model's folder of its own, <c>&lt;model&gt;_Media</c>, beside it.</summary>
    static string OwnMediaFolder(string assetPath)
        => FolderOf(assetPath) + "/" + Path.GetFileNameWithoutExtension(assetPath) + MEDIA_FOLDER_SUFFIX;

    static string FolderOf(string assetPath)
        => (Path.GetDirectoryName(assetPath) ?? "").Replace('\\', '/');

    // A file beside a model is its own only when it put the file there: each
    // file it extracts beside itself is marked so in its importer's userData --
    // the .meta, versioned with the project, so every checkout and a rebuilt
    // Library agree, and a move keeps it -- by the model's GUID. A file
    // extracted before 2026-10-31 carries no mark, so the first extraction
    // that lands on one goes to the model's folder of its own instead.
    const string EXTRACTED_BY = PersistedStrings.ExtractedByMark;

    static string ExtractedMark(string modelAssetPath)
        => EXTRACTED_BY + AssetDatabase.AssetPathToGUID(modelAssetPath);

    /// <summary>
    /// True when the file at <paramref name="fileAssetPath"/> is the model at
    /// <paramref name="modelAssetPath"/>'s own media: in its folder of its own
    /// (<c>&lt;model&gt;_Media</c>), or beside it and extracted there by it --
    /// not another model's file or a library's that only shares its name.
    /// </summary>
    public static bool IsOwnMedia(string modelAssetPath, string fileAssetPath)
    {
        string dir = FolderOf(fileAssetPath);
        if (dir == OwnMediaFolder(modelAssetPath)) return true;
        if (dir != FolderOf(modelAssetPath)) return false;
        var importer = AssetImporter.GetAtPath(fileAssetPath);
        return importer != null && importer.userData == ExtractedMark(modelAssetPath);
    }

    /// <summary>
    /// True when an extraction may rewrite the file at
    /// <paramref name="fileAssetPath"/>: one a model extracted beside itself
    /// (marked), or one in a model's folder of its own. Its model's re-export
    /// writes it over in place, and its folder of its own can go -- so a
    /// same-named file a model reuses is never another's extraction.
    /// </summary>
    public static bool IsExtractedMedia(string fileAssetPath)
    {
        var importer = AssetImporter.GetAtPath(fileAssetPath);
        if (importer != null && importer.userData != null
            && importer.userData.StartsWith(EXTRACTED_BY, StringComparison.Ordinal))
            return true;
        string dir = FolderOf(fileAssetPath);
        string folder = Path.GetFileName(dir);
        if (!folder.EndsWith(MEDIA_FOLDER_SUFFIX, StringComparison.Ordinal)) return false;
        string stem = folder.Substring(0, folder.Length - MEDIA_FOLDER_SUFFIX.Length);
        string parent = FolderOf(dir);
        if (!Directory.Exists(parent)) return false;
        foreach (var path in Directory.GetFiles(parent))
            if (Imports.IsDelivery(path)
                && string.Equals(Path.GetFileNameWithoutExtension(path), stem, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>True when <paramref name="fileAssetPath"/> lies in the model at
    /// <paramref name="modelAssetPath"/>'s folder of its own,
    /// <c>&lt;model&gt;_Media</c> -- where every extraction first imports its
    /// files (<see cref="ExtractBeside"/>), so a channel that sets a file's
    /// first-import settings by name finds the model's own there, and a move
    /// beside the model keeps them (the .meta goes with it). Not the folder the
    /// model sits in: a same-named file dropped there is not the model's.</summary>
    public static bool InOwnMediaFolder(string modelAssetPath, string fileAssetPath)
        => string.Equals(FolderOf(fileAssetPath), OwnMediaFolder(modelAssetPath),
                         StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when the model at <paramref name="modelAssetPath"/> may bind the
    /// file at <paramref name="fileAssetPath"/>, a same-named one found by
    /// name: its own (<see cref="IsOwnMedia"/>), or one nothing will rewrite or
    /// take away -- never another model's extraction
    /// (<see cref="IsExtractedMedia"/>: its re-export overwrites it in place,
    /// or its folder of its own goes), nor an unmarked file lying beside
    /// another FBX: one an older release extracted there, which reads as no
    /// model's and is deleted as that model's leftover.
    /// </summary>
    public static bool IsReusableBy(string modelAssetPath, string fileAssetPath)
    {
        if (IsOwnMedia(modelAssetPath, fileAssetPath)) return true;
        if (IsExtractedMedia(fileAssetPath)) return false;
        string dir = FolderOf(fileAssetPath);
        if (!Directory.Exists(dir)) return true;
        foreach (var path in Directory.GetFiles(dir))
            if (Imports.IsDelivery(path)
                && !string.Equals(path.Replace('\\', '/'), modelAssetPath, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    /// <summary>The asset paths of the model at <paramref name="assetPath"/>'s
    /// own copies (<see cref="IsOwnMedia"/>) of the file <paramref name="name"/>
    /// in its media folder (<see cref="MediaFolder"/>) -- every one of that stem
    /// when the name has no extension.</summary>
    public static IEnumerable<string> OwnMediaCopies(string assetPath, string name)
    {
        string folder = MediaFolder(assetPath);
        foreach (var copy in Copies(folder, Path.GetFileName(name)))
        {
            string path = folder + "/" + copy.Name;
            if (IsOwnMedia(assetPath, path)) yield return path;
        }
    }


    /// <summary>
    /// The folders a model's files are looked for in, nearest first: its media
    /// folder (<see cref="MediaFolder"/>), then the folder it sits in -- where a
    /// map copied in by hand, or extracted before the model shared its folder,
    /// still lies.
    /// </summary>
    public static string[] MediaFolders(string assetPath)
    {
        string media = MediaFolder(assetPath);
        string folder = (Path.GetDirectoryName(assetPath) ?? "").Replace('\\', '/');
        return media == folder ? new[] { folder } : new[] { media, folder };
    }

    /// <summary>True when another FBX sits in the folder of <paramref name="assetPath"/>.</summary>
    static bool SharesFolder(string assetPath)
    {
        string folder = Path.GetDirectoryName(assetPath);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return false;
        int models = 0;
        foreach (var path in Directory.GetFiles(folder))
            if (Imports.IsDelivery(path) && ++models > 1)
                return true;
        return false;
    }

    /// <summary>Every file in <paramref name="folder"/> named <paramref name="name"/>
    /// -- every one of that stem when the name has no extension.</summary>
    static IEnumerable<FileInfo> Copies(string folder, string name)
    {
        if (!Directory.Exists(folder)) yield break;
        bool stem = !Path.HasExtension(name);
        foreach (var path in Directory.GetFiles(folder))
        {
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            string compared = stem ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);
            if (string.Equals(compared, name, StringComparison.OrdinalIgnoreCase))
                yield return new FileInfo(path);
        }
    }

    /// <summary>The asset path of the file in <paramref name="folder"/> whose
    /// name is <paramref name="fileName"/>, ignoring case as the file systems
    /// Unity extracts onto do; null when there is none.</summary>
    static string FileIn(string folder, string fileName)
    {
        foreach (var copy in Copies(folder, fileName))
            if (Path.HasExtension(fileName) || !Path.HasExtension(copy.Name))
                return folder + "/" + copy.Name;
        return null;
    }

    /// <summary>Every file in <paramref name="folder"/> but the .meta files, by
    /// asset path, with its size and write time: what an extraction wrote.</summary>
    static Dictionary<string, string> FolderState(string folder)
    {
        var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.GetFiles(folder))
        {
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            var info = new FileInfo(path);
            state[path.Replace('\\', '/')] = info.Length + ":" + info.LastWriteTimeUtc.Ticks;
        }
        return state;
    }

    /// <summary>
    /// Extract the embedded media of the model at <paramref name="assetPath"/>
    /// -- the only FBX in its folder, with no folder of its own -- beside it,
    /// never over a file it did not extract. Unity's extraction writes every
    /// embedded file, a material's own maps too (which no record names), in
    /// place, keeping the GUID of a file already there: another model's
    /// door.wav or a library's wood.png would take the model's bytes for
    /// everything bound to it. So the files land first in a folder of the
    /// model's own, <c>&lt;model&gt;_Media</c>, where they are imported (the
    /// first-import settings a channel gives a file are set there:
    /// <see cref="InOwnMediaFolder"/>), and the exact names that landed decide.
    /// One that names a file beside the model it did not extract
    /// (<see cref="IsOwnMedia"/>) keeps the folder, and the model's media in it.
    /// Else each moves beside the model: a new one, marked its own (its import
    /// settings move with it); one it extracted before takes the new bytes in
    /// place, its GUID and settings kept. The folder goes. Returns what was
    /// done, for the log; null when nothing changed. Each file it leaves the
    /// model is added to <paramref name="wrote"/>.
    /// </summary>
    static string ExtractBeside(ModelImporter mi, string assetPath, List<string> wrote)
    {
        string beside = FolderOf(assetPath);
        string staging = OwnMediaFolder(assetPath);
        AssetDatabase.CreateFolder(beside, Path.GetFileName(staging));
        if (!mi.ExtractTextures(staging) || FolderState(staging).Count == 0)
        {
            AssetDatabase.DeleteAsset(staging);  // it embeds nothing: no folder of its own
            return null;
        }
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        var landed = new List<string>(FolderState(staging).Keys);
        foreach (var path in landed)
        {
            string there = FileIn(beside, Path.GetFileName(path));
            if (there != null && !IsOwnMedia(assetPath, there))
            {
                wrote.AddRange(landed);
                return $"its embedded media extracted into '{staging}' ('{Path.GetFileName(there)}' "
                       + "beside it is not its own)";
            }
        }
        // File by file: a file that cannot be written stays in the folder of
        // its own, said, and the rest go on -- and every moved file is marked
        // however the loop ends, so a failure never leaves one beside the
        // model that reads as no model's.
        bool changed = false;
        // One refresh for every file that lands, and one for their marks: each
        // is a file the model's import depends on (AskedPaths), so landing them
        // one refresh at a time reimported the model once a file.
        var moved = new List<string>();
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var path in landed)
            {
                string name = Path.GetFileName(path);
                try
                {
                    string there = FileIn(beside, name);
                    if (there != null)
                    {
                        if (!SameBytes(path, there))
                        {
                            File.Copy(path, there, true);
                            AssetDatabase.ImportAsset(there, ImportAssetOptions.ForceUpdate);
                            changed = true;
                        }
                        AssetDatabase.DeleteAsset(path);
                        wrote.Add(there);
                        continue;
                    }
                    string target = beside + "/" + name;
                    string error = AssetDatabase.MoveAsset(path, target);
                    if (!string.IsNullOrEmpty(error))
                        throw new IOException(error);
                    moved.Add(target);
                    wrote.Add(target);
                    changed = true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    ImportLog.Warning(assetPath, $"'{name}' stays in '{staging}' -- {e.Message}");
                    wrote.Add(path);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            Mark(assetPath, moved);  // whatever stops the loop: no moved file reads as no model's
        }
        if (FolderState(staging).Count == 0)
            AssetDatabase.DeleteAsset(staging);
        return changed ? "its embedded media extracted beside it" : null;
    }

    /// <summary>Mark each of <paramref name="files"/> -- just extracted beside the model
    /// at <paramref name="modelAssetPath"/> -- as its own (<see cref="IsOwnMedia"/>):
    /// each mark written to its .meta, then all reimported in one refresh.</summary>
    static void Mark(string modelAssetPath, List<string> files)
    {
        string mark = ExtractedMark(modelAssetPath);
        var marked = new List<string>();
        foreach (var file in files)
        {
            var importer = AssetImporter.GetAtPath(file);
            if (importer == null || importer.userData == mark) continue;
            importer.userData = mark;
            EditorUtility.SetDirty(importer);
            AssetDatabase.WriteImportSettingsIfDirty(file);
            marked.Add(file);
        }
        if (marked.Count == 0) return;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var file in marked) AssetDatabase.ImportAsset(file);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    /// <summary>True when the files at <paramref name="a"/> and
    /// <paramref name="b"/> hold the same bytes: their lengths, then their
    /// SHA-256 read in streams -- never both whole in memory (a 4K EXR).</summary>
    static bool SameBytes(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            byte[] x, y;
            using (var stream = fa.OpenRead()) x = sha.ComputeHash(stream);
            using (var stream = fb.OpenRead()) y = sha.ComputeHash(stream);
            for (int i = 0; i < x.Length; i++)
                if (x[i] != y[i]) return false;
        }
        return true;
    }

    /// <summary>The files of <paramref name="after"/> that <paramref name="before"/>
    /// lacked, or held otherwise: what was written between them.</summary>
    static List<string> Written(Dictionary<string, string> before, Dictionary<string, string> after)
    {
        var written = new List<string>();
        foreach (var pair in after)
            if (!before.TryGetValue(pair.Key, out var was) || was != pair.Value)
                written.Add(pair.Key);
        return written;
    }


    /// <summary>True while *importer*'s current settings can't yet satisfy the
    /// asset's requested channels — i.e. this pass is detection-only and a
    /// configured reimport is coming. Curve-rewriting importers skip their
    /// work on such a pass rather than transform resampled data.</summary>
    public static bool IsDetectionPass(string assetPath, AssetImporter importer)
    {
        var mi = importer as ModelImporter;
        if (mi == null) return false;
        if (_needSparseCurves.Contains(assetPath) && mi.resampleCurves)
            return true;
        return false;
    }

    /// <summary>Apply every pending request; called from
    /// <c>UnitytkImportConfigurator.OnPostprocessAllAssets</c> (after the
    /// import batch, when reimporting is legal). Reimports only assets whose
    /// settings actually changed, so a configured asset never loops.</summary>
    public static void ApplyPendingImportSettings()
    {
        if (_needSparseCurves.Count == 0 && _needExtractedTextures.Count == 0)
            return;

        // SaveAndReimport below re-enters this method via the nested import's
        // own OnPostprocessAllAssets (whose detection re-requests the asset —
        // harmless: its settings now satisfy, so the entry purges next time).
        // The guard keeps the loop flat instead of one nesting level per
        // pending asset.
        if (_applying) return;
        _applying = true;
        try
        {
            ApplyPending();
        }
        finally
        {
            _applying = false;
        }
    }

    static void ApplyPending()
    {
        var paths = new HashSet<string>(_needSparseCurves);
        paths.UnionWith(_needExtractedTextures);
        // One asset's failure (a file locked mid-extraction) is said and the
        // next goes on: an exception here stranded every asset after it.
        foreach (var assetPath in paths)
        {
            try
            {
                ApplyPendingTo(assetPath);
            }
            catch (Exception e)
            {
                ImportLog.Warning(assetPath, "its pending import settings were not "
                                             + $"applied -- {e.GetType().Name}: {e.Message}");
            }
        }
    }

    static void ApplyPendingTo(string assetPath)
    {
        bool wantSparse = _needSparseCurves.Remove(assetPath);
        bool wantTextures = _needExtractedTextures.Remove(assetPath);
        var mi = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (mi == null) return;

        var enabled = new List<string>();
        if (wantTextures)
        {
            // Into the model's media folder (beside it, unless it shares
            // its folder or has one of its own: MediaFolder), so each
            // importer's by-name search finds the file on the
            // reimport this triggers -- beside it through a folder of its
            // own, so nothing it did not extract is written over
            // (ExtractBeside). The files are imported before the model:
            // reimported first, it would miss them by name and request a
            // second round (measured). ExtractTextures returns true for an
            // FBX that embeds nothing as well (measured), so the folder
            // itself says whether there is anything to redo.
            string folder = MediaFolder(assetPath);
            string done;
            var wrote = new List<string>();
            _extracting = assetPath;
            try
            {
                if (folder == FolderOf(assetPath))
                    done = ExtractBeside(mi, assetPath, wrote);
                else
                {
                    bool created = !AssetDatabase.IsValidFolder(folder);
                    if (created)
                        AssetDatabase.CreateFolder(FolderOf(folder), Path.GetFileName(folder));
                    var before = FolderState(folder);
                    bool extracted = mi.ExtractTextures(folder);
                    if (extracted)
                        wrote.AddRange(Written(before, FolderState(folder)));
                    extracted &= wrote.Count > 0;
                    if (created && !extracted)
                        AssetDatabase.DeleteAsset(folder);  // it embeds nothing: no folder of its own
                    if (extracted)
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    done = extracted
                        ? $"its embedded media extracted into '{folder}' (its folder of its own)"
                        : null;
                }
                // Before the reimport: the import that follows reads the memo.
                RememberWritten(assetPath, wrote);
            }
            finally
            {
                _extracting = null;
            }
            if (done != null) enabled.Add(done);
        }
        if (wantSparse && mi.resampleCurves)
        {
            mi.resampleCurves = false;
            enabled.Add("unresampled curves (Resample Curves off)");
        }
        if (enabled.Count == 0) return;

        ImportLog.Info(assetPath, string.Join(" + ", enabled) + " -- this asset only; reimporting it once.");
        mi.SaveAndReimport();
    }

    /// <summary>
    /// The <paramref name="key"/> record <paramref name="json"/> holds, read the
    /// one way a record is read (<see cref="UnitytkRecords.Read{T}"/>): through
    /// its generated shape's <paramref name="read"/>, at <paramref name="current"/>
    /// alone. Else null, with the refusal (which names the file) warned in the
    /// import log under <paramref name="tag"/> -- and the importer reads nothing
    /// of the record.
    /// </summary>
    public static T Read<T>(string key, string json, int current, Func<object, T> read,
                            string tag, string assetPath) where T : class
    {
        var record = UnitytkRecords.Read(key, json, current, read, assetPath, out string refusal);
        if (refusal != null) ImportLog.Warning(null, $"[{tag}] {refusal}");
        return record;
    }
}
