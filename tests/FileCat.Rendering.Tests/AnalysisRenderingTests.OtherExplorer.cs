using System.Collections.Immutable;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    private static string OtherStatus(Window window) => window.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(text => text.IsEffectivelyVisible && AutomationProperties.GetName(text) == "Indexed analysis status")?.Text ?? "";
    private static bool ExactOtherReady(Window window) => OtherStatus(window).StartsWith("Exact Other subset.", StringComparison.Ordinal);
    private static bool OtherParentReady(Window window) => OtherStatus(window).StartsWith("Complete under captured policy.", StringComparison.Ordinal) ||
        OtherStatus(window).StartsWith("Partial namespace observations;", StringComparison.Ordinal);
    private static string OtherViewDiagnostic(Window window) => string.Join(" | ", window.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible).Select(text => text.Text));
    private static void OtherKeyStroke(Window window, Key key, PhysicalKey physical)
    { window.KeyPress(key, RawInputModifiers.None, physical, null); window.KeyRelease(key, RawInputModifiers.None, physical, null); }
    private static async Task<AnalysisInventory> ReadOtherReference(PreparationFixture profile, string name)
    {
        string path = profile.Services.Paths.AnalysisSnapshotsDirectory;
        return await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), cancellation =>
        {
            using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path)) : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); return store.Load(name, cancellation);
        }, TestContext.Current.CancellationToken);
    }

    [AvaloniaTheory]
    [InlineData("Treemap")]
    [InlineData("Sunburst")]
    public async Task OtherExplorerNativeWindowKeepsCanonicalMembershipThroughFocusSortHistoryAndCatalogRestore(string mode)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync();
        var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await prepared.SaveInventoryAsync(source.Lease!, "reference.fcdsa", AnalysisSavedView.Default, token);
        using var reference = await ReadOtherReference(profile, "reference.fcdsa"); var snapshot = reference.Snapshot;
        var tree = AnalysisTree.Build(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.Files, cancellation: token), false, token);
        var window = new DiskSpaceWindow { Width = 1180, Height = 960 };
        await window.AddIndexedSessionAsync(source.Lease!, AnalysisMetric.Files, cancellation: token, services: profile.Services); await source.ReleaseInitialLeaseAsync();
        window.Show(); Frame(window);
        try
        {
            var list = IndexedList(window); var root = Assert.Single(list.Items.OfType<AnalysisHierarchyRow>());
            list.SelectedItem = root; Click(window, "Explore selected folder"); await Until(() => list.Items.OfType<AnalysisHierarchyRow>().Count() > 1);
            IndexedPersistenceControl<ComboBox>(window, "Indexed chart mode").SelectedItem = mode; Frame(window);
            var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(control => control.IsEffectivelyVisible);
            string? selected = null; chart.Activated += (key, _, enter) => { if (!enter) selected = key; };
            object? before = list.ItemsSource; Assert.True(chart.Focus()); OtherKeyStroke(window, Key.End, PhysicalKey.End);
            Assert.NotNull(selected); var scope = AnalysisSavedCatalog.SplitAggregationScope(selected); Assert.True(scope.Skipped > 0);
            Assert.Equal(AnalysisTree.Key(root.Path.Id), scope.Scope); Assert.Same(before, list.ItemsSource); Assert.False(ExactOtherReady(window));
            Assert.True(IndexedSelectionCount(window, 0, 0)); Assert.Contains("Press Enter or double-click", IndexedDetails(window).Text);
            OtherKeyStroke(window, Key.Enter, PhysicalKey.Enter); await Until(() => ExactOtherReady(window), () => OtherViewDiagnostic(window)); Frame(window);
            string exactKey = AnalysisTree.OtherKey(scope.Scope, scope.Skipped);
            var expected = tree.ChildrenOf(exactKey).Where(row => row.NodeId.HasValue).Select(row => row.NodeId!.Value).ToHashSet();
            int matching = tree.CaptureMatchingFiles(exactKey, token).NodeIds.Length;
            Assert.NotEmpty(expected); Click(window, "Mark matching files in this scope"); await Until(() => IndexedSelectionCount(window, matching, 0));
            Click(window, "Add to review basket"); Assert.True(IndexedSelectionCount(window, matching, matching));
            var order = IndexedPersistenceControl<ComboBox>(window, "Indexed table order"); var descending = IndexedPersistenceControl<CheckBox>(window, "Indexed descending table order");
            foreach (int sort in new[] { 0, 1, 2, 3, 4 }) foreach (bool direction in new[] { false, true })
            {
                object? old = list.ItemsSource; order.SelectedIndex = sort; descending.IsChecked = direction;
                string label = order.SelectedItem + (direction ? " · Descending" : " · Ascending") + " · exact geometry subset";
                await Until(() => ExactOtherReady(window) && !ReferenceEquals(old, list.ItemsSource) && window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.EndsWith(label, StringComparison.Ordinal) == true));
                var actual = new HashSet<ulong>();
                while (true)
                {
                    foreach (var row in list.Items.OfType<AnalysisHierarchyRow>()) Assert.True(actual.Add(row.Path.Id));
                    var next = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Next entries")); if (!next.IsEnabled) break;
                    old = list.ItemsSource; Click(window, "Next entries"); await Until(() => ExactOtherReady(window) && !ReferenceEquals(old, list.ItemsSource));
                }
                Assert.True(expected.SetEquals(actual)); Assert.True(IndexedSelectionCount(window, matching, matching));
            }
            Click(window, "Parent"); await Until(() => OtherParentReady(window) && list.Items.OfType<AnalysisHierarchyRow>().Count() > 1, () => OtherViewDiagnostic(window));
            Click(window, "Back"); await Until(() => ExactOtherReady(window), () => OtherViewDiagnostic(window)); Assert.All(list.Items.OfType<AnalysisHierarchyRow>(), row => Assert.Contains(row.Path.Id, expected));
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Forward")).IsEnabled);
            Click(window, "Forward"); await Until(() => OtherParentReady(window), () => OtherViewDiagnostic(window)); Click(window, "Back"); await Until(() => ExactOtherReady(window), () => OtherViewDiagnostic(window));
            order.SelectedItem = "Name"; descending.IsChecked = false;
            await Until(() => ExactOtherReady(window) && window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.EndsWith(" · Name · Ascending · exact geometry subset", StringComparison.Ordinal) == true));
            IndexedPersistenceControl<TextBox>(window, "Filter indexed observations").Text = "name:future-only.bin";
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(source.PayloadPath)!, "future-only.bin"), [5]);
            Click(window, "Save named analysis"); await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Inventory filename"));
            IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text = "other.fcdsa"; Click(window, "Save new inventory");
            await Until(() => OtherStatus(window).StartsWith("Saved other.fcdsa", StringComparison.Ordinal), () => OtherViewDiagnostic(window));
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            {
                using var catalog = await prepared.ReadCatalogAsync(token); var entry = Assert.Single(catalog.Entries, entry => entry.Name == "other.fcdsa");
                Assert.Equal(exactKey, entry.View.Scope); Assert.Equal("", entry.View.Query); Assert.Equal(AnalysisTableSort.Name, entry.View.Sort); Assert.False(entry.View.SortDescending);
            }
            using (var saved = await ReadOtherReference(profile, "other.fcdsa"))
            { Assert.Equal(snapshot.Nodes.Length, saved.Snapshot.Nodes.Length); Assert.DoesNotContain(saved.Snapshot.Nodes, row => row.Name == "future-only.bin"); }
            Click(window, "Open saved analysis"); await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Saved inventory filename"));
            Click(window, "Browse saved analyses"); await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Saved analyses" && control.ItemCount == 2));
            var choices = IndexedPersistenceControl<ComboBox>(window, "Saved analyses"); choices.SelectedItem = choices.Items.Cast<object>().Single(item => item.ToString()!.StartsWith("other.fcdsa ·", StringComparison.Ordinal)); Click(window, "Open inventory");
            await Until(() => window.SessionCount == 2 && ExactOtherReady(window), () => OtherViewDiagnostic(window)); Frame(window);
            var restored = IndexedList(window).Items.OfType<AnalysisHierarchyRow>().ToArray(); Assert.All(restored, row => Assert.Contains(row.Path.Id, expected));
            Assert.Equal(expected.Count < 128 ? expected.Count : 128, restored.Length); Assert.True(IndexedSelectionCount(window, 0, 0));
            Assert.Equal("", IndexedPersistenceControl<TextBox>(window, "Filter indexed observations").Text);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text?.Contains("Offline imported observations", StringComparison.Ordinal) == true);
            Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(source.PrivatePath)); Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaTheory]
    [InlineData(1, 32, true)]
    [InlineData(2, 0, true)]
    [InlineData(3, 65568, false)]
    public async Task OtherExplorerSavedAccountingOnlyAndEmptyScopesStayExactWhileAbsentSuffixIsExplained(int skipped, int area, bool exact)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var saved = AnalysisSavedView.Default with { Metric = AnalysisMetric.OnDisk, Scope = AnalysisTree.OtherKey("n:1", skipped), Chart = "Sunburst", Sort = AnalysisTableSort.Name, SortDescending = false };
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false)) { }
        string path = profile.Services.Paths.AnalysisSnapshotsDirectory;
        var entry = await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), cancellation =>
        {
            var snapshot = Fixture(); snapshot = snapshot with { Nodes = [snapshot.Nodes[0], snapshot.Nodes[3] with { ParentId = 1 }], Objects = [snapshot.Objects[0]],
                Facts = [snapshot.Facts[0], new("directory", null, 1, "directory-self", "fixture", AnalysisMeasurement.Known(32), "fixture", DateTimeOffset.UnixEpoch, [])] };
            using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path)) : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); var catalog = new AnalysisSavedCatalog(store);
            catalog.SaveNew("edge.fcdsa", snapshot, Guid.NewGuid(), saved, cancellation); using var entries = catalog.Read(cancellation); return Assert.Single(entries.Entries);
        }, token);
        byte[] before = File.ReadAllBytes(Path.Combine(path, entry.Name)); var window = new DiskSpaceWindow();
        try
        {
            var explorer = await window.AddSavedIndexedSessionAsync(profile.Services, entry.Name, entry, token); window.Show(); Frame(window);
            Assert.Equal(exact, ExactOtherReady(window)); Assert.Equal((UInt128)area, explorer.Chart!.ScopeValue(explorer.Chart.Scope));
            Assert.Equal(exact ? 0 : 1, explorer.Page!.Children.Rows.Length); Assert.Equal(skipped == 2 ? 0 : 1, explorer.Page.Buckets.Length);
            Assert.Equal(exact ? skipped : 0, explorer.GeometrySkipped); Assert.True(IndexedSelectionCount(window, 0, 0));
            if (!exact) Assert.Contains("saved Other subset is absent", OtherStatus(window));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(path, entry.Name)));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }
}
