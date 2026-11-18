using System.Collections.Immutable;
using System.Diagnostics;

namespace FileCat.Core.Analysis;

public sealed record AnalysisRecipeCandidate(ulong PathId, string RootId, string DisplayName,
    AnalysisMeasurement RecordedBytes, AnalysisRecipeFinding Finding)
{
    public ulong? RecordedFilePaths { get; init; }
}

public sealed record AnalysisRecipePreview(SessionIdentity Identity, AnalysisRecipeDefinition Definition, ImmutableArray<string> RootIds,
    AnalysisMetric Metric, NamespaceCoverage Coverage, UInt128? MinimumBytes, int? MinimumAgeDays, ulong Candidates, ulong Unknown,
    ulong Excluded, ImmutableArray<AnalysisRecipeCandidate> Rows)
{
    public string? CoverageNote { get; init; }
    internal bool SupportsStaging => Definition.Recipe is AnalysisMetadataRecipe.LargestFiles or AnalysisMetadataRecipe.OldLargeFiles
        or AnalysisMetadataRecipe.DownloadsInstallers or AnalysisMetadataRecipe.GeneratedBuildOutput or AnalysisMetadataRecipe.ApplicationCache
        or AnalysisMetadataRecipe.CustomFilter or AnalysisMetadataRecipe.AllocationDifference;
    internal bool CanInclude(AnalysisRecipeCandidate row) => SupportsStaging && row.PathId != 0 &&
        row.Finding.Match == AnalysisRecipeMatch.Candidate && RootIds.Contains(row.RootId);
}

internal sealed partial class AnalysisRecipePreviews
{
    internal const int MaximumRows = 128;
    private readonly AnalysisRecipes? _rules;
    private readonly AnalysisRecipeDefinition _definition;
    private readonly ImmutableArray<string> _rootIds;
    private readonly PriorityQueue<AnalysisRecipeCandidate, (bool Known, UInt128 Bytes, ulong ReversePathId)> _rows = new();
    private ulong _candidates;
    private ulong _unknown;
    private ulong _excluded;

    internal AnalysisRecipePreviews(AnalysisRecipeRequest request, ImmutableArray<RootBinding> roots, SessionIdentity identity, AnalysisMetric metric)
    {
        _rules = new(request, roots, identity.ObservedUtc);
        _definition = _rules.Definition;
        _rootIds = request.RootIds;
        if (!Enum.IsDefined(metric) || request.Recipe == AnalysisMetadataRecipe.LargestFiles && metric == AnalysisMetric.Files)
            throw new ArgumentException("Choose a byte measure for this preview.", nameof(metric));
        Metric = request.Recipe is AnalysisMetadataRecipe.LargestFiles or AnalysisMetadataRecipe.LargestFolders ? metric : AnalysisMetric.FileSize;
    }

    internal AnalysisMetric Metric { get; }
    internal AnalysisMetadataRecipe Recipe => _definition.Recipe;
    internal bool NeedsAllocation => Metric == AnalysisMetric.OnDisk || Recipe is AnalysisMetadataRecipe.AllocationDifference or AnalysisMetadataRecipe.ManySmallFiles ||
        (_saved is not null && (_saved.Needs & AnalysisFilterNeeds.Allocation) != 0);
    internal bool NeedsContextMarkers => _definition.Recipe is AnalysisMetadataRecipe.GeneratedBuildOutput or AnalysisMetadataRecipe.ApplicationCache;
    internal bool NeedsRelativePath => _saved is not null && (_saved.Needs & AnalysisFilterNeeds.Path) != 0;

    internal bool IncludesRoot(string id) => _rules?.IncludesRoot(id) ?? _saved!.Includes(id);
    internal bool Includes(AnalysisNode node) => node.Kind == AnalysisKind.File && IncludesRoot(node.RootId);

    internal void Add(AnalysisNode node, AnalysisObject? recordedObject, AnalysisMeasurement allocation,
        Func<ulong, AnalysisNode?> readPath, Func<AnalysisNode, string, AnalysisNode?> readChild)
    {
        if (Recipe == AnalysisMetadataRecipe.AllocationDifference)
        {
            AddDifference(node, recordedObject, allocation);
            return;
        }
        AnalysisMeasurement measured = Metric switch
        {
            AnalysisMetric.OnDisk => allocation,
            AnalysisMetric.UniqueFileSize when recordedObject?.VerifiedIdentity is null => AnalysisMeasurement.Missing(MeasurementState.Unsupported),
            _ => recordedObject?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported)
        };
        var finding = _rules?.Evaluate(node, recordedObject, measured, readPath, readChild) ??
            EvaluateSaved(node, recordedObject, measured, allocation, readPath);
        switch (finding.Match)
        {
            case AnalysisRecipeMatch.Candidate:
                AddPathRow(node, measured, finding, measured.IsKnown, measured.Value.GetValueOrDefault());
                break;
            case AnalysisRecipeMatch.Unknown:
                _unknown++;
                break;
            case AnalysisRecipeMatch.Excluded:
                _excluded++;
                break;
        }
    }

    private void AddPathRow(AnalysisNode node, AnalysisMeasurement measurement, AnalysisRecipeFinding finding, bool known, UInt128 order)
    {
        var priority = (known, order, ulong.MaxValue - node.Id);
        if (!AdmitRow(priority)) return;
        Enqueue(new(node.Id, node.RootId, AnalysisNames.DisplayName(node, 256), measurement, finding), priority);
    }

    private bool AdmitRow((bool Known, UInt128 Bytes, ulong ReversePathId) priority)
    {
        _candidates++;
        return _rows.Count < MaximumRows || !_rows.TryPeek(out _, out var lowest) || priority.CompareTo(lowest) > 0;
    }

    private void Enqueue(AnalysisRecipeCandidate row, (bool Known, UInt128 Bytes, ulong ReversePathId) priority)
    {
        if (_rows.Count == MaximumRows) _rows.DequeueEnqueue(row, priority);
        else _rows.Enqueue(row, priority);
    }

    internal AnalysisRecipePreview Finish(SessionIdentity identity, NamespaceCoverage coverage) => new AnalysisRecipePreview(identity, _definition,
        _rootIds, Metric, coverage, _rules?.MinimumBytes, _rules?.MinimumAgeDays, _candidates, _unknown, _excluded,
        [.. _rows.UnorderedItems.OrderByDescending(item => item.Priority).Select(item => item.Element)])
        { CoverageNote = _coverageNote };
}

public sealed partial class AnalysisDatasetLease
{
    /// <summary>Runs a bounded, read-only top-candidate query against this captured revision.</summary>
    public Task<AnalysisRecipePreview> PreviewRecipeAsync(AnalysisRecipeRequest request, AnalysisMetric metric, CancellationToken cancellation = default) =>
        PreviewRecipeCoreAsync(() => new(request, Descriptor.Roots, Descriptor.Identity, metric), cancellation);

    internal Task<AnalysisRecipePreview> PreviewSavedRecipeAsync(AnalysisSavedRecipe recipe, CancellationToken cancellation) =>
        PreviewRecipeCoreAsync(() => new(recipe, Descriptor.Roots, Descriptor.Identity, Descriptor.Freshness), cancellation);

    private async Task<AnalysisRecipePreview> PreviewRecipeCoreAsync(Func<AnalysisRecipePreviews> prepare, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var admission = AnalysisExplorerAdmission.Acquire();
        var preview = await AnalysisIo.Run(_io, "analysis-filter-calendar", _ => prepare(), cancellation).ConfigureAwait(false);
        if (preview.Recipe == AnalysisMetadataRecipe.LargestFolders)
        {
            await using var hierarchy = await BuildHierarchyAsync(preview.Metric, cancellation).ConfigureAwait(false);
            return await hierarchy.PreviewLargestFoldersAsync(preview, cancellation).ConfigureAwait(false);
        }
        return await _owned.UseAsync(async reader =>
        {
            if (preview.Recipe is AnalysisMetadataRecipe.UnmeasuredLocations or AnalysisMetadataRecipe.ManySmallFiles or AnalysisMetadataRecipe.LargeExtraData)
            {
                await ReadRecordedInsightAsync(reader, preview, cancellation).ConfigureAwait(false);
                return preview.Finish(Descriptor.Identity, Descriptor.Coverage);
            }
            ulong next = 1;
            while (next <= Descriptor.Paths)
                await AnalysisIo.Run(_io, _device, token =>
                {
                    var clock = Stopwatch.StartNew();
                    long bytes = 0;
                    int records = 0;
                    int rootOrdinal = 0;
                    while (next <= Descriptor.Paths && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes && clock.ElapsedMilliseconds < 20)
                    {
                        token.ThrowIfCancellationRequested();
                        var path = reader.ReadPathRowRecord(next++, token);
                        var node = path.Node;
                        bytes += AnalysisIndexedFilter.Cost(node);
                        if (!preview.Includes(node)) continue;
                        rootOrdinal = path.RootIndex;
                        var obj = node is { MetadataState: MeasurementState.Known, IncludedByPolicy: true, Cloud: not true, ObjectId: { } id }
                            ? ReadRequiredObservedObject(reader, id, token) : null;
                        if (obj is not null) bytes += 1024L + (obj.VerifiedIdentity?.Length ?? 0) * 2L + (obj.VersionEvidence?.Length ?? 0) * 2L;
                        AnalysisMeasurement allocation = preview.NeedsAllocation && obj is not null
                            ? ReadRecipeAllocation(reader, obj.Id, ref bytes, token, preview.Recipe == AnalysisMetadataRecipe.AllocationDifference)
                            : AnalysisMeasurement.Missing(MeasurementState.Unsupported);
                        preview.Add(node, obj, allocation, ReadPath, FindChild);
                    }
                    return true;

                    AnalysisNode? ReadPath(ulong id)
                    {
                        token.ThrowIfCancellationRequested();
                        var path = reader.ReadPath(id, token);
                        bytes += AnalysisIndexedFilter.Cost(path);
                        return path;
                    }

                    AnalysisNode? FindChild(AnalysisNode parent, string name)
                    {
                        AnalysisNode? match = null;
                        foreach (string encoding in RecipeNameEncodings)
                        {
                            token.ThrowIfCancellationRequested();
                            var keyNode = new AnalysisNode(0, parent.RootId, parent.Id, name, AnalysisKind.File) { NameEncoding = encoding };
                            var key = AnalysisGraphKeys.Child(new(keyNode, rootOrdinal, 0));
                            bytes += key.Length + 64;
                            var range = reader.Find(AnalysisIndexedGraphRevision.Children, key, cancellation: token);
                            if (range.Count == 0) continue;
                            if (range.Count != 1 || match is not null) return null;
                            match = reader.ReadPathRow(reader.ReadIndex(AnalysisIndexedGraphRevision.Children, range.First, token).Value, token);
                            bytes += AnalysisIndexedFilter.Cost(match);
                        }
                        return match;
                    }
                }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            return preview.Finish(Descriptor.Identity, Descriptor.Coverage);
        }).ConfigureAwait(false);
    }

    private static readonly ImmutableArray<string> RecipeNameEncodings = ["utf8", "utf16le", "unix-bytes"];

    private static AnalysisObject ReadRequiredObservedObject(AnalysisIndexedGraphReader reader, string id, CancellationToken token)
    {
        var range = reader.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: token);
        if (range.Count != 1) throw new InvalidDataException("The recorded object index is ambiguous.");
        return reader.ReadObject(reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value, token);
    }

    private static AnalysisMeasurement ReadRecipeAllocation(AnalysisIndexedGraphReader reader, string id, ref long bytes, CancellationToken token, bool primaryOnly)
    {
        var facts = ReadRecipeComponents(reader, id, ref bytes, token);
        if (facts is null) return AnalysisMeasurement.Missing(MeasurementState.Unsupported);
        return primaryOnly ? AnalysisRecipePreviews.PrimaryAllocation(facts)?.Measurement ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported)
            : AnalysisAggregate.CompatibleAllocation(facts);
    }

    private static List<StorageFact>? ReadRecipeComponents(AnalysisIndexedGraphReader reader, string id, ref long bytes, CancellationToken token)
    {
        var range = reader.Find(AnalysisIndexedGraphRevision.FactOwners, AnalysisGraphKeys.FactOwner(id, null), cancellation: token);
        var maximum = Math.Min(range.Count, AnalysisLimits.Components);
        if (maximum != range.Count) throw new InvalidDataException("Too many recorded storage components.");
        var facts = new List<StorageFact>((int)maximum);
        long factBytes = 0;
        for (ulong i = 0; i < maximum; i++)
        {
            token.ThrowIfCancellationRequested();
            var fact = reader.ReadFact(reader.ReadIndex(AnalysisIndexedGraphRevision.FactOwners, range.First + i, token).Value, token);
            long cost = FactBytes(fact);
            bytes += cost;
            factBytes += cost;
            if (factBytes > AnalysisLimits.BatchBytes) return null;
            facts.Add(fact);
        }
        return facts;
    }
}
