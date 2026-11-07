using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedBrowserInspectsActualPrivateBytesByGeneratedIdentityAndHandlesCorruption(bool indexed)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(indexed); var token = TestContext.Current.CancellationToken;
        const string name = "owned-inspect.fcdsa"; var window = new DiskSpaceWindow();
        try
        {
            if (indexed)
            {
                await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
                    await prepared.SaveInventoryAsync(source.Lease!, name, AnalysisSavedView.Default, token);
                await window.AddIndexedSessionAsync(source.Lease!, services: profile.Services, cancellation: token);
            }
            else
            {
                var model = new DiskSpaceViewModel(profile.Services, ImportedFixture(source.Memory!)); await model.RebuildAsync(true); await model.SaveNewAsync(name, token); window.AddSession(model);
            }
            window.Show(); Frame(window); Click(window, indexed ? "Open saved analysis" : "Open saved…");
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && Equals(b.Content, "Browse saved analyses")));
            var inspect = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && Equals(b.Content, "Inspect selected analysis")); Assert.False(inspect.IsEnabled);
            Click(window, "Browse saved analyses");
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Saved analyses" && c.ItemCount == 1));
            IndexedPersistenceControl<ComboBox>(window, "Saved analyses").SelectedIndex = 0; Assert.True(inspect.IsEnabled);
            string path = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name); byte[] original = File.ReadAllBytes(path);
            Click(window, "Inspect selected analysis");
            await Until(() => Details().Text?.Contains("File checksum and record format verified.", StringComparison.Ordinal) == true, () => Details().Text ?? "No inspection status");
            Assert.Contains(name, Details().Text); Assert.Contains(original.Length.ToString("N0") + " bytes", Details().Text);
            Assert.Contains("Location:", Details().Text); Assert.Contains("Observed:", Details().Text); Assert.True(inspect.IsEnabled);
            Assert.Equal(original, File.ReadAllBytes(path));
            string text = Encoding.UTF8.GetString(original); int at = text.LastIndexOf("\"sha256\":\"", StringComparison.Ordinal) + 10;
            Assert.True(at >= 10); var changed = text.ToCharArray(); changed[at] = changed[at] == '0' ? '1' : '0';
            byte[] corrupted = Encoding.UTF8.GetBytes(changed); File.WriteAllBytes(path, corrupted);
            Click(window, "Inspect selected analysis"); await Until(() => Details().Text?.StartsWith("Inspection unavailable:", StringComparison.Ordinal) == true);
            Assert.Equal(corrupted, File.ReadAllBytes(path)); Assert.True(inspect.IsEnabled); source.AssertPayloads();
            IndexedPersistenceControl<TextBox>(window, "Saved inventory filename").Text = "another.fcdsa";
            Frame(window);
            Assert.False(inspect.IsEnabled); Assert.Equal("", Details().Text);
            IndexedPersistenceControl<TextBox>(window, "Saved inventory filename").Text = name;
            Frame(window);
            Click(window, indexed ? "Open inventory" : "Open observations");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible &&
                AutomationProperties.GetName(t) == (indexed ? "Indexed analysis status" : "Analysis status") &&
                t.Text?.StartsWith("Inventory was not opened:", StringComparison.Ordinal) == true));
            Assert.Equal(1, window.SessionCount); Assert.Equal(corrupted, File.ReadAllBytes(path)); source.AssertPayloads();
            TextBlock Details() => window.GetVisualDescendants().OfType<TextBlock>().Single(t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) == "Saved analysis details");
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task InspectionPanelSelectionChangesSuppressLateResultsAndCloseWaitsForActualWork()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        using var model = new DiskSpaceViewModel(profile.Services, ImportedFixture()); await model.RebuildAsync(true); var receipt = await model.SaveNewAsync("panel.fcdsa");
        var value = await model.InspectSavedAsync(receipt.InventoryId); AnalysisSavedEntry? selected = value.Entry;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<OwnedAnalysisInventoryDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken capturedToken = default;
        var panel = new SavedAnalysisInspectionPanel(model.SavedDirectory!, () => selected,
            (_, token) => { capturedToken = token; entered.TrySetResult(); return release.Task; }, TestContext.Current.CancellationToken, _ => { });
        var window = new Window { Width = 380, Height = 430, Content = panel }; window.Show(); panel.SelectionChanged(); Frame(window);
        Task? closing = null;
        try
        {
            panel.Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await entered.Task; panel.SelectionChanged(); Assert.False(capturedToken.IsCancellationRequested);
            selected = null; panel.SelectionChanged(); Assert.True(capturedToken.IsCancellationRequested);
            closing = panel.CloseAsync(); Assert.False(closing.IsCompleted);
            release.SetResult(value); await closing; Assert.Equal("", panel.Children.OfType<TextBlock>().Single().Text);
        }
        finally { release.TrySetResult(value); await (closing ?? panel.CloseAsync()); window.Close(); }
    }

    [AvaloniaFact]
    public async Task NativePrivateInventoryReadersKeepSharedLeasesUntilTheirActualClose()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); string parent = profile.Root;
        await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(parent), _ =>
        {
            using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(parent, "lease-test") : new UnixAnalysisPrivateDirectory(parent, "lease-test");
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
            var snapshot = Fixture(); store.SaveNew("readers.fcdsa", snapshot, Guid.NewGuid());
            string path = Path.Combine(parent, "lease-test", "readers.fcdsa"); byte[] original = File.ReadAllBytes(path);
            using var first = directory.OpenRead("readers.fcdsa"); using var second = directory.OpenRead("readers.fcdsa");
            if (OperatingSystem.IsWindows())
            {
                Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete));
                first.Dispose(); Assert.Throws<IOException>(() => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete));
                second.Dispose(); using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); Assert.Equal(original.Length, exclusive.Length);
            }
            else
            {
                using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                int fd = probe.SafeFileHandle.DangerousGetHandle().ToInt32();
                Assert.Equal(-1, OwnedStoreFlock(fd, 2 | 4)); first.Dispose(); Assert.Equal(-1, OwnedStoreFlock(fd, 2 | 4));
                second.Dispose(); Assert.Equal(0, OwnedStoreFlock(fd, 2 | 4)); Assert.Equal(0, OwnedStoreFlock(fd, 8));
            }
            Assert.Equal(original, File.ReadAllBytes(path)); return true;
        }, TestContext.Current.CancellationToken);
    }

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int OwnedStoreFlock(int descriptor, int operation);
}
