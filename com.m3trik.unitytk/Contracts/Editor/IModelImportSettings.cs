using System;
using System.Collections.Generic;
using M3trik.Unitytk.Kernel;
using UnityEditor;

namespace M3trik.Unitytk.Contracts
{
    /// <summary>
    /// The model importer settings a channel needs of one FBX, asked for during its
    /// import and applied after the import batch, with one reimport of that model
    /// alone: an importer that finds its channel in a model asks here, so an FBX
    /// without it keeps its own settings, and nothing is ever switched off. The
    /// carrier feature provides it, beside the embedded media it extracts in the
    /// same reimport (<see cref="IModelMedia.RequestEmbeddedFiles"/>); a test
    /// passes <see cref="PureModelImportSettings"/>.
    /// </summary>
    /// <remarks>
    /// Its own contract, not the media's or the records': those say what a model
    /// carries and where its files lie, this what Unity's model importer must do
    /// with it. Prior art: <c>AssetPostprocessor.OnPreprocessModel</c> may set a
    /// model's importer settings, but a channel is only known later in the import
    /// (its user properties reach <c>OnPostprocessGameObjectWithUserProperties</c>),
    /// and changing a setting there takes the next import; Unity's documented
    /// place to reimport is <c>OnPostprocessAllAssets</c>, where the provider
    /// applies what was asked.
    /// </remarks>
    public interface IModelImportSettings
    {
        /// <summary>
        /// Ask that the model at <paramref name="assetPath"/> import its curves as
        /// keyed (Resample Curves off): a fade window lives between its keys, which
        /// resampling fills.
        /// </summary>
        void RequestSparseCurves(string assetPath);

        /// <summary>
        /// Whether this import of the model at <paramref name="assetPath"/> is a
        /// detection pass: a setting asked for it is not in effect yet in
        /// <paramref name="importer"/>, and the reimport that turns it on follows.
        /// An importer that rewrites curves skips its work on such a pass.
        /// </summary>
        bool IsDetectionPass(string assetPath, AssetImporter importer);
    }

    /// <summary>
    /// The stand-in: it applies nothing, so no pass waits on a setting; it keeps
    /// what was asked for, by model, for a test to read.
    /// </summary>
    [Provides(typeof(IModelImportSettings), StandIn = true)]
    public sealed class PureModelImportSettings : IModelImportSettings
    {
        /// <summary>The models asked to import their curves as keyed.</summary>
        public HashSet<string> SparseCurves { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <inheritdoc/>
        public void RequestSparseCurves(string assetPath) => SparseCurves.Add(assetPath);

        /// <inheritdoc/>
        public bool IsDetectionPass(string assetPath, AssetImporter importer) => false;
    }
}
