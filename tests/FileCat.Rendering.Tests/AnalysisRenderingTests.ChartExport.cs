using System.Text;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
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
    public void ChartExportSvgEscapesXmlForbiddenScalarsVisiblyAndKeepsSupplementaryCharacters()
    {
        const string source = "valid \U0001F431 invalid \uFFFF\uFFFE\u0001\uDFFF";
        const string expected = "valid \U0001F431 invalid \\uFFFF\\uFFFE\\u0001\\uDFFF";
        var scene = new AnalysisChartScene(960, 720, "#FFFFFF", [], [new(source, new Rect(0, 0, 800, 30), "#000000", 13)]);
        using var bytes = new MemoryStream(); scene.WriteSvg(bytes, TestContext.Current.CancellationToken);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(bytes.ToArray()));
        Assert.Equal(expected, xml.Descendants().Single(element => element.Name.LocalName == "text").Value);
    }
    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task NativeChartExportUsesIndexedFolderAndGroupedGeometryAfterOriginalOwnersClose(bool grouped, bool sunburst)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true, extraExtensions: 4);
        var token = TestContext.Current.CancellationToken; var lease = source.Lease!;
        AnalysisCapturedReport report; IAnalysisChartData data; string scope;
        if (grouped)
        {
            await using var groups = await AnalysisDatasetGroupExplorer.CreateAsync(lease, AnalysisMetric.UniqueFileSize, AnalysisGroupingDimension.Extension, cancellation: token);
            var page = await groups.ReadGroupsAsync(cancellation: token);
            var captured = AnalysisGroupedChart.Overview(lease.Descriptor.Identity, AnalysisGroupingDimension.Extension, AnalysisMetric.UniqueFileSize, groups.Totals, groups.LogicalTotals, page); data = captured; scope = captured.Scope;
            report = await lease.CaptureViewReportAsync(groups, null, AnalysisReportFormat.Csv, cancellation: token);
        }
        else
        {
            await using var folders = await AnalysisDatasetExplorer.CreateAsync(lease, AnalysisMetric.UniqueFileSize, cancellation: token);
            Assert.True(await folders.LoadChartAsync(cancellation: token)); data = folders.Chart!; scope = folders.Chart!.Scope;
            report = await lease.CaptureViewReportAsync(folders, 0, AnalysisReportFormat.Csv, cancellation: token);
        }
        await using var retained = report;
        AnalysisChartControl chart = sunburst ? new SunburstControl() : new TreemapControl(); chart.SetView(data, scope, null, []);
        var scene = chart.CaptureExportScene(report.Header with { Coverage = NamespaceCoverage.Partial, Taint = report.Header.Taint | AnalysisTaint.PrivilegedDerived }, 960, 720, true);
        Assert.Contains(scene.Texts, text => text.Value == "PARTIAL"); Assert.Contains(scene.Texts, text => text.Value.Contains("privileged-derived", StringComparison.Ordinal));
        await source.ReleaseInitialLeaseAsync(); chart = chart.CaptureExportView()!;
        await using var image = await AnalysisCapturedChart.CaptureAsync(profile.Services.Io, report, chart, AnalysisChartFormat.Svg, 960, 720, true, token);
        string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder);
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
            await prepared.ExportChartAsync(image, folder, "indexed.svg", true, token);
        string text = File.ReadAllText(Path.Combine(folder, "indexed.svg")); Assert.Contains("Unique file size", text); Assert.DoesNotContain(source.Root, text);
        await image.DisposeAsync(); Assert.Empty(Directory.GetFiles(source.PrivatePath)); Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)] [InlineData(false, false, true)] [InlineData(false, true, false)] [InlineData(false, true, true)]
    [InlineData(true, false, false)] [InlineData(true, false, true)] [InlineData(true, true, false)] [InlineData(true, true, true)]
    public async Task NativeChartExportKeepsCapturedScopeOfflineAndPreservesExistingFiles(bool sunburst, bool png, bool redacted)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(ExternalInventoryBytes(source.Memory!)); using var inventory = AnalysisInventoryFormat.Read(input, token); using var owner = inventory.Retain();
        string sessionId = owner.Snapshot.Identity.SessionId.ToString();
        var tree = AnalysisTree.Build(owner.Snapshot, AnalysisAggregate.Project(owner.Snapshot, AnalysisMetric.FileSize, cancellation: token), false, token);
        await using var report = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, profile.Services.Io, AnalysisReportFormat.Csv, AnalysisReportScope.FilteredView, cancellation: token);
        AnalysisChartControl chart = sunburst ? new SunburstControl() : new TreemapControl(); chart.SetView(tree, AnalysisTree.RootKey, null, []);
        long reserved = AnalysisCapturedChart.ReservedBytes;
        await using var image = await AnalysisCapturedChart.CaptureAsync(profile.Services.Io, report, chart, png ? AnalysisChartFormat.Png : AnalysisChartFormat.Svg, 960, 720, redacted, token);
        Assert.True(AnalysisCapturedChart.ReservedBytes > reserved); owner.Dispose(); inventory.Dispose(); chart.Clear();
        string offline = source.SourcePath + "-offline", folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder);
        Directory.Move(source.SourcePath, offline); string name = png ? "chart.png" : "chart.svg";
        try
        {
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
                await prepared.ExportChartAsync(image, folder, name, redacted, token);
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, name));
            if (png)
            {
                Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8));
                using var bitmap = new Bitmap(Path.Combine(folder, name)); Assert.Equal(new PixelSize(960, 720), bitmap.PixelSize);
                int pixelBytes = 960 * 720 * 4; var pixels = new byte[pixelBytes]; var memory = System.Runtime.InteropServices.Marshal.AllocHGlobal(pixelBytes);
                try
                {
                    bitmap.CopyPixels(new PixelRect(0, 0, 960, 720), memory, pixelBytes, 960 * 4);
                    System.Runtime.InteropServices.Marshal.Copy(memory, pixels, 0, pixelBytes);
                    Assert.True(Enumerable.Range(0, pixels.Length / 4).Select(index => BitConverter.ToUInt32(pixels, index * 4)).Distinct().Take(5).Count() == 5, "PNG must contain rendered chart/text pixels, not an empty surface.");
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(memory); }
                var chunks = new List<string>();
                for (int offset = 8; offset < bytes.Length;)
                {
                    int length = checked((int)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4)));
                    chunks.Add(Encoding.ASCII.GetString(bytes, offset + 4, 4)); offset = checked(offset + length + 12); Assert.True(offset <= bytes.Length);
                }
                Assert.DoesNotContain(chunks, chunk => chunk is "tEXt" or "iTXt" or "zTXt" or "eXIf");
            }
            else
            {
                string text = Encoding.UTF8.GetString(bytes); var xml = XDocument.Parse(text);
                Assert.Contains("FileCat disk space", text); Assert.Contains("Observed UTC:", text); Assert.Contains("Colors:", text);
                Assert.Contains("File size (bytes)", text); Assert.Contains(report.Header.Coverage == NamespaceCoverage.CompleteUnderPolicy ? "complete under captured policy" : "PARTIAL", text);
                Assert.All(xml.Descendants(), element => Assert.Contains(element.Name.LocalName, new[] { "svg", "rect", "path", "text", "defs", "clipPath" }));
                Assert.DoesNotContain(xml.Descendants().Attributes(), attr => attr.Name.LocalName is "href" or "src" || attr.Name.LocalName.StartsWith("on", StringComparison.Ordinal));
                Assert.DoesNotContain(sessionId, text);
                if (redacted) { Assert.DoesNotContain(source.SourcePath, text); Assert.DoesNotContain("ordinary.txt", text); Assert.DoesNotContain("plain", text); }
                else Assert.Contains(report.Header.ScopeLabel, string.Join(" ", xml.Descendants().Select(element => element.Value)));
            }
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
                await Assert.ThrowsAsync<AnalysisExportTargetExistsException>(() => prepared.ExportChartAsync(image, folder, name, redacted, token));
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(folder, name))); Assert.False(Directory.Exists(source.SourcePath));
        }
        finally { Directory.Move(offline, source.SourcePath); }
        await image.DisposeAsync(); Assert.Equal(reserved, AnalysisCapturedChart.ReservedBytes); source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeChartExportCancellationWaitsForActualOutputAndKeepsMemoryReserved(bool png)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(ExternalInventoryBytes(source.Memory!)); using var inventory = AnalysisInventoryFormat.Read(input, token); using var owner = inventory.Retain();
        var tree = AnalysisTree.Build(owner.Snapshot, AnalysisAggregate.Project(owner.Snapshot, AnalysisMetric.FileSize, cancellation: token), false, token);
        await using var report = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, profile.Services.Io, AnalysisReportFormat.Csv, AnalysisReportScope.FilteredView, cancellation: token);
        var chart = new TreemapControl(); chart.SetView(tree, AnalysisTree.RootKey, null, []); long before = AnalysisCapturedChart.ReservedBytes;
        var image = await AnalysisCapturedChart.CaptureAsync(profile.Services.Io, report, chart, png ? AnalysisChartFormat.Png : AnalysisChartFormat.Svg, 960, 720, true, token);
        owner.Dispose(); inventory.Dispose(); chart.Clear(); string folder = Path.Combine(source.Root, "reports"); Directory.CreateDirectory(folder);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var release = new ManualResetEventSlim();
        var exporting = image.ExportNewAsync(png ? "cancel.png" : "cancel.svg", PathUtil.GetDeviceKey(folder), () => new ExportFaultDestination(
            OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "chart-staging")), "cancel", entered, release), true, token);
        Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); closing = image.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted); Assert.False(exporting.IsCompleted); Assert.True(AnalysisCapturedChart.ReservedBytes > before);
            release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporting); await closing;
            Assert.Empty(Directory.GetFiles(folder)); Assert.Equal(before, AnalysisCapturedChart.ReservedBytes); source.AssertPayloads();
        }
        finally { release.Set(); try { await exporting; } catch (Exception error) when (error is IOException or OperationCanceledException) { } await (closing ?? image.DisposeAsync().AsTask()); }
    }

    [AvaloniaFact]
    public async Task ChartExportAdmissionAndMismatchRefuseBeforeOpeningOutputAndReleaseFailedCaptures()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(ExternalInventoryBytes(source.Memory!)); using var inventory = AnalysisInventoryFormat.Read(input, token); using var owner = inventory.Retain();
        var tree = AnalysisTree.Build(owner.Snapshot, AnalysisAggregate.Project(owner.Snapshot, AnalysisMetric.FileSize, cancellation: token), false, token);
        await using var report = await AnalysisCapturedMemoryReport.CaptureAsync(owner, tree, profile.Services.Io, AnalysisReportFormat.Csv, AnalysisReportScope.FilteredView, cancellation: token);
        var chart = new TreemapControl(); chart.SetView(tree, AnalysisTree.RootKey, null, []); long before = AnalysisCapturedChart.ReservedBytes;
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AnalysisCapturedChart.CaptureAsync(profile.Services.Io, report, chart, AnalysisChartFormat.Png, 16385, 720, true, token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AnalysisCapturedChart.CaptureAsync(profile.Services.Io, report, chart, AnalysisChartFormat.Png, 16384, 16384, true, token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AnalysisCapturedChart.CaptureAsync(profile.Services.Io, report, chart, AnalysisChartFormat.Png, 8000, 8000, true, token));
        Assert.Equal(before, AnalysisCapturedChart.ReservedBytes);
        await using var image = await AnalysisCapturedChart.CaptureAsync(profile.Services.Io, report, chart, AnalysisChartFormat.Svg, 960, 720, true, token);
        bool opened = false;
        await Assert.ThrowsAsync<ArgumentException>(() => image.ExportNewAsync("wrong.png", "chart-device", () => { opened = true; throw new IOException(); }, true, token));
        await Assert.ThrowsAsync<ArgumentException>(() => image.ExportNewAsync("chart.svg", "chart-device", () => { opened = true; throw new IOException(); }, false, token));
        Assert.False(opened); await image.DisposeAsync(); Assert.Equal(before, AnalysisCapturedChart.ReservedBytes); source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task ChartExportReviewKeepsRedactionAndCancelDefaultAndLimitsScopeToCurrentChart(bool png)
    {
        using var styles = new ProductStyles(); using var ui = new HistoryDialogFixture(480, 620, 20);
        var pending = DiskSpaceWindow.ReviewReportOptionsAsync(ui.Dialogs, true, 2, TestContext.Current.CancellationToken);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window);
        var format = ui.Window.GetVisualDescendants().OfType<ComboBox>().Single(value => AutomationProperties.GetName(value) == "Report format");
        var scope = ui.Window.GetVisualDescendants().OfType<ComboBox>().Single(value => AutomationProperties.GetName(value) == "Report scope");
        scope.SelectedIndex = scope.ItemCount - 1; format.SelectedIndex = png ? 4 : 3; Frame(ui.Window);
        Assert.Equal(0, scope.SelectedIndex); Assert.False(scope.IsEnabled); Assert.True(ui.Find("Cancel").IsDefault);
        ui.Click("Choose destination…"); var reviewed = Assert.IsType<DiskSpaceWindow.ReportOptions>(await pending);
        Assert.True(reviewed.RedactPaths); Assert.Equal(AnalysisReportScope.FilteredView, reviewed.Scope);
        Assert.Equal(png ? ".png" : ".svg", reviewed.Extension); Assert.Equal(1280, reviewed.ChartWidth); Assert.Equal(960, reviewed.ChartHeight);
        Assert.Contains("partial-state notice", reviewed.Description);
    }

    [AvaloniaFact]
    public void ChartExportSvgEscapesHostileTextAndContainsOnlyGeneratedLocalClipReferences()
    {
        string hostile = "secret <script onload=\"boom\"> & file:///private/person";
        var scene = new AnalysisChartScene(960, 720, "#FFFFFF", [], [new(hostile, new Rect(0, 0, 800, 30), "#000000", 13)]);
        using var bytes = new MemoryStream(); scene.WriteSvg(bytes, TestContext.Current.CancellationToken);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(bytes.ToArray()));
        Assert.Single(xml.Descendants(), element => element.Name.LocalName == "text");
        Assert.Equal(hostile, xml.Descendants().Single(element => element.Name.LocalName == "text").Value);
        Assert.DoesNotContain(xml.Descendants(), element => element.Name.LocalName is "script" or "foreignObject" or "image");
        Assert.All(xml.Descendants().Attributes().Where(attr => attr.Value.Contains("url(", StringComparison.Ordinal)), attr => Assert.Equal("url(#t0)", attr.Value));
    }
}
