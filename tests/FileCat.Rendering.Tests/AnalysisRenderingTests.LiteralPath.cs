using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisNameRules.Windows, "C:\\", "C:\\Projects\\source.cs")]
    [InlineData(AnalysisNameRules.Unix, "/", "/Projects/source.cs")]
    [InlineData(AnalysisNameRules.Unix, "/fixture\\", "/fixture\\/Projects/source.cs")]
    public async Task ClipboardPathPreservesCapturedRootAndSeparators(AnalysisNameRules names, string root, string descendant)
    {
        var snapshot = Fixture();
        snapshot = snapshot with { Roots = [snapshot.Roots[0] with { CanonicalLocator = root, NameRules = names }] };
        using var model = new DiskSpaceViewModel(snapshot);
        await model.RebuildAsync(true);
        model.Focus("n:1");
        Assert.Equal(root, model.FocusedPath());
        model.Focus("n:4");
        Assert.Equal(descendant, model.FocusedPath());
    }

    [AvaloniaFact]
    public async Task ClipboardPathDeclinesOversizedCapturedAncestry()
    {
        var snapshot = Fixture();
        string longName = new('é', 30_000);
        var nodes = snapshot.Nodes.Take(1).ToList();
        for (ulong id = 2; id <= 6; id++) nodes.Add(new(id, "r", id - 1, longName, AnalysisKind.Directory));
        using var model = new DiskSpaceViewModel(snapshot with { Nodes = [.. nodes], Objects = [], Facts = [] });
        await model.RebuildAsync(true);
        model.Focus("n:5");
        Assert.NotNull(model.FocusedPath());
        model.Focus("n:6");
        Assert.Null(model.FocusedPath());
        Assert.Contains("exceeds the clipboard limit", model.FocusedDisplayPath());
    }

    [AvaloniaFact]
    public async Task ClipboardPathFromCompactInspectorRemainsBoundToItsDisplayedEntry()
    {
        using var styles = new ProductStyles();
        using var model = new DiskSpaceViewModel(Fixture()) { Chart = "Table" };
        var window = new DiskSpaceWindow { Width = 380, Height = 430 };
        window.AddSession(model); window.Show();
        try
        {
            await model.RebuildAsync(true);
            model.Focus("n:4");
            Frame(window);
            Click(window, "Inspect selection");
            Frame(window);
            model.Focus("n:5");
            Assert.Equal("/fixture/Photos/picture.png", model.FocusedPath());
            Click(window, "Copy path");
            Frame(window);
            Assert.Equal("/fixture/Projects/source.cs", await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaFact]
    public async Task ClipboardPathFromIndexedInspectorUsesCapturedLocationAfterTheSourceMoves()
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        await using var fixture = await IndexedFixture.CreateAsync();
        var source = fixture.Lease!.Descriptor.Roots.Single();
        Directory.Move(Path.Combine(fixture.Root, "source"), Path.Combine(fixture.Root, "moved"));
        var window = new DiskSpaceWindow();
        var explorer = await window.AddIndexedSessionAsync(fixture.Lease!, AnalysisMetric.Files, cancellation: TestContext.Current.CancellationToken);
        window.Show(); Frame(window);
        try
        {
            var copy = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Copy path");
            Assert.False(copy.IsEnabled);
            var list = IndexedList(window);
            list.SelectedItem = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>());
            Click(window, "Explore selected folder");
            await Until(() => IndexedReady(window, explorer, 1));
            list.SelectedItem = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Name == "folder");
            Click(window, "Explore selected folder");
            await Until(() => explorer.Page?.Scope?.Path.Name == "folder");
            list.SelectedItem = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Name == "match-target.bin");
            await Until(() => copy.IsEnabled && explorer.Inspection?.Row.Path.Name == "match-target.bin");
            Click(window, "Copy path"); Frame(window);
            string expected = Path.Combine(source.CanonicalLocator, "folder", "match-target.bin");
            Assert.Equal(expected, await ClipboardExtensions.TryGetTextAsync(window.Clipboard!));
            Assert.False(File.Exists(fixture.PayloadPath));
            Assert.Equal(fixture.Payload, File.ReadAllBytes(Path.Combine(fixture.Root, "moved", "folder", "match-target.bin")));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
