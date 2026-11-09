using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace M3trik.Unitytk.Testing
{
    /// <summary>A node of a binary FBX: its name, its properties and its children.</summary>
    public sealed class FbxNode
    {
        /// <summary>A node named <paramref name="name"/> carrying <paramref name="props"/>.</summary>
        public FbxNode(string name, params object[] props)
        {
            Name = name;
            Props = props;
        }

        /// <summary>The node's name.</summary>
        public string Name { get; }

        /// <summary>Its properties: <c>long</c>, <c>int</c>, <c>double</c>, <c>string</c> or <c>byte[]</c>.</summary>
        public object[] Props { get; }

        /// <summary>Its children, in file order.</summary>
        public List<FbxNode> Children { get; } = new List<FbxNode>();

        /// <summary>Add <paramref name="child"/>; this node.</summary>
        public FbxNode Add(FbxNode child)
        {
            Children.Add(child);
            return this;
        }

        /// <summary>Add a child named <paramref name="name"/> carrying <paramref name="props"/>; this node.</summary>
        public FbxNode Add(string name, params object[] props) => Add(new FbxNode(name, props));
    }

    /// <summary>
    /// A binary FBX 7.4 written from its nodes, as the test deliveries write theirs: the
    /// header, every top-level node's record, the null record, and the footer.
    /// </summary>
    /// <remarks>
    /// Prior art: Autodesk's FBX SDK is the reference writer and is not in a Unity project;
    /// the layout is the documented binary record format (version 7400: 32-bit offsets).
    /// </remarks>
    public static class FbxBinary
    {
        /// <summary>The file's version, 7.4.</summary>
        public const uint Version = 7400;

        static readonly byte[] Magic = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0");

        /// <summary>The FileId the SDK writes, which the footer's hash must match.</summary>
        public static readonly byte[] FileId = { 0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1 };

        static readonly byte[] FootId = { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e };
        static readonly byte[] FootMagic = { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b };

        /// <summary>
        /// The file of <paramref name="nodes"/>, with the SDK's footer unless
        /// <paramref name="footer"/> is false (a reader that stops at the null record).
        /// </summary>
        public static byte[] Write(IEnumerable<FbxNode> nodes, bool footer = true)
        {
            using (var stream = new MemoryStream())
            using (var w = new BinaryWriter(stream))
            {
                w.Write(Magic);
                w.Write(Version);
                foreach (FbxNode node in nodes) Record(w, node);
                w.Write(new byte[13]);
                if (footer)
                {
                    w.Write(FootId);
                    w.Write(new byte[4]);
                    int pad = (int)(((stream.Position + 15) & ~15L) - stream.Position);
                    w.Write(new byte[pad == 0 ? 16 : pad]);
                    w.Write(Version);
                    w.Write(new byte[120]);
                    w.Write(FootMagic);
                }
                w.Flush();
                return stream.ToArray();
            }
        }

        static void Record(BinaryWriter w, FbxNode node)
        {
            var stream = w.BaseStream;
            long start = stream.Position;
            w.Write(0u);
            w.Write((uint)node.Props.Length);
            w.Write(0u);
            byte[] name = Encoding.ASCII.GetBytes(node.Name);
            w.Write((byte)name.Length);
            w.Write(name);
            long propsStart = stream.Position;
            foreach (var p in node.Props) Property(w, p);
            long propsEnd = stream.Position;
            foreach (var child in node.Children) Record(w, child);
            if (node.Children.Count > 0 || node.Props.Length == 0) w.Write(new byte[13]);
            long end = stream.Position;
            stream.Position = start;
            w.Write((uint)end);
            stream.Position = start + 8;
            w.Write((uint)(propsEnd - propsStart));
            stream.Position = end;
        }

        static void Property(BinaryWriter w, object value)
        {
            switch (value)
            {
                case long l: w.Write((byte)'L'); w.Write(l); break;
                case int i: w.Write((byte)'I'); w.Write(i); break;
                case double d: w.Write((byte)'D'); w.Write(d); break;
                case bool b: w.Write((byte)'C'); w.Write((byte)(b ? 1 : 0)); break;
                case byte[] raw: w.Write((byte)'R'); w.Write((uint)raw.Length); w.Write(raw); break;
                case string s:
                    byte[] text = Encoding.UTF8.GetBytes(s);
                    w.Write((byte)'S'); w.Write((uint)text.Length); w.Write(text);
                    break;
                case double[] ds:
                    w.Write((byte)'d'); w.Write((uint)ds.Length); w.Write(0u); w.Write((uint)(ds.Length * 8));
                    foreach (double v in ds) w.Write(v);
                    break;
                case int[] ints:
                    w.Write((byte)'i'); w.Write((uint)ints.Length); w.Write(0u); w.Write((uint)(ints.Length * 4));
                    foreach (int v in ints) w.Write(v);
                    break;
                default: throw new ArgumentException($"no FBX property type for {value?.GetType().Name ?? "null"}");
            }
        }
    }
}
