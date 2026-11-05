using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.Threading;

namespace FileCat.App.ViewModels;

/// <summary>Owns one analysis tab. Observations, displayed projection and selection never become action authority.</summary>
public sealed class DiskSpaceViewModel : IDisposable
{
    private readonly AppServices? _services;
    private readonly TimeZoneInfo? _zone;
    private AnalysisSession? _session;
    private AnalysisSnapshotLease? _lease;
    private AnalysisSnapshot? _observation;
    private CancellationTokenSource? _projectionCancel;
    private (AnalysisTree Tree, ImmutableArray<AnalysisTreeItem> Rows, AnalysisSnapshotLease? Lease, string Scope)? _pending;
    private bool _disposed, _building, _rebuildRequested;
    private int _generation, _interaction;
    private readonly List<IAnalysisBackend> _prepared = [];
    private ImmutableArray<string> _preparedInput = [];
    public AnalysisSelectionState Selection { get; } = new();
    public AnalysisTree? Tree { get; private set; }
    public ImmutableArray<AnalysisTreeItem> Rows { get; private set; } = [];
    public AnalysisMetric Metric { get; set; } = AnalysisMetric.OnDisk;
    public AnalysisComponentPreset Components { get; set; }
    public string Query { get; set; } = "";
    public string? RootFilter { get; set; }
    public bool CaseSensitive { get; set; }
    public bool ShowUnknownFilterResults { get; set; }
    public string Status { get; private set; } = "Choose locations. Standard analysis reads metadata, not file contents.";
    public string? FilterError { get; private set; }
    public string? ResolvedTargets { get; private set; }
    public bool Dirty { get; private set; }
    public bool IsPreparing { get; private set; }
    public AnalysisPhase Phase { get; private set; } = AnalysisPhase.Ready;
    public bool Running => Phase is AnalysisPhase.Scanning or AnalysisPhase.Pausing or AnalysisPhase.Paused or AnalysisPhase.Refreshing;
    public bool HasTemporaryWork => !_disposed && (IsPreparing || _session is not null || _prepared.Count > 0 || Selection.Marks.Count > 0 || Selection.Basket.Count > 0 ||
        _observation is { Origin: AnalysisOrigin.LiveSession, Persistence: AnalysisPersistence.Temporary });
    public event Action? Changed;
    public DiskSpaceViewModel(AppServices services) { _services = services; _zone = TimeZoneInfo.Local; }
    public DiskSpaceViewModel(AnalysisSnapshot observations)
    {
        _observation = observations; Phase = observations.Phase;
        try { _zone = TimeZoneInfo.FindSystemTimeZoneById(observations.Identity.TimeZoneId); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { _zone = null; }
        Status = "Observation view. Current filesystem actions require an explicit live source binding.";
    }
    public async Task StartAsync(IReadOnlyList<string> roots)
    {
        roots = roots.ToImmutableArray();
        if (_disposed || IsPreparing || Running || _services is null) return;
        if (roots.Count is < 1 or > AnalysisLimits.Roots || roots.Any(string.IsNullOrWhiteSpace))
        { SetStatus("Choose between one and 32 explicit locations."); return; }
        if (AnalysisAdmission.Refusal() is { } refusal) { SetStatus(refusal); return; }
        IsPreparing = true; SetStatus("Binding the selected locations…");
        try
        {
            if (!_preparedInput.SequenceEqual(roots, StringComparer.Ordinal) || _prepared.Count != roots.Count)
            {
                ReleasePrepared();
                foreach (string path in roots)
                {
                    string requested = Path.GetFullPath(path);
                    var source = await _services.Io.Run(PathUtil.GetDeviceKey(requested), IoPriority.Background, _ => Prepare(requested), CancellationToken.None);
                    if (_disposed) { source.Dispose(); return; }
                    _prepared.Add(source);
                }
                _preparedInput = roots.ToImmutableArray();
                ResolvedTargets = string.Join(Environment.NewLine, _prepared.Select(p => AnalysisNames.Display(p.Root.CanonicalLocator)));
                bool changed = _prepared.Where((p, i) => !SameLocator(p.Root.CanonicalLocator, Path.GetFullPath(roots[i]))).Any();
                if (changed) { SetStatus("Review the resolved locations shown below, then choose Start analysis again to use those captured targets."); return; }
            }
            _session?.Dispose();
            _session = new AnalysisCoordinator(_services.Io).Start(_prepared.ToArray(), new LatestProgress(_services.Ui, UpdateProgress), Components);
            _prepared.Clear(); _preparedInput = []; Phase = AnalysisPhase.Scanning; Dirty = true;
            SetStatus("Scanning metadata. Known results so far; allocation can share physical blocks.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        { ReleasePrepared(); SetStatus("Analysis could not start: " + AnalysisNames.Display(ex.Message, 500)); }
        finally { IsPreparing = false; Changed?.Invoke(); }
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
        SetStatus($"{progress.Phase} · {progress.ObservedPaths:N0} observed paths · {progress.Issues:N0} issues. Values are observations over time.");
    }
    public void PauseOrResume() { if (Phase == AnalysisPhase.Paused || Phase == AnalysisPhase.Pausing) _session?.Resume(); else _session?.Pause(); }
    public void Stop() => _session?.Stop();
    public void SetStatus(string text) { if (_disposed) return; Status = text; Changed?.Invoke(); }
    public async Task RebuildAsync(bool force = false)
    {
        if (_disposed) return;
        if (_building) { if (force) _rebuildRequested = true; return; }
        if (!force && !Dirty && Tree is not null) return;
        _building = true; Dirty = false;
        int generation = ++_generation;
        _projectionCancel?.Cancel(); _projectionCancel?.Dispose(); _projectionCancel = new();
        var token = _projectionCancel.Token;
        string query = Query, scope = Selection.Scope; var metric = Metric; bool sensitive = CaseSensitive; string? root = RootFilter;
        if (string.IsNullOrWhiteSpace(query) && root is null) ShowUnknownFilterResults = false;
        bool showUnknown = ShowUnknownFilterResults;
        AnalysisSnapshotLease? nextLease = null;
        try
        {
            var built = await Task.Run<(AnalysisTree Tree, ImmutableArray<AnalysisTreeItem> Rows)?>(() =>
            {
                nextLease = _session?.AcquireSnapshot();
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
                return (Tree: tree, Rows: tree.ChildrenOf(scope));
            }, token);
            if (_disposed || token.IsCancellationRequested || generation != _generation || built is null) { nextLease?.Dispose(); return; }
            while (scope != Selection.Scope)
            {
                scope = Selection.Scope; string requestedScope = scope;
                built = (built.Value.Tree, await Task.Run(() => built.Value.Tree.ChildrenOf(requestedScope), token));
            }
            if (_disposed || token.IsCancellationRequested) { nextLease?.Dispose(); return; }
            FilterError = null;
            if (_interaction > 0)
            {
                _pending?.Lease?.Dispose(); _pending = (built.Value.Tree, built.Value.Rows, nextLease, scope); nextLease = null;
                SetStatus("New results available. The current interaction keeps its original targets.");
            }
            else { Publish(built.Value.Tree, built.Value.Rows, nextLease); nextLease = null; }
        }
        catch (OperationCanceledException) { nextLease?.Dispose(); }
        catch (ObjectDisposedException) when (_disposed || token.IsCancellationRequested) { nextLease?.Dispose(); }
        catch (Exception ex) when (ex is FormatException or OverflowException or InvalidDataException or ArgumentOutOfRangeException)
        { nextLease?.Dispose(); FilterError = "Filter not applied. " + AnalysisNames.Display(ex.Message, 240); Changed?.Invoke(); }
        finally
        {
            _building = false;
            if (_rebuildRequested && !_disposed) { _rebuildRequested = false; await RebuildAsync(true); }
        }
    }
    private void Publish(AnalysisTree tree, ImmutableArray<AnalysisTreeItem> rows, AnalysisSnapshotLease? lease)
    {
        var old = _lease; _lease = lease; Tree = tree; Rows = rows; _observation = tree.Snapshot;
        Changed?.Invoke(); old?.Dispose();
    }
    public void Interaction(bool start)
    {
        _interaction = Math.Max(0, _interaction + (start ? 1 : -1));
        if (_interaction == 0 && _pending is { } pending)
        {
            _pending = null;
            if (pending.Scope == Selection.Scope) Publish(pending.Tree, pending.Rows, pending.Lease);
            else { pending.Lease?.Dispose(); _ = RebuildAsync(true); }
        }
    }
    public void Focus(string key, bool toggleMark = false, bool open = false)
    {
        Selection.SetFocus(key);
        if (toggleMark && Tree?.Items.GetValueOrDefault(key)?.NodeId is { } id)
        { try { Selection.ToggleMark(id); } catch (InvalidOperationException ex) { SetStatus(ex.Message); } }
        if (open && Tree is { } tree && (key.StartsWith("other:", StringComparison.Ordinal) || tree.Items.GetValueOrDefault(key) is { } item && (item.Kind == AnalysisKind.Directory || item.Members.Length > 0)))
        { Selection.Navigate(key); RefreshRows(); }
        Changed?.Invoke();
    }
    public void Parent() { if (Tree is { } tree) { Selection.Navigate(tree.ParentOf(Selection.Scope)); RefreshRows(); } }
    public void Back() { Selection.Back(); RefreshRows(); }
    public void Forward() { Selection.Forward(); RefreshRows(); }
    private async void RefreshRows()
    {
        var tree = Tree; string scope = Selection.Scope;
        if (tree is null) return;
        try
        {
            var rows = await Task.Run(() => tree.ChildrenOf(scope));
            if (_disposed || Tree != tree || Selection.Scope != scope) return;
            Rows = rows; Changed?.Invoke();
        }
        catch (Exception ex) when (ex is InvalidDataException or OverflowException)
        { SetStatus("This scope could not be displayed: " + AnalysisNames.Display(ex.Message, 240)); }
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
        : item.IdentityUnverified > 0 ? "Identity unverified" : item.Unknown ? "Partial" : item.SharedElsewhere ? "Shared links" : item.Synthetic ? "Group" : "Observed";
    public static string LogicalText(AnalysisTreeItem item, bool exact = false) => !item.IncludedByPolicy ? "Excluded"
        : item.LogicalUnknown && item.LogicalSize == 0 ? "Unknown"
        : (exact ? item.LogicalSize.ToString(CultureInfo.InvariantCulture) + " bytes" : Bytes(item.LogicalSize)) + (item.LogicalUnknown ? " known" : "");
    public static string ValueText(AnalysisTreeItem item, AnalysisMetric metric, bool exact = false) => !item.IncludedByPolicy ? "Excluded"
        : item.SharedElsewhere && item.Value == 0 ? "Shared at ancestor"
        : item.Unknown && item.Value == 0 ? "Unknown" : metric == AnalysisMetric.Files ? item.Value.ToString(exact ? "0" : "N0", exact ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture)
        : (exact ? item.Value.ToString(CultureInfo.InvariantCulture) + " bytes" : Bytes(item.Value)) +
        (item.IdentityUnverified > 0 ? " · unverified" : item.Unknown ? " known" : "");
    public static string Bytes(UInt128 bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB", "ZiB", "YiB"];
        double value = (double)bytes; int at = 0;
        while (value >= 1024 && at < units.Length - 1) { value /= 1024; at++; }
        return value.ToString(at == 0 ? "N0" : "N1", CultureInfo.CurrentCulture) + " " + units[at];
    }
    private void ReleasePrepared() { foreach (var source in _prepared) source.Dispose(); _prepared.Clear(); _preparedInput = []; }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_generation;
        _projectionCancel?.Cancel(); _projectionCancel?.Dispose(); _session?.Dispose(); ReleasePrepared(); _lease?.Dispose(); _pending?.Lease?.Dispose();
        Tree = null; Rows = []; _observation = null; _pending = null; Changed = null;
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
