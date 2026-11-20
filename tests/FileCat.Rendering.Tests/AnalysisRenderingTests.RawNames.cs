using System.Collections.Immutable;
using Avalonia.Automation;
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
    [InlineData("utf8", "Treemap", false)]
    [InlineData("utf16le", "Treemap", false)]
    [InlineData("unix-bytes", "Treemap", false)]
    [InlineData("utf8", "Sunburst", false)]
    [InlineData("utf16le", "Sunburst", false)]
    [InlineData("unix-bytes", "Sunburst", false)]
    [InlineData("utf8", "Treemap", true)]
    [InlineData("utf16le", "Treemap", true)]
    [InlineData("unix-bytes", "Treemap", true)]
    [InlineData("utf8", "Sunburst", true)]
    [InlineData("utf16le", "Sunburst", true)]
    [InlineData("unix-bytes", "Sunburst", true)]
    public async Task RawNameSavedOtherNativePrivateWindowRetainsExactBytesInspectorAndKeyboardTip(string encoding, string mode, bool grouped)
    {
        OrdinaryPreparation(); using var styles = new ProductStyles(); using var profile = new PreparationFixture(); var token = TestContext.Current.CancellationToken;
        await using (var prepared = await AnalysisIndexedAcquisition.PrepareAsync(profile.Services, token, indexedStorage: false)) { }
        string path = profile.Services.Paths.AnalysisSnapshotsDirectory, absent = Path.Combine(profile.Root, "source-must-not-be-created");
        byte[] raw = encoding == "unix-bytes" ? [0xff, (byte)'.', (byte)'b', (byte)'i', (byte)'n'] : AnalysisRawNames.Encode(encoding, encoding == "utf16le" ? "unpaired-\uD800. " : "bidi\u202E\\u202E.bin");
        string name = AnalysisRawNames.Decode(encoding, raw), display = AnalysisNames.DisplayName(new AnalysisNode(6, "r", 1, name, AnalysisKind.File) { NameEncoding = encoding, RawName = raw.ToImmutableArray() }, 256);
        string? groupKey = null;
        var entry = await AnalysisIo.Run(profile.Services.Io, PathUtil.GetDeviceKey(path), cancellation =>
        {
            var snapshot = Fixture(); snapshot = snapshot with
            {
                Nodes = [snapshot.Nodes[0], snapshot.Nodes[3] with { ParentId = 1 }, snapshot.Nodes[4] with { ParentId = 1 },
                    snapshot.Nodes[5] with { ParentId = 1, Name = name, NameEncoding = encoding, RawName = raw.ToImmutableArray() }],
                Objects = snapshot.Objects.SetItem(2, snapshot.Objects[2] with { LogicalLength = AnalysisMeasurement.Known(16384) }),
                Roots = [snapshot.Roots[0] with { CanonicalLocator = absent, NameRules = encoding == "utf16le" ? AnalysisNameRules.Windows : AnalysisNameRules.Unix }]
            };
            var view = AnalysisSavedView.Default with { Metric = AnalysisMetric.FileSize, Chart = mode, Scope = AnalysisTree.OtherKey("n:1", 1), Sort = AnalysisTableSort.Name, SortDescending = false };
            if (grouped)
            {
                var tree = AnalysisTree.BuildGrouped(snapshot, AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize, cancellation: cancellation), AnalysisGroupingDimension.Extension, false, cancellation);
                groupKey = Assert.Single(tree.Groups.Values, group => group.Members.Contains(6UL)).Definition.Key;
                view = view with { Grouping = AnalysisGroupingDimension.Extension, Scope = AnalysisTree.OtherKey(AnalysisTree.RootKey, 1) };
            }
            using AnalysisPrivateDirectory directory = OperatingSystem.IsWindows() ? new WindowsAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path)) : new UnixAnalysisPrivateDirectory(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            using var store = new AnalysisPrivateInventoryStore(directory, leaveOpen: true); var catalog = new AnalysisSavedCatalog(store);
            catalog.SaveNew("raw-name.fcdsa", snapshot, Guid.NewGuid(), view, cancellation); using var entries = catalog.Read(cancellation); return Assert.Single(entries.Entries);
        }, token);
        byte[] before = File.ReadAllBytes(Path.Combine(path, entry.Name)); var window = new DiskSpaceWindow { Width = 1180, Height = 960 };
        try
        {
            await window.AddSavedIndexedSessionAsync(profile.Services, entry.Name, entry, token); window.Show(); Frame(window);
            var list = IndexedList(window);
            if (grouped)
            {
                Assert.True(GroupOtherReady(window)); list.SelectedItem = list.Items.OfType<AnalysisDatasetGroup>().Single(group => group.Definition.Key == groupKey);
                Click(window, "Explore selected group"); await Until(() => list.Items.OfType<AnalysisDatasetGroupRow>().Any());
                var row = Assert.Single(list.Items.OfType<AnalysisDatasetGroupRow>()); Assert.Equal(6UL, row.Member.Path.Id); Assert.Equal(raw, row.Member.Path.RawName); Assert.Equal(name, row.Member.Path.Name);
                list.SelectedItem = row;
            }
            else
            {
                Assert.True(ExactOtherReady(window)); Assert.Equal(new ulong[] { 5, 6 }, list.Items.OfType<AnalysisHierarchyRow>().Select(row => row.Path.Id).Order());
                var row = list.Items.OfType<AnalysisHierarchyRow>().Single(row => row.Path.Id == 6); Assert.Equal(raw, row.Path.RawName); Assert.Equal(name, row.Path.Name); list.SelectedItem = row;
            }
            await Until(() => IndexedDetails(window).Text?.Contains("Raw name bytes: " + Convert.ToHexString(raw), StringComparison.Ordinal) == true);
            Assert.Contains(display, IndexedDetails(window).Text);
            var copy = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Copy path");
            Assert.Equal(encoding == "utf8", copy.IsEnabled);
            if (encoding != "utf8") Assert.Contains("no lossless clipboard path", AutomationProperties.GetHelpText(copy));
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible && button.Content as string == "Bind current source…");
            var chart = window.GetVisualDescendants().OfType<AnalysisChartControl>().Single(control => control.IsEffectivelyVisible); Assert.True(chart.Focus()); Frame(window);
            var tip = Assert.IsType<RichTip>(ToolTip.GetTip(chart)); Assert.Contains(display, tip.Text);
            if (encoding != "utf8") Assert.Contains("Raw name (" + encoding + "): " + Convert.ToHexString(raw), tip.Text);
            Click(window, "Mark matching files in this scope"); await Until(() => IndexedSelectionCount(window, grouped ? 1 : 2, 0));
            Assert.Equal(before, File.ReadAllBytes(Path.Combine(path, entry.Name))); Assert.False(Directory.Exists(absent));
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        Assert.Empty(Directory.GetFiles(profile.Services.Paths.AnalysisScratchDirectory));
    }
}
