using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3trik.Unitytk.RenderEffects.Tests
{
    /// <summary>
    /// A delivery as a DCC writes one for its render effects (the layout measured on a
    /// real two-take export), as an ASCII FBX 7.4, which Unity's importer reads as it
    /// reads a binary one. Two takes, TakeA on 0-1 s and TakeB on 1-2 s; per channel a
    /// curve proxy, a Null child named <c>&lt;target&gt;__&lt;channel&gt;</c> carrying the
    /// <c>curveProxy</c> user property, its <c>Lcl Scaling</c> X keyed in each take:
    /// <list type="bullet">
    /// <item><c>box</c> (<c>boxMat</c>): opacity 1 to 0 in TakeA, 0 to 1 in TakeB.</item>
    /// <item><c>crowd</c>, a group of <c>kidA</c> and <c>kidB</c> (<c>kidMat</c>): visibility stepped
    /// off at 0.5 s in TakeA and on at 1.5 s in TakeB, and the node's own FBX Visibility keyed alike.</item>
    /// <item><c>glow</c> (<c>glowMat</c>): highlight 0 to 1, then 1 to 0.</item>
    /// <item><c>twoslot</c> (<c>slotA</c>, <c>slotB</c>): opacity 1 to 0.5, then 0.5 to 1.</item>
    /// <item><c>hull</c> (<c>hullMat_signal</c>, <c>hullMat</c>): <c>hullMat_signal</c>'s emission weight 0 to 1,
    /// then 1 to 0, on the proxy under the <c>data_export</c> carrier, which holds the record.</item>
    /// </list>
    /// </summary>
    internal static class EffectsFbx
    {
        const long Second = 46186158000L;  // FBX time units in a second
        // KeyAttrFlags: linear as the DCC writes it; constant held to the next key (0x02, not 0x102: 'next')
        const int Linear = 24836, Constant = 2;

        /// <summary>The record the file's carrier holds by default: every channel above.</summary>
        public const string Record =
            "{\"version\": 2, \"tracks\": [" +
            "{\"node\": \"box\", \"channels\": [\"opacity\"]}, " +
            "{\"node\": \"crowd\", \"channels\": [\"visibility\"]}, " +
            "{\"node\": \"glow\", \"channels\": [\"highlight\"], \"highlight_color\": [1.0, 0.5, 0.0], \"highlight_color_dim\": [0.0, 0.0, 0.0]}, " +
            "{\"node\": \"twoslot\", \"channels\": [\"opacity\"]}], " +
            "\"materials\": [{\"material\": \"hullMat_signal\", \"channels\": [\"emission\"], \"emission_color\": [1.0, 0.5, 0.25], " +
            "\"emission_strength\": 2.0}]}";

        /// <summary>
        /// <see cref="Record"/> as a delivery of a namespaced asset states it (<see cref="Write"/>
        /// with <c>ns: "ns:"</c>): <c>box</c> and <c>hullMat_signal</c> named with their namespace.
        /// </summary>
        public const string NamespacedRecord =
            "{\"version\": 2, \"tracks\": [{\"node\": \"ns:box\", \"channels\": [\"opacity\"]}], " +
            "\"materials\": [{\"material\": \"ns:hullMat_signal\", \"channels\": [\"emission\"], \"emission_strength\": 2.0}]}";

        sealed class Writer
        {
            public readonly StringBuilder objects = new StringBuilder();
            public readonly StringBuilder links = new StringBuilder();
            public int models, geometries, materials, stacks, layers, nodes, curves;
        }

        /// <summary>
        /// The file, its carrier holding <paramref name="record"/> (none when null);
        /// <paramref name="ns"/> (<c>"ns:"</c>) names <c>box</c>, <c>hull</c> and their
        /// materials as a namespaced asset's, each proxy still by its target's leaf, as the
        /// transport names it.
        /// </summary>
        public static string Write(string record = Record, string ns = "")
        {
            var w = new Writer();
            Mesh(w, 1000, ns + "box", 0, ns + "boxMat");
            Proxy(w, 1300, "box__opacity", 1000);
            Null(w, 2000, "crowd", 0, extra: "\t\t\tP: \"Visibility\", \"Visibility\", \"\", \"A\",1\n");
            Mesh(w, 2001, "kidA", 2000, "kidMat");
            Mesh(w, 2002, "kidB", 2000, new string[] { null });
            w.links.Append("\tC: \"OO\",2201,2002\n");  // kidB shares kidA's material
            Proxy(w, 2300, "crowd__visibility", 2000);
            Mesh(w, 3000, "glow", 0, "glowMat");
            Proxy(w, 3300, "glow__highlight", 3000);
            Mesh(w, 4000, "twoslot", 0, "slotA", "slotB");
            Proxy(w, 4300, "twoslot__opacity", 4000);
            Mesh(w, 5000, ns + "hull", 0, ns + "hullMat_signal", ns + "hullMat");
            Null(w, 9000, "data_export", 0, extra: record == null ? "" :
                $"\t\t\tP: \"render_effects\", \"KString\", \"\", \"U\", \"{record.Replace("\"", "&quot;")}\"\n");
            Proxy(w, 9300, "hullMat_signal__emission", 9000);

            Take(w, 0, "TakeA", 0f, 1f, new[]
            {
                (1300L, "Lcl Scaling", "S", "X", Linear, new[] { (0f, 1f), (1f, 0f) }),
                (2300L, "Lcl Scaling", "S", "X", Constant, new[] { (0f, 1f), (0.5f, 0f), (1f, 0f) }),
                (2000L, "Visibility", "Visibility", "Visibility", Constant, new[] { (0f, 1f), (0.5f, 0f), (1f, 0f) }),
                (3300L, "Lcl Scaling", "S", "X", Linear, new[] { (0f, 0f), (1f, 1f) }),
                (4300L, "Lcl Scaling", "S", "X", Linear, new[] { (0f, 1f), (1f, 0.5f) }),
                (9300L, "Lcl Scaling", "S", "X", Linear, new[] { (0f, 0f), (1f, 1f) }),
            });
            Take(w, 1, "TakeB", 1f, 2f, new[]
            {
                (1300L, "Lcl Scaling", "S", "X", Linear, new[] { (1f, 0f), (2f, 1f) }),
                (2300L, "Lcl Scaling", "S", "X", Constant, new[] { (1f, 0f), (1.5f, 1f), (2f, 1f) }),
                (2000L, "Visibility", "Visibility", "Visibility", Constant, new[] { (1f, 0f), (1.5f, 1f), (2f, 1f) }),
                (3300L, "Lcl Scaling", "S", "X", Linear, new[] { (1f, 1f), (2f, 0f) }),
                (4300L, "Lcl Scaling", "S", "X", Linear, new[] { (1f, 0.5f), (2f, 1f) }),
                (9300L, "Lcl Scaling", "S", "X", Linear, new[] { (1f, 1f), (2f, 0f) }),
            });

            return Document(w, ("TakeA", 0f, 1f), ("TakeB", 1f, 2f));
        }

        /// <summary>The record <see cref="WriteSingle"/>'s carrier holds: the one node's opacity and visibility.</summary>
        public const string SingleRecord =
            "{\"version\": 2, \"tracks\": [{\"node\": \"lamp\", \"channels\": [\"opacity\", \"visibility\"]}], \"materials\": []}";

        /// <summary>
        /// A file whose one top-level node is <c>lamp</c> (<c>lampMat</c>), the carrier and
        /// its two curve proxies under it, in one take (TakeA, 0-1 s): opacity 1 to 0, and
        /// visibility stepped off at 0.5 s with the node's own FBX Visibility keyed alike.
        /// </summary>
        public static string WriteSingle(string record = SingleRecord)
        {
            var w = new Writer();
            Mesh(w, 1000, "lamp", 0, "lampMat");
            Proxy(w, 1300, "lamp__opacity", 1000);
            Proxy(w, 1301, "lamp__visibility", 1000);
            Null(w, 9000, "data_export", 1000, extra:
                $"\t\t\tP: \"render_effects\", \"KString\", \"\", \"U\", \"{record.Replace("\"", "&quot;")}\"\n");
            Take(w, 0, "TakeA", 0f, 1f, new[]
            {
                (1300L, "Lcl Scaling", "S", "X", Linear, new[] { (0f, 1f), (1f, 0f) }),
                (1301L, "Lcl Scaling", "S", "X", Constant, new[] { (0f, 1f), (0.5f, 0f), (1f, 0f) }),
                (1000L, "Visibility", "Visibility", "Visibility", Constant, new[] { (0f, 1f), (0.5f, 0f), (1f, 0f) }),
            });
            return Document(w, ("TakeA", 0f, 1f));
        }

        /// <summary>
        /// A file with two referenced nodes sharing the leaf <c>box</c>: <c>a:box</c>
        /// (<c>aMat</c>) carrying the curve proxy <c>box__opacity</c> (1 to 0) and
        /// <c>b:box</c> (<c>bMat</c>) carrying none, its own FBX Visibility keyed off at
        /// 0.5 s; and two nodes sharing the leaf <c>lid</c>, <c>c:lid</c> and <c>d:lid</c>,
        /// neither with a proxy. One take (TakeA, 0-1 s); the carrier holds
        /// <paramref name="record"/>.
        /// </summary>
        public static string WriteSharedLeaves(string record)
        {
            var w = new Writer();
            Mesh(w, 1000, "a:box", 0, "aMat");
            Proxy(w, 1300, "box__opacity", 1000);
            Mesh(w, 2000, "b:box", 0, "bMat");
            Mesh(w, 3000, "c:lid", 0, "cMat");
            Mesh(w, 4000, "d:lid", 0, "dMat");
            Null(w, 9000, "data_export", 0, extra:
                $"\t\t\tP: \"render_effects\", \"KString\", \"\", \"U\", \"{record.Replace("\"", "&quot;")}\"\n");
            Take(w, 0, "TakeA", 0f, 1f, new[]
            {
                (1300L, "Lcl Scaling", "S", "X", Linear, new[] { (0f, 1f), (1f, 0f) }),
                (2000L, "Visibility", "Visibility", "Visibility", Constant, new[] { (0f, 1f), (0.5f, 0f), (1f, 0f) }),
            });
            return Document(w, ("TakeA", 0f, 1f));
        }

        // The file around what the writer holds: header, settings, definitions, objects, links and takes.
        static string Document(Writer w, params (string name, float start, float stop)[] takes)
        {
            var info = new StringBuilder();
            foreach (var (name, start, stop) in takes) info.Append(TakeInfo(name, start, stop));
            float end = takes[takes.Length - 1].stop;
            int total = w.models + w.geometries + w.materials + w.stacks + w.layers + w.nodes + w.curves + 1;
            return "; FBX 7.4.0 project file\n" +
                   "FBXHeaderExtension:  {\n\tFBXHeaderVersion: 1003\n\tFBXVersion: 7400\n\tCreator: \"unitytk tests\"\n}\n" +
                   "GlobalSettings:  {\n\tVersion: 1000\n\tProperties70:  {\n" +
                   "\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1\n\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1\n" +
                   "\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2\n\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1\n" +
                   "\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0\n\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1\n" +
                   "\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1\n" +
                   "\t\tP: \"TimeMode\", \"enum\", \"\", \"\",6\n" +
                   $"\t\tP: \"TimeSpanStart\", \"KTime\", \"Time\", \"\",0\n\t\tP: \"TimeSpanStop\", \"KTime\", \"Time\", \"\",{Ticks(end)}\n" +
                   "\t}\n}\n" +
                   $"Definitions:  {{\n\tVersion: 100\n\tCount: {total}\n" +
                   "\tObjectType: \"GlobalSettings\" {\n\t\tCount: 1\n\t}\n" +
                   $"\tObjectType: \"Model\" {{\n\t\tCount: {w.models}\n\t}}\n" +
                   $"\tObjectType: \"Geometry\" {{\n\t\tCount: {w.geometries}\n\t}}\n" +
                   $"\tObjectType: \"Material\" {{\n\t\tCount: {w.materials}\n\t}}\n" +
                   $"\tObjectType: \"AnimationStack\" {{\n\t\tCount: {w.stacks}\n\t}}\n" +
                   $"\tObjectType: \"AnimationLayer\" {{\n\t\tCount: {w.layers}\n\t}}\n" +
                   $"\tObjectType: \"AnimationCurveNode\" {{\n\t\tCount: {w.nodes}\n\t}}\n" +
                   $"\tObjectType: \"AnimationCurve\" {{\n\t\tCount: {w.curves}\n\t}}\n}}\n" +
                   "Objects:  {\n" + w.objects + "}\n" +
                   "Connections:  {\n" + w.links + "}\n" +
                   $"Takes:  {{\n\tCurrent: \"{takes[0].name}\"\n" + info + "}\n";
        }

        static string F(float v) => v.ToString(CultureInfo.InvariantCulture);

        static string Ticks(float seconds) => ((long)System.Math.Round((double)seconds * Second)).ToString(CultureInfo.InvariantCulture);

        static void Model(Writer w, long id, string name, string kind, long parent, string properties)
        {
            w.models++;
            w.objects.Append($"\tModel: {id}, \"Model::{name}\", \"{kind}\" {{\n\t\tVersion: 232\n\t\tProperties70:  {{\n{properties}\t\t}}\n" +
                             "\t\tShading: T\n\t\tCulling: \"CullingOff\"\n\t}\n");
            w.links.Append($"\tC: \"OO\",{id},{parent}\n");
        }

        static void Null(Writer w, long id, string name, long parent, string extra = "") => Model(w, id, name, "Null", parent, extra);

        static void Proxy(Writer w, long id, string name, long parent)
            => Model(w, id, name, "Null", parent, "\t\t\tP: \"curveProxy\", \"bool\", \"\", \"U\",1\n");

        // One quad per material, side by side, the node at its own place along X.
        static void Mesh(Writer w, long id, string name, long parent, params string[] materials)
        {
            int quads = System.Math.Max(1, materials.Length);
            float x = (id / 1000) * 300f;
            Model(w, id, name, "Mesh", parent, $"\t\t\tP: \"Lcl Translation\", \"Lcl Translation\", \"\", \"A\",{F(x)},0,0\n");
            var vertices = new List<string>();
            var indices = new List<string>();
            var normals = new List<string>();
            var perPolygon = new List<string>();
            float[] corners = { 0, 0, 1, 0, 1, 1, 0, 1 };
            for (int q = 0; q < quads; q++)
            {
                for (int c = 0; c < 4; c++)
                {
                    vertices.Add(F((q * 1.5f + corners[c * 2]) * 100f));
                    vertices.Add(F(corners[c * 2 + 1] * 100f));
                    vertices.Add("0");
                    int v = q * 4 + c;
                    indices.Add((c == 3 ? -(v + 1) : v).ToString(CultureInfo.InvariantCulture));
                    normals.Add("0,0,1");
                }
                perPolygon.Add(q.ToString(CultureInfo.InvariantCulture));
            }
            long geometry = id + 100;
            w.geometries++;
            w.objects.Append(
                $"\tGeometry: {geometry}, \"Geometry::{name}\", \"Mesh\" {{\n" +
                $"\t\tVertices: *{vertices.Count} {{\n\t\t\ta: {string.Join(",", vertices)}\n\t\t}}\n" +
                $"\t\tPolygonVertexIndex: *{indices.Count} {{\n\t\t\ta: {string.Join(",", indices)}\n\t\t}}\n" +
                "\t\tGeometryVersion: 124\n" +
                "\t\tLayerElementNormal: 0 {\n\t\t\tVersion: 102\n\t\t\tName: \"\"\n" +
                "\t\t\tMappingInformationType: \"ByPolygonVertex\"\n\t\t\tReferenceInformationType: \"Direct\"\n" +
                $"\t\t\tNormals: *{normals.Count * 3} {{\n\t\t\t\ta: {string.Join(",", normals)}\n\t\t\t}}\n\t\t}}\n" +
                "\t\tLayerElementMaterial: 0 {\n\t\t\tVersion: 101\n\t\t\tName: \"\"\n" +
                "\t\t\tMappingInformationType: \"ByPolygon\"\n\t\t\tReferenceInformationType: \"IndexToDirect\"\n" +
                $"\t\t\tMaterials: *{perPolygon.Count} {{\n\t\t\t\ta: {string.Join(",", perPolygon)}\n\t\t\t}}\n\t\t}}\n" +
                "\t\tLayer: 0 {\n\t\t\tVersion: 100\n" +
                "\t\t\tLayerElement:  {\n\t\t\t\tType: \"LayerElementNormal\"\n\t\t\t\tTypedIndex: 0\n\t\t\t}\n" +
                "\t\t\tLayerElement:  {\n\t\t\t\tType: \"LayerElementMaterial\"\n\t\t\t\tTypedIndex: 0\n\t\t\t}\n\t\t}\n\t}\n");
            w.links.Append($"\tC: \"OO\",{geometry},{id}\n");
            for (int i = 0; i < materials.Length; i++)
            {
                if (materials[i] == null) continue;
                long material = id + 200 + i;
                w.materials++;
                w.objects.Append($"\tMaterial: {material}, \"Material::{materials[i]}\", \"\" {{\n\t\tVersion: 102\n" +
                                 "\t\tShadingModel: \"phong\"\n\t\tMultiLayer: 0\n\t\tProperties70:  {\n" +
                                 $"\t\t\tP: \"DiffuseColor\", \"Color\", \"\", \"A\",{F(0.2f + 0.3f * i)},{F(0.8f - 0.3f * i)},0.4\n" +
                                 "\t\t}\n\t}\n");
                w.links.Append($"\tC: \"OO\",{material},{id}\n");
            }
        }

        // A take: its stack and layer, and per curve a curve node on its model's property.
        static void Take(Writer w, int index, string name, float start, float stop,
                         (long model, string property, string node, string channel, int flags, (float t, float v)[] keys)[] curves)
        {
            long stack = 100000 * (index + 1), layer = stack + 1;
            w.stacks++;
            w.layers++;
            w.objects.Append($"\tAnimationStack: {stack}, \"AnimStack::{name}\", \"\" {{\n\t\tProperties70:  {{\n" +
                             $"\t\t\tP: \"LocalStart\", \"KTime\", \"Time\", \"\",{Ticks(start)}\n" +
                             $"\t\t\tP: \"LocalStop\", \"KTime\", \"Time\", \"\",{Ticks(stop)}\n" +
                             $"\t\t\tP: \"ReferenceStart\", \"KTime\", \"Time\", \"\",{Ticks(start)}\n" +
                             $"\t\t\tP: \"ReferenceStop\", \"KTime\", \"Time\", \"\",{Ticks(stop)}\n\t\t}}\n\t}}\n");
            w.objects.Append($"\tAnimationLayer: {layer}, \"AnimLayer::BaseLayer\", \"\" {{\n\t}}\n");
            w.links.Append($"\tC: \"OO\",{layer},{stack}\n");
            for (int c = 0; c < curves.Length; c++)
            {
                var (model, property, node, channel, flags, keys) = curves[c];
                long curveNode = stack + 10 + c * 10, curve = curveNode + 1;
                w.nodes++;
                w.curves++;
                string first = F(keys[0].v);
                string defaults = channel == "X"
                    ? $"\t\t\tP: \"d|X\", \"Number\", \"\", \"A\",{first}\n\t\t\tP: \"d|Y\", \"Number\", \"\", \"A\",1\n\t\t\tP: \"d|Z\", \"Number\", \"\", \"A\",1\n"
                    : $"\t\t\tP: \"d|{channel}\", \"Visibility\", \"\", \"A\",{first}\n";
                w.objects.Append($"\tAnimationCurveNode: {curveNode}, \"AnimCurveNode::{node}\", \"\" {{\n\t\tProperties70:  {{\n" +
                                 defaults + "\t\t}\n\t}\n");
                var times = new List<string>();
                var values = new List<string>();
                foreach (var (t, v) in keys)
                {
                    times.Add(Ticks(t));
                    values.Add(F(v));
                }
                w.objects.Append($"\tAnimationCurve: {curve}, \"AnimCurve::\", \"\" {{\n\t\tDefault: {first}\n\t\tKeyVer: 4009\n" +
                                 $"\t\tKeyTime: *{times.Count} {{\n\t\t\ta: {string.Join(",", times)}\n\t\t}}\n" +
                                 $"\t\tKeyValueFloat: *{values.Count} {{\n\t\t\ta: {string.Join(",", values)}\n\t\t}}\n" +
                                 $"\t\tKeyAttrFlags: *1 {{\n\t\t\ta: {flags}\n\t\t}}\n" +
                                 "\t\tKeyAttrDataFloat: *4 {\n\t\t\ta: 0,0,218434821,0\n\t\t}\n" +
                                 $"\t\tKeyAttrRefCount: *1 {{\n\t\t\ta: {keys.Length}\n\t\t}}\n\t}}\n");
                w.links.Append($"\tC: \"OO\",{curveNode},{layer}\n\tC: \"OP\",{curveNode},{model}, \"{property}\"\n" +
                               $"\tC: \"OP\",{curve},{curveNode}, \"d|{channel}\"\n");
            }
        }

        static string TakeInfo(string name, float start, float stop)
            => $"\tTake: \"{name}\" {{\n\t\tFileName: \"{name}.tak\"\n" +
               $"\t\tLocalTime: {Ticks(start)},{Ticks(stop)}\n\t\tReferenceTime: {Ticks(start)},{Ticks(stop)}\n\t}}\n";
    }
}
