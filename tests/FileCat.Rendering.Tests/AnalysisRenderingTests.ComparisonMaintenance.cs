using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
    public async Task KnownNamedRemovalClearsTheSavedFilenameInMemoryAndIndexedViews(bool indexed)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); using var styles = new ProductStyles();
        await using var source = await NativeNamesFixture.CreateAsync(indexed); var token = TestContext.Current.CancellationToken; var window = new DiskSpaceWindow();
        const string name = "removed-reference.fcdsa";
        try
        {
            if (indexed) await window.AddIndexedSessionAsync(source.Lease!, services: profile.Services, cancellation: token);
            else
            {
                var model = new DiskSpaceViewModel(profile.Services, ImportedFixture(source.Memory!)); await model.RebuildAsync(true); window.AddSession(model);
            }
            window.Show(); Frame(window); Click(window, indexed ? "Save named analysis" : "Save…");
            await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) == "Inventory filename"));
            IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text = name;
            await Until(() => Visible("Save new inventory")?.IsEnabled == true); Click(window, "Save new inventory");
            string path = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name);
            await Until(() => File.Exists(path) && Visible(indexed ? "Open saved analysis" : "Open saved…")?.IsEnabled == true);
            Click(window, indexed ? "Open saved analysis" : "Open saved…"); await Until(() => Visible("Browse saved analyses") is not null); Click(window, "Browse saved analyses");
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Saved analyses" && c.ItemCount == 1));
            IndexedPersistenceControl<ComboBox>(window, "Saved analyses").SelectedIndex = 0;
            await Until(() => Visible("Delete selected analysis…")?.IsEnabled == true); Click(window, "Delete selected analysis…");
            await Until(() => Visible("Keep analysis") is not null); Frame(window); Click(window, "Delete analysis");
            await Until(() => !File.Exists(path) && Visible(indexed ? "Open saved analysis" : "Open saved…")?.IsEnabled == true);
            Click(window, indexed ? "Save named analysis" : "Save…");
            await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) == "Inventory filename"));
            Assert.NotEqual(name, IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text);
            Click(window, "Cancel"); Assert.Equal(1, window.SessionCount); source.AssertPayloads();
            Button? Visible(string label) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.IsEffectivelyVisible && Equals(b.Content, label));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task ComparisonRemovalClearsItsSavedReferenceWithoutErasingInputsOrStrandingObservers()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        using var model = new DiskSpaceViewModel(profile.Services, ImportedFixture()); await model.RebuildAsync(true); await model.SaveNewAsync("before.fcdsa", token);
        using var first = await model.CompareSavedAsync("before.fcdsa", token); var receipt = await first.SaveComparisonNewAsync("pair.fcdsc", token);
        using var other = new DiskSpaceViewModel(profile.Services, ImportedFixture()); await other.RebuildAsync(true); await other.SaveNewAsync("independent.fcdsa", token);
        await profile.Services.AnalysisSavedReferences.RemovedAsync(Guid.NewGuid(), "pair.fcdsc", comparison: true);
        await profile.Services.AnalysisSavedReferences.RemovedAsync(receipt.ComparisonId, "other.fcdsc", comparison: true);
        Assert.False(first.HasTemporaryWork); Assert.Equal("independent.fcdsa", other.SavedInventoryName);
        Action broken = () => throw new InvalidOperationException("Broken view observer"); first.Changed += broken;
        var observed = new List<(Guid Id, string Name, bool Comparison)>();
        var observer = new SavedReferenceObserver((id, name, comparison) => observed.Add((id, name, comparison)));
        profile.Services.AnalysisSavedReferences.Track(observer);
        var actual = new AnalysisViewWork();
        try
        {
            await using var captured = await AnalysisOwnedStoreAccess.PrepareComparisonAsync(profile.Services, "pair.fcdsc", actual, token);
            Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await captured.DeleteAsync(token)).State);
            Assert.True(first.HasTemporaryWork); Assert.Null(first.SavedComparisonName);
            Assert.NotEmpty(first.Comparison!.Rows); Assert.Equal("independent.fcdsa", other.SavedInventoryName);
            Assert.Equal((receipt.ComparisonId, "pair.fcdsc", true), Assert.Single(observed));
            Assert.Equal("before.fcdsa", model.SavedInventoryName); Assert.True(File.Exists(Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, "before.fcdsa")));
            Assert.False(File.Exists(Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, "pair.fcdsc")));
        }
        finally { first.Changed -= broken; profile.Services.AnalysisSavedReferences.Untrack(observer); await actual.CloseAsync(); first.Dispose(); other.Dispose(); await Task.WhenAll(first.CloseCompletion, other.CloseCompletion); }
    }

    private sealed class SavedReferenceObserver(Action<Guid, string, bool> removed) : IAnalysisSavedReferenceTarget
    {
        public void ForgetSavedReference(Guid id, string name, bool comparison) => removed(id, name, comparison);
    }

    [AvaloniaTheory]
    [InlineData("delete")]
    [InlineData("low-space")]
    [InlineData("pre-unlink")]
    [InlineData("post-unlink")]
    [InlineData("reader")]
    public async Task NativeComparisonMaintenanceUsesTheReviewedSingleBundleAndKeepsNeighbors(string mode)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string device = PathUtil.GetDeviceKey(profile.Root); MaintenanceDirectory? captured = null; Guid id = Guid.NewGuid();
        await using var owned = await OwnedAnalysisStore.OpenAsync(profile.Services.Io, device, () =>
        {
            AnalysisPrivateDirectory native = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(profile.Root, "comparison-maintenance") : new UnixAnalysisPrivateDirectory(profile.Root, "comparison-maintenance");
            captured = new(native); using var store = new AnalysisPrivateInventoryStore(captured, leaveOpen: true);
            new AnalysisPrivateProfileState(store).LoadOrCreate(token);
            new AnalysisSavedCatalog(store).SaveNew("named.fcdsa", Fixture(), Guid.NewGuid(), AnalysisSavedView.Default, token);
            store.SaveComparisonNew("pair.fcdsc", id, Fixture(), Fixture(), new(), null, "Previous", token);
            store.SaveComparisonNew("other.fcdsc", Guid.NewGuid(), Fixture(), Fixture(), new(), null, "Previous", token);
            return captured;
        }, token);
        string root = Path.Combine(profile.Root, "comparison-maintenance"), path = Path.Combine(root, "pair.fcdsc");
        byte[] original = File.ReadAllBytes(path), named = File.ReadAllBytes(Path.Combine(root, "named.fcdsa")), other = File.ReadAllBytes(Path.Combine(root, "other.fcdsc"));
        var review = await owned.PrepareComparisonDeleteAsync("pair.fcdsc", token); Assert.Equal(id, review.ComparisonId);
        if (mode == "reader")
        {
            var reader = await AnalysisIo.Run(profile.Services.Io, device, _ => captured!.OpenRead("pair.fcdsc"), token);
            try
            {
                await Assert.ThrowsAsync<IOException>(() => owned.PrepareComparisonDeleteAsync("pair.fcdsc", token));
                var failed = await Assert.ThrowsAsync<OwnedAnalysisComparisonDeletionException>(() => owned.DeleteComparisonAsync(review, token));
                Assert.Equal(OwnedAnalysisDeletionState.Kept, failed.Outcome.State); Assert.Equal(original, File.ReadAllBytes(path));
            }
            finally { await AnalysisIo.Run(profile.Services.Io, device, _ => { reader.Dispose(); return true; }, CancellationToken.None); }
            review = await owned.PrepareComparisonDeleteAsync("pair.fcdsc", token);
        }
        captured!.LowSpace = mode == "low-space"; int publications = captured.Publications;
        if (mode is "pre-unlink" or "post-unlink")
        {
            if (mode == "pre-unlink") captured.BeforeRemove = _ => throw new IOException("Before unlink");
            else captured.AfterRemove = _ => throw new IOException("After unlink");
            var failed = await Assert.ThrowsAsync<OwnedAnalysisComparisonDeletionException>(() => owned.DeleteComparisonAsync(review, token));
            Assert.Equal(mode == "pre-unlink" ? OwnedAnalysisDeletionState.Kept : OwnedAnalysisDeletionState.Uncertain, failed.Outcome.State);
            Assert.Equal(mode == "pre-unlink", File.Exists(path));
            if (mode == "pre-unlink") Assert.Equal(original, File.ReadAllBytes(path));
        }
        else
        {
            var outcome = await owned.DeleteComparisonAsync(review, token);
            Assert.Equal(OwnedAnalysisDeletionState.Deleted, outcome.State); Assert.True(outcome.RemovalConfirmed); Assert.False(File.Exists(path));
        }
        Assert.Equal(publications, captured.Publications); Assert.Equal(named, File.ReadAllBytes(Path.Combine(root, "named.fcdsa"))); Assert.Equal(other, File.ReadAllBytes(Path.Combine(root, "other.fcdsc")));
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    public async Task ComparisonBrowserDeletionKeepsItsDefaultReviewSourcesInputsAndTabsAtCapacity(bool delete, bool atCapacity, bool privileged)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); using var styles = new ProductStyles();
        await using var source = await NativeNamesFixture.CreateAsync(false); var token = TestContext.Current.CancellationToken;
        var snapshot = source.Memory!; if (privileged) snapshot = snapshot with { Taint = snapshot.Taint | AnalysisTaint.PrivilegedDerived };
        using var model = new DiskSpaceViewModel(profile.Services, ImportedFixture(snapshot)); await model.RebuildAsync(true);
        await model.SaveNewAsync("before.fcdsa", token);
        using var comparison = await model.CompareSavedAsync("before.fcdsa", token); await comparison.SaveComparisonNewAsync("pair.fcdsc", cancellation: token);
        string root = profile.Services.Paths.AnalysisSnapshotsDirectory, path = Path.Combine(root, "pair.fcdsc");
        byte[] original = File.ReadAllBytes(path), before = File.ReadAllBytes(Path.Combine(root, "before.fcdsa")); var window = new DiskSpaceWindow();
        try
        {
            if (atCapacity) for (int i = 0; i < 7; i++) window.AddSession(new DiskSpaceViewModel(profile.Services, ImportedFixture()));
            window.AddSession(model); window.Show(); Frame(window); Click(window, "Open comparison…");
            await Until(() => Visible("Browse saved comparisons") is not null); Assert.False(Visible("Delete selected comparison…")!.IsEnabled);
            Click(window, "Browse saved comparisons");
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Saved comparisons" && c.ItemCount == 1));
            IndexedPersistenceControl<ComboBox>(window, "Saved comparisons").SelectedIndex = 0;
            await Until(() => Visible("Delete selected comparison…")?.IsEnabled == true);
            Assert.Equal(!atCapacity, Visible("Open comparison observations")!.IsEnabled);
            IndexedPersistenceControl<TextBox>(window, "Saved comparison filename").Text = "different.fcdsc";
            await Until(() => Visible("Delete selected comparison…")?.IsEnabled == false);
            IndexedPersistenceControl<TextBox>(window, "Saved comparison filename").Text = "pair.fcdsc";
            await Until(() => Visible("Delete selected comparison…")?.IsEnabled == true);
            Click(window, "Delete selected comparison…"); await Until(() => Visible("Keep comparison") is not null); Frame(window);
            Assert.True(Visible("Keep comparison")!.IsDefault); Assert.True(Visible("Keep comparison")!.IsCancel);
            Assert.False(Visible("Delete comparison")!.IsDefault); Assert.Contains("danger", Visible("Delete comparison")!.Classes);
            string text = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
            Assert.Contains("pair.fcdsc", text); Assert.Contains("both captured inventories", text); Assert.Contains("not secure erasure", text);
            Assert.Equal(privileged, text.Contains("administrator access", StringComparison.Ordinal)); Assert.Equal(original, File.ReadAllBytes(path));
            Click(window, delete ? "Delete comparison" : "Keep comparison");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && AutomationProperties.GetName(t) == "Analysis status" &&
                t.Text?.Contains(delete ? "were deleted" : "was kept", StringComparison.Ordinal) == true));
            Assert.Equal(!delete, File.Exists(path)); if (!delete) Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(root, "before.fcdsa"))); Assert.Equal(atCapacity ? 8 : 1, window.SessionCount);
            Assert.NotEmpty(comparison.Comparison!.Rows); Assert.Equal(delete, comparison.HasTemporaryWork);
            Assert.Equal(delete ? null : "pair.fcdsc", comparison.SavedComparisonName); Assert.Equal("before.fcdsa", model.SavedInventoryName); source.AssertPayloads();
            Button? Visible(string label) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.IsEffectivelyVisible && Equals(b.Content, label));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); comparison.Dispose(); await comparison.CloseCompletion; }
    }
}
