using System.Collections.Immutable;
using System.Diagnostics;

namespace FileCat.Core.Analysis;

public sealed record AnalysisRecipeCandidate(ulong PathId, string RootId, string DisplayName,
    AnalysisMeasurement RecordedBytes, AnalysisRecipeFinding Finding);

public sealed record AnalysisRecipePreview(SessionIdentity Identity, AnalysisRecipeDefinition Definition, ImmutableArray<string> RootIds,
    AnalysisMetric Metric, NamespaceCoverage Coverage, UInt128? MinimumBytes, int? MinimumAgeDays, ulong Candidates, ulong Unknown,
    ulong Excluded, ImmutableArray<AnalysisRecipeCandidate> Rows);

internal sealed class AnalysisRecipePreviews
{
    internal const int MaximumRows = 128;
    private readonly AnalysisRecipes _rules;
    private readonly AnalysisRecipeRequest _request;
    private readonly PriorityQueue<AnalysisRecipeCandidate, (bool Known, UInt128 Bytes, ulong ReversePathId)> _rows = new();
    private ulong _candidates;
    private ulong _unknown;
    private ulong _excluded;

    internal AnalysisRecipePreviews(AnalysisRecipeRequest request, ImmutableArray<RootBinding> roots, SessionIdentity identity, AnalysisMetric metric)
    {
        _request = request;
        _rules = new(request, roots, identity.ObservedUtc);
        if (!Enum.IsDefined(metric) || request.Recipe == AnalysisMetadataRecipe.LargestFiles && metric == AnalysisMetric.Files)
            throw new ArgumentException("Choose a byte measure for this preview.", nameof(metric));
        Metric = request.Recipe == AnalysisMetadataRecipe.LargestFiles ? metric : AnalysisMetric.FileSize;
    }

    internal AnalysisMetric Metric { get; }
    internal bool NeedsRankedAllocation => Metric == AnalysisMetric.OnDisk;

    internal bool Includes(AnalysisNode node) => node.Kind == AnalysisKind.File && _rules.IncludesRoot(node.RootId);

    internal void Add(AnalysisNode node, AnalysisObject? recordedObject, AnalysisMeasurement allocation,
        Func<ulong, AnalysisNode?> readPath, Func<AnalysisNode, string, AnalysisNode?> readChild)
    {
        AnalysisMeasurement measured = Metric switch
        {
            AnalysisMetric.OnDisk => allocation,
            AnalysisMetric.UniqueFileSize when recordedObject?.VerifiedIdentity is null => AnalysisMeasurement.Missing(MeasurementState.Unsupported),
            _ => recordedObject?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported)
        };
        var finding = _rules.Evaluate(node, recordedObject, measured, readPath, readChild);
        switch (finding.Match)
        {
            case AnalysisRecipeMatch.Candidate:
                _candidates++;
                var priority = (measured.IsKnown, measured.Value.GetValueOrDefault(), ulong.MaxValue - node.Id);
                if (_rows.Count == MaximumRows && _rows.TryPeek(out _, out var lowest) && priority.CompareTo(lowest) <= 0) break;
                var row = new AnalysisRecipeCandidate(node.Id, node.RootId, AnalysisNames.DisplayName(node, 256), measured, finding);
                if (_rows.Count == MaximumRows) _rows.DequeueEnqueue(row, priority);
                else _rows.Enqueue(row, priority);
                break;
            case AnalysisRecipeMatch.Unknown:
                _unknown++;
                break;
            case AnalysisRecipeMatch.Excluded:
                _excluded++;
                break;
        }
    }

    internal AnalysisRecipePreview Finish(SessionIdentity identity, NamespaceCoverage coverage) => new(identity, _rules.Definition,
        _request.RootIds, Metric, coverage, _rules.MinimumBytes, _rules.MinimumAgeDays, _candidates, _unknown, _excluded,
        [.. _rows.UnorderedItems.OrderByDescending(item => item.Priority).Select(item => item.Element)]);
}

public sealed partial class AnalysisDatasetLease
{
    /// <summary>Runs a bounded, read-only top-candidate query against this captured revision.</summary>
    public async Task<AnalysisRecipePreview> PreviewRecipeAsync(AnalysisRecipeRequest request, AnalysisMetric metric,
        CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        using var admission = AnalysisExplorerAdmission.Acquire();
        var preview = new AnalysisRecipePreviews(request, Descriptor.Roots, Descriptor.Identity, metric);
        return await _owned.UseAsync(async reader =>
        {
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
                            ? ReadRecipeObject(reader, id, token) : null;
                        if (obj is not null) bytes += 1024L + (obj.VerifiedIdentity?.Length ?? 0) * 2L + (obj.VersionEvidence?.Length ?? 0) * 2L;
                        AnalysisMeasurement allocation = preview.NeedsRankedAllocation && obj is not null
                            ? ReadRecipeAllocation(reader, obj.Id, ref bytes, token) : AnalysisMeasurement.Missing(MeasurementState.Unsupported);
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

    private static AnalysisObject ReadRecipeObject(AnalysisIndexedGraphReader reader, string id, CancellationToken token)
    {
        var range = reader.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: token);
        if (range.Count != 1) throw new InvalidDataException("The recorded object index is ambiguous.");
        return reader.ReadObject(reader.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value, token);
    }

    private static AnalysisMeasurement ReadRecipeAllocation(AnalysisIndexedGraphReader reader, string id, ref long bytes, CancellationToken token)
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
            if (factBytes > AnalysisLimits.BatchBytes) return AnalysisMeasurement.Missing(MeasurementState.Unsupported);
            facts.Add(fact);
        }
        return AnalysisAggregate.CompatibleAllocation(facts);
    }
}
