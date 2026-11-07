using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

/// <summary>Owns one analysis tab. Observations, displayed projection and selection never become action authority.</summary>
public sealed partial class DiskSpaceViewModel : IDisposable
{
    private readonly AppServices? _services;
    private readonly TimeZoneInfo? _zone;
    private readonly Func<string, IAnalysisBackend> _prepareSource = Prepare;
    private CancellationTokenSource? _preparationCancel;
    private string? _preparationDevice;
    private AnalysisSession? _session;
    private AnalysisSnapshotLease? _lease;
    private AnalysisSnapshot? _observation;
    private AnalysisSnapshotLease? _capturedOwner;
    private CancellationTokenSource? _projectionCancel;
    private (AnalysisTree Tree, AnalysisOrderedRows Order, AnalysisSnapshotLease? Lease, string Scope, AnalysisTableSort Sort, bool Descending)? _pending;
    private bool _disposed, _building, _rebuildRequested;
    private int _generation, _interaction;
    private readonly List<IAnalysisBackend> _prepared = [];
    private ImmutableArray<string> _preparedInput = [];
    public AnalysisSelectionState Selection { get; } = new();
    public AnalysisTree? Tree { get; private set; }
    public ImmutableArray<AnalysisTreeItem> Rows { get; private set; } = [];
    public AnalysisMetric Metric { get; set; } = AnalysisMetric.OnDisk;
    public string Chart { get; set; } = "Treemap";
    public AnalysisColorDimension ColorDimension { get; set; } = AnalysisColorDimension.FileType;
    public AnalysisComponentPreset Components { get; set; }
    public string Query { get; set; } = "";
    public string? RootFilter { get; set; }
    public bool CaseSensitive { get; set; }
    public bool ShowUnknownFilterResults { get; set; }
    public string Status { get; private set; } = "Choose locations. Standard analysis reads metadata, not file contents.";
    public string? FilterError { get; private set; }
    public string? ResolvedTargets { get; private set; }
    public string? AcquisitionNotice { get; private set; }
    public bool Dirty { get; private set; }
    public bool IsPreparing { get; private set; }
    public AnalysisPhase Phase { get; private set; } = AnalysisPhase.Ready;
    public ulong SourceCloseFailures { get; private set; }
    public bool Running => Phase is AnalysisPhase.Scanning or AnalysisPhase.Pausing or AnalysisPhase.Paused or AnalysisPhase.Refreshing;
    public bool HasTemporaryWork => !_disposed && (Comparison is not null || IsPreparing || Running || _prepared.Count > 0 || Selection.Marks.Count > 0 || Selection.Basket.Count > 0 ||
        !DisplayedRevisionSaved && (_session is not null || _observation is { Origin: AnalysisOrigin.LiveSession, Persistence: AnalysisPersistence.Temporary }));
    public event Action? Changed;
    public DiskSpaceViewModel(AppServices services) : this(services, Prepare) { }
    internal DiskSpaceViewModel(AppServices services, Func<string, IAnalysisBackend> prepare)
    { _services = services; _zone = TimeZoneInfo.Local; _prepareSource = prepare; }
    public DiskSpaceViewModel(AnalysisSnapshot observations)
    {
        _observation = observations;
        // The recorded phase is historical data. Opening an inventory never starts or resumes its old scan.
        Phase = observations.Phase is AnalysisPhase.Ready or AnalysisPhase.Scanning or AnalysisPhase.Pausing or AnalysisPhase.Paused or AnalysisPhase.Refreshing
            ? AnalysisPhase.Stopped : observations.Phase;
        try { _zone = TimeZoneInfo.FindSystemTimeZoneById(observations.Identity.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { _zone = null; }
        AcquisitionNotice = PrivateBoundaryNotice(observations);
        Status = "Observation view. Current filesystem actions require an explicit live source binding.";
    }
    // Transfers one captured-data owner, not source/backend authority. The store's shared memory
    // reservation remains held through this view, its publications and any canceled actual workers.
    internal DiskSpaceViewModel(AnalysisSnapshotLease observations, AppServices? services, bool memoryFallback) : this(observations.Snapshot)
    {
        _capturedOwner = observations; _services = services;
        _privateStorageUnavailable = observations.Snapshot.Identity.HostProfileIdentity is null;
        if (memoryFallback)
        {
            AcquisitionNotice = "Secure scratch is unavailable. This analysis uses bounded memory." + (AcquisitionNotice is { } notice ? " " + notice : "");
            Status = "Memory analysis. Current filesystem actions require a fresh live source binding.";
        }
        if (observations.Snapshot.Identity.HostProfileIdentity is not { HasHostContext: true })
            ContinuityNotice = "Host/profile continuity is unknown. Comparison uses recorded paths.";
    }
    private static IAnalysisBackend Prepare(string path) => OperatingSystem.IsWindows()
        ? new FileCat.Platform.Windows.Analysis.WindowsAnalysisBackend(path) : new UnixAnalysisBackend(path);
    private static bool SameLocator(string a, string b)
    {
        if (a.StartsWith("\\\\?\\", StringComparison.Ordinal)) a = a[4..];
        return a.TrimEnd(Path.DirectorySeparatorChar).Equals(b.TrimEnd(Path.DirectorySeparatorChar), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    private void UpdateProgress(AnalysisProgress progress)
    {
        if (_disposed) return;
        Phase = progress.Phase; Dirty = true;
        SourceCloseFailures = progress.SourceCloseFailures;
        SetStatus($"{progress.Phase} · {progress.ObservedPaths:N0} observed paths · {progress.Issues:N0} issues. Values are observations over time." +
            (progress.MemoryFallback ? " Using bounded memory; analysis stops with partial results if the memory limit is reached." : "") +
            (SourceCloseFailures == 0 ? "" : $" Source cleanup reported {SourceCloseFailures:N0} " + (SourceCloseFailures == 1 ? "failure" : "failures") + "; close was not retried."));
    }
    public void PauseOrResume() { if (Phase == AnalysisPhase.Paused || Phase == AnalysisPhase.Pausing) _session?.Resume(); else _session?.Pause(); }
    public void Stop()
    {
        if (_preparationCancel is { } preparation)
        {
            preparation.Cancel(); SetStatus("Stopping location binding. Waiting for its native request to return…");
        }
        _session?.Stop();
    }
    public void SetStatus(string text) { if (_disposed) return; Status = text; Changed?.Invoke(); }
    public async Task RebuildAsync(bool force = false)
    {
        if (_disposed || Comparison is not null) return;
        if (_building) { if (force) { _rebuildRequested = true; _projectionCancel?.Cancel(); } return; }
        if (!force && !Dirty && Tree is not null) return;
        using var actualWork = _actualWork.Enter();
        _building = true; Dirty = false;
        int generation = ++_generation;
        _projectionCancel?.Cancel(); _projectionCancel?.Dispose(); _projectionCancel = new();
        var token = _projectionCancel.Token;
        string query = Query, scope = Selection.Scope; var metric = Metric; bool sensitive = CaseSensitive; string? root = RootFilter;
        var sort = Sort; bool descending = SortDescending;
        if (string.IsNullOrWhiteSpace(query) && root is null) ShowUnknownFilterResults = false;
        bool showUnknown = ShowUnknownFilterResults;
        AnalysisSnapshotLease? nextLease = null;
        AnalysisOrderedRows? nextOrder = null;
        try
        {
            var built = await Task.Run<(AnalysisTree Tree, AnalysisOrderedRows Order)?>(() =>
            {
                nextLease = _session?.AcquireSnapshot() ?? _inventory?.Retain() ?? _capturedOwner?.Retain();
                var s = nextLease?.Snapshot ?? _observation;
                if (s is null) return null;
                var rootScopes = new AnalysisRootScopeIndex(s, token);
                var filter = AnalysisFilter.Parse(query, _zone ?? TimeZoneInfo.Utc, sensitive, roots: s.Roots, rootScopes: rootScopes, allowDateOnly: _zone is not null);
                var nodes = s.Nodes.ToDictionary(n => n.Id);
                var pathTextAvailable = new Dictionary<ulong, bool>();
                foreach (var node in s.Nodes)
                {
                    token.ThrowIfCancellationRequested();
                    pathTextAvailable.Add(node.Id, node.ParentId == 0 || pathTextAvailable[node.ParentId] && AnalysisRawNames.HasLosslessText(node));
                }
                var all = AnalysisAggregate.Project(s, metric, cancellation: token);
                var allocation = s.Facts.Where(f => f.ObjectId is not null).ToLookup(f => f.ObjectId!, StringComparer.Ordinal);
                var unknown = ImmutableHashSet.CreateBuilder<ulong>();
                AnalysisMeasurement Allocation(AnalysisObject? obj)
                {
                    if (obj is null) return AnalysisMeasurement.Missing(MeasurementState.Unsupported);
                    return AnalysisAggregate.CompatibleAllocation(allocation[obj.Id]);
                }
                bool Matches(AnalysisNode n, AnalysisObject? obj)
                {
                    var active = all.UnmeasuredStates.GetValueOrDefault(n.Id, metric == AnalysisMetric.UniqueFileSize && obj?.VerifiedIdentity is null ? MeasurementState.Unsupported : MeasurementState.Known);
                    var truth = filter.Evaluate(new(n, obj, RelativePath(n, nodes), Allocation(obj), Stale: s.Freshness != AnalysisFreshness.Observed,
                        ActiveState: active, RelativePathTextAvailable: pathTextAvailable[n.Id]));
                    if (root is not null) truth = AnalysisFilter.And(rootScopes.IsMember(root, n), truth);
                    if (truth == AnalysisTruth.Unknown) unknown.Add(n.Id);
                    return showUnknown ? truth == AnalysisTruth.Unknown : truth == AnalysisTruth.True;
                }
                bool filtered = !string.IsNullOrWhiteSpace(query) || root is not null;
                // A location selector is a namespace scope, not a file predicate. Keep that scope's
                // independent directory allocation; name/type/age predicates do not match non-file facts.
                Func<AnalysisNode, bool>? directoryFilter = root is not null && string.IsNullOrWhiteSpace(query) && !showUnknown
                    ? n => rootScopes.IsMember(root, n) == AnalysisTruth.True : null;
                var projection = filtered ? AnalysisAggregate.Project(s, metric, Matches, token, directoryFilter) : all;
                projection = projection with { UnknownFilterNodes = unknown.ToImmutable(), IncludesUnknownFilterResults = filtered && showUnknown, FilterText = query };
                var tree = AnalysisTree.Build(s, projection, filtered, token);
                nextOrder = _orderRows(tree.ChildrenOf(scope), sort, descending, token);
                return (Tree: tree, Order: nextOrder);
            }, token);
            if (_disposed || token.IsCancellationRequested || generation != _generation || built is null) { nextLease?.Dispose(); nextOrder?.Dispose(); return; }
            while (scope != Selection.Scope)
            {
                scope = Selection.Scope; string requestedScope = scope;
                nextOrder?.Dispose(); nextOrder = null;
                nextOrder = await Task.Run(() => _orderRows(built.Value.Tree.ChildrenOf(requestedScope), sort, descending, token), token);
                built = (built.Value.Tree, nextOrder);
            }
            if (_disposed || token.IsCancellationRequested || Sort != sort || SortDescending != descending) { nextLease?.Dispose(); nextOrder?.Dispose(); return; }
            FilterError = null;
            if (_interaction > 0)
            {
                _pending?.Lease?.Dispose(); _pending?.Order.Dispose(); _pending = (built.Value.Tree, built.Value.Order, nextLease, scope, sort, descending); nextLease = null; nextOrder = null;
                SetStatus("New results available. The current interaction keeps its original targets.");
            }
            else
            {
                var publishedLease = nextLease; nextLease = null; nextOrder = null;
                Publish(built.Value.Tree, built.Value.Order, publishedLease);
            }
        }
        catch (OperationCanceledException) { nextLease?.Dispose(); nextOrder?.Dispose(); }
        catch (ObjectDisposedException) when (_disposed || token.IsCancellationRequested) { nextLease?.Dispose(); nextOrder?.Dispose(); }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidDataException or ArgumentOutOfRangeException)
        { nextLease?.Dispose(); nextOrder?.Dispose(); FilterError = "Filter not applied. " + AnalysisNames.Display(ex.Message, 240); Changed?.Invoke(); }
        catch (InvalidOperationException ex)
        { nextLease?.Dispose(); nextOrder?.Dispose(); SetStatus("View not applied. " + AnalysisNames.Display(ex.Message, 240)); }
        finally
        {
            _building = false;
            if (_rebuildRequested && !_disposed) { _rebuildRequested = false; await RebuildAsync(true); }
        }
    }
    private void Publish(AnalysisTree tree, AnalysisOrderedRows order, AnalysisSnapshotLease? lease)
    {
        if (_session is not null)
            AcquisitionNotice = (_acquisition?.MemoryPreparationNotice ?? "This analysis uses bounded memory. It stops with partial results if the memory limit is reached.") +
                (PrivateBoundaryNotice(tree.Snapshot) is { } notice ? " " + notice : "");
        RebindTableRangeOrder(tree, order);
        var old = _lease; var oldOrder = _orderedRows; _lease = lease; _orderedRows = order; Tree = tree; Rows = order.Rows; _observation = tree.Snapshot;
        try { Changed?.Invoke(); } finally { old?.Dispose(); oldOrder?.Dispose(); }
    }
    public void Interaction(bool start)
    {
        _interaction = Math.Max(0, _interaction + (start ? 1 : -1));
        if (_interaction == 0 && _pending is { } pending)
        {
            _pending = null;
            if (pending.Scope == Selection.Scope && pending.Sort == Sort && pending.Descending == SortDescending) Publish(pending.Tree, pending.Order, pending.Lease);
            else { pending.Lease?.Dispose(); pending.Order.Dispose(); _ = RebuildAsync(true); }
        }
    }
    public void Focus(string key, bool toggleMark = false, bool open = false)
    {
        SetTableAnchor(key);
        Selection.SetFocus(key);
        if (toggleMark && Tree?.Items.GetValueOrDefault(key)?.NodeId is { } id)
        { try { Selection.ToggleMark(id); } catch (InvalidOperationException ex) { SetStatus(ex.Message); } }
        if (open && Tree is { } tree && (key.StartsWith("other:", StringComparison.Ordinal) || tree.Items.GetValueOrDefault(key) is { } item && (item.Kind == AnalysisKind.Directory || item.Members.Length > 0)))
        { NavigateRows(key, () => Selection.Navigate(key)); }
        Changed?.Invoke();
    }
    public void Parent() { if (Tree is { } tree) { string parent = tree.ParentOf(Selection.Scope); NavigateRows(parent, () => Selection.Navigate(parent)); } }
    public void Back() { if (Selection.BackScope is { } scope) NavigateRows(scope, Selection.Back); else _rowOrderCancel?.Cancel(); }
    public void Forward() { if (Selection.ForwardScope is { } scope) NavigateRows(scope, Selection.Forward); else _rowOrderCancel?.Cancel(); }
    private async void NavigateRows(string scope, Action commit)
    {
        _rowOrderCancel?.Cancel();
        var tree = Tree; string previousScope = Selection.Scope; var sort = Sort; bool descending = SortDescending;
        if (_disposed || tree is null || scope == previousScope) return;
        if (!tree.Items.ContainsKey(scope) && !scope.StartsWith("other:", StringComparison.Ordinal))
        { SetStatus("That captured location is no longer present in this revision."); return; }
        // Navigation can outlive the tab while its actual CPU worker unwinds cancellation.
        AnalysisSnapshotLease? retained = null;
        IDisposable? actualWork = null;
        var cancellation = new CancellationTokenSource(); _rowOrderCancel = cancellation;
        AnalysisOrderedRows? order = null;
        // Keep chart/table/breadcrumb/history on one published scope until actual admission
        // succeeds. Incoming scan revisions stay pending during this CPU interaction.
        Interaction(true);
        try
        {
            actualWork = _actualWork.Enter();
            retained = _lease?.Retain();
            while (true)
            {
                order = await Task.Run(() => _orderRows(tree.ChildrenOf(scope), sort, descending, cancellation.Token), cancellation.Token);
                if (_disposed || cancellation.IsCancellationRequested || Tree != tree || Selection.Scope != previousScope) return;
                if (Sort == sort && SortDescending == descending) break;
                order.Dispose(); order = null; sort = Sort; descending = SortDescending;
            }
            commit();
            var previous = _orderedRows; _orderedRows = order; Rows = order.Rows; order = null;
            try { SetStatus("Viewing captured location: " + AnalysisNames.Display(tree.NameOf(scope), 120) + "."); } finally { previous?.Dispose(); }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (_disposed) { }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException or InvalidOperationException)
        { SetStatus("This scope could not be displayed: " + AnalysisNames.Display(ex.Message, 240)); }
        finally
        {
            order?.Dispose(); if (ReferenceEquals(_rowOrderCancel, cancellation)) _rowOrderCancel = null;
            try { cancellation.Dispose(); Interaction(false); }
            finally { retained?.Dispose(); actualWork?.Dispose(); }
        }
    }
    public void ToggleFocusedMark() { if (Selection.Focus is { } key) Focus(key, toggleMark: true); }
    public void MarkCurrentMembers()
    {
        if (Tree is not { } tree) return;
        try { Selection.MarkCaptured(tree.CaptureMembers(Selection.Focus ?? Selection.Scope)); Changed?.Invoke(); }
        catch (InvalidOperationException ex) { SetStatus(ex.Message); }
    }
    public void StageSelection()
    {
        var ids = Selection.Marks;
        if (ids.Count == 0 && Tree?.Items.GetValueOrDefault(Selection.Focus ?? "")?.NodeId is { } id) ids = ids.Add(id);
        try { Selection.StageCaptured(ids); Changed?.Invoke(); }
        catch (InvalidOperationException ex) { SetStatus(ex.Message); }
    }
    public string? FocusedPath()
    {
        if (Tree is not { } tree || tree.Items.GetValueOrDefault(Selection.Focus ?? "")?.NodeId is not { } id) return null;
        var nodes = tree.Snapshot.Nodes.ToDictionary(n => n.Id); var node = nodes[id];
        for (var current = node; ; current = nodes[current.ParentId])
        {
            if (!AnalysisRawNames.HasLosslessText(current)) return null;
            if (current.ParentId == 0) break;
        }
        var root = tree.Snapshot.Roots.First(r => r.Id == node.RootId);
        string relative = RelativePath(node, nodes);
        bool windows = root.CanonicalLocator.StartsWith("\\\\", StringComparison.Ordinal) || root.CanonicalLocator.Length > 1 && root.CanonicalLocator[1] == ':';
        if (windows) relative = relative.Replace('/', '\\');
        return root.CanonicalLocator.TrimEnd('/', '\\') + (relative.Length == 0 ? "" : (windows ? "\\" : "/") + relative);
    }
    public string FocusedDisplayPath()
    {
        if (Tree is not { } tree || tree.Items.GetValueOrDefault(Selection.Focus ?? "")?.NodeId is not { } id) return "Accounting group";
        if (FocusedPath() is not { } path) return "Raw name has no lossless clipboard path.";
        var node = tree.Snapshot.Nodes.First(n => n.Id == id);
        return AnalysisNames.DisplayPath(path, tree.Snapshot.Roots.First(r => r.Id == node.RootId).NameRules);
    }
    public static string RelativePath(AnalysisNode node, IReadOnlyDictionary<ulong, AnalysisNode> nodes)
    {
        var names = new Stack<string>(); int depth = 0;
        while (node.ParentId != 0)
        {
            if (++depth > AnalysisLimits.Depth) throw new InvalidDataException("Invalid observation ancestry.");
            names.Push(node.Name); node = nodes[node.ParentId];
        }
        return string.Join('/', names);
    }
    public static string StatusText(AnalysisTreeItem item) => !item.IncludedByPolicy ? "Excluded by policy"
        : item.MetadataState != MeasurementState.Known ? item.MetadataState + " metadata"
        : item.IsNonFileLeaf ? item.Kind == AnalysisKind.Link ? "Link not followed" : "Special entry not traversed"
        : item.IdentityUnverified > 0 ? "Identity unverified" : item.Unknown ? "Partial" : item.SharedElsewhere ? "Shared links" : item.Synthetic ? "Group" : "Observed";
    public static string? EntryExplanation(AnalysisTreeItem item) => !item.IsNonFileLeaf ? null : item.Kind == AnalysisKind.Link
        ? "Navigation link. Its target was not followed or measured. It contributes no file size, allocation or regular-file count to this analysis."
        : "Special entry. The scan did not traverse it or open it for content. It contributes no file size, allocation or regular-file count to this analysis.";
    public static string LogicalText(AnalysisTreeItem item, bool exact = false) => !item.IncludedByPolicy ? "Excluded"
        : item.IsNonFileLeaf ? "Not measured"
        : item.LogicalUnknown && item.LogicalSize == 0 ? "Unknown"
        : (exact ? item.LogicalSize.ToString(CultureInfo.InvariantCulture) + " bytes" : Bytes(item.LogicalSize)) + (item.LogicalUnknown ? " known" : "");
    public static string ValueText(AnalysisTreeItem item, AnalysisMetric metric, bool exact = false) => !item.IncludedByPolicy ? "Excluded"
        : item.IsNonFileLeaf ? metric == AnalysisMetric.Files ? "Not counted" : "Not measured"
        : item.SharedElsewhere && item.Value == 0 ? item.ChargeLocation ?? "Shared at ancestor"
        : item.Unknown && item.Value == 0 ? "Unknown" : metric == AnalysisMetric.Files ? item.Value.ToString(exact ? "0" : "N0", exact ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture)
        : (exact ? item.Value.ToString(CultureInfo.InvariantCulture) + " bytes" : Bytes(item.Value)) +
        (item.IdentityUnverified > 0 ? " · unverified" : item.Unknown ? " known" : "");
    public string ScopeSummary
    {
        get
        {
            if (Tree is not { } tree) return "No measurements yet";
            string label = tree.Projection.Metric switch { AnalysisMetric.FileSize => "File size", AnalysisMetric.UniqueFileSize => "Verified unique file size",
                AnalysisMetric.OnDisk => "On disk — reported", _ => "Files" };
            if (tree.Projection.Metric == AnalysisMetric.OnDisk && !tree.Projection.Totals.CompatibleBasis)
                return label + ": allocation bases differ; no combined total.";
            string scope = Selection.Scope; UInt128 value = tree.ScopeValue(scope); var item = tree.Items.GetValueOrDefault(scope);
            string amount = item is not null && item.IdentityUnverified > 0
                ? $"{Bytes(value - item.IdentityUnverified)} known with verified identities + {Bytes(item.IdentityUnverified)} reported for identity-unverified paths"
                : (tree.Projection.Metric == AnalysisMetric.Files ? value.ToString("N0", CultureInfo.CurrentCulture) + (value == 1 ? " file" : " files") : Bytes(value)) + " known";
            return label + ": " + amount + (item is null ? "" : $" · {item.Files:N0} observed file " + (item.Files == 1 ? "path" : "paths")) + (item?.Unknown == true ? " · partial" : "");
        }
    }
    public static string Bytes(UInt128 bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB"];
        double value = (double)bytes; int at = 0;
        while (value >= 1024 && at < units.Length - 1) { value /= 1024; at++; }
        return value.ToString(at == 0 ? "N0" : "N1", CultureInfo.CurrentCulture) + " " + units[at];
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_generation;
        var actualWork = _actualWork.CloseAsync();
        _preparationCancel?.Cancel();
        _projectionCancel?.Cancel(); _projectionCancel?.Dispose(); _session?.Stop(); _lease?.Dispose(); _pending?.Lease?.Dispose();
        _inventory?.Dispose();
        _capturedOwner?.Dispose(); _capturedOwner = null;
        Comparison?.Dispose(); Comparison = null; _comparisonPrevious?.Dispose(); _comparisonPrevious = null; _comparisonCurrent?.Dispose(); _comparisonCurrent = null;
        _rowOrderCancel?.Cancel(); _orderedRows?.Dispose(); _pending?.Order.Dispose();
        _rangeBaseline = _rangePublishedMarks = null; _rangeOrderOwner = null; _tableAnchor = null;
        Tree = null; Rows = []; _observation = null; _pending = null; Changed = null;
        CloseCompletion = CloseAcquisitionAsync(actualWork);
    }
    private sealed class LatestProgress(IUiDispatcher ui, Action<AnalysisProgress> consume) : IProgress<AnalysisProgress>
    {
        private readonly object _sync = new(); private AnalysisProgress? _latest; private bool _queued;
        public void Report(AnalysisProgress value)
        {
            lock (_sync) { _latest = value; if (_queued) return; _queued = true; }
            ui.Post(() => { AnalysisProgress? latest; lock (_sync) { latest = _latest; _queued = false; } if (latest is not null) consume(latest); });
        }
    }
}
