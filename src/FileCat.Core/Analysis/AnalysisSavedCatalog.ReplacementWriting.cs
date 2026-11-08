namespace FileCat.Core.Analysis;

public sealed partial class AnalysisSavedCatalog
{
    internal void ValidateReplacementBudget(AnalysisSavedEntry previous, Guid incoming, long bytes, NamespaceCoverage coverage,
        AnalysisTaint taint, AnalysisSavedView view, CancellationToken cancellation)
    {
        ValidateView(view); using var current = Read(cancellation);
        if (!current.Entries.Contains(previous) || current.PendingReplacements.Any(p => p.Previous.Name.Equals(previous.Name, StringComparison.OrdinalIgnoreCase)) ||
            current.Entries.Any(e => e.InventoryId == incoming) || current.PendingDeletions.Any(p => p.Entry.InventoryId == incoming) ||
            current.PendingReplacements.Any(p => p.Previous.InventoryId == incoming || p.IncomingId == incoming))
            throw Invalid("The named analysis is no longer available for this replacement.");
        var planned = new AnalysisSavedEntry(incoming, previous.Name, bytes, DateTimeOffset.UtcNow, coverage, taint, view);
        RequireBytes(current, current.CatalogId!.Value, planned);
    }
    // Caller holds the catalog writer, exact pending metadata and both native data leases.
    // Do not reopen its exclusively held metadata through a whole-catalog read.
    internal void CompleteReplacement(Guid catalog, AnalysisSavedEntry entry, CancellationToken cancellation)
    {
        if (ReadIdentity(cancellation) != catalog) throw Invalid("The replacement catalog identity changed.");
        ValidateView(entry.View);
        store.WriteMetadataNew(AnalysisPrivateNames.CatalogEntry(catalog, entry.InventoryId), EncodeEntry(catalog, entry), cancellation);
    }
}
