// The record reader every unitytk part reads its scene records with, and the JSON
// it reads them from: engine-free, in the core assembly, so every importer and
// `dotnet test` read a record the same way (no runtime component reads one: the
// importers fill components by reference). Kept in the global namespace: project scripts call it.
using System;
using System.Collections.Generic;

/// <summary>
/// The records unitytk reads off a model's <c>data_export</c> carrier. Each
/// key is declared here once, beside the channel it rides and its reader --
/// every importer reads its record under that constant, and m3trik's
/// sync_scene_records.py holds each such line to pythontk's SceneRecords and
/// generates the record's typed reader beside the importer it names -- and so
/// is the one way every reader reads a record (<see cref="Read{T}"/>): through
/// its generated shape, at the version that shape declares. unitytk reads the
/// current standard only: a record of another version, or one that fails its
/// declared shape, is refused, never read through a branch for its shape, and
/// a file written before it is re-exported.
/// </summary>
public static class UnitytkRecords
{
    public const string Animation = "animation";             // channel → ShotsImporter
    public const string Audio = "audio";                     // channel → AnimationAudioImporter
    public const string Lightmaps = "lightmaps";             // channel → LightmapsImporter
    public const string Shadows = "shadows";                 // channel → ShadowPlaneImporter
    public const string Lods = "lods";                       // channel → LodsImporter
    public const string RenderEffects = "render_effects";    // channel → RenderEffectsImporter
    public const string Articulation = "articulation";       // channel → ArticulationRecord
    /// <summary>The materials record: two features read it (MaterialsImporter,
    /// RenderEffectsImporter), so its reader sits in the contracts.</summary>
    public const string Materials = "materials";             // channel → MaterialRecord

    /// <summary>
    /// The warning a <paramref name="key"/> record of <paramref name="version"/>
    /// (0: none stated) gets in <paramref name="where"/> from a reader of
    /// <paramref name="supported"/> alone: an older file is re-exported, a
    /// newer one needs a newer unitytk package.
    /// </summary>
    public static string Refusal(string key, int version, int supported, string where)
        => $"'{where}' carries the {key} record v{version}; unitytk reads v{supported} only, so it is not read -- " +
           (version < supported
               ? "re-export the file from a current DCC exporter."
               : "update the com.m3trik.unitytk version in the project's Packages/manifest.json to read it.");

    /// <summary>
    /// The <paramref name="key"/> record <paramref name="json"/> holds, read
    /// through its declared shape by <paramref name="read"/> -- the record's
    /// generated <c>Shape.Payload.Read</c> -- when it is of
    /// <paramref name="current"/>, the version that shape declares (the
    /// record's generated <c>CurrentVersion</c>). Else null, and
    /// <paramref name="refusal"/> says why and what to do: text that is not
    /// JSON, a record of another version (<see cref="Refusal"/>), or one that
    /// fails its declared shape -- nothing of it is read, never half of it.
    /// Empty <paramref name="json"/> is no record: null, and no refusal.
    /// </summary>
    /// <param name="where">The file, for the refusal.</param>
    public static T Read<T>(string key, string json, int current, Func<object, T> read,
                            string where, out string refusal) where T : class
    {
        refusal = null;
        if (string.IsNullOrEmpty(json)) return null;
        object record;
        try
        {
            record = M3trik.Unitytk.Core.Json.Parse(json, false);
        }
        catch (FormatException e)
        {
            refusal = $"'{where}' carries a {key} record that is not JSON ({e.Message}), so it is not read -- " +
                      "re-export the file from a current DCC exporter.";
            return null;
        }
        int version = VersionOf(record);
        if (version != current)
        {
            refusal = Refusal(key, version, current, where);
            return null;
        }
        try
        {
            return read(record);
        }
        catch (Exception e) when (e is FormatException || e is InvalidCastException || e is OverflowException)
        {
            refusal = $"'{where}' carries a {key} record v{version} that fails its declared shape ({e.Message}), " +
                      "so it is not read -- re-export the file from a current DCC exporter.";
            return null;
        }
    }

    /// <summary>
    /// The record <paramref name="key"/> a node's user properties hold: the value
    /// of the property named <paramref name="key"/> (ignoring case), a string that
    /// is not empty. The one rule every record source reads a node by.
    /// </summary>
    public static bool TryGetProperty(string[] names, object[] values, string key, out string json)
    {
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], key, StringComparison.OrdinalIgnoreCase))
            {
                json = values[i] as string;
                return !string.IsNullOrEmpty(json);
            }
        }
        json = null;
        return false;
    }

    /// <summary>
    /// The version <paramref name="record"/> -- parsed by
    /// <see cref="M3trik.Unitytk.Core.Json.Parse(string, bool)"/>, every number a double -- states under <c>version</c>, the key
    /// every record states it under: 0 when it states none or is no object, -1
    /// when it is not a whole number. Neither is a version any reader reads.
    /// </summary>
    public static int VersionOf(object record)
    {
        if (!(record is IDictionary<string, object> fields)
            || !fields.TryGetValue("version", out object stated) || stated == null)
            return 0;
        return stated is double d && d == Math.Floor(d) && Math.Abs(d) <= int.MaxValue ? (int)d : -1;
    }
}
