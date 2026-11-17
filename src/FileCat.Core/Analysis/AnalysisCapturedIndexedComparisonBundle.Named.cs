namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisCapturedIndexedComparisonBundle
{
    internal async Task<AnalysisComparisonReceipt> SaveNamedNewAsync(string name, string destinationDevice,
        Func<AnalysisPrivateDirectory> createDirectory, CancellationToken cancellation = default)
    {
        AnalysisPrivateInventoryStore.ValidateComparisonName(name); ArgumentNullException.ThrowIfNull(createDirectory);
        try
        {
            var receipt = await ExportNewAsync(name, destinationDevice,
                () => NamedDestination.Create(createDirectory(), name, _id, cancellation), false, cancellation).ConfigureAwait(false);
            return new(_id, name, receipt.Bytes);
        }
        catch (AnalysisExportUncertainException error) { throw new AnalysisPublicationUncertainException(name, error); }
    }

    private sealed class NamedDestination(AnalysisPrivateInventoryStore store, AnalysisPrivateDirectory directory,
        AnalysisPrivateFile held, AnalysisPrivateInventoryStore.DatasetWrite writing, string name, Guid id,
        CancellationToken cancellation) : AnalysisExportDestination
    {
        internal static NamedDestination Create(AnalysisPrivateDirectory directory, string name, Guid id, CancellationToken cancellation)
        {
            var store = new AnalysisPrivateInventoryStore(directory); AnalysisPrivateFile? held = null;
            AnalysisPrivateInventoryStore.DatasetWrite? writing = null;
            try
            {
                held = store.OpenCatalogLock(cancellation);
                var names = store.ComparisonNames(cancellation);
                if (names.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new AnalysisExportTargetExistsException(name, new IOException("That saved comparison already exists. Keep it and choose another name."));
                if (names.Length >= AnalysisSavedCatalog.EntryLimit) throw new IOException("The saved comparison count limit has been reached. Keep existing comparisons and review storage first.");
                writing = AnalysisPrivateInventoryStore.BeginComparisonWrite(store.BorrowDirectory(), name, cancellation);
                return new(store, directory, held, writing, name, id, cancellation);
            }
            catch
            {
                try { if (writing is not null) AnalysisIo.CloseOnWorker(writing); }
                finally { try { if (held is not null) AnalysisIo.CloseOnWorker(held); } finally { AnalysisIo.CloseOnWorker(store); } }
                throw;
            }
        }
        internal override Stream Stream => writing.Stream;
        internal override void Verify() { directory.Verify(); held.Verify(); writing.Verify(); }
        internal override (UInt128 Capacity, UInt128 Available) Space() { Verify(); return directory.Space(); }
        internal override void FlushDurable() => writing.Prepare();
        internal override void PublishNew(string selectedName)
        {
            if (!string.Equals(name, selectedName, StringComparison.Ordinal)) throw new InvalidOperationException("The captured comparison filename changed.");
            writing.PublishPrepared(id, cancellation);
        }
        public override void Dispose()
        {
            try { AnalysisIo.CloseOnWorker(writing); }
            finally { try { AnalysisIo.CloseOnWorker(held); } finally { AnalysisIo.CloseOnWorker(store); } }
        }
    }
}
