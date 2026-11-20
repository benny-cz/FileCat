using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;
using FileCat.Core.Analysis;
using Microsoft.VisualBasic.FileIO;

namespace FileCat.Core.Tests;

public sealed class AnalysisHumanReportTests
{
    [Theory]
    [InlineData(AnalysisReportFormat.Markdown, false)] [InlineData(AnalysisReportFormat.Markdown, true)]
    [InlineData(AnalysisReportFormat.Html, false)] [InlineData(AnalysisReportFormat.Html, true)]
    [InlineData(AnalysisReportFormat.Csv, false)] [InlineData(AnalysisReportFormat.Csv, true)]
    public async Task CapturedFilterAndProviderBasisContextEscapesLabelsAndOmitsAllRedactedSourceText(AnalysisReportFormat format, bool redacted)
    {
        var fixture = Fixture("name");
        var header = fixture.Header with { FilterContext = new("name:PRIVATEQUERY<script>", true, true, true),
            MeasurementBases = new(["PRIVATEBASIS<svg/onload=1>"], true), DetailedAllocation = true, FilterTimeZone = "PRIVATEZONE<svg/onload=1>" };
        string text = await Write(header, [fixture.Row], format, redacted);
        foreach (string marker in new[] { "PRIVATEQUERY", "PRIVATEBASIS", "PRIVATEZONE" })
            if (redacted) Assert.DoesNotContain(marker, text); else Assert.Contains(marker, text);
        Assert.Contains("case sensitive: yes", text); Assert.Contains("Detailed allocation policy", text); Assert.Contains("Basis label detail is limited", text);
        if (format == AnalysisReportFormat.Html) { Assert.DoesNotContain("<script>", text); Assert.DoesNotContain("<svg/", text); }
        if (format == AnalysisReportFormat.Csv) Assert.All(ParseCsv(text), row => Assert.Equal(23, row.Length));
    }
    [Theory]
    [InlineData(AnalysisReportFormat.Markdown, false)] [InlineData(AnalysisReportFormat.Markdown, true)]
    [InlineData(AnalysisReportFormat.Html, false)] [InlineData(AnalysisReportFormat.Html, true)]
    [InlineData(AnalysisReportFormat.Csv, false)] [InlineData(AnalysisReportFormat.Csv, true)]
    public async Task HumanFormatsEscapePoisonedLabelsKeepExactNumbersAndOmitEveryRedactedLocator(AnalysisReportFormat format, bool redacted)
    {
        var fixture = Fixture("=PRIVATENAME<svg/onload=alert(1)>[link](https://example.invalid/a)\u202E,\n\"end");
        string text = await Write(fixture.Header, [fixture.Row], format, redacted);
        foreach (string marker in new[] { "PRIVATESCOPE", "PRIVATENAME", "PRIVATELOCATION" })
            if (redacted) Assert.DoesNotContain(marker, text); else Assert.Contains(marker, text);
        Assert.DoesNotContain("IDENTITYCANARY", text); Assert.DoesNotContain('\u202E', text);
        Assert.Contains(UInt128.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), text);
        Assert.Contains("PARTIAL OBSERVATIONS", text);
        if (format == AnalysisReportFormat.Html)
        { Assert.DoesNotContain("<svg", text); Assert.DoesNotContain("<script", text); Assert.DoesNotContain("href=", text); Assert.DoesNotContain("data-", text); }
        else if (format == AnalysisReportFormat.Markdown && !redacted)
        { Assert.Contains("\\<svg", text); Assert.Contains("\\[link\\]\\(https://", text); }
        else if (format == AnalysisReportFormat.Csv)
        {
            var parsed = ParseCsv(text); Assert.Equal(3, parsed.Count); Assert.All(parsed, row => Assert.Equal(parsed[0].Length, row.Length));
            Assert.Equal("metadata", parsed[1][0]); Assert.Equal("item", parsed[2][0]); Assert.Equal("1", parsed[2][21]);
            Assert.Equal(redacted ? "true" : "false", parsed[2][19]); Assert.Equal("true", parsed[2][20]);
            Assert.Equal(UInt128.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), parsed[2][7]);
            if (!redacted) Assert.StartsWith("'=PRIVATENAME", parsed[2][4]);
        }
    }

    [Theory]
    [InlineData("=SUMCANARY(1)")] [InlineData(" +SUMCANARY")] [InlineData("-SUMCANARY")]
    [InlineData("@SUMCANARY")] [InlineData("\t=SUMCANARY")] [InlineData("\r=SUMCANARY")]
    public async Task SpreadsheetCsvKeepsFormulaLeadingSourceTextInertAfterActualCsvParsing(string name)
    {
        var fixture = Fixture(name); var parsed = ParseCsv(await Write(fixture.Header, [fixture.Row], AnalysisReportFormat.Csv, false));
        string value = parsed[2][4]; Assert.Contains("SUMCANARY", value);
        Assert.DoesNotContain(value.TrimStart()[0], new[] { '=', '+', '-', '@', '\t', '\r', '\n' });
        Assert.Contains("apostrophe", parsed[1][22]);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task DeclaredScopeMismatchCannotBecomeACompleteReport(bool extra)
    {
        var fixture = Fixture("row"); using var stream = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisHumanReport.WriteAsync(stream, fixture.Header,
            Rows(extra ? [fixture.Row, fixture.Row] : []), AnalysisReportFormat.Html, false, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("rows")] [InlineData("human-detail")] [InlineData("partial-csv")] [InlineData("scope")]
    public async Task FormatLimitsRefuseUnboundedOrIncompleteFullOutputs(string fault)
    {
        var fixture = Fixture("row"); var header = fault switch
        {
            "rows" => fixture.Header with { Rows = AnalysisLimits.Observations + 1UL },
            "human-detail" => fixture.Header with { Rows = AnalysisLimits.ChartLeaves + 1UL },
            "partial-csv" => fixture.Header with { DetailLimited = true },
            _ => fixture.Header with { ScopeLabel = new string('x', AnalysisLimits.LocatorBytes + 1) }
        };
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisHumanReport.WriteAsync(stream, header, Rows([]),
            fault is "rows" or "partial-csv" ? AnalysisReportFormat.Csv : AnalysisReportFormat.Html, true, TestContext.Current.CancellationToken));
        Assert.Equal(0, stream.Length);
    }

    [Theory]
    [InlineData(AnalysisMetric.FileSize)] [InlineData(AnalysisMetric.UniqueFileSize)] [InlineData(AnalysisMetric.OnDisk)] [InlineData(AnalysisMetric.Files)]
    public async Task NamespaceBoundaryRowsNeverExportAsMeasuredZeroTargets(AnalysisMetric metric)
    {
        var fixture = Fixture("link"); var boundary = fixture.Row with { Path = fixture.Row.Path with { Kind = AnalysisKind.Link } };
        var header = fixture.Header with { Metric = metric };
        string html = await Write(header, [boundary], AnalysisReportFormat.Html, false);
        Assert.Contains(metric == AnalysisMetric.Files ? "Not counted" : "Not measured", html);
        var parsed = ParseCsv(await Write(header, [boundary], AnalysisReportFormat.Csv, false));
        Assert.Equal("", parsed[2][7]); Assert.Equal("Not measured", parsed[2][9]); Assert.Equal("", parsed[2][11]); Assert.Equal("", parsed[2][13]);
        var zero = fixture.Row with { Totals = fixture.Row.Totals with { Known = 0 }, Logical = fixture.Row.Logical with { KnownBytes = 0 } };
        parsed = ParseCsv(await Write(header, [zero], AnalysisReportFormat.Csv, false)); Assert.Equal("0", parsed[2][7]); Assert.Equal("0", parsed[2][11]); Assert.Equal("1", parsed[2][13]);
    }

    [Fact]
    public async Task EmptyCsvStillDeclaresScopeTimeCoverageAndExactSummary()
    {
        var fixture = Fixture("row"); var parsed = ParseCsv(await Write(fixture.Header with { Rows = 0 }, [], AnalysisReportFormat.Csv, true));
        Assert.Equal(2, parsed.Count); Assert.Equal("metadata", parsed[1][0]); Assert.Equal("Entire unfiltered result", parsed[1][2]);
        Assert.Contains("PARTIAL OBSERVATIONS", parsed[1][17]); Assert.Contains("1970", parsed[1][18]); Assert.Equal("0", parsed[1][21]);
        Assert.Equal(UInt128.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture), parsed[1][7]);
    }

    [Fact]
    public async Task CancellationWaitsForTheActualRowProducerAndItsEnumeratorRelease()
    {
        var fixture = Fixture("row"); using var stop = new CancellationTokenSource(); using var output = new MemoryStream();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); bool closed = false;
        var pending = AnalysisHumanReport.WriteAsync(output, fixture.Header, Blocked(stop.Token), AnalysisReportFormat.Html, true, stop.Token);
        await entered.Task; stop.Cancel(); Assert.False(pending.IsCompleted); Assert.False(closed); release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.True(closed);
        async IAsyncEnumerable<AnalysisReportRow> Blocked([EnumeratorCancellation] CancellationToken cancellation)
        { try { entered.TrySetResult(); await release.Task; yield return fixture.Row; cancellation.ThrowIfCancellationRequested(); } finally { closed = true; } }
    }

    private static (AnalysisReportHeader Header, AnalysisReportRow Row) Fixture(string name)
    {
        var missing = ImmutableDictionary<MeasurementState, ulong>.Empty;
        var totals = new AnalysisTotals(UInt128.MaxValue, 0, 1, missing.Add(MeasurementState.Unsupported, 1), true);
        var logical = new AnalysisLogicalTotals(UInt128.MaxValue, 1, missing);
        var path = new AnalysisPathMetadata(42, "IDENTITYCANARY", 0, name, AnalysisKind.File, "IDENTITYCANARY", null, null, null,
            MeasurementState.Known, true, "unix-bytes", [.. Encoding.UTF8.GetBytes(name)], null, "IDENTITYCANARY");
        return (new(AnalysisMetric.OnDisk, AnalysisReportScope.EntireResult, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            NamespaceCoverage.Partial, AnalysisFreshness.PossiblyStale, AnalysisTaint.OrdinarySensitive | AnalysisTaint.ImportedUnverified,
            totals, logical, "PRIVATESCOPE", 1), new(path, "PRIVATELOCATION/" + name, totals, logical, true, true));
    }
    private static async Task<string> Write(AnalysisReportHeader header, AnalysisReportRow[] rows, AnalysisReportFormat format, bool redacted)
    {
        using var stream = new MemoryStream(); await AnalysisHumanReport.WriteAsync(stream, header, Rows(rows), format, redacted, TestContext.Current.CancellationToken);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
    private static async IAsyncEnumerable<AnalysisReportRow> Rows(AnalysisReportRow[] rows)
    { await Task.CompletedTask; foreach (var row in rows) yield return row; }
    private static List<string[]> ParseCsv(string text)
    {
        using var reader = new TextFieldParser(new StringReader(text)) { TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        reader.SetDelimiters(","); var result = new List<string[]>();
        while (!reader.EndOfData) result.Add(reader.ReadFields()!); return result;
    }
}
