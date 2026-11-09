// ----------------------------------------------------------------------------
// Part of the unitytk core's contracts (M3trik.Unitytk.Contracts): a record two
// features read -- the Materials importer writes its materials, and the Render
// Effects importer joins its material entries by its rules -- and a feature
// compiles against the core alone. Global names kept: project scripts
// call them.
//
// The delivery's materials record (data_export.materials) is literal glTF 2.0:
// a glTF document holding only materials, textures, images and samplers (with
// glTF's asset and extensionsUsed, and the record's own version), each
// material's name the FBX material it describes, each image named by the file
// the delivery carries. Read off the file itself at import (the carrier's
// IRecordSource.Record), at CurrentVersion alone: a record of version 1 (the
// flat shape) is refused, and the cure is a re-export. A material it does not
// name, or a file without one, keeps Unity's own import.
//
// The record's types are generated beside this file (MaterialRecord.g.cs, from
// pythontk's declaration, with CurrentVersion, the one version read); this half
// is the reader's policy: the entries by name, and a map's image by file name.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;

/// <summary>
/// The <c>materials</c> record: its generated shape (<c>MaterialRecord.g.cs</c>) and
/// this reader's policy -- the entries by name, a map's image by file name.
/// </summary>
/// <remarks>
/// Prior art: glTF 2.0's own material model and its ratified extensions, adopted as
/// they stand: the record IS a glTF document of materials, textures, images and
/// samplers. glTFast's and UnityGLTF's glTF readers were weighed for reading it: each
/// parses a whole glTF asset into its own scene objects and is a package dependency,
/// where the record is a few objects of a shape declared once, so it is read by its
/// generated reader as every record is (<see cref="UnitytkRecords.Read{T}"/>).
/// </remarks>
public static partial class MaterialRecord
{
    /// <summary>
    /// The record <paramref name="json"/> holds, read the one way a record is read
    /// (<see cref="UnitytkRecords.Read{T}"/>: its generated shape, at
    /// <see cref="CurrentVersion"/> alone, under its carrier key
    /// <see cref="UnitytkRecords.Materials"/>); null when <paramref name="json"/> is
    /// empty or the record is refused, with why in <paramref name="refusal"/>. What a
    /// caller holding the record's text calls; an importer reads through its records
    /// provider.
    /// </summary>
    /// <param name="where">The file, for the refusal.</param>
    public static Shape.Payload Read(string json, string where, out string refusal)
        => UnitytkRecords.Read(UnitytkRecords.Materials, json, CurrentVersion, Shape.Payload.Read, where, out refusal);

    /// <summary>
    /// The materials of <paramref name="payload"/>, a record already read through its
    /// shape, by name -- the first of a repeated name; null for null (no record, or
    /// one refused).
    /// </summary>
    public static Dictionary<string, Shape.Entry> Entries(Shape.Payload payload)
    {
        if (payload == null) return null;
        var entries = new Dictionary<string, Shape.Entry>(StringComparer.Ordinal);
        foreach (var entry in payload.materials)
            if (entry != null && !string.IsNullOrEmpty(entry.name) && !entries.ContainsKey(entry.name))
                entries[entry.name] = entry;
        return entries;
    }

    /// <summary>
    /// What <paramref name="entries"/> holds for the material named
    /// <paramref name="name"/>, or null: by its name in the file; else as Unity's
    /// model import names a material (<see cref="ImportedName"/>: 'ns:Mat' imports as
    /// 'ns_Mat'); else -- as the record states, for an import that drops a namespace --
    /// by the name after the last ':'. Each rule on both sides, so the keys may be the
    /// record's names or the imported materials' (one join, whichever side a caller
    /// holds by name); a rule two keys meet joins neither (no guess).
    /// </summary>
    public static T Find<T>(Dictionary<string, T> entries, string name) where T : class
    {
        if (entries == null || string.IsNullOrEmpty(name)) return null;
        if (entries.TryGetValue(name, out var exact)) return exact;
        foreach (Func<string, string> rule in new Func<string, string>[] { ImportedName, M3trik.Unitytk.Core.RecordNames.Leaf })
        {
            string wanted = rule(name);
            T found = null;
            foreach (var pair in entries)
            {
                if (!string.Equals(rule(pair.Key), wanted, StringComparison.Ordinal)) continue;
                if (found != null) return null;  // two modules' materials share it: no guess
                found = pair.Value;
            }
            if (found != null) return found;
        }
        return null;
    }

    // The characters Unity's model import writes as '_' in a material's name (measured,
    // Unity 6000.3 on Windows: each one a Windows file name refuses; '.', '-', '#', '&',
    // '(', a space and a non-ASCII letter are kept).
    static readonly char[] Refused = { '\\', '/', ':', '*', '?', '<', '>', '|' };

    /// <summary>
    /// <paramref name="name"/> as Unity's model import names the material a file names
    /// so: each of <c>\ / : * ? &lt; &gt; |</c> as '_' (measured, Unity 6000.3: the
    /// material and its description alike; a node keeps every character).
    /// </summary>
    public static string ImportedName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.IndexOfAny(Refused) < 0) return name;
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (Array.IndexOf(Refused, chars[i]) >= 0) chars[i] = '_';
        return new string(chars);
    }

    /// <summary>
    /// The file name of the image the texture <paramref name="texture"/> (an index
    /// into <paramref name="payload"/>'s <c>textures</c>, as a material's texture info
    /// names it) reads, or null: no texture, or an index or source the document does
    /// not hold. A <c>uri</c> holding a folder is read for its file name alone.
    /// </summary>
    public static string ImageOf(Shape.Payload payload, int? texture)
    {
        if (payload == null || texture == null || payload.textures == null) return null;
        int t = texture.Value;
        if (t < 0 || t >= payload.textures.Count || payload.textures[t] == null) return null;
        int? source = payload.textures[t].source;
        if (source == null || payload.images == null || source.Value < 0 || source.Value >= payload.images.Count) return null;
        string uri = payload.images[source.Value]?.uri;
        if (string.IsNullOrEmpty(uri)) return null;
        uri = Uri.UnescapeDataString(uri).Replace('\\', '/');
        return uri.Substring(uri.LastIndexOf('/') + 1);
    }

    /// <summary>
    /// The file names of the images <paramref name="payload"/>'s materials read, each
    /// once, in the order they first appear; empty for null (no record, or one refused).
    /// </summary>
    public static List<string> Images(Shape.Payload payload)
    {
        var names = new List<string>();
        if (payload?.materials == null) return names;
        foreach (var e in payload.materials)
        {
            if (e == null) continue;
            var p = e.pbrMetallicRoughness;
            foreach (int? texture in new[]
                     {
                         p?.baseColorTexture?.index, p?.metallicRoughnessTexture?.index, e.normalTexture?.index,
                         e.occlusionTexture?.index, e.emissiveTexture?.index,
                     })
            {
                string name = ImageOf(payload, texture);
                if (name != null && !names.Contains(name)) names.Add(name);
            }
        }
        return names;
    }
}
