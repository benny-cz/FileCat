using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    private readonly SemaphoreSlim _navigation = new(1, 1);

    internal Task<ulong> FindMemberOffsetAsync(bool previous, ulong entity, ulong path, CancellationToken cancellation = default) =>
        OwnerUse(() => _owned.UseAsync(_ => (previous ? _previous : _current).FindMemberOffsetAsync(entity, path, cancellation)));

    internal async Task<AnalysisIndexedComparisonRow?> FindRowForPathAsync(bool previous, ulong path,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var admission = AnalysisExplorerAdmission.Acquire();
        return await OwnerUse(() => _owned.UseAsync(async state =>
        {
            await _navigation.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                ulong entity = await AnalysisIo.Run(_io, _devices, token =>
                {
                    var side = previous ? state.Previous : state.Current;
                    return side.Graph.FindPathRow(path, token) is { } row ? side.EntityForPathRow(row, token) : 0;
                }, cancellation).ConfigureAwait(false);
                if (entity == 0) return null;
                if (state.PathRows is null)
                    state.PathRows = await BuildNavigationIndexAsync(state, PathRows(state, cancellation), _io, _devices, cancellation).ConfigureAwait(false);
                return await AnalysisIo.Run(_io, _devices, token =>
                {
                    var range = state.PathRows.FindRange(SideRowKey(previous, entity), token);
                    if (range.Count == 0) return null;
                    if (range.Count != 1) throw AnalysisIndexedGraph.Invalid();
                    return state.ReadRow(state.PathRows.Read(range.First, token).Value, token, out _);
                }, cancellation).ConfigureAwait(false);
            }
            finally { _navigation.Release(); }
        })).ConfigureAwait(false);
    }

    internal async Task<ulong?> FindOffsetAsync(ulong row, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var admission = AnalysisExplorerAdmission.Acquire();
        return await OwnerUse(() => _owned.UseAsync(async state =>
        {
            if (row == 0 || row > Count) throw new ArgumentOutOfRangeException(nameof(row));
            await _navigation.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                state.Positions ??= await BuildNavigationIndexAsync(state, Positions(state.Order!, cancellation), _io, _devices, cancellation).ConfigureAwait(false);
                return await AnalysisIo.Run(_io, _devices, token => Position(state.Positions, row, token), cancellation).ConfigureAwait(false);
            }
            finally { _navigation.Release(); }
        })).ConfigureAwait(false);
    }

    internal sealed partial class Query
    {
        private readonly SemaphoreSlim _navigation = new(1, 1);
        internal async Task<ulong?> FindOffsetAsync(ulong row, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            using var admission = AnalysisExplorerAdmission.Acquire();
            return await _owned.UseAsync(async state =>
            {
                if (row == 0 || row > _owner.Count) throw new ArgumentOutOfRangeException(nameof(row));
                await _navigation.WaitAsync(cancellation).ConfigureAwait(false);
                try
                {
                    if (state.Positions is null)
                        state.Positions = await _source.UseAsync(source => BuildNavigationIndexAsync(source,
                            Positions(state.Order!, cancellation), _owner._io, _owner._devices, cancellation)).ConfigureAwait(false);
                    return await AnalysisIo.Run(_owner._io, _owner._devices, token => Position(state.Positions, row, token), cancellation).ConfigureAwait(false);
                }
                finally { _navigation.Release(); }
            }).ConfigureAwait(false);
        }
    }
    private static ulong? Position(AnalysisSortedIndexReader positions, ulong row, CancellationToken token)
    {
        var range = positions.FindRange(AnalysisGraphKeys.PathId(row), token);
        if (range.Count == 0) return null;
        if (range.Count != 1) throw AnalysisIndexedGraph.Invalid();
        ulong oneBased = positions.Read(range.First, token).Value;
        return oneBased == 0 || oneBased > positions.Count ? throw AnalysisIndexedGraph.Invalid() : oneBased - 1;
    }
    private static byte[] SideRowKey(bool previous, ulong entity) => [previous ? (byte)0 : (byte)1, .. AnalysisGraphKeys.PathId(entity)];
    private static IEnumerable<AnalysisSortInput> PathRows(State state, CancellationToken token)
    {
        for (ulong row = 1; row <= state.Rows!.Count; row++)
        {
            token.ThrowIfCancellationRequested(); var data = DecodeRow(state.Rows.Read(row, token));
            if (data.Previous != 0) yield return new(new(SideRowKey(true, data.Previous), row), 64);
            if (data.Current != 0) yield return new(new(SideRowKey(false, data.Current), row), 64);
        }
    }
    private static IEnumerable<AnalysisSortInput> Positions(AnalysisSortedIndexReader order, CancellationToken token)
    {
        for (ulong offset = 1; offset <= order.Count; offset++)
        {
            token.ThrowIfCancellationRequested();
            yield return new(new(AnalysisGraphKeys.PathId(order.Read(offset, token).Value), offset), 64);
        }
    }
    private static async Task<AnalysisSortedIndexReader> BuildNavigationIndexAsync(State state, IEnumerable<AnalysisSortInput> entries,
        DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        var workspace = await AnalysisIo.Run(io, devices, _ => state.Current.Graph.BorrowWorkspace(), cancellation).ConfigureAwait(false);
        AnalysisSortedIndexReader? result = null;
        try
        {
            using var input = entries.GetEnumerator();
            result = await BuildReaderAsync(workspace, input, AnalysisLimits.Observations * 2, io, devices, cancellation).ConfigureAwait(false);
            return result;
        }
        finally
        {
            try { await AnalysisIo.ReleaseScratchAsync(io, devices, workspace).ConfigureAwait(false); }
            catch { if (result is not null) await AnalysisIo.ReleaseScratchAsync(io, devices, result).ConfigureAwait(false); throw; }
        }
    }
}
