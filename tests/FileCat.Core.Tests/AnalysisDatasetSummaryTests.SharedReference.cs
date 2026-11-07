using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    public static IEnumerable<object[]> FolderSharedReferenceCases()
    {
        foreach (var metric in new[] { AnalysisMetric.UniqueFileSize, AnalysisMetric.OnDisk })
        foreach (bool filtered in new[] { false, true })
        foreach (bool distinct in new[] { false, true })
        foreach (bool missing in new[] { false, true })
        foreach (bool components in new[] { false, true })
            yield return [metric, filtered, distinct, missing, components];
    }

    [Theory]
    [MemberData(nameof(FolderSharedReferenceCases))]
    public async Task FolderSharedReferenceStatisticsMatchMemoryWithoutDuplicatingNamespaceTotals(AnalysisMetric metric, bool filtered, bool distinct, bool missing, bool components)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = AnalysisAccountingTests.Golden();
        source = source with { Objects = source.Objects.SetItem(0, source.Objects[0] with { LogicalLength = missing ? AnalysisMeasurement.Missing(MeasurementState.Unsupported) : AnalysisMeasurement.Known(100) }),
            Facts = source.Facts.SetItem(1, source.Facts[1] with { Measurement = AnalysisMeasurement.Known(4) }) };
        if (distinct) source = source with { Nodes = source.Nodes.SetItem(4, source.Nodes[4] with { ObjectId = "X2" }),
            Objects = source.Objects.Add(source.Objects[0] with { Id = "X2" }), Facts = source.Facts.Add(source.Facts[0] with { Id = "duplicate", ObjectId = "X2" }) };
        if (components) source = source with { Facts = source.Facts.Add(new("extra", "X", null, "extra", "fixture-v1", AnalysisMeasurement.Known(5), "fixture", DateTimeOffset.UnixEpoch, [])) };
        var tree = AnalysisTree.Build(source, AnalysisAggregate.Project(source, metric, filtered ? (node, _) => node.Id == 4 : null, Cancellation), filtered, Cancellation);
        var directory = new MemoryDirectory(); var dataset = await Build(source, io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var hierarchy = filtered ? await lease.BuildHierarchyAsync(metric, new("name:x.bin"), Cancellation) : await lease.BuildHierarchyAsync(metric, Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        try
        {
            var bucket = Assert.Single(await hierarchy.ReadBucketsAsync(1, Cancellation)); var expected = tree.Items["shared:n:1"];
            var actual = AnalysisIndexedChart.Bucket(bucket, metric); Assert.NotNull(actual.ReferenceLogical); Assert.NotNull(expected.ReferenceLogical);
            ulong files = filtered ? 1UL : 2UL;
            Assert.Equal(files, actual.ReferenceLogical.Files); Assert.Equal(missing ? files : 0UL, actual.ReferenceLogical.UnknownFiles);
            Assert.Equal((UInt128)(missing ? 0 : filtered ? 100 : 200), actual.ReferenceLogical.KnownBytes);
            Assert.Equal(expected.ReferenceLogical.KnownBytes, actual.ReferenceLogical.KnownBytes); Assert.Equal(expected.ReferenceLogical.Files, actual.ReferenceLogical.Files);
            Assert.Equal(expected.ReferenceLogical.Missing.OrderBy(pair => pair.Key), actual.ReferenceLogical.Missing.OrderBy(pair => pair.Key));
            Assert.Equal(0UL, actual.Files); Assert.Equal((UInt128)0, actual.LogicalSize); Assert.Equal(expected.Value, actual.Value);
            var root = await hierarchy.ReadAsync(1, Cancellation); Assert.Equal(tree.Items["n:1"].LogicalSize, root.Logical.KnownBytes); Assert.Equal(tree.Items["n:1"].Files, root.Logical.Files);
            var geometry = await hierarchy.ReadGeometryAsync(1, cancellation: Cancellation); Assert.Equal(tree.ChildrenOf("n:1").Select(item => item.Key), geometry.Rows.Select(item => item.Key));
            Assert.Equal(root.Logical.KnownBytes, geometry.Rows.Aggregate((UInt128)0, (sum, item) => checked(sum + item.LogicalSize)));
            Assert.Equal(root.Logical.Files, geometry.Rows.Aggregate(0UL, (sum, item) => checked(sum + item.Files)));
            for (int skip = 1; skip <= geometry.Rows.Length; skip++)
            {
                await using var subset = await hierarchy.CreateSubsetAsync(1, skip, Cancellation); string scope = AnalysisTree.OtherKey("n:1", skip);
                Assert.Equal(tree.CaptureMatchingFiles(scope, Cancellation).NodeIds, (await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
                Assert.Equal(tree.ScopeValue(scope), AnalysisGroupedChart.Area(subset.Totals, metric));
            }
        }
        finally { await hierarchy.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); }
        Assert.True(directory.Closed);
    }

    [Fact]
    public async Task FolderSharedReferenceMultipleCohortsAndComponentsCountEachMatchingPathOnce()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = AnalysisAccountingTests.Golden();
        source = source with { Nodes = source.Nodes.Add(new(7, "r", 3, "z-alias.bin", AnalysisKind.File, "Z")),
            Objects = source.Objects.SetItem(1, source.Objects[1] with { LinkCount = AnalysisMeasurement.Known(2) }),
            Facts = source.Facts.SetItem(1, source.Facts[1] with { Measurement = AnalysisMeasurement.Known(4) })
                .Add(new("x-extra", "X", null, "extra", "fixture-v1", AnalysisMeasurement.Known(5), "fixture", DateTimeOffset.UnixEpoch, []))
                .Add(new("z-extra", "Z", null, "extra", "fixture-v1", AnalysisMeasurement.Known(2), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await using var dataset = await Build(source, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, Cancellation);
        var bucket = Assert.Single(await hierarchy.ReadBucketsAsync(1, Cancellation)); Assert.Equal(2UL, bucket.Objects); Assert.Equal(4UL, bucket.Charges);
        Assert.Equal((UInt128)91, bucket.Totals.Known); Assert.NotNull(bucket.ReferenceLogical); Assert.Equal(4UL, bucket.ReferenceLogical.Files); Assert.Equal((UInt128)240, bucket.ReferenceLogical.KnownBytes);
        Assert.Equal(bucket.ReferenceLogical, hierarchy.LogicalTotals);
    }

    [Fact]
    public async Task FolderSharedReferenceChartRemainderAndEveryChildPreserveAdditiveLogicalStatistics()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = AnalysisAccountingTests.Golden();
        source = source with { Nodes = new[] { source.Nodes[0] }.Concat(Enumerable.Range(2, 142).Select(id => new AnalysisNode((ulong)id, "r", 1, $"file-{id:D3}", AnalysisKind.File, id <= 3 ? "X" : "N" + id))).ToImmutableArray(),
            Objects = new[] { source.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(1000) } }.Concat(Enumerable.Range(4, 140).Select(id => new AnalysisObject("N" + id, "identity:" + id, AnalysisMeasurement.Known(1), AnalysisMeasurement.Known(1)))).ToImmutableArray(), Facts = [] };
        var tree = AnalysisTree.Build(source, AnalysisAggregate.Project(source, AnalysisMetric.UniqueFileSize, cancellation: Cancellation), false, Cancellation);
        var children = tree.ChildrenOf("n:1"); Assert.Equal((UInt128)2140, children.Aggregate((UInt128)0, (sum, row) => checked(sum + row.LogicalSize)));
        Assert.Equal(142UL, children.Aggregate(0UL, (sum, row) => checked(sum + row.Files)));
        await using var dataset = await Build(source, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.UniqueFileSize, Cancellation);
        var chart = await AnalysisIndexedChart.BuildAsync(hierarchy, 1, Cancellation); var remainder = Assert.Single(chart.Remainders);
        Assert.Equal(128UL, remainder.Value.Offset); var item = chart.Items[remainder.Key]; Assert.Equal((UInt128)13, item.Value); Assert.Equal((UInt128)2013, item.LogicalSize); Assert.Equal(15UL, item.Files);
        await using var subset = await hierarchy.CreateSubsetAsync(1, 128, Cancellation);
        Assert.Equal(item.LogicalSize, subset.LogicalTotals.KnownBytes); Assert.Equal(item.Files, subset.LogicalTotals.Files);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize, false)]
    [InlineData(AnalysisMetric.UniqueFileSize, false)]
    [InlineData(AnalysisMetric.OnDisk, false)]
    [InlineData(AnalysisMetric.Files, false)]
    [InlineData(AnalysisMetric.FileSize, true)]
    [InlineData(AnalysisMetric.UniqueFileSize, true)]
    [InlineData(AnalysisMetric.OnDisk, true)]
    [InlineData(AnalysisMetric.Files, true)]
    public Task FolderSharedReferenceMetadataEvidenceRetainsCanonicalScopeFlags(AnalysisMetric metric, bool filtered) =>
        UnmeasuredHierarchyGeometryAndEveryOtherSuffixRetainMemoryEvidence(metric, filtered, "metadata");
}
