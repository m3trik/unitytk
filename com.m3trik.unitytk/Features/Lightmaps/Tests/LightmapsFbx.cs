using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

using Node = M3trik.Unitytk.Testing.FbxNode;
using M3trik.Unitytk.Testing;
namespace M3trik.Unitytk.Lightmaps.Tests
{
    /// <summary>
    /// A lightmapped delivery as a DCC writes one, as a binary FBX 7.4 (centimetres, Y
    /// up): a room of quads, each with a second UV set for its lightmap, the
    /// <c>data_export</c> carrier holding the lightmaps record, and the maps and the
    /// probe's panorama EMBEDDED as the Scene Exporter embeds them -- each a bare
    /// <c>Video</c> holding the file's bytes under its file name, wired to nothing,
    /// which Unity's model import extracts on request:
    /// <list type="bullet">
    /// <item><c>room</c>: <c>Crate</c> (its own map, <c>Crate_Lightmap.exr</c>), <c>Floor</c> and
    /// <c>Wall</c> (one atlas, <c>Room_Lightmap.exr</c>, each its own rect), and two machines,
    /// <c>MACHINE_A/BODY</c> and <c>MACHINE_B/BODY</c>, the same name under different parents
    /// (each its rect of the atlas).</item>
    /// <item>The probe, <c>Room_Probe.exr</c>, a 64 x 32 panorama, captured at (100, 150, -200) in a box
    /// from (-500, 0, -400) to (300, 400, 600), at 0.01 metres per unit.</item>
    /// </list>
    /// </summary>
    internal static class LightmapsFbx
    {
        public const string CrateMap = "Crate_Lightmap.exr";
        public const string RoomMap = "Room_Lightmap.exr";
        public const string Probe = "Room_Probe.exr";

        /// <summary>Every node's rect, as the record states it.</summary>
        public static readonly Dictionary<string, Vector4> Rects = new Dictionary<string, Vector4>
        {
            ["Crate"] = new Vector4(1f, 1f, 0f, 0f),
            ["Floor"] = new Vector4(1f, 0.5f, 0f, 0f),
            ["Wall"] = new Vector4(1f, 0.5f, 0f, 0.5f),
            ["MACHINE_A/BODY"] = new Vector4(0.5f, 0.25f, 0f, 0.75f),
            ["MACHINE_B/BODY"] = new Vector4(0.5f, 0.25f, 0.5f, 0.75f),
        };

        /// <summary>The record the carrier holds by default, its names with <paramref name="ns"/> before each.</summary>
        public static string Record(int version = 3, string ns = "")
        {
            string Entry(string name, string map, Vector4 r, params string[] hierarchy)
                => $"{{\"name\": \"{ns}{name}\", \"map\": \"{map}\", \"scaleOffset\": [{F(r.x)}, {F(r.y)}, {F(r.z)}, {F(r.w)}], " +
                   $"\"hierarchy\": [{string.Join(", ", hierarchy.Select(h => $"\"{ns}{h}\""))}]}}";
            return $"{{\"version\": {version}, \"unit_scale\": 0.01, \"objects\": [" +
                   string.Join(", ",
                       Entry("Crate", CrateMap, Rects["Crate"], "room", "Crate"),
                       Entry("Floor", RoomMap, Rects["Floor"], "room", "Floor"),
                       Entry("Wall", RoomMap, Rects["Wall"], "room", "Wall"),
                       Entry("BODY", RoomMap, Rects["MACHINE_A/BODY"], "room", "MACHINE_A", "BODY"),
                       Entry("BODY", RoomMap, Rects["MACHINE_B/BODY"], "room", "MACHINE_B", "BODY")) +
                   $"], \"probe\": {{\"map\": \"{Probe}\", \"position\": [100, 150, -200], " +
                   "\"box\": {\"min\": [-500, 0, -400], \"max\": [300, 400, 600]}}}";
        }

        /// <summary>
        /// Write the delivery to <paramref name="path"/>: its carrier holding
        /// <paramref name="record"/> (none when null), its nodes named with
        /// <paramref name="ns"/> before each, and <paramref name="files"/> embedded by name.
        /// </summary>
        public static void Write(string path, string record, string ns = "", IDictionary<string, byte[]> files = null)
        {
            var w = new Writer();
            Null(w, 100, ns + "room", 0);
            Mesh(w, 1000, ns + "Crate", 100, new Vector3(0f, 0f, 0f));
            Mesh(w, 2000, ns + "Floor", 100, new Vector3(150f, 0f, 0f));
            Mesh(w, 3000, ns + "Wall", 100, new Vector3(300f, 0f, 0f));
            Null(w, 4000, ns + "MACHINE_A", 100);
            Mesh(w, 4100, ns + "BODY", 4000, new Vector3(0f, 150f, 0f));
            Null(w, 5000, ns + "MACHINE_B", 100);
            Mesh(w, 5100, ns + "BODY", 5000, new Vector3(150f, 150f, 0f));
            Model(w, 9000, "data_export", "Null", 0, record == null ? null : P("lightmaps", "KString", "", "U", record));
            long video = 20000;
            foreach (var file in files ?? new Dictionary<string, byte[]>()) w.objects.Add(Video(video++, file.Key, file.Value, w));

            var root = new List<Node>
            {
                new Node("FBXHeaderExtension").Add("FBXHeaderVersion", 1003).Add("FBXVersion", 7400).Add("EncryptionType", 0)
                    .Add(new Node("CreationTimeStamp").Add("Version", 1000).Add("Year", 1970).Add("Month", 1).Add("Day", 1)
                                                      .Add("Hour", 10).Add("Minute", 0).Add("Second", 0).Add("Millisecond", 0))
                    .Add("Creator", "unitytk tests"),
                new Node("FileId", FbxBinary.FileId),
                new Node("CreationTime", "1970-01-01 10:00:00:000"),
                new Node("Creator", "unitytk tests"),
                new Node("GlobalSettings").Add("Version", 1000).Add(Properties(
                    P("UpAxis", "int", "Integer", "", 1), P("UpAxisSign", "int", "Integer", "", 1),
                    P("FrontAxis", "int", "Integer", "", 2), P("FrontAxisSign", "int", "Integer", "", 1),
                    P("CoordAxis", "int", "Integer", "", 0), P("CoordAxisSign", "int", "Integer", "", 1),
                    P("UnitScaleFactor", "double", "Number", "", 1.0))),
                new Node("Definitions").Add("Version", 100).Add("Count", w.models + w.geometries + w.materials + w.videos)
                    .Add(new Node("ObjectType", "Model").Add("Count", w.models))
                    .Add(new Node("ObjectType", "Geometry").Add("Count", w.geometries))
                    .Add(new Node("ObjectType", "Material").Add("Count", w.materials))
                    .Add(new Node("ObjectType", "Video").Add("Count", w.videos)),
                w.objects,
                w.links,
            };
            File.WriteAllBytes(path, FbxBinary.Write(root));
        }

        /// <summary>An EXR of <paramref name="width"/> x <paramref name="height"/> half-float texels, all <paramref name="colour"/>.</summary>
        public static byte[] Exr(int width, int height, Color colour)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
            try
            {
                texture.SetPixels(Enumerable.Repeat(colour, width * height).ToArray());
                texture.Apply(false);
                return texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>The delivery's three files: the two maps and the probe's panorama.</summary>
        public static Dictionary<string, byte[]> Files() => new Dictionary<string, byte[]>
        {
            [CrateMap] = Exr(8, 8, new Color(0.5f, 0.4f, 0.3f, 1f)),
            [RoomMap] = Exr(16, 16, new Color(0.8f, 0.7f, 0.6f, 1f)),
            [Probe] = Exr(64, 32, new Color(0.25f, 0.5f, 1f, 1f)),
        };

                sealed class Writer
        {
            public readonly Node objects = new Node("Objects");
            public readonly Node links = new Node("Connections");
            public int models, geometries, materials, videos;
        }

        static string F(float v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

        static void Model(Writer w, long id, string name, string kind, long parent, Node property, Vector3 at = default)
        {
            w.models++;
            var properties = new Node("Properties70");
            if (at != default) properties.Add(P("Lcl Translation", "Lcl Translation", "", "A", (double)at.x, (double)at.y, (double)at.z));
            if (property != null) properties.Add(property);
            w.objects.Add(new Node("Model", id, name + "\0\x01Model", kind)
                          .Add("Version", 232).Add(properties).Add("Shading", true).Add("Culling", "CullingOff"));
            w.links.Add("C", "OO", id, parent);
        }

        static void Null(Writer w, long id, string name, long parent) => Model(w, id, name, "Null", parent, null);

        // A one-metre quad facing +Z, its texture UVs and its lightmap UVs (the second set), and a material of its own.
        static void Mesh(Writer w, long id, string name, long parent, Vector3 at)
        {
            Model(w, id, name, "Mesh", parent, null, at);
            double[] corners = { 0, 0, 1, 0, 1, 1, 0, 1 };
            var vertices = new double[12];
            var normals = new double[12];
            var uvs = new double[8];
            for (int c = 0; c < 4; c++)
            {
                vertices[c * 3] = corners[c * 2] * 100;
                vertices[c * 3 + 1] = corners[c * 2 + 1] * 100;
                normals[c * 3 + 2] = 1;
                uvs[c * 2] = corners[c * 2];
                uvs[c * 2 + 1] = corners[c * 2 + 1];
            }
            long geometry = id + 1;
            w.geometries++;
            var g = new Node("Geometry", geometry, name + "\0\x01Geometry", "Mesh");
            g.Add("Vertices", vertices).Add("PolygonVertexIndex", new[] { 0, 1, 2, -4 }).Add("GeometryVersion", 124);
            g.Add(new Node("LayerElementNormal", 0).Add("Version", 102).Add("Name", "")
                .Add("MappingInformationType", "ByPolygonVertex").Add("ReferenceInformationType", "Direct").Add("Normals", normals));
            g.Add(new Node("LayerElementUV", 0).Add("Version", 101).Add("Name", "map1")
                .Add("MappingInformationType", "ByPolygonVertex").Add("ReferenceInformationType", "IndexToDirect")
                .Add("UV", uvs).Add("UVIndex", new[] { 0, 1, 2, 3 }));
            g.Add(new Node("LayerElementUV", 1).Add("Version", 101).Add("Name", "lightmap")
                .Add("MappingInformationType", "ByPolygonVertex").Add("ReferenceInformationType", "IndexToDirect")
                .Add("UV", uvs).Add("UVIndex", new[] { 0, 1, 2, 3 }));
            g.Add(new Node("LayerElementMaterial", 0).Add("Version", 101).Add("Name", "")
                .Add("MappingInformationType", "AllSame").Add("ReferenceInformationType", "IndexToDirect").Add("Materials", new[] { 0 }));
            g.Add(new Node("Layer", 0).Add("Version", 100)
                .Add(new Node("LayerElement").Add("Type", "LayerElementNormal").Add("TypedIndex", 0))
                .Add(new Node("LayerElement").Add("Type", "LayerElementMaterial").Add("TypedIndex", 0))
                .Add(new Node("LayerElement").Add("Type", "LayerElementUV").Add("TypedIndex", 0)));
            g.Add(new Node("Layer", 1).Add("Version", 100)
                .Add(new Node("LayerElement").Add("Type", "LayerElementUV").Add("TypedIndex", 1)));
            w.objects.Add(g);
            w.links.Add("C", "OO", geometry, id);
            long material = id + 2;
            w.materials++;
            w.objects.Add(new Node("Material", material, name.Replace(":", "_") + "_mat\0\x01Material", "")
                          .Add("Version", 102).Add("ShadingModel", "phong").Add("MultiLayer", 0)
                          .Add(Properties(P("DiffuseColor", "Color", "", "A", 0.8, 0.8, 0.8))));
            w.links.Add("C", "OO", material, id);
        }

        // A bare Video: the file's bytes under its name, wired to nothing (pythontk FbxMedia.embed).
        static Node Video(long id, string name, byte[] data, Writer w)
        {
            w.videos++;
            return new Node("Video", id, name + "\0\x01Video", "Clip")
                .Add("Type", "Clip")
                .Add(Properties(P("Path", "KString", "XRefUrl", "", name), P("RelPath", "KString", "XRefUrl", "", name)))
                .Add("UseMipMap", 0)
                .Add("Filename", name)
                .Add("RelativeFilename", name)
                .Add("Content", data);
        }

        static Node P(params object[] values) => new Node("P", values);

        static Node Properties(params Node[] rows)
        {
            var block = new Node("Properties70");
            foreach (var row in rows) block.Add(row);
            return block;
        }

    }
}
