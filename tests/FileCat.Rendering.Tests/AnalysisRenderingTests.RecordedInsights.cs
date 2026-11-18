using System.Buffers.Binary;
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
    public async Task NativeDetailedExtraAllocationKeepsPrimaryAndNamedDataSeparate(bool indexed)
    {
        OrdinaryPreparation();
        using var styles = new ProductStyles();
        using var fixture = new PreparationFixture();
        var token = TestContext.Current.CancellationToken;
        string root = Directory.CreateDirectory(Path.Combine(fixture.Root, "extra-allocation")).FullName;
        string high = Path.Combine(root, "large-extra.bin"), low = Path.Combine(root, "small-extra.bin");
        string alias = Path.Combine(root, "alias-extra.bin"), empty = Path.Combine(root, "no-extra.bin");
        byte[] primary = Enumerable.Repeat((byte)2, 24 * 1024 + 1).ToArray();
        byte[] stream = Enumerable.Repeat((byte)3, 74 * 1024 + 5).ToArray();
        foreach (string path in new[] { high, low, empty }) File.WriteAllBytes(path, primary);
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(high + ":extra:$DATA", stream);
            File.WriteAllBytes(high + ":second:$DATA", primary);
            File.WriteAllBytes(low + ":small:$DATA", primary);
        }
        else if (OperatingSystem.IsMacOS())
        {
            File.WriteAllBytes(high + "/..namedfork/rsrc", stream);
            File.WriteAllBytes(low + "/..namedfork/rsrc", primary);
        }
        CreateGroupedNativeAlias(high, alias);

        var pool = new AnalysisSessionAdmission();
        var window = new DiskSpaceWindow(null, null, pool);
        try
        {
            window.Show();
            await window.AddOwnedAcquisitionAsync(fixture.Services, async cancel =>
            {
                var owner = await AnalysisIndexedAcquisition.PrepareAsync(fixture.Services, cancel, indexedStorage: indexed);
                IAnalysisBackend? backend = null;
                try
                {
                    backend = await AnalysisIo.Run(fixture.Services.Io, PathUtil.GetDeviceKey(root), _ => OperatingSystem.IsWindows()
                        ? (IAnalysisBackend)new WindowsAnalysisBackend(root) : new UnixAnalysisBackend(root), cancel);
                    var session = owner.Start([backend], components: AnalysisComponentPreset.DetailedAllocation);
                    backend = null;
                    await session.Completion;
                    if (indexed)
                    {
                        await using var dataset = await session.AcquireDatasetAsync(cancel) ?? throw new InvalidOperationException("Expected an indexed capture.");
                        var facts = (await dataset.ReadFactsAsync(0, cancellation: cancel)).Rows;
                        await using var hierarchy = await dataset.BuildHierarchyAsync(AnalysisMetric.OnDisk, cancel);
                        VerifyFacts(facts, hierarchy.Totals.Known);
                        Assert.Equal((UInt128)primary.Length * 4, hierarchy.LogicalTotals.KnownBytes);
                    }
                    else
                    {
                        var snapshot = session.Snapshot();
                        var allocation = AnalysisAggregate.Project(snapshot, AnalysisMetric.OnDisk, cancellation: cancel);
                        VerifyFacts(snapshot.Facts, allocation.Totals.Known);
                        Assert.Equal((UInt128)primary.Length * 4, AnalysisAggregate.Project(snapshot, AnalysisMetric.FileSize, cancellation: cancel).Totals.Known);
                    }
                    return owner;
                }
                catch { await owner.DisposeAsync(); throw; }
                finally { if (backend is not null) await AnalysisIo.ReleaseAsync(fixture.Services.Io, backend); }
            });
            Frame(window);
            Click(window, "Candidate recipes…");
            await Until(() => ComparisonControl<ListBox>(window, "Locations for candidate preview") is not null);
            ComparisonControl<ComboBox>(window, "Candidate recipe")!.SelectedItem = "Large extra data";
            ComparisonControl<ListBox>(window, "Locations for candidate preview")!.SelectedIndex = 0;
            Click(window, "View candidates");
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                await Until(() => ComparisonControl<ListBox>(window, "Captured recipe candidates")?.ItemCount == 3, () => ComparisonText(window));
                var list = ComparisonControl<ListBox>(window, "Captured recipe candidates")!;
                var rows = list.Items.Cast<string>().ToArray();
                Assert.Equal(new[] { "alias-extra.bin", "large-extra.bin" }, rows.Take(2).Select(row => row[..row.IndexOf('\n')]).Order());
                Assert.StartsWith("small-extra.bin\n", rows[2], StringComparison.Ordinal);
                if (OperatingSystem.IsWindows())
                {
                    Assert.All(rows, row => Assert.Contains("Reported NTFS named data stream allocation", row, StringComparison.Ordinal));
                    Assert.DoesNotContain(rows, row => row.Contains(":$DATA", StringComparison.Ordinal));
                    Assert.Equal(stream, File.ReadAllBytes(high + ":extra:$DATA"));
                    Assert.Equal(primary, File.ReadAllBytes(high + ":second:$DATA"));
                }
                else
                {
                    Assert.All(rows, row => Assert.Contains("Reported APFS resource fork allocation. Included in the reported file total.", row, StringComparison.Ordinal));
                    Assert.Equal(stream, File.ReadAllBytes(high + "/..namedfork/rsrc"));
                    Assert.Equal(primary, File.ReadAllBytes(low + "/..namedfork/rsrc"));
                }
            }
            else
            {
                await Until(() => ComparisonText(window).Contains("Extra-data size is unavailable", StringComparison.Ordinal), () => ComparisonText(window));
                Assert.Null(ComparisonControl<ListBox>(window, "Captured recipe candidates"));
            }
            Assert.Equal(primary, File.ReadAllBytes(high));
            Click(window, "Close preview");
        }
        finally { window.ForceClose(); await window.WaitForIndexedCloseAsync(); }
        await AssertAllRuntimePlacesAvailableAsync(pool);

        void VerifyFacts(IEnumerable<StorageFact> facts, UInt128 total)
        {
            token.ThrowIfCancellationRequested();
            var captured = facts.ToArray();
            var named = captured.Where(AnalysisComponents.IsExtraAllocation).OrderBy(fact => fact.Measurement.Value).ToArray();
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            {
                Assert.Equal(3, named.Length);
                Assert.Equal(UInt128.Zero, named[0].Measurement.Value);
                Assert.True(named[1].Measurement.Value > (UInt128)primary.Length);
                Assert.True(named[2].Measurement.Value > (UInt128)(OperatingSystem.IsMacOS() ? stream.Length : primary.Length + stream.Length));
                if (OperatingSystem.IsMacOS())
                    foreach (var fork in named)
                    {
                        Assert.Equal(AnalysisComponents.ResourceForkBasis, fork.BasisId);
                        var main = Assert.Single(captured, fact => fact.ObjectId == fork.ObjectId && fact.ComponentId == "reported-data");
                        Assert.Equal(new[] { AnalysisComponents.ResourceForkComponent }, main.Includes);
                        Assert.True(main.Measurement.Value > fork.Measurement.Value);
                    }
            }
            else Assert.Empty(named);
            Assert.Equal(captured.Where(fact => fact.ComponentId != AnalysisComponents.ResourceForkComponent)
                .Aggregate(UInt128.Zero, (sum, fact) => sum + fact.Measurement.Value.GetValueOrDefault()), total);
        }
    }

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
            Assert.Contains(OperatingSystem.IsMacOS() ? "reported total-file allocation" :
                OperatingSystem.IsWindows() ? "reported primary allocation" : "reported file allocation", difference);
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
    public void RecordedInsightOrdersBothDirectionsAndUsesTheReportedAllocationBasis()
    {
        var identity = DuplicateEstimateIdentity();
        var preview = new AnalysisRecipePreviews(new(AnalysisMetadataRecipe.AllocationDifference, ["r"]), [DuplicateEstimateRoot], identity, AnalysisMetric.Files);
        var file = new AnalysisNode(1, "r", 0, "file.bin", AnalysisKind.File, "o");
        var obj = new AnalysisObject("o", "id", AnalysisMeasurement.Known(100), AnalysisMeasurement.Known(1));
        var primary = new StorageFact("p", "o", null, "reported-data", "basis", AnalysisMeasurement.Known(30), "method", DateTimeOffset.UnixEpoch, []);
        var extra = primary with { Id = "e", ComponentId = "extra-stream", Measurement = AnalysisMeasurement.Known(1_000) };
        Assert.Equal(primary, AnalysisRecipePreviews.ReportedFileAllocation([primary, extra]));
        Assert.Null(AnalysisRecipePreviews.ReportedFileAllocation([primary with { Includes = ["extra-stream"] }, extra]));
        Assert.Null(AnalysisRecipePreviews.ReportedFileAllocation([primary, primary with { BasisId = "another" }]));
        var total = primary with { BasisId = AnalysisComponents.DarwinTotalAllocationBasis, Includes = [AnalysisComponents.ResourceForkComponent] };
        Assert.Equal(total, AnalysisRecipePreviews.ReportedFileAllocation([total]));
        preview.AddDifference(file, obj, total);
        preview.AddDifference(file with { Id = 2, Name = "other.bin" }, obj, primary with { Measurement = AnalysisMeasurement.Known(220) });
        preview.AddDifference(file with { Id = 3, Name = "unknown.bin" }, obj, primary with { Measurement = AnalysisMeasurement.Missing(MeasurementState.Failed) });
        preview.AddDifference(file with { Id = 4, Name = "offline.bin", Cloud = true }, obj, primary);
        var result = preview.Finish(identity, NamespaceCoverage.Partial);
        Assert.Equal(2UL, result.Candidates);
        Assert.Equal(1UL, result.Unknown);
        Assert.Equal(1UL, result.Excluded);
        Assert.Equal([2UL, 1UL], result.Rows.Select(row => row.PathId));
        Assert.Equal((UInt128)120, result.Rows[0].RecordedBytes.Value);
        Assert.Contains("reported allocation is larger", result.Rows[0].Finding.Evidence);
        Assert.Contains("logical size is larger", result.Rows[1].Finding.Evidence);
        Assert.Contains("reported total-file allocation", result.Rows[1].Finding.Evidence);
    }

    [Fact]
    public void ResourceForkAllocationRequiresReturnedAndConsistentFields()
    {
        var response = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(response, 56);
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(4), 0x80000000);
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(16), 0x3404);
        BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(24), 8192);
        BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(32), 4096);
        BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(40), 17);
        BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(48), 4096);
        Assert.Equal(AnalysisMeasurement.Known(4096), UnixAnalysisBackend.ParseResourceAllocation(response, 8192));
        Assert.Equal(MeasurementState.Unsupported, UnixAnalysisBackend.ParseResourceAllocation(response, 12288).State);
        Assert.Equal(MeasurementState.Failed, UnixAnalysisBackend.ParseResourceAllocation(response.AsSpan(0, 40), 8192).State);
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(16), 0x1404);
        Assert.Equal(MeasurementState.Unsupported, UnixAnalysisBackend.ParseResourceAllocation(response, 8192).State);
        BinaryPrimitives.WriteUInt32LittleEndian(response.AsSpan(16), 0x3404);
        BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(48), -1);
        Assert.Equal(MeasurementState.Failed, UnixAnalysisBackend.ParseResourceAllocation(response, 8192).State);
        BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(48), 0);
        BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(40), 0);
        BinaryPrimitives.WriteInt64LittleEndian(response.AsSpan(24), 4096);
        Assert.Equal(AnalysisMeasurement.Known(0), UnixAnalysisBackend.ParseResourceAllocation(response, 4096));
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
