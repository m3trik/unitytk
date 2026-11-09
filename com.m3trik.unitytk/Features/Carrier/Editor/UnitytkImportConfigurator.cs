using M3trik.Unitytk.Core;
using M3trik.Unitytk.Kernel;
using UnityEditor;

/// <summary>
/// Applies the per-asset import-settings requests collected during the batch
/// (see the scoping notes on <see cref="CarrierImport"/>). Lives in its own
/// postprocessor because reimporting from OnPostprocessAllAssets is the
/// supported pattern; doing it mid-import is not. A model whose extracted
/// media were deleted under a cached import is reimported first, so its
/// importers ask for them again (<see cref="CarrierImport.ReimportModelsMissing"/>);
/// and every delivery's import depends on each file its channels named, where
/// it lands, there yet or not (<see cref="CarrierImport.AskedPaths"/>), once
/// every channel's importer has run. The global name is kept.
/// </summary>
public class UnitytkImportConfigurator : AssetPostprocessor
{
    /// <inheritdoc/>
    public override uint GetVersion() => 3;

    /// <summary>After every channel's importer (their orders are at most <see cref="ImportOrder.AfterMaterialDescriptions"/>).</summary>
    public override int GetPostprocessOrder() => ImportOrder.AfterMaterialDescriptions + 1;

    void OnPreprocessModel()
    {
        if (Imports.IsDelivery(assetPath)) CarrierImport.BeginImport(assetPath);
    }

    void OnPostprocessModel(UnityEngine.GameObject root)
    {
        if (!Imports.IsDelivery(assetPath)) return;
        foreach (string path in CarrierImport.AskedPaths(assetPath)) context.DependsOnSourceAsset(path);
    }

    static void OnPostprocessAllAssets(
        string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        CarrierImport.ReimportModelsMissing(deleted);
        CarrierImport.ApplyPendingImportSettings();
    }
}
