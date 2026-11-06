using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record AnalysisDatasetDescriptor(SessionIdentity Identity, ImmutableArray<RootBinding> Roots, ScanPolicy Policy,
    AnalysisPhase Phase, NamespaceCoverage Coverage, AnalysisFreshness Freshness, AnalysisOrigin Origin, AnalysisTaint Taint,
    AnalysisPersistence Persistence, ulong Paths, ulong Objects, ulong Facts, ulong SampledIssues,
    ImmutableDictionary<MeasurementState, ulong> IssueTotals, ImmutableDictionary<AnalysisBoundaryKind, ulong> BoundaryTotals, AnalysisDatasetCoverage CoverageDetails)
{
    public ulong Issues => IssueTotals.Values.Aggregate(0UL, (sum, value) => checked(sum + value));
}

// Related issues and aliases have their own paged queries; a base metadata row does not masquerade as
// a fully materialized AnalysisNode/AnalysisObject with an empty related-record list.
public sealed record AnalysisPathMetadata(ulong Id, string RootId, ulong ParentId, string Name, AnalysisKind Kind, string? ObjectId,
    bool? Sparse, bool? Compressed, bool? Cloud, MeasurementState MetadataState, bool IncludedByPolicy, string NameEncoding,
    ImmutableArray<byte> RawName, AnalysisObservationTimes? Times, string? ObservedIdentity)
{
    internal static AnalysisPathMetadata From(AnalysisNode n) => new(n.Id, n.RootId, n.ParentId, n.Name, n.Kind, n.ObjectId, n.Sparse, n.Compressed, n.Cloud,
        n.MetadataState, n.IncludedByPolicy, n.NameEncoding, n.RawName, n.Times, n.ObservedIdentity);
}
public sealed record AnalysisObjectMetadata(string Id, string? VerifiedIdentity, AnalysisMeasurement LogicalLength, AnalysisMeasurement LinkCount,
    DateTimeOffset? ModifiedUtc, string? VersionEvidence, ulong ModifiedPrecisionTicks)
{
    internal static AnalysisObjectMetadata From(AnalysisObject o) => new(o.Id, o.VerifiedIdentity, o.LogicalLength, o.LinkCount, o.ModifiedUtc, o.VersionEvidence, o.ModifiedPrecisionTicks);
}
public sealed record AnalysisDatasetPage<T>(ulong Offset, ulong Total, ImmutableArray<T> Rows)
{
    public ulong NextOffset => checked(Offset + (ulong)Rows.Length);
    public bool HasMore => NextOffset < Total;
}

/// <summary>An immutable data owner. All filesystem-backed queries and disposal enter captured Background I/O.</summary>
public sealed class AnalysisDataset : IAsyncDisposable
{
    private readonly AnalysisOwnedDatasetIo<AnalysisIndexedGraphRevision> _owned;
    private readonly DeviceIoScheduler _io; private readonly AnalysisIoDevices _device;
    public AnalysisDatasetDescriptor Descriptor { get; }
    internal AnalysisDataset(AnalysisIndexedGraphRevision revision, DeviceIoScheduler io, AnalysisIoDevices device)
    {
        ArgumentNullException.ThrowIfNull(revision); ArgumentNullException.ThrowIfNull(io);
        ArgumentNullException.ThrowIfNull(device);
        var h = revision.Header; _io = io; _device = device;
        Descriptor = new(h.Identity, h.Roots, h.Policy, h.Phase, h.Coverage, h.Freshness, h.Origin, h.Taint, h.Persistence,
            revision.Count(AnalysisIndexedGraph.Paths), revision.Count(AnalysisIndexedGraph.Objects), revision.Count(AnalysisIndexedGraph.Facts), revision.Count(AnalysisIndexedGraph.Issues), h.IssueTotals, h.BoundaryTotals,
            h.CoverageDetails ?? throw new InvalidDataException("Indexed coverage was not validated."));
        _owned = new(revision, io, device);
    }
    public Task<AnalysisDatasetLease> AcquireAsync(CancellationToken cancellation = default) =>
        _owned.RunAsync((revision, _) => new AnalysisDatasetLease(revision.Acquire(), Descriptor, _io, _device), cancellation);
    public ValueTask DisposeAsync() => _owned.DisposeAsync();
}

/// <summary>Read-only observations; no member binds a source, opens content or authorizes a job.</summary>
public sealed partial class AnalysisDatasetLease : IAsyncDisposable
{
    private readonly AnalysisOwnedDatasetIo<AnalysisIndexedGraphReader> _owned;
    private readonly DeviceIoScheduler _io; private readonly AnalysisIoDevices _device;
    public AnalysisDatasetDescriptor Descriptor { get; }
    internal AnalysisDatasetLease(AnalysisIndexedGraphReader reader, AnalysisDatasetDescriptor descriptor, DeviceIoScheduler io, AnalysisIoDevices device)
    { Descriptor = descriptor; _io = io; _device = device; _owned = new(reader, io, device); }
    public Task<AnalysisDatasetLease> RetainAsync(CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, _) => new AnalysisDatasetLease(reader.Retain(), Descriptor, _io, _device), cancellation);
    public Task<AnalysisDatasetPage<AnalysisPathMetadata>> ReadPathsAsync(ulong offset, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, token) => Page(offset, maximum, Descriptor.Paths, n => AnalysisPathMetadata.From(reader.ReadPathRow(n + 1, token)), PathBytes, token), cancellation);
    public Task<AnalysisPathMetadata> ReadPathAsync(ulong id, CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, token) => AnalysisPathMetadata.From(reader.ReadPath(id, token)), cancellation);
    public Task<AnalysisDatasetPage<AnalysisObjectMetadata>> ReadObjectsAsync(ulong offset, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, token) => Page(offset, maximum, Descriptor.Objects, n => AnalysisObjectMetadata.From(reader.ReadObject(n + 1, token)),
            o => 256 + o.Id.Length * 2 + (o.VerifiedIdentity?.Length ?? 0) * 2 + (o.VersionEvidence?.Length ?? 0) * 2, token), cancellation);
    public Task<AnalysisObjectMetadata?> ReadObjectAsync(string objectId, CancellationToken cancellation = default) =>
        _owned.RunAsync<AnalysisObjectMetadata?>((reader, token) =>
        {
            var range = reader.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(objectId), cancellation: token);
            if (range.Count == 0) return null;
            if (range.Count != 1) throw new InvalidDataException("The object index is ambiguous.");
            return AnalysisObjectMetadata.From(reader.ReadObject(reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value, token));
        }, cancellation);
    public Task<AnalysisDatasetPage<StorageFact>> ReadFactsAsync(ulong offset, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, token) => Page(offset, maximum, Descriptor.Facts, n => reader.ReadFact(n + 1, token), FactBytes, token), cancellation);
    public Task<AnalysisDatasetPage<AnalysisIssue>> ReadIssuesAsync(ulong offset, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, token) => Page(offset, maximum, Descriptor.SampledIssues, n => reader.ReadIssue(n + 1, token), IssueBytes, token), cancellation);
    public Task<AnalysisDatasetPage<AnalysisPathMetadata>> ReadChildrenAsync(int rootOrdinal, ulong parent, ulong offset, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, token) =>
        {
            if ((uint)rootOrdinal >= (uint)Descriptor.Roots.Length) throw new ArgumentOutOfRangeException(nameof(rootOrdinal));
            reader.RequireParent(parent, token);
            var range = reader.Children(rootOrdinal, parent, token);
            return Page(offset, maximum, range.Count, n => AnalysisPathMetadata.From(reader.ReadPathRow(reader.ReadIndex(AnalysisIndexedGraphRevision.Children, range.First + n, token).Value, token)), PathBytes, token);
        }, cancellation);
    public Task<AnalysisDatasetPage<AnalysisPathMetadata>> ReadAliasesAsync(string objectId, ulong offset, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, token) =>
        {
            var range = reader.Aliases(objectId, token);
            return Page(offset, maximum, range.Count, n => AnalysisPathMetadata.From(reader.ReadPathRow(reader.ReadIndex(AnalysisIndexedGraphRevision.Aliases, range.First + n, token).Value, token)), PathBytes, token);
        }, cancellation);
    public Task<AnalysisDatasetPage<StorageFact>> ReadObjectFactsAsync(string objectId, ulong offset, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, token) =>
        {
            var range = reader.Find(AnalysisIndexedGraphRevision.FactOwners, AnalysisGraphKeys.FactOwner(objectId, null), cancellation: token);
            return Page(offset, maximum, range.Count, n => reader.ReadFact(reader.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, range.First + n, token).Value, token), FactBytes, token);
        }, cancellation);
    public Task<AnalysisDatasetPage<AnalysisIssue>> ReadPathIssuesAsync(ulong node, ulong offset, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, token) =>
        {
            _ = reader.PathRow(node, token);
            var range = reader.Find(AnalysisIndexedGraphRevision.IssueOwners, AnalysisGraphKeys.IssueOwner(node, null, Descriptor.Roots), cancellation: token);
            return Page(offset, maximum, range.Count, n => reader.ReadIssue(reader.ReadIndex(AnalysisIndexedGraphRevision.IssueOwners, range.First + n, token).Value, token), IssueBytes, token);
        }, cancellation);
    public Task<AnalysisDatasetPage<StorageFact>> ReadDirectoryFactsAsync(ulong node, ulong offset, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((reader, token) =>
        {
            _ = reader.PathRow(node, token);
            var range = reader.Find(AnalysisIndexedGraphRevision.FactOwners, AnalysisGraphKeys.FactOwner(null, node), cancellation: token);
            return Page(offset, maximum, range.Count, n => reader.ReadFact(reader.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, range.First + n, token).Value, token), FactBytes, token);
        }, cancellation);
    private static AnalysisDatasetPage<T> Page<T>(ulong offset, int maximum, ulong total, Func<ulong, T> read, Func<T, long> cost, CancellationToken cancellation)
    {
        if (maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (offset > total) throw new ArgumentOutOfRangeException(nameof(offset));
        var rows = ImmutableArray.CreateBuilder<T>(); long bytes = 0;
        while (offset + (ulong)rows.Count < total && rows.Count < maximum)
        {
            cancellation.ThrowIfCancellationRequested(); T row = read(offset + (ulong)rows.Count); long next = cost(row);
            if (next is < 0 or > AnalysisLimits.BatchBytes) throw new InvalidDataException("The dataset row exceeds the bounded page payload.");
            if (next > AnalysisLimits.BatchBytes - bytes) break;
            rows.Add(row); bytes += next;
        }
        return new(offset, total, rows.ToImmutable());
    }
    private static long PathBytes(AnalysisPathMetadata p) => 320L + p.Name.Length * 2L + p.RawName.Length + p.RootId.Length * 2L + (p.ObjectId?.Length ?? 0) * 2L + (p.ObservedIdentity?.Length ?? 0) * 2L;
    private static long FactBytes(StorageFact f) => 384L + f.Id.Length * 2L + (f.ObjectId?.Length ?? 0) * 2L + f.ComponentId.Length * 2L + f.BasisId.Length * 2L + f.Method.Length * 2L + f.Includes.Sum(s => 32L + s.Length * 2L);
    private static long IssueBytes(AnalysisIssue i) => 128L + i.Code.Length * 2L + (i.RootId?.Length ?? 0) * 2L;
    public ValueTask DisposeAsync() => _owned.DisposeAsync();
}

/// <summary>Real queued/running work retains the owner through actual completion, including scheduler shutdown.</summary>
internal sealed class AnalysisOwnedDatasetIo<T>(T resource, DeviceIoScheduler io, AnalysisIoDevices device) : IAsyncDisposable where T : class, IAnalysisScratchOwned
{
    private T? _resource = resource;
    private readonly object _gate = new(); private int _active; private bool _disposed, _closing;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task<R> RunAsync<R>(Func<T, CancellationToken, R> read, CancellationToken cancellation) =>
        UseAsync(captured => AnalysisIo.Run(io, device, token => read(captured, token), cancellation));
    // Compound operations admit their own bounded I/O batches. Their ownership spans the entire
    // operation rather than only the task that started a nested asynchronous operation.
    internal Task<R> UseAsync<R>(Func<T, Task<R>> read)
    {
        T captured;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_active >= AnalysisLimits.IoRequests) throw new InvalidOperationException("This dataset already has eight queued or running requests.");
            _active++; captured = _resource!;
        }
        return Execute();
        async Task<R> Execute()
        {
            try { return await read(captured).ConfigureAwait(false); }
            finally
            {
                bool close;
                lock (_gate) { _active--; close = BeginClose(); }
                if (close) await CloseAsync().ConfigureAwait(false);
            }
        }
    }
    public ValueTask DisposeAsync()
    {
        bool close;
        lock (_gate) { _disposed = true; close = BeginClose(); }
        if (close) _ = CloseAsync();
        return new(_closed.Task);
    }
    private bool BeginClose() { if (!_disposed || _active != 0 || _closing) return false; _closing = true; return true; }
    private async Task CloseAsync()
    {
        T captured;
        lock (_gate) { captured = _resource!; _resource = null; }
        try { await AnalysisIo.ReleaseScratchAsync(io, device, captured).ConfigureAwait(false); _closed.TrySetResult(); }
        catch (Exception error) { _closed.TrySetException(error); }
    }
}
