using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task NamedReplacementRequiresDefaultKeepAndUpdatesOnlyTheReplacedSavedReference(bool indexed, bool replace, bool atCapacity)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(indexed); var token = TestContext.Current.CancellationToken;
        const string name = "replace-ui.fcdsa";
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false)) { }
        string directory = profile.Services.Paths.AnalysisSnapshotsDirectory, path = Path.Combine(directory, name);
        Guid oldId = Guid.NewGuid();
        await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(directory), _ =>
        {
            using AnalysisPrivateDirectory native = OperatingSystem.IsWindows()
                ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(directory)!, Path.GetFileName(directory)) : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(directory)!, Path.GetFileName(directory));
            using var store = new AnalysisPrivateInventoryStore(native, leaveOpen: true); new AnalysisPrivateProfileState(store).LoadOrCreate(token);
            new AnalysisSavedCatalog(store).SaveNew(name, Fixture(), oldId, AnalysisSavedView.Default, token); return true;
        }, token);
        byte[] before = File.ReadAllBytes(path);
        using var opener = new DiskSpaceViewModel(profile.Services, ImportedFixture());
        var oldView = await opener.OpenSavedAsync(name, token); await oldView.RebuildAsync(true);
        var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(oldView);
            if (atCapacity) for (int n = 0; n < 6; n++) window.AddSession(new DiskSpaceViewModel(profile.Services, ImportedFixture()));
            if (indexed) await window.AddIndexedSessionAsync(source.Lease!, services: profile.Services, cancellation: token);
            else { var model = new DiskSpaceViewModel(profile.Services, ImportedFixture(source.Memory!)); await model.RebuildAsync(true); window.AddSession(model); }
            window.Show(); Frame(window); Click(window, indexed ? "Save named analysis" : "Save…");
            await Until(() => Visible("Save new inventory") is not null);
            Frame(window);
            IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text = "REPLACE-UI.fcdsa"; Click(window, "Save new inventory");
            await Until(() => Visible("Keep existing") is not null); Frame(window);
            var keep = Visible("Keep existing")!; var replaceButton = Visible("Replace analysis")!;
            Assert.True(keep.IsDefault); Assert.True(keep.IsCancel); Assert.False(replaceButton.IsDefault); Assert.Contains("danger", replaceButton.Classes);
            string text = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
            Assert.Contains(name, text); Assert.Contains("Previous:", text); Assert.Contains("Incoming:", text); Assert.Contains("Source files are unchanged.", text);
            Assert.Equal(before, File.ReadAllBytes(path));
            if (replace) Click(window, "Replace analysis");
            else { keep.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); }
            await Until(() => Visible("Keep existing") is null && window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible &&
                (t.Text == "The existing saved analysis was kept." || t.Text?.StartsWith("The named analysis was replaced.", StringComparison.Ordinal) == true)));
            Assert.Equal(replace ? null : name, oldView.SavedInventoryName);
            await using var inspected = await AnalysisOwnedStoreAccess.OpenAsync(profile.Services, token); using var catalog = await inspected.ReadCatalogAsync(token);
            var entry = Assert.Single(catalog.Entries); Assert.Empty(catalog.PendingReplacements);
            Assert.Equal(!replace, entry.InventoryId == oldId); Assert.Equal(name, entry.Name);
            if (!replace) Assert.Equal(before, File.ReadAllBytes(path));
            else Assert.False(before.AsSpan().SequenceEqual(File.ReadAllBytes(path)));
            Assert.Equal(atCapacity ? 8 : 2, window.SessionCount); source.AssertPayloads();
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Button? Visible(string caption) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.IsEffectivelyVisible && b.Content as string == caption);
    }

    [AvaloniaTheory]
    [InlineData(false, "leave")]
    [InlineData(false, "restore")]
    [InlineData(false, "finish")]
    [InlineData(false, "copy")]
    [InlineData(true, "leave")]
    [InlineData(true, "restore")]
    [InlineData(true, "finish")]
    [InlineData(true, "copy")]
    public async Task PendingReplacementBrowserUsesAFreshPreservingRecoveryReviewAtTheTabLimit(bool indexed, string choice)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var fixture = await CreateNativeRecoveryAsync(profile, "committed", token, applicationStore: true); await using var setup = fixture.Store;
        await using var source = await NativeNamesFixture.CreateAsync(indexed);
        using var opener = new DiskSpaceViewModel(profile.Services, ImportedFixture()); var savedView = await opener.OpenSavedAsync("owned.fcdsa", token); await savedView.RebuildAsync(true);
        var window = new DiskSpaceWindow();
        try
        {
            window.AddSession(savedView); for (int n = 0; n < 6; n++) window.AddSession(new DiskSpaceViewModel(profile.Services, ImportedFixture()));
            if (indexed) await window.AddIndexedSessionAsync(source.Lease!, services: profile.Services, cancellation: token);
            else { var model = new DiskSpaceViewModel(profile.Services, ImportedFixture(source.Memory!)); await model.RebuildAsync(true); window.AddSession(model); }
            window.Show(); Frame(window); Click(window, indexed ? "Open saved analysis" : "Open saved…");
            await Until(() => Visible("Browse saved analyses") is not null); Click(window, "Browse saved analyses");
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Saved analyses" && c.ItemCount == 2));
            var choices = IndexedPersistenceControl<ComboBox>(window, "Saved analyses");
            choices.SelectedIndex = choices.Items.Cast<object>().ToList().FindIndex(item => item.ToString()!.Contains("replacement pending", StringComparison.Ordinal));
            await Until(() => Visible("Review pending replacement…")?.IsEnabled == true);
            Assert.False(Visible(indexed ? "Open inventory" : "Open observations")!.IsEnabled); Assert.False(Visible("Delete selected analysis…")!.IsEnabled);
            Click(window, "Review pending replacement…"); await Until(() => Visible("Leave pending") is not null); Frame(window);
            var preserve = Visible("Leave pending")!;
            Assert.True(preserve.IsDefault); Assert.True(preserve.IsCancel); Assert.False(Visible("Restore previous analysis")!.IsDefault);
            Assert.Contains("danger", Visible("Restore previous analysis")!.Classes); Assert.Contains("danger", Visible("Keep incoming and finish")!.Classes);
            if (choice == "leave")
            { preserve.Focus(); window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null); }
            else if (choice == "copy")
            {
                Click(window, "Copy previous to a new name…"); await Until(() => Visible("Save recovery copy") is not null);
                Frame(window);
                IndexedPersistenceControl<TextBox>(window, "Recovery inventory filename").Text = "recovered-ui.fcdsa"; Click(window, "Save recovery copy");
            }
            else Click(window, choice == "restore" ? "Restore previous analysis" : "Keep incoming and finish");
            await Until(() => Visible("Leave pending") is null && window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible &&
                t.Text is { } text && (text.StartsWith("The previous named analysis was restored.", StringComparison.Ordinal) || text.StartsWith("The incoming named analysis was confirmed.", StringComparison.Ordinal) ||
                text.StartsWith("The replacement was left pending.", StringComparison.Ordinal) || text.StartsWith("The previous inventory was copied", StringComparison.Ordinal))));
            using var catalog = await setup.ReadCatalogAsync(token);
            Assert.Equal(choice is "leave" or "copy", catalog.PendingReplacements.Length == 1);
            Assert.Equal(choice == "restore" ? fixture.OldBytes : fixture.NewBytes, File.ReadAllBytes(Path.Combine(fixture.Root, "owned.fcdsa")));
            Assert.Equal(choice == "restore" ? null : "owned.fcdsa", savedView.SavedInventoryName);
            Assert.Equal(choice == "restore", savedView.HasTemporaryWork);
            if (choice == "copy") Assert.Equal(fixture.OldBytes, File.ReadAllBytes(Path.Combine(fixture.Root, "recovered-ui.fcdsa")));
            Assert.Equal(8, window.SessionCount); source.AssertPayloads();
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Button? Visible(string caption) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.IsEffectivelyVisible && b.Content as string == caption);
    }

    [AvaloniaFact]
    public async Task UncertainRecoveryCopyPreservesOriginalSavedReferencesAndAllExistingVersions()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        MaintenanceDirectory? captured = null;
        var fixture = await CreateNativeRecoveryAsync(profile, "committed", token, wrap: native => captured = new(native));
        var review = await fixture.Store.PrepareReplacementRecoveryAsync(fixture.Operation, token); var invalidated = new List<Guid>();
        await using var operation = new AnalysisOwnedRecoveryCapture(fixture.Store, review, new CancellationTokenSource(), _ => { },
            (id, _) => { invalidated.Add(id); return Task.CompletedTask; });
        captured!.AfterPublish = name => { if (name == "uncertain-copy.fcdsa") throw new IOException("Unconfirmed copied-name durability"); };
        var error = await Assert.ThrowsAsync<OwnedAnalysisRecoveryException>(() => operation.ApplyAsync(OwnedAnalysisRecoveryAction.CopyPrevious, "uncertain-copy.fcdsa", token));
        Assert.Equal(OwnedAnalysisRecoveryState.Uncertain, error.Outcome.State); Assert.Empty(invalidated);
        Assert.Equal(fixture.NewBytes, File.ReadAllBytes(Path.Combine(fixture.Root, "owned.fcdsa")));
        Assert.Equal(fixture.OldBytes, File.ReadAllBytes(Path.Combine(fixture.Root, "uncertain-copy.fcdsa")));
        using var catalog = await fixture.Store.ReadCatalogAsync(token); Assert.Single(catalog.PendingReplacements);
    }
}
