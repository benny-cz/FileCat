using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetLease
{
    // Internal until metric/entity matching, coverage and comparison presentation consume this index.
    internal Task<AnalysisComparisonPaths> MatchPathsAsync(AnalysisDatasetLease previous,
        IReadOnlyDictionary<string, string>? structuralRoots = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(previous);
        cancellation.ThrowIfCancellationRequested();
        var devices = _device;
        foreach (string key in previous._device.Keys) devices = devices.Include(key);
        var mapping = AnalysisComparison.MapRoots(previous.Descriptor.Roots, previous.Descriptor.Identity,
            Descriptor.Roots, Descriptor.Identity, structuralRoots);
        return _owned.UseAsync(current => previous._owned.UseAsync(old =>
            AnalysisComparisonPaths.BuildAsync(old, current, previous.Descriptor, Descriptor, mapping, _io, devices, cancellation)));
    }
}

internal sealed record AnalysisPathCorrespondence(AnalysisPathMetadata? Previous, AnalysisPathMetadata? Current, bool Mapped);

/// <summary>
/// Exact path correspondence over two retained, inert revisions. Each child is looked up by its tagged
/// raw component under the already matched parent. No full-path strings, hashes, host normalization,
/// identities or zero-value baselines are used. Metric/alias matching is a separate comparison stage.
/// </summary>
internal sealed class AnalysisComparisonPaths : IAsyncDisposable
{
    private static int s_active;
    private readonly AnalysisOwnedDatasetIo<State> _owned;
    internal AnalysisDatasetDescriptor Previous { get; }
    internal AnalysisDatasetDescriptor Current { get; }
    internal ImmutableDictionary<string, string> RootMapping { get; }

    private AnalysisComparisonPaths(State state, AnalysisDatasetDescriptor previous, AnalysisDatasetDescriptor current,
        ImmutableDictionary<string, string> mapping, DeviceIoScheduler io, AnalysisIoDevices devices)
    { _owned = new(state, io, devices); Previous = previous; Current = current; RootMapping = mapping; }

    internal static async Task<AnalysisComparisonPaths> BuildAsync(AnalysisIndexedGraphReader previous, AnalysisIndexedGraphReader current,
        AnalysisDatasetDescriptor previousDescriptor, AnalysisDatasetDescriptor currentDescriptor, ImmutableDictionary<string, string> mapping,
        DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var state = new State(mapping, currentDescriptor.Roots.Length);
        if (Interlocked.CompareExchange(ref s_active, 1, 0) != 0)
            throw new InvalidOperationException("The indexed comparison path budget is in use. Close the other indexed comparison first.");
        bool transferred = false;
        try
        {
            await AnalysisIo.Run(io, devices, _ => { state.Initialize(previous, current); return true; }, cancellation).ConfigureAwait(false);
            while (!state.Finished)
                await AnalysisIo.Run(io, devices, token =>
                {
                    int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (!state.Finished && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    { token.ThrowIfCancellationRequested(); bytes += state.Step(token); if (clock.ElapsedMilliseconds >= 20) break; }
                    return true;
                }, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(io, devices, token => { state.Seal(token); return true; }, cancellation).ConfigureAwait(false);
            using (var entries = state.ReverseEntries(cancellation).GetEnumerator())
            {
                var reverse = await AnalysisExternalSort.BuildSparseAsync(state.Workspace!, io, devices, entries, cancellation).ConfigureAwait(false);
                try { await AnalysisIo.Run(io, devices, _ => { state.Reverse = reverse.Acquire(); return true; }, cancellation).ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(io, devices, reverse).ConfigureAwait(false); }
            }
            await AnalysisIo.Run(io, devices, _ => { state.Workspace!.Dispose(); state.Workspace = null; return true; }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var result = new AnalysisComparisonPaths(state, previousDescriptor, currentDescriptor, mapping, io, devices);
            transferred = true; return result;
        }
        finally
        {
            // A failed actual close preserves admission rather than claiming the resource was released.
            if (!transferred) await AnalysisIo.ReleaseScratchAsync(io, devices, state).ConfigureAwait(false);
        }
    }

    internal Task<AnalysisDatasetPage<AnalysisPathCorrespondence>> ReadCurrentAsync(ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) => state.Page(false, offset, maximum, token), cancellation);
    internal Task<AnalysisDatasetPage<AnalysisPathCorrespondence>> ReadPreviousAsync(ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) => state.Page(true, offset, maximum, token), cancellation);
    public ValueTask DisposeAsync() => _owned.DisposeAsync();

    private sealed class State : IAnalysisScratchOwned
    {
        private AnalysisIndexedGraphReader? _previous, _current;
        internal AnalysisScratchWorkspace? Workspace;
        private AnalysisScratchVector? _writer;
        private AnalysisScratchVectorCheckpoint? _rows;
        internal AnalysisSortedIndexReader? Reverse;
        private readonly int[] _previousRoots;
        private readonly ImmutableDictionary<string, string> _mapping;
        private ulong _next = 1;
        private bool _disposed;
        internal bool Finished => _next > _current!.Count(AnalysisIndexedGraph.Paths);
        internal State(ImmutableDictionary<string, string> mapping, int currentRoots)
        { _mapping = mapping; _previousRoots = Enumerable.Repeat(-1, currentRoots).ToArray(); }
        internal void Initialize(AnalysisIndexedGraphReader previous, AnalysisIndexedGraphReader current)
        {
            _previous = previous.Retain(); _current = current.Retain();
            Workspace = current.BorrowWorkspace(); _writer = new(Workspace.CreateArena());
            for (int c = 0; c < current.Header.Roots.Length; c++)
                for (int p = 0; p < previous.Header.Roots.Length; p++)
                    if (_mapping.TryGetValue(previous.Header.Roots[p].Id, out string? id) && id == current.Header.Roots[c].Id)
                        _previousRoots[c] = p;
        }
        internal long Step(CancellationToken token)
        {
            ulong row = _next++; var current = _current!.ReadPathRowRecord(row, token);
            int root = _previousRoots[current.RootIndex]; ulong oldRow = 0;
            if (root >= 0)
            {
                if (current.Node.ParentId == 0)
                {
                    var roots = _previous!.Children(root, 0, token);
                    if (roots.Count > 1) throw AnalysisIndexedGraph.Invalid();
                    if (roots.Count == 1) oldRow = _previous.ReadIndex(AnalysisIndexedGraphRevision.Children, roots.First, token).Value;
                }
                else
                {
                    ulong parentRow = _current.PathRow(current.Node.ParentId, token);
                    if (parentRow >= row) throw AnalysisIndexedGraph.Invalid();
                    ulong previousParent = Decode(_writer!.ReadCurrent(parentRow, token));
                    if (previousParent != 0)
                    {
                        var parent = _previous!.ReadPathRow(previousParent, token);
                        var candidate = current with { RootIndex = root, Node = current.Node with { ParentId = parent.Id } };
                        var matches = _previous.Find(AnalysisIndexedGraphRevision.Children, AnalysisGraphKeys.Child(candidate), cancellation: token);
                        if (matches.Count > 1) throw AnalysisIndexedGraph.Invalid();
                        if (matches.Count == 1) oldRow = _previous.ReadIndex(AnalysisIndexedGraphRevision.Children, matches.First, token).Value;
                    }
                }
            }
            Span<byte> record = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(record, oldRow);
            if (_writer!.Append(record, token) != row) throw AnalysisIndexedGraph.Invalid();
            return 1024L + current.Node.Name.Length * 2L + current.Node.RawName.Length;
        }
        internal void Seal(CancellationToken token)
        {
            _writer!.Flush(token); _rows = _writer.CaptureCommitted(); _writer.Dispose(); _writer = null;
            if (_rows.Count != _current!.Count(AnalysisIndexedGraph.Paths)) throw AnalysisIndexedGraph.Invalid();
        }
        internal IEnumerable<AnalysisSortInput> ReverseEntries(CancellationToken token)
        {
            for (ulong row = 1; row <= _rows!.Count; row++)
            {
                token.ThrowIfCancellationRequested(); ulong previous = Decode(_rows.Read(row, token));
                yield return new(previous == 0 ? null : new(AnalysisGraphKeys.PathId(previous), row), 8);
            }
        }
        internal AnalysisDatasetPage<AnalysisPathCorrespondence> Page(bool previous, ulong offset, int maximum, CancellationToken token)
        {
            ulong total = (previous ? _previous : _current)!.Count(AnalysisIndexedGraph.Paths);
            if (maximum is < 1 or > AnalysisLimits.BatchRecords || offset > total) throw new ArgumentOutOfRangeException(nameof(offset));
            var rows = ImmutableArray.CreateBuilder<AnalysisPathCorrespondence>(); long bytes = 0;
            while (offset + (ulong)rows.Count < total && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); ulong row = offset + (ulong)rows.Count + 1;
                AnalysisNode? a = null, b = null;
                if (previous)
                {
                    a = _previous!.ReadPathRow(row, token);
                    var range = Reverse!.FindRange(AnalysisGraphKeys.PathId(row), token);
                    if (range.Count > 1) throw AnalysisIndexedGraph.Invalid();
                    if (range.Count == 1) b = _current!.ReadPathRow(Reverse.Read(range.First, token).Value, token);
                }
                else
                {
                    b = _current!.ReadPathRow(row, token); ulong old = Decode(_rows!.Read(row, token));
                    if (old != 0) a = _previous!.ReadPathRow(old, token);
                }
                static long Cost(AnalysisNode? n) => n is null ? 0 : 1024L + n.Name.Length * 2L + n.RawName.Length;
                long cost = Cost(a) + Cost(b);
                if (cost > AnalysisLimits.BatchBytes) throw new IOException("A comparison path pair exceeds the bounded page payload.");
                if (cost > AnalysisLimits.BatchBytes - bytes) break;
                bool mapped = previous ? _mapping.ContainsKey(a!.RootId) : _mapping.Values.Contains(b!.RootId, StringComparer.Ordinal);
                rows.Add(new(a is null ? null : AnalysisPathMetadata.From(a), b is null ? null : AnalysisPathMetadata.From(b), mapped)); bytes += cost;
            }
            return new(offset, total, rows.ToImmutable());
        }
        private static ulong Decode(byte[] bytes) => bytes.Length == 8 ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : throw AnalysisIndexedGraph.Invalid();
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; Exception? error = null;
            foreach (IDisposable? resource in new IDisposable?[] { Reverse, _rows, _writer, Workspace, _current, _previous })
            { try { resource?.Dispose(); } catch (Exception failure) { error = error is null ? failure : new AggregateException(error, failure); } }
            if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            Interlocked.Exchange(ref s_active, 0);
        }
    }
}
