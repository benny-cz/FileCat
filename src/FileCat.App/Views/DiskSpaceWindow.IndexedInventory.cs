using System.Globalization;
using FileCat.App.Services;
using FileCat.Core.Analysis;

namespace FileCat.App.Views;

public sealed partial class DiskSpaceWindow
{
    // Internal qualification route. The normal snapshot opener remains until indexed
    // persistence/comparison controls have their full user-facing behavior and evidence.
    internal Task<AnalysisDatasetExplorer> AddSavedIndexedSessionAsync(AppServices services, string name,
        AnalysisSavedEntry? reviewedEntry = null, CancellationToken cancellation = default)
    {
        AnalysisInventoryReceipt? opened = null;
        return AddPreparedIndexedSessionAsync(Load, reviewedEntry?.View.Metric ?? AnalysisMetric.OnDisk, null, reviewedEntry?.View, cancellation, services, () => opened);

        // Local source ownership is transferred only after preparation/import close succeeds.
        async Task<AnalysisDatasetLease> Load(CancellationToken token)
        {
            AnalysisDatasetLease? result = null;
            try
            {
                await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, token);
                await using var inventory = await prepared.LoadInventoryAsync(name, reviewedEntry, token);
                opened = new(inventory.InventoryId, name, inventory.SourceBytes);
                result = await inventory.AcquireAsync(token); return result;
            }
            catch { if (result is not null) await result.DisposeAsync(); throw; }
        }
    }

    private static async Task<AnalysisDatasetLease> LoadIndexedComparisonInputAsync(AppServices services, string name,
        AnalysisSavedEntry? reviewedEntry, CancellationToken cancellation)
    {
        AnalysisDatasetLease? result = null;
        try
        {
            await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(services, cancellation);
            await using var inventory = await prepared.LoadInventoryAsync(name, reviewedEntry, cancellation);
            result = await inventory.AcquireAsync(cancellation); return result;
        }
        catch { if (result is not null) await result.DisposeAsync(); throw; }
    }

    private static AnalysisHierarchySort SavedIndexedSort(AnalysisTableSort sort) => sort switch
    {
        AnalysisTableSort.ActiveMetric => AnalysisHierarchySort.Metric,
        AnalysisTableSort.Name => AnalysisHierarchySort.Name,
        AnalysisTableSort.FileSize => AnalysisHierarchySort.FileSize,
        AnalysisTableSort.FilePaths => AnalysisHierarchySort.Files,
        AnalysisTableSort.MeasurementState => AnalysisHierarchySort.MetadataState,
        _ => throw new InvalidDataException("The saved table order is unsupported.")
    };

    private static async Task<(ulong Scope, int Skipped, string? Notice)> RestoreIndexedScopeAsync(AnalysisDatasetLease source, string key, CancellationToken cancellation)
    {
        int offset;
        try { var aggregate = AnalysisSavedCatalog.SplitAggregationScope(key); key = aggregate.Scope; offset = aggregate.Skipped; }
        catch (Exception error) when (error is InvalidDataException or OverflowException)
        { return (0, 0, "The saved display scope is unavailable. Showing the captured top level."); }
        if (key == AnalysisTree.RootKey) return (0, offset, null);
        if (key.StartsWith("n:", StringComparison.Ordinal) && ulong.TryParse(key.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) &&
            id != 0 && key.AsSpan(2).SequenceEqual(id.ToString(CultureInfo.InvariantCulture)))
        {
            try { if ((await source.ReadPathAsync(id, cancellation)).Kind == AnalysisKind.Directory) return (id, offset, null); }
            catch (ArgumentOutOfRangeException) { }
        }
        return (0, 0, "The saved folder is absent or unavailable. Showing the top level.");
    }
}
