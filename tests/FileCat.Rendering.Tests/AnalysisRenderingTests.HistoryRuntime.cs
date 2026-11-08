using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Core.State;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeHistoryCompletionCapturesOnceExcludesPrivateFilesAndPrunesOnlyRollingCopies(bool indexed)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        var services = profile.Services; var token = TestContext.Current.CancellationToken;
        services.Settings.AnalysisPreferences = new() { RollingHistory = new(true, 1) };
        string sourcePath = Path.Combine(profile.Root, "files"); Directory.CreateDirectory(sourcePath);
        string payload = Path.Combine(sourcePath, "source-do-not-change.txt"); await File.WriteAllTextAsync(payload, "keep source bytes", token);
        string sourceHash = RollingHash(payload);
        Guid first = Guid.Empty;
        for (int at = 0; at < 2; at++)
        {
            await using var acquisition = await AnalysisIndexedAcquisition.PrepareAsync(services, token, indexed);
            string device = PathUtil.GetDeviceKey(profile.Root);
            IAnalysisBackend? source = await AnalysisIo.Run(services.Io, device, _ => NativeRoot(profile.Root), token);
            var window = new DiskSpaceWindow();
            try
            {
                var session = acquisition.Start([source]); source = null;
                await session.Completion.WaitAsync(TimeSpan.FromSeconds(45), token);
                var outcome = await acquisition.HistoryCompletion.WaitAsync(TimeSpan.FromSeconds(45), token);
                Assert.NotNull(outcome); Assert.Equal(AnalysisRollingCaptureState.Saved, outcome.State); Assert.NotNull(outcome.Receipt);
                Assert.Same(services.AnalysisHistory.CaptureCompletedAsync(session), services.AnalysisHistory.CaptureCompletedAsync(session));
                Assert.Equal(outcome, await services.AnalysisHistory.CaptureCompletedAsync(session));
                if (at == 0) first = outcome.Receipt.InventoryId;
                else Assert.Equal(first, Assert.Single(outcome.Pruned));
                await using (var history = await AnalysisRollingHistoryAccess.OpenAsync(services, token))
                using (var catalog = await history.ReadCatalogAsync(token))
                { Assert.Equal(outcome.Receipt.InventoryId, Assert.Single(catalog.Entries).InventoryId); Assert.Empty(catalog.UnindexedNames); }
                await using (var data = await session.AcquireDatasetAsync(token))
                {
                    if (data is not null)
                    {
                        ulong offset = 0; int excluded = 0;
                        do
                        {
                            var page = await data.ReadPathsAsync(offset, cancellation: token);
                            Assert.DoesNotContain(page.Rows, row => row.Name.EndsWith(".fcdsa", StringComparison.Ordinal) || row.Name is "catalog.json" or "private-state.json");
                            excluded += page.Rows.Count(row => row.MetadataState == MeasurementState.Excluded);
                            offset = page.NextOffset;
                        } while (offset < data.Descriptor.Paths);
                        Assert.True(excluded >= 3);
                    }
                    else
                    {
                        var snapshot = session.Snapshot();
                        Assert.DoesNotContain(snapshot.Nodes, row => row.Name.EndsWith(".fcdsa", StringComparison.Ordinal) || row.Name is "catalog.json" or "private-state.json");
                        Assert.True(snapshot.Nodes.Count(row => row.MetadataState == MeasurementState.Excluded) >= 3);
                    }
                }
                var view = await window.AddAcquiredSessionAsync(session, services, token);
                window.Show(); Frame(window);
                if (indexed)
                {
                    Assert.NotNull(view.Indexed); Assert.Null(view.Memory);
                    Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == outcome.Detail);
                }
                else { Assert.NotNull(view.Memory); Assert.Equal(outcome.Detail, view.Memory.HistoryNotice); Assert.Null(view.Memory.SavedInventoryName); }
            }
            finally
            {
                window.ForceClose(); await window.WaitForIndexedCloseAsync();
                if (source is not null) await AnalysisIo.ReleaseAsync(services.Io, source);
            }
        }
        Assert.Equal(sourceHash, RollingHash(payload));
        Assert.Single(Directory.GetFiles(services.Paths.AnalysisHistoryDirectory, "*.fcdsa"));
        Assert.Empty(Directory.GetFiles(services.Paths.AnalysisSnapshotsDirectory, "*.fcdsa"));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeHistoryOffCreatesNoInventoryAndOrdinaryMemoryViewReportsEnabledCapture(bool enabled)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var services = profile.Services;
        services.Settings.AnalysisPreferences = new() { RollingHistory = new(enabled) };
        string source = Path.Combine(profile.Root, "source"); Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "payload.txt"), "preserve", TestContext.Current.CancellationToken);
        var model = new DiskSpaceViewModel(services);
        try
        {
            await model.StartAsync([source]);
            await model.HistoryObservation.WaitAsync(TimeSpan.FromSeconds(45), TestContext.Current.CancellationToken);
            await Until(() => !model.Running);
            await model.RebuildAsync(true, TestContext.Current.CancellationToken);
            Assert.Equal(AnalysisPhase.Finished, model.Phase);
            Assert.Equal(enabled ? 1 : 0, Directory.GetFiles(services.Paths.AnalysisHistoryDirectory, "*.fcdsa").Length);
            if (enabled) Assert.Contains("saved in rolling history", model.HistoryNotice);
            else Assert.Null(model.HistoryNotice);
        }
        finally { model.Dispose(); await model.CloseCompletion; }
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task StoppedAndUnconfirmedSourceCloseSessionsNeverEnterAutomaticHistory(bool closeFailure)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); using var gate = new ManualResetEventSlim();
        profile.Services.Settings.AnalysisPreferences = new() { RollingHistory = new(true) };
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new PreparedSource(profile.Root) { CloseAction = () =>
        { entered.TrySetResult(); gate.Wait(TimeSpan.FromSeconds(15)); if (closeFailure) throw new IOException("injected source close failure"); } };
        var session = new AnalysisCoordinator(profile.Services.Io, "history-eligibility-control").Start([source]);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!closeFailure) session.Stop();
            var history = profile.Services.AnalysisHistory.CaptureCompletedAsync(session);
            Assert.False(history.IsCompleted); gate.Set(); await session.Completion;
            var outcome = await history; Assert.NotNull(outcome); Assert.Equal(AnalysisRollingCaptureState.Deferred, outcome.State);
            Assert.False(Directory.Exists(profile.Services.Paths.AnalysisHistoryDirectory)); Assert.Equal(1, source.CloseCalls);
        }
        finally { gate.Set(); await session.Completion; session.Dispose(); await session.DatasetCloseCompletion; }
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task SettingsHistoryPolicyRequiresItsSeparatePreservingReviewAndDisablingKeepsAllBytes(bool initiallyEnabled, bool accept)
    {
        using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        profile.Services.Settings.AnalysisPreferences = new() { RollingHistory = new(initiallyEnabled) };
        Dictionary<string, string>? historyBefore = null;
        if (initiallyEnabled)
        {
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, TestContext.Current.CancellationToken, false)) { }
            await using var store = await AnalysisRollingHistoryAccess.OpenAsync(profile.Services, TestContext.Current.CancellationToken);
            var seeded = await store.CaptureRollingAsync(new(true), Fixture(), AnalysisSavedView.Default, null, TestContext.Current.CancellationToken);
            Assert.Equal(AnalysisRollingCaptureState.Saved, seeded.State);
            historyBefore = Directory.EnumerateFiles(profile.Services.Paths.AnalysisHistoryDirectory).ToDictionary(file => Path.GetFileName(file)!, RollingHash);
        }
        var vm = new MainViewModel(profile.Services);
        var window = new MainWindow(vm, null) { Width = 900, Height = 760 };
        vm.Initialize(null);
        var dialogs = Assert.IsType<OverlayDialogService>(vm.Dialogs);
        try
        {
            window.Show(); var settings = SettingsDialog.ShowAsync(vm); await Until(() => dialogs.IsOpen); Frame(window);
            var tabs = window.GetVisualDescendants().OfType<TabControl>().Single(control => control.Items.OfType<TabItem>().Any(tab => tab.Header as string == "Analysis history"));
            tabs.SelectedItem = tabs.Items.OfType<TabItem>().Single(tab => tab.Header as string == "Analysis history"); Frame(window);
            var enabled = window.GetVisualDescendants().OfType<CheckBox>().Single(box => box.Content as string == "Keep rolling disk-space analysis history");
            Assert.Equal(initiallyEnabled, enabled.IsChecked); enabled.IsChecked = !initiallyEnabled;
            string apply = initiallyEnabled ? "Turn off history" : "Enable history and pruning", keep = initiallyEnabled ? "Keep current policy" : "Leave off";
            Click("OK"); await Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.Content as string == apply)); Frame(window);
            Assert.Equal(initiallyEnabled, profile.Services.AnalysisHistory.Policy.Enabled);
            var leave = window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == keep);
            Assert.True(leave.IsDefault); Assert.Same(leave, window.FocusManager?.GetFocusedElement());
            Click(accept ? apply : keep); await settings.WaitAsync(TimeSpan.FromSeconds(10));
            bool expected = accept ? !initiallyEnabled : initiallyEnabled;
            Assert.Equal(expected, profile.Services.AnalysisHistory.Policy.Enabled);
            var saved = JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(profile.Services.Paths.SettingsFile));
            Assert.Equal(expected, saved!.AnalysisPreferences.RollingHistory.Enabled);
            if (historyBefore is null) Assert.False(Directory.Exists(profile.Services.Paths.AnalysisHistoryDirectory));
            else
            {
                Assert.Equal(historyBefore.Keys.Order(), Directory.EnumerateFiles(profile.Services.Paths.AnalysisHistoryDirectory).Select(Path.GetFileName).Order());
                foreach (var file in historyBefore) Assert.Equal(file.Value, RollingHash(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, file.Key)));
            }
        }
        finally
        {
            dialogs.CancelAll(); window.Close();
            foreach (var tab in vm.Workspace.Panels.SelectMany(panel => panel.Tabs).ToList()) tab.Dispose();
        }
        void Click(string label) => window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Content as string == label)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    [AvaloniaFact]
    public void HistorySettingsKeepExactByteCapsAndRefuseFractionalOrEmptyLimits()
    {
        var policy = new AnalysisRollingHistoryPolicy(true, 3, 5, (1L << 30) + 1);
        var editor = new AnalysisHistorySettingsEditor(policy, "private/history");
        Assert.True(editor.TryGet(out var unchanged, out _, out _)); Assert.Equal(policy, unchanged);
        var fields = editor.View.GetLogicalDescendants().OfType<NumericUpDown>().ToArray(); Assert.Equal(3, fields.Length);
        fields[0].Value = 1.5m; Assert.False(editor.TryGet(out _, out _, out var invalid)); Assert.Same(fields[0], invalid);
        fields[0].Value = 3; fields[2].Value = null; Assert.False(editor.TryGet(out _, out _, out invalid)); Assert.Same(fields[2], invalid);
    }

    [AvaloniaFact]
    public async Task HistoryPolicyChangedDuringReviewRequiresAnotherReview()
    {
        using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        var overlay = new Grid { IsVisible = false }; var focus = new Button { Content = "Settings" };
        var window = new Window { Width = 700, Height = 650, Content = new Grid { Children = { focus, overlay } } };
        var dialogs = new OverlayDialogService(overlay, () => focus);
        try
        {
            window.Show(); var pending = profile.Services.AnalysisHistory.ReviewAndApplyAsync(dialogs, new(true));
            await Until(() => dialogs.IsOpen); Frame(window);
            var other = new AnalysisRollingHistoryPolicy(true, 20);
            profile.Services.Settings.AnalysisPreferences = new() { RollingHistory = other };
            window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Enable history and pruning")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Assert.ThrowsAsync<InvalidOperationException>(() => pending); Assert.Equal(other, profile.Services.AnalysisHistory.Policy);
            Assert.False(Directory.Exists(profile.Services.Paths.AnalysisHistoryDirectory));
        }
        finally { dialogs.CancelAll(); window.Close(); }
    }
}
