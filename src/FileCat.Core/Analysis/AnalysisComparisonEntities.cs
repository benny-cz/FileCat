using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetLease
{
    // Metric entities are an internal stage; public comparison requires the two-side matcher and compatibility checks.
    internal Task<AnalysisComparisonEntities> IndexComparisonEntitiesAsync(AnalysisMetric metric, CancellationToken cancellation = default)
    {
        if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        cancellation.ThrowIfCancellationRequested();
        return _owned.UseAsync(source => AnalysisComparisonEntities.CreateAsync(source, Descriptor, metric, _io, _device, cancellation));
    }
}

public sealed partial class AnalysisObjectChargeIndex
{
    internal Task<AnalysisComparisonEntities> IndexComparisonEntitiesAsync(CancellationToken cancellation) =>
        _owned.UseAsync(state => AnalysisComparisonEntities.BuildAsync(state.Graph, Descriptor, Metric, Totals, state, _io, _device, cancellation));

    internal sealed partial class State
    {
        internal IEnumerable<AnalysisSortInput> ComparisonEntries(CancellationToken token)
        {
            for (ulong row = 1; row <= Count; row++)
            {
                token.ThrowIfCancellationRequested(); var charge = Decode(_rows!.Read(row, token));
                ulong pathRow = _graph.PathRow(charge.Path, token); var path = _graph.ReadPathRow(pathRow, token);
                var obj = charge.Object == 0 ? null : _graph.ReadObject(charge.Object, token);
                byte[] key = AnalysisComparisonEntities.EntityKey(Metric, path, pathRow, obj);
                yield return new(new(key, row), checked((int)(PathBytes(path) + key.Length + 1024L + (obj?.VersionEvidence?.Length ?? 0) * 2L)));
            }
        }
    }
}

internal sealed record AnalysisComparisonAccountingEntity(ulong Id, ulong? RepresentativePath, ulong ObservedPaths,
    AnalysisMeasurement Value, ulong? AttributionParent, string? ObservedIdentity, string? VersionEvidence,
    DateTimeOffset? ModifiedUtc, bool Fresh, uint RootMask, bool DirectorySelf, bool UniformObjectMetadata, bool UniformPathMetadata)
{
    // Scalar comparison pages deliberately defer representative-path/directory-time details.
    internal bool DetailsLoaded { get; init; }
}

/// <summary>
/// Bounded metric accounting cohorts over one retained inert revision. Aliases remain in an exact private
/// index rather than arrays. All component selection, coherence checks and LCA placement come from the
/// existing charge index; totals come from the existing summary/charge fold. No source authority is acquired.
/// </summary>
internal sealed partial class AnalysisComparisonEntities : IAsyncDisposable
{
    private static int s_active;
    internal static bool HasCapacityForPair => Volatile.Read(ref s_active) == 0;
    private readonly AnalysisOwnedDatasetIo<State> _owned;
    internal AnalysisDatasetDescriptor Descriptor { get; }
    internal AnalysisMetric Metric { get; }
    internal AnalysisTotals Totals { get; }
    internal ulong Count { get; }
    internal Task<T> UseAsync<T>(Func<State, Task<T>> use) => _owned.UseAsync(use);
    private AnalysisComparisonEntities(State state, AnalysisDatasetDescriptor descriptor, AnalysisMetric metric, AnalysisTotals totals,
        DeviceIoScheduler io, AnalysisIoDevices device)
    { _owned = new(state, io, device); Descriptor = descriptor; Metric = metric; Totals = totals; Count = state.Count; }

    internal static async Task<AnalysisComparisonEntities> CreateAsync(AnalysisIndexedGraphReader source, AnalysisDatasetDescriptor descriptor,
        AnalysisMetric metric, DeviceIoScheduler io, AnalysisIoDevices device, CancellationToken cancellation)
    {
        if (metric is AnalysisMetric.FileSize or AnalysisMetric.Files)
        {
            var summary = await AnalysisDatasetSummarizer.RunAsync(source, metric, io, device, cancellation).ConfigureAwait(false);
            return await BuildAsync(source, descriptor, metric, summary.Totals, null, io, device, cancellation).ConfigureAwait(false);
        }
        var charges = await AnalysisObjectChargeIndex.BuildAsync(source, descriptor, metric, io, device, cancellation).ConfigureAwait(false);
        return await BuildAfterProducerCloseAsync(charges, () => charges.IndexComparisonEntitiesAsync(cancellation)).ConfigureAwait(false);
    }

    // A return inside await using can lose an already-created result when producer disposal throws.
    // Publication waits for the actual producer close; failed close releases the unpublished result too.
    internal static async Task<T> BuildAfterProducerCloseAsync<T>(IAsyncDisposable producer, Func<Task<T>> build) where T : class, IAsyncDisposable
    {
        ArgumentNullException.ThrowIfNull(producer); ArgumentNullException.ThrowIfNull(build);
        T? result = null; Exception? error = null;
        try { result = await build().ConfigureAwait(false) ?? throw new InvalidDataException("The comparison producer returned no data owner."); }
        catch (Exception failure) { error = failure; }
        try { await producer.DisposeAsync().ConfigureAwait(false); }
        catch (Exception failure)
        {
            error = error is null ? failure : new AggregateException(error, failure);
            if (result is not null)
                try { await result.DisposeAsync().ConfigureAwait(false); }
                catch (Exception release) { error = new AggregateException(error, release); }
            result = null;
        }
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        return result!;
    }

    internal static async Task<AnalysisComparisonEntities> BuildAsync(AnalysisIndexedGraphReader source, AnalysisDatasetDescriptor descriptor,
        AnalysisMetric metric, AnalysisTotals totals, AnalysisObjectChargeIndex.State? charges, DeviceIoScheduler io, AnalysisIoDevices device,
        CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); var state = new State(descriptor, metric);
        if (Interlocked.Increment(ref s_active) > 2)
        { Interlocked.Decrement(ref s_active); throw new InvalidOperationException("The two indexed comparison entity places are in use. Close the other comparison first."); }
        bool transferred = false;
        try
        {
            await AnalysisIo.Run(io, device, _ => { state.Initialize(source); return true; }, cancellation).ConfigureAwait(false);
            using (var entries = state.MemberEntries(cancellation).GetEnumerator())
                state.Members = await BuildIndex(entries).ConfigureAwait(false);
            if (charges is not null)
                using (var entries = charges.ComparisonEntries(cancellation).GetEnumerator())
                    state.Charges = await BuildIndex(entries).ConfigureAwait(false);
            while (!state.Finished)
                await AnalysisIo.Run(io, device, token =>
                {
                    int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (!state.Finished && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    { token.ThrowIfCancellationRequested(); bytes += state.Step(charges, token); if (clock.ElapsedMilliseconds >= 20) break; }
                    return true;
                }, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(io, device, token => { state.Seal(token); return true; }, cancellation).ConfigureAwait(false);
            using (var entries = state.AssignmentEntries(cancellation).GetEnumerator())
                state.Assignments = await BuildIndex(entries).ConfigureAwait(false);
            await AnalysisIo.Run(io, device, _ => { state.FinishBuild(); return true; }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var result = new AnalysisComparisonEntities(state, descriptor, metric, totals, io, device); transferred = true; return result;
        }
        finally { if (!transferred) await AnalysisIo.ReleaseScratchAsync(io, device, state).ConfigureAwait(false); }

        async Task<AnalysisSortedIndexReader> BuildIndex(IEnumerator<AnalysisSortInput> entries)
        {
            var index = await AnalysisExternalSort.BuildSparseAsync(state.Workspace!, io, device, entries, cancellation).ConfigureAwait(false);
            AnalysisSortedIndexReader? reader = null;
            try { reader = await AnalysisIo.Run(io, device, _ => index.Acquire(), cancellation).ConfigureAwait(false); }
            finally
            {
                try { await AnalysisIo.ReleaseScratchAsync(io, device, index).ConfigureAwait(false); }
                catch (Exception error)
                {
                    state.NoteCloseFailure(error);
                    if (reader is not null) await AnalysisIo.ReleaseScratchAsync(io, device, reader).ConfigureAwait(false);
                    throw;
                }
            }
            return reader;
        }
    }

    internal Task<AnalysisDatasetPage<AnalysisComparisonAccountingEntity>> ReadAsync(ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) => state.ReadPage(offset, maximum, token), cancellation);
    internal Task<AnalysisComparisonAccountingEntity?> FindPathAsync(ulong path, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) => state.FindPath(path, token), cancellation);
    internal Task<AnalysisDatasetPage<AnalysisPathMetadata>> ReadMembersAsync(ulong entity, ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) => state.ReadMembers(entity, offset, maximum, token), cancellation);
    internal Task<AnalysisDatasetPage<AnalysisPathMetadata>> ReadMembersBeforeAsync(ulong entity, ulong before, int maximum, CancellationToken cancellation) =>
        _owned.RunAsync((state, token) => state.ReadMembersBefore(entity, before, maximum, token), cancellation);
    internal Task<ulong> FindMemberOffsetAsync(ulong entity, ulong path, CancellationToken cancellation) =>
        _owned.RunAsync((state, token) => state.FindMemberOffset(entity, path, token), cancellation);
    public ValueTask DisposeAsync() => _owned.DisposeAsync();

    // Self-delimiting cohort keys may later prefix exact metadata/evidence keys. They are local to one side.
    internal static byte[] EntityKey(AnalysisMetric metric, AnalysisNode path, ulong row, AnalysisObject? obj)
    {
        if (path.Kind == AnalysisKind.Directory) return [2, .. AnalysisGraphKeys.PathId(row)];
        if (metric is AnalysisMetric.FileSize or AnalysisMetric.Files || obj?.VerifiedIdentity is null)
            return [1, .. AnalysisGraphKeys.PathId(row)];
        byte[] identity = AnalysisGraphKeys.ObjectIdentity(obj);
        byte[] key = new byte[identity.Length + 2]; key[0] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(key.AsSpan(1), checked((ushort)(identity.Length - 1)));
        identity.AsSpan(1).CopyTo(key.AsSpan(3)); return key;
    }

    internal sealed partial class State(AnalysisDatasetDescriptor descriptor, AnalysisMetric metric) : IAnalysisScratchOwned
    {
        internal AnalysisScratchWorkspace? Workspace;
        private AnalysisIndexedGraphReader? _graph;
        internal AnalysisIndexedGraphReader Graph => _graph!;
        private AnalysisScratchArena? _arena;
        private AnalysisScratchVector? _writer;
        private AnalysisScratchVectorCheckpoint? _rows;
        internal AnalysisSortedIndexReader? Members, Charges, Assignments;
        internal ulong Count => _rows!.Count;
        private bool _disposed;
        private Exception? _closeFailure;
        internal void Initialize(AnalysisIndexedGraphReader source)
        {
            _graph = source.Retain(); Workspace = _graph.BorrowWorkspace();
            _arena = Workspace.CreateArena(); _writer = new(_arena); _arena = null;
        }
        private (AnalysisObject? Object, ulong Row) Object(AnalysisNode node, CancellationToken token)
        {
            if (node.ObjectId is not { } id) return (null, 0);
            var range = _graph!.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: token);
            if (range.Count != 1) throw AnalysisIndexedGraph.Invalid();
            ulong row = _graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value;
            return (_graph.ReadObject(row, token), row);
        }
        internal IEnumerable<AnalysisSortInput> MemberEntries(CancellationToken token)
        {
            for (ulong row = 1, count = _graph!.Count(AnalysisIndexedGraph.Paths); row <= count; row++)
            {
                token.ThrowIfCancellationRequested(); var node = _graph.ReadPathRow(row, token);
                bool eligible = node.IncludedByPolicy && (node.Kind is AnalysisKind.File or AnalysisKind.Unknown ||
                    metric == AnalysisMetric.OnDisk && node.Kind == AnalysisKind.Directory &&
                    _graph.Find(AnalysisIndexedGraphRevision.FactOwners, AnalysisGraphKeys.FactOwner(null, node.Id), cancellation: token).Count != 0);
                var obj = eligible ? Object(node, token).Object : null;
                byte[]? key = eligible ? EntityKey(metric, node, row, obj) : null;
                yield return new(key is null ? null : new(key, row), checked((int)(PathBytes(node) + 1024L + (key?.Length ?? 0) + (obj?.VersionEvidence?.Length ?? 0) * 2L)));
            }
        }
        internal void Seal(CancellationToken token)
        { _writer!.Flush(token); _rows = _writer.CaptureCommitted(); _writer.Dispose(); _writer = null; }
        internal void NoteCloseFailure(Exception error) => _closeFailure = _closeFailure is null ? error : new AggregateException(_closeFailure, error);
        internal void FinishBuild()
        {
            var charges = Charges; Charges = null; var workspace = Workspace; Workspace = null;
            foreach (IDisposable? owned in new IDisposable?[] { charges, workspace })
                try { owned?.Dispose(); } catch (Exception error) { NoteCloseFailure(error); }
            if (_closeFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_closeFailure).Throw();
        }
        internal IEnumerable<AnalysisSortInput> AssignmentEntries(CancellationToken token)
        {
            for (ulong entity = 1; entity <= Count; entity++)
            {
                var row = Decode(_rows!.Read(entity, token));
                for (ulong at = 0; at < row.Count; at++)
                {
                    token.ThrowIfCancellationRequested(); ulong path = Members!.Read(row.First + at, token).Value;
                    yield return new(new(AnalysisGraphKeys.PathId(path), entity), 16);
                }
            }
        }
        private static long PathBytes(AnalysisNode n) => 1024L + n.Name.Length * 2L + n.RawName.Length;
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; Exception? error = _closeFailure;
            foreach (IDisposable? owned in new IDisposable?[] { Assignments, Charges, Members, _rows, _writer, _arena, Workspace, _graph })
            { try { owned?.Dispose(); } catch (Exception failure) { error = error is null ? failure : new AggregateException(error, failure); } }
            if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            Interlocked.Decrement(ref s_active);
        }
    }
}
