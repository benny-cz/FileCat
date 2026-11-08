using System.Collections.Immutable;
using System.Text;
using Avalonia.Headless.XUnit;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisReportFormat.Markdown, false)] [InlineData(AnalysisReportFormat.Markdown, true)]
    [InlineData(AnalysisReportFormat.Html, false)] [InlineData(AnalysisReportFormat.Html, true)]
    [InlineData(AnalysisReportFormat.Csv, false)] [InlineData(AnalysisReportFormat.Csv, true)]
    public async Task NativeHumanReportsStreamOnlyTheirDeclaredScopeWithSafeTextAndCompleteRedaction(AnalysisReportFormat format, bool redacted)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string folder = Path.Combine(profile.Root, "report-output"); Directory.CreateDirectory(folder);
        string name = "report" + (format == AnalysisReportFormat.Html ? ".html" : format == AnalysisReportFormat.Csv ? ".csv" : ".md");
        var fixture = HumanReportFixture(); int handles = AnalysisNativeHandles.Count;
        var receipt = await AnalysisHumanReport.ExportNewAsync(profile.Services.Io, PathUtil.GetDeviceKey(folder), name,
            () => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "report-staging")),
            fixture.Header, HumanReportRows(fixture.Row), format, redacted, token);
        string path = Path.Combine(folder, receipt.Name); byte[] bytes = File.ReadAllBytes(path); string text = Encoding.UTF8.GetString(bytes);
        Assert.Equal(bytes.LongLength, receipt.Bytes); Assert.Equal(handles, AnalysisNativeHandles.Count); Assert.Contains("PARTIAL OBSERVATIONS", text);
        foreach (string marker in new[] { "PRIVATEROW", "PRIVATEPATH", "PRIVATESCOPE" })
            if (redacted) Assert.DoesNotContain(marker, text); else Assert.Contains(marker, text);
        Assert.DoesNotContain("IDENTITYCANARY", text); Assert.DoesNotContain('\u202E', text);
        if (format == AnalysisReportFormat.Html) { Assert.DoesNotContain("<script>", text); Assert.DoesNotContain("href=", text); }
        if (format == AnalysisReportFormat.Csv && !redacted) Assert.Contains("\"'=PRIVATEROW", text);
        Assert.Equal(path, Assert.Single(Directory.GetFiles(folder)));
        if (Directory.Exists(Path.Combine(profile.Root, "report-staging"))) Assert.Empty(Directory.GetFiles(Path.Combine(profile.Root, "report-staging")));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeHumanReportScopeMismatchCannotPublishAPartialOutput(bool extra)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string folder = Path.Combine(profile.Root, "report-output"); Directory.CreateDirectory(folder);
        string neighbor = Path.Combine(folder, "keep.html"); File.WriteAllText(neighbor, "KEEP_CANARY");
        var fixture = HumanReportFixture(); int handles = AnalysisNativeHandles.Count;
        var work = AnalysisHumanReport.ExportNewAsync(profile.Services.Io, PathUtil.GetDeviceKey(folder), "rejected.html",
            () => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "report-staging")),
            fixture.Header, HumanReportRows(extra ? [fixture.Row, fixture.Row] : []), AnalysisReportFormat.Html, true, token);
        await Assert.ThrowsAsync<InvalidDataException>(() => work);
        Assert.Equal(handles, AnalysisNativeHandles.Count); Assert.Equal("KEEP_CANARY", File.ReadAllText(neighbor)); Assert.Equal(neighbor, Assert.Single(Directory.GetFiles(folder)));
        if (Directory.Exists(Path.Combine(profile.Root, "report-staging"))) Assert.Empty(Directory.GetFiles(Path.Combine(profile.Root, "report-staging")));
    }

    private static (AnalysisReportHeader Header, AnalysisReportRow Row) HumanReportFixture()
    {
        const string name = "=PRIVATEROW<script>\u202E,\"quoted";
        var states = ImmutableDictionary<MeasurementState, ulong>.Empty;
        var totals = new AnalysisTotals(20, 0, 1, states.Add(MeasurementState.Unsupported, 1), true); var logical = new AnalysisLogicalTotals(20, 1, states);
        var path = new AnalysisPathMetadata(42, "IDENTITYCANARY", 0, name, AnalysisKind.File, "IDENTITYCANARY", null, null, null,
            MeasurementState.Known, true, "unix-bytes", [.. Encoding.UTF8.GetBytes(name)], null, "IDENTITYCANARY");
        return (new(AnalysisMetric.OnDisk, AnalysisReportScope.SelectedRows, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            NamespaceCoverage.Partial, AnalysisFreshness.PossiblyStale, AnalysisTaint.OrdinarySensitive | AnalysisTaint.ImportedUnverified,
            totals, logical, "PRIVATESCOPE", 1), new(path, "PRIVATEPATH/" + name, totals, logical, true, false));
    }
    private static async IAsyncEnumerable<AnalysisReportRow> HumanReportRows(params AnalysisReportRow[] rows)
    { await Task.CompletedTask; foreach (var row in rows) yield return row; }
}
