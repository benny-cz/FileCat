using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeBranchRefreshRebindsOnlyTheCapturedDirectoryAndKeepsTheFullResult(bool indexed)
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input");
        string branch = Path.Combine(input, "parent", "branch");
        Directory.CreateDirectory(branch);
        File.WriteAllText(Path.Combine(branch, "keep.txt"), "original");
        File.WriteAllText(Path.Combine(input, "outside.txt"), "outside");

        await using var original = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, indexedStorage: indexed);
        var scan = await StartAcquiredAsync(original, fixture, input);
        var snapshot = scan.Snapshot();
        var token = TestContext.Current.CancellationToken;
        var (revision, selected) = await FinishedBranchAsync(scan, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            original.BeginBranchRefreshAsync(revision with { Epoch = revision.Epoch + 1 }, selected, cancellation: token));

        File.WriteAllText(Path.Combine(branch, "new.txt"), "observed only by the refreshed branch");
        await using var result = await original.BeginBranchRefreshAsync(revision, selected, cancellation: token);
        var refreshed = result.RuntimeSession;
        await refreshed.Completion.WaitAsync(TimeSpan.FromSeconds(20), token);
        var branchSnapshot = refreshed.Snapshot();
        Assert.Equal(AnalysisPhase.Finished, branchSnapshot.Phase);
        Assert.Equal(0UL, refreshed.SourceCloseFailures);
        Assert.NotEqual(snapshot.Identity.SessionId, branchSnapshot.Identity.SessionId);
        Assert.Equal("branch", Assert.Single(branchSnapshot.Roots).Label);
        Assert.Equal(new[] { "branch", "keep.txt", "new.txt" }, branchSnapshot.Nodes.Select(node => node.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(snapshot.Nodes, node => node.Name == "new.txt");
        Assert.Contains(snapshot.Nodes, node => node.Name == "outside.txt");
        Assert.True(original.HasRuntimeRescanContext);
        Assert.Equal("original", File.ReadAllText(Path.Combine(branch, "keep.txt")));
        Assert.Equal("outside", File.ReadAllText(Path.Combine(input, "outside.txt")));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeBranchRefreshRefusesAReplacedAncestorEvenWhenTheSelectedDirectoryMovedBack(bool indexed)
    {
        OrdinaryPreparation();
        using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input");
        string parent = Path.Combine(input, "parent");
        string branch = Path.Combine(parent, "branch");
        string moved = Path.Combine(fixture.Root, "moved");
        Directory.CreateDirectory(branch);
        File.WriteAllText(Path.Combine(branch, "keep.txt"), "original");

        await using var original = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, indexedStorage: indexed);
        var scan = await StartAcquiredAsync(original, fixture, input);
        var token = TestContext.Current.CancellationToken;
        var (revision, selected) = await FinishedBranchAsync(scan, token);
        Directory.Move(parent, moved);
        Directory.CreateDirectory(parent);
        Directory.Move(Path.Combine(moved, "branch"), branch);

        await Assert.ThrowsAsync<AnalysisSourceChangedException>(() =>
            original.BeginBranchRefreshAsync(revision, selected, cancellation: token));
        Assert.True(original.HasRuntimeRescanContext);
        Assert.Equal("original", File.ReadAllText(Path.Combine(branch, "keep.txt")));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeBranchRefreshWindowOpensTheSubtreeWithoutChangingTheOriginalMarksOrBasket(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        var pool = new AnalysisSessionAdmission();
        var window = new DiskSpaceWindow(null, null, pool);
        string input = Path.Combine(fixture.Root, "input");
        string branch = Path.Combine(input, "branch");
        Directory.CreateDirectory(branch);
        File.WriteAllText(Path.Combine(branch, "keep.txt"), "original");
        try
        {
            File.WriteAllText(Path.Combine(input, "outside.txt"), "outside");
            window.Show();
            var original = await window.AddOwnedAcquisitionAsync(fixture.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, fixture, input); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            });
            Frame(window);
            if (original.Indexed is { } explorer)
            {
                var list = IndexedList(window);
                list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>());
                Click(window, "Explore selected folder");
                await Until(() => explorer.Page?.ParentId == 1 && list.Items.OfType<AnalysisHierarchyRow>().Any(row => row.Path.Name == "branch"));
                Frame(window);
                var command = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Refresh branch"));
                list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>(), row => row.Path.Name == "outside.txt");
                Assert.False(command.IsEnabled);
                await Until(() => explorer.Inspection?.Row.Path.Name == "outside.txt");
                Assert.False(command.IsEnabled);
                list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>(), row => row.Path.Name == "branch");
                await Until(() => explorer.Inspection?.Row.Path.Name == "branch");
                Click(window, "Toggle focused mark");
                Click(window, "Add to review basket");
                Assert.True(IndexedSelectionCount(window, 1, 1));
            }
            else
            {
                var model = Assert.IsType<FileCat.App.ViewModels.DiskSpaceViewModel>(original.Memory);
                var outside = Assert.Single(model.Tree!.Snapshot.Nodes, node => node.Name == "outside.txt");
                model.Focus(AnalysisTree.Key(outside.Id));
                Assert.False(window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Refresh branch")).IsEnabled);
                var selected = Assert.Single(model.Tree!.Snapshot.Nodes, node => node.Name == "branch");
                model.Focus(AnalysisTree.Key(selected.Id), toggleMark: true);
                model.StageSelection();
                Assert.Single(model.Selection.Marks);
                Assert.Single(model.Selection.Basket);
            }
            File.WriteAllText(Path.Combine(branch, "new.txt"), "branch only");
            await Until(() => window.GetVisualDescendants().OfType<Button>().Any(button => button.IsEffectivelyVisible &&
                Equals(button.Content, "Refresh branch") && button.IsEnabled));
            Click(window, "Refresh branch");
            await Until(() => window.SessionCount == 2, () => ComparisonText(window), TimeSpan.FromSeconds(30));
            Frame(window);
            if (original.Indexed is not null)
            {
                Assert.True(IndexedSelectionCount(window, 0, 0));
                Assert.Equal("branch", Assert.Single(IndexedList(window).Items.OfType<AnalysisHierarchyRow>()).Path.Name);
                var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();
                tabs.SelectedIndex = 0;
                Frame(window);
                Assert.True(IndexedSelectionCount(window, 1, 1));
            }
            else
            {
                Assert.DoesNotContain(original.Memory!.Tree!.Snapshot.Nodes, node => node.Name == "new.txt");
                Assert.Single(original.Memory.Selection.Marks);
                Assert.Single(original.Memory.Selection.Basket);
                Assert.Contains("branch", ComparisonText(window), StringComparison.Ordinal);
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);
    }

    [AvaloniaFact]
    public async Task NativeBranchRefreshDoesNotTreatAnIndexedOtherSubsetAsItsParentFolder()
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        var window = new DiskSpaceWindow();
        string input = Path.Combine(fixture.Root, "input");
        Directory.CreateDirectory(input);
        File.WriteAllBytes(Path.Combine(input, "largest.bin"), new byte[8192]);
        File.WriteAllText(Path.Combine(input, "smaller.txt"), "small");
        string other = AnalysisTree.OtherKey("n:1", 1);
        try
        {
            window.Show();
            var opened = await window.AddOwnedAcquisitionAsync(fixture.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token);
                try { await StartAcquiredAsync(owner, fixture, input); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            }, AnalysisSavedView.Default with { Scope = other, Metric = AnalysisMetric.FileSize });
            Frame(window);
            Assert.Equal(other, opened.Indexed!.OtherScopeKey);
            Assert.False(window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Refresh branch")).IsEnabled);
            Assert.Equal(1, window.SessionCount);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    private static async Task<(SessionIdentity Revision, ulong Folder)> FinishedBranchAsync(AnalysisSession session, CancellationToken token)
    {
        await using var dataset = await session.AcquireDatasetAsync(token);
        if (dataset is not null)
        {
            var page = await dataset.ReadPathsAsync(0, cancellation: token);
            return (dataset.Descriptor.Identity, Assert.Single(page.Rows, node => node.Name == "branch").Id);
        }
        var snapshot = session.Snapshot();
        return (snapshot.Identity, Assert.Single(snapshot.Nodes, node => node.Name == "branch").Id);
    }
}
