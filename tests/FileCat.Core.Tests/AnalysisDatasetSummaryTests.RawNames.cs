using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    public static IEnumerable<object[]> RawNameFolderCases()
    {
        foreach (var metric in Enum.GetValues<AnalysisMetric>()) foreach (string encoding in new[] { "utf8", "utf16le", "unix-bytes" }) foreach (bool filtered in new[] { false, true })
            yield return [metric, encoding, filtered];
    }
    public static IEnumerable<object[]> RawNameGroupCases()
    {
        foreach (var dimension in Enum.GetValues<AnalysisGroupingDimension>().Where(value => value != AnalysisGroupingDimension.FolderHierarchy))
        foreach (var metric in Enum.GetValues<AnalysisMetric>()) foreach (string encoding in new[] { "utf8", "utf16le", "unix-bytes" })
            yield return [dimension, metric, encoding];
    }

    [Theory]
    [MemberData(nameof(RawNameFolderCases))]
    public async Task RawNameFolderGeometryOrdersAndExactOtherCapturesKeepDistinctCapturedNames(AnalysisMetric metric, string encoding, bool filtered)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = RawNameSource(encoding);
        var filter = AnalysisFilter.Parse("name:.bin", TimeZoneInfo.Utc);
        bool Match(AnalysisNode node, AnalysisObject? obj) => filter.Evaluate(new(node, obj, node.Name, AnalysisMeasurement.Missing(MeasurementState.Unsupported))) == AnalysisTruth.True;
        var tree = AnalysisTree.Build(source, AnalysisAggregate.Project(source, metric, filtered ? Match : null, Cancellation), filtered, Cancellation);
        var directory = new MemoryDirectory(); var dataset = await Build(source, io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var hierarchy = filtered ? await lease.BuildHierarchyAsync(metric, new("name:.bin"), Cancellation) : await lease.BuildHierarchyAsync(metric, Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        try
        {
            var geometry = (await hierarchy.ReadGeometryAsync(1, cancellation: Cancellation)).Rows;
            Assert.Equal(tree.ChildrenOf("n:1").Select(item => item.Key), geometry.Select(item => item.Key));
            foreach (var item in geometry.Where(item => item.NodeId.HasValue))
            {
                var node = source.Nodes.Single(node => node.Id == item.NodeId); var row = await hierarchy.ReadAsync(node.Id, Cancellation);
                Assert.Equal(node.Name, row.Path.Name); Assert.Equal(node.RawName, row.Path.RawName); Assert.Equal(node.NameEncoding, row.Path.NameEncoding);
                Assert.Equal(AnalysisNames.DisplayName(node, 256), item.Name); Assert.Equal(AnalysisNames.NameEvidence(node), item.NameEvidence);
                Assert.Null(item.CapturedOrderName);
            }
            foreach (bool descending in new[] { false, true })
            {
                using var memory = AnalysisTableOrder.Apply(tree.ChildrenOf("n:1").Where(item => item.NodeId.HasValue).ToImmutableArray(), AnalysisTableSort.Name, descending, Cancellation);
                var actual = await FolderNameRows(hierarchy, descending);
                Assert.Equal(memory.Rows.Select(item => item.NodeId!.Value), actual);
            }
            foreach (int skip in new[] { 1, Math.Max(1, geometry.Length / 2), geometry.Length }.Distinct())
            {
                await using var subset = await hierarchy.CreateSubsetAsync(1, skip, Cancellation); string scope = AnalysisTree.OtherKey("n:1", skip);
                Assert.Equal(tree.CaptureMatchingFiles(scope, Cancellation).NodeIds, (await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
                foreach (bool descending in new[] { false, true })
                {
                    using var memory = AnalysisTableOrder.Apply(tree.ChildrenOf(scope).Where(item => item.NodeId.HasValue).ToImmutableArray(), AnalysisTableSort.Name, descending, Cancellation);
                    var page = await subset.ReadChildrenAsync(AnalysisHierarchySort.Name, cancellation: Cancellation, descending: descending);
                    Assert.Equal(memory.Rows.Select(item => item.NodeId!.Value), page.Rows.Select(row => row.Path.Id));
                    if (page.Total > 0) Assert.Equal(page.Rows.Select(row => row.Path.Id), (await subset.CaptureTableRangeAsync(0, page.Total - 1, AnalysisHierarchySort.Name, descending, Cancellation)).Selection.NodeIds);
                }
            }
        }
        finally { await hierarchy.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); }
        Assert.True(directory.Closed);
    }

    [Theory]
    [MemberData(nameof(RawNameGroupCases))]
    public async Task RawNameGroupedGeometryNameOrderAndOtherEvidenceSurviveSourceClosure(AnalysisGroupingDimension dimension, AnalysisMetric metric, string encoding)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = RawNameSource(encoding);
        var tree = AnalysisTree.BuildGrouped(source, AnalysisAggregate.Project(source, metric, cancellation: Cancellation), dimension, false, Cancellation);
        var directory = new MemoryDirectory(); var dataset = await Build(source, io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var groups = await lease.BuildGroupingAsync(metric, dimension, cancellation: Cancellation); await lease.DisposeAsync(); await dataset.DisposeAsync();
        try
        {
            foreach (var group in tree.Groups.Values)
            {
                string key = group.Definition.Key; var expected = tree.ChildrenOf(key); var geometry = (await groups.ReadGeometryAsync(key, cancellation: Cancellation)).Rows;
                Assert.Equal(expected.Select(item => item.Key), geometry.Select(item => item.Key));
                foreach (var item in geometry.Where(item => item.NodeId.HasValue))
                {
                    var node = source.Nodes.Single(node => node.Id == item.NodeId);
                    Assert.Equal(AnalysisNames.DisplayName(node, 256), item.Name); Assert.Equal(AnalysisNames.NameEvidence(node), item.NameEvidence); Assert.Null(item.CapturedOrderName);
                }
                foreach (bool descending in new[] { false, true })
                {
                    using var memory = AnalysisTableOrder.Apply(expected.Where(item => item.NodeId.HasValue).ToImmutableArray(), AnalysisTableSort.Name, descending, Cancellation);
                    var page = await groups.ReadMemberRowsAsync(key, sort: AnalysisTableSort.Name, descending: descending, cancellation: Cancellation);
                    Assert.Equal(memory.Rows.Select(item => item.NodeId!.Value), page.Rows.Select(row => row.Member.Path.Id));
                    foreach (var row in page.Rows)
                    {
                        var node = source.Nodes.Single(node => node.Id == row.Member.Path.Id); Assert.Equal(node.RawName, row.Member.Path.RawName); Assert.Equal(node.Name, row.Member.Path.Name);
                    }
                }
                if (geometry.Length == 0) continue;
                int skip = Math.Max(1, geometry.Length / 2); await using var subset = await groups.CreateSubsetAsync(key, skip, Cancellation);
                string scope = AnalysisTree.OtherKey(key, skip);
                Assert.Equal(tree.CaptureMatchingFiles(scope, Cancellation).NodeIds, (await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
                Assert.Equal(tree.ChildrenOf(scope).Select(item => item.Key), (await subset.ReadGeometryAsync(cancellation: Cancellation)).Rows.Select(item => item.Key));
            }
        }
        finally { await groups.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); }
        Assert.True(directory.Closed);
    }

    [Theory]
    [InlineData("utf8", false)]
    [InlineData("utf16le", false)]
    [InlineData("unix-bytes", false)]
    [InlineData("utf8", true)]
    [InlineData("utf16le", true)]
    [InlineData("unix-bytes", true)]
    public async Task RawNameMaximumRecordsBoundGeometryWorkInsteadOfCountingOnlyShortLabels(string encoding, bool grouped)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = RawNameSource(encoding, maximum: true);
        await using var dataset = await Build(source, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        if (grouped)
        {
            await using var groups = await lease.BuildGroupingAsync(AnalysisMetric.Files, AnalysisGroupingDimension.CapturedLocation, cancellation: Cancellation);
            string key = Assert.Single((await groups.ReadGroupsAsync(cancellation: Cancellation)).Rows).Definition.Key;
            var first = await groups.ReadGeometryAsync(key, cancellation: Cancellation); Assert.InRange(first.Rows.Length, 1, 2); Assert.True(first.HasMore);
            ulong offset = 0; var ids = new HashSet<ulong>();
            do { var page = await groups.ReadGeometryAsync(key, offset, cancellation: Cancellation); Assert.NotEmpty(page.Rows); foreach (var row in page.Rows) Assert.True(ids.Add(row.NodeId!.Value)); offset = page.NextOffset; } while (offset < 8);
            Assert.Equal(8, ids.Count);
            await using var subset = await groups.CreateSubsetAsync(key, 1, Cancellation); Assert.InRange((await subset.ReadGeometryAsync(cancellation: Cancellation)).Rows.Length, 1, 2);
        }
        else
        {
            await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.Files, Cancellation);
            var first = await hierarchy.ReadGeometryAsync(1, cancellation: Cancellation); Assert.InRange(first.Rows.Length, 1, 2); Assert.True(first.HasMore);
            ulong offset = 0; var ids = new HashSet<ulong>();
            do { var page = await hierarchy.ReadGeometryAsync(1, offset, cancellation: Cancellation); Assert.NotEmpty(page.Rows); foreach (var row in page.Rows) Assert.True(ids.Add(row.NodeId!.Value)); offset = page.NextOffset; } while (offset < 8);
            Assert.Equal(8, ids.Count);
            await using var subset = await hierarchy.CreateSubsetAsync(1, 1, Cancellation); Assert.InRange((await subset.ReadGeometryAsync(cancellation: Cancellation)).Rows.Length, 1, 2);
        }
    }

    private static async Task<ulong[]> FolderNameRows(AnalysisDatasetHierarchy hierarchy, bool descending)
    {
        var ids = new List<ulong>(); ulong offset = 0;
        while (true) { var page = await hierarchy.ReadChildrenAsync(1, AnalysisHierarchySort.Name, offset, 5, Cancellation, descending); ids.AddRange(page.Rows.Select(row => row.Path.Id)); if (!page.HasMore) return ids.ToArray(); offset = page.NextOffset; }
    }
    private static AnalysisSnapshot RawNameSource(string encoding, bool maximum = false)
    {
        var source = AnalysisAccountingTests.Golden(); string[] names = ["A.bin", "a.bin", "aa.bin", "a\u0001.bin", "a\u202E.bin", "trail.bin. ", "trail.bin ", "é.bin", "e\u0301.bin", "𐐨.bin", "𐐀.bin", "ſ.bin", "s.bin", "=1+1.bin"];
        if (encoding != "utf16le") names = [.. names, "a\\u0001.bin", "a\\u202E.bin", "literal\\path.bin"];
        names = [.. names, encoding == "utf16le" ? "unpaired-\uD800.bin" : "last.bin", "other.bin"];
        if (maximum) names = Enumerable.Repeat("", 8).ToArray();
        var nodes = ImmutableArray.CreateBuilder<AnalysisNode>(); nodes.Add(source.Nodes[0]); var objects = ImmutableArray.CreateBuilder<AnalysisObject>(); var facts = ImmutableArray.CreateBuilder<StorageFact>();
        for (int at = 0; at < names.Length; at++)
        {
            byte[] raw = maximum ? encoding == "utf16le" ? Enumerable.Range(0, AnalysisLimits.RawNameBytes / 2).SelectMany(_ => new byte[] { 0, 0xd8 }).ToArray()
                : Enumerable.Repeat(encoding == "unix-bytes" ? (byte)0xff : (byte)'x', AnalysisLimits.RawNameBytes).ToArray()
                : encoding == "unix-bytes" && at >= names.Length - 2 ? [(byte)(at == names.Length - 2 ? 0xff : 0xfe), (byte)'.', (byte)'b', (byte)'i', (byte)'n'] : AnalysisRawNames.Encode(encoding, names[at]);
            if (maximum) { raw[0] = (byte)('a' + at); if (encoding == "utf16le") raw[1] = 0; }
            string name = AnalysisRawNames.Decode(encoding, raw); ulong id = (ulong)at + 2; string objectId = at < 2 ? "shared" : "object-" + at;
            nodes.Add(new(id, "r", 1, name, AnalysisKind.File, objectId) { NameEncoding = encoding, RawName = raw.ToImmutableArray() });
            if (at == 1) continue;
            objects.Add(new(objectId, "identity:" + objectId, AnalysisMeasurement.Known((uint)(at < 2 ? 100 : at + 1)), AnalysisMeasurement.Known(at < 2 ? 2U : 1U)) { ModifiedUtc = DateTimeOffset.UnixEpoch });
            facts.Add(new("fact-" + at, objectId, null, "primary", "fixture-v1", AnalysisMeasurement.Known((uint)(at < 2 ? 80 : at + 1)), "fixture", DateTimeOffset.UnixEpoch, []));
        }
        return source with { Roots = [source.Roots[0] with { NameRules = encoding == "utf16le" ? AnalysisNameRules.Windows : AnalysisNameRules.Unix }], Nodes = nodes.ToImmutable(), Objects = objects.ToImmutable(), Facts = facts.ToImmutable() };
    }
}
