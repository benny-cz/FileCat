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
    private static T IndexedPersistenceControl<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.IsEffectivelyVisible && AutomationLabel(control) == name);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndexedPersistenceSavesTheWholeNativeRevisionAndRestoresPublishedSettingsWithoutMarks(bool grouped)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync();
        var token = TestContext.Current.CancellationToken; var window = new DiskSpaceWindow();
        var explorer = await window.AddIndexedSessionAsync(source.Lease!, AnalysisMetric.FileSize, new("name:file-"), token, profile.Services);
        await source.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            Assert.Equal(300UL, explorer.Filter!.DefinitePaths);
            if (grouped)
            {
                await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window);
                list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisDatasetGroup>()); Click(window, "Explore selected group"); await Until(() => MemberReady(window));
            }
            Click(window, grouped ? "Mark current members" : "Mark matching files in this scope"); await Until(() => IndexedSelectionCount(window, 300, 0));
            Click(window, "Add to review basket"); Assert.True(IndexedSelectionCount(window, 300, 300));
            IndexedPersistenceControl<ComboBox>(window, "Indexed table order").SelectedItem = "Name";
            IndexedPersistenceControl<CheckBox>(window, "Indexed descending table order").IsChecked = false;
            await Until(() => grouped ? MemberReady(window) && window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible && text.Text?.EndsWith(" · Name · Ascending", StringComparison.Ordinal) == true) : explorer.Page?.Sort == AnalysisHierarchySort.Name && !explorer.Page.Descending);
            IndexedPersistenceControl<ComboBox>(window, "Indexed chart mode").SelectedItem = "Table";
            // A newer editor draft must not replace the query behind the published table.
            IndexedPersistenceControl<TextBox>(window, "Filter indexed observations").Text = "name:future-only.bin";
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(source.PayloadPath)!, "future-only.bin"), [5]);
            Click(window, "Save named analysis"); await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(t => AutomationLabel(t) == "Inventory filename")); Frame(window);
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && Equals(b.Content, "Cancel")).IsDefault);
            Click(window, "Cancel"); await Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && Equals(b.Content, "Save named analysis") && b.IsEnabled));
            Assert.False(Directory.Exists(profile.Services.Paths.AnalysisSnapshotsDirectory) && Directory.GetFiles(profile.Services.Paths.AnalysisSnapshotsDirectory, "*.fcdsa").Length != 0);
            Click(window, "Save named analysis"); await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(t => AutomationLabel(t) == "Inventory filename"));
            IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text = "streamed.fcdsa"; Click(window, "Save new inventory");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => AutomationLabel(t) == "Indexed analysis status" && t.Text?.StartsWith("Saved streamed.fcdsa", StringComparison.Ordinal) == true));
            AnalysisSavedEntry entry;
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            {
                using var catalog = await prepared.ReadCatalogAsync(token); entry = Assert.Single(catalog.Entries);
                Assert.Equal("name:file-", entry.View.Query); Assert.Equal(grouped ? AnalysisGroupingDimension.Extension : AnalysisGroupingDimension.FolderHierarchy, entry.View.Grouping);
                Assert.Equal(AnalysisTableSort.Name, entry.View.Sort); Assert.False(entry.View.SortDescending); Assert.Equal("Table", entry.View.Chart);
            }
            using (var bytes = File.OpenRead(Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, entry.Name)))
            using (var loaded = AnalysisInventoryFormat.Read(bytes, token))
            { Assert.Equal(305, loaded.Snapshot.Nodes.Length); Assert.Contains(loaded.Snapshot.Nodes, node => node.Name == "match-target.bin"); Assert.DoesNotContain(loaded.Snapshot.Nodes, node => node.Name == "future-only.bin"); }
            byte[] before = File.ReadAllBytes(Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, entry.Name));
            Click(window, "Save named analysis"); await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(t => AutomationLabel(t) == "Inventory filename"));
            IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text = "STREAMED.fcdsa"; Click(window, "Save new inventory");
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && b.Content as string == "Keep existing"));
            var keep = window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && b.Content as string == "Keep existing");
            Assert.True(keep.IsDefault); Assert.True(keep.IsCancel); Click(window, "Keep existing");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => AutomationLabel(t) == "Indexed analysis status" && t.Text?.Contains("The existing saved analysis was kept.", StringComparison.Ordinal) == true),
                () => string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Text)));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, entry.Name)));
            Assert.Single(Directory.GetFiles(profile.Services.Paths.AnalysisSnapshotsDirectory, "*.fcdsa"));
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(b => b.IsEffectivelyVisible && b.Content as string == "Open saved analysis" && b.IsEnabled));
            Click(window, "Open saved analysis"); await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(t => AutomationLabel(t) == "Saved inventory filename"));
            Click(window, "Browse saved analyses");
            await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(control => control.IsEffectivelyVisible && AutomationLabel(control) == "Saved analyses" && control.ItemCount == 1),
                () => string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible).Select(text => text.Text)));
            IndexedPersistenceControl<ComboBox>(window, "Saved analyses").SelectedIndex = 0; Click(window, "Open inventory");
            await Until(() => window.SessionCount == 2 && window.GetVisualDescendants().OfType<TextBox>().Any(control => control.IsEffectivelyVisible && AutomationLabel(control) == "Filter indexed observations" && control.Text == "name:file-")); Frame(window);
            Assert.True(IndexedSelectionCount(window, 0, 0)); Assert.Equal("Table", IndexedPersistenceControl<ComboBox>(window, "Indexed chart mode").SelectedItem);
            Assert.Equal(grouped ? (int)AnalysisGroupingDimension.Extension : 0, GroupingSelector(window).SelectedIndex);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text?.Contains("Offline imported observations", StringComparison.Ordinal) == true);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(source.PrivatePath)); Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaFact]
    public async Task IndexedPersistenceWithoutAProfileExplainsDisabledControls()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); await using var source = await IndexedFixture.CreateAsync(); var window = new DiskSpaceWindow();
        await window.AddIndexedSessionAsync(source.Lease!, cancellation: TestContext.Current.CancellationToken); window.Show(); Frame(window);
        try
        {
            foreach (string label in new[] { "Save named analysis", "Open saved analysis", "Compare…" })
            {
                var button = window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, label)); Assert.False(button.IsEnabled);
                Assert.Contains("no available private inventory store", AutomationProperties.GetHelpText(button));
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task IndexedPersistenceNativeCatalogWritersContendOnTheHeldFileAndReleaseBeforeTheNextSave()
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false)) { }
        string path = profile.Services.Paths.AnalysisSnapshotsDirectory;
        await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), cancellation =>
        {
            AnalysisPrivateDirectory Open() => OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path)) : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            using var one = new AnalysisPrivateInventoryStore(Open()); using var two = new AnalysisPrivateInventoryStore(Open());
            var snapshot = Fixture(); var catalog = new AnalysisSavedCatalog(one);
            using (catalog.BeginNamedWrite("held.fcdsa", Guid.NewGuid(), snapshot.Coverage, snapshot.Taint, AnalysisSavedView.Default, cancellation))
                Assert.Throws<IOException>(() => new AnalysisSavedCatalog(two).SaveNew("refused.fcdsa", snapshot, Guid.NewGuid(), AnalysisSavedView.Default, cancellation));
            new AnalysisSavedCatalog(two).SaveNew("after.fcdsa", snapshot, Guid.NewGuid(), AnalysisSavedView.Default, cancellation);
            using var index = catalog.Read(cancellation); Assert.Equal("after.fcdsa", Assert.Single(index.Entries).Name); Assert.Empty(index.UnindexedNames); return true;
        }, token);
    }
}
