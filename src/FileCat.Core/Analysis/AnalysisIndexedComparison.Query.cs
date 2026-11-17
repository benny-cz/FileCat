using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    private static int s_queryBuild;
    internal async Task<Query> QueryAsync(AnalysisComparisonView view, AnalysisRootDisplayIds? previousRootDisplayIds = null,
        AnalysisRootDisplayIds? currentRootDisplayIds = null, CancellationToken cancellation = default)
    {
        AnalysisComparisonView.Validate(view);
        if (view.Metric != Metric) throw new ArgumentException("Choose the metric of these comparison rows.", nameof(view));
        cancellation.ThrowIfCancellationRequested(); var lease = Retain(); bool transferred = false;
        try
        {
            var result = await lease.UseAsync(state => BuildQueryAsync(state, lease, view, previousRootDisplayIds, currentRootDisplayIds, cancellation)).ConfigureAwait(false);
            transferred = true; return result;
        }
        finally { if (!transferred) await lease.DisposeAsync().ConfigureAwait(false); }
    }
    private async Task<Query> BuildQueryAsync(State source, ReadLease lease, AnalysisComparisonView view,
        AnalysisRootDisplayIds? oldIds, AnalysisRootDisplayIds? newIds, CancellationToken cancellation)
    {
        var state = new QueryState(source); bool transferred = false;
        if (Interlocked.CompareExchange(ref s_queryBuild, 1, 0) != 0) throw new InvalidOperationException("The comparison query builder is in use. Keep the applied view and retry.");
        try
        {
            bool before = view.QuerySide is AnalysisComparisonSide.Previous or AnalysisComparisonSide.Either || view.QuerySide == AnalysisComparisonSide.CurrentOrPrevious && source.HasPreviousOnlyRows;
            bool now = view.QuerySide is AnalysisComparisonSide.Current or AnalysisComparisonSide.Either || view.QuerySide == AnalysisComparisonSide.CurrentOrPrevious && source.HasCurrentRows;
            if (!before && !now) now = true;
            if (before) state.PreviousFilter = await AnalysisIndexedFilter.CreateAsync(source.Previous.Graph,
                new(view.Query, view.CaseSensitive) { RootDisplayIds = oldIds }, _io, _devices, cancellation, comparison: true).ConfigureAwait(false);
            if (now) state.CurrentFilter = await AnalysisIndexedFilter.CreateAsync(source.Current.Graph,
                new(view.Query, view.CaseSensitive) { RootDisplayIds = newIds }, _io, _devices, cancellation, comparison: true).ConfigureAwait(false);
            await AnalysisIo.Run(_io, _devices, _ => { state.Initialize(); return true; }, cancellation).ConfigureAwait(false);
            using (var steps = new QueryWork(source, state.PreviousFilter, state.CurrentFilter, view, state).Steps(cancellation).GetEnumerator())
            {
                bool finished = false;
                while (!finished)
                    await AnalysisIo.Run(_io, _devices, token =>
                    {
                        int records = 0; long bytes = 0; var watch = System.Diagnostics.Stopwatch.StartNew();
                        while (records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                        {
                            token.ThrowIfCancellationRequested(); if (!steps.MoveNext()) { finished = true; break; }
                            var step = steps.Current; bytes += step.Bytes;
                            if (step.Entry is { } entry) state.Append(entry, token);
                            if (watch.ElapsedMilliseconds >= 20) break;
                        }
                        return true;
                    }, cancellation).ConfigureAwait(false);
            }
            await AnalysisIo.Run(_io, _devices, token => { state.Seal(token); return true; }, cancellation).ConfigureAwait(false);
            using (var entries = state.Entries(cancellation).GetEnumerator())
                state.Order = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations * 2, _io, _devices, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(_io, _devices, _ => { state.CloseBuildInputs(); return true; }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); var query = new Query(this, state, lease, view); transferred = true; return query;
        }
        finally
        {
            try { if (!transferred) await AnalysisIo.ReleaseScratchAsync(_io, _devices, state).ConfigureAwait(false); }
            finally { Interlocked.Exchange(ref s_queryBuild, 0); }
        }
    }

    internal sealed class Query : IAsyncDisposable
    {
        private readonly AnalysisIndexedComparison _owner;
        private readonly AnalysisOwnedDatasetIo<QueryState> _owned;
        private readonly ReadLease _source;
        private readonly object _gate = new(); private Task? _closed;
        internal AnalysisComparisonView View { get; }
        internal AnalysisTaint Taint { get; }
        internal ulong Count { get; }
        internal ulong Definite { get; }
        internal ulong Unknown { get; }
        internal Query(AnalysisIndexedComparison owner, QueryState state, ReadLease source, AnalysisComparisonView view)
        {
            _owner = owner; _owned = new(state, owner._io, owner._devices); _source = source; View = view; Taint = owner.Taint;
            Count = state.Order!.Count; Definite = state.Definite; Unknown = state.Unknown;
        }
        internal Task<AnalysisDatasetPage<AnalysisIndexedComparisonRow>> ReadAsync(ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
            _owned.RunAsync((state, token) => state.Page(offset, maximum, token), cancellation);
        internal Task<AnalysisDatasetPage<AnalysisPathMetadata>> ReadMembersAsync(bool previous, ulong entity, ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
            _owned.UseAsync(_ => _source.ReadMembersAsync(previous, entity, offset, maximum, cancellation));
        internal Task<AnalysisComparisonAccountingEntity> ReadSideAsync(bool previous, ulong entity, CancellationToken cancellation = default) =>
            _owned.UseAsync(_ => _source.ReadSideAsync(previous, entity, cancellation));
        internal bool BelongsTo(AnalysisIndexedComparison owner) => ReferenceEquals(_owner, owner);
        internal AnalysisDatasetDescriptor Descriptor(bool previous) => _source.Descriptor(previous);
        internal Task<AnalysisPathMetadata> ReadPathAsync(bool previous, ulong path, CancellationToken cancellation = default) =>
            _owned.UseAsync(_ => _source.ReadPathAsync(previous, path, cancellation));
        internal Task<Query> RetainAsync(CancellationToken cancellation = default) => _owned.UseAsync(async state =>
        {
            cancellation.ThrowIfCancellationRequested(); ReadLease? reader = _source.Retain(); QueryState? clone = null;
            try
            {
                clone = await AnalysisIo.Run(_owner._io, _owner._devices, _ => state.Retain(), cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested(); var result = new Query(_owner, clone, reader, View); clone = null; reader = null; return result;
            }
            finally
            {
                try { if (clone is not null) await AnalysisIo.ReleaseScratchAsync(_owner._io, _owner._devices, clone).ConfigureAwait(false); }
                finally { if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false); }
            }
        });
        public ValueTask DisposeAsync() { lock (_gate) return new(_closed ??= CloseAsync(_owned, _source)); }
    }
    internal sealed class QueryState(State source) : IAnalysisScratchOwned
    {
        internal AnalysisScratchWorkspace? Workspace;
        internal AnalysisIndexedFilter? PreviousFilter, CurrentFilter;
        internal AnalysisSortedIndexReader? Order;
        internal ulong Definite, Unknown;
        private AnalysisScratchArena? _arena;
        private AnalysisScratchVector? _writer;
        private AnalysisScratchVectorCheckpoint? _input;
        private Exception? _closeFailure;
        private bool _disposed;
        internal QueryState Retain()
        {
            var index = Order!.Retain();
            try { return new(source) { Order = index, Definite = Definite, Unknown = Unknown }; }
            catch { index.Dispose(); throw; }
        }
        internal void Initialize()
        {
            Workspace = source.Current.Graph.BorrowWorkspace(); _arena = Workspace.CreateArena();
            _writer = new(_arena, recordLimit: AnalysisLimits.Observations * 2); _arena = null;
        }
        internal void Append(AnalysisSortEntry entry, CancellationToken token)
        {
            byte[] data = new byte[8 + entry.Key.Length]; BinaryPrimitives.WriteUInt64LittleEndian(data, entry.Value); entry.Key.CopyTo(data, 8);
            _writer!.Append(data, token);
        }
        internal void Seal(CancellationToken token)
        { _writer!.Flush(token); _input = _writer.CaptureCommitted(); _writer.Dispose(); _writer = null; }
        internal IEnumerable<AnalysisSortInput> Entries(CancellationToken token)
        {
            for (ulong id = 1; id <= _input!.Count; id++)
            {
                token.ThrowIfCancellationRequested(); byte[] data = _input.Read(id, token);
                if (data.Length <= 8) throw AnalysisIndexedGraph.Invalid();
                yield return new(new(data.AsSpan(8).ToArray(), BinaryPrimitives.ReadUInt64LittleEndian(data)), checked(data.Length + 64));
            }
        }
        internal void CloseBuildInputs()
        {
            var a = PreviousFilter; var b = CurrentFilter; var input = _input; var workspace = Workspace;
            PreviousFilter = CurrentFilter = null; _input = null; Workspace = null;
            try { CloseAll(a, b, input, workspace); }
            catch (Exception error) { _closeFailure = error; throw; }
        }
        internal AnalysisDatasetPage<AnalysisIndexedComparisonRow> Page(ulong offset, int maximum, CancellationToken token)
        {
            if (offset > Order!.Count || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(offset));
            var rows = ImmutableArray.CreateBuilder<AnalysisIndexedComparisonRow>(); long bytes = 0;
            while (offset + (ulong)rows.Count < Order.Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var row = source.ReadRow(Order.Read(offset + (ulong)rows.Count + 1, token).Value, token, out long cost);
                if (cost > AnalysisLimits.BatchBytes) throw new IOException("A comparison scalar row exceeds the bounded page payload.");
                if (cost > AnalysisLimits.BatchBytes - bytes) break;
                rows.Add(row); bytes += cost;
            }
            return new(offset, Order.Count, rows.ToImmutable());
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            Exception? failure = _closeFailure;
            try { CloseAll(Order, PreviousFilter, CurrentFilter, _input, _writer, _arena, Workspace); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
