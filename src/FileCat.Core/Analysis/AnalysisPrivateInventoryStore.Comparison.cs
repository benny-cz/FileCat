using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisPrivateInventoryStore
{
    /// <summary>Publishes both captured inputs and applied view in one immutable native sibling.</summary>
    public AnalysisComparisonReceipt SaveComparisonNew(string name, Guid id, AnalysisSnapshot previous, AnalysisSnapshot current,
        AnalysisComparisonView view, IReadOnlyDictionary<string, string>? structuralRoots, string previousLabel, CancellationToken cancellation = default,
        IReadOnlyDictionary<string, string>? previousRootDisplayIds = null, IReadOnlyDictionary<string, string>? currentRootDisplayIds = null)
    {
        ValidateComparisonName(name); if (id == Guid.Empty) throw new ArgumentException("A generated comparison identity is required.", nameof(id));
        lock (_gate)
        {
            Guard(); cancellation.ThrowIfCancellationRequested();
            AnalysisComparisonReceipt? receipt = null;
            try
            {
                using var writing = new AnalysisWorkerClose(OpenCatalogLock(cancellation));
                var names = ComparisonNames(cancellation);
                if (names.Length >= AnalysisSavedCatalog.EntryLimit || names.Any(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException("That comparison name exists. Keep the existing comparison and choose another name.");
                long bytes = WriteNew(name, stream => AnalysisComparisonFormat.Write(stream, id, previous, current, view, structuralRoots, previousLabel, cancellation, previousRootDisplayIds, currentRootDisplayIds), cancellation);
                receipt = new(id, name, bytes); return receipt;
            }
            catch (Exception error) when (receipt is not null && error is not AnalysisPublicationUncertainException)
            { throw new AnalysisPublicationUncertainException(name, error); }
        }
    }
    public AnalysisSavedComparison LoadComparison(string name, CancellationToken cancellation = default)
    {
        ValidateComparisonName(name);
        lock (_gate)
        {
            Guard(); cancellation.ThrowIfCancellationRequested(); AnalysisSavedComparison? loaded = null;
            try
            {
                var file = directory.OpenRead(name); using var close = new AnalysisWorkerClose(file); file.Verify();
                if (file.Stream.Length is < 1 or > AnalysisLimits.InventoryBytes) throw new InvalidDataException("Saved comparison exceeds its size limit.");
                using var stream = new GuardedReadStream(file.Stream, () => { Guard(); file.Verify(); }, cancellation);
                loaded = AnalysisComparisonFormat.Read(stream, cancellation); file.Verify(); return loaded;
            }
            catch { loaded?.Dispose(); throw; }
        }
    }
    public ImmutableArray<string> ComparisonNames(CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            Guard(); cancellation.ThrowIfCancellationRequested(); var result = ImmutableArray.CreateBuilder<string>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in directory.Entries(cancellation))
            {
                cancellation.ThrowIfCancellationRequested(); if (!name.EndsWith(".fcdsc", StringComparison.Ordinal)) continue;
                ValidateComparisonName(name);
                if (result.Count >= AnalysisSavedCatalog.EntryLimit || !seen.Add(name)) throw new InvalidDataException("Saved comparison names conflict or exceed the catalog limit.");
                result.Add(name);
            }
            return result.Order(StringComparer.Ordinal).ToImmutableArray();
        }
    }
    public static void ValidateComparisonName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!name.EndsWith(".fcdsc", StringComparison.Ordinal)) throw new ArgumentException("Choose a portable filename ending in .fcdsc.", nameof(name));
        ValidateName(name[..^6] + ".fcdsa");
    }
}
