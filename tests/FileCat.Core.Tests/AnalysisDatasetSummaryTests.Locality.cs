using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task UniqueIdentityTraversalPreservesFilteredAndUnfilteredAccountingWhenNativeKeysReverseObjectIds(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisAccountingTests.Golden();
        snapshot = snapshot with { Objects = [snapshot.Objects[0] with { VerifiedIdentity = "zzz" }, snapshot.Objects[1] with { VerifiedIdentity = "aaa" }] };
        snapshot = snapshot with { Nodes = [.. snapshot.Nodes.Select(node => node.ObjectId is { } id
            ? node with { ObservedIdentity = snapshot.Objects.Single(obj => obj.Id == id).VerifiedIdentity } : node)] };
        await using var dataset = await Build(snapshot, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        var expected = AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation).Totals;
        Equal(expected, (await lease.SummarizeAsync(metric, Cancellation)).Totals);
        await using var filtered = await lease.BuildHierarchyAsync(metric, new("name:.bin"), Cancellation);
        Equal(expected, filtered.Totals);
    }
}
