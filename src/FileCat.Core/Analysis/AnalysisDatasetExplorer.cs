using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public sealed record AnalysisExplorerPage(ulong ParentId, AnalysisHierarchyRow? Scope, AnalysisHierarchySort Sort,
    AnalysisDatasetPage<AnalysisHierarchyRow> Children, ImmutableArray<AnalysisHierarchyBucket> Buckets,
    ImmutableArray<AnalysisPathMetadata> Ancestors, bool AncestorsComplete);

public sealed record AnalysisExplorerInspection(AnalysisHierarchyRow Row, AnalysisObjectMetadata? Object,
    ImmutableArray<AnalysisPathMetadata> Ancestors, bool AncestorsComplete,
    AnalysisDatasetPage<AnalysisPathMetadata> Aliases, AnalysisDatasetPage<StorageFact> Facts,
    AnalysisDatasetPage<AnalysisIssue> Issues);

/// <summary>
/// Bounded, read-only explorer for one captured revision and query. Page and inspector requests have
/// independent latest-request lanes. No decoded path is rebound to a filesystem capability.
/// </summary>
public sealed class AnalysisDatasetExplorer : IAsyncDisposable
{
    private readonly AnalysisDatasetLease _dataset;
    private readonly AnalysisDatasetHierarchy _hierarchy;
    private readonly object _gate = new();
    private readonly Lane _pages = new(), _inspections = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AnalysisExplorerPage? _page;
    private AnalysisExplorerInspection? _inspection;
    private int _active;
    private bool _disposed, _closing;

    public AnalysisDatasetDescriptor Descriptor => _hierarchy.Descriptor;
    public AnalysisMetric Metric => _hierarchy.Metric;
    public AnalysisFilterSummary? Filter => _hierarchy.Filter;
    public AnalysisTotals Totals => _hierarchy.Totals;
    public AnalysisLogicalTotals LogicalTotals => _hierarchy.LogicalTotals;
    public AnalysisExplorerPage? Page { get { lock (_gate) return _page; } }
    public AnalysisExplorerInspection? Inspection { get { lock (_gate) return _inspection; } }
    public int PendingRequests { get { lock (_gate) return _active; } }

    private AnalysisDatasetExplorer(AnalysisDatasetLease dataset, AnalysisDatasetHierarchy hierarchy)
    { _dataset = dataset; _hierarchy = hierarchy; }

    public static async Task<AnalysisDatasetExplorer> CreateAsync(AnalysisDatasetLease dataset, AnalysisMetric metric,
        AnalysisFilterRequest? filter = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var retained = await dataset.RetainAsync(cancellation).ConfigureAwait(false);
        AnalysisDatasetHierarchy? hierarchy = null;
        try
        {
            hierarchy = filter is null
                ? await retained.BuildHierarchyAsync(metric, cancellation).ConfigureAwait(false)
                : await retained.BuildHierarchyAsync(metric, filter, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return new(retained, hierarchy);
        }
        catch
        {
            try { if (hierarchy is not null) await hierarchy.DisposeAsync().ConfigureAwait(false); }
            finally { await retained.DisposeAsync().ConfigureAwait(false); }
            throw;
        }
    }

    /// <returns>True only if this request published. A superseded request returns false.</returns>
    public Task<bool> LoadPageAsync(ulong parent = 0, AnalysisHierarchySort sort = AnalysisHierarchySort.Metric,
        ulong offset = 0, int maximum = 128, CancellationToken cancellation = default)
    {
        ValidateMaximum(maximum);
        if (parent > Descriptor.Paths || !Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(parent));
        return RequestAsync(_pages, async token =>
        {
            var scope = parent == 0 ? null : await _hierarchy.ReadAsync(parent, token).ConfigureAwait(false);
            if (scope is not null && (scope.Path.Kind != AnalysisKind.Directory || !scope.IsVisible))
                throw new InvalidOperationException("Only a visible directory can be an explorer scope.");
            var children = await _hierarchy.ReadChildrenAsync(parent, sort, offset, maximum, token).ConfigureAwait(false);
            var buckets = await _hierarchy.ReadBucketsAsync(parent, token).ConfigureAwait(false);
            var ancestors = await ReadAncestorsAsync(scope?.Path, token).ConfigureAwait(false);
            return new AnalysisExplorerPage(parent, scope, sort, children, buckets, ancestors.Rows, ancestors.Complete);
        }, value => _page = value, cancellation);
    }

    /// <summary>Each related-record collection is independently paged; totals do not imply all rows are loaded.</summary>
    public Task<bool> InspectAsync(ulong path, ulong aliasOffset = 0, ulong factOffset = 0, ulong issueOffset = 0,
        int maximum = 128, CancellationToken cancellation = default)
    {
        ValidateMaximum(maximum);
        if (path == 0 || path > Descriptor.Paths) throw new ArgumentOutOfRangeException(nameof(path));
        return RequestAsync(_inspections, async token =>
        {
            var row = await _hierarchy.ReadAsync(path, token).ConfigureAwait(false);
            var ancestors = await ReadAncestorsAsync(row.Path, token).ConfigureAwait(false);
            AnalysisObjectMetadata? obj = null;
            AnalysisDatasetPage<AnalysisPathMetadata> aliases;
            AnalysisDatasetPage<StorageFact> facts;
            if (row.Path.ObjectId is { } objectId)
            {
                obj = await _dataset.ReadObjectAsync(objectId, token).ConfigureAwait(false);
                aliases = await _dataset.ReadAliasesAsync(objectId, aliasOffset, maximum, token).ConfigureAwait(false);
                facts = await _dataset.ReadObjectFactsAsync(objectId, factOffset, maximum, token).ConfigureAwait(false);
            }
            else
            {
                if (aliasOffset != 0) throw new ArgumentOutOfRangeException(nameof(aliasOffset));
                aliases = new(0, 0, []);
                facts = await _dataset.ReadDirectoryFactsAsync(path, factOffset, maximum, token).ConfigureAwait(false);
            }
            var issues = await _dataset.ReadPathIssuesAsync(path, issueOffset, maximum, token).ConfigureAwait(false);
            return new AnalysisExplorerInspection(row, obj, ancestors.Rows, ancestors.Complete, aliases, facts, issues);
        }, value => _inspection = value, cancellation);
    }

    private async Task<(ImmutableArray<AnalysisPathMetadata> Rows, bool Complete)> ReadAncestorsAsync(
        AnalysisPathMetadata? path, CancellationToken cancellation)
    {
        var rows = ImmutableArray.CreateBuilder<AnalysisPathMetadata>(); long bytes = 0;
        ulong next = path?.ParentId ?? 0;
        while (next != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (rows.Count == AnalysisLimits.Depth) return (rows.ToImmutable().Reverse().ToImmutableArray(), false);
            var parent = (await _hierarchy.ReadAsync(next, cancellation).ConfigureAwait(false)).Path;
            if (parent.RootId != path!.RootId || parent.Kind != AnalysisKind.Directory)
                throw new InvalidDataException("The captured ancestor chain is inconsistent.");
            long cost = 384L + parent.Name.Length * 2L + parent.RawName.Length + parent.RootId.Length * 2L +
                (parent.ObjectId?.Length ?? 0) * 2L + (parent.ObservedIdentity?.Length ?? 0) * 2L;
            if (cost > AnalysisLimits.BatchBytes - bytes) return (rows.ToImmutable().Reverse().ToImmutableArray(), false);
            rows.Add(parent); bytes += cost; next = parent.ParentId;
        }
        return (rows.ToImmutable().Reverse().ToImmutableArray(), true);
    }

    private Task<bool> RequestAsync<T>(Lane lane, Func<CancellationToken, Task<T>> read, Action<T> publish,
        CancellationToken cancellation)
    {
        Request request; Request? previous;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            cancellation.ThrowIfCancellationRequested();
            if (_active >= AnalysisLimits.IoRequests)
                throw new InvalidOperationException("Eight explorer requests are still completing. Wait before requesting another page or inspection.");
            request = new(cancellation); previous = lane.Current; lane.Current = request; _active++;
        }
        previous?.Cancel();
        return ExecuteAsync();

        async Task<bool> ExecuteAsync()
        {
            bool entered = false;
            try
            {
                await lane.Turn.WaitAsync(request.Token).ConfigureAwait(false); entered = true;
                var value = await read(request.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (_disposed || !ReferenceEquals(lane.Current, request)) return false;
                    publish(value); return true;
                }
            }
            catch (OperationCanceledException) when (request.Token.IsCancellationRequested && !cancellation.IsCancellationRequested)
            { return false; }
            finally
            {
                if (entered) lane.Turn.Release();
                request.Dispose(); bool close;
                lock (_gate)
                {
                    if (ReferenceEquals(lane.Current, request)) lane.Current = null;
                    _active--; close = BeginClose();
                }
                if (close) await CloseAsync().ConfigureAwait(false);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Request? page, inspection; bool close;
        lock (_gate)
        {
            _disposed = true; _page = null; _inspection = null;
            page = _pages.Current; inspection = _inspections.Current; close = BeginClose();
        }
        page?.Cancel(); inspection?.Cancel();
        if (close) _ = CloseAsync();
        return new(_closed.Task);
    }
    private bool BeginClose()
    { if (!_disposed || _active != 0 || _closing) return false; _closing = true; return true; }
    private async Task CloseAsync()
    {
        try
        {
            try { await _hierarchy.DisposeAsync().ConfigureAwait(false); }
            finally { await _dataset.DisposeAsync().ConfigureAwait(false); }
            _pages.Turn.Dispose(); _inspections.Turn.Dispose(); _closed.TrySetResult();
        }
        catch (Exception error) { _closed.TrySetException(error); }
    }
    private static void ValidateMaximum(int maximum)
    { if (maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(maximum)); }
    private sealed class Lane
    { internal readonly SemaphoreSlim Turn = new(1, 1); internal Request? Current; }
    private sealed class Request : IDisposable
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancel;
        private bool _disposed;
        internal CancellationToken Token { get; }
        internal Request(CancellationToken cancellation)
        { _cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellation); Token = _cancel.Token; }
        internal void Cancel() { lock (_gate) { if (!_disposed) _cancel.Cancel(); } }
        public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _cancel.Dispose(); } }
    }
}
