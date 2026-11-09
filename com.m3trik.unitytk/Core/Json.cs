using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// The JSON every unitytk part reads and writes: <see cref="Parse(string)"/> gives
    /// the tree the generated readers take (an object as <c>Dictionary&lt;string,
    /// object&gt;</c>, an array as <c>List&lt;object&gt;</c>, an integer as
    /// <c>long</c> -- <c>decimal</c> past a long, so no digit is lost -- any other
    /// number as <c>double</c>, plus strings, booleans, <c>null</c> and the NaN /
    /// Infinity Python writes), and <see cref="Write(object)"/> writes such a tree
    /// back, as Python's <c>json</c> reads it: every value reads back the same.
    /// </summary>
    /// <remarks>
    /// Prior art: JsonUtility cannot say null (a channel with no limit, a probe
    /// with no box) and reads only into serializable classes; Newtonsoft.Json is a
    /// package dependency a player build would carry. A parser this small, with no
    /// engine reference, runs in the editor, a player and <c>dotnet test</c> alike.
    /// </remarks>
    public static class Json
    {
        /// <summary>Parse <paramref name="text"/>, an integer as <c>long</c> (or <c>decimal</c>).</summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        /// <exception cref="FormatException">The text is not JSON.</exception>
        public static object Parse(string text) => Parse(text, true);

        /// <summary>
        /// Parse <paramref name="text"/>; with <paramref name="exactIntegers"/> false every
        /// number is a <c>double</c>, as the scene records' readers (<c>UnitytkRecords.Read</c>) take them.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
        /// <exception cref="FormatException">The text is not JSON.</exception>
        public static object Parse(string text, bool exactIntegers)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            int i = 0;
            object value = ReadValue(text, ref i, exactIntegers);
            SkipSpace(text, ref i);
            if (i != text.Length) throw Error(i, "trailing characters");
            return value;
        }

        /// <summary>
        /// A number as the shortest text that reads back as the same double, with
        /// <c>.0</c> on an integral one, as Python's <c>repr</c> writes it (an exponent
        /// is spelled <c>E+16</c> where Python writes <c>e+16</c>; both read alike).
        /// </summary>
        public static string Number(double value)
        {
            if (double.IsNaN(value)) return "NaN";
            if (double.IsPositiveInfinity(value)) return "Infinity";
            if (double.IsNegativeInfinity(value)) return "-Infinity";
            // "R" is not always round-trip on the .NET Framework profile Unity runs, so
            // take the fewest of 15-17 significant digits that read back the same.
            string text = value.ToString("G15", CultureInfo.InvariantCulture);
            for (int digits = 16; digits <= 17 && Read(text) != value; digits++)
                text = value.ToString("G" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
            return text.IndexOfAny(new[] { '.', 'E', 'e' }) < 0 ? text + ".0" : text;
        }

        static double Read(string number) => double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);

        /// <summary>
        /// <paramref name="value"/> as JSON text: a dictionary with string keys as an
        /// object, any other sequence as an array, numbers (an integer type as an
        /// integer, a float as <see cref="Number"/> writes it), strings, booleans, enums
        /// (by name) and null.
        /// </summary>
        /// <exception cref="FormatException">It holds a value no JSON writes (name the value's type).</exception>
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0, 0);
            return sb.ToString();
        }

        /// <summary>
        /// <paramref name="value"/> as indented JSON text, laid out as Python's
        /// <c>json.dumps(value, indent=indent)</c> lays it out (the same breaks, spaces
        /// and escapes, non-ASCII escaped): a tree of strings, integers, booleans and
        /// nulls (an operation catalog) is the same bytes either side writes; a float
        /// may be spelled otherwise (<see cref="Number"/>) but reads back the same.
        /// </summary>
        /// <exception cref="FormatException">It holds a value no JSON writes.</exception>
        public static string Write(object value, int indent)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, Math.Max(0, indent), 0);
            return sb.ToString();
        }

        static FormatException Error(int at, string what)
            => new FormatException($"Invalid JSON at {at}: {what}.");

        static void SkipSpace(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++;
        }

        static object ReadValue(string s, ref int i, bool exact)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length) throw Error(i, "unexpected end");
            char c = s[i];
            switch (c)
            {
                case '{': return ReadObject(s, ref i, exact);
                case '[': return ReadArray(s, ref i, exact);
                case '"': return ReadString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                case 'N': Expect(s, ref i, "NaN"); return double.NaN;
                case 'I': Expect(s, ref i, "Infinity"); return double.PositiveInfinity;
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(s, ref i, exact);
                    throw Error(i, $"unexpected '{c}'");
            }
        }

        static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error(i, $"expected {word}");
            i += word.Length;
        }

        static Dictionary<string, object> ReadObject(string s, ref int i, bool exact)
        {
            var obj = new Dictionary<string, object>();
            i++;  // {
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }
            while (true)
            {
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw Error(i, "expected a key");
                string key = ReadString(s, ref i);
                SkipSpace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw Error(i, "expected ':'");
                i++;
                obj[key] = ReadValue(s, ref i, exact);
                SkipSpace(s, ref i);
                if (i >= s.Length) throw Error(i, "unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return obj; }
                throw Error(i, "expected ',' or '}'");
            }
        }

        static List<object> ReadArray(string s, ref int i, bool exact)
        {
            var list = new List<object>();
            i++;  // [
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ReadValue(s, ref i, exact));
                SkipSpace(s, ref i);
                if (i >= s.Length) throw Error(i, "unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw Error(i, "expected ',' or ']'");
            }
        }

        static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;  // opening quote
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw Error(i, "short \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw Error(i, $"bad escape '\\{e}'");
                }
            }
            throw Error(i, "unterminated string");
        }

        static object ReadNumber(string s, ref int i, bool exact)
        {
            int start = i;
            if (s[i] == '-')
            {
                i++;
                if (i < s.Length && s[i] == 'I') { Expect(s, ref i, "Infinity"); return double.NegativeInfinity; }
            }
            bool integer = true;
            while (i < s.Length)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') i++;
                else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { integer = false; i++; }
                else break;
            }
            string token = s.Substring(start, i - start);
            if (exact && integer)
            {
                // An integer stays one: a long, else a decimal (28 digits), else a double.
                if (long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long whole))
                    return whole;
                if (decimal.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal wide))
                    return wide;
            }
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw Error(start, $"bad number '{token}'");
            return value;
        }

        static void WriteValue(StringBuilder sb, object value, int indent, int depth)
        {
            switch (value)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s, indent > 0); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case char c: WriteString(sb, c.ToString(), indent > 0); return;
                case double d: sb.Append(Number(d)); return;
                case float f: sb.Append(Number(f)); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
                case Enum e: WriteString(sb, e.ToString(), indent > 0); return;
                case IDictionary<string, object> map: WriteObject(sb, map, indent, depth); return;
                case IDictionary dict:
                    var keyed = new Dictionary<string, object>();
                    foreach (DictionaryEntry entry in dict)
                        keyed[Convert.ToString(entry.Key, CultureInfo.InvariantCulture)] = entry.Value;
                    WriteObject(sb, keyed, indent, depth);
                    return;
                case IEnumerable items:
                    var list = new List<object>();
                    foreach (object item in items) list.Add(item);
                    if (list.Count == 0)
                    {
                        sb.Append("[]");
                        return;
                    }
                    sb.Append('[');
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        NewLine(sb, indent, depth + 1);
                        WriteValue(sb, list[i], indent, depth + 1);
                    }
                    NewLine(sb, indent, depth);
                    sb.Append(']');
                    return;
                case IConvertible number when IsInteger(number):
                    sb.Append(Convert.ToString(number, CultureInfo.InvariantCulture));
                    return;
                default:
                    throw new FormatException(
                        $"{value.GetType().FullName} is no JSON value: give its name, number or a dictionary instead");
            }
        }

        static bool IsInteger(IConvertible value)
        {
            switch (value.GetTypeCode())
            {
                case TypeCode.SByte: case TypeCode.Byte: case TypeCode.Int16: case TypeCode.UInt16:
                case TypeCode.Int32: case TypeCode.UInt32: case TypeCode.Int64: case TypeCode.UInt64:
                    return true;
                default:
                    return false;
            }
        }

        static void WriteObject(StringBuilder sb, IDictionary<string, object> map, int indent, int depth)
        {
            if (map.Count == 0)
            {
                sb.Append("{}");
                return;
            }
            sb.Append('{');
            bool first = true;
            foreach (var pair in map)
            {
                if (!first) sb.Append(',');
                first = false;
                NewLine(sb, indent, depth + 1);
                WriteString(sb, pair.Key, indent > 0);
                sb.Append(indent > 0 ? ": " : ":");
                WriteValue(sb, pair.Value, indent, depth + 1);
            }
            NewLine(sb, indent, depth);
            sb.Append('}');
        }

        static void NewLine(StringBuilder sb, int indent, int depth)
        {
            if (indent <= 0) return;
            sb.Append('\n').Append(' ', indent * depth);
        }

        static void WriteString(StringBuilder sb, string s, bool asciiOnly)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20 || (asciiOnly && c > 0x7e))
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
