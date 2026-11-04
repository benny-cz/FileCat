using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed record AnalysisEntry(ImmutableArray<byte> RelativeName, string Name, ImmutableArray<byte> RawName,
    AnalysisKind Kind, string? Identity, AnalysisMeasurement LogicalLength, AnalysisMeasurement Allocation,
    AnalysisMeasurement Links, DateTimeOffset? ModifiedUtc, string Version, bool Cloud = false, bool Compressed = false,
    string NameEncoding = "utf8", DateTimeOffset? ObservedUtc = null);

/// <summary>An explicitly prepared, held live source. Inventories cannot implement or deserialize this capability.</summary>
public interface IAnalysisBackend : IDisposable
{
    RootBinding Root { get; }
    string Basis { get; }
    AnalysisEntry RootEntry { get; }
    IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation);
}

public sealed class AnalysisSourceChangedException : IOException
{
    public AnalysisSourceChangedException() : base("The selected source changed or crossed its captured boundary.") { }
}
