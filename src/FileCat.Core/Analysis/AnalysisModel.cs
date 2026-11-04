using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

// Inventory records are observations. None of these types authorizes a filesystem operation.
public enum MeasurementState { Known, Pending, Unsupported, Excluded, Failed, Changed }
public enum AnalysisKind { Directory, File, Link, Special }
public enum AnalysisMetric { FileSize, UniqueFileSize, OnDisk, Files }
public enum AnalysisPhase { Ready, Scanning, Pausing, Paused, Refreshing, Finished, Stopped, Failed }
public enum NamespaceCoverage { CompleteUnderPolicy, Partial, Unknown }
public enum AnalysisFreshness { Observed, PossiblyStale, SourceUnavailable, IdentityMismatch }
public enum AnalysisOrigin { LiveSession, OwnSavedInventory, ImportedInventory }
public enum AnalysisPersistence { Temporary, SavingRevision, NamedSaved, RollingSaved, SaveFailed }
public enum AnalysisTaint { OrdinarySensitive, PrivilegedDerived, ImportedUnverified }
public enum IdentityAssurance { Unknown, Verified }
public enum AnalysisAssurance { ObservedMetadata, ReviewedObject, ComparedContent }

public readonly record struct AnalysisMeasurement
{
    public MeasurementState State { get; }
    public UInt128? Value { get; }
    private AnalysisMeasurement(MeasurementState state, UInt128? value) { State = state; Value = value; }
    public static AnalysisMeasurement Known(UInt128 value) => new(MeasurementState.Known, value);
    public static AnalysisMeasurement Missing(MeasurementState state)
    {
        if (state == MeasurementState.Known) throw new ArgumentException("A known measurement requires a value.", nameof(state));
        return new(state, null);
    }
    // default(struct) is deliberately not a valid known measurement.
    public bool IsKnown => State == MeasurementState.Known && Value.HasValue;
}

public sealed record SessionIdentity(Guid SessionId, long Epoch, long Revision, string HostProfile,
    DateTimeOffset StartedUtc, DateTimeOffset ObservedUtc);
public sealed record RootBinding(string Id, string Scheme, string DisplayLocator, string CanonicalLocator,
    string? SourceIdentity, IdentityAssurance Assurance, string Label);
public sealed record AnalysisObject(string Id, string? VerifiedIdentity, AnalysisMeasurement LogicalLength,
    AnalysisMeasurement LinkCount, DateTimeOffset? ModifiedUtc = null, string? VersionEvidence = null);
public sealed record AnalysisNode(ulong Id, string RootId, ulong ParentId, string Name, AnalysisKind Kind,
    string? ObjectId = null, bool Sparse = false, bool Compressed = false, bool Cloud = false)
{
    public string NameEncoding { get; init; } = "utf8";
    public ImmutableArray<byte> RawName { get; init; } = [];
}
public sealed record StorageFact(string Id, string? ObjectId, ulong? DirectoryId, string ComponentId, string BasisId,
    AnalysisMeasurement Measurement, string Method, DateTimeOffset ObservedUtc,
    ImmutableArray<string> Includes, bool MayShareExtents = true);
public sealed record AnalysisIssue(string Code, ulong? NodeId = null, MeasurementState State = MeasurementState.Failed);
public sealed record AnalysisSnapshot(SessionIdentity Identity, ImmutableArray<RootBinding> Roots,
    ImmutableArray<AnalysisNode> Nodes, ImmutableArray<AnalysisObject> Objects, ImmutableArray<StorageFact> Facts,
    ImmutableArray<AnalysisIssue> Issues, ScanPolicy Policy, AnalysisPhase Phase, NamespaceCoverage Coverage,
    AnalysisFreshness Freshness = AnalysisFreshness.Observed, AnalysisOrigin Origin = AnalysisOrigin.LiveSession,
    AnalysisTaint Taint = AnalysisTaint.OrdinarySensitive);

public sealed record AnalysisSelection(Guid SessionId, long Epoch, long Revision, ImmutableArray<ulong> NodeIds);
public sealed record AnalysisCharge(ulong ParentId, string Label, UInt128 Value, ImmutableArray<ulong> Aliases,
    bool Shared, bool IdentityUnverified, bool DirectorySelf = false);
public sealed record AnalysisTotals(UInt128 Known, UInt128 IdentityUnverified, ulong MatchingPaths,
    ImmutableDictionary<MeasurementState, ulong> Missing, bool CompatibleBasis);
public sealed record AnalysisProjection(Guid SessionId, long Epoch, long Revision, AnalysisMetric Metric,
    ImmutableArray<AnalysisCharge> Charges, AnalysisTotals Totals, ImmutableHashSet<ulong> MatchingNodes);
