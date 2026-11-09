using System;
using System.Collections.Generic;
using System.Reflection;

namespace M3trik.Unitytk.Core
{
    /// <summary>
    /// Declares, once per reader, that an assembly reads the scene record
    /// <see cref="Key"/> through the generated shape <see cref="Shape"/> (its
    /// <c>CurrentVersion</c> is the version read). The records audit lists every
    /// file whose record of that key another version holds.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class ReadsRecordAttribute : Attribute
    {
        /// <param name="key">The record's key (a <c>UnitytkRecords</c> constant).</param>
        /// <param name="shape">Its generated reader (<c>&lt;Shape&gt;.g.cs</c>), which declares <c>CurrentVersion</c>.</param>
        public ReadsRecordAttribute(string key, Type shape)
        {
            Key = key;
            Shape = shape;
        }

        /// <summary>The record's key.</summary>
        public string Key { get; }

        /// <summary>Its generated reader.</summary>
        public Type Shape { get; }

        /// <summary>The version the reader reads: its shape's <c>CurrentVersion</c>, or -1 when it declares none.</summary>
        public int Version
        {
            get
            {
                FieldInfo field = Shape?.GetField("CurrentVersion", BindingFlags.Public | BindingFlags.Static);
                return field != null && field.IsLiteral && field.GetRawConstantValue() is int v ? v : -1;
            }
        }

        /// <summary>Every record the loaded assemblies read, by key: the version each reads.</summary>
        public static SortedDictionary<string, int> Read(IEnumerable<Assembly> assemblies)
        {
            var read = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (Assembly assembly in assemblies)
                foreach (ReadsRecordAttribute reads in assembly.GetCustomAttributes<ReadsRecordAttribute>())
                    read[reads.Key] = reads.Version;
            return read;
        }
    }
}
