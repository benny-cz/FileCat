using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeRollingCaptureConfirmsCompleteBytesBeforePruningAndKeepsNamedFiles(bool indexed)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await using var source = await NativeNamesFixture.CreateAsync(indexed);
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false)) { }
        string named = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, "keep-named.fcdsa");
        using (var model = new FileCat.App.ViewModels.DiskSpaceViewModel(profile.Services, ImportedFixture()))
        { await model.RebuildAsync(true, token); await model.SaveNewAsync("keep-named.fcdsa", token); }
        string namedHash = RollingHash(named);
        await using var history = await AnalysisRollingHistoryAccess.OpenAsync(profile.Services, token);
        var policy = new AnalysisRollingHistoryPolicy(true, 1);
        var first = await Capture(policy); Assert.Equal(AnalysisRollingCaptureState.Saved, first.State); Assert.True(first.Cataloged);
        Assert.NotNull(first.Receipt); Assert.Empty(first.Pruned);
        string firstPath = Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, first.Receipt.Name);
        Assert.True(File.Exists(firstPath));
        var second = await Capture(policy); Assert.Equal(AnalysisRollingCaptureState.Saved, second.State);
        Assert.Equal(first.Receipt.InventoryId, Assert.Single(second.Pruned)); Assert.False(File.Exists(firstPath));
        using (var catalog = await history.ReadCatalogAsync(token))
        {
            var entry = Assert.Single(catalog.Entries); Assert.Empty(catalog.PendingDeletions); Assert.Empty(catalog.UnindexedNames);
            Assert.Equal(second.Receipt!.InventoryId, entry.InventoryId);
            var inspected = await history.InspectAsync(entry.InventoryId, token);
            Assert.Equal(RollingHash(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, entry.Name)), inspected.Sha256);
        }
        string secondPath = Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, second.Receipt!.Name), before = RollingHash(secondPath);
        var tooLarge = await Capture(policy with { MaximumBytes = 1 });
        Assert.Equal(AnalysisRollingCaptureState.TooLarge, tooLarge.State); Assert.Null(tooLarge.Receipt); Assert.Empty(tooLarge.Pruned);
        Assert.Equal(before, RollingHash(secondPath)); Assert.Equal(namedHash, RollingHash(named));
        Assert.Empty(Directory.EnumerateFiles(profile.Services.Paths.AnalysisHistoryDirectory, ".staging-*")); source.AssertPayloads();

        Task<AnalysisRollingCaptureOutcome> Capture(AnalysisRollingHistoryPolicy p) => indexed
            ? history.CaptureRollingAsync(p, source.Lease!, AnalysisSavedView.Default, null, token)
            : history.CaptureRollingAsync(p, source.Memory!, AnalysisSavedView.Default, null, token);
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task NativeRollingProtectionAndDisabledPolicyLeaveEveryCurrentByteUntouched(bool indexed)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await using var source = await NativeNamesFixture.CreateAsync(indexed);
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false)) { }
        await using var history = await AnalysisRollingHistoryAccess.OpenAsync(profile.Services, token);
        var policy = new AnalysisRollingHistoryPolicy(true, 1);
        var first = await Capture(policy, null); Assert.Equal(AnalysisRollingCaptureState.Saved, first.State);
        var before = Directory.EnumerateFiles(profile.Services.Paths.AnalysisHistoryDirectory).ToDictionary(file => Path.GetFileName(file)!, RollingHash);
        var protectedResult = await Capture(policy, new HashSet<Guid> { first.Receipt!.InventoryId });
        Assert.Equal(AnalysisRollingCaptureState.Deferred, protectedResult.State); Assert.Empty(protectedResult.Pruned);
        var disabled = await Capture(policy with { Enabled = false }, null); Assert.Equal(AnalysisRollingCaptureState.Disabled, disabled.State);
        Assert.Equal(before.Keys.Order(), Directory.EnumerateFiles(profile.Services.Paths.AnalysisHistoryDirectory).Select(Path.GetFileName).Order());
        foreach (var item in before) Assert.Equal(item.Value, RollingHash(Path.Combine(profile.Services.Paths.AnalysisHistoryDirectory, item.Key!)));
        source.AssertPayloads();
        Task<AnalysisRollingCaptureOutcome> Capture(AnalysisRollingHistoryPolicy p, IReadOnlySet<Guid>? protect) => indexed
            ? history.CaptureRollingAsync(p, source.Lease!, AnalysisSavedView.Default, protect, token)
            : history.CaptureRollingAsync(p, source.Memory!, AnalysisSavedView.Default, protect, token);
    }

    [AvaloniaFact]
    public async Task NativeNamedOwnerCannotBecomeAutomaticHistoryAndNonHistoryFilesArePreserved()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false)) { }
        await using var named = await AnalysisOwnedStoreAccess.OpenAsync(profile.Services, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => named.CaptureRollingAsync(new(true), Fixture(), AnalysisSavedView.Default, null, token));
        string root = profile.Services.Paths.AnalysisHistoryDirectory;
        await using (var empty = await AnalysisRollingHistoryAccess.OpenAsync(profile.Services, token)) { }
        string unrelated = Path.Combine(root, "unrelated.txt"); await File.WriteAllTextAsync(unrelated, "keep unrelated data", token);
        await Assert.ThrowsAsync<InvalidDataException>(() => AnalysisRollingHistoryAccess.OpenAsync(profile.Services, token));
        Assert.Equal("keep unrelated data", await File.ReadAllTextAsync(unrelated, token));
    }

    private static string RollingHash(string path)
    { using var stream = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(stream)); }
}
