using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    public static IEnumerable<object[]> IndexedComparisonLocateCases()
    {
        foreach (var metric in Enum.GetValues<AnalysisMetric>()) foreach (var order in Enum.GetValues<AnalysisComparisonOrder>()) yield return [metric, order];
    }
    [Theory]
    [MemberData(nameof(IndexedComparisonLocateCases))]
    public async Task IndexedComparisonLocateBindsWholeAliasEntitiesAndExactAppliedRowOffsets(AnalysisMetric metric, AnalysisComparisonOrder order)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var (old, current) = QuerySamples();
        await using var a = await Build(old, io, enumeration: ComparisonEnumeration(old), arbitraryPathIds: true);
        await using var b = await Build(current, io, enumeration: ComparisonEnumeration(current), arbitraryPathIds: true);
        await using var before = await a.AcquireAsync(Cancellation); await using var now = await b.AcquireAsync(Cancellation);
        await using var result = await now.CompareIndexedAsync(before, metric, cancellation: Cancellation);
        await using var query = await result.QueryAsync(new() { Metric = metric, Order = order }, cancellation: Cancellation);
        var all = (await query.ReadAsync(0, 512, Cancellation)).Rows;
        Assert.Equal(query.Count, (ulong)all.Length);
        var previousPage = await query.ReadBeforeAsync(query.Count, 2, Cancellation);
        Assert.Equal(all.TakeLast(2).Select(row => row.Id), previousPage.Rows.Select(row => row.Id));
        Assert.Equal(query.Count, previousPage.NextOffset);
        Assert.Empty((await query.ReadBeforeAsync(0, 2, Cancellation)).Rows);
        foreach (bool previous in new[] { false, true })
        {
            var snapshot = previous ? old : current;
            foreach (var path in snapshot.Nodes.Where(path => path.Kind == AnalysisKind.File && path.IncludedByPolicy))
            {
                var row = await result.FindRowForPathAsync(previous, path.Id, Cancellation); Assert.NotNull(row);
                var side = previous ? row.Previous : row.Current; Assert.NotNull(side);
                var members = (await result.ReadMembersAsync(previous, side.Id, 0, 512, Cancellation)).Rows;
                Assert.Contains(members, member => member.Id == path.Id);
                ulong memberOffset = await result.FindMemberOffsetAsync(previous, side.Id, path.Id, Cancellation);
                Assert.Equal(path.Id, Assert.Single((await result.ReadMembersAsync(previous, side.Id, memberOffset, 1, Cancellation)).Rows).Id);
                var preceding = await result.ReadMembersBeforeAsync(previous, side.Id, (ulong)members.Length, 1, Cancellation);
                Assert.Equal(members[^1].Id, Assert.Single(preceding.Rows).Id);
                Assert.Equal((ulong)members.Length, preceding.NextOffset);
                ulong? offset = await result.FindOffsetAsync(row.Id, Cancellation); Assert.NotNull(offset);
                Assert.Equal(row.Id, Assert.Single((await result.ReadAsync(offset.Value, 1, Cancellation)).Rows).Id);
                ulong? applied = await query.FindOffsetAsync(row.Id, Cancellation); Assert.NotNull(applied);
                Assert.Equal(row.Id, Assert.Single((await query.ReadAsync(applied.Value, 1, Cancellation)).Rows).Id);
            }
        }
        var found = await result.FindRowForPathAsync(false, current.Nodes.First(path => path.Kind == AnalysisKind.File).Id, Cancellation); Assert.NotNull(found);
        await using var excluded = await result.QueryAsync(new() { Metric = metric, Query = "name:missing-comparison-fixture" }, cancellation: Cancellation);
        Assert.Equal(0UL, excluded.Count); Assert.Null(await excluded.FindOffsetAsync(found.Id, Cancellation));
        await using var retained = await query.RetainAsync(Cancellation); await query.DisposeAsync();
        var closing = result.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
        Assert.NotNull(await retained.FindOffsetAsync(found.Id, Cancellation));
        await retained.DisposeAsync(); await excluded.DisposeAsync(); await closing;
    }
    [Fact]
    public async Task IndexedComparisonLocateCanceledBeforeAdmissionDoesNotAllocateAnIndexAndAllowsRetry()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); var directory = new MemoryDirectory();
        await using var dataset = await Build(s, io, directory, enumeration: ComparisonEnumeration(s), arbitraryPathIds: true);
        await using var source = await dataset.AcquireAsync(Cancellation);
        await using var result = await source.CompareIndexedAsync(source, AnalysisMetric.FileSize, cancellation: Cancellation);
        await using var query = await result.QueryAsync(new() { Order = AnalysisComparisonOrder.Path }, cancellation: Cancellation);
        long before = directory.WrittenBytes; using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.FindRowForPathAsync(false, 4, stop.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result.FindOffsetAsync(1, stop.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query.FindOffsetAsync(1, stop.Token));
        Assert.Equal(before, directory.WrittenBytes);
        Assert.NotNull(await result.FindOffsetAsync(1, Cancellation)); Assert.NotNull(await query.FindOffsetAsync(1, Cancellation));
    }
}
