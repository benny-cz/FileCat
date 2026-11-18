using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.Commands;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAnalysisPanelResultsKeepExactMarksWhenTheSourceMovesAndTheFilterHidesOne(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var profile = new PreparationFixture();
        string input = Directory.CreateDirectory(Path.Combine(profile.Root, "old-source")).FullName;
        File.WriteAllText(Path.Combine(input, "visible.txt"), "first");
        File.WriteAllText(Path.Combine(input, "hidden.txt"), "second");
        File.WriteAllText(Path.Combine(input, "unmarked.txt"), "third");
        var vm = new MainViewModel(profile.Services);
        var main = new MainWindow(vm, null);
        vm.Initialize(null);
        var analysis = new DiskSpaceWindow();
        var token = TestContext.Current.CancellationToken;
        main.Show();
        analysis.Show();
        try
        {
            var captured = await analysis.AddOwnedAcquisitionAsync(profile.Services, async cancellation =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, cancellation, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, profile, input); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            }, cancellation: token);
            Frame(analysis);
            if (captured.Indexed is { } explorer)
            {
                var list = IndexedList(analysis);
                list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>());
                Click(analysis, "Explore selected folder");
                await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Count() == 3);
                foreach (string name in new[] { "visible.txt", "hidden.txt" })
                {
                    list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>(), row => row.Path.Name == name);
                    await Until(() => explorer.Inspection?.Row.Path.Name == name);
                    Click(analysis, "Toggle focused mark");
                }
                var query = analysis.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationLabel(box) == "Filter indexed observations");
                query.Text = "name:visible.txt";
                Click(analysis, "Apply filter");
                await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Count() == 1 && IndexedSelectionCount(analysis, 2, 0));
            }
            else
            {
                var model = Assert.IsType<DiskSpaceViewModel>(captured.Memory);
                string root = AnalysisTree.Key(Assert.Single(model.Tree!.Snapshot.Nodes, row => row.ParentId == 0).Id);
                model.Focus(root, open: true);
                await Until(() => model.Selection.Scope == root);
                foreach (string name in new[] { "visible.txt", "hidden.txt" })
                    model.Focus(AnalysisTree.Key(Assert.Single(model.Tree!.Snapshot.Nodes, row => row.Name == name).Id), toggleMark: true);
                model.Query = "name:visible.txt";
                await model.RebuildAsync(true, token);
                await Until(() => model.PublishedReportView.Query == "name:visible.txt");
                Assert.Equal(2, model.Selection.Marks.Count);
                Assert.Contains(model.Rows, row => row.Name == "visible.txt");
                Assert.DoesNotContain(model.Rows, row => row.Name == "hidden.txt");
            }

            string moved = Path.Combine(profile.Root, "moved-source");
            Directory.Move(input, moved);
            await Until(() => analysis.GetVisualDescendants().OfType<Button>().Any(button =>
                button.IsEffectivelyVisible && button.IsEnabled && Equals(button.Content, "Show selection in results")));
            Click(analysis, "Show selection in results");
            await Until(() => vm.ActiveTab is { Location.Scheme: Schemes.AnalysisFolder, Listing.State: ListingState.Complete },
                () => ComparisonText(analysis), TimeSpan.FromSeconds(25));
            var shown = vm.ActiveTab!;
            Assert.Contains("recorded locations stay offline", shown.Banner);
            Assert.Equal("Read-only analysis results", shown.Listing.Provider!.GetDisplayName(shown.Location!));
            Assert.Equal(2, shown.Listing.Store.Count);
            var results = Enumerable.Range(0, shown.Listing.Store.Count).Select(index => shown.Listing.GetItemRef(index)).ToArray();
            Assert.All(results, item => Assert.Null(item.FileSystemPath));
            Assert.Contains(results, item => item.Name.EndsWith("visible.txt", StringComparison.Ordinal));
            Assert.Contains(results, item => item.Name.EndsWith("hidden.txt", StringComparison.Ordinal));
            Assert.False(vm.GetAvailability(CommandIds.AddToWorkingSet).Enabled);
            Assert.Null(shown.ToState().Location);
            Assert.False(Directory.Exists(input));
            Assert.Equal("second", File.ReadAllText(Path.Combine(moved, "hidden.txt")));
        }
        finally
        {
            analysis.ForceClose();
            await analysis.WaitForIndexedCloseAsync();
            await CloseDiskSpaceEntryAsync(profile.Services, vm, main);
        }
    }

    [AvaloniaFact]
    public async Task NativeAnalysisPanelResultsRefuseAnOversizedSelectionBeforeReadingEntries()
    {
        OrdinaryPreparation();
        var selected = Enumerable.Range(1, AnalysisPanelFolder.MaximumEntries + 1).Select(id => (ulong)id).ToHashSet();
        bool read = false;
        var error = await Assert.ThrowsAsync<NotSupportedException>(() => AnalysisPanelResults.ReadAsync(selected, "Marked entries", [],
            AnalysisTaint.OrdinarySensitive,
            (_, _) => { read = true; throw new InvalidOperationException(); }, TestContext.Current.CancellationToken));
        Assert.Equal(AnalysisPanelResults.LimitMessage, error.Message);
        Assert.False(read);
    }

    [AvaloniaFact]
    public async Task NativeAnalysisPanelResultsRetainImportedAndPrivilegedProvenance()
    {
        OrdinaryPreparation();
        var snapshot = Fixture();
        var taint = AnalysisTaint.OrdinarySensitive | AnalysisTaint.ImportedUnverified | AnalysisTaint.PrivilegedDerived;
        var listing = await AnalysisPanelResults.ReadAsync(new HashSet<ulong> { 4 }, "Focused entry", snapshot.Roots, taint,
            (id, _) => Task.FromResult(AnalysisPathMetadata.From(snapshot.Nodes.Single(node => node.Id == id))),
            TestContext.Current.CancellationToken);
        var provider = new AnalysisPanelFolderProvider();
        using var lease = provider.Publish(listing);
        Assert.Equal(taint, listing.Taint);
        Assert.Contains("Imported observations are unverified", provider.GetBanner(lease.Location));
        Assert.Contains("privileged-derived metadata", provider.GetBanner(lease.Location));
    }
}
