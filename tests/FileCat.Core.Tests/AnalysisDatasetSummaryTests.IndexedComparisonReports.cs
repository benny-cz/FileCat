using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    public static IEnumerable<object[]> IndexedComparisonReportCases()
    {
        foreach (var metric in Enum.GetValues<AnalysisMetric>()) foreach (var format in Enum.GetValues<AnalysisReportFormat>())
            foreach (bool redact in new[] { false, true }) yield return [metric, format, redact];
    }
    private sealed class ComparisonReportDestination : AnalysisExportDestination
    {
        private readonly MemoryStream _stream = new();
        internal byte[] Bytes = [];
        internal override Stream Stream => _stream;
        internal override void Verify() { }
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1 << 50, (UInt128)1 << 50);
        internal override void FlushDurable() { }
        internal override void PublishNew(string name) => Bytes = _stream.ToArray();
        public override void Dispose() => _stream.Dispose();
    }
    private static async Task<string> ComparisonReportText(AnalysisCapturedIndexedComparisonReport report, bool redact, CancellationToken token)
    {
        string name = report.Format == AnalysisReportFormat.Html ? "report.html" : report.Format == AnalysisReportFormat.Csv ? "report.csv" : "report.md";
        var output = new ComparisonReportDestination(); var receipt = await report.ExportNewAsync(name, "comparison-report", () => output, redact, token);
        Assert.Equal(output.Bytes.LongLength, receipt.Bytes); return Encoding.UTF8.GetString(output.Bytes);
    }
    [Theory]
    [MemberData(nameof(IndexedComparisonReportCases))]
    public async Task IndexedComparisonReportsRetainAppliedRowsAndKeepWholeAliasesSafeTextAndTaint(AnalysisMetric metric, AnalysisReportFormat format, bool redact)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var (old, current) = QuerySamples();
        old = old with { Nodes = old.Nodes.Select(n => n.Id == 4 ? n with { Name = "=PRIVATE<script>\u202e.csv" } : n).ToImmutableArray() };
        current = current with { Nodes = current.Nodes.Select(n => n.Id == 4 ? n with { Name = "=PRIVATE<script>\u202e.csv" } : n).ToImmutableArray() };
        await using var a = await Build(old, io, enumeration: ComparisonEnumeration(old), arbitraryPathIds: true);
        await using var b = await Build(current, io, enumeration: ComparisonEnumeration(current), arbitraryPathIds: true);
        await using var before = await a.AcquireAsync(Cancellation); await using var now = await b.AcquireAsync(Cancellation);
        await using var result = await now.CompareIndexedAsync(before, metric, cancellation: Cancellation);
        var view = new AnalysisComparisonView { Metric = metric, Query = "name:PRIVATE", QuerySide = AnalysisComparisonSide.Either, Order = AnalysisComparisonOrder.Path };
        await using var query = await result.QueryAsync(view, cancellation: Cancellation);
        await using var report = await result.CaptureReportAsync(view, format, query, cancellation: Cancellation);
        Assert.Equal(1UL, report.Rows); Assert.Equal(result.Taint, report.Taint);
        Assert.True(report.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
        await query.DisposeAsync(); await before.DisposeAsync(); await now.DisposeAsync(); await a.DisposeAsync(); await b.DisposeAsync();
        var closing = result.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
        string text = await ComparisonReportText(report, redact, Cancellation);
        Assert.Contains("Declared item rows: 1", text); Assert.Contains("Full input totals", text); Assert.Contains("imported, unverified", text);
        Assert.Contains("Attribution movement is not", text); Assert.Contains("Unknown values are not zero", text);
        Assert.DoesNotContain('\u202e', text); Assert.DoesNotContain("device:X", text);
        if (redact) { Assert.DoesNotContain("PRIVATE", text); Assert.DoesNotContain("x.bin", text); Assert.DoesNotContain("y.bin", text); }
        else
        {
            Assert.Contains("PRIVATE", text);
            if (metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk)
            { Assert.Contains(format == AnalysisReportFormat.Markdown ? @"y\.bin" : "y.bin", text); Assert.Contains(format == AnalysisReportFormat.Markdown ? @"renamed\.bin" : "renamed.bin", text); }
            if (format == AnalysisReportFormat.Html) { Assert.DoesNotContain("<script>", text); Assert.Contains("&lt;script&gt;", text); Assert.DoesNotContain("href=", text); }
            if (format == AnalysisReportFormat.Markdown) Assert.Contains("\\<script\\>", text);
            if (format == AnalysisReportFormat.Csv) Assert.Contains("PRIVATE<script>", text);
        }
        await report.DisposeAsync(); await closing;
    }
    [Theory]
    [InlineData(AnalysisMetric.FileSize)] [InlineData(AnalysisMetric.UniqueFileSize)] [InlineData(AnalysisMetric.OnDisk)] [InlineData(AnalysisMetric.Files)]
    public async Task IndexedComparisonReportsEntireScopeIgnoresQueryAndKeepsAllRows(AnalysisMetric metric)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var (old, current) = QuerySamples();
        await using var a = await Build(old, io, enumeration: ComparisonEnumeration(old), arbitraryPathIds: true); await using var b = await Build(current, io, enumeration: ComparisonEnumeration(current), arbitraryPathIds: true);
        await using var before = await a.AcquireAsync(Cancellation); await using var now = await b.AcquireAsync(Cancellation);
        await using var result = await now.CompareIndexedAsync(before, metric, cancellation: Cancellation);
        var view = new AnalysisComparisonView { Metric = metric, Query = "name:never-matches", Cohort = AnalysisComparisonCohort.Removed, Order = AnalysisComparisonOrder.CurrentAmount };
        await using var query = await result.QueryAsync(view, cancellation: Cancellation); Assert.Equal(0UL, query.Count);
        await using var filtered = await result.CaptureReportAsync(view, AnalysisReportFormat.Csv, query, cancellation: Cancellation); Assert.Equal(0UL, filtered.Rows);
        await using var entire = await result.CaptureReportAsync(view, AnalysisReportFormat.Csv, query, true, Cancellation); Assert.Equal(result.Count, entire.Rows);
        string text = await ComparisonReportText(entire, true, Cancellation); Assert.Contains("Entire unfiltered comparison", text); Assert.DoesNotContain("never-matches", text);
        Assert.Equal((int)result.Count, text.Split('\n').Count(line => line.StartsWith("\"item\",", StringComparison.Ordinal)));
    }
    [Fact]
    public async Task IndexedComparisonReportCaptureRejectsMismatchedViewAndKeepsTheQueryForRetry()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample();
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s)); await using var input = await a.AcquireAsync(Cancellation);
        await using var result = await input.CompareIndexedAsync(input, AnalysisMetric.UniqueFileSize, cancellation: Cancellation);
        var view = new AnalysisComparisonView { Metric = AnalysisMetric.UniqueFileSize, Query = "name=x.bin" };
        await using var query = await result.QueryAsync(view, cancellation: Cancellation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DiscardUnexpectedReport(result.CaptureReportAsync(view with { Query = "name=z.bin" }, AnalysisReportFormat.Csv, query, cancellation: Cancellation)));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DiscardUnexpectedReport(result.CaptureReportAsync(view, AnalysisReportFormat.Csv, query, cancellation: cancel.Token)));
        var reports = new List<AnalysisCapturedIndexedComparisonReport>();
        try
        {
            for (int i = 0; i < 7; i++) reports.Add(await result.CaptureReportAsync(view, AnalysisReportFormat.Csv, query, cancellation: Cancellation));
            await Assert.ThrowsAsync<InvalidOperationException>(() => DiscardUnexpectedReport(result.CaptureReportAsync(view, AnalysisReportFormat.Csv, query, cancellation: Cancellation)));
            await reports[^1].DisposeAsync(); reports.RemoveAt(reports.Count - 1);
            await using var retry = await result.CaptureReportAsync(view with { Chart = AnalysisComparisonChartMode.Sunburst }, AnalysisReportFormat.Csv, query, cancellation: Cancellation);
            Assert.Equal(1UL, retry.Rows); Assert.Equal(1UL, query.Count);
        }
        finally { foreach (var report in reports) await report.DisposeAsync(); }
    }
    private static async Task DiscardUnexpectedReport(Task<AnalysisCapturedIndexedComparisonReport> request) { await using var report = await request; }
    [Fact]
    public async Task IndexedComparisonReportBoundsLongDisplayedAliasCellsWithoutSplittingTheAccountingRow()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample();
        s = s with { Nodes = ImmutableArray.Create(s.Nodes[0]).AddRange(Enumerable.Range(0, 40).Select(i => new AnalysisNode((ulong)i + 10, "r", 1, new string('x', 4096) + i, AnalysisKind.File, "X"))),
            Objects = [s.Objects[0] with { LinkCount = AnalysisMeasurement.Known(40) }], Facts = [s.Facts[0]] };
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s), arbitraryPathIds: true); await using var input = await a.AcquireAsync(Cancellation);
        await using var result = await input.CompareIndexedAsync(input, AnalysisMetric.UniqueFileSize, cancellation: Cancellation);
        await using var report = await result.CaptureReportAsync(new() { Metric = AnalysisMetric.UniqueFileSize }, AnalysisReportFormat.Csv, cancellation: Cancellation);
        string text = await ComparisonReportText(report, false, Cancellation);
        var match = System.Text.RegularExpressions.Regex.Match(text, @"first (\d+) of 40 displayed aliases");
        Assert.True(match.Success); Assert.InRange(int.Parse(match.Groups[1].Value), 1, 31); Assert.InRange(text.Length, 1, 64 * 1024); Assert.Equal(1UL, report.Rows);
    }
    [Fact]
    public async Task IndexedComparisonReportsStreamMoreThanHumanDetailLimitAndBoundAliasCells()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = ComparisonSample(); const int count = 5201;
        s = s with { Nodes = ImmutableArray.Create(s.Nodes[0]).AddRange(Enumerable.Range(0, count).Select(i => new AnalysisNode((ulong)i + 10, "r", 1, "alias-" + i, AnalysisKind.File, "X"))),
            Objects = [s.Objects[0] with { LinkCount = AnalysisMeasurement.Known(count) }], Facts = [s.Facts[0]] };
        await using var a = await Build(s, io, enumeration: ComparisonEnumeration(s), arbitraryPathIds: true); await using var input = await a.AcquireAsync(Cancellation);
        await using (var result = await input.CompareIndexedAsync(input, AnalysisMetric.FileSize, cancellation: Cancellation))
        {
            await using var human = await result.CaptureReportAsync(new(), AnalysisReportFormat.Html, cancellation: Cancellation);
            await using var csv = await result.CaptureReportAsync(new(), AnalysisReportFormat.Csv, cancellation: Cancellation);
            Assert.Equal(5000UL, human.Rows); Assert.True(human.DetailLimited); Assert.Equal((ulong)count, csv.Rows); Assert.False(csv.DetailLimited);
            string text = await ComparisonReportText(csv, true, Cancellation); Assert.Equal(count, text.Split('\n').Count(line => line.StartsWith("\"item\",", StringComparison.Ordinal)));
        }
        await using var objects = await input.CompareIndexedAsync(input, AnalysisMetric.UniqueFileSize, cancellation: Cancellation);
        await using var aliases = await objects.CaptureReportAsync(new() { Metric = AnalysisMetric.UniqueFileSize }, AnalysisReportFormat.Csv, cancellation: Cancellation);
        string all = await ComparisonReportText(aliases, false, Cancellation); Assert.Contains("first 32 of 5201 displayed aliases", all); Assert.Equal(1UL, aliases.Rows);
    }
}
