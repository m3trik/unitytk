using System.Globalization;
using System.Text;

namespace M3trik.Unitytk.Testing
{
    /// <summary>
    /// A model as a DCC delivers one: a node, <c>Box</c>, keyed over two takes, one per shot --
    /// Intro on 0-2 s and Outro on 2-4 s of the timeline -- and the scene's <c>data_export</c>
    /// carrier holding one record. ASCII FBX 7.4, which Unity's importer reads as it reads a
    /// binary one.
    /// </summary>
    public static class TwoTakeFbx
    {
        const long Second = 46186158000L;  // FBX time units in a second

        /// <summary>
        /// The file, its carrier holding <paramref name="record"/> under the channel
        /// <paramref name="channel"/> (<c>animation</c>, <c>audio</c>), its unit
        /// <paramref name="unitScaleFactor"/> centimetres (1: a centimetre file, 100: a metre one).
        /// </summary>
        public static string Write(string channel, string record, double unitScaleFactor = 1.0)
        {
            var objects = new StringBuilder();
            var links = new StringBuilder();
            objects.Append(Node(1000, "Box")).Append(Node(1001, "data_export",
                $"\t\t\tP: \"{channel}\", \"KString\", \"\", \"U\", \"{Escape(record)}\"\n"));
            links.Append("\tC: \"OO\",1000,0\n\tC: \"OO\",1001,0\n");
            Stack(objects, links, 2000, "Intro", 0, 2, 0f, 1f);
            Stack(objects, links, 3000, "Outro", 2, 4, 1f, 3f);
            string unit = unitScaleFactor.ToString(CultureInfo.InvariantCulture);
            return "; FBX 7.4.0 project file\n" +
                   "FBXHeaderExtension:  {\n\tFBXHeaderVersion: 1003\n\tFBXVersion: 7400\n\tCreator: \"unitytk tests\"\n}\n" +
                   "GlobalSettings:  {\n\tVersion: 1000\n\tProperties70:  {\n" +
                   "\t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1\n\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1\n" +
                   "\t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2\n\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1\n" +
                   "\t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0\n\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1\n" +
                   $"\t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",{unit}\n" +
                   "\t\tP: \"TimeMode\", \"enum\", \"\", \"\",6\n" +
                   $"\t\tP: \"TimeSpanStart\", \"KTime\", \"Time\", \"\",0\n\t\tP: \"TimeSpanStop\", \"KTime\", \"Time\", \"\",{4 * Second}\n" +
                   "\t}\n}\n" +
                   "Definitions:  {\n\tVersion: 100\n\tCount: 13\n" +
                   "\tObjectType: \"GlobalSettings\" {\n\t\tCount: 1\n\t}\n" +
                   "\tObjectType: \"Model\" {\n\t\tCount: 2\n\t}\n" +
                   "\tObjectType: \"AnimationStack\" {\n\t\tCount: 2\n\t}\n" +
                   "\tObjectType: \"AnimationLayer\" {\n\t\tCount: 2\n\t}\n" +
                   "\tObjectType: \"AnimationCurveNode\" {\n\t\tCount: 2\n\t}\n" +
                   "\tObjectType: \"AnimationCurve\" {\n\t\tCount: 2\n\t}\n}\n" +
                   "Objects:  {\n" + objects + "}\n" +
                   "Connections:  {\n" + links + "}\n" +
                   "Takes:  {\n\tCurrent: \"Intro\"\n" + Take("Intro", 0, 2) + Take("Outro", 2, 4) + "}\n";
        }

        // ASCII FBX quotes a string's own double quotes as &quot;.
        static string Escape(string text) => text.Replace("\"", "&quot;");

        static string Node(long id, string name, string properties = "")
            => $"\tModel: {id}, \"Model::{name}\", \"Null\" {{\n\t\tVersion: 232\n\t\tProperties70:  {{\n{properties}\t\t}}\n" +
               "\t\tShading: Y\n\t\tCulling: \"CullingOff\"\n\t}\n";

        // A take: its stack and layer, and the box's X translation keyed from one value to another over it.
        static void Stack(StringBuilder objects, StringBuilder links, long id, string take, int start, int stop,
                          float from, float to)
        {
            string Ticks(int seconds) => (seconds * Second).ToString(CultureInfo.InvariantCulture);
            string F(float v) => v.ToString(CultureInfo.InvariantCulture);
            objects.Append($"\tAnimationStack: {id}, \"AnimStack::{take}\", \"\" {{\n\t\tProperties70:  {{\n" +
                           $"\t\t\tP: \"LocalStart\", \"KTime\", \"Time\", \"\",{Ticks(start)}\n" +
                           $"\t\t\tP: \"LocalStop\", \"KTime\", \"Time\", \"\",{Ticks(stop)}\n" +
                           $"\t\t\tP: \"ReferenceStart\", \"KTime\", \"Time\", \"\",{Ticks(start)}\n" +
                           $"\t\t\tP: \"ReferenceStop\", \"KTime\", \"Time\", \"\",{Ticks(stop)}\n\t\t}}\n\t}}\n");
            objects.Append($"\tAnimationLayer: {id + 1}, \"AnimLayer::BaseLayer\", \"\" {{\n\t}}\n");
            objects.Append($"\tAnimationCurveNode: {id + 2}, \"AnimCurveNode::T\", \"\" {{\n\t\tProperties70:  {{\n" +
                           $"\t\t\tP: \"d|X\", \"Number\", \"\", \"A\",{F(from)}\n" +
                           "\t\t\tP: \"d|Y\", \"Number\", \"\", \"A\",0\n\t\t\tP: \"d|Z\", \"Number\", \"\", \"A\",0\n\t\t}\n\t}\n");
            objects.Append($"\tAnimationCurve: {id + 3}, \"AnimCurve::\", \"\" {{\n\t\tDefault: {F(from)}\n\t\tKeyVer: 4009\n" +
                           $"\t\tKeyTime: *2 {{\n\t\t\ta: {Ticks(start)},{Ticks(stop)}\n\t\t}}\n" +
                           $"\t\tKeyValueFloat: *2 {{\n\t\t\ta: {F(from)},{F(to)}\n\t\t}}\n" +
                           "\t\tKeyAttrFlags: *1 {\n\t\t\ta: 24836\n\t\t}\n" +
                           "\t\tKeyAttrDataFloat: *4 {\n\t\t\ta: 0,0,218434821,0\n\t\t}\n" +
                           "\t\tKeyAttrRefCount: *1 {\n\t\t\ta: 2\n\t\t}\n\t}\n");
            links.Append($"\tC: \"OO\",{id + 1},{id}\n\tC: \"OO\",{id + 2},{id + 1}\n" +
                         $"\tC: \"OP\",{id + 2},1000, \"Lcl Translation\"\n\tC: \"OP\",{id + 3},{id + 2}, \"d|X\"\n");
        }

        static string Take(string name, int start, int stop)
            => $"\tTake: \"{name}\" {{\n\t\tFileName: \"{name}.tak\"\n" +
               $"\t\tLocalTime: {start * Second},{stop * Second}\n\t\tReferenceTime: {start * Second},{stop * Second}\n\t}}\n";
    }
}
