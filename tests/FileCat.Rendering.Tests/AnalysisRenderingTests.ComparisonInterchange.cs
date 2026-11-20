using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static byte[] ExternalComparisonBytes(AnalysisSnapshot snapshot, AnalysisComparisonView? view = null)
    {
        using var output = new MemoryStream();
        AnalysisComparisonFormat.Write(output, Guid.NewGuid(), snapshot, snapshot, view ?? new(), null, "Captured previous", TestContext.Current.CancellationToken);
        return output.ToArray();
    }
    private static AnalysisInventoryInput OpenComparisonInput(string path) => OperatingSystem.IsWindows()
        ? WindowsAnalysisPrivateDirectory.OpenChosenComparison(path) : UnixAnalysisPrivateDirectory.OpenChosenComparison(path);

    [AvaloniaTheory]
    [InlineData(AnalysisMetric.FileSize)] [InlineData(AnalysisMetric.UniqueFileSize)] [InlineData(AnalysisMetric.OnDisk)] [InlineData(AnalysisMetric.Files)]
    public async Task NativeExternalComparisonRestoresBothOfflineInputsAndAppliedViewInAnUnsavedTab(AnalysisMetric metric)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        var token = TestContext.Current.CancellationToken; var window = new DiskSpaceWindow(); int handles = AnalysisNativeHandles.Count;
        var view = new AnalysisComparisonView { Metric = metric, Query = "name=source.cs", FocusNode = 4, Chart = AnalysisComparisonChartMode.Sunburst, PreviousChart = true, ChartScope = "n:1" };
        string path = Path.Combine(profile.Root, "shared.fcdsc"); byte[] bytes = ExternalComparisonBytes(Fixture(), view); File.WriteAllBytes(path, bytes); ExternalPublicRead(path);
        try
        {
            var model = await window.AddExternalComparisonSessionAsync(profile.Services, path, token); window.Show(); Frame(window);
            Assert.Equal(1, window.SessionCount); Assert.Equal(view, model.ComparisonView); Assert.Null(model.SavedComparisonName); Assert.True(model.HasTemporaryWork);
            Assert.True(model.Comparison!.Taint.HasFlag(AnalysisTaint.ImportedUnverified)); Assert.Equal(metric, model.Comparison.Metric);
            using var captured = model.CaptureComparison(forExport: true);
            Assert.Equal(AnalysisOrigin.ImportedInventory, captured.Previous.Snapshot.Origin); Assert.Equal(AnalysisOrigin.ImportedInventory, captured.Current.Snapshot.Origin);
            Assert.Equal(6, captured.Previous.Snapshot.Nodes.Length); Assert.Equal(6, captured.Current.Snapshot.Nodes.Length);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && button.Content as string == "Export comparison report…" && button.IsEnabled);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text?.StartsWith("Imported comparison · offline observations.", StringComparison.Ordinal) == true);
            Assert.False(Directory.Exists(profile.Services.Paths.AnalysisSnapshotsDirectory) && Directory.EnumerateFiles(profile.Services.Paths.AnalysisSnapshotsDirectory, "*.fcdsc").Any());
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(handles, AnalysisNativeHandles.Count); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [AvaloniaTheory]
    [InlineData("checksum")] [InlineData("truncated")] [InlineData("future")] [InlineData("authority")]
    public async Task NativeExternalComparisonRefusesCorruptionWithoutPublishingATabOrChangingTheInput(string fault)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        byte[] bytes = ExternalComparisonBytes(Fixture());
        if (fault == "truncated") bytes = bytes[..^1];
        else
        {
            var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int index = fault == "checksum" ? lines.Length - 1 : 0; var value = JsonNode.Parse(lines[index])!.AsObject();
            if (fault == "checksum") value["sha256"] = new string('0', 64);
            else if (fault == "future") value["schema"] = "999"; else value["runtimeOrigin"] = "LiveSession";
            lines[index] = value.ToJsonString(); bytes = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
        }
        string path = Path.Combine(profile.Root, "bad.fcdsc"); File.WriteAllBytes(path, bytes); int handles = AnalysisNativeHandles.Count;
        var window = new DiskSpaceWindow();
        try { await Assert.ThrowsAsync<InvalidDataException>(() => window.AddExternalComparisonSessionAsync(profile.Services, path, token)); Assert.Equal(0, window.SessionCount); }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Equal(handles, AnalysisNativeHandles.Count); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [AvaloniaTheory]
    [InlineData("cancel")] [InlineData("read-cancel")] [InlineData("verify")] [InlineData("close")]
    public async Task NativeExternalComparisonRetainsActualInputOwnershipThroughCancellationAndFailedRelease(string fault)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); using var stop = new CancellationTokenSource();
        var token = TestContext.Current.CancellationToken; string path = Path.Combine(profile.Root, "fault.fcdsc");
        byte[] bytes = ExternalComparisonBytes(Fixture()); File.WriteAllBytes(path, bytes); int handles = AnalysisNativeHandles.Count;
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); ExternalFaultInput? input = null;
        var pending = AnalysisComparisonFormat.ReadExternalAsync(profile.Services.Io, PathUtil.GetDeviceKey(path),
            () => input = new(OpenComparisonInput(path), fault, entered, release), stop.Token);
        try
        {
            if (fault is "cancel" or "read-cancel")
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); stop.Cancel(); Assert.False(pending.IsCompleted); Assert.False(input!.Closed);
                release.Set(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            }
            else if (fault == "close") await Assert.ThrowsAsync<AnalysisCloseUnconfirmedException>(() => pending);
            else await Assert.ThrowsAsync<IOException>(() => pending);
            Assert.NotNull(input); Assert.True(input.Closed);
        }
        finally { release.Set(); if (pending.IsCompletedSuccessfully) (await pending).Dispose(); }
        Assert.Equal(handles, AnalysisNativeHandles.Count); Assert.Equal(bytes, File.ReadAllBytes(path));
    }

    [AvaloniaTheory]
    [InlineData(AnalysisReportFormat.Markdown, false, false)] [InlineData(AnalysisReportFormat.Markdown, true, false)]
    [InlineData(AnalysisReportFormat.Html, false, false)] [InlineData(AnalysisReportFormat.Html, true, false)]
    [InlineData(AnalysisReportFormat.Csv, false, false)] [InlineData(AnalysisReportFormat.Csv, true, false)]
    [InlineData(AnalysisReportFormat.Markdown, false, true)] [InlineData(AnalysisReportFormat.Markdown, true, true)]
    [InlineData(AnalysisReportFormat.Html, false, true)] [InlineData(AnalysisReportFormat.Html, true, true)]
    [InlineData(AnalysisReportFormat.Csv, false, true)] [InlineData(AnalysisReportFormat.Csv, true, true)]
    public async Task NativeComparisonReportsFreezeBothInputsAndKeepScopeEscapingAndRedaction(AnalysisReportFormat format, bool redact, bool entire)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        const string secret = "=COMPARISONSECRET<script>\u202e|.cs";
        var snapshot = Fixture(); snapshot = snapshot with { Nodes = snapshot.Nodes.Select(n => n.Id == 4 ? n with { Name = secret } : n).ToImmutableArray() };
        using var before = AnalysisInventoryFormat.Read(new MemoryStream(ExternalInventoryBytes(snapshot)), token);
        var changed = snapshot with { Identity = snapshot.Identity with { Revision = 2 }, Objects = snapshot.Objects.Select(o => o.Id == "a" ? o with { LogicalLength = AnalysisMeasurement.Known(131072) } : o).ToImmutableArray() };
        using var current = AnalysisInventoryFormat.Read(new MemoryStream(ExternalInventoryBytes(changed)), token);
        using var previousLease = before.Retain(); using var currentLease = current.Retain();
        using var comparison = AnalysisComparison.Build(before.Snapshot, current.Snapshot, AnalysisMetric.FileSize);
        var view = new AnalysisComparisonView { Metric = AnalysisMetric.FileSize, Query = "name:COMPARISONSECRET", Cohort = AnalysisComparisonCohort.Growing, Order = AnalysisComparisonOrder.CurrentAmount };
        await using var report = await AnalysisCapturedComparisonReport.CaptureAsync(profile.Services.Io, previousLease, currentLease, comparison, view, format, entire, token);
        Assert.Equal(entire ? 3 : 1, report.Rows); previousLease.Dispose(); currentLease.Dispose(); before.Dispose(); current.Dispose(); comparison.Dispose();
        string folder = Path.Combine(profile.Root, "reports"); Directory.CreateDirectory(folder); string name = "Comparison" + (format == AnalysisReportFormat.Csv ? ".csv" : format == AnalysisReportFormat.Html ? ".html" : ".md");
        AnalysisExportDestination Destination() => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
            : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "comparison-report-stage"));
        await report.ExportNewAsync(name, PathUtil.GetDeviceKey(folder), Destination, redact, token);
        string text = File.ReadAllText(Path.Combine(folder, name));
        Assert.Contains("Previous observations UTC", text); Assert.Contains("current observations UTC", text); Assert.Contains("Full input totals", text);
        Assert.Contains("case sensitive: False", text); Assert.Contains("may be abbreviated", text);
        Assert.Contains("not zero", text); Assert.Contains("131072", text); Assert.Contains("Growing", text); Assert.DoesNotContain("\u202e", text, StringComparison.Ordinal);
        if (redact) { Assert.DoesNotContain("COMPARISONSECRET", text); Assert.DoesNotContain("/fixture", text); Assert.DoesNotContain("render-fixture", text); }
        else Assert.Contains("COMPARISONSECRET", text);
        if (format == AnalysisReportFormat.Html) Assert.DoesNotContain("<script>", text, StringComparison.OrdinalIgnoreCase);
        if (format == AnalysisReportFormat.Csv) { Assert.Equal(report.Rows + 2, text.Count(c => c == '\n')); if (!redact) Assert.Contains("'", text); }
        byte[] original = File.ReadAllBytes(Path.Combine(folder, name));
        await Assert.ThrowsAsync<AnalysisExportTargetExistsException>(() => report.ExportNewAsync(name, PathUtil.GetDeviceKey(folder), Destination, redact, token));
        Assert.Equal(original, File.ReadAllBytes(Path.Combine(folder, name)));
    }

    [AvaloniaFact]
    public async Task NativeComparisonReportKeepsUnknownAllocationSeparateFromZeroAndDisclaimsContentEquality()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        using var inventory = AnalysisInventoryFormat.Read(new MemoryStream(ExternalInventoryBytes(Fixture())), token); using var owner = inventory.Retain();
        using var comparison = AnalysisComparison.Build(owner.Snapshot, owner.Snapshot, AnalysisMetric.OnDisk); Assert.Null(comparison.NetChange);
        await using var report = await AnalysisCapturedComparisonReport.CaptureAsync(profile.Services.Io, owner, owner, comparison,
            new() { Metric = AnalysisMetric.OnDisk }, AnalysisReportFormat.Csv, entire: true, cancellation: token);
        string folder = Path.Combine(profile.Root, "unknown-report"); Directory.CreateDirectory(folder);
        await report.ExportNewAsync("Unknown.csv", PathUtil.GetDeviceKey(folder), () => OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
            : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "unknown-report-stage")), true, token);
        string text = File.ReadAllText(Path.Combine(folder, "Unknown.csv"));
        Assert.Contains("\"Unknown\"", text); Assert.Contains("not zero", text); Assert.Contains("does not prove unchanged contents", text); Assert.Contains("Attribution movement is not new consumption", text);
    }

    [AvaloniaFact]
    public async Task NativeComparisonReportCancellationWaitsForActualOutputAfterOriginalOwnersClose()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        using var inventory = AnalysisInventoryFormat.Read(new MemoryStream(ExternalInventoryBytes(Fixture())), token); using var owner = inventory.Retain();
        using var comparison = AnalysisComparison.Build(owner.Snapshot, owner.Snapshot, AnalysisMetric.FileSize);
        var report = await AnalysisCapturedComparisonReport.CaptureAsync(profile.Services.Io, owner, owner, comparison, new(), AnalysisReportFormat.Csv, cancellation: token);
        owner.Dispose(); inventory.Dispose(); comparison.Dispose(); string folder = Path.Combine(profile.Root, "cancel-report"); Directory.CreateDirectory(folder);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var release = new ManualResetEventSlim();
        var exporting = report.ExportNewAsync("Canceled.csv", PathUtil.GetDeviceKey(folder), () => new ExportFaultDestination(
            OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "comparison-cancel-stage")), "cancel", entered, release), true, token);
        Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); closing = report.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted); Assert.False(exporting.IsCompleted); release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporting); await closing; Assert.Empty(Directory.GetFiles(folder));
        }
        finally { release.Set(); try { await exporting; } catch (Exception error) when (error is IOException or OperationCanceledException) { } await (closing ?? report.DisposeAsync().AsTask()); }
    }

    [AvaloniaFact]
    public async Task NativeComparisonReportGuardsProtectedStateAndRejectsFormatMismatchBeforeOpeningOutput()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        using var inventory = AnalysisInventoryFormat.Read(new MemoryStream(ExternalInventoryBytes(Fixture())), token); using var owner = inventory.Retain();
        using var comparison = AnalysisComparison.Build(owner.Snapshot, owner.Snapshot, AnalysisMetric.FileSize);
        await using var report = await AnalysisCapturedComparisonReport.CaptureAsync(profile.Services.Io, owner, owner, comparison, new(), AnalysisReportFormat.Html, cancellation: token);
        bool opened = false; AnalysisExportDestination Destination() { opened = true; throw new IOException(); }
        await Assert.ThrowsAsync<ArgumentException>(() => report.ExportNewAsync("wrong.csv", "comparison-report", Destination, true, token)); Assert.False(opened);
        string folder = profile.Services.Paths.AnalysisSnapshotsDirectory; Directory.CreateDirectory(folder);
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
            await Assert.ThrowsAnyAsync<IOException>(() => prepared.ExportComparisonReportAsync(report, folder, "Blocked.html", true, token));
        Assert.False(File.Exists(Path.Combine(folder, "Blocked.html")));
        await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(folder), _ =>
        { Assert.Throws<ArgumentException>(() => OpenComparisonInput(Path.Combine(folder, "wrong.fcdsa"))); return true; }, token);
    }

    [AvaloniaFact]
    public async Task ComparisonImportAndReportReviewsKeepCancelAndPathOmissionDefaults()
    {
        using var styles = new ProductStyles(); using var ui = new HistoryDialogFixture(480, 620, 20); var token = TestContext.Current.CancellationToken;
        var opening = DiskSpaceWindow.ConfirmExternalInventoryAsync(ui.Dialogs, "/chosen/observations.fcdsc", token, comparison: true);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window); Assert.True(ui.Find("Cancel").IsDefault); ui.Click("Cancel"); Assert.False(await opening);
        var pending = DiskSpaceWindow.ReviewComparisonReportOptionsAsync(ui.Dialogs, token);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window); Assert.True(ui.Find("Cancel").IsDefault);
        var format = ui.Window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationLabel(c) == "Comparison report format");
        var scope = ui.Window.GetVisualDescendants().OfType<ComboBox>().Single(c => AutomationLabel(c) == "Comparison report scope");
        format.SelectedIndex = 2; scope.SelectedIndex = 1; ui.Click("Choose destination…"); var options = Assert.IsType<DiskSpaceWindow.ReportOptions>(await pending);
        Assert.True(options.ComparisonReport); Assert.True(options.RedactPaths); Assert.Equal(AnalysisReportScope.EntireResult, options.Scope);
        Assert.Contains("previous and current", options.Description); Assert.Contains("all accounting rows", options.Description);
    }
}
