using System.Collections.Immutable;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Controls;
using FileCat.App.Controls.DiskSpace;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(AnalysisMetric.FileSize, false)]
    [InlineData(AnalysisMetric.UniqueFileSize, false)]
    [InlineData(AnalysisMetric.OnDisk, false)]
    [InlineData(AnalysisMetric.Files, false)]
    [InlineData(AnalysisMetric.FileSize, true)]
    [InlineData(AnalysisMetric.UniqueFileSize, true)]
    [InlineData(AnalysisMetric.OnDisk, true)]
    [InlineData(AnalysisMetric.Files, true)]
    public async Task NativeNameSourceCapturesExactEntriesIdentitiesMetricAndGroupedOtherWithoutReplacementBinding(AnalysisMetric metric, bool indexed)
    {
        OrdinaryPreparation(); using var profile = new PreparationFixture(); await using var source = await NativeNamesFixture.CreateAsync(indexed); var token = TestContext.Current.CancellationToken;
        AnalysisSnapshot snapshot;
        if (indexed)
        {
            await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
                await prepared.SaveInventoryAsync(source.Lease!, "native-names.fcdsa", AnalysisSavedView.Default, token);
            using var saved = await ReadOtherReference(profile, "native-names.fcdsa"); snapshot = saved.Snapshot;
        }
        else snapshot = source.Memory!;
        ValidateNativeNames(source, snapshot, token);
        var projection = AnalysisAggregate.Project(snapshot, metric, cancellation: token); var tree = AnalysisTree.Build(snapshot, projection, false, token);
        ulong root = snapshot.Nodes.Single(node => node.ParentId == 0).Id;
        UInt128? expected = metric switch { AnalysisMetric.FileSize => (uint)source.Files.Count * 4U, AnalysisMetric.UniqueFileSize => (uint)(source.Files.Count - 1) * 4U, AnalysisMetric.Files => (uint)source.Files.Count, _ => null };
        if (expected.HasValue) Assert.Equal(expected.Value, projection.Totals.Known);
        if (indexed)
        {
            await using var hierarchy = await source.Lease!.BuildHierarchyAsync(metric, token);
            Assert.Equal(projection.Totals.Known, hierarchy.Totals.Known); Assert.Equal(projection.Totals.IdentityUnverified, hierarchy.Totals.IdentityUnverified);
            var geometry = await hierarchy.ReadGeometryAsync(root, cancellation: token); Assert.Equal(tree.ChildrenOf(AnalysisTree.Key(root)).Select(item => item.Key), geometry.Rows.Select(item => item.Key));
            await using var subset = await hierarchy.CreateSubsetAsync(root, 1, token); string scope = AnalysisTree.OtherKey(AnalysisTree.Key(root), 1);
            Assert.Equal(tree.CaptureMatchingFiles(scope, token).NodeIds, (await subset.CaptureMatchingFilesAsync(cancellation: token)).NodeIds);
            foreach (var dimension in Enum.GetValues<AnalysisGroupingDimension>().Where(value => value != AnalysisGroupingDimension.FolderHierarchy))
            {
                await using var groups = await source.Lease.BuildGroupingAsync(metric, dimension, cancellation: token);
                var grouped = AnalysisTree.BuildGrouped(snapshot, projection, dimension, false, token);
                Assert.Equal(grouped.ChildrenOf(AnalysisTree.RootKey).Select(item => item.Key), (await groups.ReadGeometryAsync(cancellation: token)).Rows.Select(item => item.Key));
                foreach (var group in grouped.Groups.Values)
                {
                    var members = await groups.ReadMemberRowsAsync(group.Definition.Key, cancellation: token);
                    Assert.Equal(group.Members.Order(), members.Rows.Select(row => row.Member.Path.Id).Order());
                    foreach (var row in members.Rows)
                    {
                        var captured = snapshot.Nodes.Single(node => node.Id == row.Member.Path.Id); Assert.Equal(captured.RawName, row.Member.Path.RawName);
                    }
                }
            }
            int lossy = snapshot.Nodes.Count(node => node.Kind == AnalysisKind.File && !AnalysisRawNames.HasLosslessText(node));
            await using var uncertain = await source.Lease.BuildHierarchyAsync(metric, new("name:odd_", UnknownOnly: true), token);
            Assert.Equal((ulong)lossy, uncertain.Totals.MatchingPaths);
        }
        source.AssertPayloads();
    }

    [AvaloniaTheory]
    [InlineData("Treemap", false)]
    [InlineData("Sunburst", false)]
    [InlineData("Treemap", true)]
    [InlineData("Sunburst", true)]
    public async Task NativeNameSourceSavedOtherKeepsRealRawEntriesThroughInspectionFocusAndMarking(string mode, bool grouped)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture();
        // A filesystem may reject the invalid byte names. Keep a display-sensitive name after dropping the first entry.
        await using var source = await NativeNamesFixture.CreateAsync(indexed: true, includeSecondBidiName: true);
        var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await prepared.SaveInventoryAsync(source.Lease!, "native-reference.fcdsa", AnalysisSavedView.Default, token);
        using var reference = await ReadOtherReference(profile, "native-reference.fcdsa"); var snapshot = reference.Snapshot; ValidateNativeNames(source, snapshot, token);
        var projection = AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize, cancellation: token);
        var tree = grouped ? AnalysisTree.BuildGrouped(snapshot, projection, AnalysisGroupingDimension.Extension, false, token) : AnalysisTree.Build(snapshot, projection, false, token);
        IEnumerable<string> parents = grouped
            ? tree.Groups.Values.Where(group => group.Kind == AnalysisGroupKind.Entries).Select(group => group.Definition.Key).Order(StringComparer.Ordinal)
            : [AnalysisTree.Key(snapshot.Nodes.Single(node => node.ParentId == 0).Id)];
        // Lossy names belong to the unknown-extension group, whose membership can be smaller than the .bin group.
        var candidates = parents.Select(parent => AnalysisTree.OtherKey(parent, 1))
            .SelectMany(candidateScope => tree.ChildrenOf(candidateScope).Where(item => item.CapturedNode is not null)
                .Select(item => (Scope: candidateScope, Item: item)));
        var choice = candidates.OrderByDescending(row => row.Item.NameEvidence is not null)
            .FirstOrDefault(row => row.Item.NameEvidence is not null || row.Item.CapturedNode!.Name.Contains('\u202E'));
        Assert.NotNull(choice.Item);
        string scope = choice.Scope;
        var target = choice.Item;
        var selected = tree.ChildrenOf(scope).Where(item => item.NodeId.HasValue).ToArray();
        var targetNode = target.CapturedNode!;
        var view = AnalysisSavedView.Default with { Metric = AnalysisMetric.FileSize, Chart = mode, Scope = scope, Grouping = grouped ? AnalysisGroupingDimension.Extension : AnalysisGroupingDimension.FolderHierarchy };
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false))
            await prepared.SaveInventoryAsync(source.Lease!, "native-other.fcdsa", view, token);
        await source.ReleaseLeaseAsync(); Assert.Empty(Directory.EnumerateFileSystemEntries(source.PrivatePath));
        string path = profile.Services.Paths.AnalysisSnapshotsDirectory;
        var entry = await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), cancellation =>
        {
            using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path)) : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); using var entries = new AnalysisSavedCatalog(store).Read(cancellation); return entries.Entries.Single(row => row.Name == "native-other.fcdsa");
        }, token);
        byte[] before = File.ReadAllBytes(Path.Combine(path, entry.Name)); var window = new DiskSpaceWindow { Width = 1180, Height = 960 };
        try
        {
            await window.AddSavedIndexedSessionAsync(profile.Services, entry.Name, entry, token); window.Show(); Frame(window); var list = IndexedList(window);
            if (grouped)
            {
                Assert.True(GroupOtherReady(window)); var rows = list.Items.OfType<AnalysisDatasetGroupRow>().ToArray();
                Assert.Equal(selected.Select(item => item.NodeId!.Value).Order(), rows.Select(row => row.Member.Path.Id).Order()); list.SelectedItem = rows.Single(row => row.Member.Path.Id == targetNode.Id);
            }
            else
            {
                Assert.True(ExactOtherReady(window)); var rows = list.Items.OfType<AnalysisHierarchyRow>().ToArray();
                Assert.Equal(selected.Select(item => item.NodeId!.Value).Order(), rows.Select(row => row.Path.Id).Order()); list.SelectedItem = rows.Single(row => row.Path.Id == targetNode.Id);
            }
            await Until(() => IndexedDetails(window).Text?.Contains("Raw name bytes: " + Convert.ToHexString(targetNode.RawName.AsSpan()), StringComparison.Ordinal) == true);
            var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(control => control.IsEffectivelyVisible); Assert.True(chart.Focus()); Frame(window);
            var tip = Assert.IsType<RichTip>(ToolTip.GetTip(chart)); Assert.Contains(AnalysisNames.DisplayName(targetNode, 256), tip.Text);
            if (target.NameEvidence is { } evidence) Assert.Contains(evidence, tip.Text);
            Click(window, "Mark matching files in this scope"); int count = tree.CaptureMatchingFiles(scope, token).NodeIds.Length; await Until(() => IndexedSelectionCount(window, count, 0));
            source.AssertPayloads(); Assert.Equal(before, File.ReadAllBytes(Path.Combine(path, entry.Name)));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }

    private static void ValidateNativeNames(NativeNamesFixture source, AnalysisSnapshot snapshot, CancellationToken token)
    {
        AnalysisValidation.Validate(snapshot, token); Assert.Equal(NamespaceCoverage.CompleteUnderPolicy, snapshot.Coverage);
        var files = snapshot.Nodes.Where(node => node.Kind == AnalysisKind.File).ToArray(); Assert.Equal(source.Files.Count, files.Length);
        foreach (var record in source.Files)
        {
            var node = Assert.Single(files, node => node.RawName.AsSpan().SequenceEqual(record.Name)); Assert.Equal(source.EncodingName, node.NameEncoding); Assert.Equal(MeasurementState.Known, node.MetadataState);
            var obj = Assert.Single(snapshot.Objects, obj => obj.Id == node.ObjectId); Assert.Equal(AnalysisMeasurement.Known((uint)record.Payload.Length), obj.LogicalLength); Assert.NotNull(obj.VerifiedIdentity);
        }
        var aliases = files.Where(node => node.Name is "plain.bin" or "plain-alias.bin").ToArray(); Assert.Equal(2, aliases.Length); Assert.Single(aliases.Select(node => node.ObjectId).Distinct());
        if (!OperatingSystem.IsWindows())
        {
            var odd = files.Where(node => node.Name == "odd_\uFFFD.bin").ToArray(); int expected = source.Files.Count(record => AnalysisRawNames.Decode(source.EncodingName, record.Name) == "odd_\uFFFD.bin");
            Assert.Equal(expected, odd.Length); Assert.Equal(expected, odd.Select(node => node.ObjectId).Distinct().Count());
        }
    }
}
