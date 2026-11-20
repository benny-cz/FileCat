using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Theory]
    [MemberData(nameof(AnalysisGroupingTests.DimensionsAndMetrics), MemberType = typeof(AnalysisGroupingTests))]
    public async Task GroupGeometryRetainsTheCanonicalOverviewAndMemberOrderIncludingSharedReferencesAfterProducerClosure(AnalysisGroupingDimension dimension, AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var snapshot = AnalysisGroupingTests.Fixture();
        snapshot = snapshot with { Issues = snapshot.Issues.Add(new("fixture-metadata-gap", 4, MeasurementState.Changed)) };
        var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, metric, cancellation: Cancellation), dimension, false, Cancellation);
        var directory = new MemoryDirectory(); var dataset = await Build(snapshot, io, directory); var lease = await dataset.AcquireAsync(Cancellation);
        var original = await lease.BuildGroupingAsync(metric, dimension, cancellation: Cancellation); var retained = await original.RetainAsync(Cancellation);
        await original.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); Assert.False(directory.Closed);
        try
        {
            var overview = await ReadAllGeometry(null); EqualGeometry(tree.ChildrenOf(AnalysisTree.RootKey), overview);
            foreach (var group in tree.Groups.Values)
            {
                string key = group.Definition.Key; var members = await ReadAllGeometry(key); EqualGeometry(tree.ChildrenOf(key), members);
                if (group.Kind == AnalysisGroupKind.SharedAcrossGroups)
                {
                    Assert.All(members.Where(item => item.NodeId.HasValue), item => Assert.StartsWith("group-reference:" + key + ":", item.Key));
                    Assert.Contains(members, item => item.Family == "shared");
                    var captured = await retained.ReadGroupAsync(key, Cancellation); var expectedReferences = tree.Items[key].ReferenceLogical!;
                    Assert.Equal(expectedReferences.KnownBytes, captured.ReferenceLogical.KnownBytes); Assert.Equal(expectedReferences.Files, captured.ReferenceLogical.Files);
                    Assert.Equal(expectedReferences.Missing.OrderBy(pair => pair.Key), captured.ReferenceLogical.Missing.OrderBy(pair => pair.Key));
                }
                var first = await retained.ReadGeometryAsync(key, maximum: 1, cancellation: Cancellation);
                await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => retained.ReadGeometryAsync(key, first.Total + 1, cancellation: Cancellation));
                foreach (var sort in Enum.GetValues<AnalysisTableSort>()) foreach (bool descending in new[] { false, true })
                {
                    var table = await retained.ReadMemberRowsAsync(key, sort: sort, descending: descending, cancellation: Cancellation);
                    Assert.Equal(group.Members.Order(), table.Rows.Select(row => row.Member.Path.Id).Order());
                    EqualGeometry(members, await ReadAllGeometry(key));
                }
            }
        }
        finally { await retained.DisposeAsync(); await original.DisposeAsync(); await lease.DisposeAsync(); await dataset.DisposeAsync(); }
        Assert.True(directory.Closed);

        async Task<ImmutableArray<AnalysisTreeItem>> ReadAllGeometry(string? key)
        {
            var result = ImmutableArray.CreateBuilder<AnalysisTreeItem>(); ulong offset = 0;
            while (true)
            {
                var page = await retained.ReadGeometryAsync(key, offset, maximum: 1, cancellation: Cancellation); result.AddRange(page.Rows);
                if (!page.HasMore) return result.ToImmutable(); Assert.NotEmpty(page.Rows); offset = page.NextOffset;
            }
        }
        static void EqualGeometry(ImmutableArray<AnalysisTreeItem> expected, ImmutableArray<AnalysisTreeItem> actual)
        {
            Assert.Equal(expected.Select(item => item.Key), actual.Select(item => item.Key));
            for (int row = 0; row < expected.Length; row++)
            {
                Assert.Equal(expected[row].Value, actual[row].Value); Assert.Equal(expected[row].Unknown, actual[row].Unknown);
                Assert.Equal(expected[row].NodeId, actual[row].NodeId); Assert.Equal(expected[row].LogicalSize, actual[row].LogicalSize);
                Assert.Equal(expected[row].Files, actual[row].Files); Assert.Equal(expected[row].IdentityUnverified, actual[row].IdentityUnverified);
            }
        }
    }
}
