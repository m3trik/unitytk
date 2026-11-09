// ----------------------------------------------------------------------------
// The record carrier's reader of the FBX file itself (the carrier feature's
// editor part): a model's data_export channels before Unity hands an importer
// its user properties, read by the rules pythontk reads the same file by.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using M3trik.Unitytk.Kernel;
using UnityEditor;

/// <summary>
/// The <c>data_export</c> carriers' channels read off a model's binary FBX
/// itself, for an importer that needs a record before Unity hands it the
/// file's user properties: those reach OnPostprocessGameObjectWithUserProperties
/// after every material description, and nothing of them reaches
/// OnPreprocessModel (measured 2026-10-31, 6000.3.10f1). Read by the rules
/// pythontk reads the same file by (FbxFile / FbxMedia): Kaydara's binary
/// records, 32-bit before version 7500 and 64-bit from it; a carrier is a
/// Model of the Objects section whose name <see cref="IsCarrier"/> accepts,
/// its channels the string values of its Properties70 <c>P</c> rows; of the
/// carriers stating a channel the scene's own wins (<see cref="IsSceneCarrier"/>:
/// the root-level <c>data_export</c>, its Model wired to the scene root by the
/// Connections section) else the first in file order (pythontk
/// <c>FbxMedia._applied_channels</c>, <c>MeshConvert._carried_record</c>). The
/// walk ends with the Connections section: arrays, geometry and embedded media
/// are skipped by their record lengths and the sections after it (Takes) are
/// never read; a file cut short after its Objects still carries its records,
/// each own-named carrier then taken as root level. A package's model is read where the package lies
/// (<c>FileUtil.GetPhysicalPath</c>). An ASCII FBX carries none here (Unity
/// truncates its long strings anyway); a file it cannot read or parse is said,
/// and carries none. The global name is kept, since verifiers name it.
/// </summary>
/// <remarks>
/// Prior art: no Unity API reads a model's user properties outside its import, and
/// the FBX SDK (Autodesk's licence) is not in a Unity project, so this walks the
/// binary records as pythontk's <c>FbxFile</c> does, by the same precedence.
/// </remarks>
public static class CarrierFile
{
    /// <summary>The 23 bytes every binary FBX starts with.</summary>
    static readonly byte[] MAGIC = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0");

    /// <summary>FBX splits an object's name from its class with these two bytes.</summary>
    const byte NAME_END = 0, CLASS_START = 1;

    // The last file read (an import asks once per importer that reads a record).
    static string _path;
    static long _stamp = -1, _length = -1;
    static Dictionary<string, string> _channels;
    static HashSet<string> _rootValues;

    /// <summary>
    /// The channel <paramref name="key"/> the model at <paramref name="assetPath"/>
    /// carries, as its readers apply it -- the scene's own carrier's, else the
    /// first carrier's -- or null when no carrier states it.
    /// </summary>
    public static string Record(string assetPath, string key)
        => Channels(assetPath).TryGetValue(key, out var value) ? value : null;

    /// <summary>
    /// True when the model at <paramref name="assetPath"/> carries
    /// <paramref name="value"/> on its scene's own carrier (the root-level
    /// <c>data_export</c>, <see cref="IsSceneCarrier"/>), or when its file cannot
    /// tell: missing, ASCII, unreadable, or holding no carrier. What
    /// <see cref="CarrierImport.Collect"/> asks of an own-named node, which Unity
    /// hands it before the model's hierarchy is parented: a grouped module's
    /// <c>Module|data_export</c> then looks like the scene's own.
    /// </summary>
    public static bool AtRoot(string assetPath, string value)
    {
        if (string.IsNullOrEmpty(assetPath) || !File.Exists(FileUtil.GetPhysicalPath(assetPath)))
            return true;
        Channels(assetPath);
        return _rootValues == null || (value != null && _rootValues.Contains(value));
    }

    /// <summary>
    /// True when a node named <paramref name="name"/> is a carrier: the scene's
    /// own (<see cref="CarrierImport.CARRIER"/>), a referenced module's
    /// (<c>NS:data_export</c>), or a numbered copy an import made
    /// (<c>data_export1</c>) -- a DAG path and a namespace aside. pythontk's
    /// <c>SceneStoreBase.is_carrier</c> exactly (the name, the name and digits,
    /// or the name, a dot and digits: <c>data_export.001</c>): a property of
    /// a record's name on another node is an artist's, no record. Every channel
    /// reads carriers alone (<see cref="CarrierImport.Collect"/>).
    /// </summary>
    public static bool IsCarrier(string name)
    {
        string leaf = name ?? "";
        leaf = M3trik.Unitytk.Core.RecordNames.Leaf(leaf.Substring(leaf.LastIndexOf('|') + 1));
        if (leaf == CarrierImport.CARRIER) return true;
        if (!leaf.StartsWith(CarrierImport.CARRIER, StringComparison.Ordinal)) return false;
        string number = leaf.Substring(CarrierImport.CARRIER.Length);
        if (number[0] == '.') number = number.Substring(1);
        if (number.Length == 0) return false;
        foreach (char c in number)
            if (c < '0' || c > '9') return false;
        return true;
    }

    /// <summary>
    /// True when a node named <paramref name="name"/> is the scene's own carrier:
    /// named exactly <see cref="CarrierImport.CARRIER"/> (no namespace, no number)
    /// and at the root of the file's hierarchy (<paramref name="rootLevel"/>: its
    /// FBX Model wired to the scene root; a top-level child of the model in Unity).
    /// Every other carrier is a module's -- a referenced module's
    /// <c>NS:data_export</c>, a numbered copy, a grouped module's own-named
    /// <c>data_export</c> under a group -- read only where the scene's carries no
    /// such channel. pythontk's <c>SceneStoreBase.is_scene_carrier</c> exactly.
    /// </summary>
    public static bool IsSceneCarrier(string name, bool rootLevel)
        => rootLevel && (name ?? "").Trim('|') == CarrierImport.CARRIER;

    static Dictionary<string, string> Channels(string assetPath)
    {
        // A package's asset path (Packages/<name>/...) names a folder the file is not in
        // (Library/PackageCache, or a local package anywhere on disk).
        string path = string.IsNullOrEmpty(assetPath) ? assetPath : FileUtil.GetPhysicalPath(assetPath);
        var info = new FileInfo(string.IsNullOrEmpty(path) ? assetPath : path);
        if (!info.Exists)
        {
            ImportLog.Warning(assetPath, $"the model's file was not found ('{info.FullName}') -- its " +
                                         "data_export records are read as absent.");
            return new Dictionary<string, string>();
        }
        long stamp = info.LastWriteTimeUtc.Ticks;
        if (_channels != null && _path == assetPath && _stamp == stamp && _length == info.Length)
            return _channels;
        Dictionary<string, string> found;
        HashSet<string> rootValues;
        try
        {
            found = Read(info.FullName, info.Length, out rootValues);
        }
        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException
                                  || e is FormatException || e is ArgumentException)
        {
            ImportLog.Warning(assetPath, $"its data_export carrier could not be read off the file ({e.Message}) " +
                                         "-- its records are read as absent.");
            found = new Dictionary<string, string>();
            rootValues = null;
        }
        _path = assetPath;
        _stamp = stamp;
        _length = info.Length;
        _rootValues = rootValues;
        return _channels = found;
    }

    /// <summary>One record's header: where it ends, its properties' span, its name.</summary>
    struct Head
    {
        public long end, props, propsLength;
        public string name;
    }

    /// <summary>One carrier Model of the Objects section: its id, its name, its channels in row order.</summary>
    sealed class Carrier
    {
        public long id;
        public string name;
        public readonly List<KeyValuePair<string, string>> rows = new List<KeyValuePair<string, string>>();
    }

    // The channels by the precedence, and in rootValues every value the scene's own
    // carrier holds -- null when the file holds no carrier (or is no binary FBX).
    static Dictionary<string, string> Read(string path, long length, out HashSet<string> rootValues)
    {
        rootValues = null;
        var channels = new Dictionary<string, string>(StringComparer.Ordinal);
        var own = new Dictionary<string, string>(StringComparer.Ordinal);  // the scene's own carrier's
        if (length < MAGIC.Length + 4) return channels;
        var carriers = new List<Carrier>();
        var models = new HashSet<long>();
        var nested = new HashSet<long>();  // carriers wired under another Model: a grouped module's
        using (var file = new Source(path))
        {
            for (int i = 0; i < MAGIC.Length; i++)
                if (file.Byte(i) != MAGIC[i]) return channels;  // ASCII, or not an FBX
            bool wide = file.UInt32(MAGIC.Length) >= 7500;
            long pos = MAGIC.Length + 4;
            bool objects = false;
            try
            {
                while (Next(file, ref pos, wide, file.Length, out var root))
                {
                    if (root.name == "Objects")
                    {
                        ReadCarriers(file, root, wide, carriers, models);
                        objects = true;  // a file cut short inside its Objects carries none
                        if (carriers.Count == 0) break;  // no carrier: nothing to place
                    }
                    else if (root.name == "Connections" && objects)
                    {
                        ReadNested(file, root, wide, carriers, models, nested);
                        break;  // the carriers and their parents are read; nothing after it is
                    }
                }
            }
            catch (FormatException) when (objects)
            {
                // Cut short after its Objects: the records stand, every carrier at the root.
            }
        }
        if (carriers.Count > 0) rootValues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var carrier in carriers)
        {
            bool scene = IsSceneCarrier(carrier.name, !nested.Contains(carrier.id));
            var held = scene ? own : channels;
            foreach (var row in carrier.rows)
            {
                if (!held.ContainsKey(row.Key)) held[row.Key] = row.Value;
                if (scene) rootValues.Add(row.Value);
            }
        }
        foreach (var pair in own)
            channels[pair.Key] = pair.Value;
        return channels;
    }

    /// <summary>Every carrier Model of the Objects section <paramref name="root"/>, with its
    /// channels, and the id of every Model (<paramref name="models"/>).</summary>
    static void ReadCarriers(Source file, Head root, bool wide, List<Carrier> carriers, HashSet<long> models)
    {
        foreach (var model in Children(file, root, wide))
        {
            if (model.name != "Model") continue;
            var head = Scalars(file, model);
            if (head.Count > 0 && head[0] is long id) models.Add(id);
            string name = head.Count > 1 ? DisplayName(head[1] as byte[]) : null;
            if (!IsCarrier(name)) continue;
            var carrier = new Carrier { id = head.Count > 0 && head[0] is long cid ? cid : 0, name = name };
            foreach (var block in Children(file, model, wide))
            {
                if (block.name != "Properties70") continue;
                foreach (var row in Children(file, block, wide))
                {
                    var p = Scalars(file, row);
                    if (p.Count < 5 || !(p[0] is byte[] key) || !(p[4] is byte[] value)) continue;
                    string text = Encoding.UTF8.GetString(value);
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    carrier.rows.Add(new KeyValuePair<string, string>(Encoding.UTF8.GetString(key), text));
                }
            }
            carriers.Add(carrier);
        }
    }

    /// <summary>The carriers an <c>OO</c> row of the Connections section <paramref name="root"/>
    /// wires under another Model (<paramref name="nested"/>); one wired to the scene root
    /// (id 0) is at the root.</summary>
    static void ReadNested(
        Source file, Head root, bool wide, List<Carrier> carriers, HashSet<long> models, HashSet<long> nested)
    {
        var ids = new HashSet<long>();
        foreach (var carrier in carriers) ids.Add(carrier.id);
        foreach (var row in Children(file, root, wide))
        {
            var c = Scalars(file, row);
            if (c.Count >= 3 && c[0] is byte[] kind && kind.Length == 2 && kind[0] == 'O' && kind[1] == 'O'
                && c[1] is long child && c[2] is long parent && ids.Contains(child) && models.Contains(parent))
                nested.Add(child);
        }
    }

    /// <summary>
    /// The record at <paramref name="pos"/> (moved past it), or false at the
    /// NULL record that ends a list. A record that ends before it starts or
    /// past <paramref name="limit"/> -- its parent's end, or the file's -- is a
    /// truncated or corrupt file.
    /// </summary>
    static bool Next(Source file, ref long pos, bool wide, long limit, out Head head)
    {
        head = default;
        int size = wide ? 24 : 12;
        long end = wide ? (long)file.UInt64(pos) : file.UInt32(pos);
        long propsLength = wide ? (long)file.UInt64(pos + 16) : file.UInt32(pos + 8);
        int nameLength = file.Byte(pos + size);
        if (end == 0) return false;
        if (end <= pos || end > limit)
            throw new FormatException("a truncated or corrupt FBX record");
        head = new Head
        {
            end = end,
            props = pos + size + 1 + nameLength,
            propsLength = propsLength,
            name = Encoding.ASCII.GetString(file.Bytes(pos + size + 1, nameLength)),
        };
        pos = end;
        return true;
    }

    /// <summary>The records nested in <paramref name="parent"/>, before the NULL record that closes them.</summary>
    static IEnumerable<Head> Children(Source file, Head parent, bool wide)
    {
        long pos = parent.props + parent.propsLength;
        long sentinel = (wide ? 24 : 12) + 1;
        while (pos < parent.end - sentinel && Next(file, ref pos, wide, parent.end, out var child))
            yield return child;
    }

    /// <summary>
    /// The record's leading <c>L</c> / <c>I</c> / <c>S</c> properties, a string
    /// as its bytes -- up to the first property of any other type (pythontk
    /// <c>FbxMedia._scalars</c>): ids, names and a <c>P</c> row's head and
    /// string value are all at the head.
    /// </summary>
    static List<object> Scalars(Source file, Head record)
    {
        var found = new List<object>();
        long pos = record.props, end = record.props + record.propsLength;
        if (end > record.end) throw new FormatException("a truncated or corrupt FBX record");
        while (pos < end)
        {
            char kind = (char)file.Byte(pos);
            if (kind == 'L' && pos + 9 <= end) { found.Add(file.Int64(pos + 1)); pos += 9; }
            else if (kind == 'I' && pos + 5 <= end) { found.Add((int)file.UInt32(pos + 1)); pos += 5; }
            else if (kind == 'S' && pos + 5 <= end)
            {
                long size = file.UInt32(pos + 1);
                if (pos + 5 + size > end) throw new FormatException("a truncated or corrupt FBX string");
                found.Add(file.Bytes(pos + 5, (int)size));
                pos += 5 + size;
            }
            else break;
        }
        return found;
    }

    /// <summary>
    /// The file, read through one window that moves forward with the walk:
    /// the records it visits lie in file order, so most reads are in the
    /// window already, and what it skips (a mesh's arrays, an embedded map) is
    /// never read at all.
    /// </summary>
    sealed class Source : IDisposable
    {
        readonly FileStream _stream;
        readonly byte[] _window = new byte[1 << 16];
        long _start;
        int _count;

        public readonly long Length;

        public Source(string path)
        {
            _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1);
            Length = _stream.Length;
        }

        public void Dispose() => _stream.Dispose();

        /// <summary>The window's offset of <paramref name="count"/> bytes at
        /// <paramref name="pos"/>, read in when they are not there yet.</summary>
        int At(long pos, int count)
        {
            if (pos < 0 || count < 0 || pos + count > Length)
                throw new FormatException("a truncated or corrupt FBX record");
            if (pos < _start || pos + count > _start + _count)
            {
                _stream.Position = _start = pos;
                _count = 0;
                int read;
                while (_count < _window.Length && (read = _stream.Read(_window, _count, _window.Length - _count)) > 0)
                    _count += read;
                if (_count < count) throw new FormatException("a truncated FBX file");
            }
            return (int)(pos - _start);
        }

        public byte Byte(long pos) => _window[At(pos, 1)];
        public uint UInt32(long pos) => BitConverter.ToUInt32(_window, At(pos, 4));
        public ulong UInt64(long pos) => BitConverter.ToUInt64(_window, At(pos, 8));
        public long Int64(long pos) => BitConverter.ToInt64(_window, At(pos, 8));

        public byte[] Bytes(long pos, int count)
        {
            var bytes = new byte[count];
            if (count <= _window.Length)
            {
                Buffer.BlockCopy(_window, At(pos, count), bytes, 0, count);
                return bytes;
            }
            if (pos + count > Length) throw new FormatException("a truncated or corrupt FBX string");
            _stream.Position = pos;
            int done = 0, read;
            while (done < count && (read = _stream.Read(bytes, done, count - done)) > 0)
                done += read;
            if (done < count) throw new FormatException("a truncated FBX file");
            return bytes;
        }
    }

    /// <summary>The name half of an object's name property (<c>name\0\x01Class</c>).</summary>
    static string DisplayName(byte[] raw)
    {
        if (raw == null) return null;
        int cut = raw.Length;
        for (int i = 0; i + 1 < raw.Length; i++)
            if (raw[i] == NAME_END && raw[i + 1] == CLASS_START) { cut = i; break; }
        return Encoding.UTF8.GetString(raw, 0, cut);
    }
}
