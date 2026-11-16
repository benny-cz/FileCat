using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private sealed class ComparisonImageFixture : IDisposable
    {
        private const string Secret = "CHARTSECRET<script>\u202e.cs";
        private readonly AnalysisInventory _before, _now;
        internal readonly AnalysisSnapshotLease Previous, Current;
        internal readonly AnalysisComparisonResult Result;
        internal readonly AnalysisComparisonChart Data;
        internal readonly AnalysisComparisonView View;
        internal readonly AnalysisComparisonChartContext Context;
        internal ComparisonImageFixture(bool previous, bool sunburst)
        {
            var token = TestContext.Current.CancellationToken; var snapshot = Fixture();
            snapshot = snapshot with { Nodes = snapshot.Nodes.Select(n => n.Id == 4 ? n with { Name = Secret } : n).ToImmutableArray(), Taint = AnalysisTaint.OrdinarySensitive | AnalysisTaint.PrivilegedDerived };
            _before = AnalysisInventoryFormat.Read(new MemoryStream(ExternalInventoryBytes(snapshot)), token);
            var current = snapshot with { Identity = snapshot.Identity with { Revision = 2, ObservedUtc = DateTimeOffset.UnixEpoch.AddDays(1) }, Taint = AnalysisTaint.OrdinarySensitive,
                Phase = AnalysisPhase.Stopped, Coverage = NamespaceCoverage.Partial,
                Objects = snapshot.Objects.Select(o => o.Id == "a" ? o with { LogicalLength = AnalysisMeasurement.Known(131072) } : o).ToImmutableArray() };
            _now = AnalysisInventoryFormat.Read(new MemoryStream(ExternalInventoryBytes(current)), token);
            Previous = _before.Retain(); Current = _now.Retain(); Result = AnalysisComparison.Build(Previous.Snapshot, Current.Snapshot, AnalysisMetric.FileSize);
            using var rows = Result.Retain(); Data = AnalysisComparisonChart.Build(previous ? Previous : Current, rows, AnalysisMetric.FileSize, previous, token);
            View = new() { Metric = AnalysisMetric.FileSize, Chart = sunburst ? AnalysisComparisonChartMode.Sunburst : AnalysisComparisonChartMode.Treemap,
                PreviousChart = previous, ChartScope = previous ? AnalysisTree.OtherKey("n:2", 1) : "n:2" };
            var before = Previous.Snapshot; var now = Current.Snapshot;
            Context = new(before.Identity, now.Identity, previous, before.Coverage, now.Coverage, before.Freshness, now.Freshness, Result.Taint, Result.Compatibility, false);
        }
        internal DiskSpaceViewModel.ComparisonCapture Capture() => new(Previous.Retain(), Current.Retain(), Result.Retain(), View, null, "Previous", null, null);
        internal AnalysisChartControl Chart()
        {
            AnalysisChartControl chart = View.Chart == AnalysisComparisonChartMode.Sunburst ? new SunburstControl() : new TreemapControl();
            chart.SetView(Data, View.ChartScope, null, [], AnalysisColorDimension.Growth); return chart;
        }
        public void Dispose() { Data.Dispose(); Result.Dispose(); Previous.Dispose(); Current.Dispose(); _before.Dispose(); _now.Dispose(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false, false, false)] [InlineData(false, false, false, true)]
    [InlineData(false, false, true, false)] [InlineData(false, false, true, true)]
    [InlineData(false, true, false, false)] [InlineData(false, true, false, true)]
    [InlineData(false, true, true, false)] [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)] [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)] [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)] [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)] [InlineData(true, true, true, true)]
    public async Task NativeComparisonChartExportCapturesBothIntervalsSideAndExactScopeWithoutLiveSources(bool previous, bool sunburst, bool png, bool redact)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        using var fixture = new ComparisonImageFixture(previous, sunburst); var token = TestContext.Current.CancellationToken;
        await using var owner = fixture.Capture(); await using var report = await fixture.Data.CaptureReportAsync(profile.Services.Io, fixture.View.ChartScope, token);
        var chart = fixture.Chart(); var scene = chart.CaptureExportScene(report.Header, 960, 720, redact, fixture.Context);
        Assert.Contains(scene.Texts, text => text.Value == "PARTIAL"); Assert.Contains(scene.Texts, text => text.Value.StartsWith("Previous UTC:", StringComparison.Ordinal));
        Assert.Contains(scene.Texts, text => text.Value.StartsWith("Current UTC:", StringComparison.Ordinal));
        Assert.Contains(scene.Texts, text => text.Value.StartsWith(previous ? "Previous recorded amounts" : "Current recorded amounts", StringComparison.Ordinal));
        Assert.Contains(scene.Texts, text => text.Value.Contains("table filters do not restrict", StringComparison.Ordinal));
        Assert.Contains(scene.Texts, text => text.Value.Contains("privileged-derived", StringComparison.Ordinal));
        Assert.Contains(scene.Texts, text => text.Value.Contains("Imported, unverified", StringComparison.Ordinal));
        Assert.Contains(scene.Texts, text => text.Value.Contains(fixture.Data.ScopeValue(fixture.View.ChartScope).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        Assert.All(scene.Texts, text => Assert.DoesNotContain("\u202e", text.Value, StringComparison.Ordinal));
        if (redact) Assert.All(scene.Texts, text => Assert.DoesNotContain("CHARTSECRET", text.Value, StringComparison.Ordinal));
        if (!redact && !previous && !sunburst) Assert.Contains(scene.Texts, text => text.Value.Contains("CHARTSECRET", StringComparison.Ordinal));
        long before = AnalysisCapturedChart.ReservedBytes;
        await using var image = await AnalysisCapturedChart.CaptureComparisonAsync(profile.Services.Io, report, owner, chart, fixture.Context,
            png ? AnalysisChartFormat.Png : AnalysisChartFormat.Svg, 960, 720, redact, token);
        fixture.Dispose(); chart.Clear(); Assert.False(fixture.Result.CloseCompletion.IsCompleted);
        string folder = Path.Combine(profile.Root, "chart-output"); Directory.CreateDirectory(folder); string name = png ? "Comparison.png" : "Comparison.svg";
        AnalysisExportDestination Destination() => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
            : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "comparison-chart-stage"));
        await image.ExportNewAsync(name, PathUtil.GetDeviceKey(folder), Destination, redact, token); byte[] bytes = File.ReadAllBytes(Path.Combine(folder, name));
        if (png) { using var bitmap = new Bitmap(new MemoryStream(bytes)); Assert.Equal(960, bitmap.PixelSize.Width); Assert.Equal(720, bitmap.PixelSize.Height); }
        else
        {
            var xml = XDocument.Parse(Encoding.UTF8.GetString(bytes)); Assert.Contains("Previous UTC:", xml.Root!.Value); Assert.Contains("Current UTC:", xml.Root.Value);
            Assert.DoesNotContain(xml.Descendants(), element => new[] { "script", "foreignObject", "image", "a", "use" }.Contains(element.Name.LocalName));
            Assert.DoesNotContain(xml.Descendants().Attributes(), attr => attr.Name.LocalName is "href" or "src" || attr.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase));
            if (redact) Assert.DoesNotContain("CHARTSECRET", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        }
        await Assert.ThrowsAsync<AnalysisExportTargetExistsException>(() => image.ExportNewAsync(name, PathUtil.GetDeviceKey(folder), Destination, redact, token));
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(folder, name))); await image.DisposeAsync();
        Assert.True(fixture.Result.CloseCompletion.IsCompletedSuccessfully); Assert.Equal(before, AnalysisCapturedChart.ReservedBytes);
    }

    [AvaloniaTheory]
    [InlineData("Classic")] [InlineData("ClassicDark")] [InlineData("HighContrast")] [InlineData("Cyberpunk")]
    [InlineData("Psychedelic")] [InlineData("Steampunk")] [InlineData("DosCommander")]
    public async Task ComparisonChartExportThemesKeepCaptionsAndGeometryWithinTheImage(string theme)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); using var fixture = new ComparisonImageFixture(false, false);
        string original = ThemeManager.RequestedName;
        try
        {
            ThemeManager.Apply(theme); await using var report = await fixture.Data.CaptureReportAsync(profile.Services.Io, fixture.View.ChartScope, TestContext.Current.CancellationToken);
            foreach (var chart in new AnalysisChartControl[] { new TreemapControl(), new SunburstControl() })
            {
                chart.SetView(fixture.Data, fixture.View.ChartScope, null, [], AnalysisColorDimension.Growth);
                var scene = chart.CaptureExportScene(report.Header, 960, 720, true, fixture.Context);
                Assert.NotEmpty(scene.Shapes); Assert.All(scene.Shapes, shape => Assert.True(shape.Bounds.X >= 0 && shape.Bounds.Y >= 0 && shape.Bounds.Right <= 960 && shape.Bounds.Bottom <= 610));
                Assert.All(scene.Texts, text => Assert.True(text.Bounds.X >= 0 && text.Bounds.Y >= 0 && text.Bounds.Right <= 960 && text.Bounds.Bottom <= 720));
                Assert.Contains(scene.Texts, text => text.Value == "PARTIAL"); chart.Clear();
            }
        }
        finally { ThemeManager.Apply(original); }
    }

    [AvaloniaTheory]
    [InlineData("side")] [InlineData("revision")] [InlineData("missing")]
    public async Task ComparisonChartExportRejectsMismatchedCaptionsBeforeCapturingAnImage(string mismatch)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); using var fixture = new ComparisonImageFixture(false, false);
        var token = TestContext.Current.CancellationToken; await using var owner = fixture.Capture();
        await using var report = await fixture.Data.CaptureReportAsync(profile.Services.Io, fixture.View.ChartScope, token);
        var chart = fixture.Chart(); long before = AnalysisCapturedChart.ReservedBytes;
        if (mismatch == "missing") Assert.Throws<InvalidOperationException>(() => chart.CaptureExportScene(report.Header, 960, 720, true));
        else
        {
            var wrong = mismatch == "side" ? fixture.Context with { PreviousSide = true } : fixture.Context with { Current = fixture.Context.Current with { Revision = 99 } };
            await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisCapturedChart.CaptureComparisonAsync(profile.Services.Io, report, owner, chart, wrong, AnalysisChartFormat.Svg, 960, 720, true, token));
        }
        Assert.Equal(before, AnalysisCapturedChart.ReservedBytes); Assert.NotEmpty(fixture.Result.Rows);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeComparisonChartExportCancellationKeepsBothInputsAndImageMemoryUntilActualOutputReturns(bool png)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); using var fixture = new ComparisonImageFixture(false, false);
        var token = TestContext.Current.CancellationToken; await using var owner = fixture.Capture();
        await using var report = await fixture.Data.CaptureReportAsync(profile.Services.Io, fixture.View.ChartScope, token); long before = AnalysisCapturedChart.ReservedBytes;
        var image = await AnalysisCapturedChart.CaptureComparisonAsync(profile.Services.Io, report, owner, fixture.Chart(), fixture.Context, png ? AnalysisChartFormat.Png : AnalysisChartFormat.Svg, 960, 720, true, token);
        fixture.Dispose(); string folder = Path.Combine(profile.Root, "blocked-image"); Directory.CreateDirectory(folder);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var release = new ManualResetEventSlim();
        var exporting = image.ExportNewAsync(png ? "Blocked.png" : "Blocked.svg", PathUtil.GetDeviceKey(folder), () => new ExportFaultDestination(
            OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "comparison-chart-blocked-stage")), "cancel", entered, release), true, token);
        Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); closing = image.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted); Assert.False(fixture.Result.CloseCompletion.IsCompleted); Assert.True(AnalysisCapturedChart.ReservedBytes > before);
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporting); await closing;
            Assert.Empty(Directory.GetFiles(folder)); Assert.True(fixture.Result.CloseCompletion.IsCompletedSuccessfully); Assert.Equal(before, AnalysisCapturedChart.ReservedBytes);
        }
        finally { release.Set(); try { await exporting; } catch (Exception error) when (error is IOException or OperationCanceledException) { } await (closing ?? image.DisposeAsync().AsTask()); }
    }

    [AvaloniaFact]
    public async Task ComparisonChartReleaseFailureStillReleasesImageBuffersAndOtherCapturedOwners()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); using var fixture = new ComparisonImageFixture(false, false);
        var token = TestContext.Current.CancellationToken; await using var owner = fixture.Capture();
        await using var report = await fixture.Data.CaptureReportAsync(profile.Services.Io, fixture.View.ChartScope, token); long before = AnalysisCapturedChart.ReservedBytes;
        var image = await AnalysisCapturedChart.CaptureComparisonAsync(profile.Services.Io, report, new FailedComparisonImageOwner(owner), fixture.Chart(), fixture.Context, AnalysisChartFormat.Svg, 960, 720, true, token);
        fixture.Dispose(); await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => image.DisposeAsync().AsTask());
        Assert.Equal(before, AnalysisCapturedChart.ReservedBytes); Assert.True(fixture.Result.CloseCompletion.IsCompletedSuccessfully);
    }
    private sealed class FailedComparisonImageOwner(IAsyncDisposable owner) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { await owner.DisposeAsync(); throw new IOException("Injected comparison owner release failure."); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task ComparisonChartExportReviewKeepsSafeDefaultsAndMakesUnavailableChartsExplicit(bool available, bool png)
    {
        using var styles = new ProductStyles(); using var ui = new HistoryDialogFixture(480, 620, 20);
        var pending = DiskSpaceWindow.ReviewComparisonReportOptionsAsync(ui.Dialogs, TestContext.Current.CancellationToken, available);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window);
        var format = ui.Window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Comparison report format");
        var scope = ui.Window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Comparison report scope");
        scope.SelectedIndex = 1; format.SelectedIndex = png ? 4 : 3; Frame(ui.Window);
        if (!available) { ui.Click("Choose destination…"); Assert.False(pending.IsCompleted); }
        await Until(() => ui.Find("Choose destination…").IsEffectivelyEnabled == available);
        Assert.Equal(0, scope.SelectedIndex); Assert.False(scope.IsEnabled); Assert.True(ui.Find("Cancel").IsDefault); Assert.Equal(available, ui.Find("Choose destination…").IsEffectivelyEnabled);
        ui.Click(available ? "Choose destination…" : "Cancel"); var options = await pending;
        if (!available) Assert.Null(options);
        else { Assert.NotNull(options); Assert.True(options.RedactPaths); Assert.Equal(png ? ".png" : ".svg", options.Extension); Assert.Contains("Both observation intervals", options.Description); Assert.Contains("Table filters do not restrict", options.Description); }
    }
}
