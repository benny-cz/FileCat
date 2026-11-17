using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRecipeDialogPreviewsOnlyCapturedFilesWithContextAndLeavesSourceAndMarksAlone(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        string input = Path.Combine(fixture.Root, "input");
        string example = Path.Combine(input, "Example");
        string qualified = Path.Combine(example, "obj");
        string lookalike = Path.Combine(input, "Loose", "obj");
        Directory.CreateDirectory(qualified);
        Directory.CreateDirectory(lookalike);
        string found = Path.Combine(qualified, "project.assets.json");
        string excluded = Path.Combine(lookalike, "project.assets.json");
        File.WriteAllText(Path.Combine(example, "Example.csproj"), "project marker");
        File.WriteAllText(found, "generated fixture");
        File.WriteAllText(excluded, "user fixture");
        var pool = new AnalysisSessionAdmission();
        var window = new DiskSpaceWindow(null, null, pool);
        try
        {
            window.Show();
            var opened = await window.AddOwnedAcquisitionAsync(fixture.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, fixture, input); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            });
            Frame(window);
            Click(window, "Candidate recipes…");
            await Until(() => ComparisonControl<ListBox>(window, "Locations for candidate preview") is not null);
            var show = window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "View candidates"));
            Assert.False(show.IsEnabled);
            ComparisonControl<ComboBox>(window, "Candidate recipe")!.SelectedItem = ".NET restore output";
            ComparisonControl<ListBox>(window, "Locations for candidate preview")!.SelectedIndex = 0;
            Assert.True(show.IsEnabled);
            Click(window, "View candidates");
            await Until(() => ComparisonControl<ListBox>(window, "Captured recipe candidates")?.ItemCount == 1,
                () => ComparisonText(window), TimeSpan.FromSeconds(20));
            var list = ComparisonControl<ListBox>(window, "Captured recipe candidates")!;
            Assert.Contains("project.assets.json", (string)list.Items[0]!);
            Assert.Contains("1 candidate · 1 uncertain", ComparisonText(window));
            list.SelectedIndex = 0;
            Click(window, "Inspect recorded candidate");
            await Until(() => ComparisonControl<TextBlock>(window, "Recorded candidate evidence")?.Text?.Contains("Example/Example.csproj", StringComparison.Ordinal) == true,
                () => ComparisonText(window));
            Assert.Contains("Example/obj/project.assets.json", ComparisonControl<TextBlock>(window, "Recorded candidate evidence")!.Text);
            Click(window, "Close preview");
            if (indexed) Assert.True(IndexedSelectionCount(window, 0, 0));
            else
            {
                Assert.Empty(opened.Memory!.Selection.Marks);
                Assert.Empty(opened.Memory.Selection.Basket);
            }
            Assert.Equal(1, window.SessionCount);
            Assert.Equal("generated fixture", File.ReadAllText(found));
            Assert.Equal("user fixture", File.ReadAllText(excluded));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);
    }
}
