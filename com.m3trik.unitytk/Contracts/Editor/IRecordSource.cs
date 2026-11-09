using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEngine;

namespace M3trik.Unitytk.Contracts
{
    /// <summary>
    /// Where a model's scene records come from: the <c>data_export</c> carrier a
    /// DCC writes. An importer collects a record off the nodes it is offered
    /// (<see cref="TryGetRecord"/>, <see cref="Collect"/>, by the carrier
    /// precedence every reader shares) and reads it through its generated shape
    /// (<see cref="Read{T}"/>); an action reads a model's record off its file
    /// (<see cref="Record"/>). The carrier feature provides it; a test passes
    /// <see cref="PureRecordSource"/>.
    /// </summary>
    /// <remarks>
    /// Prior art: Unity's <c>AssetPostprocessor</c> hands user properties to each
    /// importer separately, with no shared reader or precedence; the contract
    /// keeps the carrier's one precedence rule in one provider every importer
    /// reads through, as the code standard's declared needs ask.
    /// </remarks>
    public interface IRecordSource
    {
        /// <summary>Whether a node's user properties hold the record <paramref name="key"/>, and its JSON.</summary>
        bool TryGetRecord(string[] propertyNames, object[] values, string key, out string json);

        /// <summary>
        /// Keep a node's record <paramref name="json"/> for the model at
        /// <paramref name="assetPath"/> in <paramref name="pending"/>, by the carrier
        /// precedence (the scene's own carrier first, else the first met); a node
        /// that is no carrier is never read.
        /// </summary>
        void Collect(Dictionary<string, string> pending, string assetPath, GameObject node, string json);

        /// <summary>
        /// The record <paramref name="json"/> read through its shape at
        /// <paramref name="current"/>, or null with the refusal reported (an import's
        /// warning naming <paramref name="assetPath"/>).
        /// </summary>
        T Read<T>(string key, string json, int current, Func<object, T> read, string tag, string assetPath) where T : class;

        /// <summary>The record <paramref name="key"/> the model at <paramref name="assetPath"/> carries (its JSON), or null.</summary>
        string Record(string assetPath, string key);
    }

    /// <summary>
    /// The stand-in: records a test hands it, by model and key, read with the
    /// core's one reader (<see cref="UnitytkRecords.Read{T}"/>); every node offered
    /// counts as a carrier, and the first record collected wins.
    /// </summary>
    [Provides(typeof(IRecordSource), StandIn = true)]
    public sealed class PureRecordSource : IRecordSource
    {
        readonly Dictionary<(string, string), string> _records = new Dictionary<(string, string), string>();

        /// <summary>Hold <paramref name="json"/> as the record <paramref name="key"/> of the model at <paramref name="assetPath"/>.</summary>
        public PureRecordSource With(string assetPath, string key, string json)
        {
            _records[(assetPath, key)] = json;
            return this;
        }

        /// <summary>The refusals <see cref="Read{T}"/> met, in order.</summary>
        public List<string> Refusals { get; } = new List<string>();

        /// <inheritdoc/>
        public bool TryGetRecord(string[] propertyNames, object[] values, string key, out string json)
            => UnitytkRecords.TryGetProperty(propertyNames, values, key, out json);

        /// <inheritdoc/>
        public void Collect(Dictionary<string, string> pending, string assetPath, GameObject node, string json)
        {
            if (!pending.ContainsKey(assetPath)) pending[assetPath] = json;
        }

        /// <inheritdoc/>
        public T Read<T>(string key, string json, int current, Func<object, T> read, string tag, string assetPath) where T : class
        {
            T record = UnitytkRecords.Read(key, json, current, read, assetPath, out string refusal);
            if (refusal != null) Refusals.Add(refusal);
            return record;
        }

        /// <inheritdoc/>
        public string Record(string assetPath, string key) => _records.TryGetValue((assetPath, key), out string json) ? json : null;
    }
}
