using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task NativeIndexedComparisonOutputsPreflightCoversRetainedCsvAndSvg()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true);
        var token = TestContext.Current.CancellationToken; await using var result = await source.Lease!.CompareIndexedAsync(source.Lease, AnalysisMetric.UniqueFileSize, cancellation: token);
        var view = new AnalysisComparisonView { Metric = AnalysisMetric.UniqueFileSize, Query = "name:match-target" };
        await using var query = await result.QueryAsync(view, cancellation: token);
        await using var report = await result.CaptureReportAsync(view, AnalysisReportFormat.Csv, query, cancellation: token);
        await using var owner = await result.BuildChartAsync(false, token); var data = await owner.LoadAsync(cancellation: token);
        await using var captured = await owner.CaptureScopeAsync(data, token); var chart = new TreemapControl(); chart.SetView(data, data.Scope, null, [], AnalysisColorDimension.Growth);
        await using var image = await AnalysisCapturedChart.CaptureComparisonAsync(profile.Services.Io, captured, chart, AnalysisChartFormat.Svg, 960, 720, true, token);
        string folder = Path.Combine(source.Root, "preflight-output"); Directory.CreateDirectory(folder);
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token)) await prepared.ExportComparisonReportAsync(report, folder, "Comparison.csv", true, token);
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token)) await prepared.ExportChartAsync(image, folder, "Comparison.svg", true, token);
        Assert.Contains("Declared item rows: 1", File.ReadAllText(Path.Combine(folder, "Comparison.csv")));
        Assert.Contains("Current recorded amounts", File.ReadAllText(Path.Combine(folder, "Comparison.svg")));
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }
    [AvaloniaFact]
    public async Task NativeIndexedComparisonReportCancellationWaitsForActualOutputAndInputClosure()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(); var token = TestContext.Current.CancellationToken;
        await using var result = await source.Lease!.CompareIndexedAsync(source.Lease, AnalysisMetric.FileSize, cancellation: token);
        var report = await result.CaptureReportAsync(new(), AnalysisReportFormat.Csv, cancellation: token); await source.ReleaseInitialLeaseAsync();
        var resultClose = result.DisposeAsync().AsTask(); Assert.False(resultClose.IsCompleted);
        string folder = Path.Combine(source.Root, "canceled-report"); Directory.CreateDirectory(folder);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var release = new ManualResetEventSlim();
        var exporting = report.ExportNewAsync("Canceled.csv", PathUtil.GetDeviceKey(folder), () => new ExportFaultDestination(
            OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "indexed-report-cancel-stage")), "cancel", entered, release), true, token);
        Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20), token); closing = report.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted); Assert.False(exporting.IsCompleted); Assert.False(resultClose.IsCompleted); release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporting); await closing; await resultClose; Assert.Empty(Directory.GetFiles(folder));
        }
        finally
        {
            release.Set(); try { await exporting; } catch (Exception error) when (error is IOException or OperationCanceledException) { }
            await (closing ?? report.DisposeAsync().AsTask()); await resultClose;
        }
        Assert.Empty(Directory.GetFiles(source.PrivatePath)); Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }
    [AvaloniaTheory]
    [InlineData(AnalysisReportFormat.Markdown, false, false)] [InlineData(AnalysisReportFormat.Markdown, true, false)]
    [InlineData(AnalysisReportFormat.Html, false, false)] [InlineData(AnalysisReportFormat.Html, true, false)]
    [InlineData(AnalysisReportFormat.Csv, false, false)] [InlineData(AnalysisReportFormat.Csv, true, false)]
    [InlineData(AnalysisReportFormat.Markdown, false, true)] [InlineData(AnalysisReportFormat.Markdown, true, true)]
    [InlineData(AnalysisReportFormat.Html, false, true)] [InlineData(AnalysisReportFormat.Html, true, true)]
    [InlineData(AnalysisReportFormat.Csv, false, true)] [InlineData(AnalysisReportFormat.Csv, true, true)]
    public async Task NativeIndexedComparisonReportExportsRetainedOfflineInputsAndPreservesExistingOutputs(AnalysisReportFormat format, bool redact, bool entire)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true);
        var token = TestContext.Current.CancellationToken;
        await using var result = await source.Lease!.CompareIndexedAsync(source.Lease, AnalysisMetric.UniqueFileSize, cancellation: token);
        var view = new AnalysisComparisonView { Metric = AnalysisMetric.UniqueFileSize, Query = "name:match-target", Order = AnalysisComparisonOrder.Path };
        await using var query = await result.QueryAsync(view, cancellation: token);
        await using var report = await result.CaptureReportAsync(view, format, query, entire, token);
        Assert.Equal(entire ? result.Count : 1UL, report.Rows); await query.DisposeAsync(); await source.ReleaseInitialLeaseAsync();
        var closing = result.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
        string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder);
        string original = Path.Combine(source.Root, "source"), offline = Path.Combine(source.Root, "source-offline"); Directory.Move(original, offline);
        string name = "Comparison" + (format == AnalysisReportFormat.Csv ? ".csv" : format == AnalysisReportFormat.Html ? ".html" : ".md");
        try
        {
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
                await prepared.ExportComparisonReportAsync(report, folder, name, redact, token);
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, name)); string text = Encoding.UTF8.GetString(bytes);
            Assert.Contains("Previous observations UTC", text); Assert.Contains("Full input totals", text); Assert.Contains("not zero", text);
            Assert.Contains(entire ? "Entire unfiltered comparison" : "Captured filtered comparison", text);
            Assert.DoesNotContain('\u202e', text);
            string pathText = format == AnalysisReportFormat.Markdown
                ? text.Replace(@"\\", @"\", StringComparison.Ordinal).Replace(@"\-", "-", StringComparison.Ordinal).Replace(@"\.", ".", StringComparison.Ordinal)
                    .Replace(@"\_", "_", StringComparison.Ordinal)
                : text;
            if (redact) { Assert.DoesNotContain("match-target", pathText); Assert.DoesNotContain("alias.jpg", pathText); Assert.DoesNotContain(source.Root, pathText); }
            else
            {
                Assert.Contains("match-target.bin", pathText); Assert.Contains("alias.jpg", pathText);
                if (format == AnalysisReportFormat.Markdown) { Assert.Contains(@"match\-target\.bin", text); Assert.Contains(@"alias\.jpg", text); }
            }
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
                await Assert.ThrowsAsync<AnalysisExportTargetExistsException>(() => prepared.ExportComparisonReportAsync(report, folder, name, redact, token));
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(folder, name))); Assert.False(Directory.Exists(original));
            RetainIndexedComparisonOutput(Path.Combine(folder, name), $"report-{format}-{redact}-{entire}" + Path.GetExtension(name));
            await report.DisposeAsync(); await closing;
        }
        finally { Directory.Move(offline, original); }
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath)); Assert.Empty(Directory.GetFiles(source.PrivatePath));
    }
    public static IEnumerable<object[]> IndexedComparisonImageCases()
    {
        foreach (bool png in new[] { false, true }) foreach (bool previous in new[] { false, true })
            foreach (bool sunburst in new[] { false, true }) foreach (bool redact in new[] { false, true }) yield return [png, previous, sunburst, redact];
    }
    [AvaloniaTheory]
    [MemberData(nameof(IndexedComparisonImageCases))]
    public async Task NativeIndexedComparisonImagesBindBothInputsScopeAndPrivacyAfterOriginalOwnersClose(bool png, bool previous, bool sunburst, bool redact)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await IndexedFixture.CreateAsync(sharedAlias: true); var token = TestContext.Current.CancellationToken;
        await using var result = await source.Lease!.CompareIndexedAsync(source.Lease, AnalysisMetric.UniqueFileSize, cancellation: token);
        await using var owner = await result.BuildChartAsync(previous, token); var projection = await owner.LoadAsync(cancellation: token);
        if (previous && sunburst)
        {
            var other = projection.Items.Values.First(item => item.Synthetic && item.Family == "other");
            projection = await owner.LoadAsync(other.Key, token);
        }
        await using var captured = await owner.CaptureScopeAsync(projection, token);
        AnalysisChartControl chart = sunburst ? new SunburstControl() : new TreemapControl(); chart.SetView(projection, projection.Scope, null, [], AnalysisColorDimension.Growth);
        if (sunburst) AssertSunburstExportCenterCaptionsStayInsideTheHole(chart.CaptureExportScene(captured.Header, 960, 720, redact, captured.Context));
        long before = AnalysisCapturedChart.ReservedBytes;
        await using var image = await AnalysisCapturedChart.CaptureComparisonAsync(profile.Services.Io, captured, chart,
            png ? AnalysisChartFormat.Png : AnalysisChartFormat.Svg, 960, 720, redact, token);
        await owner.DisposeAsync(); await source.ReleaseInitialLeaseAsync(); chart.Clear(); var closing = result.DisposeAsync().AsTask(); Assert.False(closing.IsCompleted);
        string folder = Path.Combine(source.Root, "images"); Directory.CreateDirectory(folder); string name = png ? "Comparison.png" : "Comparison.svg";
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token)) await prepared.ExportChartAsync(image, folder, name, redact, token);
        byte[] bytes = File.ReadAllBytes(Path.Combine(folder, name));
        RetainIndexedComparisonOutput(Path.Combine(folder, name), $"image-{png}-{previous}-{sunburst}-{redact}" + Path.GetExtension(name));
        if (png) Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        else
        {
            string text = Encoding.UTF8.GetString(bytes); Assert.Contains("Previous UTC", text); Assert.Contains("Current UTC", text);
            Assert.Contains(previous ? "Previous recorded amounts" : "Current recorded amounts", text);
            Assert.DoesNotContain("<script", text); Assert.DoesNotContain("foreignObject", text); Assert.DoesNotContain("href=", text);
            if (redact) { Assert.DoesNotContain(source.Root, text); Assert.DoesNotContain("match-target", text); }
        }
        await image.DisposeAsync(); await closing; Assert.Equal(before, AnalysisCapturedChart.ReservedBytes);
        Assert.Empty(Directory.GetFiles(source.PrivatePath)); Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }
    [AvaloniaTheory]
    [InlineData("previous")] [InlineData("current")] [InlineData("coverage")] [InlineData("freshness")]
    [InlineData("taint")] [InlineData("structural")] [InlineData("compatibility")] [InlineData("missing")]
    public async Task NativeIndexedComparisonImageGuardRejectsAllMismatchedCapturedContext(string mismatch)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var source = await IndexedFixture.CreateAsync();
        var token = TestContext.Current.CancellationToken; await using var result = await source.Lease!.CompareIndexedAsync(source.Lease, AnalysisMetric.Files, cancellation: token);
        await using var owner = await result.BuildChartAsync(false, token); var projection = await owner.LoadAsync(cancellation: token);
        await using var captured = await owner.CaptureScopeAsync(projection, token); var chart = new TreemapControl(); chart.SetView(projection, projection.Scope, null, [], AnalysisColorDimension.Growth);
        var context = captured.Context;
        AnalysisComparisonChartContext? wrong = mismatch switch
        {
            "previous" => context with { Previous = context.Previous with { Revision = 99 } },
            "current" => context with { Current = context.Current with { Revision = 99 } },
            "coverage" => context with { PreviousCoverage = NamespaceCoverage.Partial },
            "freshness" => context with { CurrentFreshness = AnalysisFreshness.PossiblyStale },
            "taint" => context with { Taint = 0 }, "structural" => context with { Structural = !context.Structural },
            "compatibility" => context with { Compatibility = context.Compatibility with { SamePolicy = !context.Compatibility.SamePolicy } }, _ => null
        };
        Assert.Throws<InvalidOperationException>(() => chart.CaptureExportScene(captured.Header, 960, 720, true, wrong));
        Assert.NotNull(chart.CaptureExportScene(captured.Header, 960, 720, true, context));
    }
    [AvaloniaFact]
    public async Task NativeIndexedComparisonReportReviewIsReachableAndCancelKeepsTheAppliedView()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync();
        var token = TestContext.Current.CancellationToken; var window = new DiskSpaceWindow();
        await window.AddIndexedComparisonSessionAsync(source.Lease!, source.Lease!, AnalysisMetric.FileSize, cancellation: token, services: profile.Services);
        window.Show(); Frame(window);
        try
        {
            var rows = ComparisonControl<ListBox>(window, "Captured comparison rows")!; var first = rows.Items[0];
            Click(window, "Export comparison report…"); await Until(() => ComparisonControl<ComboBox>(window, "Comparison report format") is not null);
            ComparisonControl<ComboBox>(window, "Comparison report format")!.SelectedIndex = 3;
            var choose = window.GetVisualDescendants().OfType<Button>().Single(control => control.IsEffectivelyVisible && Equals(control.Content, "Choose destination…"));
            Frame(window); await Until(() => !choose.IsEffectivelyEnabled); Click(window, "Cancel");
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(control => control.IsEffectivelyVisible && Equals(control.Content, "Export comparison report…") && control.IsEnabled));
            Assert.Equal(first, rows.Items[0]); Assert.Equal(1, window.SessionCount);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }
    private static void AssertSunburstExportCenterCaptionsStayInsideTheHole(AnalysisChartScene scene)
    {
        var ring = scene.Shapes.First(shape => shape.Path is not null);
        var arcs = System.Text.RegularExpressions.Regex.Matches(ring.Path!, @"\bA ([0-9.]+) [0-9.]+");
        double innerRadius = double.Parse(arcs[1].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var captions = scene.Texts.Where(text => text.Centered).ToArray(); Assert.Equal(2, captions.Length);
        foreach (var text in captions)
        {
            Assert.Equal(ring.Bounds.Center.X, text.Bounds.Center.X, 6);
            foreach (var corner in new[] { text.Bounds.TopLeft, text.Bounds.TopRight, text.Bounds.BottomLeft, text.Bounds.BottomRight })
            {
                var delta = corner - ring.Bounds.Center;
                Assert.True(Math.Sqrt(delta.X * delta.X + delta.Y * delta.Y) < innerRadius, "Center-caption clipping bounds must fit inside the innermost arc.");
            }
        }
        using var svg = new MemoryStream(); scene.WriteSvg(svg, TestContext.Current.CancellationToken); svg.Position = 0;
        var document = System.Xml.Linq.XDocument.Load(svg);
        Assert.Equal(2, document.Descendants().Count(element => element.Name.LocalName == "text" && (string?)element.Attribute("text-anchor") == "middle"));
    }
    private static void RetainIndexedComparisonOutput(string path, string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "indexed-comparison-output-artifacts"); Directory.CreateDirectory(directory);
        File.Copy(path, Path.Combine(directory, name), overwrite: true);
    }
}
