using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.Analysis;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Analysis;

namespace FileCat.Rendering.Tests;

public sealed partial class AnalysisRenderingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRecordedInsightsUseBoundedCapturedAllocationCountsAndIssues(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        string root = Path.Combine(fixture.Root, "insight-root"), folder = Path.Combine(root, "small-files"),
            nested = Path.Combine(folder, "nested");
        Directory.CreateDirectory(nested);
        Directory.CreateDirectory(Path.Combine(root, "empty-folder"));
        string heavyFolder = Directory.CreateDirectory(Path.Combine(root, "bytes-heavy")).FullName;
        string unmeasuredFolder = Directory.CreateDirectory(Path.Combine(root, "unmeasured-only")).FullName;
        File.WriteAllText(Path.Combine(unmeasuredFolder, "no-known-length.bin"), "unchanged observation fixture");
        for (int i = 0; i < 33; i++) File.WriteAllBytes(Path.Combine(folder, $"small-{i:D2}.txt"), [1, 2, 3, 4]);
        CreateGroupedNativeAlias(Path.Combine(folder, "small-00.txt"), Path.Combine(folder, "small-alias.txt"));
        File.WriteAllText(Path.Combine(nested, "nested-small.txt"), "nested");
        string large = Path.Combine(heavyFolder, "allocation-shape.bin");
        byte[] payload = new byte[128 * 1024 + 5];
        Array.Fill(payload, (byte)9);
        File.WriteAllBytes(large, payload);
        string issue = Path.Combine(folder, "unreadable.bin");
        File.WriteAllText(issue, "unchanged observation fixture");

        var pool = new AnalysisSessionAdmission();
        var window = new DiskSpaceWindow(null, null, pool);
        try
        {
            window.Show();
            var opened = await window.AddOwnedAcquisitionAsync(fixture.Services, async token =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, token, indexedStorage: indexed);
                IAnalysisBackend? source = null;
                try
                {
                    source = await AnalysisIo.Run(fixture.Services.Io, PathUtil.GetDeviceKey(root), _ => OperatingSystem.IsWindows()
                        ? (IAnalysisBackend)new WindowsAnalysisBackend(root, null, null, Observe) : new UnixAnalysisBackend(root, null, null, Observe), token);
                    var session = owner.Start([source]);
                    source = null;
                    await session.Completion;
                    return owner;
                }
                catch { await owner.DisposeAsync(); throw; }
                finally { if (source is not null) await AnalysisIo.ReleaseAsync(fixture.Services.Io, source); }
            });
            Frame(window);

            await SelectInsightAsync("Folders with many small files");
            await Until(() => ComparisonControl<ListBox>(window, "Captured recipe candidates")?.ItemCount == 1,
                () => ComparisonText(window), TimeSpan.FromSeconds(20));
            var small = ComparisonControl<ListBox>(window, "Captured recipe candidates")!;
            var observed = Assert.IsType<string>(small.Items[0]);
            Assert.Contains("small-files", observed);
            Assert.Contains("34 small file paths", observed);
            Assert.Contains("1 direct file path with unknown size", observed);
            Assert.Contains("32 verified single-link files", observed);
            Assert.Contains("known single-link allocation", observed);
            small.SelectedIndex = 0;
            Click(window, "Inspect recorded candidate");
            await Until(() => ComparisonControl<TextBlock>(window, "Recorded candidate evidence")?.Text?.Contains("insight-root/small-files", StringComparison.Ordinal) == true,
                () => ComparisonText(window));
            Click(window, "Close preview");

            var metric = ComparisonControl<ComboBox>(window, indexed ? "Indexed metric" : "Analysis metric")!;
            await ChangeMetricAsync(AnalysisMetric.FileSize, "File size:");
            await SelectInsightAsync("Largest folders");
            await Until(() => ComparisonControl<ListBox>(window, "Captured recipe candidates")?.ItemCount == 5,
                () => ComparisonText(window), TimeSpan.FromSeconds(20));
            var folders = ComparisonControl<ListBox>(window, "Captured recipe candidates")!;
            var sizes = folders.Items.Cast<string>().ToArray();
            Assert.Equal(new[] { "insight-root", "bytes-heavy", "small-files", "nested", "empty-folder" }, sizes.Select(Name));
            Assert.Contains("Some entries are unmeasured", sizes[2]);
            Assert.Contains("0 B", sizes[4]);
            Assert.DoesNotContain(root, sizes[1]);
            folders.SelectedIndex = 1;
            Click(window, "Inspect recorded candidate");
            await Until(() => ComparisonControl<TextBlock>(window, "Recorded candidate evidence")?.Text?.Contains("insight-root/bytes-heavy", StringComparison.Ordinal) == true,
                () => ComparisonText(window));
            Assert.True(window.GetVisualDescendants().OfType<Button>().Single(button => button.IsEffectivelyVisible && Equals(button.Content, "Close preview")).IsDefault);
            Click(window, "Close preview");

            await ChangeMetricAsync(AnalysisMetric.Files, "Files:");
            await SelectInsightAsync("Largest folders");
            await Until(() => ComparisonControl<ListBox>(window, "Captured recipe candidates") is not null &&
                ComparisonText(window).Contains("by recorded file count", StringComparison.Ordinal), () => ComparisonText(window), TimeSpan.FromSeconds(20));
            var counts = ComparisonControl<ListBox>(window, "Captured recipe candidates")!.Items.Cast<string>().ToArray();
            Assert.Equal("insight-root", Name(counts[0]));
            Assert.Equal("small-files", Name(counts[1]));
            Assert.Contains("1 file path", Assert.Single(counts, item => Name(item) == "bytes-heavy"));
            Assert.Contains("0 file paths", Assert.Single(counts, item => Name(item) == "empty-folder"));
            Click(window, "Close preview");

            await SelectInsightAsync("Logical and allocated size differences");
            await Until(() => ComparisonControl<ListBox>(window, "Captured recipe candidates")?.Items.Cast<string>()
                .Any(row => row.Contains("allocation-shape.bin", StringComparison.Ordinal)) == true,
                () => ComparisonText(window), TimeSpan.FromSeconds(20));
            var difference = Assert.Single(ComparisonControl<ListBox>(window, "Captured recipe candidates")!.Items.Cast<string>(),
                row => row.Contains("allocation-shape.bin", StringComparison.Ordinal));
            Assert.Contains("reported primary allocation", difference);
            Assert.Contains("difference", difference);
            Assert.Contains("ranked by absolute difference", ComparisonText(window));
            Click(window, "Close preview");

            await SelectInsightAsync("Unmeasured and unreadable locations");
            await Until(() => ComparisonControl<ListBox>(window, "Captured recipe candidates")?.Items.Cast<string>()
                .Any(row => row.Contains("insight-fixture-unreadable", StringComparison.Ordinal)) == true,
                () => ComparisonText(window), TimeSpan.FromSeconds(20));
            var issues = ComparisonControl<ListBox>(window, "Captured recipe candidates")!;
            var row = Assert.Single(issues.Items.Cast<string>(), item => item.Contains("insight-fixture-unreadable", StringComparison.Ordinal));
            Assert.Contains("Failed observation", row);
            Assert.DoesNotContain("0 B", row);
            issues.SelectedItem = row;
            Click(window, "Inspect recorded candidate");
            await Until(() => ComparisonControl<TextBlock>(window, "Recorded candidate evidence")?.Text?.Contains("small-files/unreadable.bin", StringComparison.Ordinal) == true,
                () => ComparisonText(window));
            Click(window, "Close preview");

            await SelectInsightAsync("Large extra data");
            await Until(() => ComparisonText(window).Contains("Extra-data size is unavailable", StringComparison.Ordinal), () => ComparisonText(window));
            Assert.Null(ComparisonControl<ListBox>(window, "Captured recipe candidates"));
            Click(window, "Close preview");
            Assert.Equal(1, window.SessionCount);
            if (indexed) Assert.True(IndexedSelectionCount(window, 0, 0));
            else
            {
                Assert.Empty(opened.Memory!.Selection.Marks);
                Assert.Empty(opened.Memory.Selection.Basket);
            }
            Assert.Equal(payload, File.ReadAllBytes(large));
            Assert.Equal("unchanged observation fixture", File.ReadAllText(issue));

            async Task SelectInsightAsync(string title)
            {
                Click(window, "Candidate recipes…");
                await Until(() => ComparisonControl<ListBox>(window, "Locations for candidate preview") is not null,
                    () => ComparisonText(window));
                ComparisonControl<ComboBox>(window, "Candidate recipe")!.SelectedItem = title;
                ComparisonControl<ListBox>(window, "Locations for candidate preview")!.SelectedIndex = 0;
                Click(window, "View candidates");
            }

            async Task ChangeMetricAsync(AnalysisMetric measure, string heading)
            {
                metric.SelectedIndex = (int)measure;
                await Until(() => indexed
                    ? window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.IsEffectivelyVisible &&
                        text.Text?.StartsWith(heading, StringComparison.Ordinal) == true)
                    : opened.Memory!.Tree?.Metric == measure, () => ComparisonText(window), TimeSpan.FromSeconds(20));
            }
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);

        static string Name(string row) => row[..row.IndexOf('\n')];
        static AnalysisEntry Observe(AnalysisEntry entry) => entry.Name switch
        {
            "unreadable.bin" => AnalysisEntry.Unavailable(entry.RelativeName, entry.RawName, entry.NameEncoding, entry.Kind, MeasurementState.Failed, "insight-fixture-unreadable"),
            "no-known-length.bin" => AnalysisEntry.Unavailable(entry.RelativeName, entry.RawName, entry.NameEncoding, entry.Kind, MeasurementState.Failed, "insight-fixture-no-length"),
            _ => entry
        };
    }

    [Fact]
    public void RecordedInsightOrdersBothDirectionsAndUsesOnlySeparatePrimaryAllocation()
    {
        var identity = DuplicateEstimateIdentity();
        var preview = new AnalysisRecipePreviews(new(AnalysisMetadataRecipe.AllocationDifference, ["r"]), [DuplicateEstimateRoot], identity, AnalysisMetric.Files);
        var file = new AnalysisNode(1, "r", 0, "file.bin", AnalysisKind.File, "o");
        var obj = new AnalysisObject("o", "id", AnalysisMeasurement.Known(100), AnalysisMeasurement.Known(1));
        var primary = new StorageFact("p", "o", null, "reported-data", "basis", AnalysisMeasurement.Known(30), "method", DateTimeOffset.UnixEpoch, []);
        var extra = primary with { Id = "e", ComponentId = "extra-stream", Measurement = AnalysisMeasurement.Known(1_000) };
        Assert.Equal(primary, AnalysisRecipePreviews.PrimaryAllocation([primary, extra]));
        Assert.Null(AnalysisRecipePreviews.PrimaryAllocation([primary with { Includes = ["extra-stream"] }, extra]));
        Assert.Null(AnalysisRecipePreviews.PrimaryAllocation([primary, primary with { BasisId = "another" }]));
        preview.Add(file, obj, primary.Measurement, _ => null, (_, _) => null);
        preview.Add(file with { Id = 2, Name = "other.bin" }, obj, AnalysisMeasurement.Known(220), _ => null, (_, _) => null);
        preview.Add(file with { Id = 3, Name = "unknown.bin" }, obj, AnalysisMeasurement.Missing(MeasurementState.Failed), _ => null, (_, _) => null);
        preview.Add(file with { Id = 4, Name = "offline.bin", Cloud = true }, obj, primary.Measurement, _ => null, (_, _) => null);
        var result = preview.Finish(identity, NamespaceCoverage.Partial);
        Assert.Equal(2UL, result.Candidates);
        Assert.Equal(1UL, result.Unknown);
        Assert.Equal(1UL, result.Excluded);
        Assert.Equal([2UL, 1UL], result.Rows.Select(row => row.PathId));
        Assert.Equal((UInt128)120, result.Rows[0].RecordedBytes.Value);
        Assert.Contains("reported allocation is larger", result.Rows[0].Finding.Evidence);
        Assert.Contains("logical size is larger", result.Rows[1].Finding.Evidence);
    }

    [Fact]
    public void RecordedInsightKeepsUnlocatedAndUnsampledIssuesOutsideScopedCounts()
    {
        var identity = DuplicateEstimateIdentity();
        var preview = new AnalysisRecipePreviews(new(AnalysisMetadataRecipe.UnmeasuredLocations, ["r"]),
            [DuplicateEstimateRoot, DuplicateEstimateRoot with { Id = "other" }], identity, AnalysisMetric.OnDisk);
        var entry = new AnalysisNode(3, "r", 0, "issue-file.bin", AnalysisKind.File);
        preview.AddIssue(new("unverified-allocation", 3, MeasurementState.Unsupported), entry, 1);
        preview.AddIssue(new("enumeration-failed", State: MeasurementState.Failed) { RootId = "r" }, null, 2);
        preview.AddIssue(new("unlocated", State: MeasurementState.Changed), null, 3);
        preview.AddIssue(new("elsewhere", 9) { RootId = "other" }, entry with { Id = 9, RootId = "other" }, 4);
        preview.AddIssue(new("policy", 3, MeasurementState.Excluded), entry, 5);
        preview.CompleteIssues(5, 18);
        var result = preview.Finish(identity, NamespaceCoverage.Partial);
        Assert.Equal(2UL, result.Candidates);
        Assert.Equal([0UL, 3UL], result.Rows.Select(row => row.PathId));
        Assert.All(result.Rows, row => Assert.Null(row.RecordedBytes.Value));
        Assert.Contains("retains a sample", result.CoverageNote);
        Assert.Contains("1 captured issue record has no verified location", result.CoverageNote);
    }
}
