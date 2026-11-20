using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisSavedCatalog
{
    private ImmutableArray<AnalysisPendingReplacement>.Builder ReadReplacements(string[] names, HashSet<string> present,
        Guid? catalog, ref int bytes, CancellationToken cancellation)
    {
        var result = ImmutableArray.CreateBuilder<AnalysisPendingReplacement>();
        var identities = new HashSet<Guid>(); var operations = new HashSet<Guid>(); var logicalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!AnalysisPrivateNames.TryPendingReplacement(name, out var owner, out var previous, out var incoming, out var operation)) continue;
            if (catalog != owner) throw Invalid("A replacement record has no matching supported catalog header.");
            if (result.Count >= EntryLimit) throw Invalid("The saved replacement record limit was reached.");
            byte[] data = store.ReadMetadata(name, cancellation, EntryBytes); bytes = checked(bytes + data.Length);
            if (bytes > TotalBytes) throw Invalid("The saved analysis catalog exceeds its metadata budget.");
            var entry = DecodeEntry(data, owner);
            if (entry.InventoryId != previous || !identities.Add(previous) || !identities.Add(incoming) || !operations.Add(operation) || !logicalNames.Add(entry.Name))
                throw Invalid("Saved replacement records have conflicting generated identities or names.");
            result.Add(new(operation, incoming, entry, null, present.Contains(entry.Name),
                present.Contains(AnalysisPrivateNames.ReplacementIncoming(incoming, operation)),
                present.Contains(AnalysisPrivateNames.ReplacementPrevious(previous, operation))));
        }
        return result;
    }

    private static void CompleteReplacements(ImmutableArray<AnalysisPendingReplacement>.Builder replacements,
        ImmutableArray<AnalysisSavedEntry>.Builder entries, ImmutableArray<AnalysisPendingDeletion>.Builder deletions,
        HashSet<Guid> ids, HashSet<string> indexed)
    {
        int slots = entries.Count + deletions.Count;
        for (int index = 0; index < replacements.Count; index++)
        {
            var replacement = replacements[index]; var previous = replacement.Previous;
            if (!ids.Add(previous.InventoryId)) throw Invalid("A previous replacement identity is also cataloged independently.");
            var current = entries.SingleOrDefault(entry => entry.InventoryId == replacement.IncomingId);
            if (current is null)
            {
                if (!ids.Add(replacement.IncomingId) || !indexed.Add(previous.Name)) throw Invalid("A pending replacement conflicts with another saved analysis.");
                slots++;
            }
            else
            {
                if (current.Name != previous.Name || deletions.Any(deletion => deletion.Entry.InventoryId == replacement.IncomingId))
                    throw Invalid("The current replacement record does not match its pending previous version.");
                replacements[index] = replacement with { Current = current };
            }
        }
        if (slots > EntryLimit) throw Invalid("The saved analysis catalog entry limit was reached.");
    }
}
