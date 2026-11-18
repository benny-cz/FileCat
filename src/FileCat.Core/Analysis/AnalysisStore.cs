using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.Analysis;

/// <summary>Bounded temporary store. Until private spill is qualified, exhausting memory preserves a partial result.</summary>
public sealed class AnalysisStore(bool canonicalPathIds = false, int pathLimit = AnalysisLimits.Observations) : IDisposable
{
    private static long s_reserved;
    private const long SharedBudget = AnalysisLimits.MemoryBytes / 2; // leave the other half for immutable publication
    private readonly List<AnalysisNode> _nodes = [];
    private readonly List<AnalysisObject> _objects = [];
    private readonly List<StorageFact> _facts = [];
    private readonly List<(int Primary, int? Extra)> _objectFactIndices = [];
    private readonly List<AnalysisIssue> _issues = [];
    private readonly Dictionary<MeasurementState, ulong> _issueTotals = [];
    private readonly Dictionary<AnalysisBoundaryKind, ulong> _boundaryTotals = [];
    private readonly Dictionary<string, int> _identities = new(StringComparer.Ordinal);
    private long _reserved;
    private bool _disposed;
    private bool _sealed;
    private readonly object _sync = new();
    public ulong IssueCount { get; private set; }
    public bool Limited { get; private set; }
    public int Count => _nodes.Count;

    // A failed unpublished scratch initialization may switch only before any path was accepted.
    // The shared memory reservation remains authoritative; this lifts only the indexed UI preview cap.
    internal void UseBoundedMemory()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sealed || _nodes.Count != 0) throw new InvalidOperationException("Memory fallback requires an empty accepting store.");
            pathLimit = AnalysisLimits.Observations;
        }
    }

    public ulong? Append(AnalysisEntry e, ulong parent, string rootId, string basis, bool includeDirectoryAllocation = false)
    {
        lock (_sync) return AppendLocked(e, parent, rootId, basis, includeDirectoryAllocation);
    }
    private ulong? AppendLocked(AnalysisEntry e, ulong parent, string rootId, string basis, bool includeDirectoryAllocation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed) return null;
        var times = e.CaptureTimes(e.ObservedUtc ?? DateTimeOffset.UtcNow);
        long cost = checked(2048L + e.RawName.Length * 4L + Encoding.UTF8.GetByteCount(e.Name) * 8L + (e.Identity?.Length ?? 0) * 8L +
            AnalysisPendingPayload.ExtraBytes(e));
        if (_nodes.Count >= Math.Clamp(pathLimit, 1, AnalysisLimits.Observations) || !Reserve(cost)) { Limited = true; return null; }
        ulong id = checked((ulong)_nodes.Count + 1);
        string? objectId = null;
        if (e.Kind == AnalysisKind.File)
        {
            bool existing = e.Identity is { } identity && _identities.TryGetValue(identity, out _);
            int at = existing ? _identities[e.Identity!] : _objects.Count;
            if (!existing)
            {
                objectId = canonicalPathIds ? AnalysisObservationCapture.Id('o', id) : (_objects.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                _objects.Add(new(objectId, e.Identity, e.LogicalLength, e.Links, e.ModifiedUtc, e.Version.Length == 0 ? null : e.Version, e.ModifiedPrecisionTicks));
                if (e.Identity is not null) _identities.Add(e.Identity, at);
                _objectFactIndices.Add((_facts.Count, e.ExtraAllocation is null ? null : _facts.Count + 1));
                _facts.Add(new("f" + id, objectId, null, "reported-data", basis, e.Allocation, basis,
                    e.ObservedUtc ?? DateTimeOffset.UtcNow, []));
                if (e.ExtraAllocation is { } extra)
                    _facts.Add(extra.Fact("e" + id, objectId, times.ObservedUtc));
            }
            else
            {
                var original = _objects[at]; objectId = original.Id;
                var indices = _objectFactIndices[at];
                bool logicalChanged = original.VersionEvidence != e.Version || original.LogicalLength != e.LogicalLength;
                if (logicalChanged)
                {
                    _objects[at] = original with { LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Changed) };
                    int factAt = indices.Primary;
                    _facts[factAt] = _facts[factAt] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Changed) };
                    Issue("changed-during-observation", id, MeasurementState.Changed);
                }
                else if (_facts[indices.Primary].Measurement != e.Allocation)
                {
                    int factAt = indices.Primary;
                    _facts[factAt] = _facts[factAt] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Changed) };
                    Issue("allocation-changed-during-observation", id, MeasurementState.Changed);
                }
                if (indices.Extra is { } extraAt)
                {
                    var fact = _facts[extraAt];
                    if (fact.Measurement.State != MeasurementState.Changed && (logicalChanged || e.ExtraAllocation?.Matches(fact) != true))
                    {
                        _facts[extraAt] = fact with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Changed) };
                        Issue("extra-data-changed-during-observation", id, MeasurementState.Changed);
                    }
                }
                else if (e.ExtraAllocation is { } extra)
                {
                    _objectFactIndices[at] = (indices.Primary, _facts.Count);
                    _facts.Add(extra.Fact("e" + id, objectId, times.ObservedUtc) with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Changed) });
                    Issue("extra-data-changed-during-observation", id, MeasurementState.Changed);
                }
            }
        }
        if (includeDirectoryAllocation && e.Kind == AnalysisKind.Directory && e.IncludedByPolicy)
            _facts.Add(new("d" + id, null, id, "directory-self", basis, e.Allocation, basis,
                e.ObservedUtc ?? DateTimeOffset.UtcNow, []));
        _nodes.Add(new(id, rootId, parent, e.Name, e.Kind, objectId, Sparse: e.Sparse, Compressed: e.Compressed, Cloud: e.Cloud)
            { NameEncoding = e.NameEncoding, RawName = e.RawName, Times = times, ObservedIdentity = e.Identity,
                MetadataState = e.MetadataState, IncludedByPolicy = e.IncludedByPolicy });
        return id;
    }
    public void Issue(string code, ulong? node = null, MeasurementState state = MeasurementState.Failed, string? rootId = null)
    {
        lock (_sync)
        {
            if (_sealed || _disposed) return;
            IssueCount = checked(IssueCount + 1);
            _issueTotals[state] = checked(_issueTotals.GetValueOrDefault(state) + 1);
            if (AnalysisCoverage.BoundaryKind(code) is { } boundary) _boundaryTotals[boundary] = checked(_boundaryTotals.GetValueOrDefault(boundary) + 1);
            if (_issues.Count < AnalysisLimits.Issues && Reserve(512)) _issues.Add(new(code, node, state) { RootId = rootId });
        }
    }
    public AnalysisSnapshot Snapshot(SessionIdentity session, ImmutableArray<RootBinding> roots, ScanPolicy policy,
        AnalysisPhase phase, NamespaceCoverage coverage)
    {
        lock (_sync)
        {
            var aliases = _nodes.Where(n => n.ObjectId is not null).ToLookup(n => n.ObjectId!, StringComparer.Ordinal);
            var issues = _issues.Where(i => i.NodeId.HasValue).ToLookup(i => i.NodeId!.Value);
            return new(session, roots, _nodes.Select(n => n with { IssueCodes = issues[n.Id].Select(i => i.Code).Distinct(StringComparer.Ordinal).ToImmutableArray() }).ToImmutableArray(),
                _objects.Select(o => o with { ObservedAliases = aliases[o.Id].Select(n => n.Id).ToImmutableArray() }).ToImmutableArray(),
                _facts.ToImmutableArray(), _issues.ToImmutableArray(), policy, phase, coverage)
                { IssueTotals = _issueTotals.ToImmutableDictionary(), BoundaryTotals = _boundaryTotals.ToImmutableDictionary() };
        }
    }
    public void Seal() { lock (_sync) _sealed = true; }
    private bool Reserve(long bytes)
    {
        while (true)
        {
            long observed = Volatile.Read(ref s_reserved);
            if (bytes > SharedBudget - observed) return false;
            if (Interlocked.CompareExchange(ref s_reserved, observed + bytes, observed) != observed) continue;
            _reserved += bytes; return true;
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _nodes.Clear(); _objects.Clear(); _facts.Clear(); _objectFactIndices.Clear(); _issues.Clear(); _issueTotals.Clear(); _boundaryTotals.Clear(); _identities.Clear();
            Interlocked.Add(ref s_reserved, -_reserved); _reserved = 0;
        }
    }
}
