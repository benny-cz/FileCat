using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedInventoryWindowFitClosesBeforeOrAfterOpening(bool show)
    {
        var window = new Window { Width = 640, Height = 480 }; WindowFit.Attach(window);
        if (show) { window.Show(); Frame(window); }
        window.Close(); Dispatcher.UIThread.RunJobs(); Assert.False(window.IsVisible); Assert.False(WindowFit.Fit(window));
    }

    private static async Task<AnalysisSavedEntry> SaveIndexedInventoryFixture(PreparationFixture fixture, AnalysisSavedView view, bool privileged = false, bool directoryAllocation = false)
    {
        var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token)) { }
        string path = fixture.Services.Paths.AnalysisSnapshotsDirectory;
        return await AnalysisIo.Run(fixture.Services.Io, PathUtil.GetDeviceKey(path), cancellation =>
        {
            var snapshot = Fixture(); ulong[] ids = [0, ulong.MaxValue, 400, 2, ulong.MaxValue - 1, 1, 3];
            snapshot = snapshot with
            {
                Taint = AnalysisTaint.OrdinarySensitive | (privileged ? AnalysisTaint.PrivilegedDerived : 0),
                Roots = [snapshot.Roots[0] with { CanonicalLocator = Path.Combine(fixture.Root, "source-must-not-be-created"), DisplayLocator = "Offline source" }],
                Nodes = snapshot.Nodes.Select(n => n with { Id = ids[n.Id], ParentId = ids[n.ParentId], Name = n.Id == 4 ? "alpha.bin" : n.Id == 6 ? "beta.bin" : n.Name }).ToImmutableArray()
            };
            if (directoryAllocation) snapshot = snapshot with { Facts = snapshot.Facts.Add(new("directory", null, 400, "directory-self", "fixture", AnalysisMeasurement.Known(32), "fixture", DateTimeOffset.UnixEpoch, [])) };
            using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows()
                ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path))
                : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); var catalog = new AnalysisSavedCatalog(store);
            catalog.SaveNew("indexed.fcdsa", snapshot, Guid.NewGuid(), view, cancellation);
            using var entries = catalog.Read(cancellation); return Assert.Single(entries.Entries);
        }, token);
    }

    [AvaloniaTheory]
    [InlineData(false, "Table", false)]
    [InlineData(true, "Sunburst", true)]
    public async Task SavedIndexedWindowRestoresOriginalFolderAndControlsAfterNativeOwnersClose(bool descending, string chart, bool privileged)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var saved = new AnalysisSavedView(AnalysisMetric.FileSize, chart, "name:.bin", "1", "n:400", true, false, AnalysisTableSort.Name, descending);
        var entry = await SaveIndexedInventoryFixture(fixture, saved, privileged);
        string input = Path.Combine(fixture.Services.Paths.AnalysisSnapshotsDirectory, entry.Name); byte[] before = File.ReadAllBytes(input);
        var window = new DiskSpaceWindow();
        try
        {
            var explorer = await window.AddSavedIndexedSessionAsync(fixture.Services, entry.Name, entry, token); window.Show(); Frame(window);
            Assert.Equal(AnalysisOrigin.ImportedInventory, explorer.Descriptor.Origin); Assert.True(explorer.Descriptor.Taint.HasFlag(AnalysisTaint.ImportedUnverified));
            Assert.Equal(400UL, explorer.Page!.ParentId); Assert.Equal(AnalysisHierarchySort.Name, explorer.Page.Sort); Assert.Equal(descending, explorer.Page.Descending);
            Assert.Equal(descending ? new ulong[] { 3, ulong.MaxValue - 1 } : [ulong.MaxValue - 1, 3], explorer.Page.Children.Rows.Select(r => r.Path.Id));
            Assert.Equal(saved.Query, explorer.Filter!.Request.Text); Assert.Equal("1", explorer.Filter.Request.RootId); Assert.True(explorer.Filter.Request.CaseSensitive);
            Assert.Equal(AnalysisMetric.FileSize, explorer.Metric);
            var combos = window.GetVisualDescendants().OfType<ComboBox>().ToArray();
            Assert.Equal(chart, combos.Single(c => AutomationProperties.GetName(c) == "Indexed chart mode").SelectedItem);
            Assert.Equal("Name", combos.Single(c => AutomationProperties.GetName(c) == "Indexed table order").SelectedItem);
            Assert.Equal(descending, window.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Descending")).IsChecked);
            var provenance = window.GetVisualDescendants().OfType<TextBlock>().Single(t => (AutomationProperties.GetName(t) ?? "").StartsWith("Analysis provenance:", StringComparison.Ordinal));
            Assert.Contains("Offline imported observations", provenance.Text); Assert.Contains("unverified", provenance.Text);
            Assert.Equal(privileged, provenance.Text!.Contains("Privileged-derived metadata", StringComparison.Ordinal));
            Assert.Equal(before, File.ReadAllBytes(input)); Assert.False(Directory.Exists(Path.Combine(fixture.Root, "source-must-not-be-created")));
            window.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Close analysis")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => window.SessionCount == 0);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), b => b.IsEffectivelyVisible && Equals(b.Content, "Keep open"));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(fixture.Services.Paths.AnalysisScratchDirectory)); Assert.Equal(before, File.ReadAllBytes(input));
    }

    [AvaloniaFact]
    public async Task MissingIndexedSavedScopesExplainTheirFallbackWithoutBindingHistoricalLocators()
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var fixture = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var entry = await SaveIndexedInventoryFixture(fixture, AnalysisSavedView.Default with { Root = "absent", Scope = "n:999" }); var window = new DiskSpaceWindow();
        try
        {
            var explorer = await window.AddSavedIndexedSessionAsync(fixture.Services, entry.Name, entry, token); window.Show(); Frame(window);
            Assert.Equal(0UL, explorer.Page!.ParentId); Assert.Null(explorer.Filter!.Request.RootId);
            var text = string.Join('\n', window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Contains("saved location is absent", text); Assert.Contains("saved folder is absent", text);
            Assert.False(Directory.Exists(Path.Combine(fixture.Root, "source-must-not-be-created")));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(fixture.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaTheory]
    [InlineData("id")]
    [InlineData("bytes")]
    [InlineData("coverage")]
    [InlineData("taint")]
    public async Task RejectedIndexedCatalogSelectionPreservesNativeSourceAndReleasesItsPendingSession(string fault)
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var entry = await SaveIndexedInventoryFixture(fixture, AnalysisSavedView.Default); var window = new DiskSpaceWindow();
        byte[] before = File.ReadAllBytes(Path.Combine(fixture.Services.Paths.AnalysisSnapshotsDirectory, entry.Name));
        var wrong = fault switch
        {
            "id" => entry with { InventoryId = Guid.NewGuid() }, "bytes" => entry with { Bytes = entry.Bytes + 1 },
            "coverage" => entry with { Coverage = NamespaceCoverage.Partial }, _ => entry with { Taint = entry.Taint | AnalysisTaint.PrivilegedDerived }
        };
        try { await Assert.ThrowsAsync<InvalidDataException>(() => window.AddSavedIndexedSessionAsync(fixture.Services, entry.Name, wrong, token)); }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(fixture.Services.Paths.AnalysisScratchDirectory));
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(fixture.Services.Paths.AnalysisSnapshotsDirectory, entry.Name)));
        var slots = new List<AnalysisSessionAdmission.Slot>();
        try { for (int n = 0; n < AnalysisLimits.Sessions; n++) slots.Add(AnalysisSessionAdmission.Shared.Acquire()); }
        finally { foreach (var slot in slots) await slot.CloseAfterAsync(Task.CompletedTask); }
    }

    [AvaloniaFact]
    public async Task MemoryFallbackPreparationRefusesIndexedInventoryWithoutTouchingItsInput()
    {
        OrdinaryPreparation(); using var fixture = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var entry = await SaveIndexedInventoryFixture(fixture, AnalysisSavedView.Default);
        string input = Path.Combine(fixture.Services.Paths.AnalysisSnapshotsDirectory, entry.Name); byte[] before = File.ReadAllBytes(input);
        File.WriteAllText(fixture.Services.Paths.AnalysisPrivateStateFile, "unsupported-state");
        await using var prepared = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token); Assert.NotNull(prepared.MemoryPreparationNotice);
        await Assert.ThrowsAsync<NotSupportedException>(() => prepared.LoadInventoryAsync(entry.Name, entry, token));
        Assert.Equal(before, File.ReadAllBytes(input)); Assert.Empty(Directory.GetFiles(fixture.Services.Paths.AnalysisScratchDirectory));
    }
}
