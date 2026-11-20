using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaFact]
    public async Task NativeComparisonBundleExportRetainsBothInputsOfflineAndKeepsExistingOutput()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(ExternalInventoryBytes(source.Memory!)); using var inventory = AnalysisInventoryFormat.Read(input, token);
        using var previous = inventory.Retain(); using var current = inventory.Retain(); int paths = previous.Snapshot.Nodes.Length;
        var view = new AnalysisComparisonView { Metric = AnalysisMetric.FileSize, Query = "name:ordinary", CaseSensitive = true, QuerySide = AnalysisComparisonSide.Either, Chart = AnalysisComparisonChartMode.Sunburst, PreviousChart = true };
        await using var bundle = AnalysisCapturedComparisonBundle.Capture(profile.Services.Io, previous, current, view, previousLabel: "Captured previous");
        previous.Dispose(); current.Dispose(); inventory.Dispose(); input.Dispose();
        string folder = Path.Combine(source.Root, "exports"), offline = source.SourcePath + "-offline"; Directory.CreateDirectory(folder); Directory.Move(source.SourcePath, offline);
        try
        {
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
                await prepared.ExportComparisonAsync(bundle, folder, "Both.fcdsc", token);
            byte[] bytes = File.ReadAllBytes(Path.Combine(folder, "Both.fcdsc"));
            using (var encoded = new MemoryStream(bytes))
            using (var saved = AnalysisComparisonFormat.Read(encoded, token))
            {
                Assert.Equal(paths, saved.Previous.Snapshot.Nodes.Length); Assert.Equal(paths, saved.Current.Snapshot.Nodes.Length);
                Assert.Equal(view, saved.View); Assert.Equal("Captured previous", saved.PreviousLabel);
                Assert.Equal(bundle.Taint, saved.Taint); Assert.False(Directory.Exists(source.SourcePath));
            }
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
                await Assert.ThrowsAsync<AnalysisExportTargetExistsException>(() => prepared.ExportComparisonAsync(bundle, folder, "Both.fcdsc", token));
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(folder, "Both.fcdsc")));
        }
        finally { Directory.Move(offline, source.SourcePath); }
        source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeComparisonBundleCloseWaitsForActualOutputAndReleasesBothInputsAfterCancellation()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(ExternalInventoryBytes(source.Memory!)); using var inventory = AnalysisInventoryFormat.Read(input, token); using var owner = inventory.Retain();
        var bundle = AnalysisCapturedComparisonBundle.Capture(profile.Services.Io, owner, owner, new()); owner.Dispose(); inventory.Dispose(); input.Dispose();
        string folder = Path.Combine(source.Root, "exports"); Directory.CreateDirectory(folder);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); using var release = new ManualResetEventSlim();
        var exporting = bundle.ExportNewAsync("Canceled.fcdsc", PathUtil.GetDeviceKey(folder), () => new ExportFaultDestination(
            OperatingSystem.IsWindows() ? WindowsAnalysisPrivateDirectory.OpenExportDestination(folder)
                : UnixAnalysisPrivateDirectory.OpenExportDestination(folder, () => new UnixAnalysisPrivateDirectory(profile.Root, "comparison-staging")), "cancel", entered, release), false, token);
        Task? closing = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token); closing = bundle.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted); Assert.False(exporting.IsCompleted); release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporting); await closing; Assert.Empty(Directory.GetFiles(folder)); source.AssertPayloads();
        }
        finally
        {
            release.Set(); try { await exporting; } catch (Exception error) when (error is IOException or OperationCanceledException) { }
            await (closing ?? bundle.DisposeAsync().AsTask());
        }
    }

    [AvaloniaFact]
    public async Task NativeComparisonBundleCannotWriteIntoProtectedApplicationState()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(ExternalInventoryBytes(source.Memory!)); using var inventory = AnalysisInventoryFormat.Read(input, token); using var owner = inventory.Retain();
        await using var bundle = AnalysisCapturedComparisonBundle.Capture(profile.Services.Io, owner, owner, new());
        string folder = profile.Services.Paths.AnalysisSnapshotsDirectory; Directory.CreateDirectory(folder);
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareExportAsync(profile.Services, token))
            await Assert.ThrowsAnyAsync<IOException>(() => prepared.ExportComparisonAsync(bundle, folder, "Blocked.fcdsc", token));
        Assert.False(File.Exists(Path.Combine(folder, "Blocked.fcdsc"))); source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeComparisonBundleRejectsAPathOmissionClaimAndWrongSuffixBeforeOpeningOutput()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken;
        using var input = new MemoryStream(ExternalInventoryBytes(source.Memory!)); using var inventory = AnalysisInventoryFormat.Read(input, token); using var owner = inventory.Retain();
        await using var bundle = AnalysisCapturedComparisonBundle.Capture(profile.Services.Io, owner, owner, new()); bool opened = false;
        AnalysisExportDestination Destination() { opened = true; throw new IOException(); }
        await Assert.ThrowsAsync<ArgumentException>(() => bundle.ExportNewAsync("Wrong.fcdsa", "comparison-test", Destination, false, token));
        await Assert.ThrowsAsync<ArgumentException>(() => bundle.ExportNewAsync("Both.fcdsc", "comparison-test", Destination, true, token));
        Assert.False(opened); source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task ComparisonBundleExportReviewDeclaresBothFullInputsAndKeepsCancelDefault()
    {
        using var styles = new ProductStyles(); using var ui = new HistoryDialogFixture(480, 620, 20);
        var options = new DiskSpaceWindow.ReportOptions(AnalysisReportFormat.Markdown, AnalysisReportScope.EntireResult, false) { ComparisonBundle = true };
        Assert.Equal(".fcdsc", options.Extension); Assert.Contains("both entire", options.Description); Assert.Contains("cannot omit paths", options.Description);
        var pending = DiskSpaceWindow.ReviewExternalOutputAsync(ui.Dialogs, "/chosen", "Previous and current captured intervals", "Both.fcdsc", options, TestContext.Current.CancellationToken);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window); Assert.True(ui.Find("Cancel").IsDefault); Assert.True(ui.Find("Cancel").IsCancel);
        Assert.True(ui.Find("Export new comparison bundle").IsEffectivelyEnabled); ui.Click("Cancel"); Assert.Null(await pending);
    }
}
