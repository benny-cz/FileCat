using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Fact]
    public async Task IndexedRecipePreviewRetainsOnlyTheHighestRankedRowsAndReportsUncertainty()
    {
        Ordinary();
        using var io = new DeviceIoScheduler();
        var snapshot = AnalysisAccountingTests.Golden();
        var files = new List<AnalysisNode>();
        var objects = new List<AnalysisObject>();
        for (ulong id = 2; id <= 201; id++)
        {
            string name = $"file-{id}";
            files.Add(new(id, "r", 1, name, AnalysisKind.File, name));
            objects.Add(new(name, null, id == 2 ? AnalysisMeasurement.Missing(MeasurementState.Unsupported) : AnalysisMeasurement.Known(id),
                AnalysisMeasurement.Known(1)));
        }
        files[1] = files[1] with { Cloud = true };
        snapshot = snapshot with { Nodes = [snapshot.Nodes[0], .. files], Objects = [.. objects], Facts = [] };
        await using var dataset = await Build(snapshot, io);
        await using var lease = await dataset.AcquireAsync(Cancellation);
        var request = new AnalysisRecipeRequest(AnalysisMetadataRecipe.LargestFiles, ["r"]);
        var result = await lease.PreviewRecipeAsync(request, AnalysisMetric.FileSize, Cancellation);
        Assert.Equal(dataset.Descriptor.Identity, result.Identity);
        Assert.Equal("largest-files", result.Definition.Id);
        Assert.Equal(198UL, result.Candidates);
        Assert.Equal(1UL, result.Unknown);
        Assert.Equal(1UL, result.Excluded);
        Assert.Equal(Enumerable.Range(74, 128).Reverse().Select(id => (ulong)id), result.Rows.Select(row => row.PathId));
        Assert.Equal((UInt128)201, result.Rows[0].RecordedBytes.Value);
        await Assert.ThrowsAsync<ArgumentException>(() => lease.PreviewRecipeAsync(request, AnalysisMetric.Files, Cancellation));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lease.PreviewRecipeAsync(request, AnalysisMetric.FileSize, canceled.Token));
    }

    [Fact]
    public async Task IndexedRecipePreviewUsesRecordedCompatibleAllocation()
    {
        Ordinary();
        using var io = new DeviceIoScheduler();
        await using var dataset = await Build(AnalysisAccountingTests.Golden(), io);
        await using var lease = await dataset.AcquireAsync(Cancellation);
        var result = await lease.PreviewRecipeAsync(new(AnalysisMetadataRecipe.LargestFiles, ["r"]), AnalysisMetric.OnDisk, Cancellation);
        Assert.Equal(2UL, result.Candidates);
        Assert.Equal(1UL, result.Unknown);
        Assert.Equal(new ulong[] { 4, 5 }, result.Rows.Select(row => row.PathId));
        Assert.All(result.Rows, row => Assert.Equal((UInt128)80, row.RecordedBytes.Value));
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    [InlineData("unix-bytes")]
    public async Task IndexedRecipeLooksUpTheExactCapturedProjectMarker(string encoding)
    {
        Ordinary();
        using var io = new DeviceIoScheduler();
        var snapshot = AnalysisAccountingTests.Golden();
        AnalysisNode[] nodes =
        [
            snapshot.Nodes[0], new(2, "r", 1, "Example", AnalysisKind.Directory),
            new(3, "r", 2, "obj", AnalysisKind.Directory), new(4, "r", 3, "project.assets.json", AnalysisKind.File, "a"),
            new(5, "r", 2, "Example.csproj", AnalysisKind.File, "b"), new(6, "r", 1, "Lookalike", AnalysisKind.Directory),
            new(7, "r", 6, "obj", AnalysisKind.Directory), new(8, "r", 7, "project.assets.json", AnalysisKind.File, "c")
        ];
        snapshot = snapshot with
        {
            Nodes = nodes.Select(node => node with { NameEncoding = encoding, RawName = [..AnalysisRawNames.Encode(encoding, node.Name)] }).ToImmutableArray(),
            Objects = [.. new[] { "a", "b", "c" }.Select(id => new AnalysisObject(id, null, AnalysisMeasurement.Known(10), AnalysisMeasurement.Known(1)))],
            Facts = []
        };
        await using var dataset = await Build(snapshot, io);
        await using var lease = await dataset.AcquireAsync(Cancellation);
        var result = await lease.PreviewRecipeAsync(new(AnalysisMetadataRecipe.GeneratedBuildOutput, ["r"]), AnalysisMetric.FileSize, Cancellation);
        Assert.Equal(1UL, result.Candidates);
        Assert.Equal(1UL, result.Unknown);
        var row = Assert.Single(result.Rows);
        Assert.Equal(4UL, row.PathId);
        Assert.Equal(3UL, row.Finding.ContextPath);
        Assert.Equal(5UL, row.Finding.MarkerPath);
    }
}
