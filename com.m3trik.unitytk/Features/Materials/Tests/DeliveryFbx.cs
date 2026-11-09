using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Node = M3trik.Unitytk.Testing.FbxNode;
using M3trik.Unitytk.Testing;
namespace M3trik.Unitytk.Materials.Tests
{
    /// <summary>
    /// A delivery as a DCC writes one, as a binary FBX 7.4: one mesh of a quad per
    /// material, side by side, each quad wearing its own material, and the scene's
    /// <c>data_export</c> carrier holding the materials record. Binary, not ASCII: the
    /// importer reads the record off the file before Unity hands it the user
    /// properties, and that reader reads Kaydara's binary records alone.
    /// </summary>
    /// <remarks>
    /// The layout is Kaydara's binary FBX as the open-source writers document it: each
    /// record its end offset, property count and length, name, typed properties and
    /// nested records closed by a 13-byte null record; the file closed by the null
    /// record, the footer id, padding to 16 bytes, the version and the footer magic.
    /// </remarks>
    internal static class DeliveryFbx
    {
        const string Time = "1970-01-01 10:00:00:000";

        /// <summary>
        /// Write a model of one quad per material in <paramref name="materials"/> to
        /// <paramref name="path"/>, its carrier holding <paramref name="record"/> as the
        /// <c>materials</c> channel (none when null).
        /// </summary>
        public static void Write(string path, string record, params string[] materials)
        {
            var objects = new Node("Objects");
            const long geometryId = 1000, meshId = 2000, carrierId = 3000;
            objects.Add(Geometry(geometryId, materials.Length));
            objects.Add(Model(meshId, "Quads", "Mesh", null));
            objects.Add(Model(carrierId, "data_export", "Null", record));
            var links = new Node("Connections");
            links.Add("C", "OO", meshId, 0L);
            links.Add("C", "OO", carrierId, 0L);
            links.Add("C", "OO", geometryId, meshId);
            for (int i = 0; i < materials.Length; i++)
            {
                long id = 4000 + i;
                var material = new Node("Material", id, materials[i] + "\0\x01Material", "");
                material.Add("Version", 102).Add("ShadingModel", "phong").Add("MultiLayer", 0)
                        .Add(Properties(P("DiffuseColor", "Color", "", "A", 0.8, 0.8, 0.8)));
                objects.Add(material);
                links.Add("C", "OO", id, meshId);
            }

            var root = new List<Node>
            {
                new Node("FBXHeaderExtension")
                    .Add("FBXHeaderVersion", 1003).Add("FBXVersion", (int)FbxBinary.Version).Add("EncryptionType", 0)
                    .Add(new Node("CreationTimeStamp").Add("Version", 1000).Add("Year", 1970).Add("Month", 1).Add("Day", 1)
                                                      .Add("Hour", 10).Add("Minute", 0).Add("Second", 0).Add("Millisecond", 0))
                    .Add("Creator", "unitytk tests"),
                new Node("FileId", FbxBinary.FileId),
                new Node("CreationTime", Time),
                new Node("Creator", "unitytk tests"),
                new Node("GlobalSettings").Add("Version", 1000).Add(Properties(
                    P("UpAxis", "int", "Integer", "", 1), P("UpAxisSign", "int", "Integer", "", 1),
                    P("FrontAxis", "int", "Integer", "", 2), P("FrontAxisSign", "int", "Integer", "", 1),
                    P("CoordAxis", "int", "Integer", "", 0), P("CoordAxisSign", "int", "Integer", "", 1),
                    P("UnitScaleFactor", "double", "Number", "", 1.0))),
                new Node("Definitions").Add("Version", 100).Add("Count", 3 + materials.Length)
                    .Add(new Node("ObjectType", "Model").Add("Count", 2))
                    .Add(new Node("ObjectType", "Geometry").Add("Count", 1))
                    .Add(new Node("ObjectType", "Material").Add("Count", materials.Length)),
                objects,
                links,
            };

            File.WriteAllBytes(path, FbxBinary.Write(root));
        }

        // Quads one unit wide along X, one per material, in the XY plane facing +Z.
        static Node Geometry(long id, int quads)
        {
            var vertices = new double[quads * 12];
            var indices = new int[quads * 4];
            var normals = new double[quads * 12];
            var uvs = new double[quads * 8];
            var uvIndex = new int[quads * 4];
            var perPolygon = new int[quads];
            double[] corners = { 0, 0, 1, 0, 1, 1, 0, 1 };
            for (int q = 0; q < quads; q++)
            {
                for (int c = 0; c < 4; c++)
                {
                    int v = q * 4 + c;
                    vertices[v * 3] = (q * 1.5 + corners[c * 2]) * 100;
                    vertices[v * 3 + 1] = corners[c * 2 + 1] * 100;
                    indices[v] = c == 3 ? -(v + 1) : v;
                    normals[v * 3 + 2] = 1;
                    uvs[v * 2] = corners[c * 2];
                    uvs[v * 2 + 1] = corners[c * 2 + 1];
                    uvIndex[v] = v;
                }
                perPolygon[q] = q;
            }
            var geometry = new Node("Geometry", id, "Quads\0\x01Geometry", "Mesh");
            geometry.Add("Vertices", vertices).Add("PolygonVertexIndex", indices).Add("GeometryVersion", 124);
            geometry.Add(new Node("LayerElementNormal", 0).Add("Version", 102).Add("Name", "")
                .Add("MappingInformationType", "ByPolygonVertex").Add("ReferenceInformationType", "Direct")
                .Add("Normals", normals));
            geometry.Add(new Node("LayerElementUV", 0).Add("Version", 101).Add("Name", "map1")
                .Add("MappingInformationType", "ByPolygonVertex").Add("ReferenceInformationType", "IndexToDirect")
                .Add("UV", uvs).Add("UVIndex", uvIndex));
            geometry.Add(new Node("LayerElementMaterial", 0).Add("Version", 101).Add("Name", "")
                .Add("MappingInformationType", "ByPolygon").Add("ReferenceInformationType", "IndexToDirect")
                .Add("Materials", perPolygon));
            geometry.Add(new Node("Layer", 0).Add("Version", 100)
                .Add(new Node("LayerElement").Add("Type", "LayerElementNormal").Add("TypedIndex", 0))
                .Add(new Node("LayerElement").Add("Type", "LayerElementMaterial").Add("TypedIndex", 0))
                .Add(new Node("LayerElement").Add("Type", "LayerElementUV").Add("TypedIndex", 0)));
            return geometry;
        }

        static Node Model(long id, string name, string kind, string record)
        {
            var properties = new Node("Properties70");
            if (record != null) properties.Add(P("materials", "KString", "", "U", record));
            return new Node("Model", id, name + "\0\x01Model", kind)
                .Add("Version", 232).Add(properties).Add("Shading", true).Add("Culling", "CullingOff");
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
