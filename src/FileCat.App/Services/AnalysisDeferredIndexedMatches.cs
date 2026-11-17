using System.Globalization;
using FileCat.Core.Analysis;

namespace FileCat.App.Services;

/// <summary>Materializes one captured display request as data only. No locator is rebound.</summary>
internal static class AnalysisDeferredIndexedMatches
{
    internal static async Task<AnalysisSelection> CaptureAsync(AnalysisDatasetLease source, SessionIdentity requested,
        AnalysisSavedView view, CancellationToken cancellation)
    {
        var descriptor = source.Descriptor; var identity = descriptor.Identity;
        if (descriptor.Phase != AnalysisPhase.Finished || identity.SessionId != requested.SessionId || identity.Epoch != requested.Epoch ||
            identity.HostProfile != requested.HostProfile || identity.Revision < requested.Revision)
            throw new InvalidOperationException("The requested scan did not finish in its captured observation domain. No matches were selected.");
        AnalysisSavedCatalog.ValidateView(view);
        if (view.Root is not null && !descriptor.Roots.Any(root => root.Id == view.Root))
            throw new InvalidOperationException("The captured location is absent. No broader selection was substituted.");
        var filter = new AnalysisFilterRequest(view.Query, view.CaseSensitive, view.ShowUnknown, view.Root) { RootDisplayIds = view.RootDisplayIds };
        var scope = AnalysisSavedCatalog.SplitAggregationScope(view.Scope);
        AnalysisSelection selected;
        if (view.Grouping == AnalysisGroupingDimension.FolderHierarchy)
        {
            ulong folder = 0;
            if (scope.Scope != AnalysisTree.RootKey)
            {
                if (!scope.Scope.StartsWith("n:", StringComparison.Ordinal) ||
                    !ulong.TryParse(scope.Scope.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out folder) || folder == 0 ||
                    scope.Scope != AnalysisTree.Key(folder) || (await source.ReadPathAsync(folder, cancellation)).Kind != AnalysisKind.Directory)
                    throw new InvalidOperationException("The requested folder is unavailable. No broader selection was substituted.");
            }
            AnalysisDatasetExplorer? explorer = null;
            try
            {
                explorer = await AnalysisDatasetExplorer.CreateAsync(source, view.Metric, filter, cancellation);
                if (scope.Skipped != 0)
                {
                    var next = await explorer.CreateScopeAsync(folder, scope.Skipped, cancellation);
                    var previous = explorer; explorer = next; await previous.DisposeAsync();
                }
                selected = await explorer.CaptureMatchingFilesAsync(folder, cancellation: cancellation);
            }
            finally { if (explorer is not null) await explorer.DisposeAsync(); }
        }
        else
        {
            AnalysisDatasetGroupExplorer? groups = null; string? key = scope.Scope == AnalysisTree.RootKey ? null : scope.Scope;
            try
            {
                groups = await AnalysisDatasetGroupExplorer.CreateAsync(source, view.Metric, view.Grouping, filter, cancellation);
                if (key is not null) _ = await groups.ReadGroupAsync(key, cancellation);
                if (scope.Skipped != 0)
                {
                    var next = await groups.CreateScopeAsync(key, scope.Skipped, cancellation);
                    var previous = groups; groups = next; await previous.DisposeAsync();
                }
                selected = await groups.CaptureMatchingFilesAsync(key, cancellation: cancellation);
            }
            finally { if (groups is not null) await groups.DisposeAsync(); }
        }
        cancellation.ThrowIfCancellationRequested();
        if (selected.SessionId != identity.SessionId || selected.Epoch != identity.Epoch || selected.Revision != identity.Revision ||
            selected.Kind != AnalysisSelectionKind.VisibleMatches || selected.NodeIds.Length > AnalysisLimits.ReviewPaths)
            throw new InvalidDataException("Captured matches belong to another revision or exceed the review limit.");
        return selected with
        {
            FilterSummary = view.Query + (view.Root is { } root ? " · captured location " + root : "") +
                (view.CaseSensitive ? " · case sensitive" : "") + (view.ShowUnknown ? " · uncertain results" : "")
        };
    }
}

/// <summary>A shared captured-data lease. The view's actual-work place covers its final native close.</summary>
internal sealed class AnalysisDeferredIndexedReview : IDisposable
{
    private Shared? _shared;
    internal Guid Request { get; }
    internal AnalysisSelection Selection { get; }
    internal AnalysisSavedView View { get; }
    internal string ScopeLabel { get; }
    internal AnalysisDatasetLease Dataset => State.Dataset;
    private Shared State => Volatile.Read(ref _shared) ?? throw new ObjectDisposedException(nameof(AnalysisDeferredIndexedReview));

    // Ownership transfers only when this constructor returns successfully.
    internal AnalysisDeferredIndexedReview(Guid request, AnalysisSelection selection, AnalysisSavedView view, string label,
        AnalysisDatasetLease dataset, AnalysisViewWork work)
        : this(request, selection, view, label, new Shared(dataset, work)) { }
    private AnalysisDeferredIndexedReview(Guid request, AnalysisSelection selection, AnalysisSavedView view, string label, Shared shared)
    { Request = request; Selection = selection; View = view; ScopeLabel = label; _shared = shared; }
    internal AnalysisDeferredIndexedReview Retain()
    {
        var shared = State; shared.Retain(); return new(Request, Selection, View, ScopeLabel, shared);
    }
    public void Dispose() => Interlocked.Exchange(ref _shared, null)?.Release();

    private sealed class Shared
    {
        private readonly object _gate = new();
        private readonly AnalysisViewWork _work;
        private readonly IDisposable _actual;
        private int _owners = 1;
        internal AnalysisDatasetLease Dataset { get; }
        internal Shared(AnalysisDatasetLease dataset, AnalysisViewWork work)
        { _work = work; _actual = work.Enter(); Dataset = dataset; }
        internal void Retain()
        { lock (_gate) { ObjectDisposedException.ThrowIf(_owners == 0, this); checked { _owners++; } } }
        internal void Release()
        {
            bool close; lock (_gate) close = --_owners == 0;
            if (close) _ = CloseAsync();
        }
        private async Task CloseAsync()
        {
            try { await Dataset.DisposeAsync().ConfigureAwait(false); }
            catch (Exception error)
            { _work.RecordUnconfirmedClose(error as AnalysisCloseUnconfirmedException ?? new AnalysisCloseUnconfirmedException(error)); }
            finally { _actual.Dispose(); }
        }
    }
}
