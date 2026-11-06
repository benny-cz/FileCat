using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

// Inventory records are observations. None of these types authorizes a filesystem operation.
public enum MeasurementState { Known, Pending, Unsupported, Excluded, Failed, Changed }
public enum AnalysisKind { Directory, File, Link, Special, Unknown }
public enum AnalysisMetric { FileSize, UniqueFileSize, OnDisk, Files }
public enum AnalysisComponentPreset { Standard, DetailedAllocation }
public enum AnalysisBoundaryKind { Mount, NavigationLink, CloudDirectory, SpecialEntry, UnverifiedRootOverlap }
public enum AnalysisPhase { Ready, Scanning, Pausing, Paused, Refreshing, Finished, Stopped, Failed }
public enum NamespaceCoverage { CompleteUnderPolicy, Partial, Unknown }
public enum AnalysisFreshness { Observed, PossiblyStale, SourceUnavailable, IdentityMismatch }
public enum AnalysisOrigin { LiveSession, OwnSavedInventory, ImportedInventory }
public enum AnalysisPersistence { Temporary, SavingRevision, NamedSaved, RollingSaved, SaveFailed }
[Flags]
public enum AnalysisTaint { OrdinarySensitive = 1, PrivilegedDerived = 2, ImportedUnverified = 4 }
public enum IdentityAssurance { Unknown, Verified }
public enum AnalysisAssurance { ObservedMetadata, ReviewedObject, ComparedContent }
public enum AnalysisNameRules { Provider, Windows, Unix }
public enum AnalysisRootDisposition { Active, Covered, ExcludedUnverifiedOverlap, UnavailablePrivateBoundary, ExcludedPrivateState }
public enum AnalysisSelectionKind { ExplicitPaths, WholeFolder, FilteredMembersOnly, RealGroupMembers, VisibleMatches }

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
    DateTimeOffset StartedUtc, DateTimeOffset ObservedUtc)
{
    public string TimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
    public AnalysisHostProfileIdentity? HostProfileIdentity => AnalysisHostProfileIdentity.TryParse(HostProfile, out var identity) ? identity : null;
}
public sealed record RootBinding(string Id, string Scheme, string DisplayLocator, string CanonicalLocator,
    string? SourceIdentity, IdentityAssurance Assurance, string Label)
{
    public AnalysisKind Kind { get; init; } = AnalysisKind.Directory;
    public AnalysisNameRules NameRules { get; init; } = AnalysisNameRules.Provider;
    public string TraversalBoundary { get; init; } = "stay-on-source";
    public string? ConnectionIdentityReference { get; init; }
    public AnalysisRootDisposition Disposition { get; init; }
    public string? CoveredByRootId { get; init; }
    public string? CoveredRelativePath { get; init; }
    public string? ObservedObjectIdentity { get; init; }
}
public sealed record AnalysisObject(string Id, string? VerifiedIdentity, AnalysisMeasurement LogicalLength,
    AnalysisMeasurement LinkCount, DateTimeOffset? ModifiedUtc = null, string? VersionEvidence = null,
    ulong ModifiedPrecisionTicks = 1)
{
    public ImmutableArray<ulong> ObservedAliases { get; init; } = [];
}
public sealed record AnalysisObservationTimes(DateTimeOffset ObservedUtc, DateTimeOffset? ModifiedUtc, ulong ModifiedPrecisionTicks,
    DateTimeOffset? CreatedUtc = null, ulong? CreatedPrecisionTicks = null, DateTimeOffset? ChangedUtc = null, ulong? ChangedPrecisionTicks = null);
public sealed record AnalysisNode(ulong Id, string RootId, ulong ParentId, string Name, AnalysisKind Kind,
    string? ObjectId = null, bool? Sparse = null, bool? Compressed = null, bool? Cloud = null)
{
    public MeasurementState MetadataState { get; init; } = MeasurementState.Known;
    public bool IncludedByPolicy { get; init; } = true;
    public string NameEncoding { get; init; } = "utf8";
    public ImmutableArray<byte> RawName { get; init; } = [];
    public AnalysisObservationTimes? Times { get; init; }
    public ImmutableArray<string> IssueCodes { get; init; } = [];
    public string? ObservedIdentity { get; init; }
}
public sealed record StorageFact(string Id, string? ObjectId, ulong? DirectoryId, string ComponentId, string BasisId,
    AnalysisMeasurement Measurement, string Method, DateTimeOffset ObservedUtc,
    ImmutableArray<string> Includes, bool MayShareExtents = true);
public sealed record AnalysisIssue(string Code, ulong? NodeId = null, MeasurementState State = MeasurementState.Failed)
{
    public string? RootId { get; init; }
}
public sealed record AnalysisCoverageRecord(ulong EnumerationRequested, ulong EnumerationCompleted, ulong EnumerationFailed,
    bool DenominatorFinal, ulong Boundaries, ulong ExcludedIssues, ulong FailedIssues, ulong PendingIssues,
    ImmutableDictionary<MeasurementState, ulong> MeasurementStates, ulong IdentityVerifiedPaths, ulong IdentityUnverifiedPaths,
    ImmutableArray<ulong> StaleScopeNodes)
{
    public ulong EnumerationPending => checked(EnumerationRequested - EnumerationCompleted - EnumerationFailed);
    public ImmutableArray<string> StaleRootIds { get; init; } = [];
    public ImmutableDictionary<AnalysisBoundaryKind, ulong> BoundaryTotals { get; init; } = ImmutableDictionary<AnalysisBoundaryKind, ulong>.Empty;
    public AnalysisCoverageQualification? Qualification { get; init; }
}
/// <summary>Historical knowledge limits, distinct from zero counts or complete stale-scope evidence.</summary>
public sealed record AnalysisCoverageQualification(bool EnumerationKnown, ulong EligibleDirectories,
    bool StaleScopesComplete, bool HasUnlocatedStaleScopes);
public sealed record AnalysisSnapshot(SessionIdentity Identity, ImmutableArray<RootBinding> Roots,
    ImmutableArray<AnalysisNode> Nodes, ImmutableArray<AnalysisObject> Objects, ImmutableArray<StorageFact> Facts,
    ImmutableArray<AnalysisIssue> Issues, ScanPolicy Policy, AnalysisPhase Phase, NamespaceCoverage Coverage,
    AnalysisFreshness Freshness = AnalysisFreshness.Observed, AnalysisOrigin Origin = AnalysisOrigin.LiveSession,
    AnalysisTaint Taint = AnalysisTaint.OrdinarySensitive)
{
    public AnalysisPersistence Persistence { get; init; } = AnalysisPersistence.Temporary;
    public AnalysisCoverageRecord? CoverageDetails { get; init; }
    public ImmutableDictionary<MeasurementState, ulong> IssueTotals { get; init; } = ImmutableDictionary<MeasurementState, ulong>.Empty;
    public ImmutableDictionary<AnalysisBoundaryKind, ulong> BoundaryTotals { get; init; } = ImmutableDictionary<AnalysisBoundaryKind, ulong>.Empty;
}

public sealed record AnalysisSelection(Guid SessionId, long Epoch, long Revision, ImmutableArray<ulong> NodeIds)
{
    public ImmutableArray<string> ObjectIds { get; init; } = [];
    public AnalysisSelectionKind Kind { get; init; } = AnalysisSelectionKind.ExplicitPaths;
    public string ScopeSummary { get; init; } = "";
    public string FilterSummary { get; init; } = "";
    public ulong ExactMemberCount => (ulong)NodeIds.Length;
}
public sealed record AnalysisCharge(ulong ParentId, string Label, UInt128 Value, ImmutableArray<ulong> Aliases,
    bool Shared, bool IdentityUnverified, bool DirectorySelf = false);
public sealed record AnalysisTotals(UInt128 Known, UInt128 IdentityUnverified, ulong MatchingPaths,
    ImmutableDictionary<MeasurementState, ulong> Missing, bool CompatibleBasis);
public sealed record AnalysisProjection(Guid SessionId, long Epoch, long Revision, AnalysisMetric Metric,
    ImmutableArray<AnalysisCharge> Charges, AnalysisTotals Totals, ImmutableHashSet<ulong> MatchingNodes)
{
    public ImmutableDictionary<ulong, MeasurementState> UnmeasuredStates { get; init; } = ImmutableDictionary<ulong, MeasurementState>.Empty;
    public AnalysisTaint Taint { get; init; } = AnalysisTaint.OrdinarySensitive;
    public ImmutableHashSet<ulong> UnknownFilterNodes { get; init; } = ImmutableHashSet<ulong>.Empty;
    public bool IncludesUnknownFilterResults { get; init; }
    public string FilterText { get; init; } = "";
}
