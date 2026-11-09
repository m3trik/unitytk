using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Node = M3trik.Unitytk.Testing.FbxNode;
using M3trik.Unitytk.Testing;
namespace M3trik.Unitytk.Carrier.Tests
{
    /// <summary>
    /// A binary FBX 7.4 of carrier Models alone, as <see cref="CarrierFile"/> reads one:
    /// each carrier a <c>Null</c> Model holding one string channel as a user property,
    /// wired to the scene root or under a group Model of its own, in the order given.
    /// Objects and Connections only: the reader walks no other section. And a whole file
    /// Unity's model import reads (<see cref="WriteEmbedding"/>): one <c>Null</c> Model and
    /// the files given EMBEDDED as the producers embed them, each a bare <c>Video</c>
    /// holding its bytes under its file name, wired to nothing.
    /// </summary>
    /// <remarks>
    /// The layout is Kaydara's binary FBX as the open-source writers document it (as
    /// the materials feature's <c>DeliveryFbx</c> writes a whole delivery): each record
    /// its end offset, property count and length, name, typed properties and nested
    /// records closed by a 13-byte null record; the list closed by a null record.
    /// </remarks>
    internal static class CarrierFbx
    {
        /// <summary>
        /// Write <paramref name="carriers"/> -- each a node's name, the group it hangs
        /// under (null: the scene root), and one channel's key and value -- to
        /// <paramref name="path"/>, in file order.
        /// </summary>
        public static string Write(string path, params (string name, string parent, string key, string value)[] carriers)
        {
            var objects = new Node("Objects");
            var links = new Node("Connections");
            var groups = new Dictionary<string, long>(StringComparer.Ordinal);
            long id = 1000;
            foreach (var c in carriers)
            {
                if (c.parent == null || groups.ContainsKey(c.parent)) continue;
                groups[c.parent] = ++id;
                objects.Add(Model(id, c.parent, null, null));
                links.Add(new Node("C", "OO", id, 0L));
            }
            foreach (var c in carriers)
            {
                objects.Add(Model(++id, c.name, c.key, c.value));
                links.Add(new Node("C", "OO", id, c.parent == null ? 0L : groups[c.parent]));
            }

            File.WriteAllBytes(path, FbxBinary.Write(new[] { objects, links }, footer: false));
            return path;
        }

        /// <summary>
        /// Write a model Unity imports to <paramref name="path"/>: one <c>Null</c> node,
        /// <c>root</c>, and each of <paramref name="files"/> embedded by its file name.
        /// </summary>
        public static string WriteEmbedding(string path, IDictionary<string, byte[]> files)
        {
            var objects = new Node("Objects");
            var links = new Node("Connections");
            objects.Add(Model(1000, "root", null, null));
            links.Add(new Node("C", "OO", 1000L, 0L));
            long id = 2000;
            foreach (var file in files)
                objects.Add(new Node("Video", ++id, file.Key + "\0\x01Video", "Clip")
                    .Add(new Node("Type", "Clip"))
                    .Add(new Node("Properties70")
                        .Add(new Node("P", "Path", "KString", "XRefUrl", "", file.Key))
                        .Add(new Node("P", "RelPath", "KString", "XRefUrl", "", file.Key)))
                    .Add(new Node("UseMipMap", 0))
                    .Add(new Node("Filename", file.Key))
                    .Add(new Node("RelativeFilename", file.Key))
                    .Add(new Node("Content", file.Value)));
            var root = new List<Node>
            {
                new Node("FBXHeaderExtension").Add(new Node("FBXHeaderVersion", 1003)).Add(new Node("FBXVersion", (int)FbxBinary.Version))
                    .Add(new Node("EncryptionType", 0)).Add(new Node("Creator", "unitytk tests")),
                new Node("FileId", FbxBinary.FileId),
                new Node("CreationTime", "1970-01-01 10:00:00:000"),
                new Node("Creator", "unitytk tests"),
                new Node("GlobalSettings").Add(new Node("Version", 1000)).Add(new Node("Properties70")
                    .Add(new Node("P", "UpAxis", "int", "Integer", "", 1))
                    .Add(new Node("P", "UnitScaleFactor", "double", "Number", "", 1.0))),
                new Node("Definitions").Add(new Node("Version", 100)).Add(new Node("Count", 1 + files.Count))
                    .Add(new Node("ObjectType", "Model").Add(new Node("Count", 1)))
                    .Add(new Node("ObjectType", "Video").Add(new Node("Count", files.Count))),
                objects,
                links,
            };
            File.WriteAllBytes(path, FbxBinary.Write(root));
            return path;
        }

        static Node Model(long id, string name, string key, string value)
        {
            var properties = new Node("Properties70");
            if (key != null) properties.Add(new Node("P", key, "KString", "", "U", value));
            return new Node("Model", id, name + "\0\x01Model", "Null").Add(new Node("Version", 232)).Add(properties);
        }

    }
}
