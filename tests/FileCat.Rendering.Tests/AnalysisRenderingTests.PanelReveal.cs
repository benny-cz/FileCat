using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAnalysisPanelRevealDisplaysBoundMetadataWithoutOpeningThePathAgain(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var profile = new PreparationFixture();
        var vm = new MainViewModel(profile.Services);
        var main = new MainWindow(vm, null);
        vm.Initialize(null);
        var analysis = new DiskSpaceWindow { Width = indexed ? 1180 : 380, Height = 540 };
        string input = Path.Combine(profile.Root, "input");
        string branch = Directory.CreateDirectory(Path.Combine(input, "branch")).FullName;
        string original = Path.Combine(branch, "original.txt");
        File.WriteAllText(original, "original");
        var token = TestContext.Current.CancellationToken;
        main.Show();
        analysis.Show();
        try
        {
            var result = await analysis.AddOwnedAcquisitionAsync(profile.Services, async cancellation =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, cancellation, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, profile, input); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            }, cancellation: token);
            Frame(analysis);
            if (result.Indexed is { } explorer)
            {
                var list = IndexedList(analysis);
                list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>());
                Click(analysis, "Explore selected folder");
                await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "branch"));
                list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>(), row => row.Path.Name == "branch");
                await Until(() => explorer.Inspection?.Row.Path.Name == "branch");
            }
            else
            {
                var model = Assert.IsType<DiskSpaceViewModel>(result.Memory);
                model.Focus(AnalysisTree.Key(Assert.Single(model.Tree!.Snapshot.Nodes, node => node.Name == "branch").Id));
                Click(analysis, "Inspect selection");
            }

            File.WriteAllText(Path.Combine(branch, "new\u202E.txt"), "after the scan");
            await Until(() => analysis.GetVisualDescendants().OfType<Button>().Any(button =>
                button.IsEffectivelyVisible && button.IsEnabled && Equals(button.Content, "Reveal in panel")));
            Click(analysis, "Reveal in panel");
            var panel = vm.Workspace.ActivePanel!;
            await Until(() => panel.ActiveTab?.Location?.Scheme == Schemes.AnalysisFolder &&
                panel.ActiveTab?.Listing.State == ListingState.Complete, () => ComparisonText(analysis), TimeSpan.FromSeconds(25));
            var tab = panel.ActiveTab!;
            var location = tab.Location!;
            Assert.Contains("Read-only folder metadata", tab.Banner);
            Assert.Equal(LocationCapabilities.Enumerate, tab.Listing.Provider!.GetCapabilities(location));
            Assert.Null(tab.ToState().Location);
            Assert.Null(tab.ToState().FocusName);
            Assert.Equal(["new\\u202E.txt", "original.txt"], PanelNames(tab));
            foreach (int index in Enumerable.Range(0, tab.Listing.Store.Count))
            {
                var reference = tab.Listing.GetItemRef(index);
                Assert.Null(reference.FileSystemPath);
                Assert.Null(tab.Listing.Provider.OpenContent(reference));
                Assert.Null(tab.Listing.Provider.GetChildLocation(location, tab.Listing.Store[index]));
            }
            var set = profile.Services.WorkingSets.Create("Folder metadata test");
            Assert.Equal(0, profile.Services.WorkingSets.Add(set, [tab.Listing.GetItemRef(0)]));

            string moved = Path.Combine(profile.Root, "original-branch");
            Directory.Move(branch, moved);
            Directory.CreateDirectory(branch);
            File.WriteAllText(Path.Combine(branch, "replacement.txt"), "replacement");
            tab.Refresh();
            await Until(() => tab.Listing.State == ListingState.Complete);
            Assert.Equal(["new\\u202E.txt", "original.txt"], PanelNames(tab));
            Assert.Equal("original", File.ReadAllText(Path.Combine(moved, "original.txt")));
            panel.CloseTab(tab);
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => profile.Services.AnalysisPanelFolders.PrepareNavigationAsync(location, token));
        }
        finally
        {
            analysis.ForceClose();
            await analysis.WaitForIndexedCloseAsync();
            await CloseDiskSpaceEntryAsync(profile.Services, vm, main);
        }

        static string[] PanelNames(TabViewModel tab) => Enumerable.Range(0, tab.Listing.Store.Count)
            .Select(index => tab.Listing.Store[index].Name).Order(StringComparer.Ordinal).ToArray();
    }

    [AvaloniaFact]
    public async Task NativeAnalysisPanelRevealRefusesAnOversizedCurrentFolderWithoutPublishingPartOfIt()
    {
        OrdinaryPreparation();
        using var profile = new PreparationFixture();
        string input = Path.Combine(profile.Root, "input");
        string branch = Directory.CreateDirectory(Path.Combine(input, "branch")).FullName;
        var token = TestContext.Current.CancellationToken;
        await using var owner = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false);
        var scan = await StartAcquiredAsync(owner, profile, input);
        var (revision, folder) = await FinishedBranchAsync(scan, token);
        for (int index = 0; index <= AnalysisPanelFolder.MaximumEntries; index++)
        {
            token.ThrowIfCancellationRequested();
            File.WriteAllBytes(Path.Combine(branch, $"row-{index:D5}.bin"), []);
        }

        var error = await Assert.ThrowsAsync<NotSupportedException>(() => owner.ReadPanelFolderAsync(revision, folder, token));
        Assert.Equal(AnalysisPanelFolder.LimitMessage, error.Message);
        Assert.Equal(AnalysisPanelFolder.MaximumEntries + 1, Directory.EnumerateFiles(branch).Count());
        Assert.True(owner.HasRuntimeBranchContext);
    }
}
