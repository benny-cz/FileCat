using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

internal enum AnalysisContentEvidenceKind { ObservedDigest, StableContentProof }

/// <summary>Recorded evidence used to check a fresh open under a runtime-owned root.</summary>
internal sealed record AnalysisPrimaryContentTarget(ulong PathId, AnalysisBranchComponent? RootDirectory,
    ImmutableArray<AnalysisBranchComponent> Parents, string Name, string Identity, string Version,
    DateTimeOffset CreatedUtc, ulong CreatedPrecisionTicks, long Bytes)
{
    internal const int PreviewBytes = 16 * 1024;

    internal void RequireFile(AnalysisEntry entry)
    {
        if (entry.Kind != AnalysisKind.File || entry.Cloud != false || entry.MetadataState != MeasurementState.Known ||
            entry.Identity != Identity || entry.Version != Version || entry.CreatedUtc != CreatedUtc ||
            entry.CreatedPrecisionTicks != CreatedPrecisionTicks || entry.LogicalLength.Value != (UInt128)Bytes)
            throw new AnalysisSourceChangedException();
    }
}

/// <summary>Primary stream evidence for one completed read. It is distinct from job authorization and complete-file equality.</summary>
internal sealed record AnalysisPrimaryContentProof(ulong PathId, string Sha256, long BytesRead, DateTimeOffset ObservedUtc,
    AnalysisContentEvidenceKind Kind);

/// <summary>Owned native work, advanced and closed through the source device's admitted I/O worker.</summary>
internal interface IAnalysisBoundContent : IDisposable
{
    long BytesRead { get; }
    bool ReadNext(CancellationToken cancellation);
    AnalysisPrimaryContentProof Complete(CancellationToken cancellation);
}
