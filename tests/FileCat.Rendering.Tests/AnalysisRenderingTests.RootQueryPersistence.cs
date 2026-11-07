using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using System.Collections.Immutable;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisMetric.FileSize)]
    [InlineData(AnalysisMetric.UniqueFileSize)]
    [InlineData(AnalysisMetric.OnDisk)]
    [InlineData(AnalysisMetric.Files)]
    public async Task NativeMemoryRootQuerySurvivesPrivateSaveReopenAndSecondSave(AnalysisMetric metric)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); using var styles = new ProductStyles();
        string source = Path.Combine(profile.Root, "source"); Directory.CreateDirectory(source);
        string other = Path.Combine(profile.Root, "other"); Directory.CreateDirectory(other);
        string payload = Path.Combine(source, "payload.bin"); byte[] expected = [1, 2, 3]; File.WriteAllBytes(payload, expected);
        using var acquisition = new DiskSpaceViewModel(profile.Services) { Metric = metric };
        await acquisition.StartAsync([source, other]); await Until(() => !acquisition.Running); await acquisition.RebuildAsync(true);
        var observed = acquisition.Tree!.Snapshot; Assert.Equal(2, observed.Roots.Length);
        string actualRoot = observed.Nodes.Single(n => n.Name == "payload.bin").RootId;
        const string root = "captured-memory-root";
        // Give the tiny native capture a non-wire display ID before serialization.
        // The metadata and source payload remain the actual platform observations.
        observed = observed with
        {
            Roots = observed.Roots.Select(r => r with { Id = r.Id == actualRoot ? root : "captured-memory-other" }).ToImmutableArray(),
            Nodes = observed.Nodes.Select(n => n with { RootId = n.RootId == actualRoot ? root : "captured-memory-other" }).ToImmutableArray()
        };
        Assert.NotEqual(root, actualRoot); acquisition.Dispose(); await acquisition.CloseCompletion;
        using var model = new DiskSpaceViewModel(profile.Services, new AnalysisInventory(Guid.NewGuid(), observed, 0)) { Metric = metric };
        model.Query = "root:\"" + root + "\" AND name:payload.bin"; await model.RebuildAsync(true);
        Assert.Null(model.FilterError); var totals = model.Tree!.Projection.Totals; Assert.Equal(1UL, totals.MatchingPaths);
        await model.SaveNewAsync("root-first.fcdsa");
        using var catalog = await model.ReadSavedCatalogAsync(); var entry = Assert.Single(catalog.Entries);
        using var reopened = await model.OpenSavedAsync(entry.Name, entry);
        Assert.Null(reopened.FilterError); Assert.Equal(model.Query, reopened.Query); Assert.Equal(totals.Known, reopened.Tree!.Projection.Totals.Known);
        Assert.Equal(1UL, reopened.Tree.Projection.Totals.MatchingPaths);
        reopened.Query += " AND NOT name:missing"; await reopened.RebuildAsync(true); await reopened.SaveNewAsync("root-second.fcdsa");
        using var second = await reopened.ReadSavedCatalogAsync(); var next = Assert.Single(second.Entries, e => e.Name == "root-second.fcdsa");
        using var final = await reopened.OpenSavedAsync(next.Name, next); Assert.Null(final.FilterError);
        Assert.Equal(1UL, final.Tree!.Projection.Totals.MatchingPaths); Assert.Equal(reopened.Query, final.Query);
        Assert.Equal(expected, File.ReadAllBytes(payload)); Assert.Empty(final.Selection.Marks); Assert.Empty(final.Selection.Basket);
        var restoredWindow = new DiskSpaceWindow();
        try
        {
            restoredWindow.AddSession(final); restoredWindow.Show(); Frame(restoredWindow);
            var locations = IndexedPersistenceControl<ComboBox>(restoredWindow, "Filter captured location");
            Assert.Contains(locations.Items, item => item?.ToString()?.Contains("[" + root + "]", StringComparison.Ordinal) == true);
        }
        finally { restoredWindow.ForceClose(); await restoredWindow.WaitForIndexedCloseAsync(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeIndexedRootQuerySurvivesReapplyGroupingAndSecondSave(bool grouped)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await IndexedFixture.CreateAsync(); var token = TestContext.Current.CancellationToken;
        string actualRoot = Assert.Single(source.Lease!.Descriptor.Roots).Id; const string root = "captured-indexed-root";
        Assert.NotEqual(root, actualRoot);
        var ids = new AnalysisRootDisplayIds(new Dictionary<string, string> { [actualRoot] = root });
        string query = "root:\"" + root + "\" AND name:file-";
        var window = new DiskSpaceWindow();
        try
        {
            var explorer = await window.AddIndexedSessionAsync(source.Lease, AnalysisMetric.FileSize, new(query) { RootDisplayIds = ids }, token, profile.Services);
            await source.ReleaseInitialLeaseAsync();
            window.Show(); Frame(window); Assert.Equal(300UL, explorer.Filter!.DefinitePaths);
            if (grouped) await SelectGrouping(window, AnalysisGroupingDimension.Extension);
            Click(window, "Save named analysis"); await Until(() => HasFilename(window));
            IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text = "roots-indexed.fcdsa"; Click(window, "Save new inventory");
            await Until(() => IndexedStatus(window, "Saved roots-indexed.fcdsa"));
            AnalysisSavedEntry entry;
            await using (var prepared = await FileCat.App.Services.AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            { using var catalog = await prepared.ReadCatalogAsync(token); entry = Assert.Single(catalog.Entries); }
            window.ForceClose(); await window.WaitForIndexedCloseAsync(); window = new DiskSpaceWindow();
            var reopened = await window.AddPreparedIndexedSessionAsync(async cancellation =>
            {
                await using var prepared = await FileCat.App.Services.AnalysisIndexedAcquisition.PrepareAsync(profile.Services, cancellation);
                await using var inventory = await prepared.LoadInventoryAsync(entry.Name, entry, cancellation);
                return await inventory.AcquireAsync(cancellation);
            }, AnalysisMetric.FileSize, null, entry.View, token, profile.Services);
            window.Show(); Frame(window); Assert.Equal(300UL, reopened.Filter!.DefinitePaths);
            Assert.Contains(IndexedPersistenceControl<ComboBox>(window, "Indexed captured location").Items, item => item?.ToString()?.Contains("[" + root + "]", StringComparison.Ordinal) == true);
            string revisedQuery = query + " AND name:file-0001.bin";
            IndexedPersistenceControl<TextBox>(window, "Filter indexed observations").Text = revisedQuery;
            Click(window, "Apply filter");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(c => c.IsEffectivelyVisible &&
                Avalonia.Automation.AutomationProperties.GetName(c) == "Indexed analysis status" && c.Text?.Contains(" 1 definite matches;", StringComparison.Ordinal) == true));
            Click(window, "Save named analysis"); await Until(() => HasFilename(window));
            IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text = "roots-again.fcdsa"; Click(window, "Save new inventory");
            await Until(() => IndexedStatus(window, "Saved roots-again.fcdsa"));
            window.ForceClose(); await window.WaitForIndexedCloseAsync();
            await using var check = await FileCat.App.Services.AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false);
            using var saved = await check.ReadCatalogAsync(token); var next = Assert.Single(saved.Entries, e => e.Name == "roots-again.fcdsa");
            Assert.Equal(root, next.View.RootDisplayIds!["1"]); Assert.Equal(revisedQuery, next.View.Query);
            await using var lastPreparation = await FileCat.App.Services.AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token);
            await using var lastInventory = await lastPreparation.LoadInventoryAsync(next.Name, next, token);
            await using var lastLease = await lastInventory.AcquireAsync(token);
            await using var lastExplorer = await AnalysisDatasetExplorer.CreateAsync(lastLease, next.View.Metric,
                new(next.View.Query, next.View.CaseSensitive, next.View.ShowUnknown, next.View.Root) { RootDisplayIds = next.View.RootDisplayIds }, token);
            Assert.Equal(1UL, lastExplorer.Filter!.DefinitePaths);
            Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        static bool HasFilename(Window w) => w.GetVisualDescendants().OfType<TextBox>().Any(c => c.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(c) == "Inventory filename");
        static bool IndexedStatus(Window w, string prefix) => w.GetVisualDescendants().OfType<TextBlock>().Any(c => c.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(c) == "Indexed analysis status" && c.Text?.StartsWith(prefix, StringComparison.Ordinal) == true);
    }

    [AvaloniaFact]
    public async Task IndexedSaveUnavailablePrivateStorageShowsFailureAndPreservesExistingPath()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        await using var source = await IndexedFixture.CreateAsync(); var token = TestContext.Current.CancellationToken;
        string privatePath = profile.Services.Paths.AnalysisSnapshotsDirectory; byte[] original = [7, 3, 1];
        Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!); Assert.False(Directory.Exists(privatePath)); File.WriteAllBytes(privatePath, original);
        var window = new DiskSpaceWindow();
        try
        {
            await window.AddIndexedSessionAsync(source.Lease!, cancellation: token, services: profile.Services); window.Show(); Frame(window);
            Click(window, "Save named analysis");
            await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(t => t.IsEffectivelyVisible && Avalonia.Automation.AutomationProperties.GetName(t) == "Inventory filename"));
            IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text = "refused.fcdsa"; Click(window, "Save new inventory");
            await Until(() => window.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible &&
                Avalonia.Automation.AutomationProperties.GetName(t) == "Indexed analysis status" && t.Text?.StartsWith("Inventory was not saved:", StringComparison.Ordinal) == true));
            Assert.Equal(original, File.ReadAllBytes(privatePath));
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(b => b.IsEffectivelyVisible && Equals(b.Content, "Save named analysis")).IsEnabled);
            Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath)); Assert.Equal(1, window.SessionCount);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
    }
}
