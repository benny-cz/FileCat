using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetLease
{
    /// <summary>Streams the entire revision and its inert display settings into one create-new catalog transaction.</summary>
    public async Task<AnalysisInventoryReceipt> SaveNamedAsync(string name, Guid inventoryId, AnalysisSavedView view, string destinationDevice,
        Func<AnalysisPrivateDirectory> createDirectory, CancellationToken cancellation = default)
    {
        AnalysisPrivateInventoryStore.ValidateName(name); AnalysisSavedCatalog.ValidateView(view); ArgumentNullException.ThrowIfNull(createDirectory);
        if (inventoryId == Guid.Empty) throw new ArgumentException("A generated inventory identity is required.", nameof(inventoryId));
        var saved = AnalysisSavedCatalog.ViewForInventory(Descriptor, view); var devices = _device.Include(destinationDevice); bool published = false;
        try
        {
            return await _owned.UseAsync(async reader =>
            {
                if (reader.Header.Identity != Descriptor.Identity) throw new InvalidDataException("The inventory descriptor does not match its retained revision.");
                var writing = await AnalysisIo.Run(_io, devices, token => NamedInventoryWrite.Create(createDirectory(), name, inventoryId,
                    reader.Header.Coverage, reader.Header.Taint, saved, token), cancellation).ConfigureAwait(false);
                try
                {
                    AnalysisInventoryReceipt receipt;
                    try { receipt = await AnalysisInventoryFormat.WriteDatasetAsync(reader, _io, devices, writing.BorrowDirectory, name, inventoryId,
                        () => published = true, cancellation).ConfigureAwait(false); }
                    catch (AnalysisPublicationUncertainException) { published = true; throw; }
                    try { await AnalysisIo.Run(_io, devices, token => { writing.Complete(receipt, token); return true; }, cancellation).ConfigureAwait(false); }
                    catch (Exception error) when (error is not AnalysisCatalogUpdateException) { throw new AnalysisCatalogUpdateException(receipt, error); }
                    return receipt;
                }
                finally { await AnalysisIo.ReleaseScratchAsync(_io, devices, writing).ConfigureAwait(false); }
            }).ConfigureAwait(false);
        }
        catch (Exception error) when (published && error is not AnalysisCatalogUpdateException and not AnalysisPublicationUncertainException)
        { throw new AnalysisPublicationUncertainException(name, error); }
    }
    private sealed class NamedInventoryWrite(AnalysisPrivateInventoryStore store, AnalysisSavedCatalog.NamedWrite writing) : IAnalysisScratchOwned
    {
        internal static NamedInventoryWrite Create(AnalysisPrivateDirectory directory, string name, Guid inventory, NamespaceCoverage coverage,
            AnalysisTaint taint, AnalysisSavedView view, CancellationToken cancellation)
        {
            var store = new AnalysisPrivateInventoryStore(directory);
            try { return new(store, new AnalysisSavedCatalog(store).BeginNamedWrite(name, inventory, coverage, taint, view, cancellation)); }
            catch { AnalysisIo.CloseOnWorker(store); throw; }
        }
        internal AnalysisPrivateDirectory BorrowDirectory() => writing.BorrowDirectory();
        internal void Complete(AnalysisInventoryReceipt receipt, CancellationToken cancellation) => writing.Complete(receipt, cancellation);
        public void Dispose() { try { writing.Dispose(); } finally { AnalysisIo.CloseOnWorker(store); } }
    }
}
