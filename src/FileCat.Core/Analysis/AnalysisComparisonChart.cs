using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

/// <summary>
/// Bounded offline hierarchy for one recorded side. Area always comes from that
/// side's accounting projection; a signed difference is only comparison evidence.
/// Owns its input/result readers until the displayed geometry is retired.
/// </summary>
public sealed class AnalysisComparisonChart : IAnalysisChartData, IDisposable
{
    private static int s_active;
    private AnalysisTree? _tree;
    private AnalysisSnapshotLease? _snapshot;
    private AnalysisComparisonResult.ReadLease? _reader;
    private ImmutableDictionary<ulong, int> _rowByNode = ImmutableDictionary<ulong, int>.Empty;
    public AnalysisMetric Metric { get; }
    public DateTimeOffset ObservedUtc { get; }
    public bool Previous { get; }
    public ImmutableDictionary<string, AnalysisTreeItem> Items { get; private set; }
    private AnalysisComparisonChart(AnalysisSnapshotLease snapshot, AnalysisComparisonResult.ReadLease reader,
        AnalysisTree tree, bool previous, ImmutableDictionary<ulong, int> rowByNode, ImmutableDictionary<string, AnalysisTreeItem> items)
    {
        _snapshot = snapshot; _reader = reader; _tree = tree; Previous = previous;
        Metric = tree.Metric; ObservedUtc = tree.ObservedUtc; _rowByNode = rowByNode; Items = items;
    }
    public static AnalysisComparisonChart Build(AnalysisSnapshotLease input, AnalysisComparisonResult.ReadLease rows,
        AnalysisMetric metric, bool previous, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(input); ArgumentNullException.ThrowIfNull(rows);
        cancellation.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(ref s_active, 1, 0) != 0)
            throw new InvalidOperationException("The bounded comparison chart budget is in use. Close the other chart first.");
        AnalysisSnapshotLease? snapshot = null; AnalysisComparisonResult.ReadLease? reader = null;
        try
        {
            snapshot = input.Retain(); reader = rows.Retain(); var observed = snapshot.Snapshot;
            if (reader.Metric != metric || reader.Identity(previous) != observed.Identity)
                throw new InvalidOperationException("The comparison chart input does not match the recorded side and metric.");
            // Comparison inputs already have strict producer/path limits. Admit the
            // extra hierarchy separately before allocating its per-node structures.
            if (checked(observed.Nodes.Length * 2048L + observed.Facts.Length * 256L) > 24L * 1024 * 1024)
                throw new InvalidOperationException("This comparison exceeds the bounded chart fallback. The complete comparison table remains available.");
            var projection = AnalysisAggregate.Project(observed, metric, cancellation: cancellation);
            var tree = AnalysisTree.Build(observed, projection, false, cancellation);
            var byNode = ImmutableDictionary.CreateBuilder<ulong, int>();
            var changes = new Dictionary<ulong, AnalysisChange>();
            var recordedRows = reader.Rows;
            for (int i = 0; i < recordedRows.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var row = recordedRows[i]; var entity = previous ? row.Previous : row.Current;
                if (entity is null) continue;
                foreach (ulong node in entity.NodeIds)
                {
                    if (byNode.TryGetValue(node, out int existing) && existing != i)
                    { byNode[node] = -1; changes[node] = AnalysisChange.Unknown; }
                    else { byNode[node] = i; changes[node] = row.Change; }
                }
            }
            var items = tree.Items.ToBuilder();
            var pending = new Stack<(string Key, bool Visited)>(); pending.Push((AnalysisTree.RootKey, false));
            while (pending.TryPop(out var next))
            {
                cancellation.ThrowIfCancellationRequested(); var item = items[next.Key];
                if (!next.Visited)
                {
                    pending.Push((next.Key, true));
                    foreach (string child in item.Children) pending.Push((child, false));
                    continue;
                }
                AnalysisChange? change = item.NodeId is { } id ? changes.GetValueOrDefault(id, AnalysisChange.Unknown) : null;
                if (item.Kind == AnalysisKind.Directory || item.Synthetic)
                {
                    change = null;
                    foreach (ulong member in item.Members) Merge(changes.GetValueOrDefault(member, AnalysisChange.Unknown));
                    foreach (string child in item.Children) Merge(items[child].ComparisonChange ?? AnalysisChange.Unknown);
                    if (item.NodeId is { } directory && changes.TryGetValue(directory, out var direct)) Merge(direct);
                }
                items[next.Key] = item with { ComparisonChange = change ?? AnalysisChange.Unknown };
                void Merge(AnalysisChange candidate) => change = change is null || change == candidate ? candidate : AnalysisChange.Unknown;
            }
            var result = new AnalysisComparisonChart(snapshot, reader, tree, previous, byNode.ToImmutable(), items.ToImmutable());
            snapshot = null; reader = null; return result;
        }
        catch { Interlocked.Exchange(ref s_active, 0); throw; }
        finally { reader?.Dispose(); snapshot?.Dispose(); }
    }
    private AnalysisTree Tree => _tree ?? throw new ObjectDisposedException(nameof(AnalysisComparisonChart));
    public string ParentOf(string key) => Tree.ParentOf(key);
    public string NameOf(string key) => Tree.NameOf(key);
    public UInt128 ScopeValue(string key) => Tree.ScopeValue(key);
    public ImmutableArray<AnalysisTreeItem> ChildrenOf(string key) => Tree.ChildrenOf(key).Select(item => Items[item.Key]).ToImmutableArray();
    public bool CanExplore(string key) => Tree.CanExplore(key);
    public string? FocusKey(AnalysisComparisonRow row)
    {
        _ = Tree;
        var entity = Previous ? row.Previous : row.Current;
        if (entity is null) return null;
        foreach (var item in Items.Values)
            if (item.Family == "shared" && item.Members.Any(entity.NodeIds.Contains)) return item.Key;
        return entity.NodeIds.Select(AnalysisTree.Key).FirstOrDefault(Items.ContainsKey);
    }
    public AnalysisComparisonRow? RowFor(string key)
    {
        var tree = Tree;
        if (!Items.TryGetValue(key, out var item)) return null;
        int found = -1;
        if (item.NodeId is { } id && _rowByNode.TryGetValue(id, out int direct)) found = direct;
        foreach (ulong member in item.Members)
            if (_rowByNode.TryGetValue(member, out int index))
            { if (index < 0 || found >= 0 && found != index) return null; found = index; }
        return found < 0 ? null : _reader!.Rows[found];
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _tree, null) is null) return;
        Items = ImmutableDictionary<string, AnalysisTreeItem>.Empty; _rowByNode = ImmutableDictionary<ulong, int>.Empty;
        _reader?.Dispose(); _reader = null; _snapshot?.Dispose(); _snapshot = null;
        Interlocked.Exchange(ref s_active, 0);
    }
}
