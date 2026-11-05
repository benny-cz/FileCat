namespace FileCat.Core.Analysis;

/// <summary>Data-only membership for covered bindings. No locator is opened and no live source is produced.</summary>
public sealed class AnalysisRootScopeIndex
{
    private sealed record Scope(string? ActiveRoot, int? Node, bool Known);
    private readonly Dictionary<string, Scope> _scopes = new(StringComparer.Ordinal);
    private readonly Dictionary<ulong, int> _indices = [];
    private readonly AnalysisNode[] _nodes;
    private readonly int[] _enter, _exit;
    public AnalysisRootScopeIndex(AnalysisSnapshot snapshot, CancellationToken cancellation = default)
    {
        _nodes = snapshot.Nodes.ToArray(); int count = _nodes.Length;
        _enter = new int[count]; _exit = new int[count];
        var parent = new int[count]; var first = new int[count]; var next = new int[count];
        Array.Fill(first, -1); Array.Fill(next, -1); Array.Fill(parent, -1);
        var byIdentity = new Dictionary<(string Root, string Identity, AnalysisKind Kind), List<int>>();
        var objects = snapshot.Objects.ToDictionary(o => o.Id, StringComparer.Ordinal);
        var rootNodes = new Dictionary<string, int>(StringComparer.Ordinal); var depth = new int[count];
        for (int i = 0; i < count; i++)
        {
            if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
            var node = _nodes[i];
            if (!_indices.TryAdd(node.Id, i)) throw new InvalidDataException("Duplicate observation id.");
            if (node.ParentId == 0) { if (!rootNodes.TryAdd(node.RootId, i)) throw new InvalidDataException("Duplicate root observation."); }
            else
            {
                if (!_indices.TryGetValue(node.ParentId, out int p) || p == i || _nodes[p].RootId != node.RootId || _nodes[p].Kind != AnalysisKind.Directory)
                    throw new InvalidDataException("Invalid observation ancestry.");
                parent[i] = p; next[i] = first[p]; first[p] = i; depth[i] = depth[p] + 1;
                if (depth[i] > AnalysisLimits.Depth) throw new InvalidDataException("Observation ancestry exceeds its limit.");
            }
            string? identity = node.ObservedIdentity ?? (node.ObjectId is { } id ? objects.GetValueOrDefault(id)?.VerifiedIdentity : null);
            if (identity is not null)
            {
                var key = (node.RootId, identity, node.Kind);
                if (!byIdentity.TryGetValue(key, out var members)) byIdentity[key] = members = [];
                members.Add(i);
            }
        }
        int clock = 0;
        foreach (int root in rootNodes.Values)
        {
            int current = root;
            while (current >= 0)
            {
                if ((clock & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                _enter[current] = clock++;
                if (first[current] >= 0) { current = first[current]; continue; }
                while (current >= 0)
                {
                    _exit[current] = clock;
                    if (next[current] >= 0) { current = next[current]; break; }
                    current = parent[current];
                }
            }
        }
        foreach (var root in snapshot.Roots)
        {
            if (root.Disposition == AnalysisRootDisposition.Active) { _scopes[root.Id] = new(root.Id, null, true); continue; }
            _scopes[root.Id] = new(null, null, false);
            if (root.Disposition != AnalysisRootDisposition.Covered || root.CoveredByRootId is not { } covering ||
                root.CoveredRelativePath is not { } relative || root.ObservedObjectIdentity is not { } identity ||
                !byIdentity.TryGetValue((covering, identity, root.Kind), out var candidates)) continue;
            string[] parts = relative.Length == 0 ? [] : relative.Split('/');
            int? found = Find(StringComparison.Ordinal);
            // A uniquely matching observed alias can account for source-case spelling. Ambiguity stays unknown.
            found ??= Find(StringComparison.OrdinalIgnoreCase);
            if (found is { } index) _scopes[root.Id] = new(covering, index, true);
            int? Find(StringComparison comparison)
            {
                int? match = null; int checkedCount = 0;
                foreach (int candidate in candidates)
                {
                    if ((checkedCount++ & 4095) == 0) cancellation.ThrowIfCancellationRequested();
                    int current = candidate, part = parts.Length - 1;
                    while (part >= 0 && current >= 0 && parent[current] >= 0 && _nodes[current].Name.Equals(parts[part], comparison)) { part--; current = parent[current]; }
                    if (part >= 0 || current < 0 || parent[current] >= 0) continue;
                    if (match.HasValue) return null;
                    match = candidate;
                }
                return match;
            }
        }
    }
    public AnalysisTruth IsMember(string rootId, AnalysisNode node)
    {
        if (!_scopes.TryGetValue(rootId, out var scope) || !scope.Known || !_indices.TryGetValue(node.Id, out int index)) return AnalysisTruth.Unknown;
        if (node.RootId != scope.ActiveRoot) return AnalysisTruth.False;
        return scope.Node is not { } ancestor || _enter[index] >= _enter[ancestor] && _enter[index] < _exit[ancestor] ? AnalysisTruth.True : AnalysisTruth.False;
    }
}
