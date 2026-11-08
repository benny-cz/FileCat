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
    [InlineData("delete")]
    [InlineData("low-space")]
    [InlineData("restore")]
    [InlineData("finish-present")]
    [InlineData("finish-absent")]
    public async Task NativeNamedMaintenanceUsesExclusiveOwnedObjectsAndExplicitPendingRecovery(string mode)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string device = PathUtil.GetDeviceKey(profile.Root); Guid id = Guid.NewGuid(); MaintenanceDirectory? captured = null;
        await using var owned = await OwnedAnalysisStore.OpenAsync(profile.Services.Io, device, () =>
        {
            AnalysisPrivateDirectory native = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(profile.Root, "maintenance") : new UnixAnalysisPrivateDirectory(profile.Root, "maintenance");
            captured = new(native); using var store = new AnalysisPrivateInventoryStore(captured, leaveOpen: true);
            new AnalysisPrivateProfileState(store).LoadOrCreate(token);
            new AnalysisSavedCatalog(store).SaveNew("owned.fcdsa", Fixture(), id, AnalysisSavedView.Default, token);
            new AnalysisSavedCatalog(store).SaveNew("other.fcdsa", Fixture(), Guid.NewGuid(), AnalysisSavedView.Default, token);
            return captured;
        }, token);
        string path = Path.Combine(profile.Root, "maintenance", "owned.fcdsa");
        byte[] original = File.ReadAllBytes(path), other = File.ReadAllBytes(Path.Combine(profile.Root, "maintenance", "other.fcdsa"));
        var review = await owned.PrepareDeleteAsync(id, token);
        if (mode is "restore" or "finish-present" or "finish-absent")
        {
            captured!.BeforeRemove = name => { if (name.EndsWith(".fcdsa", StringComparison.Ordinal) && mode != "finish-absent") throw new IOException("Injected pre-unlink refusal"); };
            captured.AfterRemove = name => { if (name.EndsWith(".fcdsa", StringComparison.Ordinal) && mode == "finish-absent") throw new IOException("Injected post-unlink durability failure"); };
            captured.BeforeMove = (_, destination) => { if (AnalysisPrivateNames.IsCatalogEntry(destination)) throw new IOException("Injected rollback refusal"); };
            var failed = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => owned.DeleteAsync(review, token));
            Assert.Equal(mode == "finish-absent" ? OwnedAnalysisDeletionState.Uncertain : OwnedAnalysisDeletionState.RecoveryRequired, failed.Outcome.State);
            using (var pending = await owned.ReadCatalogAsync(token))
            { Assert.Single(pending.Entries); Assert.Equal(mode != "finish-absent", Assert.Single(pending.PendingDeletions).PayloadPresent); }
            Assert.Equal(mode != "finish-absent", File.Exists(path));
            captured.BeforeRemove = captured.AfterRemove = null; captured.BeforeMove = null;
            review = await owned.PrepareDeleteAsync(id, token); Assert.True(review.IsRecovery);
        }
        captured!.LowSpace = mode == "low-space"; int publications = captured.Publications;
        var outcome = mode == "restore" ? await owned.RestoreNamedAsync(review, token) : await owned.DeleteAsync(review, token);
        Assert.Equal(mode == "restore" ? OwnedAnalysisDeletionState.Restored : OwnedAnalysisDeletionState.Deleted, outcome.State);
        Assert.Equal(publications, captured.Publications); Assert.Equal(mode == "restore", File.Exists(path));
        if (mode == "restore") Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Equal(other, File.ReadAllBytes(Path.Combine(profile.Root, "maintenance", "other.fcdsa")));
        using var after = await owned.ReadCatalogAsync(token); Assert.Empty(after.PendingDeletions); Assert.Equal(mode == "restore" ? 2 : 1, after.Entries.Length);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeNamedMaintenanceRefusesAnActualPayloadOrCatalogReaderUntilClose(bool metadata)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        string device = PathUtil.GetDeviceKey(profile.Root); Guid id = Guid.NewGuid(); AnalysisPrivateDirectory? directory = null;
        await using var owned = await OwnedAnalysisStore.OpenAsync(profile.Services.Io, device, () =>
        {
            directory = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(profile.Root, "active-maintenance") : new UnixAnalysisPrivateDirectory(profile.Root, "active-maintenance");
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); new AnalysisPrivateProfileState(store).LoadOrCreate(token);
            new AnalysisSavedCatalog(store).SaveNew("owned.fcdsa", Fixture(), id, AnalysisSavedView.Default, token); return directory;
        }, token);
        var review = await owned.PrepareDeleteAsync(id, token);
        var reader = await AnalysisIo.Run(profile.Services.Io, device, _ => directory!.OpenRead(metadata
            ? directory.Entries(token).Single(AnalysisPrivateNames.IsCatalogEntry) : "owned.fcdsa"), token);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => owned.PrepareDeleteAsync(id, token));
            var failed = await Assert.ThrowsAsync<OwnedAnalysisDeletionException>(() => owned.DeleteAsync(review, token));
            Assert.Equal(OwnedAnalysisDeletionState.Kept, failed.Outcome.State); Assert.True(File.Exists(Path.Combine(profile.Root, "active-maintenance", "owned.fcdsa")));
        }
        finally { await AnalysisIo.Run(profile.Services.Io, device, _ => { reader.Dispose(); return true; }, CancellationToken.None); }
        Assert.Equal(OwnedAnalysisDeletionState.Deleted, (await owned.DeleteAsync(await owned.PrepareDeleteAsync(id, token), token)).State);
    }

    [AvaloniaTheory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task NamedAnalysisDeletionRequiresASeparateDefaultKeepReviewAndPreservesSourcesAndTabs(bool indexed, bool delete, bool atCapacity)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(indexed); var token = TestContext.Current.CancellationToken;
        const string name = "review-delete.fcdsa"; var window = new DiskSpaceWindow();
        try
        {
            if (atCapacity)
                for (int i = 0; i < 7; i++) window.AddSession(new DiskSpaceViewModel(profile.Services, ImportedFixture()));
            if (indexed)
            {
                await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
                    await prepared.SaveInventoryAsync(source.Lease!, name, AnalysisSavedView.Default, token);
                await window.AddIndexedSessionAsync(source.Lease!, services: profile.Services, cancellation: token);
            }
            else
            {
                var model = new DiskSpaceViewModel(profile.Services, ImportedFixture(source.Memory!)); await model.RebuildAsync(true);
                await model.SaveNewAsync(name, token); window.AddSession(model);
            }
            string path = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name); byte[] original = File.ReadAllBytes(path);
            window.Show(); Frame(window); Click(window, indexed ? "Open saved analysis" : "Open saved…");
            await Until(() => Visible("Browse saved analyses") is not null); Assert.False(Visible("Delete selected analysis…")!.IsEnabled);
            Click(window, "Browse saved analyses");
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Saved analyses" && c.ItemCount == 1));
            IndexedPersistenceControl<ComboBox>(window, "Saved analyses").SelectedIndex = 0;
            await Until(() => Visible("Delete selected analysis…")?.IsEnabled == true);
            Assert.True(Visible("Delete selected analysis…")!.IsEnabled); Assert.False(Visible("Restore pending analysis…")!.IsEnabled);
            Assert.Equal(!atCapacity, Visible(indexed ? "Open inventory" : "Open observations")!.IsEnabled);
            Click(window, "Delete selected analysis…"); await Until(() => Visible("Keep analysis") is not null);
            Frame(window);
            var keep = Visible("Keep analysis")!; var remove = Visible("Delete analysis")!;
            Assert.True(keep.IsDefault); Assert.True(keep.IsCancel); Assert.False(remove.IsDefault); Assert.Contains("danger", remove.Classes);
            string text = string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text));
            Assert.Contains(name, text); Assert.Contains("Source files are unchanged.", text); Assert.Contains("not secure erasure", text);
            Assert.Equal(original, File.ReadAllBytes(path)); Click(window, delete ? "Delete analysis" : "Keep analysis");
            await Until(() => Visible("Keep analysis") is null && !window.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && Equals(b.Content, "Cancel")));
            if (delete) await Until(() => !File.Exists(path)); else Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(atCapacity ? 8 : 1, window.SessionCount); source.AssertPayloads();
            Button? Visible(string label) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.IsEffectivelyVisible && Equals(b.Content, label));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PendingNamedBrowserOffersExplicitRestoreOrMissingPayloadCleanupWithoutAutomaticReplay(bool indexed, bool payloadPresent)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await NativeNamesFixture.CreateAsync(indexed); var token = TestContext.Current.CancellationToken;
        const string name = "pending-ui.fcdsa"; var window = new DiskSpaceWindow();
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
                var model = new DiskSpaceViewModel(profile.Services, ImportedFixture(source.Memory!)); await model.RebuildAsync(true);
                await model.SaveNewAsync(name, token); window.AddSession(model);
            }
            string path = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, name); byte[] original = File.ReadAllBytes(path);
            await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), _ =>
            {
                string parent = Path.GetDirectoryName(profile.Services.Paths.AnalysisSnapshotsDirectory)!;
                string child = Path.GetFileName(profile.Services.Paths.AnalysisSnapshotsDirectory);
                using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(parent, child) : new UnixAnalysisPrivateDirectory(parent, child);
                using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true);
                new AnalysisPrivateProfileState(store).LoadOrCreate(token);
                using var writer = store.OpenCatalogLock(token);
                using var catalog = new AnalysisSavedCatalog(store).Read(token); var entry = Assert.Single(catalog.Entries);
                using var metadata = directory.OpenMaintenance(AnalysisPrivateNames.CatalogEntry(catalog.CatalogId!.Value, entry.InventoryId));
                metadata.MoveOwnedNew(AnalysisPrivateNames.PendingDeletion(catalog.CatalogId.Value, entry.InventoryId, Guid.NewGuid()));
                if (!payloadPresent) { using var payload = directory.OpenMaintenance(entry.Name); payload.RemoveOwned(); }
                return true;
            }, token);
            window.Show(); Frame(window); Click(window, indexed ? "Open saved analysis" : "Open saved…");
            await Until(() => Visible("Browse saved analyses") is not null); Click(window, "Browse saved analyses");
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(c => c.IsEffectivelyVisible && AutomationProperties.GetName(c) == "Saved analyses" && c.ItemCount == 1));
            IndexedPersistenceControl<ComboBox>(window, "Saved analyses").SelectedIndex = 0;
            await Until(() => Visible("Delete selected analysis…")?.IsEnabled == true);
            Assert.Equal(payloadPresent, Visible("Restore pending analysis…")!.IsEnabled); Assert.Equal(payloadPresent, File.Exists(path));
            Click(window, payloadPresent ? "Restore pending analysis…" : "Delete selected analysis…");
            string preserve = payloadPresent ? "Leave pending" : "Keep analysis";
            await Until(() => Visible(preserve) is not null); Frame(window);
            Assert.True(Visible(preserve)!.IsDefault); Assert.True(Visible(preserve)!.IsCancel); Assert.Equal(payloadPresent, File.Exists(path));
            Click(window, payloadPresent ? "Restore named analysis" : "Delete analysis");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible &&
                AutomationProperties.GetName(t) == (indexed ? "Indexed analysis status" : "Analysis status") &&
                t.Text?.Contains(payloadPresent ? "was restored" : "were deleted", StringComparison.Ordinal) == true));
            await using var owned = await AnalysisOwnedStoreAccess.OpenAsync(profile.Services, token);
            using var after = await owned.ReadCatalogAsync(token); Assert.Empty(after.PendingDeletions); Assert.Equal(payloadPresent ? 1 : 0, after.Entries.Length);
            if (payloadPresent) Assert.Equal(original, File.ReadAllBytes(path)); else Assert.False(File.Exists(path));
            Assert.Equal(1, window.SessionCount); source.AssertPayloads();
            Button? Visible(string label) => window.GetVisualDescendants().OfType<Button>().SingleOrDefault(b => b.IsEffectivelyVisible && Equals(b.Content, label));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    private sealed class MaintenanceDirectory(AnalysisPrivateDirectory inner) : AnalysisPrivateDirectory
    {
        internal Action<string>? BeforeRemove, AfterRemove;
        internal Action<string>? AfterPublish;
        internal Action<string, string>? BeforeMove;
        internal bool LowSpace;
        internal int Publications;
        internal override void Verify() => inner.Verify();
        internal override (UInt128 Capacity, UInt128 Available) Space() => LowSpace ? (1UL << 40, 0) : inner.Space();
        internal override string[] Entries(CancellationToken cancellation) => inner.Entries(cancellation);
        internal override AnalysisPrivateFile CreateStaging() { Publications++; return new StagingFile(this, inner.CreateStaging()); }
        internal override AnalysisPrivateFile OpenRead(string name) => inner.OpenRead(name);
        internal override AnalysisPrivateFile OpenMaintenance(string name) => new File(this, inner.OpenMaintenance(name));
        internal override AnalysisPrivateFile OpenReplacementFile(string name) => new File(this, inner.OpenReplacementFile(name));
        public override void Dispose() => inner.Dispose();
        private sealed class File(MaintenanceDirectory owner, AnalysisPrivateFile inner) : AnalysisPrivateFile
        {
            public override Stream Stream => inner.Stream;
            internal override string Identity => inner.Identity;
            internal override string? MaintenanceName => inner.MaintenanceName;
            internal override bool RemovalRequested => inner.RemovalRequested;
            public override void Verify() => inner.Verify();
            public override void Flush() => inner.Flush();
            public override void PublishNew(string name) => throw new NotSupportedException();
            internal override void MoveOwnedNew(string name) { owner.BeforeMove?.Invoke(inner.MaintenanceName!, name); inner.MoveOwnedNew(name); }
            internal override void RemoveOwned() { string name = inner.MaintenanceName!; owner.BeforeRemove?.Invoke(name); inner.RemoveOwned(); owner.AfterRemove?.Invoke(name); }
            internal override void ReplaceOwned(AnalysisPrivateFile destination)
            {
                if (destination is not File target) throw new ArgumentException("Wrong private file wrapper.");
                inner.ReplaceOwned(target.Inner);
            }
            private AnalysisPrivateFile Inner => inner;
            public override void Dispose() => inner.Dispose();
        }
        private sealed class StagingFile(MaintenanceDirectory owner, AnalysisPrivateFile inner) : AnalysisPrivateFile
        {
            public override Stream Stream => inner.Stream;
            internal override string Identity => inner.Identity;
            internal override string PublishedIdentity => inner.PublishedIdentity;
            public override void Verify() => inner.Verify();
            public override void Flush() => inner.Flush();
            public override void PublishNew(string name)
            {
                inner.PublishNew(name);
                try { owner.AfterPublish?.Invoke(name); }
                catch (Exception error) { throw new AnalysisPublicationUncertainException(name, error); }
            }
            public override void Dispose() => inner.Dispose();
        }
    }
}
