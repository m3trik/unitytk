// ----------------------------------------------------------------------------
// Part of the unitytk core's contracts (M3trik.Unitytk.Contracts): a record two
// features read -- the Looks importer its renderer section, the Lightmaps importer
// its environment, key light and lightmapped materials' sections -- and a feature
// compiles against the core alone.
//
// The record's types are generated beside this file (HandoffBlock.g.cs, from
// pythontk's declaration, with CurrentVersion, the one version read; its lighting
// recipe's names and policy values in HandoffRendering.g.cs); this half is the
// reader's policy: a setting a file leaves out takes the policy's value.
// ----------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Globalization;

namespace M3trik.Unitytk.Contracts
{
    /// <summary>
    /// The <c>handoff</c> record: its generated shape (<c>HandoffBlock.g.cs</c>) and this reader's policy -- a
    /// lighting setting read off its <c>rendering</c> section, the policy's value (<c>HandoffRendering</c>) where
    /// the file states none.
    /// </summary>
    /// <remarks>
    /// Prior art: none to adopt: the block is the hand-off's own (pythontk's <c>HandoffRecord</c>), read by its
    /// generated reader as every record is (<see cref="UnitytkRecords.Read{T}"/>).
    /// </remarks>
    public static partial class HandoffBlock
    {
        /// <summary>
        /// The record <paramref name="json"/> holds, read the one way a record is read (its generated shape, at
        /// <see cref="CurrentVersion"/> alone, under its carrier key <see cref="UnitytkRecords.Handoff"/>); null when
        /// <paramref name="json"/> is empty or the record is refused, with why in <paramref name="refusal"/>.
        /// </summary>
        /// <param name="where">The file, for the refusal.</param>
        public static Shape.Payload Read(string json, string where, out string refusal)
            => UnitytkRecords.Read(UnitytkRecords.Handoff, json, CurrentVersion, Shape.Payload.Read, where, out refusal);

        /// <summary>Whether <paramref name="record"/> states the section <paramref name="section"/> of its lighting.</summary>
        public static bool States(Shape.Payload record, string section) => Section(record, section) != null;

        /// <summary>
        /// The name <paramref name="record"/> states for <paramref name="setting"/> in its lighting's
        /// <paramref name="section"/>; <paramref name="policy"/> where it states none (or no string).
        /// </summary>
        public static string Text(Shape.Payload record, string section, string setting, string policy)
            => Section(record, section) is IDictionary<string, object> s && s.TryGetValue(setting, out object value) && value is string text
                ? text : policy;

        /// <summary>
        /// The number <paramref name="record"/> states for <paramref name="setting"/> in its lighting's
        /// <paramref name="section"/>; <paramref name="policy"/> where it states none (or no number).
        /// </summary>
        public static float Number(Shape.Payload record, string section, string setting, float policy)
            => Section(record, section) is IDictionary<string, object> s && s.TryGetValue(setting, out object value)
               && value is IConvertible number && !(value is string) && !(value is bool)
                ? Convert.ToSingle(number, CultureInfo.InvariantCulture) : policy;

        static IDictionary<string, object> Section(Shape.Payload record, string section)
            => record?.rendering != null && record.rendering.TryGetValue(section, out object found) ? found as IDictionary<string, object> : null;
    }
}
