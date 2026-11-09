using System;
using System.Collections.Generic;
using M3trik.Unitytk.Contracts;
using M3trik.Unitytk.Kernel;
using UnityEditor;
using UnityEngine;

namespace M3trik.Unitytk.Carrier
{
    /// <summary>
    /// The record carrier: the <c>data_export</c> node every DCC export writes,
    /// read during an import off its user properties and afterwards off the FBX
    /// file itself, the media the FBX embeds, and the import settings a channel
    /// asks of the model, applied with the media's extraction in one reimport after
    /// the batch. Provides the records, media and import-settings contracts every
    /// split feature's importer reads through.
    /// </summary>
    /// <remarks>
    /// Over <see cref="CarrierImport"/> (a node's channels, the media, the import
    /// settings) and <see cref="CarrierFile"/> (a record read off the file), beside it
    /// in this part, whose global names verifiers and project scripts call. Prior art:
    /// Unity's FBX importer hands user properties to
    /// <c>OnPostprocessGameObjectWithUserProperties</c> only during an import, so a
    /// reader outside one reads the file (<c>CarrierFile</c>, a binary FBX scan);
    /// no Unity API reads an imported model's user properties back.
    /// </remarks>
    [Provides(typeof(IRecordSource))]
    [Provides(typeof(IModelMedia))]
    [Provides(typeof(IModelImportSettings))]
    public sealed class CarrierRecords : IRecordSource, IModelMedia, IModelImportSettings
    {
        /// <inheritdoc/>
        public bool TryGetRecord(string[] propertyNames, object[] values, string key, out string json)
            => CarrierImport.TryGetProp(propertyNames, values, key, out json);

        /// <inheritdoc/>
        public void Collect(Dictionary<string, string> pending, string assetPath, GameObject node, string json)
            => CarrierImport.Collect(pending, assetPath, node, json);

        /// <inheritdoc/>
        public T Read<T>(string key, string json, int current, Func<object, T> read, string tag, string assetPath) where T : class
            => CarrierImport.Read(key, json, current, read, tag, assetPath);

        /// <inheritdoc/>
        public string Record(string assetPath, string key) => CarrierFile.Record(assetPath, key);

        /// <inheritdoc/>
        public bool RequestEmbeddedFiles(string assetPath, IEnumerable<string> names, Func<string, bool> holds = null)
            => CarrierImport.RequestEmbeddedFiles(assetPath, names, holds);

        /// <inheritdoc/>
        public bool HoldsCurrent(string assetPath, string name) => CarrierImport.HoldsCurrent(assetPath, name);

        /// <inheritdoc/>
        public string MediaFolder(string assetPath) => CarrierImport.MediaFolder(assetPath);

        /// <inheritdoc/>
        public string[] MediaFolders(string assetPath) => CarrierImport.MediaFolders(assetPath);

        /// <inheritdoc/>
        public bool IsOwnMedia(string modelAssetPath, string fileAssetPath) => CarrierImport.IsOwnMedia(modelAssetPath, fileAssetPath);

        /// <inheritdoc/>
        public bool InOwnMediaFolder(string modelAssetPath, string fileAssetPath)
            => CarrierImport.InOwnMediaFolder(modelAssetPath, fileAssetPath);

        /// <inheritdoc/>
        public bool IsReusableBy(string modelAssetPath, string fileAssetPath) => CarrierImport.IsReusableBy(modelAssetPath, fileAssetPath);

        /// <inheritdoc/>
        public IEnumerable<string> OwnMediaCopies(string assetPath, string name) => CarrierImport.OwnMediaCopies(assetPath, name);

        /// <inheritdoc/>
        public void RequestSparseCurves(string assetPath) => CarrierImport.RequestSparseCurves(assetPath);

        /// <inheritdoc/>
        public bool IsDetectionPass(string assetPath, AssetImporter importer) => CarrierImport.IsDetectionPass(assetPath, importer);
    }
}
