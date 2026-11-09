using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// An operation's result as a JSON tree (<see cref="M3trik.Unitytk.Core.Json.Write"/>
    /// writes it): a Unity object by its name, a vector or colour as its numbers,
    /// a sequence or dictionary item by item, anything else by its text -- as
    /// pythontk hands a Python result back.
    /// </summary>
    internal static class JsonValue
    {
        public static object From(object value) => From(value, 0);

        static object From(object value, int depth)
        {
            if (depth > 32) return "...";
            switch (value)
            {
                case null: return null;
                case string _:
                case bool _:
                case double _:
                    return value;
                case float f: return (double)f;
                case Enum e: return e.ToString();
                case UnityEngine.Object unityObject: return unityObject == null ? null : unityObject.name;
                case Vector2 v: return new List<object> { (double)v.x, (double)v.y };
                case Vector3 v: return new List<object> { (double)v.x, (double)v.y, (double)v.z };
                case Vector4 v: return new List<object> { (double)v.x, (double)v.y, (double)v.z, (double)v.w };
                case Quaternion q: return new List<object> { (double)q.x, (double)q.y, (double)q.z, (double)q.w };
                case Color c: return new List<object> { (double)c.r, (double)c.g, (double)c.b, (double)c.a };
                case IDictionary dict:
                    var map = new Dictionary<string, object>();
                    foreach (DictionaryEntry entry in dict)
                        map[System.Convert.ToString(entry.Key, CultureInfo.InvariantCulture)] = From(entry.Value, depth + 1);
                    return map;
                case IEnumerable items:
                    var list = new List<object>();
                    foreach (object item in items) list.Add(From(item, depth + 1));
                    return list;
                case IConvertible number when number.GetTypeCode() >= TypeCode.SByte && number.GetTypeCode() <= TypeCode.Decimal:
                    return value;
                default:
                    return value.ToString();
            }
        }
    }
}
