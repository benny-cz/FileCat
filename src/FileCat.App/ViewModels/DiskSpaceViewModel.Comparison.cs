using FileCat.Core.Analysis;
using System.Collections.Immutable;

namespace FileCat.App.ViewModels;

public sealed partial class DiskSpaceViewModel
{
    private AnalysisSnapshotLease? _comparisonCurrent;
    private AnalysisInventory? _comparisonPrevious;
    public AnalysisComparisonResult? Comparison { get; private set; }
    public bool CanCompare => !_disposed && Comparison is null && HasPrivateStorage && Tree is not null && _lease is not null;
    public SessionIdentity? ComparisonPreviousIdentity => _comparisonPrevious?.Snapshot.Identity;
    public SessionIdentity? ComparisonCurrentIdentity => _comparisonCurrent?.Snapshot.Identity;
    public string? ComparisonPreviousName { get; private set; }
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
        if (!CanCompare) throw new InvalidOperationException("Choose a published inventory with private profile storage before comparing.");
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        AnalysisPrivateInventoryStore.ValidateName(name);
        AnalysisSnapshotLease? current = captured.Retain(); AnalysisInventory? previous = null; AnalysisComparisonResult? result = null;
        try
        {
            string path = _services!.Paths.AnalysisSnapshotsDirectory;
            previous = await RunPrivateIoAsync(path, token =>
            {
                using var store = OpenPrivateStore(path);
                if (entry is not null && entry.Name != name) throw new InvalidDataException("The selected comparison entry does not match its filename.");
                return entry is null ? store.Load(name, token) : new AnalysisSavedCatalog(store).Load(entry, token);
            }, cancellation);
            var before = previous.Snapshot; var now = current.Snapshot;
            result = await Task.Run(() => AnalysisComparison.Build(before, now, metric, cancellation: cancellation), CancellationToken.None);
            cancellation.ThrowIfCancellationRequested();
            var model = new DiskSpaceViewModel(_services)
            {
                _comparisonCurrent = current, _comparisonPrevious = previous, Comparison = result, ComparisonPreviousName = name,
                Metric = metric, Chart = "Table", Phase = AnalysisPhase.Stopped,
                Status = "Offline comparison. Source files are not connected; differences are metadata observations."
            };
            current = null; previous = null; result = null; return model;
        }
        finally { result?.Dispose(); previous?.Dispose(); current?.Dispose(); }
    }
}
