namespace FileCat.Core.Analysis;

public sealed partial class OwnedAnalysisStore
{
    // A copy into an already prepared named owner needs no new scan/private-exclusion
    // preparation. Its runtime root/profile remain owned through actual save and cleanup.
    internal Task<AnalysisInventoryReceipt> SaveCopyNewAsync(AnalysisDatasetLease source, string name, AnalysisSavedView view,
        CancellationToken cancellation) => _owned.UseAsync(async data =>
    {
        ArgumentNullException.ThrowIfNull(source);
        if (data.Rolling) throw new InvalidOperationException("Named copies require a named-store owner.");
        var receipt = await source.SaveNamedAsync(name, Guid.NewGuid(), view, _device.Primary, () =>
        {
            RequireProfile(data, cancellation); return data.Store!.BorrowDirectory();
        }, cancellation).ConfigureAwait(false);
        try { await AnalysisIo.Run(_io, _device, token => { RequireProfile(data, token); return true; }, cancellation).ConfigureAwait(false); }
        catch (Exception error) { throw new AnalysisCatalogUpdateException(receipt, error); }
        return receipt;
    });
}
