using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisIndexedInventory
{
    /// <summary>Import an explicitly selected native read-only file into private bounded scratch as inert observations.</summary>
    public static async Task<AnalysisIndexedInventory> LoadExternalAsync(DeviceIoScheduler io, AnalysisIoDevices storageDevices, string inputDevice,
        Func<AnalysisPrivateDirectory> createScratch, Func<AnalysisProfileQuota> createProfileQuota, Func<AnalysisInventoryInput> openInput,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(storageDevices);
        ArgumentNullException.ThrowIfNull(createScratch); ArgumentNullException.ThrowIfNull(createProfileQuota); ArgumentNullException.ThrowIfNull(openInput);
        if (AnalysisAdmission.Refusal() is { } reason) throw new InvalidOperationException(reason);
        var devices = storageDevices.Include(inputDevice);
        await Loads.WaitAsync(cancellation).ConfigureAwait(false);
        AnalysisScratchWorkspace? workspace = null; AnalysisIndexedInventory? result = null;
        try
        {
            await AnalysisIo.Run(io, devices, _ =>
            {
                AnalysisPrivateDirectory? scratch = createScratch(); AnalysisProfileQuota? quota = null;
                try
                {
                    quota = createProfileQuota() ?? throw new InvalidOperationException("Private profile capacity is required for an inventory import.");
                    workspace = new(scratch, AnalysisLimits.ScratchBytes, AnalysisScratchBudget.Shared, quota); scratch = null;
                }
                finally
                {
                    try { if (quota is not null) AnalysisIo.CloseOnWorker(quota); }
                    finally { if (scratch is not null) AnalysisIo.CloseOnWorker(scratch); }
                }
                return true;
            }, cancellation).ConfigureAwait(false);
            result = await AnalysisInventoryFormat.LoadExternalIndexedAsync(workspace!, io, storageDevices, inputDevice, openInput, cancellation).ConfigureAwait(false);
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
