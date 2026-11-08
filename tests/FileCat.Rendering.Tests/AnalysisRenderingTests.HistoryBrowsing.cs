using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeHistoryViewProtectsItsRollingCopyAndPinnedCopySurvivesLaterRetention(bool indexed)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(indexed); var token = TestContext.Current.CancellationToken;
        var row = await SeedHistoryAsync(profile, source);
        var ui = new HistoryDialogFixture(); var view = new DiskSpaceWindow();
        try
        {
            string root = Path.GetFullPath(source.Root), originalSource = Path.GetFullPath(source.SourcePath), offline = Path.GetFullPath(Path.Combine(source.Root, "source-offline"));
            Assert.StartsWith(root + Path.DirectorySeparatorChar, originalSource, StringComparison.Ordinal);
            Assert.StartsWith(root + Path.DirectorySeparatorChar, offline, StringComparison.Ordinal);
            Directory.Move(originalSource, offline);
            AnalysisHistoryOpenSource opened;
            try { opened = await profile.Services.AnalysisHistory.OpenHistoryAsync(row, token); }
            finally { Directory.Move(offline, originalSource); }
            var explorer = await view.AddHistorySessionAsync(profile.Services, opened, token); view.Show(); Frame(view);
            Assert.Equal(AnalysisOrigin.ImportedInventory, explorer.Descriptor.Origin);
            var deferred = await CaptureMoreHistoryAsync(profile, source.SourcePath);
            Assert.NotNull(deferred); Assert.Equal(AnalysisRollingCaptureState.Deferred, deferred.State);
            Assert.Single((await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token)).Rows);
            var pinned = await profile.Services.AnalysisHistory.PinHistoryAsync(row, "pinned-copy.fcdsa", ui.Dialogs, token);
            Assert.NotNull(pinned.Receipt); Assert.NotEqual(row.InventoryId, pinned.Receipt.InventoryId);
            string named = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, pinned.Receipt.Name), before = RollingHash(named);
            view.ForceClose(); await view.WaitForIndexedCloseAsync();
            var next = await CaptureMoreHistoryAsync(profile, source.SourcePath);
            Assert.NotNull(next); Assert.Equal(AnalysisRollingCaptureState.Saved, next.State);
            Assert.Equal(row.InventoryId, Assert.Single(next.Pruned)); Assert.Equal(before, RollingHash(named));
            Assert.False(File.Exists(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name)));
            source.AssertPayloads();
        }
        finally { view.ForceClose(); await view.WaitForIndexedCloseAsync(); ui.Dispose(); }
    }

    [AvaloniaFact]
    public async Task NativeHistoryRemovalRejectsActiveWorkAndRequiresAFreshReviewAfterRefusal()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var row = await SeedHistoryAsync(profile, source);
        var manager = profile.Services.AnalysisHistory;
        await using (var review = await manager.PrepareHistoryMaintenanceAsync(row, token))
        {
            await using (var opened = await manager.OpenHistoryAsync(row, token))
                await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ChangeHistoryAsync(review, false, token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ChangeHistoryAsync(review, false, token));
        }
        Assert.True(File.Exists(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name)));
        await using (var fresh = await manager.PrepareHistoryMaintenanceAsync(row, token))
            Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await manager.ChangeHistoryAsync(fresh, false, token)).State);
        Assert.Empty((await manager.ReadHistoryPageAsync(0, token)).Rows); source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeUnindexedHistoryRecoveryChecksTheExactGraphAndRejectsChangedBytesBeforeCataloging()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var row = await SeedHistoryAsync(profile, source);
        MakeHistoryUnindexed(profile, row);
        var manager = profile.Services.AnalysisHistory; row = Assert.Single((await manager.ReadHistoryPageAsync(0, token)).Rows);
        Assert.Equal(AnalysisHistoryRowState.Unindexed, row.State);
        string path = Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name); byte[] original = await File.ReadAllBytesAsync(path, token);
        await using (var review = await manager.PrepareHistoryRecoveryAsync(row, token))
        {
            byte[] corrupt = (byte[])original.Clone(); corrupt[0] = (byte)'!'; await File.WriteAllBytesAsync(path, corrupt, token);
            await Assert.ThrowsAsync<AnalysisRollingCaptureException>(() => manager.RecoverHistoryAsync(review, token));
            Assert.Equal(corrupt, await File.ReadAllBytesAsync(path, token));
            await File.WriteAllBytesAsync(path, original, token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RecoverHistoryAsync(review, token));
        }
        await using (var fresh = await manager.PrepareHistoryRecoveryAsync(row, token))
        {
            var result = await manager.RecoverHistoryAsync(fresh, token);
            Assert.True(result.Cataloged); Assert.Equal(AnalysisRollingCaptureState.Saved, result.State); Assert.Empty(result.Pruned);
        }
        Assert.Equal(original, await File.ReadAllBytesAsync(path, token));
        Assert.Equal(AnalysisHistoryRowState.Saved, Assert.Single((await manager.ReadHistoryPageAsync(0, token)).Rows).State);
        source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeHistoryRecoveryRefusesAChecksummedContainerWithAnInvalidRecordGraph()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var row = await SeedHistoryAsync(profile, source); MakeHistoryUnindexed(profile, row);
        string path = Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name);
        var lines = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(path, token)).TrimEnd('\n').Split('\n');
        int at = Array.FindIndex(lines, line => JsonNode.Parse(line)!["kind"]!.GetValue<string>() == "path-node" && JsonNode.Parse(line)!["parent"]!.GetValue<string>() != "0");
        Assert.True(at > 0);
        var node = JsonNode.Parse(lines[at])!; node["parent"] = "18446744073709551500"; lines[at] = node.ToJsonString();
        var trailer = JsonNode.Parse(lines[^1])!; trailer["sha256"] = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines[..^1]) + "\n")));
        lines[^1] = trailer.ToJsonString(); byte[] forged = Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n"); await File.WriteAllBytesAsync(path, forged, token);
        var manager = profile.Services.AnalysisHistory; row = Assert.Single((await manager.ReadHistoryPageAsync(0, token)).Rows);
        await using (var owner = await AnalysisRollingHistoryAccess.OpenReviewAsync(profile.Services, token))
            Assert.Equal(forged.Length, (await owner.PrepareRollingRecoveryAsync(row.Name, token)).Bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.PrepareHistoryRecoveryAsync(row, token));
        Assert.Equal(forged, await File.ReadAllBytesAsync(path, token)); Assert.Equal(AnalysisHistoryRowState.Unindexed, Assert.Single((await manager.ReadHistoryPageAsync(0, token)).Rows).State);
        source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task HistoryBrowserRemovalUsesAPreservingDefaultAndScopedDangerReview(bool remove)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(false); var row = await SeedHistoryAsync(profile, source); var token = TestContext.Current.CancellationToken;
        using var ui = new HistoryDialogFixture(480, 620, 20);
        var browser = AnalysisHistoryBrowser.ShowAsync(profile.Services, ui.Dialogs, token);
        await Until(() => ui.Window.GetVisualDescendants().OfType<ListBox>().Any(list => Avalonia.Automation.AutomationProperties.GetName(list) == "Rolling history entries"));
        var list = ui.Window.GetVisualDescendants().OfType<ListBox>().Single(list => Avalonia.Automation.AutomationProperties.GetName(list) == "Rolling history entries");
        list.SelectedIndex = 0; Frame(ui.Window); ui.Click("Review removal");
        await Until(() => ui.Window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Keep history entry")); Frame(ui.Window);
        var keep = ui.Find("Keep history entry"); var erase = ui.Find("Remove history entry");
        Assert.True(keep.IsDefault); Assert.True(keep.IsCancel); Assert.Same(keep, ui.Window.FocusManager?.GetFocusedElement());
        Assert.Contains("danger", erase.Classes); Assert.False(erase.IsDefault);
        string text = string.Join('\n', ui.Window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
        Assert.Contains("Named copies and source files are unchanged", text); Assert.Contains("not secure erasure", text);
        ui.Click(remove ? "Remove history entry" : "Keep history entry");
        await Until(() => ui.Window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Refresh"));
        Frame(ui.Window); ui.Click("Close"); await browser.WaitAsync(TimeSpan.FromSeconds(10), token);
        Assert.Equal(!remove, File.Exists(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name))); source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeHistoryPinReplacementKeepsTheOldNameUnlessItsPreparedReviewIsAccepted(bool replace)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(false); var row = await SeedHistoryAsync(profile, source); var token = TestContext.Current.CancellationToken;
        using var loader = new DiskSpaceViewModel(profile.Services, ImportedFixture()); await loader.RebuildAsync(true, token); await loader.SaveNewAsync("existing.fcdsa", token);
        using var originalView = await loader.OpenSavedAsync("existing.fcdsa", token);
        string named = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, "existing.fcdsa"), before = RollingHash(named);
        using var ui = new HistoryDialogFixture(); var saving = profile.Services.AnalysisHistory.PinHistoryAsync(row, "existing.fcdsa", ui.Dialogs, token);
        await Until(() => ui.Window.GetVisualDescendants().OfType<Button>().Any(button => button.Content as string == "Keep existing")); Frame(ui.Window);
        Assert.True(ui.Find("Keep existing").IsDefault); Assert.False(ui.Find("Replace analysis").IsDefault);
        ui.Click(replace ? "Replace analysis" : "Keep existing"); var result = await saving.WaitAsync(TimeSpan.FromSeconds(20), token);
        Assert.Equal(replace, result.Receipt is not null); Assert.Equal(!replace, before == RollingHash(named));
        Assert.Equal(replace ? null : "existing.fcdsa", originalView.SavedInventoryName);
        Assert.True(File.Exists(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name))); source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeHistoryRecoveryGraphAttestationMustMatchTheReviewedSourceDigest()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var row = await SeedHistoryAsync(profile, source); MakeHistoryUnindexed(profile, row);
        string path = Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name); byte[] original = await File.ReadAllBytesAsync(path, token);
        await using var owner = await AnalysisRollingHistoryAccess.OpenReviewAsync(profile.Services, token);
        var review = await owner.PrepareRollingRecoveryAsync(row.Name, token);
        using var alternate = new MemoryStream();
        var snapshot = source.Memory! with { Identity = source.Memory!.Identity with { SessionId = Guid.NewGuid() } };
        AnalysisInventoryFormat.Write(alternate, snapshot, review.InventoryId, token);
        Assert.Equal(original.Length, alternate.Length); Assert.NotEqual(Convert.ToHexStringLower(SHA256.HashData(original)), Convert.ToHexStringLower(SHA256.HashData(alternate.ToArray())));
        await File.WriteAllBytesAsync(path, alternate.ToArray(), token);
        await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token);
        var entry = new AnalysisSavedEntry(review.InventoryId, row.Name, review.Bytes, DateTimeOffset.UtcNow, review.Inspection.Coverage, review.Inspection.Taint, AnalysisSavedView.Default);
        await using var validated = await prepared.LoadHistoryInventoryAsync(entry, token);
        await File.WriteAllBytesAsync(path, original, token);
        await Assert.ThrowsAsync<InvalidDataException>(() => owner.RecoverRollingAsync(review, validated, token));
        Assert.Equal(original, await File.ReadAllBytesAsync(path, token));
        using var catalog = await owner.ReadCatalogAsync(token); Assert.Empty(catalog.Entries); Assert.Equal(row.Name, Assert.Single(catalog.UnindexedNames));
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativePendingHistoryCanBeExplicitlyRestoredOrItsRemovalFinished(bool restore)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var row = await SeedHistoryAsync(profile, source);
        string root = Path.GetFullPath(profile.Services.Paths.AnalysisHistoryDirectory);
        string original = Path.GetFullPath(Path.Combine(root, $"catalog-{row.CatalogId:N}-{row.InventoryId:N}.json"));
        string pending = Path.GetFullPath(Path.Combine(root, $"delete-{row.CatalogId:N}-{row.InventoryId:N}-{Guid.NewGuid():N}.json"));
        Assert.Equal(root, Path.GetDirectoryName(original)); Assert.Equal(root, Path.GetDirectoryName(pending));
        File.Move(original, pending);
        var manager = profile.Services.AnalysisHistory; row = Assert.Single((await manager.ReadHistoryPageAsync(0, token)).Rows);
        Assert.Equal(AnalysisHistoryRowState.PendingPresent, row.State);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.OpenHistoryAsync(row, token));
        await using (var reviewed = await manager.PrepareHistoryMaintenanceAsync(row, token))
            Assert.Equal(restore ? OwnedAnalysisDeletionState.Restored : OwnedAnalysisDeletionState.Deleted, (await manager.ChangeHistoryAsync(reviewed, restore, token)).State);
        var after = await manager.ReadHistoryPageAsync(0, token);
        if (restore) Assert.Equal(AnalysisHistoryRowState.Saved, Assert.Single(after.Rows).State); else Assert.Empty(after.Rows);
        Assert.Equal(restore, File.Exists(Path.Combine(root, row.Name))); Assert.False(File.Exists(pending)); source.AssertPayloads();
    }

    private static async Task<AnalysisHistoryRow> SeedHistoryAsync(PreparationFixture profile, NativeNamesFixture source)
    {
        var token = TestContext.Current.CancellationToken;
        profile.Services.Settings.AnalysisPreferences = new() { RollingHistory = new(true, 1) };
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, false)) { }
        await using (var store = await AnalysisRollingHistoryAccess.OpenAsync(profile.Services, token))
        {
            var result = source.Lease is { } indexed ? await store.CaptureRollingAsync(new(true), indexed, AnalysisSavedView.Default, null, token)
                : await store.CaptureRollingAsync(new(true), source.Memory!, AnalysisSavedView.Default, null, token);
            Assert.Equal(AnalysisRollingCaptureState.Saved, result.State);
        }
        return Assert.Single((await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token)).Rows);
    }
    private static async Task<AnalysisRollingCaptureOutcome?> CaptureMoreHistoryAsync(PreparationFixture profile, string path)
    {
        var token = TestContext.Current.CancellationToken;
        await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, false);
        IAnalysisBackend? source = await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), _ => NativeRoot(path), token);
        try { var session = prepared.Start([source]); source = null; await session.Completion.WaitAsync(TimeSpan.FromSeconds(30), token); return await prepared.HistoryCompletion; }
        finally { if (source is not null) await AnalysisIo.ReleaseAsync(profile.Services.Io, source); }
    }
    private static void MakeHistoryUnindexed(PreparationFixture profile, AnalysisHistoryRow row) =>
        File.Delete(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, $"catalog-{row.CatalogId:N}-{row.InventoryId:N}.json"));
    private sealed class HistoryDialogFixture : IDisposable
    {
        internal Window Window { get; }
        internal OverlayDialogService Dialogs { get; }
        internal HistoryDialogFixture(double width = 800, double height = 700, double size = 13)
        {
            var overlay = new Grid { IsVisible = false }; var focus = new Button { Content = "History" };
            Window = new() { Width = width, Height = height, FontSize = size, Content = new Grid { Children = { focus, overlay } } };
            Dialogs = new(overlay, () => focus); Window.Show(); Frame(Window);
        }
        internal Button Find(string label) => Window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && button.Content as string == label);
        internal void Click(string label) { var button = Find(label); button.BringIntoView(); Frame(Window); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        public void Dispose() { Dialogs.CancelAll(); Window.Close(); }
    }
}
