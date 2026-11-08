using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>One immutable grouped display scope over captured observations. It owns no live binding.</summary>
public sealed class AnalysisDatasetGroupExplorer : IAsyncDisposable
{
    private readonly AnalysisDatasetGrouping? _groups;
    private readonly AnalysisDatasetGrouping.Subset? _subset;
    private readonly object _gate = new(), _cancelGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed, _closing, _cancelled;
    private int _active;
    private AnalysisDatasetGroupExplorer(AnalysisDatasetGrouping groups) => _groups = groups;
    private AnalysisDatasetGroupExplorer(AnalysisDatasetGrouping.Subset subset) => _subset = subset;
    public AnalysisDatasetDescriptor Descriptor => _subset?.Descriptor ?? _groups!.Descriptor;
    public AnalysisMetric Metric => _subset?.Metric ?? _groups!.Metric;
    public AnalysisGroupingDimension Dimension => _subset?.Dimension ?? _groups!.Dimension;
    public AnalysisFilterSummary? Filter => _subset is not null ? _subset.Filter : _groups!.Filter;
    public AnalysisTotals Totals => _subset?.Totals ?? _groups!.Totals;
    public AnalysisLogicalTotals LogicalTotals => _subset?.LogicalTotals ?? _groups!.LogicalTotals;
    public int GeometrySkipped => _subset?.Skipped ?? 0;
    public string? GeometryParent => _subset?.ParentKey;
    public string? OtherScopeKey => _subset?.ScopeKey;
    internal AnalysisReportBases? ReportBases => _subset?.MeasurementBases ?? _groups?.MeasurementBases;
    public bool Unknown => _subset?.Unknown ?? (Totals.Missing.Count != 0 || !Totals.CompatibleBasis || Metric == AnalysisMetric.UniqueFileSize && Totals.IdentityUnverified != 0);
    public int PendingScopeRequests { get { lock (_gate) return _active; } }
    private static string? Normalize(string? key) => key == AnalysisTree.RootKey ? null : key;
    private AnalysisDatasetGrouping.Subset SubsetFor(string? key, bool allowDefault = false)
    {
        if (_subset is null || Normalize(key) != _subset.ParentKey && !(allowDefault && Normalize(key) is null)) throw new InvalidOperationException("This request belongs to another captured grouping scope.");
        return _subset;
    }
    public static async Task<AnalysisDatasetGroupExplorer> CreateAsync(AnalysisDatasetLease source, AnalysisMetric metric, AnalysisGroupingDimension dimension,
        AnalysisFilterRequest? filter = null, CancellationToken cancellation = default)
    {
        AnalysisDatasetGrouping? groups = await source.BuildGroupingAsync(metric, dimension, filter, cancellation).ConfigureAwait(false);
        try { cancellation.ThrowIfCancellationRequested(); var result = new AnalysisDatasetGroupExplorer(groups); groups = null; return result; }
        finally { if (groups is not null) await groups.DisposeAsync().ConfigureAwait(false); }
    }
    public Task<AnalysisDatasetGroup> ReadGroupAsync(string key, CancellationToken cancellation = default) =>
        _subset is not null ? _subset.ReadGroupAsync(key, cancellation) : _groups!.ReadGroupAsync(key, cancellation);
    public Task<AnalysisDatasetPage<AnalysisDatasetGroup>> ReadGroupsAsync(ulong offset = 0, int maximum = 128, CancellationToken cancellation = default,
        AnalysisTableSort sort = AnalysisTableSort.ActiveMetric, bool descending = true) => _subset is not null
            ? SubsetFor(null).ReadGroupsAsync(offset, maximum, sort, descending, cancellation) : _groups!.ReadGroupsAsync(offset, maximum, cancellation, sort, descending);
    public Task<AnalysisDatasetPage<AnalysisDatasetGroupRow>> ReadMemberRowsAsync(string key, ulong offset = 0, int maximum = 128,
        AnalysisTableSort sort = AnalysisTableSort.ActiveMetric, bool descending = true, CancellationToken cancellation = default) => _subset is not null
            ? SubsetFor(key).ReadMemberRowsAsync(offset, maximum, sort, descending, cancellation) : _groups!.ReadMemberRowsAsync(key, offset, maximum, sort, descending, cancellation);
    public Task<AnalysisDatasetPage<AnalysisTreeItem>> ReadGeometryAsync(string? key = null, ulong offset = 0, int maximum = 128, CancellationToken cancellation = default)
    {
        key = Normalize(key);
        return _subset is null ? _groups!.ReadGeometryAsync(key, offset, maximum, cancellation) : key is null || key == _subset.ParentKey
            ? _subset.ReadGeometryAsync(offset, maximum, cancellation) : _subset.ParentKey is null && key is not null
                ? _subset.ReadChildGeometryAsync(key, offset, maximum, cancellation) : throw new InvalidOperationException("This chart belongs to another captured grouping scope.");
    }
    public Task<AnalysisDatasetPage<AnalysisObjectCharge>> ReadChargesAsync(string key, ulong? ownerPath = null, ulong offset = 0, int maximum = 128, CancellationToken cancellation = default) =>
        _subset is not null ? _subset.ReadChargesAsync(key, ownerPath, offset, maximum, cancellation) : _groups!.ReadChargesAsync(key, ownerPath, offset, maximum, cancellation);
    public Task<AnalysisSelection> CaptureMembersAsync(string? key = null, int maximumPaths = AnalysisLimits.ReviewPaths, CancellationToken cancellation = default) =>
        _subset is null ? _groups!.CaptureMembersAsync(key, maximumPaths, cancellation) : Normalize(key) is { } group
            ? _subset.CaptureGroupMembersAsync(group, maximumPaths, cancellation) : _subset.CaptureMembersAsync(maximumPaths, cancellation);
    public Task<AnalysisSelection> CaptureMatchingFilesAsync(string? key = null, int maximumPaths = AnalysisLimits.ReviewPaths, CancellationToken cancellation = default) =>
        _subset is null ? _groups!.CaptureMatchingFilesAsync(key, maximumPaths, cancellation) : Normalize(key) is { } group
            ? _subset.CaptureGroupMatchingFilesAsync(group, maximumPaths, cancellation) : _subset.CaptureMatchingFilesAsync(maximumPaths, cancellation);
    public Task<AnalysisSelectionVisibility> ClassifyMarksAsync(ImmutableHashSet<ulong> marks, string? key = null, CancellationToken cancellation = default) =>
        _subset is not null ? SubsetFor(key, allowDefault: true).ClassifyMarksAsync(marks, cancellation) : _groups!.ClassifyMarksAsync(marks, key, cancellation);
    public Task<AnalysisCapturedTableRange> CaptureTableRangeAsync(string? key, ulong first, ulong last, AnalysisTableSort sort = AnalysisTableSort.ActiveMetric,
        bool descending = true, CancellationToken cancellation = default) => _subset is not null
            ? SubsetFor(key).CaptureTableRangeAsync(first, last, sort, descending, cancellation) : _groups!.CaptureTableRangeAsync(key, first, last, sort, descending, cancellation);
    public Task<AnalysisDatasetGroupExplorer> CreateScopeAsync(string? key, int skipped = 0, CancellationToken cancellation = default)
    {
        if (skipped < 0) throw new ArgumentOutOfRangeException(nameof(skipped)); key = Normalize(key); CancellationTokenSource linked;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); cancellation.ThrowIfCancellationRequested();
            if (_active >= AnalysisLimits.IoRequests) throw new InvalidOperationException("Eight grouping scope requests are still completing. Wait before opening another scope.");
            linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, _lifetime.Token); _active++;
        }
        return CreateActualAsync();
        async Task<AnalysisDatasetGroupExplorer> CreateActualAsync()
        {
            AnalysisDatasetGrouping? groups = null; AnalysisDatasetGrouping.Subset? subset = null;
            try
            {
                if (skipped == 0) groups = _subset is not null ? await _subset.OpenSnapshotAsync(linked.Token).ConfigureAwait(false) : await _groups!.RetainAsync(linked.Token).ConfigureAwait(false);
                else subset = _subset is not null ? await _subset.CreateSiblingAsync(key, skipped, linked.Token).ConfigureAwait(false) : await _groups!.CreateSubsetAsync(key, skipped, linked.Token).ConfigureAwait(false);
                lock (_gate)
                {
                    linked.Token.ThrowIfCancellationRequested(); ObjectDisposedException.ThrowIf(_disposed, this);
                    var result = subset is not null ? new AnalysisDatasetGroupExplorer(subset) : new AnalysisDatasetGroupExplorer(groups!); groups = null; subset = null; return result;
                }
            }
            finally
            {
                try { try { if (subset is not null) await subset.DisposeAsync().ConfigureAwait(false); } finally { if (groups is not null) await groups.DisposeAsync().ConfigureAwait(false); } }
                finally
                {
                    linked.Dispose(); bool close; lock (_gate) { _active--; close = BeginClose(); }
                    if (close) await CloseAsync().ConfigureAwait(false);
                }
            }
        }
    }
    public ValueTask DisposeAsync()
    {
        bool close; lock (_gate) { if (_disposed) return new(_closed.Task); _disposed = true; close = BeginClose(); }
        lock (_cancelGate) { if (!_cancelled) { _cancelled = true; _lifetime.Cancel(); } }
        if (close) _ = CloseAsync(); return new(_closed.Task);
    }
    private bool BeginClose() { if (!_disposed || _active != 0 || _closing) return false; _closing = true; return true; }
    private async Task CloseAsync()
    {
        try
        {
            try { if (_subset is not null) await _subset.DisposeAsync().ConfigureAwait(false); else await _groups!.DisposeAsync().ConfigureAwait(false); }
            finally { lock (_cancelGate) { if (!_cancelled) { _cancelled = true; _lifetime.Cancel(); } _lifetime.Dispose(); } }
            _closed.TrySetResult();
        }
        catch (Exception error) { _closed.TrySetException(error); }
    }
}
