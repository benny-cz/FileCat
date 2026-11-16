using System.Collections.Immutable;
using System.Globalization;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    public static IEnumerable<object[]> IndexedComparisonQueryOrders()
    {
        foreach (var metric in Enum.GetValues<AnalysisMetric>())
            foreach (var side in Enum.GetValues<AnalysisComparisonSide>())
                foreach (var order in Enum.GetValues<AnalysisComparisonOrder>()) yield return [metric, side, order];
    }
    private static (AnalysisSnapshot Old, AnalysisSnapshot Current) QuerySamples()
    {
        var old = ComparisonSample();
        var current = old with
        {
            Nodes = old.Nodes.Where(n => n.Id != 6).Select(n => n.Id == 5 ? n with { Name = "renamed.bin" } : n).Append(new AnalysisNode(7, "r", 2, "new.bin", AnalysisKind.File, "N")).ToImmutableArray(),
            Objects = [old.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(110) }, old.Objects[1] with { Id = "N", VerifiedIdentity = "device:N", LogicalLength = AnalysisMeasurement.Known(7) }],
            Facts = [old.Facts[0] with { Measurement = AnalysisMeasurement.Known(90) }, old.Facts[1] with { Id = "new", ObjectId = "N", Measurement = AnalysisMeasurement.Known(0) }]
        };
        return (old, current);
    }
    [Theory]
    [MemberData(nameof(IndexedComparisonQueryOrders))]
    public async Task IndexedComparisonQueryMatchesMemoryPredicatesOnBothSidesAndAllOrders(AnalysisMetric metric, AnalysisComparisonSide side, AnalysisComparisonOrder order)
    {
        var (old, current) = QuerySamples();
        await CheckIndexedQueries(old, current, new[] { "", "name=x.bin OR name=renamed.bin", "path:A/ AND size>=7", "allocated>=0", "delta>0 OR change:removed", "modified>=1970-01-01T00:00:00Z AND root=r" }
            .Select(query => new AnalysisComparisonView { Metric = metric, QuerySide = side, Order = order, Query = query }));
    }
    public static IEnumerable<object[]> IndexedComparisonQueryCohorts()
    {
        foreach (var metric in Enum.GetValues<AnalysisMetric>())
            foreach (var cohort in Enum.GetValues<AnalysisComparisonCohort>()) yield return [metric, cohort];
    }
    [Theory]
    [MemberData(nameof(IndexedComparisonQueryCohorts))]
    public async Task IndexedComparisonQueryCohortsAndUncertainRowsKeepWholeMetricEntities(AnalysisMetric metric, AnalysisComparisonCohort cohort)
    {
        var (old, current) = QuerySamples();
        await CheckIndexedQueries(old, current, new[] { false, true }.Select(uncertain => new AnalysisComparisonView
            { Metric = metric, Cohort = cohort, Query = "allocated>=0 OR delta>0", QuerySide = AnalysisComparisonSide.Previous, ShowUnknown = uncertain }));
    }
    private static async Task CheckIndexedQueries(AnalysisSnapshot old, AnalysisSnapshot current, IEnumerable<AnalysisComparisonView> views,
        AnalysisRootDisplayIds? oldIds = null, AnalysisRootDisplayIds? newIds = null)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var requests = views.ToArray(); var metric = requests[0].Metric;
        await using var a = await Build(old, io, enumeration: ComparisonEnumeration(old), arbitraryPathIds: true);
        await using var b = await Build(current, io, enumeration: ComparisonEnumeration(current), arbitraryPathIds: true);
        await using var before = await a.AcquireAsync(Cancellation); await using var now = await b.AcquireAsync(Cancellation);
        old = old with { Freshness = before.Descriptor.Freshness, CoverageDetails = before.Descriptor.CoverageDetails.Observations };
        current = current with { Freshness = now.Descriptor.Freshness, CoverageDetails = now.Descriptor.CoverageDetails.Observations };
        using var expected = AnalysisComparison.Build(old, current, metric, cancellation: Cancellation);
        await using var result = await now.CompareIndexedAsync(before, metric, cancellation: Cancellation);
        foreach (var view in requests)
        {
            Assert.Equal(metric, view.Metric);
            var expectedLease = expected.Retain(); AnalysisComparisonQueryResult expectedQuery;
            try { expectedQuery = AnalysisComparisonQuery.Apply(old, current, view.Metric, expectedLease, view.Query, view.CaseSensitive, view.QuerySide, Cancellation, oldIds, newIds); }
            catch { expectedLease.Dispose(); throw; }
            using var queryOwner = expectedQuery;
            var definite = AnalysisComparisonProjection.Apply(expectedQuery.Matches, view.Cohort, view.Order).ToArray();
            var uncertain = AnalysisComparisonProjection.Apply(expectedQuery.Unknown, view.Cohort, view.Order).ToArray();
            var selected = view.ShowUnknown ? uncertain : definite;
            await using var query = await result.QueryAsync(view, oldIds, newIds, Cancellation);
            Assert.Equal((ulong)definite.Length, query.Definite); Assert.Equal((ulong)uncertain.Length, query.Unknown);
            Assert.Equal((ulong)selected.Length, query.Count); Assert.Equal(result.Taint, query.Taint);
            var observed = new List<(ulong Id, AnalysisComparisonRow Row, string Key)>(); ulong offset = 0;
            while (true)
            {
                var page = await query.ReadAsync(offset, 1, Cancellation);
                foreach (var row in page.Rows)
                {
                    async Task<ulong[]> Members(bool previous, AnalysisComparisonAccountingEntity? entity)
                    {
                        if (entity is null) return [];
                        var members = new List<ulong>(); ulong at = 0;
                        while (true)
                        {
                            var p = await query.ReadMembersAsync(previous, entity.Id, at, 2, Cancellation); members.AddRange(p.Rows.Select(n => n.Id));
                            if (!p.HasMore) break; Assert.True(p.NextOffset > at); at = p.NextOffset;
                        }
                        return members.Order().ToArray();
                    }
                    var left = await Members(true, row.Previous); var right = await Members(false, row.Current);
                    var reference = Assert.Single(selected, r => (r.Previous?.NodeIds ?? []).Order().SequenceEqual(left) && (r.Current?.NodeIds ?? []).Order().SequenceEqual(right));
                    Assert.Equal(reference.Delta, row.Delta); Assert.Equal(reference.Change, row.Change);
                    observed.Add((row.Id, reference, Key(reference)));
                }
                if (!page.HasMore) break; Assert.True(page.NextOffset > offset); offset = page.NextOffset;
            }
            Assert.Equal(selected.Select(Rank), observed.Select(x => Rank(x.Row)));
            Assert.Equal(selected.Select(Key).Order(), observed.Select(x => x.Key).Order());
            for (int at = 1; at < observed.Count; at++) if (Rank(observed[at - 1].Row) == Rank(observed[at].Row)) Assert.True(observed[at - 1].Id < observed[at].Id);
            string Rank(AnalysisComparisonRow r) => view.Order switch
            {
                AnalysisComparisonOrder.Path => r.Current?.Paths[0] ?? r.Previous!.Paths[0],
                AnalysisComparisonOrder.RelativeChange => r.RelativePercent.HasValue + ":" + Math.Abs(r.RelativePercent ?? 0).ToString("R", CultureInfo.InvariantCulture),
                AnalysisComparisonOrder.CurrentAmount => (r.Current?.Value.IsKnown == true) + ":" + (r.Current?.Value.Value ?? 0).ToString(CultureInfo.InvariantCulture),
                _ => (r.Delta?.Magnitude ?? 0).ToString(CultureInfo.InvariantCulture)
            };
            static string Key(AnalysisComparisonRow r) => string.Join(',', (r.Previous?.NodeIds ?? []).Order()) + "|" + string.Join(',', (r.Current?.NodeIds ?? []).Order());
        }
    }

    [Fact]
    public async Task IndexedComparisonQueryRetainsBothInputsAfterOwnerCloseAndReleasesActualReaderPlaces()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); var left = new MemoryDirectory(); var right = new MemoryDirectory();
        var a = await Build(s, io, left, enumeration: ComparisonEnumeration(s)); var b = await Build(s, io, right, enumeration: ComparisonEnumeration(s));
        var before = await a.AcquireAsync(Cancellation); var now = await b.AcquireAsync(Cancellation);
        var result = await now.CompareIndexedAsync(before, AnalysisMetric.OnDisk, cancellation: Cancellation);
        var query = await result.QueryAsync(new() { Metric = AnalysisMetric.OnDisk, Query = "name=y.bin AND allocated=80" }, cancellation: Cancellation);
        try
        {
            await before.DisposeAsync(); await now.DisposeAsync(); await a.DisposeAsync(); await b.DisposeAsync();
            var closing = result.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted); Assert.False(left.Closed); Assert.False(right.Closed);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => result.ReadAsync(cancellation: Cancellation));
            var row = Assert.Single((await query.ReadAsync(cancellation: Cancellation)).Rows); Assert.Equal(2UL, row.Current!.ObservedPaths);
            Assert.Equal(2UL, (await query.ReadMembersAsync(false, row.Current.Id, cancellation: Cancellation)).Total);
            Assert.True((await query.ReadSideAsync(false, row.Current.Id, Cancellation)).DetailsLoaded);
            await query.DisposeAsync(); await closing; Assert.True(left.Closed); Assert.True(right.Closed);
        }
        finally { await query.DisposeAsync(); await result.DisposeAsync(); await before.DisposeAsync(); await now.DisposeAsync(); await a.DisposeAsync(); await b.DisposeAsync(); }
    }
    [Fact]
    public async Task IndexedComparisonReaderCapAndRetainedReaderSurviveOwnerClosureWithoutEarlyReuse()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample();
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s)); await using var before = await a.AcquireAsync(Cancellation);
        var result = await before.CompareIndexedAsync(before, AnalysisMetric.Files, cancellation: Cancellation);
        var readers = Enumerable.Range(0, 8).Select(_ => result.Retain()).ToArray();
        try
        {
            Assert.Throws<InvalidOperationException>(() => result.Retain()); var closing = result.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => result.Retain()); Assert.Equal(3UL, (await readers[0].ReadAsync(cancellation: Cancellation)).Total);
            await readers[7].DisposeAsync(); await using (var replacement = readers[0].Retain()) Assert.Equal(3UL, (await replacement.ReadAsync(cancellation: Cancellation)).Total);
            foreach (var reader in readers) await reader.DisposeAsync(); await closing;
        }
        finally { foreach (var reader in readers) await reader.DisposeAsync(); await result.DisposeAsync(); }
    }
    [Fact]
    public async Task IndexedComparisonQueryFailureCancellationAndSyntaxRefusalKeepAppliedIndexAndAllowRetry()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); var storage = new MemoryDirectory();
        await using var a = await Build(s, io, storage, enumeration: ComparisonEnumeration(s)); await using var before = await a.AcquireAsync(Cancellation);
        await using var result = await before.CompareIndexedAsync(before, AnalysisMetric.UniqueFileSize, cancellation: Cancellation);
        await using var applied = await result.QueryAsync(new() { Metric = AnalysisMetric.UniqueFileSize }, cancellation: Cancellation);
        await Assert.ThrowsAsync<FormatException>(() => DiscardUnexpectedQueryAsync(result.QueryAsync(new() { Metric = AnalysisMetric.UniqueFileSize, Query = "delta>" }, cancellation: Cancellation)));
        storage.BeforeNextRead = () => throw new IOException("query read failure");
        await Assert.ThrowsAsync<IOException>(() => DiscardUnexpectedQueryAsync(result.QueryAsync(new() { Metric = AnalysisMetric.UniqueFileSize, Query = "name=x.bin" }, cancellation: Cancellation)));
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation); storage.BeforeNextRead = cancel.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiscardUnexpectedQueryAsync(result.QueryAsync(new() { Metric = AnalysisMetric.UniqueFileSize, Query = "name=x.bin" }, cancellation: cancel.Token)));
        Assert.Equal(2UL, (await applied.ReadAsync(cancellation: Cancellation)).Total);
        await using var retry = await result.QueryAsync(new() { Metric = AnalysisMetric.UniqueFileSize, Query = "name=y.bin" }, cancellation: Cancellation);
        Assert.Equal(2UL, Assert.Single((await retry.ReadAsync(cancellation: Cancellation)).Rows).Current!.ObservedPaths);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task IndexedComparisonQueryPagesLargeOrdersAndKeepsAnAliasMatchAsOneCompleteCohort(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); const int count = 14000;
        s = s with { Nodes = ImmutableArray.Create(s.Nodes[0]).AddRange(Enumerable.Range(0, count).Select(i =>
            new AnalysisNode((ulong)i + 2, "r", 1, "entry-" + (count - i - 1).ToString("D6"), AnalysisKind.File, "X"))),
            Objects = [s.Objects[0] with { LinkCount = AnalysisMeasurement.Known(count) }], Facts = [s.Facts[0]] };
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s)); await using var before = await a.AcquireAsync(Cancellation);
        await using var result = await before.CompareIndexedAsync(before, metric, cancellation: Cancellation);
        bool shared = metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk;
        await using (var ordered = await result.QueryAsync(new() { Metric = metric, Order = AnalysisComparisonOrder.Path, Query = "size>=0" }, cancellation: Cancellation))
        {
            Assert.Equal(shared ? 1UL : count, ordered.Count); ulong offset = 0, observed = 0;
            while (true)
            {
                var page = await ordered.ReadAsync(offset, 113, Cancellation); Assert.InRange(page.Rows.Length, 1, 113);
                if (!shared && (offset == 0 || !page.HasMore))
                {
                    var row = offset == 0 ? page.Rows[0] : page.Rows[^1];
                    var path = Assert.Single((await ordered.ReadMembersAsync(false, row.Current!.Id, 0, 1, Cancellation)).Rows);
                    Assert.Equal(offset == 0 ? "entry-000000" : "entry-013999", path.Name);
                }
                observed += (ulong)page.Rows.Length; if (!page.HasMore) break; Assert.True(page.NextOffset > offset); offset = page.NextOffset;
            }
            Assert.Equal(ordered.Count, observed);
        }
        await using var selected = await result.QueryAsync(new() { Metric = metric, Query = "name=entry-000000", Order = AnalysisComparisonOrder.RelativeChange }, cancellation: Cancellation);
        var selectedRow = Assert.Single((await selected.ReadAsync(cancellation: Cancellation)).Rows);
        Assert.Equal(shared ? (ulong)count : 1UL, selectedRow.Current!.ObservedPaths);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task IndexedComparisonQueryRawNamesAndCapturedRootDisplayIdsPreserveThreeValuedSemantics(AnalysisMetric metric)
    {
        var s = ComparisonSample(); s = s with { Nodes = s.Nodes.SetItem(5, s.Nodes[5] with { Name = "bad�", NameEncoding = "unix-bytes", RawName = [(byte)'b', (byte)'a', (byte)'d', 0xff] }) };
        var ids = new AnalysisRootDisplayIds(new Dictionary<string, string> { ["r"] = "captured-root" });
        await CheckIndexedQueries(s, s, new[] { false, true }.Select(unknown => new AnalysisComparisonView
            { Metric = metric, Query = "NOT path:bad AND root=captured-root", ShowUnknown = unknown }), ids, ids);
        await CheckIndexedQueries(s, s, [new() { Metric = metric, Query = "size=20 AND root=captured-root", Order = AnalysisComparisonOrder.Path }], ids, ids);
    }

    [Fact]
    public async Task IndexedComparisonQueryParsesOnlyUsedCapturedCalendarsAndStillChecksEmptyResults()
    {
        var old = ComparisonSample(); old = old with { Identity = old.Identity with { TimeZoneId = "unavailable-test-zone" } };
        var current = old with { Identity = old.Identity with { TimeZoneId = "UTC" } };
        await CheckIndexedQueries(old, current, new[] { AnalysisComparisonSide.Current, AnalysisComparisonSide.CurrentOrPrevious }.Select(side => new AnalysisComparisonView
            { Query = "modified>=1970-01-01", QuerySide = side }));
        Ordinary(); using var io = new DeviceIoScheduler();
        await using var a = await Build(old, io, enumeration: ComparisonEnumeration(old)); await using var before = await a.AcquireAsync(Cancellation);
        await using var result = await before.CompareIndexedAsync(before, AnalysisMetric.FileSize, cancellation: Cancellation);
        await Assert.ThrowsAsync<FormatException>(() => DiscardUnexpectedQueryAsync(result.QueryAsync(new() { QuerySide = AnalysisComparisonSide.Previous, Query = "modified>=1970-01-01" }, cancellation: Cancellation)));
        await using var valid = await result.QueryAsync(new() { Query = "modified>=1970-01-01T00:00:00Z" }, cancellation: Cancellation);
        Assert.Equal(3UL, valid.Count);
    }

    [Fact]
    public async Task IndexedComparisonQueryEmptyInputRejectsMalformedSyntaxWithoutConsumingAReaderPlace()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); s = s with { Nodes = s.Nodes.Where(n => n.Kind == AnalysisKind.Directory).ToImmutableArray(), Objects = [], Facts = [] };
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s)); await using var before = await a.AcquireAsync(Cancellation);
        await using var result = await before.CompareIndexedAsync(before, AnalysisMetric.FileSize, cancellation: Cancellation);
        await Assert.ThrowsAsync<FormatException>(() => DiscardUnexpectedQueryAsync(result.QueryAsync(new() { Query = "delta>" }, cancellation: Cancellation)));
        await using var query = await result.QueryAsync(new() { Query = "size>=0", Order = AnalysisComparisonOrder.Path }, cancellation: Cancellation);
        Assert.Equal(0UL, query.Count); Assert.Empty((await query.ReadAsync(cancellation: Cancellation)).Rows);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize, false)]
    [InlineData(AnalysisMetric.FileSize, true)]
    [InlineData(AnalysisMetric.UniqueFileSize, false)]
    [InlineData(AnalysisMetric.UniqueFileSize, true)]
    [InlineData(AnalysisMetric.OnDisk, false)]
    [InlineData(AnalysisMetric.OnDisk, true)]
    public async Task IndexedComparisonQueryKeepsSignedUInt128ThresholdsAndZeroRelativeBaselines(AnalysisMetric metric, bool negative)
    {
        var s = ComparisonSample(); s = s with { Nodes = s.Nodes.Where(n => n.Id != 5).ToImmutableArray(),
            Objects = [s.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(negative ? UInt128.MaxValue : 0), LinkCount = AnalysisMeasurement.Known(1) }, s.Objects[1] with { LogicalLength = AnalysisMeasurement.Known(0) }],
            Facts = [s.Facts[0] with { Measurement = AnalysisMeasurement.Known(negative ? UInt128.MaxValue : 0) }, s.Facts[1] with { Measurement = AnalysisMeasurement.Known(0) }] };
        var current = s with { Objects = s.Objects.SetItem(0, s.Objects[0] with { LogicalLength = AnalysisMeasurement.Known(negative ? 0 : UInt128.MaxValue) }),
            Facts = s.Facts.SetItem(0, s.Facts[0] with { Measurement = AnalysisMeasurement.Known(negative ? 0 : UInt128.MaxValue) }) };
        await CheckIndexedQueries(s, current, new[] { "", "delta=" + (negative ? "-" : "+") + UInt128.MaxValue.ToString(CultureInfo.InvariantCulture) }
            .Select(text => new AnalysisComparisonView { Metric = metric, Query = text, Order = AnalysisComparisonOrder.RelativeChange }));
    }

    [Fact]
    public async Task IndexedComparisonQueryBuildAdmissionWaitsForActualCanceledWorkAndThenAllowsRetry()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); var storage = new MemoryDirectory();
        await using var a = await Build(s, io, storage, enumeration: ComparisonEnumeration(s)); await using var before = await a.AcquireAsync(Cancellation);
        await using var result = await before.CompareIndexedAsync(before, AnalysisMetric.FileSize, cancellation: Cancellation);
        using var gate = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        storage.BeforeNextRead = () => { entered.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(20)); };
        var running = result.QueryAsync(new() { Query = "path:A" }, cancellation: cancel.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            await Assert.ThrowsAsync<InvalidOperationException>(() => DiscardUnexpectedQueryAsync(result.QueryAsync(new() { Query = "size>=0" }, cancellation: Cancellation)));
            cancel.Cancel(); Assert.False(running.IsCompleted); gate.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiscardUnexpectedQueryAsync(running));
            await using var retry = await result.QueryAsync(new(), cancellation: Cancellation); Assert.Equal(3UL, retry.Count);
        }
        finally { gate.Set(); try { await using var cleanup = await running; } catch (OperationCanceledException) { } }
    }

    [Fact]
    public async Task IndexedComparisonQueryOversizedDisplayedPathRefusesExactOrderingAndKeepsTheNumericView()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample();
        var raw = ImmutableArray.CreateRange(Enumerable.Repeat((byte)0xff, AnalysisLimits.RawNameBytes));
        s = s with { Nodes = [s.Nodes[0], s.Nodes[3] with { Id = 2, ParentId = 1, Name = new string('�', AnalysisLimits.RawNameBytes), NameEncoding = "unix-bytes", RawName = raw }],
            Objects = [s.Objects[0] with { LinkCount = AnalysisMeasurement.Known(1) }], Facts = [s.Facts[0]] };
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s)); await using var before = await a.AcquireAsync(Cancellation);
        await using var result = await before.CompareIndexedAsync(before, AnalysisMetric.FileSize, cancellation: Cancellation);
        await using var applied = await result.QueryAsync(new(), cancellation: Cancellation);
        await Assert.ThrowsAsync<IOException>(() => DiscardUnexpectedQueryAsync(result.QueryAsync(new() { Order = AnalysisComparisonOrder.Path }, cancellation: Cancellation)));
        Assert.Single((await applied.ReadAsync(cancellation: Cancellation)).Rows);
        await using var retry = await result.QueryAsync(new() { Query = "size=100" }, cancellation: Cancellation); Assert.Equal(1UL, retry.Count);
    }
    private static async Task DiscardUnexpectedQueryAsync(Task<AnalysisIndexedComparison.Query> pending)
    { await using var unexpected = await pending; }
}
