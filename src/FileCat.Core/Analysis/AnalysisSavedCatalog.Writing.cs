namespace FileCat.Core.Analysis;

public sealed partial class AnalysisSavedCatalog
{
    internal NamedWrite BeginNamedWrite(string name, Guid inventory, NamespaceCoverage coverage, AnalysisTaint taint, AnalysisSavedView view, CancellationToken cancellation)
    {
        AnalysisPrivateInventoryStore.ValidateName(name); ValidateView(view);
        if (inventory == Guid.Empty) throw new ArgumentException("A generated inventory identity is required.", nameof(inventory));
        // A malformed existing catalog is refused before creating even a control file.
        using (Read(cancellation)) { }
        var held = store.OpenCatalogLock(cancellation);
        try
        {
            using var current = Read(cancellation); RequireCapacity(current, inventory, name);
            Guid catalog = current.CatalogId ?? CreateHeader(cancellation);
            var entry = new AnalysisSavedEntry(inventory, name, 1, DateTimeOffset.UtcNow, coverage, taint, view);
            RequireBytes(current, catalog, entry);
            return new(this, held, catalog, entry);
        }
        catch { AnalysisIo.CloseOnWorker(held); throw; }
    }
    private static void RequireCapacity(AnalysisCatalogSnapshot current, Guid inventory, string name, bool published = false)
    {
        if (current.Entries.Length + current.PendingDeletions.Length + current.PendingReplacements.Count(p => p.Current is null) >= EntryLimit || current.Entries.Any(entry => entry.InventoryId == inventory || entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
            current.PendingDeletions.Any(p => p.Entry.InventoryId == inventory || p.Entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
            current.PendingReplacements.Any(p => p.Previous.InventoryId == inventory || p.IncomingId == inventory || p.Previous.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ||
            (!published && current.UnindexedNames.Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))))
            throw new IOException("That inventory name or identity already exists, or the catalog is full. Keep existing files and choose another name.");
        if (published && !current.UnindexedNames.Contains(name, StringComparer.Ordinal)) throw Invalid("The newly published inventory is absent from the private directory.");
    }
    private static void RequireBytes(AnalysisCatalogSnapshot current, Guid catalog, AnalysisSavedEntry entry)
    {
        byte[] preview = EncodeEntry(catalog, entry with { Bytes = AnalysisLimits.InventoryBytes });
        if (current.MetadataBytes > TotalBytes - preview.Length) throw Invalid("The saved analysis catalog metadata budget is full.");
    }
    internal sealed class NamedWrite(AnalysisSavedCatalog catalog, AnalysisPrivateFile held, Guid identity, AnalysisSavedEntry planned) : IDisposable
    {
        private bool _closed, _completed;
        internal AnalysisPrivateDirectory BorrowDirectory() { Guard(); held.Verify(); return catalog.store.BorrowDirectory(); }
        private void Guard() => ObjectDisposedException.ThrowIf(_closed, this);
        internal void Complete(AnalysisInventoryReceipt receipt, CancellationToken cancellation)
        {
            Guard();
            try
            {
                if (_completed || receipt.InventoryId != planned.InventoryId || receipt.Name != planned.Name || receipt.Bytes is < 1 or > AnalysisLimits.InventoryBytes)
                    throw Invalid("The published inventory receipt does not match this catalog write.");
                held.Verify(); using var current = catalog.Read(cancellation);
                if (current.CatalogId != identity) throw Invalid("The private catalog identity changed during save.");
                RequireCapacity(current, receipt.InventoryId, receipt.Name, published: true); RequireBytes(current, identity, planned);
                held.Verify(); catalog.store.WriteMetadataNew(AnalysisPrivateNames.CatalogEntry(identity, receipt.InventoryId),
                    EncodeEntry(identity, planned with { Bytes = receipt.Bytes, SavedUtc = DateTimeOffset.UtcNow }), cancellation);
                _completed = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or OperationCanceledException or InvalidOperationException)
            { throw new AnalysisCatalogUpdateException(receipt, error); }
        }
        public void Dispose() { if (_closed) return; _closed = true; AnalysisIo.CloseOnWorker(held); }
    }
}
