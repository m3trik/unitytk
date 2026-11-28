using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Manifests
{
    // A setting's or a part's value as the text the manifest keeps (Manifest.Value: an edit's, a build's record, an
    // adjustment), and back: the one codec every value it keeps is written and read by. Numbers invariant and exact, a
    // colour as its four numbers (exact and past 1, a glow's: a hex code would round and clamp it; a sheet's #RRGGBBAA still
    // reads), a vector as its numbers, a choice by its name. A kind of value it cannot hold reads as null and is never written.
    internal static class ManifestValues
    {
        static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        internal static string Read(SerializedProperty p)
        {
            if (p == null) return null;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float: return Text(p.floatValue);
                case SerializedPropertyType.Integer: return Text(p.intValue);
                case SerializedPropertyType.Boolean: return Text(p.boolValue);
                case SerializedPropertyType.String: return p.stringValue;
                case SerializedPropertyType.Enum:
                    return p.enumValueIndex >= 0 && p.enumValueIndex < p.enumNames.Length ? p.enumNames[p.enumValueIndex] : Text(p.intValue);
                case SerializedPropertyType.Color: return Text(p.colorValue);
                case SerializedPropertyType.Vector2: return Text(p.vector2Value);
                case SerializedPropertyType.Vector3: return Text(p.vector3Value);
                default: return null;
            }
        }

        internal static bool Write(SerializedProperty p, string value)
        {
            if (p == null || value == null) return false;
            object read;
            switch (p.propertyType)
            {
                case SerializedPropertyType.Float:
                    if (!Parse(typeof(float), value, out read)) return false;
                    p.floatValue = (float)read;
                    return true;
                case SerializedPropertyType.Integer:
                    if (!Parse(typeof(int), value, out read)) return false;
                    p.intValue = (int)read;
                    return true;
                case SerializedPropertyType.Boolean:
                    if (!Parse(typeof(bool), value, out read)) return false;
                    p.boolValue = (bool)read;
                    return true;
                case SerializedPropertyType.String:
                    p.stringValue = value;
                    return true;
                case SerializedPropertyType.Enum:
                    int index = Choice(p.enumNames, value);
                    if (index < 0 && !int.TryParse(value, NumberStyles.Integer, Invariant, out index)) return false;
                    p.enumValueIndex = index;
                    return true;
                case SerializedPropertyType.Color:
                    if (!Parse(typeof(Color), value, out read)) return false;
                    p.colorValue = (Color)read;
                    return true;
                case SerializedPropertyType.Vector2:
                    if (!Parse(typeof(Vector2), value, out read)) return false;
                    p.vector2Value = (Vector2)read;
                    return true;
                case SerializedPropertyType.Vector3:
                    if (!Parse(typeof(Vector3), value, out read)) return false;
                    p.vector3Value = (Vector3)read;
                    return true;
                default:
                    return false;
            }
        }

        // A plain value (a number, a flag, a text, a choice, a colour, a vector) as the text the manifest keeps; empty for none.
        internal static string Text(object value)
        {
            switch (value)
            {
                case null: return "";
                case float f: return f.ToString("R", Invariant);
                case int i: return i.ToString(Invariant);
                case bool b: return b ? "true" : "false";
                case string s: return s;
                case Color c: return Join(c.r, c.g, c.b, c.a);
                case Vector2 v: return Join(v.x, v.y);
                case Vector3 v: return Join(v.x, v.y, v.z);
                default: return value.ToString();  // a choice, by its name
            }
        }

        // text as a plain value of type (Text's kinds); false where it is none. A flag reads a sheet's words for it too (on,
        // off, yes, no), a choice its name as a sheet spells it (hold_to_play), a colour a hex code (#RRGGBBAA) or a name.
        internal static bool Parse(System.Type type, string text, out object value)
        {
            value = null;
            if (text == null || type == null) return false;
            if (type == typeof(string)) value = text;
            else if (type == typeof(float)) { if (float.TryParse(text, NumberStyles.Float, Invariant, out float f)) value = f; }
            else if (type == typeof(int)) { if (int.TryParse(text, NumberStyles.Integer, Invariant, out int i)) value = i; }
            else if (type == typeof(bool)) { if (Flag(text, out bool b)) value = b; }
            else if (type.IsEnum)
            {
                string[] names = System.Enum.GetNames(type);
                int index = Choice(names, text);
                if (index >= 0) value = System.Enum.Parse(type, names[index]);
            }
            else if (type == typeof(Color)) { if (Colour(text, out Color c)) value = c; }
            else if (type == typeof(Vector2)) { float[] two = Split(text, 2); if (two != null) value = new Vector2(two[0], two[1]); }
            else if (type == typeof(Vector3)) { float[] three = Split(text, 3); if (three != null) value = new Vector3(three[0], three[1], three[2]); }
            return value != null;
        }

        // A flag's text: true and false, and a sheet's words for a flag (on and off, yes and no), in any case.
        static bool Flag(string text, out bool on)
        {
            string flag = text.Trim().ToLowerInvariant();
            on = flag == "true" || flag == "on" || flag == "yes";
            return on || flag == "false" || flag == "off" || flag == "no";
        }

        // Which of names text names: by its name, else as a sheet writes it (lower case with underscores, hold_to_play); -1: none.
        static int Choice(string[] names, string text)
        {
            int index = System.Array.IndexOf(names, text);
            for (int n = 0; index < 0 && n < names.Length; n++)
                if (string.Equals(names[n], text.Replace("_", "").Replace(" ", ""), System.StringComparison.OrdinalIgnoreCase)) index = n;
            return index;
        }

        // A colour's text: its four numbers (r,g,b,a, as Text writes them), else an HTML colour (a sheet's #RRGGBBAA, a name).
        static bool Colour(string text, out Color colour)
        {
            float[] four = Split(text, 4);
            if (four != null)
            {
                colour = new Color(four[0], four[1], four[2], four[3]);
                return true;
            }
            return ColorUtility.TryParseHtmlString(text.Trim(), out colour);
        }

        // The value of the field named name on target, as text; null where it has none an edit can hold.
        internal static string Read(Object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name)) return null;
            using (var so = new SerializedObject(target)) return Read(so.FindProperty(name));
        }

        // The settings one object of a step is built by over its kit's, nearest last: what its sheet's row says (said, by its
        // kit's names: Said), what the manifest keeps as the object's own for every row of the behavior, then for this row
        // (place: its place among the step's rows in the sheet; -1: none of its own), each with what planned keeps over the
        // manifest's (a dry run's tunes, which it cannot write). Behavior's own alone (null: every behavior's): a row of the
        // step's own and a Use row's take, both kept under an empty target, are two rows, and one's speed is never the other's.
        internal static List<Manifest.Value> Over(Manifest manifest, string step, string target, int place, IEnumerable<Manifest.Value> said,
                                                  IEnumerable<Manifest.Tune> planned = null, string behavior = null)
        {
            var values = new List<Manifest.Value>();
            if (said != null) values.AddRange(said);
            if (manifest == null) return values;
            IEnumerable<Manifest.Tune> tunes = planned == null ? manifest.tunes : manifest.tunes.Concat(planned);
            foreach (int row in place >= 0 ? new[] { -1, place } : new[] { -1 })
                foreach (Manifest.Tune tune in tunes)
                    if (tune != null && tune.step == (step ?? "") && tune.target == (target ?? "") && tune.row == row && (behavior == null || tune.behavior == behavior))
                        values.AddRange(tune.values);
            return values;
        }

        // What a row's settings (by the sheet's names) are among type's (a kit's settings), as text: each by the name it has
        // there (PathOf: size on an indicate row, indicate.size, where type groups the behavior's settings). None for none.
        internal static List<Manifest.Value> Said(System.Type type, string behavior, IDictionary<string, object> rowSettings)
        {
            var said = new List<Manifest.Value>();
            if (rowSettings == null) return said;
            bool grouped = Group(type, behavior) != null;
            foreach (var pair in rowSettings)
                said.Add(new Manifest.Value { name = grouped ? behavior + "." + pair.Key : pair.Key, value = System.Convert.ToString(pair.Value, Invariant) });
            return said;
        }

        // The name a row's setting name (as the sheet names it) of a behavior row has among type's settings (a kit's): under
        // the behavior's group where type has one (size on an indicate row: indicate.size), else name as it is.
        internal static string PathOf(System.Type type, string behavior, string name)
            => Group(type, behavior) != null ? behavior + "." + name : name;

        // The member of type (a kit's settings) named as behavior that groups that behavior's settings: a field of a
        // [Serializable] class (indicate's size and colour, under indicate); null where type has none.
        internal static FieldInfo Group(System.Type type, string behavior)
        {
            if (type == null || string.IsNullOrEmpty(behavior)) return null;
            FieldInfo field = type.GetField(behavior, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || !Serialized(field)) return null;
            System.Type of = field.FieldType;
            return of.IsClass && of.IsSerializable && of != typeof(string) && !of.IsArray && !of.IsGenericType && !typeof(Object).IsAssignableFrom(of)
                ? field : null;
        }

        // The settings behavior's group in type holds (Group), each by its path (indicate.size), in the order Unity serializes
        // them (a base class's first); null where type has no group of it.
        internal static string[] GroupSettings(System.Type type, string behavior)
        {
            FieldInfo group = Group(type, behavior);
            if (group == null) return null;
            var chain = new List<System.Type>();
            for (System.Type each = group.FieldType; each != null && each != typeof(object); each = each.BaseType) chain.Insert(0, each);
            var names = new List<string>();
            foreach (System.Type each in chain)
                foreach (FieldInfo field in each.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (Serialized(field) && !field.IsDefined(typeof(HideInInspector), false)) names.Add(behavior + "." + field.Name);
            return names.ToArray();
        }

        // Whether Unity serializes field (an instance field, public or marked SerializeField, not NonSerialized).
        static bool Serialized(FieldInfo field)
            => !field.IsStatic && !field.IsNotSerialized && (field.IsPublic || field.IsDefined(typeof(SerializeField), false));

        // Write values onto settings; returns the names it holds no field of (each left out).
        internal static List<string> WriteAll(ScriptableObject settings, IEnumerable<Manifest.Value> values)
        {
            var unknown = new List<string>();
            using (var so = new SerializedObject(settings))
            {
                foreach (Manifest.Value value in values)
                {
                    if (value == null) continue;
                    SerializedProperty property = so.FindProperty(value.name);
                    if (property == null || !Write(property, value.value))
                        if (!unknown.Contains(value.name)) unknown.Add(value.name);
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return unknown;
        }

        // Whether type (a kit's settings) has a serialized field named name.
        internal static bool HasSetting(System.Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name)) return false;
            ScriptableObject probe = ScriptableObject.CreateInstance(type);
            try
            {
                using (var so = new SerializedObject(probe)) return so.FindProperty(name) != null;
            }
            finally
            {
                Object.DestroyImmediate(probe);
            }
        }

        static string Join(params float[] values)
        {
            var parts = new string[values.Length];
            for (int i = 0; i < values.Length; i++) parts[i] = values[i].ToString("R", Invariant);
            return string.Join(",", parts);
        }

        static float[] Split(string value, int count)
        {
            string[] parts = value.Split(',');
            if (parts.Length != count) return null;
            var read = new float[count];
            for (int i = 0; i < count; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, Invariant, out read[i])) return null;
            return read;
        }
    }
}
