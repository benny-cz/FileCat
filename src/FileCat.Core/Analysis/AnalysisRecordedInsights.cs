using System.Diagnostics;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisRecipePreviews
{
    private static readonly AnalysisMeasurement Unmeasured = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
    private const ulong SmallFileBytes = 64 * 1024, SmallFileFolderMinimum = 32;
    private ulong _unlocatedIssues;
    private string? _coverageNote;

    private void AddDifference(AnalysisNode node, AnalysisObject? obj, AnalysisMeasurement allocation)
    {
        if (!node.IncludedByPolicy || node.MetadataState == MeasurementState.Excluded || node.Cloud == true)
        { _excluded++; return; }
        if (node.MetadataState != MeasurementState.Known ||
            obj?.LogicalLength.Value is not { } logical || allocation.Value is not { } assigned)
        { _unknown++; return; }
        if (logical == assigned) return;
        var delta = logical > assigned ? logical - assigned : assigned - logical;
        var priority = (true, delta, ulong.MaxValue - node.Id);
        if (!AdmitRow(priority)) return;
        string direction = logical > assigned ? "logical size is larger" : "reported allocation is larger";
        var finding = new AnalysisRecipeFinding(AnalysisRecipeMatch.Candidate,
            $"Logical size {logical:N0} B · reported primary allocation {assigned:N0} B · {direction}.");
        Enqueue(new(node.Id, node.RootId, AnalysisNames.DisplayName(node, 256), AnalysisMeasurement.Known(delta), finding), priority);
    }

    internal static StorageFact? PrimaryAllocation(List<StorageFact> facts)
    {
        StorageFact? primary = null;
        foreach (var fact in facts)
        {
            if (fact.ComponentId != "reported-data") continue;
            if (!fact.Includes.IsEmpty || primary is not null &&
                (primary.BasisId != fact.BasisId || primary.Measurement != fact.Measurement || primary.Method != fact.Method))
                return null;
            primary = fact;
        }
        return primary;
    }

    internal static bool CountsSmallPaths(AnalysisNode path) => path is
        { Kind: AnalysisKind.File, Cloud: not true, IncludedByPolicy: true, MetadataState: not MeasurementState.Excluded };

    internal static bool SmallSize(AnalysisNode path, AnalysisObject? obj) =>
        path.MetadataState == MeasurementState.Known && obj?.LogicalLength.Value is { } size && size <= SmallFileBytes;

    internal static bool SingleLink(AnalysisObject? obj) => obj is { VerifiedIdentity.Length: > 0 } && obj.LinkCount.Value == UInt128.One;

    internal struct SmallFolder
    {
        private ulong _known, _unknownSize, _knownAllocation;
        private UInt128 _allocation;
        private string? _basis;
        private bool _incompatible;

        internal void Add(AnalysisNode path, AnalysisObject? obj, StorageFact? fact)
        {
            if (path.MetadataState != MeasurementState.Known || obj?.LogicalLength.Value is null)
            { _unknownSize++; return; }
            if (!SmallSize(path, obj)) return;
            _known++;
            if (!SingleLink(obj) || fact?.Measurement.Value is not { } amount) return;
            _knownAllocation++;
            if (_basis is not null && _basis != fact.BasisId || amount > UInt128.MaxValue - _allocation)
            { _incompatible = true; return; }
            _basis = fact.BasisId;
            _allocation += amount;
        }

        internal void Offer(AnalysisRecipePreviews preview, AnalysisNode directory)
        {
            if (_known < SmallFileFolderMinimum)
            {
                if (_known + _unknownSize >= SmallFileFolderMinimum) preview._unknown++;
                return;
            }
            string detail = $"{_known:N0} small file paths · {_unknownSize:N0} direct file {(_unknownSize == 1 ? "path" : "paths")} with unknown size. ";
            bool combined = !_incompatible && _knownAllocation != 0;
            detail += combined ? $"Known allocation measured for {_knownAllocation:N0} verified single-link files."
                : _incompatible ? "The recorded allocation cannot be combined." : "Known single-link allocation is unavailable.";
            preview.AddPathRow(directory, combined ? AnalysisMeasurement.Known(_allocation) : Unmeasured,
                new(AnalysisRecipeMatch.Candidate, detail), true, _known);
        }
    }

    internal static bool QualifyingIssue(AnalysisIssue issue) => issue.State is MeasurementState.Failed or
        MeasurementState.Changed or MeasurementState.Unsupported or MeasurementState.Pending;

    internal void AddIssue(AnalysisIssue issue, AnalysisNode? path, ulong row)
    {
        if (!QualifyingIssue(issue)) return;
        if (issue.NodeId.HasValue && path is null || path is not null && issue.RootId is { } owner && path.RootId != owner)
        { _unlocatedIssues++; return; }
        string? root = path?.RootId ?? issue.RootId;
        if (root is null) { _unlocatedIssues++; return; }
        if (!IncludesRoot(root)) return;
        UInt128 order = issue.State switch { MeasurementState.Failed => 4, MeasurementState.Changed => 3, MeasurementState.Unsupported => 2, _ => 1 };
        var priority = (true, order, ulong.MaxValue - row);
        if (!AdmitRow(priority)) return;
        var evidence = new AnalysisRecipeFinding(AnalysisRecipeMatch.Candidate,
            $"{issue.State} observation · issue {AnalysisNames.Display(issue.Code, 160)}.");
        Enqueue(new(path?.Id ?? 0, root, path is null ? "Location observation" : AnalysisNames.DisplayName(path, 256), Unmeasured, evidence), priority);
    }

    internal void CompleteIssues(ulong samples, ulong totalIssues)
    {
        if (totalIssues > samples) _coverageNote = "This capture retains a sample of its issues. More issues may exist in the selected locations.";
        if (_unlocatedIssues != 0)
            _coverageNote = (_coverageNote is null ? "" : _coverageNote + " ") +
                (_unlocatedIssues == 1 ? "1 captured issue record has no verified location and is outside this list." :
                    $"{_unlocatedIssues:N0} captured issue records have no verified location and are outside this list.");
    }

    internal void ExplainExtraDataUnavailable() => _coverageNote =
        "This analyzer has no qualified stream or fork measurement for this capture. Extra-data size is unavailable.";
}

public sealed partial class AnalysisDatasetLease
{
    private async Task ReadRecordedInsightAsync(AnalysisIndexedGraphReader reader, AnalysisRecipePreviews preview, CancellationToken cancel)
    {
        if (preview.Recipe == AnalysisMetadataRecipe.LargeExtraData)
        { preview.ExplainExtraDataUnavailable(); return; }
        if (preview.Recipe == AnalysisMetadataRecipe.UnmeasuredLocations)
        {
            ulong next = 1;
            while (next <= Descriptor.SampledIssues)
                await AnalysisIo.Run(_io, _device, token =>
                {
                    var clock = Stopwatch.StartNew();
                    int records = 0;
                    long bytes = 0;
                    while (next <= Descriptor.SampledIssues && records++ < AnalysisLimits.BatchRecords &&
                        bytes < AnalysisLimits.BatchBytes && clock.ElapsedMilliseconds < 20)
                    {
                        token.ThrowIfCancellationRequested();
                        ulong row = next++;
                        var issue = reader.ReadIssue(row, token);
                        bytes += IssueBytes(issue);
                        if (!AnalysisRecipePreviews.QualifyingIssue(issue)) continue;
                        var path = issue.NodeId is { } id ? reader.ReadPath(id, token) : null;
                        if (path is not null) bytes += AnalysisIndexedFilter.Cost(path);
                        preview.AddIssue(issue, path, row);
                    }
                    return true;
                }, cancel).ConfigureAwait(false);
            preview.CompleteIssues(Descriptor.SampledIssues, Descriptor.Issues);
            return;
        }

        ulong pathAt = 1, childAt = 0, childCount = 0;
        AnalysisNode? directory = null;
        AnalysisRecipePreviews.SmallFolder small = default;
        while (pathAt <= Descriptor.Paths || directory is not null)
            await AnalysisIo.Run(_io, _device, token =>
            {
                var clock = Stopwatch.StartNew();
                int records = 0;
                long bytes = 0;
                while (records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes && clock.ElapsedMilliseconds < 20)
                {
                    token.ThrowIfCancellationRequested();
                    if (directory is not null && childCount == 0)
                    {
                        small.Offer(preview, directory);
                        directory = null;
                    }
                    if (directory is null)
                    {
                        if (pathAt > Descriptor.Paths) break;
                        var path = reader.ReadPathRowRecord(pathAt++, token);
                        bytes += AnalysisIndexedFilter.Cost(path.Node);
                        if (path.Node is not { Kind: AnalysisKind.Directory, IncludedByPolicy: true, Cloud: not true, MetadataState: MeasurementState.Known } ||
                            !preview.IncludesRoot(path.Node.RootId)) continue;
                        var range = reader.Children(path.RootIndex, path.Node.Id, token);
                        (childAt, childCount) = (range.First, range.Count);
                        directory = path.Node;
                        small = default;
                        continue;
                    }
                    var child = reader.ReadPathRow(reader.ReadIndex(AnalysisIndexedGraphRevision.Children, childAt++, token).Value, token);
                    childCount--;
                    bytes += AnalysisIndexedFilter.Cost(child);
                    if (!AnalysisRecipePreviews.CountsSmallPaths(child)) continue;
                    var obj = child.ObjectId is { } id && child.MetadataState == MeasurementState.Known
                        ? ReadRequiredObservedObject(reader, id, token) : null;
                    if (obj is not null) bytes += 1024L + (obj.VerifiedIdentity?.Length ?? 0) * 2L + (obj.VersionEvidence?.Length ?? 0) * 2L;
                    var facts = AnalysisRecipePreviews.SmallSize(child, obj) && AnalysisRecipePreviews.SingleLink(obj)
                        ? ReadRecipeComponents(reader, obj!.Id, ref bytes, token) : null;
                    small.Add(child, obj, facts is null ? null : AnalysisRecipePreviews.PrimaryAllocation(facts));
                }
                return true;
            }, cancel).ConfigureAwait(false);
        cancel.ThrowIfCancellationRequested();
    }
}
