namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetExplorer
{
    private AnalysisDatasetExplorer(AnalysisDatasetLease dataset, AnalysisDatasetHierarchy.Subset subset)
    { _dataset = dataset; _subset = subset; }
    private AnalysisDatasetHierarchy.Subset CurrentSubset(ulong parent, bool allowDefault = false)
    {
        if (_subset is null || parent != _subset.ParentId && !(allowDefault && parent == 0))
            throw new InvalidOperationException("This request belongs to another captured geometry scope.");
        return _subset;
    }
    private Task<AnalysisHierarchyRow> ReadRowAsync(ulong path, CancellationToken cancellation) =>
        _subset is not null ? _subset.ReadSnapshotRowAsync(path, cancellation) : _hierarchy!.ReadAsync(path, cancellation);
    private Task<AnalysisDatasetPage<AnalysisHierarchyRow>> ReadChildrenAsync(ulong parent, AnalysisHierarchySort sort, ulong offset, int maximum,
        CancellationToken cancellation, bool descending) => _subset is not null
            ? CurrentSubset(parent).ReadChildrenAsync(sort, offset, maximum, cancellation, descending)
            : _hierarchy!.ReadChildrenAsync(parent, sort, offset, maximum, cancellation, descending);

    /// <summary>Creates an independently owned explorer for the same observations and query. A positive skip is an exact geometry suffix.</summary>
    public Task<AnalysisDatasetExplorer> CreateScopeAsync(ulong parent, int geometrySkipped = 0, CancellationToken cancellation = default)
    {
        if (geometrySkipped < 0) throw new ArgumentOutOfRangeException(nameof(geometrySkipped));
        CancellationTokenSource linked;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); cancellation.ThrowIfCancellationRequested();
            if (_active >= AnalysisLimits.IoRequests) throw new InvalidOperationException("Eight explorer requests are still completing. Wait before opening another scope.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _scopeLifetime.Token); _active++;
        }
        return CreateActualAsync();

        async Task<AnalysisDatasetExplorer> CreateActualAsync()
        {
            AnalysisDatasetLease? retained = null; AnalysisDatasetHierarchy? hierarchy = null; AnalysisDatasetHierarchy.Subset? subset = null;
            try
            {
                retained = await _dataset.RetainAsync(linked.Token).ConfigureAwait(false);
                if (geometrySkipped == 0)
                    hierarchy = _subset is not null ? await _subset.OpenHierarchyAsync(linked.Token).ConfigureAwait(false) : await _hierarchy!.RetainAsync(linked.Token).ConfigureAwait(false);
                else
                    subset = _subset is not null ? await _subset.CreateSiblingAsync(parent, geometrySkipped, linked.Token).ConfigureAwait(false) :
                        await _hierarchy!.CreateSubsetAsync(parent, geometrySkipped, linked.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this);
                    var result = subset is not null ? new AnalysisDatasetExplorer(retained, subset) : new AnalysisDatasetExplorer(retained, hierarchy!);
                    retained = null; hierarchy = null; subset = null; return result;
                }
            }
            finally
            {
                try
                {
                    try { if (subset is not null) await subset.DisposeAsync().ConfigureAwait(false); }
                    finally { try { if (hierarchy is not null) await hierarchy.DisposeAsync().ConfigureAwait(false); } finally { if (retained is not null) await retained.DisposeAsync().ConfigureAwait(false); } }
                }
                finally
                {
                    linked.Dispose(); bool close;
                    lock (_gate) { _active--; close = BeginClose(); }
                    if (close) await CloseAsync().ConfigureAwait(false);
                }
            }
        }
    }
}
