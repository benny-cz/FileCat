namespace FileCat.Core.Analysis;

/// <summary>Runs candidate previews against an independently retained in-memory observation revision.</summary>
public static class AnalysisMemoryRecipes
{
    private const long Budget = AnalysisLimits.PendingBytes * 2L;
    private static long s_reserved;

    public static async Task<AnalysisRecipePreview> PreviewAsync(AnalysisSnapshotLease source, AnalysisRecipeRequest request,
        AnalysisMetric metric, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellation.ThrowIfCancellationRequested();
        using var retained = source.Retain();
        using var admission = AnalysisExplorerAdmission.Acquire();
        var snapshot = retained.Snapshot;
        var preview = new AnalysisRecipePreviews(request, snapshot.Roots, snapshot.Identity, metric);
        bool context = request.Recipe is AnalysisMetadataRecipe.GeneratedBuildOutput or AnalysisMetadataRecipe.ApplicationCache;
        long cost = 256 * 1024L + snapshot.Objects.Length * 160L +
            (context ? snapshot.Nodes.Length * 256L : 0) + (preview.NeedsRankedAllocation ? snapshot.Facts.Length * 160L : 0);
        Reserve(cost);
        try
        {
            return await Task.Run(() => Evaluate(snapshot, preview, context, cancellation), cancellation).ConfigureAwait(false);
        }
        finally { Interlocked.Add(ref s_reserved, -cost); }
    }

    private static AnalysisRecipePreview Evaluate(AnalysisSnapshot snapshot, AnalysisRecipePreviews preview, bool context, CancellationToken token)
    {
        var byId = new Dictionary<ulong, AnalysisNode>();
        var children = new Dictionary<(ulong ParentId, string Name), AnalysisNode?>();
        if (context)
            foreach (var node in snapshot.Nodes)
            {
                token.ThrowIfCancellationRequested();
                byId.Add(node.Id, node);
                if (node.ParentId != 0)
                {
                    var key = (node.ParentId, node.Name);
                    if (!children.TryAdd(key, node)) children[key] = null;
                }
            }
        var objects = new Dictionary<string, AnalysisObject>(StringComparer.Ordinal);
        foreach (var obj in snapshot.Objects)
        {
            token.ThrowIfCancellationRequested();
            objects.Add(obj.Id, obj);
        }
        var facts = new Dictionary<string, List<StorageFact>>(StringComparer.Ordinal);
        if (preview.NeedsRankedAllocation)
            foreach (var item in snapshot.Facts)
            {
                token.ThrowIfCancellationRequested();
                if (item.ObjectId is not { } owner) continue;
                if (!facts.TryGetValue(owner, out var components)) facts.Add(owner, components = []);
                if (components.Count >= AnalysisLimits.Components) throw new InvalidDataException("Too many recorded storage components.");
                components.Add(item);
            }
        AnalysisNode? ReadPath(ulong id) => byId.GetValueOrDefault(id);
        AnalysisNode? ReadChild(AnalysisNode node, string name) => children.GetValueOrDefault((node.Id, name));
        foreach (var node in snapshot.Nodes)
        {
            token.ThrowIfCancellationRequested();
            if (!preview.Includes(node)) continue;
            var obj = node.ObjectId is { } id ? objects.GetValueOrDefault(id) : null;
            var allocation = obj is not null && facts.TryGetValue(obj.Id, out var components)
                ? AnalysisAggregate.CompatibleAllocation(components) : AnalysisMeasurement.Missing(MeasurementState.Unsupported);
            preview.Add(node, obj, allocation, ReadPath, ReadChild);
        }
        token.ThrowIfCancellationRequested();
        return preview.Finish(snapshot.Identity, snapshot.Coverage);
    }

    private static void Reserve(long cost)
    {
        while (true)
        {
            long current = Volatile.Read(ref s_reserved);
            if (cost > Budget - current) throw new InvalidOperationException("This candidate preview exceeds the available memory budget. Try an indexed analysis or a smaller captured location.");
            if (Interlocked.CompareExchange(ref s_reserved, current + cost, current) == current) return;
        }
    }
}
