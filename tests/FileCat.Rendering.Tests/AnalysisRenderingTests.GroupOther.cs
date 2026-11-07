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
    private static bool GroupOtherReady(Window window) => OtherStatus(window).StartsWith("Grouped view ready.", StringComparison.Ordinal) && OtherStatus(window).Contains("Exact Other subset.", StringComparison.Ordinal);
    private static bool GroupParentReady(Window window) => OtherStatus(window).StartsWith("Grouped view ready.", StringComparison.Ordinal) && !OtherStatus(window).Contains("Exact Other subset.", StringComparison.Ordinal);
    private static async Task<HashSet<ulong>> GroupOtherTablePaths(Window window)
    {
        var result = new HashSet<ulong>(); var list = IndexedList(window);
        while (true)
        {
            foreach (var row in list.Items.OfType<AnalysisDatasetGroupRow>()) Assert.True(result.Add(row.Member.Path.Id));
            var next = window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Next entries")); if (!next.IsEnabled) return result;
            object? before = list.ItemsSource; Click(window, "Next entries"); await Until(() => GroupOtherReady(window) && !ReferenceEquals(before, list.ItemsSource), () => OtherViewDiagnostic(window));
        }
    }

    [AvaloniaTheory]
    [InlineData(AnalysisGroupingDimension.FileType, "Treemap")]
    [InlineData(AnalysisGroupingDimension.FileType, "Sunburst")]
    [InlineData(AnalysisGroupingDimension.Extension, "Treemap")]
    [InlineData(AnalysisGroupingDimension.Extension, "Sunburst")]
    [InlineData(AnalysisGroupingDimension.ModifiedAge, "Treemap")]
    [InlineData(AnalysisGroupingDimension.ModifiedAge, "Sunburst")]
    [InlineData(AnalysisGroupingDimension.CapturedLocation, "Treemap")]
    [InlineData(AnalysisGroupingDimension.CapturedLocation, "Sunburst")]
    public async Task GroupOtherNativeWindowRetainsExactMembersThroughChartFocusEveryOrderHistoryAndCatalogRestore(AnalysisGroupingDimension dimension, string mode)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(sharedAlias: true);
        var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await prepared.SaveInventoryAsync(source.Lease!, "reference.fcdsa", AnalysisSavedView.Default, token);
        using var reference = await ReadOtherReference(profile, "reference.fcdsa"); var snapshot = reference.Snapshot;
        var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.UniqueFileSize, cancellation: token), dimension, false, token);
        string parent = Assert.Single(tree.Groups.Values, group => group.Kind == AnalysisGroupKind.Entries && group.Members.Length > 100).Definition.Key;
        var window = new DiskSpaceWindow { Width = 1180, Height = 960 };
        await window.AddIndexedSessionAsync(source.Lease!, AnalysisMetric.UniqueFileSize, cancellation: token, services: profile.Services); await source.ReleaseInitialLeaseAsync();
        window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, dimension); var list = IndexedList(window); list.SelectedItem = list.Items.OfType<AnalysisDatasetGroup>().Single(group => group.Definition.Key == parent);
            Click(window, "Explore selected group"); await Until(() => MemberReady(window));
            IndexedPersistenceControl<ComboBox>(window, "Indexed chart mode").SelectedItem = mode; Frame(window);
            var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(control => control.IsEffectivelyVisible); string? focused = null;
            chart.Activated += (key, _, enter) => { if (!enter) focused = key; };
            object? before = list.ItemsSource; Assert.True(chart.Focus()); OtherKeyStroke(window, Key.End, PhysicalKey.End);
            Assert.NotNull(focused); var aggregate = AnalysisSavedCatalog.SplitAggregationScope(focused); Assert.Equal(parent, aggregate.Scope); Assert.True(aggregate.Skipped > 0);
            Assert.Same(before, list.ItemsSource); Assert.True(GroupParentReady(window)); Assert.True(IndexedSelectionCount(window, 0, 0)); Assert.Contains("Press Enter or double-click", IndexedDetails(window).Text);
            OtherKeyStroke(window, Key.Enter, PhysicalKey.Enter); await Until(() => GroupOtherReady(window), () => OtherViewDiagnostic(window)); Frame(window);
            string scope = AnalysisTree.OtherKey(parent, aggregate.Skipped); var expected = tree.ChildrenOf(scope).Where(item => item.NodeId.HasValue).Select(item => item.NodeId!.Value).ToHashSet();
            int matching = tree.CaptureMatchingFiles(scope, token).NodeIds.Length; Assert.NotEmpty(expected);
            Click(window, "Mark matching files in this scope"); await Until(() => IndexedSelectionCount(window, matching, 0)); Click(window, "Add to review basket"); Assert.True(IndexedSelectionCount(window, matching, matching));
            var order = IndexedPersistenceControl<ComboBox>(window, "Indexed table order"); var descending = IndexedPersistenceControl<CheckBox>(window, "Indexed descending table order");
            foreach (int sort in new[] { 0, 1, 2, 3, 4 }) foreach (bool direction in new[] { false, true })
            {
                object? old = list.ItemsSource; order.SelectedIndex = sort; descending.IsChecked = direction;
                string label = order.SelectedItem + (direction ? " · Descending" : " · Ascending") + " · exact geometry subset";
                await Until(() => GroupOtherReady(window) && !ReferenceEquals(old, list.ItemsSource) && window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.EndsWith(label, StringComparison.Ordinal) == true), () => OtherViewDiagnostic(window));
                Assert.True(expected.SetEquals(await GroupOtherTablePaths(window))); Assert.True(IndexedSelectionCount(window, matching, matching));
            }
            Click(window, "Parent"); await Until(() => GroupParentReady(window) && list.Items.OfType<AnalysisDatasetGroupRow>().Any(), () => OtherViewDiagnostic(window));
            Click(window, "Back"); await Until(() => GroupOtherReady(window), () => OtherViewDiagnostic(window)); Assert.All(list.Items.OfType<AnalysisDatasetGroupRow>(), row => Assert.Contains(row.Member.Path.Id, expected));
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Forward")).IsEnabled);
            Click(window, "Forward"); await Until(() => GroupParentReady(window), () => OtherViewDiagnostic(window)); Click(window, "Back"); await Until(() => GroupOtherReady(window), () => OtherViewDiagnostic(window));
            order.SelectedItem = "Name"; descending.IsChecked = false;
            await Until(() => GroupOtherReady(window) && window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text?.EndsWith(" · Name · Ascending · exact geometry subset", StringComparison.Ordinal) == true));
            IndexedPersistenceControl<TextBox>(window, "Filter indexed observations").Text = "name:future-only.bin";
            File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(source.PayloadPath)!, "future-only.bin"), [5]);
            Click(window, "Save named analysis"); await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Inventory filename"));
            IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text = "group-other.fcdsa"; Click(window, "Save new inventory");
            await Until(() => OtherStatus(window).StartsWith("Saved group-other.fcdsa", StringComparison.Ordinal), () => OtherViewDiagnostic(window));
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            {
                using var catalog = await prepared.ReadCatalogAsync(token); var entry = Assert.Single(catalog.Entries, entry => entry.Name == "group-other.fcdsa");
                Assert.Equal(scope, entry.View.Scope); Assert.Equal(dimension, entry.View.Grouping); Assert.Equal("", entry.View.Query); Assert.Equal(AnalysisTableSort.Name, entry.View.Sort); Assert.False(entry.View.SortDescending);
            }
            using (var saved = await ReadOtherReference(profile, "group-other.fcdsa"))
            { Assert.Equal(snapshot.Nodes.Length, saved.Snapshot.Nodes.Length); Assert.DoesNotContain(saved.Snapshot.Nodes, row => row.Name == "future-only.bin"); }
            Click(window, "Open saved analysis"); await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Saved inventory filename"));
            Click(window, "Browse saved analyses"); await Until(() => window.GetVisualDescendants().OfType<ComboBox>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Saved analyses" && control.ItemCount == 2));
            var choices = IndexedPersistenceControl<ComboBox>(window, "Saved analyses"); choices.SelectedItem = choices.Items.Cast<object>().Single(item => item.ToString()!.StartsWith("group-other.fcdsa ·", StringComparison.Ordinal)); Click(window, "Open inventory");
            await Until(() => window.SessionCount == 2 && GroupOtherReady(window), () => OtherViewDiagnostic(window)); Frame(window);
            Assert.True(expected.SetEquals(await GroupOtherTablePaths(window))); Assert.True(IndexedSelectionCount(window, 0, 0)); Assert.Equal((int)dimension, GroupingSelector(window).SelectedIndex);
            Assert.Equal("", IndexedPersistenceControl<TextBox>(window, "Filter indexed observations").Text); Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text?.Contains("Offline imported observations", StringComparison.Ordinal) == true);
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(source.PrivatePath)); Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaTheory]
    [InlineData(1, true, 0, 1)]
    [InlineData(2, true, 0, 0)]
    [InlineData(3, false, 1, 1)]
    public async Task GroupOtherSavedMissingSharedBucketRemainsReviewableAndEmptyOrAbsentScopesAreExplicit(int skipped, bool exact, int rows, int matching)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        var view = AnalysisSavedView.Default with { Metric = AnalysisMetric.OnDisk, Grouping = AnalysisGroupingDimension.Extension, Query = "name:source.cs",
            Scope = AnalysisTree.OtherKey(AnalysisGroupingRules.Shared.Key, skipped), Chart = "Sunburst" };
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false)) { }
        string path = profile.Services.Paths.AnalysisSnapshotsDirectory;
        var entry = await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), cancellation =>
        {
            var snapshot = Fixture(); snapshot = snapshot with { Nodes = snapshot.Nodes.SetItem(4, snapshot.Nodes[4] with { ObjectId = "a" }),
                Objects = snapshot.Objects.RemoveAt(1).SetItem(0, snapshot.Objects[0] with { LinkCount = AnalysisMeasurement.Known(2) }),
                Facts = snapshot.Facts.RemoveAt(1).SetItem(0, snapshot.Facts[0] with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Unsupported) }),
                Roots = [snapshot.Roots[0] with { CanonicalLocator = Path.Combine(profile.Root, "source-must-not-be-created") }] };
            using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path)) : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); var catalog = new AnalysisSavedCatalog(store);
            catalog.SaveNew("shared-other.fcdsa", snapshot, Guid.NewGuid(), view, cancellation); using var entries = catalog.Read(cancellation); return Assert.Single(entries.Entries);
        }, token);
        byte[] before = File.ReadAllBytes(Path.Combine(path, entry.Name)); var window = new DiskSpaceWindow();
        try
        {
            await window.AddSavedIndexedSessionAsync(profile.Services, entry.Name, entry, token); window.Show(); Frame(window);
            Assert.Equal(exact, GroupOtherReady(window)); Assert.Equal(rows, IndexedList(window).Items.OfType<AnalysisDatasetGroupRow>().Count());
            if (!exact) Assert.Contains("saved Other subset is absent", OtherStatus(window));
            Assert.True(IndexedSelectionCount(window, 0, 0)); Click(window, "Mark matching files in this scope"); await Until(() => IndexedSelectionCount(window, matching, 0));
            await Until(() => OtherStatus(window).StartsWith("Marked ", StringComparison.Ordinal));
            if (matching != 0) { Click(window, "Add to review basket"); Assert.True(IndexedSelectionCount(window, matching, matching)); }
            if (skipped == 1)
            {
                Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.Content?.ToString()?.StartsWith("Shared files — charged once · Unknown", StringComparison.Ordinal) == true);
                Click(window, "Shared files — charged once · Unknown"); await Until(() => IndexedDetails(window).Text?.Contains("of 1. Captured aliases", StringComparison.Ordinal) == true);
            }
            Assert.False(Directory.Exists(Path.Combine(profile.Root, "source-must-not-be-created"))); Assert.Equal(before, File.ReadAllBytes(Path.Combine(path, entry.Name)));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    [AvaloniaTheory]
    [InlineData("Treemap")]
    [InlineData("Sunburst")]
    public async Task GroupOtherNativeOverviewKeepsCategoriesBoundedAndFocusedCaptureInsideItsSavedScope(string mode)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); await using var source = await IndexedFixture.CreateAsync(extraExtensions: 40);
        var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await prepared.SaveInventoryAsync(source.Lease!, "reference.fcdsa", AnalysisSavedView.Default, token);
        using var reference = await ReadOtherReference(profile, "reference.fcdsa");
        var tree = AnalysisTree.BuildGrouped(reference.Snapshot, AnalysisAggregate.Project(reference.Snapshot, AnalysisMetric.Files, cancellation: token), AnalysisGroupingDimension.Extension, false, token);
        var window = new DiskSpaceWindow { Width = 1180, Height = 960 }; await window.AddIndexedSessionAsync(source.Lease!, AnalysisMetric.Files, cancellation: token, services: profile.Services);
        await source.ReleaseInitialLeaseAsync(); window.Show(); Frame(window);
        try
        {
            await SelectGrouping(window, AnalysisGroupingDimension.Extension); var list = IndexedList(window);
            IndexedPersistenceControl<ComboBox>(window, "Indexed chart mode").SelectedItem = mode; Frame(window);
            var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(control => control.IsEffectivelyVisible); string? focused = null;
            chart.Activated += (key, _, enter) => { if (!enter) focused = key; };
            object? before = list.ItemsSource; chart.Focus(); OtherKeyStroke(window, Key.End, PhysicalKey.End);
            Assert.NotNull(focused); var parsed = AnalysisSavedCatalog.SplitAggregationScope(focused); Assert.Equal(AnalysisTree.RootKey, parsed.Scope); Assert.True(parsed.Skipped > 0); Assert.Same(before, list.ItemsSource);
            OtherKeyStroke(window, Key.Enter, PhysicalKey.Enter); await Until(() => GroupOtherReady(window), () => OtherViewDiagnostic(window)); Frame(window);
            string scope = AnalysisTree.OtherKey(AnalysisTree.RootKey, parsed.Skipped); var expected = tree.ChildrenOf(scope).Select(item => item.Key).ToHashSet();
            Assert.True(expected.SetEquals(list.Items.OfType<AnalysisDatasetGroup>().Select(group => group.Definition.Key)));
            list.SelectedIndex = 0; Assert.True(list.Focus()); OtherKeyStroke(window, Key.Home, PhysicalKey.Home);
            window.KeyPress(Key.End, RawInputModifiers.Shift, PhysicalKey.End, null); window.KeyRelease(Key.End, RawInputModifiers.Shift, PhysicalKey.End, null);
            await Until(() => OtherStatus(window).Contains("Accounting groups not expanded", StringComparison.Ordinal)); Assert.True(IndexedSelectionCount(window, 0, 0));
            var category = list.Items.OfType<AnalysisDatasetGroup>().First(); list.SelectedItem = category;
            Click(window, "Mark current members"); int members = tree.CaptureMembers(category.Definition.Key).Length;
            await Until(() => IndexedSelectionCount(window, members, 0)); Click(window, "Clear marks");
            Click(window, "Mark matching files in this scope"); int matching = tree.CaptureMatchingFiles(scope, token).NodeIds.Length;
            await Until(() => IndexedSelectionCount(window, matching, 0)); Assert.True(matching >= members);
            Click(window, "Explore selected group"); await Until(() => GroupParentReady(window) && list.Items.OfType<AnalysisDatasetGroupRow>().Any(), () => OtherViewDiagnostic(window));
            Assert.Equal(category.Members, (ulong)list.Items.OfType<AnalysisDatasetGroupRow>().Count());
            Click(window, "Back"); await Until(() => GroupOtherReady(window), () => OtherViewDiagnostic(window)); Assert.True(expected.SetEquals(list.Items.OfType<AnalysisDatasetGroup>().Select(group => group.Definition.Key)));
            Click(window, "Save named analysis"); await Until(() => window.GetVisualDescendants().OfType<TextBox>().Any(control => control.IsEffectivelyVisible && AutomationProperties.GetName(control) == "Inventory filename"));
            IndexedPersistenceControl<TextBox>(window, "Inventory filename").Text = "overview-other.fcdsa"; Click(window, "Save new inventory");
            await Until(() => OtherStatus(window).StartsWith("Saved overview-other.fcdsa", StringComparison.Ordinal));
            AnalysisSavedEntry saved;
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            { using var catalog = await prepared.ReadCatalogAsync(token); saved = Assert.Single(catalog.Entries, entry => entry.Name == "overview-other.fcdsa"); Assert.Equal(scope, saved.View.Scope); }
            await window.AddSavedIndexedSessionAsync(profile.Services, saved.Name, saved, token); Frame(window); Assert.True(GroupOtherReady(window));
            Assert.True(expected.SetEquals(IndexedList(window).Items.OfType<AnalysisDatasetGroup>().Select(group => group.Definition.Key))); Assert.True(IndexedSelectionCount(window, 0, 0));
            Assert.Equal(source.Payload, File.ReadAllBytes(source.PayloadPath));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(source.PrivatePath)); Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }
}
