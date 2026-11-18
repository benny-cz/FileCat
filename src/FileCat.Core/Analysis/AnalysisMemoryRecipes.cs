namespace FileCat.Core.Analysis;

/// <summary>Runs candidate previews against an independently retained in-memory observation revision.</summary>
public static class AnalysisMemoryRecipes
{
    private const long Budget = AnalysisLimits.PendingBytes * 2L;
    private static long s_reserved;

    public static Task<AnalysisRecipePreview> PreviewAsync(AnalysisSnapshotLease source, AnalysisRecipeRequest request,
        AnalysisMetric metric, CancellationToken cancellation = default) =>
        PreviewCoreAsync(source, snapshot => new(request, snapshot.Roots, snapshot.Identity, metric), cancellation);

    internal static Task<AnalysisRecipePreview> PreviewSavedAsync(AnalysisSnapshotLease source, AnalysisSavedRecipe recipe,
        CancellationToken cancellation) => PreviewCoreAsync(source,
        snapshot => new(recipe, snapshot.Roots, snapshot.Identity, snapshot.Freshness), cancellation);

    private static async Task<AnalysisRecipePreview> PreviewCoreAsync(AnalysisSnapshotLease source,
        Func<AnalysisSnapshot, AnalysisRecipePreviews> prepare, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellation.ThrowIfCancellationRequested();
        using var retained = source.Retain();
        using var admission = AnalysisExplorerAdmission.Acquire();
        var snapshot = retained.Snapshot;
        var preview = await Task.Run(() => prepare(snapshot), cancellation).ConfigureAwait(false);
        if (preview.Recipe == AnalysisMetadataRecipe.LargeExtraData)
        {
            preview.ExplainExtraDataUnavailable();
            return preview.Finish(snapshot.Identity, snapshot.Coverage);
        }
        if (preview.Recipe == AnalysisMetadataRecipe.UnmeasuredLocations)
            return await Task.Run(() => ReadIssues(snapshot, preview, cancellation), cancellation).ConfigureAwait(false);
        bool ancestry = preview.NeedsContextMarkers || preview.NeedsRelativePath;
        long cost = 256 * 1024L + snapshot.Objects.Length * 160L +
            (ancestry ? snapshot.Nodes.Length * 256L : 0) + (preview.NeedsAllocation ? snapshot.Facts.Length * 160L : 0) +
            (preview.Recipe == AnalysisMetadataRecipe.ManySmallFiles ? snapshot.Nodes.Length * 128L : 0);
        Reserve(cost);
        try
        {
            return await Task.Run(() => Evaluate(snapshot, preview, ancestry, cancellation), cancellation).ConfigureAwait(false);
        }
        finally { Interlocked.Add(ref s_reserved, -cost); }
    }

    private static AnalysisRecipePreview Evaluate(AnalysisSnapshot snapshot, AnalysisRecipePreviews preview, bool ancestry, CancellationToken token)
    {
        var byId = new Dictionary<ulong, AnalysisNode>();
        var children = new Dictionary<(ulong ParentId, string Name), AnalysisNode?>();
        if (ancestry)
            foreach (var node in snapshot.Nodes)
            {
                token.ThrowIfCancellationRequested();
                byId.Add(node.Id, node);
                if (preview.NeedsContextMarkers && node.ParentId != 0)
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
        if (preview.NeedsAllocation)
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
        if (preview.Recipe == AnalysisMetadataRecipe.ManySmallFiles)
            return ReadSmallFolders(snapshot, preview, objects, facts, token);
        foreach (var node in snapshot.Nodes)
        {
            token.ThrowIfCancellationRequested();
            if (!preview.Includes(node)) continue;
            var obj = node.ObjectId is { } id ? objects.GetValueOrDefault(id) : null;
            var allocation = obj is not null && facts.TryGetValue(obj.Id, out var components)
                ? preview.Recipe == AnalysisMetadataRecipe.AllocationDifference
                    ? AnalysisRecipePreviews.PrimaryAllocation(components)?.Measurement ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported)
                    : AnalysisAggregate.CompatibleAllocation(components)
                : AnalysisMeasurement.Missing(MeasurementState.Unsupported);
            preview.Add(node, obj, allocation, ReadPath, ReadChild);
        }
        token.ThrowIfCancellationRequested();
        return preview.Finish(snapshot.Identity, snapshot.Coverage);
    }

    private static AnalysisRecipePreview ReadIssues(AnalysisSnapshot snapshot, AnalysisRecipePreviews preview, CancellationToken token)
    {
        var needed = snapshot.Issues.Where(AnalysisRecipePreviews.QualifyingIssue).Where(issue => issue.NodeId.HasValue)
            .Select(issue => issue.NodeId!.Value).ToHashSet();
        var paths = new Dictionary<ulong, AnalysisNode>();
        if (needed.Count != 0)
            foreach (var node in snapshot.Nodes)
            {
                token.ThrowIfCancellationRequested();
                if (needed.Contains(node.Id)) paths.Add(node.Id, node);
                if (paths.Count == needed.Count) break;
            }
        ulong row = 0;
        foreach (var issue in snapshot.Issues)
        {
            token.ThrowIfCancellationRequested();
            preview.AddIssue(issue, issue.NodeId is { } id ? paths.GetValueOrDefault(id) : null, ++row);
        }
        preview.CompleteIssues((ulong)snapshot.Issues.Length, snapshot.IssueTotals.Values.Aggregate(0UL, (total, count) => checked(total + count)));
        return preview.Finish(snapshot.Identity, snapshot.Coverage);
    }

    private static AnalysisRecipePreview ReadSmallFolders(AnalysisSnapshot snapshot, AnalysisRecipePreviews preview,
        Dictionary<string, AnalysisObject> objects, Dictionary<string, List<StorageFact>> facts, CancellationToken token)
    {
        var folders = new Dictionary<ulong, AnalysisRecipePreviews.SmallFolder>();
        foreach (var path in snapshot.Nodes)
        {
            token.ThrowIfCancellationRequested();
            if (path.ParentId == 0 || !preview.IncludesRoot(path.RootId) || !AnalysisRecipePreviews.CountsSmallPaths(path)) continue;
            var obj = path.ObjectId is { } id ? objects.GetValueOrDefault(id) : null;
            var primary = AnalysisRecipePreviews.SmallSize(path, obj) && AnalysisRecipePreviews.SingleLink(obj) &&
                facts.TryGetValue(obj!.Id, out var components) ? AnalysisRecipePreviews.PrimaryAllocation(components) : null;
            var summary = folders.GetValueOrDefault(path.ParentId);
            summary.Add(path, obj, primary);
            folders[path.ParentId] = summary;
        }
        foreach (var path in snapshot.Nodes)
        {
            token.ThrowIfCancellationRequested();
            if (path is { Kind: AnalysisKind.Directory, IncludedByPolicy: true, Cloud: not true, MetadataState: MeasurementState.Known } &&
                preview.IncludesRoot(path.RootId) && folders.TryGetValue(path.Id, out var summary)) summary.Offer(preview, path);
        }
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
