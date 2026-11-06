using FileCat.Core.Analysis;
using System.Collections.Immutable;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private AnalysisSnapshotLease? _comparisonCurrent;
    private AnalysisSnapshotLease? _comparisonPrevious;
    public AnalysisComparisonResult? Comparison { get; private set; }
    public bool CanCompare => !_disposed && Comparison is null && HasPrivateStorage && Tree is not null && _lease is not null;
    public SessionIdentity? ComparisonPreviousIdentity => _comparisonPrevious?.Snapshot.Identity;
    public SessionIdentity? ComparisonCurrentIdentity => _comparisonCurrent?.Snapshot.Identity;
    public string? ComparisonPreviousName { get; private set; }
    internal async Task<AnalysisComparisonQueryResult> QueryComparisonAsync(string query, bool sensitive,
        AnalysisComparisonSide side, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var actualWork = _actualWork.Enter();
        var comparison = Comparison ?? throw new InvalidOperationException("Choose a comparison before applying a comparison query.");
        using var before = _comparisonPrevious!.Retain(); using var current = _comparisonCurrent!.Retain();
        AnalysisComparisonResult.ReadLease? rows = comparison.Retain(); AnalysisComparisonQueryResult? result = null;
        try
        {
            var previousSnapshot = before.Snapshot; var currentSnapshot = current.Snapshot; var retained = rows;
            result = await Task.Run(() => AnalysisComparisonQuery.Apply(previousSnapshot, currentSnapshot, comparison.Metric, retained,
                query, sensitive, side, cancellation), CancellationToken.None);
            rows = null; cancellation.ThrowIfCancellationRequested();
            var success = result; result = null; return success;
        }
        finally { result?.Dispose(); rows?.Dispose(); }
    }
    internal ImmutableArray<string> ComparisonBases(bool previous, AnalysisComparisonEntity? entity)
    {
        var snapshot = previous ? _comparisonPrevious?.Snapshot : _comparisonCurrent?.Snapshot;
        if (snapshot is null || entity is null) return [];
        var nodes = entity.NodeIds.ToHashSet();
        var objects = snapshot.Nodes.Where(n => nodes.Contains(n.Id) && n.ObjectId is not null).Select(n => n.ObjectId!).ToHashSet(StringComparer.Ordinal);
        var facts = snapshot.Facts.Where(f => f.DirectoryId is { } id && nodes.Contains(id) || f.ObjectId is { } obj && objects.Contains(obj)).ToImmutableArray();
        return AnalysisComponents.Summarize(snapshot with { Facts = facts }).Select(c => AnalysisNames.Display(c.ComponentId, 256) + " · " + AnalysisNames.Display(c.BasisId, 256) + " · " +
            string.Join(", ", c.States.OrderBy(p => p.Key).Select(p => p.Value.ToString("N0") + " " + p.Key))).ToImmutableArray();
    }

    public async Task<DiskSpaceViewModel> CompareSavedAsync(string name, CancellationToken cancellation = default)
    {
        using var revision = CaptureDisplayedRevision();
        return await CompareSavedAsync(name, null, revision, Metric, cancellation);
    }

    internal async Task<DiskSpaceViewModel> CompareSavedAsync(string name, AnalysisSavedEntry? entry,
        AnalysisSnapshotLease captured, AnalysisMetric metric, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var actualWork = _actualWork.Enter();
        using var inputs = await PrepareComparisonAsync(name, entry, captured, metric, cancellation);
        return await inputs.BuildAsync(null, cancellation);
    }

    internal async Task<ComparisonInputs> PrepareComparisonAsync(string name, AnalysisSavedEntry? entry,
        AnalysisSnapshotLease captured, AnalysisMetric metric, CancellationToken cancellation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var actualWork = _actualWork.Enter();
        if (!CanCompare) throw new InvalidOperationException("Choose a published inventory with private profile storage before comparing.");
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        AnalysisPrivateInventoryStore.ValidateName(name);
        AnalysisSnapshotLease? current = captured.Retain(); AnalysisInventory? previous = null;
        try
        {
            string path = _services!.Paths.AnalysisSnapshotsDirectory;
            previous = await RunPrivateIoAsync(path, token =>
            {
                using var store = OpenPrivateStore(path);
                if (entry is not null && entry.Name != name) throw new InvalidDataException("The selected comparison entry does not match its filename.");
                return entry is null ? store.Load(name, token) : new AnalysisSavedCatalog(store).Load(entry, token);
            }, cancellation);
            cancellation.ThrowIfCancellationRequested();
            var inputs = new ComparisonInputs(_services, name, metric, current, previous);
            current = null; previous = null; return inputs;
        }
        finally { previous?.Dispose(); current?.Dispose(); }
    }

    /// <summary>Owns inert inputs while structural mapping is reviewed. Actual work takes independent leases.</summary>
    internal sealed class ComparisonInputs(Services.AppServices services, string name, AnalysisMetric metric,
        AnalysisSnapshotLease current, AnalysisInventory previous) : IDisposable
    {
        private readonly object _gate = new();
        private bool _disposed, _building;
        public ImmutableArray<RootBinding> PreviousRoots { get { lock (_gate) { Guard(); return previous.Snapshot.Roots; } } }
        public ImmutableArray<RootBinding> CurrentRoots { get { lock (_gate) { Guard(); return current.Snapshot.Roots; } } }
        public bool HasAutomaticRoots
        {
            get
            {
                lock (_gate)
                {
                    Guard();
                    try { return AnalysisComparison.SuggestRootMapping(previous.Snapshot, current.Snapshot).Count > 0; }
                    catch (InvalidOperationException) { return false; }
                }
            }
        }
        public async Task<DiskSpaceViewModel> BuildAsync(IReadOnlyDictionary<string, string>? structuralRoots, CancellationToken cancellation)
        {
            AnalysisSnapshotLease? now = null, before = null; AnalysisComparisonResult? result = null;
            lock (_gate)
            {
                Guard(); if (_building) throw new InvalidOperationException("These comparison inputs are already being used.");
                try { now = current.Retain(); before = previous.Retain(); _building = true; }
                catch { now?.Dispose(); before?.Dispose(); throw; }
            }
            try
            {
                var mapping = structuralRoots?.ToImmutableDictionary(StringComparer.Ordinal);
                var oldSnapshot = before.Snapshot; var currentSnapshot = now.Snapshot;
                result = await Task.Run(() => AnalysisComparison.Build(oldSnapshot, currentSnapshot, metric, mapping, cancellation), CancellationToken.None);
                cancellation.ThrowIfCancellationRequested();
                var model = new DiskSpaceViewModel(services)
                {
                    _comparisonCurrent = now, _comparisonPrevious = before, Comparison = result, ComparisonPreviousName = name,
                    Metric = metric, Chart = "Table", Phase = AnalysisPhase.Stopped,
                    Status = "Offline comparison. Source files are not connected; differences are metadata observations."
                };
                now = null; before = null; result = null; return model;
            }
            finally
            {
                result?.Dispose(); before?.Dispose(); now?.Dispose();
                lock (_gate) _building = false;
            }
        }
        private void Guard() => ObjectDisposedException.ThrowIf(_disposed, this);
        public void Dispose()
        {
            lock (_gate) { if (_disposed) return; _disposed = true; previous.Dispose(); current.Dispose(); }
        }
    }
}
