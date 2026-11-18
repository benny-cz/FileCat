using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;

namespace FileCat.Core.Analysis;

internal enum AnalysisDuplicateCandidateState
{
    Absent, NotRegularFile, ExcludedByPolicy, Cloud, UnknownSize, UnverifiedObject, HardLinkAlias, NoSizePeer, Candidate
}

internal sealed record AnalysisDuplicateCandidate(ulong PathId, string? RootId, string Name, UInt128? Bytes,
    AnalysisDuplicateCandidateState State);

/// <summary>Read cost and candidate provenance from one immutable revision. Carries no live source authority.</summary>
internal sealed record AnalysisDuplicateEstimate(SessionIdentity Identity, AnalysisOrigin Origin, NamespaceCoverage Coverage,
    BigInteger KnownBytes, int UnknownBytePaths, int CandidateObjects, ImmutableArray<AnalysisDuplicateCandidate> Rows);

internal sealed class AnalysisDuplicateEstimates
{
    internal const int MaximumMarks = 4096;
    private readonly HashSet<string> _activeRoots;
    private readonly List<AnalysisDuplicateCandidate> _rows = [];
    private readonly HashSet<string> _objects = new(StringComparer.Ordinal);
    private readonly Dictionary<UInt128, int> _sizes = [];

    internal AnalysisDuplicateEstimates(ImmutableArray<RootBinding> roots, SessionIdentity identity, AnalysisSelection selection)
    {
        if (selection.SessionId != identity.SessionId || selection.Epoch != identity.Epoch || selection.Revision != identity.Revision ||
            selection.Kind != AnalysisSelectionKind.ExplicitPaths || selection.NodeIds.IsDefaultOrEmpty)
            throw new InvalidOperationException("Select marked paths from this captured analysis revision.");
        if (selection.NodeIds.Length > MaximumMarks)
            throw new InvalidOperationException($"Narrow the marked selection to at most {MaximumMarks:N0} entries for duplicate review.");
        if (selection.NodeIds.Contains(0) || selection.NodeIds.Distinct().Count() != selection.NodeIds.Length)
            throw new ArgumentException("Duplicate review requires distinct captured path IDs.", nameof(selection));
        _activeRoots = roots.Where(root => root.Disposition == AnalysisRootDisposition.Active)
            .Select(root => root.Id).ToHashSet(StringComparer.Ordinal);
    }

    internal void Add(ulong id, AnalysisNode? path, AnalysisObject? observed)
    {
        if (path is not null && path.Id != id || observed is not null && path?.ObjectId != observed.Id)
            throw new InvalidDataException("The recorded path and object evidence disagree.");
        var size = observed?.LogicalLength.Value;
        var state = path switch
        {
            null => AnalysisDuplicateCandidateState.Absent,
            { Kind: not AnalysisKind.File } => AnalysisDuplicateCandidateState.NotRegularFile,
            { IncludedByPolicy: false } => AnalysisDuplicateCandidateState.ExcludedByPolicy,
            _ when !_activeRoots.Contains(path.RootId) => AnalysisDuplicateCandidateState.ExcludedByPolicy,
            { Cloud: true } => AnalysisDuplicateCandidateState.Cloud,
            _ when size is null || path.MetadataState != MeasurementState.Known => AnalysisDuplicateCandidateState.UnknownSize,
            _ when string.IsNullOrEmpty(observed?.VerifiedIdentity) || string.IsNullOrEmpty(observed.VersionEvidence) ||
                observed.VerifiedIdentity != path.ObservedIdentity => AnalysisDuplicateCandidateState.UnverifiedObject,
            _ when !_objects.Add(observed!.Id) => AnalysisDuplicateCandidateState.HardLinkAlias,
            _ => AnalysisDuplicateCandidateState.Candidate
        };
        if (state == AnalysisDuplicateCandidateState.Candidate)
            _sizes[size!.Value] = _sizes.GetValueOrDefault(size.Value) + 1;
        _rows.Add(new(id, path?.RootId, path is null ? "Missing observation" : AnalysisNames.DisplayName(path, 256), size, state));
    }

    internal AnalysisDuplicateEstimate Finish(SessionIdentity identity, AnalysisOrigin origin, NamespaceCoverage coverage)
    {
        int unknown = 0, candidates = 0;
        BigInteger bytes = BigInteger.Zero;
        var rows = ImmutableArray.CreateBuilder<AnalysisDuplicateCandidate>(_rows.Count);
        foreach (var row in _rows)
        {
            if (row.State == AnalysisDuplicateCandidateState.UnknownSize) unknown++;
            if (row.State != AnalysisDuplicateCandidateState.Candidate) { rows.Add(row); continue; }
            UInt128 size = row.Bytes!.Value;
            if (_sizes[size] < 2) { rows.Add(row with { State = AnalysisDuplicateCandidateState.NoSizePeer }); continue; }
            bytes += (BigInteger)size;
            candidates++;
            rows.Add(row);
        }
        return new(identity, origin, coverage, bytes, unknown, candidates, rows.MoveToImmutable());
    }

    internal static async Task<AnalysisDuplicateEstimate> FromSnapshotAsync(AnalysisSnapshotLease source, AnalysisSelection selection,
        CancellationToken cancellation)
    {
        using var retained = source.Retain();
        using var admission = AnalysisExplorerAdmission.Acquire();
        return await Task.Run(() =>
        {
            var snapshot = retained.Snapshot;
            var estimate = new AnalysisDuplicateEstimates(snapshot.Roots, snapshot.Identity, selection);
            var wanted = selection.NodeIds.ToHashSet();
            var paths = new Dictionary<ulong, AnalysisNode>();
            foreach (ulong id in selection.NodeIds)
            {
                cancellation.ThrowIfCancellationRequested();
                if (id <= (ulong)snapshot.Nodes.Length && snapshot.Nodes[(int)id - 1] is { } node && node.Id == id)
                    paths.Add(id, node);
            }
            if (paths.Count != wanted.Count)
                foreach (var path in snapshot.Nodes)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (wanted.Contains(path.Id)) paths.TryAdd(path.Id, path);
                    if (paths.Count == wanted.Count) break;
                }
            var objectIds = paths.Values.Where(path => path.ObjectId is not null).Select(path => path.ObjectId!).ToHashSet(StringComparer.Ordinal);
            var objects = new Dictionary<string, AnalysisObject>(StringComparer.Ordinal);
            if (objectIds.Count != 0)
                foreach (var observed in snapshot.Objects)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (objectIds.Contains(observed.Id)) objects.Add(observed.Id, observed);
                    if (objects.Count == objectIds.Count) break;
                }
            foreach (ulong id in selection.NodeIds.Order())
            {
                cancellation.ThrowIfCancellationRequested();
                var path = paths.GetValueOrDefault(id);
                estimate.Add(id, path, path?.ObjectId is { } objectId ? objects.GetValueOrDefault(objectId) : null);
            }
            return estimate.Finish(snapshot.Identity, snapshot.Origin, snapshot.Coverage);
        }, cancellation).ConfigureAwait(false);
    }
}

public sealed partial class AnalysisDatasetLease
{
    internal async Task<AnalysisDuplicateEstimate> EstimateDuplicatesAsync(AnalysisSelection selection, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var admission = AnalysisExplorerAdmission.Acquire();
        var estimate = new AnalysisDuplicateEstimates(Descriptor.Roots, Descriptor.Identity, selection);
        var ids = selection.NodeIds.Order().ToArray();
        int next = 0;
        return await _owned.UseAsync(async reader =>
        {
            while (next < ids.Length)
                await AnalysisIo.Run(_io, _device, token =>
                {
                    var clock = Stopwatch.StartNew();
                    long bytes = 0;
                    int records = 0;
                    while (next < ids.Length && records++ < AnalysisLimits.BatchRecords &&
                        bytes < AnalysisLimits.BatchBytes && clock.ElapsedMilliseconds < 20)
                    {
                        token.ThrowIfCancellationRequested();
                        ulong id = ids[next++];
                        var path = reader.FindPathRow(id, token) is { } row ? reader.ReadPathRow(row, token) : null;
                        var observed = path?.ObjectId is { } objectId ? ReadRequiredObservedObject(reader, objectId, token) : null;
                        if (path is not null) bytes += AnalysisIndexedFilter.Cost(path);
                        if (observed is not null) bytes += 256L + (observed.VerifiedIdentity?.Length ?? 0) * 2L + (observed.VersionEvidence?.Length ?? 0) * 2L;
                        estimate.Add(id, path, observed);
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return estimate.Finish(Descriptor.Identity, Descriptor.Origin, Descriptor.Coverage);
        }).ConfigureAwait(false);
    }
}
