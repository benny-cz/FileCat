using System.Diagnostics;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisRecipePreviews
{
    internal void ExplainIncompatibleFolderTotals() =>
        _coverageNote = "The captured allocation bases are incompatible. Choose File size, Unique file size, or Files to rank these folders.";

    internal bool IncludesFolder(AnalysisNode path) => path.Kind == AnalysisKind.Directory && IncludesRoot(path.RootId);

    internal bool CanRankFolder(AnalysisNode path)
    {
        if (!path.IncludedByPolicy || path.MetadataState == MeasurementState.Excluded)
        {
            _excluded++;
            return false;
        }
        if (path.MetadataState != MeasurementState.Known)
        {
            _unknown++;
            return false;
        }
        return true;
    }

    internal void AddFolder(AnalysisNode path, UInt128 amount, bool incomplete, bool sharedElsewhere)
    {
        if (incomplete && amount == 0)
        {
            _unknown++;
            return;
        }

        var priority = (true, amount, ulong.MaxValue - path.Id);
        if (!AdmitRow(priority)) return;

        string evidence = incomplete ? "Known portion from the captured subtree. Some entries are unmeasured." : "Known total from the captured subtree.";
        if (sharedElsewhere)
            evidence += " Some shared files are counted at their common ancestor.";
        var finding = new AnalysisRecipeFinding(AnalysisRecipeMatch.Candidate, evidence);
        var bytes = Metric == AnalysisMetric.Files ? AnalysisMeasurement.Missing(MeasurementState.Unsupported) : AnalysisMeasurement.Known(amount);
        var row = new AnalysisRecipeCandidate(path.Id, path.RootId, AnalysisNames.DisplayName(path, 256), bytes, finding)
        {
            RecordedFilePaths = Metric == AnalysisMetric.Files ? checked((ulong)amount) : null
        };
        Enqueue(row, priority);
    }
}

public sealed partial class AnalysisDatasetExplorer
{
    internal async Task<AnalysisRecipePreview> PreviewLargestFoldersAsync(AnalysisRecipeRequest request, CancellationToken cancellation)
    {
        if (_hierarchy is not { Filter: null } hierarchy)
            return await _dataset.PreviewRecipeAsync(request, Metric, cancellation).ConfigureAwait(false);

        using var admission = AnalysisExplorerAdmission.Acquire();
        var descriptor = hierarchy.Descriptor;
        var preview = new AnalysisRecipePreviews(request, descriptor.Roots, descriptor.Identity, hierarchy.Metric);
        await using var retained = await hierarchy.RetainAsync(cancellation).ConfigureAwait(false);
        return await retained.PreviewLargestFoldersAsync(preview, cancellation).ConfigureAwait(false);
    }
}

public sealed partial class AnalysisDatasetHierarchy
{
    internal async Task<AnalysisRecipePreview> PreviewLargestFoldersAsync(AnalysisRecipePreviews preview, CancellationToken cancellation)
    {
        if (Filter is not null || Metric != preview.Metric)
            throw new InvalidOperationException("Folder ranking requires the unfiltered hierarchy in the chosen measure.");

        if (!Totals.CompatibleBasis)
        {
            preview.ExplainIncompatibleFolderTotals();
            return preview.Finish(Descriptor.Identity, Descriptor.Coverage);
        }

        ulong next = 1;
        while (next <= Descriptor.Paths)
        {
            await _owned.RunAsync((state, token) =>
            {
                long bytes = 0;
                int records = 0;
                var clock = Stopwatch.StartNew();
                while (next <= Descriptor.Paths && records++ < AnalysisLimits.BatchRecords &&
                    bytes < AnalysisLimits.BatchBytes)
                {
                    token.ThrowIfCancellationRequested();
                    var path = state.Graph.ReadPathRow(next++, token);
                    bytes += AnalysisIndexedFilter.Cost(path);
                    if (preview.IncludesFolder(path) && preview.CanRankFolder(path))
                    {
                        var range = state.Indexes[0].FindRange(Key(path.Id), token);
                        if (range.Count != 1) throw Invalid();
                        var row = state.Read(state.Indexes[0].Read(range.First, token).Value, token, out long rowBytes);
                        bytes += rowBytes;
                        preview.AddFolder(path, ChartArea(row.Totals), row.HasUnmeasuredEvidence || row.NamespaceIncomplete, row.HasChargesElsewhere);
                    }
                    if (clock.ElapsedMilliseconds >= 20) break;
                }
                return true;
            }, cancellation).ConfigureAwait(false);
        }
        cancellation.ThrowIfCancellationRequested();
        return preview.Finish(Descriptor.Identity, Descriptor.Coverage);
    }
}
