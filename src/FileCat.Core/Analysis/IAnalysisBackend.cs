using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed record AnalysisEntry(ImmutableArray<byte> RelativeName, string Name, ImmutableArray<byte> RawName,
    AnalysisKind Kind, string? Identity, AnalysisMeasurement LogicalLength, AnalysisMeasurement Allocation,
    AnalysisMeasurement Links, DateTimeOffset? ModifiedUtc, string Version, bool? Cloud = null, bool? Compressed = null,
    string NameEncoding = "utf8", DateTimeOffset? ObservedUtc = null, bool? Sparse = null, ulong ModifiedPrecisionTicks = 1)
{
    // Process-owned read-scope evidence. It is deliberately absent from inventory records and never authorizes mutation.
    internal AnalysisAncestry? Ancestry { get; init; }
}

internal sealed record AnalysisAncestry(Guid BindingId, ImmutableArray<byte> RelativeName, string? Identity,
    string Version, AnalysisAncestry? Parent);

public enum AnalysisRootContainment { Disjoint, Covered, Unavailable }

/// <summary>An explicitly prepared, held live source. Inventories cannot implement or deserialize this capability.</summary>
public interface IAnalysisBackend : IDisposable
{
    RootBinding Root { get; }
    string Basis { get; }
    AnalysisEntry RootEntry { get; }
    string IoDeviceKey => FileSystem.PathUtil.GetDeviceKey(Root.CanonicalLocator.StartsWith("\\\\?\\", StringComparison.Ordinal)
        ? Root.CanonicalLocator[4..] : Root.CanonicalLocator);
    IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation);
    void ValidateReadScope(AnalysisEntry directory);
    // Invoked only on admitted I/O workers. Historical RootBinding data cannot supply this proof.
    AnalysisRootContainment ContainsRoot(IAnalysisBackend other) => AnalysisRootContainment.Unavailable;
}

public sealed class AnalysisSourceChangedException : IOException
{
    public AnalysisSourceChangedException() : base("The selected source changed or crossed its captured boundary.") { }
}
