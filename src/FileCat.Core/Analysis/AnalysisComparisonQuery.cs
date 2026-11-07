using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

public enum AnalysisComparisonSide { CurrentOrPrevious, Current, Previous, Either }

/// <summary>Filtered accounting rows retain the original result admission, including after the source view closes.</summary>
public sealed class AnalysisComparisonQueryResult : IDisposable
{
    private AnalysisComparisonResult.ReadLease? _lease;
    private ImmutableArray<AnalysisComparisonRow> _matches, _unknown;
    internal AnalysisComparisonQueryResult(AnalysisComparisonResult.ReadLease lease, ImmutableArray<AnalysisComparisonRow> matches,
        ImmutableArray<AnalysisComparisonRow> unknown)
    { _lease = lease; _matches = matches; _unknown = unknown; }
    public ImmutableArray<AnalysisComparisonRow> Matches => _lease is not null ? _matches : throw new ObjectDisposedException(nameof(AnalysisComparisonQueryResult));
    public ImmutableArray<AnalysisComparisonRow> Unknown => _lease is not null ? _unknown : throw new ObjectDisposedException(nameof(AnalysisComparisonQueryResult));
    public AnalysisTaint Taint => (_lease ?? throw new ObjectDisposedException(nameof(AnalysisComparisonQueryResult))).Taint;
    public void Dispose() { var lease = Interlocked.Exchange(ref _lease, null); _matches = []; _unknown = []; lease?.Dispose(); }
}

/// <summary>Data-only three-valued predicates over either recorded side, without splitting or recounting shared rows.</summary>
public static class AnalysisComparisonQuery
{
    private const long IndexBudget = 8L * 1024 * 1024;
    private static int s_query;
    public static AnalysisComparisonQueryResult Apply(AnalysisSnapshot previous, AnalysisSnapshot current, AnalysisMetric metric,
        AnalysisComparisonResult.ReadLease rows, string query, bool sensitive = false,
        AnalysisComparisonSide side = AnalysisComparisonSide.CurrentOrPrevious, CancellationToken cancellation = default,
        IReadOnlyDictionary<string, string>? previousRootDisplayIds = null, IReadOnlyDictionary<string, string>? currentRootDisplayIds = null)
    {
        ArgumentNullException.ThrowIfNull(rows);
        // Ownership transfers only on success. The caller retains and releases its lease on failure.
        if (Interlocked.CompareExchange(ref s_query, 1, 0) != 0) throw new InvalidOperationException("The comparison query work budget is in use. Wait for the other query to finish.");
        try { return ApplyCore(previous, current, metric, rows, query, sensitive, side, cancellation, previousRootDisplayIds, currentRootDisplayIds); }
        finally { Volatile.Write(ref s_query, 0); }
    }
    private static AnalysisComparisonQueryResult ApplyCore(AnalysisSnapshot previous, AnalysisSnapshot current, AnalysisMetric metric,
        AnalysisComparisonResult.ReadLease rows, string query, bool sensitive, AnalysisComparisonSide side, CancellationToken cancellation,
        IReadOnlyDictionary<string, string>? previousRootDisplayIds, IReadOnlyDictionary<string, string>? currentRootDisplayIds)
    {
        if (!Enum.IsDefined(side) || !Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(side));
        cancellation.ThrowIfCancellationRequested();
        long cost = checked(((long)previous.Nodes.Length + current.Nodes.Length + previous.Objects.Length + current.Objects.Length + previous.Facts.Length + current.Facts.Length) * 512);
        if (cost > IndexBudget) throw new InvalidOperationException("This comparison query exceeds its memory budget. The last applied filter remains visible.");
        var observedRows = rows.Rows;
        bool needsPrevious = side is AnalysisComparisonSide.Previous or AnalysisComparisonSide.Either || side == AnalysisComparisonSide.CurrentOrPrevious && observedRows.Any(r => r.Current is null);
        bool needsCurrent = side is AnalysisComparisonSide.Current or AnalysisComparisonSide.Either || side == AnalysisComparisonSide.CurrentOrPrevious && observedRows.Any(r => r.Current is not null);
        // An empty comparison still parses the query rather than accepting invalid expressions silently.
        if (!needsPrevious && !needsCurrent) needsCurrent = true;
        var before = !needsPrevious ? null : new Context(previous, query, sensitive, cancellation, previousRootDisplayIds);
        var now = !needsCurrent ? null : new Context(current, query, sensitive, cancellation, currentRootDisplayIds);
        var matches = ImmutableArray.CreateBuilder<AnalysisComparisonRow>(); var unknown = ImmutableArray.CreateBuilder<AnalysisComparisonRow>();
        foreach (var row in observedRows)
        {
            cancellation.ThrowIfCancellationRequested();
            AnalysisTruth Old() => row.Previous is null ? AnalysisTruth.Unknown : before!.Evaluate(row, row.Previous, metric, cancellation);
            AnalysisTruth New() => row.Current is null ? AnalysisTruth.Unknown : now!.Evaluate(row, row.Current, metric, cancellation);
            var truth = side switch
            {
                AnalysisComparisonSide.Current => New(),
                AnalysisComparisonSide.Previous => Old(),
                AnalysisComparisonSide.Either => AnalysisFilter.Or(row.Previous is null ? AnalysisTruth.False : Old(), row.Current is null ? AnalysisTruth.False : New()),
                _ => row.Current is null ? Old() : New()
            };
            if (truth == AnalysisTruth.True) matches.Add(row); else if (truth == AnalysisTruth.Unknown) unknown.Add(row);
        }
        cancellation.ThrowIfCancellationRequested();
        return new(rows, matches.ToImmutable(), unknown.ToImmutable());
    }

    private sealed class Context
    {
        private readonly Dictionary<ulong, AnalysisNode> _nodes;
        private readonly Dictionary<string, AnalysisObject> _objects;
        private readonly ILookup<string, StorageFact> _facts;
        private readonly ILookup<ulong, StorageFact> _directoryFacts;
        private readonly AnalysisFilter _filter;
        private readonly Dictionary<ulong, bool> _text = [];
        public Context(AnalysisSnapshot snapshot, string query, bool sensitive, CancellationToken cancellation, IReadOnlyDictionary<string, string>? rootDisplayIds)
        {
            TimeZoneInfo? zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(snapshot.Identity.TimeZoneId); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { zone = null; }
            _filter = AnalysisFilter.Parse(query, zone ?? TimeZoneInfo.Utc, sensitive, comparison: true,
                roots: snapshot.Roots, rootScopes: new AnalysisRootScopeIndex(snapshot, cancellation), allowDateOnly: zone is not null, rootDisplayIds: rootDisplayIds);
            _nodes = snapshot.Nodes.ToDictionary(n => n.Id); _objects = snapshot.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
            _facts = snapshot.Facts.Where(f => f.ObjectId is not null).ToLookup(f => f.ObjectId!, StringComparer.Ordinal);
            _directoryFacts = snapshot.Facts.Where(f => f.DirectoryId is not null).ToLookup(f => f.DirectoryId!.Value);
            foreach (var node in snapshot.Nodes)
            {
                cancellation.ThrowIfCancellationRequested();
                _text[node.Id] = node.ParentId == 0 && node.Kind == AnalysisKind.Directory || AnalysisRawNames.HasLosslessText(node) && (node.ParentId == 0 || _text[node.ParentId]);
            }
        }
        public AnalysisTruth Evaluate(AnalysisComparisonRow row, AnalysisComparisonEntity entity, AnalysisMetric metric, CancellationToken cancellation)
        {
            var truth = AnalysisTruth.False;
            foreach (ulong id in entity.NodeIds)
            {
                cancellation.ThrowIfCancellationRequested(); var node = _nodes[id];
                var obj = node.ObjectId is { } objectId ? _objects.GetValueOrDefault(objectId) : null;
                var names = new Stack<string>(); var cursor = node; long pathBytes = 0;
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    pathBytes = checked(pathBytes + cursor.Name.Length * 2L + 2);
                    if (pathBytes > 2L * 1024 * 1024) throw new InvalidOperationException("The comparison query path exceeds its work budget.");
                    if (cursor.ParentId == 0) { if (cursor.Kind != AnalysisKind.Directory) names.Push(cursor.Name); break; }
                    names.Push(cursor.Name); cursor = _nodes[cursor.ParentId];
                }
                var allocation = node.Kind == AnalysisKind.Directory ? AnalysisAggregate.CompatibleAllocation(_directoryFacts[node.Id]) :
                    obj is null ? AnalysisMeasurement.Missing(MeasurementState.Unsupported) : AnalysisAggregate.CompatibleAllocation(_facts[obj.Id]);
                var active = metric is AnalysisMetric.OnDisk or AnalysisMetric.UniqueFileSize &&
                    (node.Kind == AnalysisKind.Directory ? node.ObservedIdentity is null : obj?.VerifiedIdentity is null) && entity.Value.IsKnown ? MeasurementState.Unsupported : entity.Value.State;
                var filterObject = obj ?? (node.Times is { } times ? new AnalysisObject("recorded-metadata", null,
                    AnalysisMeasurement.Missing(MeasurementState.Unsupported), AnalysisMeasurement.Missing(MeasurementState.Unsupported), times.ModifiedUtc,
                    ModifiedPrecisionTicks: times.ModifiedPrecisionTicks) : null);
                var value = _filter.Evaluate(new(node, filterObject, string.Join('/', names), allocation, Change: Change(row.Change), Stale: !entity.Fresh,
                    Unreadable: node.MetadataState == MeasurementState.Failed, ActiveState: active, RelativePathTextAvailable: _text[id], ExactDelta: row.Delta));
                truth = AnalysisFilter.Or(truth, value); if (truth == AnalysisTruth.True) break;
            }
            return truth;
        }
        private static string Change(AnalysisChange change) => change switch
        {
            AnalysisChange.MetadataChanged => "metadata-changed",
            AnalysisChange.NoObservedMetadataChange => "unchanged",
            AnalysisChange.PreviouslyUnknown => "previously-unknown",
            _ => change.ToString().ToLowerInvariant()
        };
    }
}
