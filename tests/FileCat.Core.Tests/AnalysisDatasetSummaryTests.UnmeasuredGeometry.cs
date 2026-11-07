using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    public static IEnumerable<object[]> UnmeasuredGeometryCases()
    {
        foreach (var metric in Enum.GetValues<AnalysisMetric>())
        foreach (bool filtered in new[] { false, true })
        foreach (string evidence in new[] { "zero", "unverified-zero", "unverified-missing", "unsupported", "excluded", "failed", "pending", "changed", "conflict", "no-own-facts", "covered-missing-fact", "namespace-issue", "partial", "unknown-kind", "directory-missing" })
            yield return [metric, filtered, evidence];
    }

    [Theory]
    [MemberData(nameof(UnmeasuredGeometryCases))]
    public async Task UnmeasuredHierarchyGeometryAndEveryOtherSuffixRetainMemoryEvidence(AnalysisMetric metric, bool filtered, string evidence)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = UnmeasuredGeometrySource(evidence);
        const string query = "name:x.bin OR name:z.bin";
        var filter = AnalysisFilter.Parse(query, TimeZoneInfo.Utc);
        bool Match(AnalysisNode node, AnalysisObject? obj) => filter.Evaluate(new(node, obj, node.Name, AnalysisMeasurement.Missing(MeasurementState.Unsupported))) == AnalysisTruth.True;
        var tree = AnalysisTree.Build(source, AnalysisAggregate.Project(source, metric, filtered ? Match : null, Cancellation), filtered, Cancellation);
        var directory = new MemoryDirectory(); var dataset = await Build(source, io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var hierarchy = filtered ? await lease.BuildHierarchyAsync(metric, new(query), Cancellation) : await lease.BuildHierarchyAsync(metric, Cancellation);
        await lease.DisposeAsync(); await dataset.DisposeAsync();
        try
        {
            var combined = await AnalysisIndexedChart.BuildAsync(hierarchy, 0, Cancellation);
            Assert.Equal(tree.Items[AnalysisTree.RootKey].Unknown, combined.Items[combined.Scope].Unknown);
            Assert.Equal(tree.Items[AnalysisTree.RootKey].LogicalUnknown, combined.Items[combined.Scope].LogicalUnknown);
            foreach (ulong parent in new ulong[] { 0, 1, 2, 3 })
            {
                string key = parent == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(parent);
                var expected = tree.ChildrenOf(key); var actual = (await hierarchy.ReadGeometryAsync(parent, cancellation: Cancellation)).Rows;
                Assert.Equal(expected.Select(item => item.Key), actual.Select(item => item.Key));
                for (int row = 0; row < actual.Length; row++)
                {
                    Assert.Equal(expected[row].Value, actual[row].Value); Assert.Equal(expected[row].Unknown, actual[row].Unknown);
                    if (actual[row].NodeId is null) continue;
                    Assert.Equal(expected[row].LogicalUnknown, actual[row].LogicalUnknown);
                    Assert.Equal(expected[row].LogicalSize, actual[row].LogicalSize); Assert.Equal(expected[row].Files, actual[row].Files);
                }
                for (int skip = 1; skip <= actual.Length; skip++)
                {
                    await using var subset = await hierarchy.CreateSubsetAsync(parent, skip, Cancellation);
                    string scope = AnalysisTree.OtherKey(key, skip);
                    Assert.Equal(tree.ChildrenOf(scope).Select(item => item.Key), (await subset.ReadGeometryAsync(cancellation: Cancellation)).Rows.Select(item => item.Key));
                    Assert.Equal(tree.ScopeValue(scope), AnalysisGroupedChart.Area(subset.Totals, metric));
                    Assert.Equal(tree.CaptureMatchingFiles(scope, Cancellation).NodeIds, (await subset.CaptureMatchingFilesAsync(cancellation: Cancellation)).NodeIds);
                    var chart = await AnalysisIndexedChart.BuildAsync(subset, Cancellation);
                    Assert.Equal(tree.ChildrenOf(scope).Any(item => item.Unknown), chart.Items[chart.Scope].Unknown);
                    Assert.Equal(tree.ChildrenOf(scope).Any(item => item.LogicalUnknown), chart.Items[chart.Scope].LogicalUnknown);
                }
            }
        }
        finally { await hierarchy.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); }
        Assert.True(directory.Closed);
    }

    private static AnalysisSnapshot UnmeasuredGeometrySource(string evidence)
    {
        var source = AnalysisAccountingTests.Golden();
        source = source with { Objects = source.Objects.SetItem(1, source.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(0) }),
            Facts = source.Facts.SetItem(1, source.Facts[1] with { Measurement = AnalysisMeasurement.Known(0) }) };
        if (Enum.TryParse<MeasurementState>(evidence, true, out var state))
            return source with { Objects = source.Objects.SetItem(0, source.Objects[0] with { LogicalLength = AnalysisMeasurement.Missing(state) }),
                Facts = source.Facts.SetItem(0, source.Facts[0] with { Measurement = AnalysisMeasurement.Missing(state) }) };
        return evidence switch
        {
            "zero" => source,
            "unverified-zero" => source with { Objects = source.Objects.SetItem(0, source.Objects[0] with { VerifiedIdentity = null, LogicalLength = AnalysisMeasurement.Known(0) }),
                Facts = source.Facts.SetItem(0, source.Facts[0] with { Measurement = AnalysisMeasurement.Known(0) }) },
            "unverified-missing" => source with { Objects = source.Objects.SetItem(0, source.Objects[0] with { VerifiedIdentity = null, LogicalLength = AnalysisMeasurement.Missing(MeasurementState.Unsupported) }),
                Facts = source.Facts.SetItem(0, source.Facts[0] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Unsupported) }) },
            "conflict" => source with { Nodes = source.Nodes.SetItem(4, source.Nodes[4] with { ObjectId = "X2" }),
                Objects = source.Objects.Add(source.Objects[0] with { Id = "X2", VersionEvidence = "later" }).SetItem(0, source.Objects[0] with { VersionEvidence = "earlier" }) },
            "no-own-facts" => source with { Nodes = source.Nodes.SetItem(4, source.Nodes[4] with { ObjectId = "X2" }), Objects = source.Objects.Add(source.Objects[0] with { Id = "X2" }) },
            "covered-missing-fact" => source with { Facts = source.Facts.SetItem(0, source.Facts[0] with { Includes = ["detail"] })
                .Add(new("covered", "X", null, "detail", "fixture-v1", AnalysisMeasurement.Missing(MeasurementState.Unsupported), "fixture", DateTimeOffset.UnixEpoch, [])) },
            "namespace-issue" => source with { Issues = [new("Unreadable", 3, MeasurementState.Failed), new("Excluded", 2, MeasurementState.Excluded)], Coverage = NamespaceCoverage.Partial },
            "partial" => source with { Coverage = NamespaceCoverage.Partial },
            "unknown-kind" => source with { Nodes = source.Nodes.Add(new(7, "r", 3, "unknown", AnalysisKind.Unknown)) },
            "directory-missing" => source with { Facts = source.Facts.Add(new("directory", null, 2, "directory-self", "fixture-v1", AnalysisMeasurement.Missing(MeasurementState.Failed), "fixture", DateTimeOffset.UnixEpoch, [])) },
            _ => throw new ArgumentOutOfRangeException(nameof(evidence))
        };
    }

    [Fact]
    public async Task IncompatibleCombinedGeometryAndOtherAreExplicitlyUnavailableWhileRootViewsRetainTheirAmounts()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var source = AnalysisAccountingTests.Golden();
        source = source with { Roots = source.Roots.Add(source.Roots[0] with { Id = "other", Label = "Other", DisplayLocator = "Other", CanonicalLocator = "Other" }),
            Nodes = source.Nodes.Add(new(7, "other", 0, "Other", AnalysisKind.Directory)).Add(new(8, "other", 7, "w", AnalysisKind.File, "W")),
            Objects = source.Objects.Add(new("W", "other:W", AnalysisMeasurement.Known(70), AnalysisMeasurement.Known(1))),
            Facts = source.Facts.Add(new("other-fact", "W", null, "primary", "different-basis", AnalysisMeasurement.Known(50), "fixture", DateTimeOffset.UnixEpoch, [])) };
        await using var dataset = await Build(source, io); await using var lease = await dataset.AcquireAsync(Cancellation);
        await using var hierarchy = await lease.BuildHierarchyAsync(AnalysisMetric.OnDisk, Cancellation);
        var unavailable = await Assert.ThrowsAsync<InvalidOperationException>(() => hierarchy.ReadGeometryAsync(0, cancellation: Cancellation));
        Assert.Contains("These locations report different allocation components", unavailable.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => hierarchy.CreateSubsetAsync(0, 1, Cancellation));
        Assert.Equal((UInt128)80, (await hierarchy.ReadGeometryAsync(1, cancellation: Cancellation)).Rows.Aggregate((UInt128)0, (sum, item) => checked(sum + item.Value)));
        Assert.Equal((UInt128)50, Assert.Single((await hierarchy.ReadGeometryAsync(7, cancellation: Cancellation)).Rows).Value);
        await using var subset = await hierarchy.CreateSubsetAsync(1, 1, Cancellation);
        Assert.Equal(AnalysisTree.OtherKey("n:1", 1), subset.ScopeKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => subset.CreateSiblingAsync(0, 1, Cancellation));
    }
}
