using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [Fact]
    public async Task NativeSavedRecipeStorePreservesCapturedScopeAcrossPrivateOpens()
    {
        OrdinaryPreparation();
        using var profile = new PreparationFixture();
        await using var source = await IndexedFixture.CreateAsync();
        var captured = source.Lease!.Descriptor;
        var token = TestContext.Current.CancellationToken;
        var rule = AnalysisSavedRecipe.Create(Guid.NewGuid(), "Installers", captured.Identity, captured.Roots,
            [captured.Roots[0].Id], "ext=msi OR ext=pkg", false, AnalysisMetric.FileSize);

        await using (var writer = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await writer.SaveRecipeAsync(rule, captured.Identity, captured.Roots, token);
        string file = Path.Combine(profile.Services.Paths.AnalysisSnapshotsDirectory, AnalysisPrivateNames.Recipe(rule.Id));
        byte[] original = File.ReadAllBytes(file);
        await using (var reader = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
        {
            var found = Assert.Single(await reader.ReadRecipesAsync(captured.Identity, captured.Roots, token));
            Assert.Equal(rule.Query, found.Query);
            Assert.Equal(rule.RootIds.ToArray(), found.RootIds.ToArray());
            Assert.Equal(rule.Id, found.Id);
        }
        await using (var wrong = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            Assert.Empty(await wrong.ReadRecipesAsync(captured.Identity with { SessionId = Guid.NewGuid() }, captured.Roots, token));
        await using (var catalog = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
        using (var index = await catalog.ReadCatalogAsync(token))
        {
            Assert.Empty(index.Entries);
            Assert.Empty(index.UnindexedNames);
        }
        Assert.Equal(original, File.ReadAllBytes(file));
        Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSavedRecipeDialogKeepsExplicitRootsWhenTheQueryMentionsAnotherLocation(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        string first = Path.Combine(fixture.Root, "selected");
        string second = Path.Combine(fixture.Root, "unselected");
        Directory.CreateDirectory(Path.Combine(first, "logs"));
        Directory.CreateDirectory(Path.Combine(second, "logs"));
        string included = Path.Combine(first, "logs", "wanted.log");
        string outside = Path.Combine(second, "logs", "outside.log");
        File.WriteAllText(included, "wanted observation");
        File.WriteAllText(outside, "outside observation");
        File.WriteAllText(Path.Combine(first, "notes.txt"), "different extension");
        var window = new DiskSpaceWindow();
        var wait = TimeSpan.FromSeconds(25);
        try
        {
            window.Show();
            await window.AddOwnedAcquisitionAsync(fixture.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: indexed);
                try { await StartAcquiredAsync(owner, fixture, first, second); return owner; }
                catch { await owner.DisposeAsync(); throw; }
            });
            Frame(window);
            Click(window, "Candidate recipes…");
            Click(window, "Saved recipes…");
            await Until(() => ComparisonText(window).Contains("No filters saved for these captured observations.", StringComparison.Ordinal), () => ComparisonText(window), wait);
            Click(window, "Create saved filter…");
            await Until(() => ComparisonControl<ListBox>(window, "Locations for saved recipe")?.ItemCount == 2);
            var locations = ComparisonControl<ListBox>(window, "Locations for saved recipe")!;
            var wanted = locations.Items.OfType<RootBinding>().Single(root => root.Label == "selected");
            var unwanted = locations.Items.OfType<RootBinding>().Single(root => root.Label == "unselected");
            ComparisonControl<TextBox>(window, "Saved recipe name")!.Text = "Scoped logs";
            ComparisonControl<TextBox>(window, "Saved recipe filter")!.Text = $"(path:logs AND ext=log) OR root={unwanted.Id}";
            locations.SelectedItem = wanted;
            Click(window, "Save and preview filter");
            await WaitForWantedAsync();
            Click(window, "Close preview");
            Click(window, "Candidate recipes…");
            Click(window, "Saved recipes…");
            await Until(() => ComparisonControl<ListBox>(window, "Saved filters for these observations")?.ItemCount == 1, () => ComparisonText(window), wait);
            ComparisonControl<ListBox>(window, "Saved filters for these observations")!.SelectedIndex = 0;
            Click(window, "Preview saved filter");
            await WaitForWantedAsync();
            Click(window, "Close preview");
            Assert.Equal(1, window.SessionCount);
            Assert.Equal("wanted observation", File.ReadAllText(included));
            Assert.Equal("outside observation", File.ReadAllText(outside));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }

        async Task WaitForWantedAsync()
        {
            await Until(() => ComparisonControl<ListBox>(window, "Captured recipe candidates")?.ItemCount == 1,
                () => ComparisonText(window), wait);
            var item = Assert.IsType<string>(ComparisonControl<ListBox>(window, "Captured recipe candidates")!.Items[0]);
            Assert.Contains("wanted.log", item, StringComparison.Ordinal);
            Assert.DoesNotContain("outside.log", item, StringComparison.Ordinal);
        }
    }
}
