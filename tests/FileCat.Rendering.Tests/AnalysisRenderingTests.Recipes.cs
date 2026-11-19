using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Platform.Windows;

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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRecipeStagingChecksExplicitInclusionsAndPreservesProtectedOrChangedFiles(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var profile = new PreparationFixture();
        string input = Directory.CreateDirectory(Path.Combine(NativeValidationFixtureRoot(),
            "recipe-staging-" + Guid.NewGuid().ToString("N"))).FullName;
        string protectedFolder = Directory.CreateDirectory(Path.Combine(input, "backup-target")).FullName;
        string retained = Path.Combine(protectedFolder, "retained.zip");
        string keep = Path.Combine(input, "selected.zip");
        string omitted = Path.Combine(input, "omitted.zip");
        string changed = Path.Combine(input, "changed.zip");
        string alias = Path.Combine(input, "alias.zip");
        foreach (string path in new[] { keep, omitted, changed, retained, Path.Combine(input, "ordinary.txt") })
            File.WriteAllText(path, "initial " + Path.GetFileName(path));
        CreateGroupedNativeAlias(retained, alias);
        string redirect = profile.Services.Paths.RegistryBackupDirectory;
        if (OperatingSystem.IsWindows()) Junction.Create(redirect, protectedFolder);
        else Directory.CreateSymbolicLink(redirect, protectedFolder);
        var pool = new AnalysisSessionAdmission();
        var window = new DiskSpaceWindow(null, null, pool);
        try
        {
            window.Show();
            var opened = await window.AddOwnedAcquisitionAsync(profile.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, profile, input); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            });
            Frame(window);
            Click(window, "Mark matching files in this scope");
            await Until(() => HasSelection(6, 0), () => ComparisonText(window));

            await OpenPreviewAsync();
            Pick("selected.zip");
            Assert.False(Button("Add included to review basket").IsEnabled);
            Click(window, "Include selected candidate");
            Assert.True(Button("Add included to review basket").IsEnabled);
            Assert.True(Button("Close preview").IsDefault && Button("Close preview").IsCancel);
            Click(window, "Close preview");
            await Until(() => ComparisonControl<ListBox>(window, "Captured recipe candidates") is null);
            Assert.True(HasSelection(6, 0));

            await OpenPreviewAsync();
            foreach (string name in new[] { "selected.zip", "omitted.zip", "changed.zip", "retained.zip", "alias.zip" })
            {
                Pick(name);
                Click(window, "Include selected candidate");
            }
            Pick("omitted.zip");
            Click(window, "Exclude selected candidate");
            File.AppendAllText(changed, " changed after the captured observation");
            Click(window, "Add included to review basket");
            await Until(() => HasSelection(6, 1) &&
                ComparisonText(window).Contains("Protected entries kept 1.", StringComparison.Ordinal) &&
                ComparisonText(window).Contains("Changed or unverified entries kept 2.", StringComparison.Ordinal),
                () => ComparisonText(window), TimeSpan.FromSeconds(25));
            Click(window, "Review captured basket");
            await Until(() => ComparisonControl<ListBox>(window, "Captured basket entries")?.ItemCount == 1,
                () => ComparisonText(window));
            var basket = ComparisonControl<ListBox>(window, "Captured basket entries")!;
            Assert.Contains("selected.zip", basket.Items[0]!.ToString(), StringComparison.Ordinal);
            basket.SelectedIndex = 0;
            Click(window, "Remove selected from basket");
            Click(window, "Keep files / Close review");
            await Until(() => HasSelection(6, 0), () => ComparisonText(window));
            Assert.Equal("initial selected.zip", File.ReadAllText(keep));
            Assert.Equal("initial omitted.zip", File.ReadAllText(omitted));
            Assert.Equal("initial retained.zip", File.ReadAllText(retained));
            Assert.Equal("initial retained.zip", File.ReadAllText(alias));
            Assert.Contains("changed after", File.ReadAllText(changed), StringComparison.Ordinal);

            bool HasSelection(int marks, int basket) => indexed ? IndexedSelectionCount(window, marks, basket) :
                opened.Memory!.Selection.Marks.Count == marks && opened.Memory.Selection.Basket.Count == basket;
        }
        finally
        {
            window.ForceClose();
            await window.WaitForIndexedCloseAsync();
            Directory.Delete(redirect);
            Directory.Delete(input, recursive: true);
        }
        await AssertAllRuntimePlacesAvailableAsync(pool);

        async Task OpenPreviewAsync()
        {
            Click(window, "Candidate recipes…");
            await Until(() => ComparisonControl<ListBox>(window, "Locations for candidate preview") is not null);
            ComparisonControl<ComboBox>(window, "Candidate recipe")!.SelectedItem = "Downloads and installers";
            ComparisonControl<ListBox>(window, "Locations for candidate preview")!.SelectedIndex = 0;
            Click(window, "View candidates");
            await Until(() => ComparisonControl<ListBox>(window, "Captured recipe candidates")?.ItemCount == 5,
                () => ComparisonText(window), TimeSpan.FromSeconds(20));
        }
        Button Button(string label) => window.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && Equals(button.Content, label));
        void Pick(string name)
        {
            var rows = ComparisonControl<ListBox>(window, "Captured recipe candidates")!;
            rows.SelectedItem = rows.Items.Cast<string>().Single(row => row.StartsWith(name + "\n", StringComparison.Ordinal));
        }
    }
}
