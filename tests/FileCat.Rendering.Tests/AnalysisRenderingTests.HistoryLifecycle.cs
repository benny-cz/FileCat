using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData("count")] [InlineData("age")] [InlineData("bytes")]
    public async Task NativeIdleHistoryEnforcesEachCapWithoutAnotherAcquisition(string cap)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var rows = await SeedLifecycleHistoryAsync(profile, source);
        long one = rows[0].Bytes!.Value;
        profile.Services.Settings.AnalysisPreferences = new() { RollingHistory = cap switch
        {
            "age" => new(true, 10, 2), "bytes" => new(true, 10, 30, one), _ => new(true, 1)
        } };
        if (cap == "age") foreach (var row in rows) SetHistorySavedTime(profile, row, DateTimeOffset.UtcNow.AddDays(-3));
        string named = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, "keep-named.fcdsa"); File.WriteAllText(named, "named sentinel");
        var outcome = await profile.Services.AnalysisHistory.RunIdleMaintenanceAsync(token);
        Assert.NotNull(outcome); Assert.Equal(AnalysisRollingCaptureState.Maintained, outcome.State);
        Assert.Null(outcome.Receipt); Assert.False(outcome.Cataloged);
        Assert.Equal(cap == "age" ? 3 : 2, outcome.Pruned.Length);
        var after = await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token);
        Assert.Equal(cap == "age" ? 0 : 1, after.Total);
        foreach (Guid id in outcome.Pruned) Assert.False(File.Exists(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, OwnedAnalysisStore.RollingName(id))));
        Assert.Equal("named sentinel", File.ReadAllText(named)); source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData("future")] [InlineData("corrupt")] [InlineData("unindexed")]
    public async Task NativeIdleHistoryDefersBeforeAnyPruningForUnresolvedData(string reason)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var rows = await SeedLifecycleHistoryAsync(profile, source);
        var target = rows[1];
        string path = Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, target.Name);
        if (reason == "future") SetHistorySavedTime(profile, target, DateTimeOffset.UtcNow.AddDays(1));
        if (reason == "corrupt") await File.WriteAllBytesAsync(path, [1, 2, 3], token);
        if (reason == "unindexed") MakeHistoryUnindexed(profile, target);
        var before = rows.ToDictionary(row => row.Name, row => RollingHash(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name)));
        profile.Services.Settings.AnalysisPreferences = new() { RollingHistory = new(true, 1) };
        var outcome = await profile.Services.AnalysisHistory.RunIdleMaintenanceAsync(token);
        Assert.NotNull(outcome); Assert.Equal(AnalysisRollingCaptureState.Deferred, outcome.State); Assert.Empty(outcome.Pruned);
        foreach (var file in before) Assert.Equal(file.Value, RollingHash(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, file.Key)));
        Assert.Equal(3, (await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token)).Total); source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeHistoryStartupSweepWaitsForProtectedWorkAndWakesWhenItsOwnerActuallyCloses()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var row = await SeedHistoryAsync(profile, source); var manager = profile.Services.AnalysisHistory;
        var opened = await manager.OpenHistoryAsync(row, token);
        SetHistorySavedTime(profile, row, DateTimeOffset.UtcNow.AddDays(-31));
        manager.StartMaintenance();
        try
        {
            await Until(() => manager.LastMaintenance is not null);
            Assert.Equal(AnalysisRollingCaptureState.Deferred, manager.LastMaintenance!.State);
            Assert.True(File.Exists(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name)));
            await opened.DisposeAsync();
            await Until(() => manager.LastMaintenance?.Pruned.Contains(row.InventoryId) == true);
            Assert.Empty((await manager.ReadHistoryPageAsync(0, token)).Rows);
        }
        finally { await opened.DisposeAsync(); await manager.StopMaintenanceAsync().WaitAsync(TimeSpan.FromSeconds(20), token); }
        source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeHistoryReviewedPolicyChangeWakesTheSingleIdleWorker()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(false); var token = TestContext.Current.CancellationToken;
        await SeedLifecycleHistoryAsync(profile, source); var manager = profile.Services.AnalysisHistory;
        profile.Services.Settings.AnalysisPreferences = new() { RollingHistory = new(true, 10) };
        using var ui = new HistoryDialogFixture(); manager.StartMaintenance(); manager.StartMaintenance();
        try
        {
            await Until(() => manager.LastMaintenance is not null); Assert.Empty(manager.LastMaintenance!.Pruned);
            var applying = manager.ReviewAndApplyAsync(ui.Dialogs, new(true, 1), token);
            await Until(() => ui.Dialogs.IsOpen); ui.Click("Apply retention and pruning"); Assert.True(await applying);
            await Until(() => manager.LastMaintenance?.Pruned.Length == 2);
            Assert.Single((await manager.ReadHistoryPageAsync(0, token)).Rows);
        }
        finally { await manager.StopMaintenanceAsync().WaitAsync(TimeSpan.FromSeconds(20), token); }
        source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeHistoryDisabledStartupCreatesNoStoreAndDisablingKeepsExpiredCopies()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var manager = profile.Services.AnalysisHistory;
        manager.StartMaintenance(); await manager.StopMaintenanceAsync(); Assert.False(Directory.Exists(profile.Services.Paths.AnalysisHistoryDirectory));
        await using var source = await NativeNamesFixture.CreateAsync(false); var token = TestContext.Current.CancellationToken;
        var row = await SeedHistoryAsync(profile, source); SetHistorySavedTime(profile, row, DateTimeOffset.UtcNow.AddDays(-60));
        profile.Services.Settings.AnalysisPreferences = new() { RollingHistory = new(false, 1, 1, 1) };
        Assert.Null(await manager.RunIdleMaintenanceAsync(token));
        Assert.True(File.Exists(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name))); source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData(false, 0)] [InlineData(false, 7)] [InlineData(true, 0)] [InlineData(true, 7)]
    public async Task NativeOpaqueHistoryRemovalDeletesOnlyReviewedCorruptBytesIncludingEmptyOrTruncatedCopies(bool unindexed, int bytes)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var rows = await SeedLifecycleHistoryAsync(profile, source); var row = rows[0];
        string path = Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name);
        byte[] corrupt = Enumerable.Repeat((byte)0x5a, bytes).ToArray(); await File.WriteAllBytesAsync(path, corrupt, token);
        if (unindexed) MakeHistoryUnindexed(profile, row);
        row = (await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token)).Rows.Single(item => item.InventoryId == row.InventoryId);
        var others = rows.Skip(1).ToDictionary(item => item.Name, item => RollingHash(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, item.Name)));
        await using (var reviewed = await profile.Services.AnalysisHistory.PrepareOpaqueHistoryRemovalAsync(row, token))
        {
            Assert.Equal(bytes, reviewed.Review.Bytes); Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(corrupt)), reviewed.Review.Sha256);
            var outcome = await profile.Services.AnalysisHistory.RemoveOpaqueHistoryAsync(reviewed, token);
            Assert.Equal(OwnedAnalysisDeletionState.Deleted, outcome.State); Assert.True(outcome.PayloadAbsentConfirmed);
        }
        Assert.False(File.Exists(path)); Assert.Equal(2, (await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token)).Total);
        foreach (var file in others) Assert.Equal(file.Value, RollingHash(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, file.Key)));
        source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData(false, false)] [InlineData(true, false)] [InlineData(false, true)] [InlineData(true, true)]
    public async Task NativeOpaqueHistoryRemovalRefusesChangedBytesOrAnIdenticalReplacementAndRequiresFreshReview(bool unindexed, bool replacement)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var row = await SeedHistoryAsync(profile, source);
        if (unindexed) MakeHistoryUnindexed(profile, row);
        row = Assert.Single((await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token)).Rows);
        string path = Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name); byte[] original = await File.ReadAllBytesAsync(path, token);
        await using (var reviewed = await profile.Services.AnalysisHistory.PrepareOpaqueHistoryRemovalAsync(row, token))
        {
            if (replacement)
            {
                string backup = Path.GetFullPath(Path.Combine(profile.Root, "kept-original.fcdsa"));
                Assert.StartsWith(Path.GetFullPath(profile.Root) + Path.DirectorySeparatorChar, backup, StringComparison.Ordinal);
                File.Move(path, backup);
                await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), _ =>
                {
                    string history = profile.Services.Paths.AnalysisHistoryDirectory;
                    using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows()
                        ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(history)!, Path.GetFileName(history))
                        : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(history)!, Path.GetFileName(history));
                    using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
                    store.SaveNew(row.Name, source.Memory!, row.InventoryId, token); return true;
                }, token);
                Assert.Equal(original, await File.ReadAllBytesAsync(path, token));
            }
            else { original[0] ^= 1; await File.WriteAllBytesAsync(path, original, token); }
            var error = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => profile.Services.AnalysisHistory.RemoveOpaqueHistoryAsync(reviewed, token));
            Assert.Equal(OwnedAnalysisDeletionState.Kept, error.Outcome.State); Assert.Equal(original, await File.ReadAllBytesAsync(path, token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => profile.Services.AnalysisHistory.RemoveOpaqueHistoryAsync(reviewed, token));
        }
        await using (var fresh = await profile.Services.AnalysisHistory.PrepareOpaqueHistoryRemovalAsync(row, token))
            Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await profile.Services.AnalysisHistory.RemoveOpaqueHistoryAsync(fresh, token)).State);
        Assert.Empty((await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token)).Rows); source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task OpaqueHistoryRemovalReviewKeepsTheNegativeDefaultAtNarrowLargeText(bool remove)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(false); var token = TestContext.Current.CancellationToken;
        var row = await SeedHistoryAsync(profile, source); MakeHistoryUnindexed(profile, row);
        row = Assert.Single((await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token)).Rows);
        await using var reviewed = await profile.Services.AnalysisHistory.PrepareOpaqueHistoryRemovalAsync(row, token);
        using var ui = new HistoryDialogFixture(480, 620, 20);
        var choice = AnalysisHistoryBrowser.ConfirmOpaqueRemovalAsync(ui.Dialogs, reviewed.Review, profile.Services.Paths.AnalysisHistoryDirectory);
        await Until(() => ui.Dialogs.IsOpen); Frame(ui.Window); var keep = ui.Find("Keep private copy");
        Assert.True(keep.IsDefault); Assert.Same(keep, ui.Window.FocusManager?.GetFocusedElement());
        Assert.Contains("danger", ui.Find("Remove private history copy").Classes);
        ui.Click(remove ? "Remove private history copy" : "Keep private copy"); Assert.Equal(remove, await choice);
        if (remove) await profile.Services.AnalysisHistory.RemoveOpaqueHistoryAsync(reviewed, token);
        Assert.Equal(!remove, File.Exists(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name))); source.AssertPayloads();
    }

    [AvaloniaFact]
    public async Task NativeOpaqueHistoryRemovalProtectsAnOpenImportedViewAndRefusesReplayedIntent()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var row = await SeedHistoryAsync(profile, source); var manager = profile.Services.AnalysisHistory;
        var opened = await manager.OpenHistoryAsync(row, token);
        try
        {
            await using var review = await manager.PrepareOpaqueHistoryRemovalAsync(row, token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RemoveOpaqueHistoryAsync(review, token));
            await opened.DisposeAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RemoveOpaqueHistoryAsync(review, token));
            Assert.True(File.Exists(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, row.Name)));
        }
        finally { await opened.DisposeAsync(); }
        await using (var fresh = await manager.PrepareOpaqueHistoryRemovalAsync(row, token))
            Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await manager.RemoveOpaqueHistoryAsync(fresh, token)).State);
        source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeOpaqueHistoryRemovalUsesPendingMetadataAndNeverRestoresCorruptBytes(bool present)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(false);
        var token = TestContext.Current.CancellationToken; var row = await SeedHistoryAsync(profile, source);
        string root = Path.GetFullPath(profile.Services.Paths.AnalysisHistoryDirectory);
        string metadata = Path.GetFullPath(Path.Combine(root, $"catalog-{row.CatalogId:N}-{row.InventoryId:N}.json"));
        string pending = Path.GetFullPath(Path.Combine(root, $"delete-{row.CatalogId:N}-{row.InventoryId:N}-{Guid.NewGuid():N}.json"));
        Assert.Equal(root, Path.GetDirectoryName(metadata)); Assert.Equal(root, Path.GetDirectoryName(pending)); File.Move(metadata, pending);
        string payload = Path.Combine(root, row.Name);
        if (present) await File.WriteAllBytesAsync(payload, [1, 2, 3], token); else File.Delete(payload);
        var manager = profile.Services.AnalysisHistory; row = Assert.Single((await manager.ReadHistoryPageAsync(0, token)).Rows);
        if (present)
        {
            await using var review = await manager.PrepareOpaqueHistoryRemovalAsync(row, token);
            Assert.NotNull(review.Review.Indexed);
            await Assert.ThrowsAsync<InvalidOperationException>(() => review.Store.RestoreNamedAsync(review.Review.Indexed, token));
            Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await manager.RemoveOpaqueHistoryAsync(review, token)).State);
        }
        else
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => manager.PrepareOpaqueHistoryRemovalAsync(row, token));
            await using var review = await manager.PrepareHistoryMaintenanceAsync(row, token);
            Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await manager.ChangeHistoryAsync(review, false, token)).State);
        }
        Assert.False(File.Exists(pending)); Assert.Empty((await manager.ReadHistoryPageAsync(0, token)).Rows); source.AssertPayloads();
    }

    private static async Task<AnalysisHistoryRow[]> SeedLifecycleHistoryAsync(PreparationFixture profile, NativeNamesFixture source)
    {
        var token = TestContext.Current.CancellationToken; await SeedHistoryAsync(profile, source);
        await using (var store = await AnalysisRollingHistoryAccess.OpenAsync(profile.Services, token))
            for (int i = 0; i < 2; i++) Assert.Equal(AnalysisRollingCaptureState.Saved, (await store.CaptureRollingAsync(new(true, 10), source.Memory!, AnalysisSavedView.Default, null, token)).State);
        var rows = (await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token)).Rows.OrderBy(row => row.SavedUtc).ToArray();
        for (int i = 0; i < rows.Length; i++) SetHistorySavedTime(profile, rows[i], DateTimeOffset.UtcNow.AddDays(-5 + i));
        return (await profile.Services.AnalysisHistory.ReadHistoryPageAsync(0, token)).Rows.OrderBy(row => row.SavedUtc).ToArray();
    }
    private static void SetHistorySavedTime(PreparationFixture profile, AnalysisHistoryRow row, DateTimeOffset time)
    {
        string root = Path.GetFullPath(profile.Services.Paths.AnalysisHistoryDirectory);
        string path = Path.GetFullPath(Path.Combine(root, $"catalog-{row.CatalogId:N}-{row.InventoryId:N}.json"));
        Assert.Equal(root, Path.GetDirectoryName(path)); var node = JsonNode.Parse(File.ReadAllText(path))!;
        node["saved"] = time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture); File.WriteAllText(path, node.ToJsonString());
    }
}
