using System;
using M3trik.Unitytk.Core;
using UnityEditor;

namespace M3trik.Unitytk.Kernel
{
    /// <summary>
    /// The one import gate every importer asks first: whether the asset is a
    /// delivery. Unity reads a DCC's delivery as an FBX; a feature then acts when
    /// its record is present on the file, and does nothing where it is absent, so
    /// no per-feature switch or file list stands between a delivery and its import.
    /// </summary>
    /// <remarks>
    /// Prior art: an <c>AssetPostprocessor</c>'s model callbacks run for every
    /// model format Unity imports; the extension is what tells a DCC's FBX from
    /// the rest, as Unity's own <c>ModelImporter</c> tells formats apart.
    /// </remarks>
    public static class Imports
    {
        /// <summary>
        /// Whether the asset at <paramref name="assetPath"/> is a delivery: a path
        /// ending in <c>.fbx</c>, in any case.
        /// </summary>
        /// <param name="assetPath">The asset being imported, such as <c>Assets/Models/shot.fbx</c>.</param>
        public static bool IsDelivery(string assetPath)
            => assetPath != null && assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The frame <paramref name="importer"/> lands a delivery's records in
        /// (<see cref="GltfFrame"/>): its Convert Units, its file's unit and its Scale
        /// Factor, read off the importer as the import applies them;
        /// <see cref="GltfFrame.Default"/> without one.
        /// </summary>
        public static GltfFrame Frame(ModelImporter importer)
            => importer == null ? GltfFrame.Default
                                : new GltfFrame(importer.useFileScale, importer.fileScale, importer.globalScale);
    }
}
