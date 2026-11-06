using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>A validated inert private import owner. No original locator is bound to a native source.</summary>
public sealed class AnalysisIndexedInventory : IAsyncDisposable
{
    private readonly AnalysisDataset _dataset;
    private static readonly SemaphoreSlim Loads = new(2, 2);
    internal AnalysisIndexedInventory(Guid inventoryId, long sourceBytes, AnalysisDataset dataset)
    { InventoryId = inventoryId; SourceBytes = sourceBytes; _dataset = dataset; }
    public Guid InventoryId { get; }
    public long SourceBytes { get; }
    public AnalysisDatasetDescriptor Descriptor => _dataset.Descriptor;
    public Task<AnalysisDatasetLease> AcquireAsync(CancellationToken cancellation = default) => _dataset.AcquireAsync(cancellation);
    public ValueTask DisposeAsync() => _dataset.DisposeAsync();

    /// <summary>
    /// Loads one named private inventory using application-owned native factories and captured
    /// devices. Capacity belongs to the destination application's private profile, never a parsed
    /// historical profile. Source records grant no native source, content or action capability.
    /// The returned owner is independent of preparation and temporary staging lifetimes.
    /// </summary>
    public static async Task<AnalysisIndexedInventory> LoadPrivateAsync(DeviceIoScheduler io, AnalysisIoDevices storageDevices, string inputDevice,
        string name, Func<AnalysisPrivateDirectory> createScratch, Func<AnalysisProfileQuota> createProfileQuota,
        Func<AnalysisPrivateDirectory> openInput, AnalysisSavedEntry? reviewedEntry = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(storageDevices); ArgumentNullException.ThrowIfNull(createScratch);
        ArgumentNullException.ThrowIfNull(createProfileQuota); ArgumentNullException.ThrowIfNull(openInput);
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        AnalysisInventoryFormat.ValidateReviewedSelection(name, reviewedEntry);
        var devices = storageDevices.Include(inputDevice);
        await Loads.WaitAsync(cancellation).ConfigureAwait(false);
        AnalysisScratchWorkspace? workspace = null; AnalysisIndexedInventory? result = null;
        try
        {
            await AnalysisIo.Run(io, devices, _ =>
            {
                AnalysisPrivateDirectory? scratch = createScratch();
                try
                {
                    using var quota = createProfileQuota() ?? throw new InvalidOperationException("Private profile capacity ownership is required for an indexed inventory load.");
                    workspace = new(scratch, AnalysisLimits.ScratchBytes, AnalysisScratchBudget.Shared, quota); scratch = null;
                }
                finally { scratch?.Dispose(); }
                return true;
            }, cancellation).ConfigureAwait(false);
            result = await AnalysisInventoryFormat.LoadPrivateIndexedAsync(workspace!, io, storageDevices, inputDevice, name, openInput,
                reviewedEntry, cancellation).ConfigureAwait(false);
            return result;
        }
        finally
        {
            try
            {
                try { if (workspace is not null) await AnalysisIo.ReleaseScratchAsync(io, devices, workspace).ConfigureAwait(false); }
                catch { if (result is not null) await result.DisposeAsync().ConfigureAwait(false); throw; }
            }
            finally { Loads.Release(); }
        }
    }
}

public static partial class AnalysisInventoryFormat
{
    // Private capability/device/workspace factories are application configuration. Catalog
    // fields only constrain expected data; none may choose a directory, device or live source.
    internal static async Task<AnalysisIndexedInventory> LoadPrivateIndexedAsync(AnalysisScratchWorkspace workspace, DeviceIoScheduler io,
        AnalysisIoDevices scratchDevices, string inputDevice, string name, Func<AnalysisPrivateDirectory> openInput,
        AnalysisSavedEntry? reviewedEntry = null, CancellationToken cancellation = default)
    {
        ValidateReviewedSelection(name, reviewedEntry);
        AnalysisInventoryStage? stage = null; AnalysisIndexedInventory? result = null;
        try
        {
            stage = await StagePrivateAsync(workspace, io, scratchDevices, inputDevice, name, openInput, cancellation).ConfigureAwait(false);
            var header = await stage.ReadHeaderAsync(cancellation).ConfigureAwait(false);
            if (reviewedEntry is { } selected && (selected.InventoryId != stage.InventoryId || selected.Bytes != stage.SourceBytes ||
                selected.Coverage != header.Historical.Coverage || AnalysisPrivacy.Join(selected.Taint) != AnalysisPrivacy.Join(header.Historical.Taint)))
                throw new InvalidDataException("The saved inventory does not match the reviewed catalog identity, size, coverage or privacy classification.");
            result = new(stage.InventoryId, stage.SourceBytes, await stage.BuildDatasetAsync(cancellation).ConfigureAwait(false));
            return result;
        }
        finally
        {
            try { if (stage is not null) await stage.DisposeAsync().ConfigureAwait(false); }
            catch { if (result is not null) await result.DisposeAsync().ConfigureAwait(false); throw; }
        }
    }
    internal static void ValidateReviewedSelection(string name, AnalysisSavedEntry? reviewedEntry)
    {
        AnalysisPrivateInventoryStore.ValidateName(name);
        if (reviewedEntry is { } expected)
        {
            AnalysisSavedCatalog.ValidateView(expected.View);
            if (expected.Name != name || expected.InventoryId == Guid.Empty || expected.Bytes is < 1 or > AnalysisLimits.InventoryBytes ||
                !Enum.IsDefined(expected.Coverage) || expected.Taint == 0 || (expected.Taint & ~AnalysisPrivacy.All) != 0)
                throw new InvalidDataException("The reviewed catalog selection is invalid or does not match the filename.");
        }
    }
}
