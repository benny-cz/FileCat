using System.Collections.Concurrent;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Records;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.App.Tests;

public sealed class FileDetailsColumnTests
{
    private sealed class Records(Action<string>? onRead = null) : IFileRecords
    {
        public bool IsSupported => true;
        public int Calls;
        public ConcurrentQueue<string> Paths { get; } = new();
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy reader invoked.");
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) => throw new InvalidOperationException("Full reader invoked.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            Assert.True(FileObservationRequests.IsAdmittedFor(PathUtil.GetDeviceKey(path)), "The read must be owned by the original filesystem device.");
            Interlocked.Increment(ref Calls);
            Paths.Enqueue(path);
            onRead?.Invoke(path);
            if (Path.GetFileName(path) == "gone.txt") throw new FileNotFoundException("Fixture original was removed.");
            return new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
            {
                Kind = Path.GetFileName(path) == "sub" ? FileObjectKind.Directory : FileObjectKind.File,
                Allocated = ObservedFact<long>.Known(Path.GetFileName(path) == "z-small.txt" ? 512 : 4096, FactScope.Entry, "fixture"),
                ReparseTag = ObservedFact<uint>.Unavailable(FactState.NotApplicable, FactScope.Entry, "fixture", "No reparse tag."),
            };
        }
    }
    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }

    [Fact]
    public void Optional_column_catalog_preserves_default_profiles_and_persisted_hidden_header()
    {
        Assert.Contains(ColumnProfileSet.Choices, c => c.Key == "meta:" + FileDetailsFields.Allocated);
        Assert.Contains(ColumnProfileSet.Choices, c => c.Key == "meta:" + FileDetailsFields.HardLinks);
        Assert.Equal(OperatingSystem.IsWindows(), ColumnProfileSet.Choices.Any(c => c.Key == "meta:" + FileDetailsFields.ReparseType));
        Assert.DoesNotContain(ColumnProfiles.Defaults.SelectMany(p => p.Columns), c => c.MetadataId is FileDetailsFields.Allocated or FileDetailsFields.HardLinks or FileDetailsFields.ReparseType);
        var saved = new ColumnSpec(ColumnField.Metadata, "Streams", 70) { MetadataId = "hidden" };
        Assert.Equal("Streams", saved.Header); Assert.Equal("Extra data", saved.DisplayHeader);
        Assert.Contains("not recursive", ColumnDescriptions.For(FileDetailsFields.Allocated));
    }

    [AvaloniaFact]
    public async Task Allocated_analysis_includes_directories_and_keeps_pointer_inspection_cached()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var records = new Records();
        foreach (var field in FileDetailsFields.Create(records, OperatingSystem.IsWindows())) services.Metadata.Register(field);
        try
        {
            var tab = vm.Workspace.ActiveTab!;
            Directory.CreateDirectory(Path.Combine(root, "files", "sub"));
            tab.Refresh();
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing && tab.Listing.TotalCount == 3);
            await tab.AnalyzeAsync(FileDetailsFields.Allocated);
            Assert.Equal(3, records.Calls);
            Assert.Contains("3 items", tab.Banner);
            Assert.DoesNotContain("unretained", tab.Banner);
            Assert.NotNull(tab.Listing.AppliedMetadataCoverage);
            Assert.Equal(3, tab.Listing.AppliedMetadataCoverage.Count);
            int i = Enumerable.Range(0, tab.Listing.VisibleCount).Single(i => tab.Listing.GetVisible(i).Name == "sub");
            var directory = tab.Listing.GetVisible(i);
            var context = tab.CaptureMetadataContext(directory, tab.Listing.GetStoreIndex(i));
            var inspected = tab.MetadataPresentation(directory, tab.Listing.GetStoreIndex(i), FileDetailsFields.Allocated, context, demand: false);
            Assert.Contains("contents are not counted", inspected.Detail);
            Assert.Equal(3, records.Calls);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Result_analysis_reads_original_devices_and_keeps_unavailable_rows_last(bool workingSet)
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var records = new Records();
        foreach (var field in FileDetailsFields.Create(records, OperatingSystem.IsWindows())) services.Metadata.Register(field);
        try
        {
            string first = Directory.CreateDirectory(Path.Join(root, "first")).FullName;
            string second = Directory.CreateDirectory(Path.Join(root, "second")).FullName;
            File.WriteAllText(Path.Join(first, "a-large.txt"), "fixture");
            File.WriteAllText(Path.Join(second, "z-small.txt"), "fixture");
            // A second POSIX spelling routes through a distinct device key without touching a real network share.
            if (!OperatingSystem.IsWindows()) second = "/" + second;
            var set = new ResultSet(Guid.NewGuid().ToString("N"), "native column fixture", "test") { IsWorkingSet = workingSet };
            services.ResultSets.Adopt(set);
            var items = new[]
            {
                new ItemRef(Location.FileSystem(first), "a-large.txt", EntryKind.File),
                new ItemRef(Location.FileSystem(second), "z-small.txt", EntryKind.File),
                new ItemRef(Location.FileSystem(first), "gone.txt", EntryKind.File),
                new ItemRef(new Location(Schemes.Zip, "", Location.FileSystem(Path.Join(root, "archive.zip"))), "member.txt", EntryKind.File),
            };
            foreach (var item in items) set.Add(item, "display label");
            var tab = vm.Workspace.ActiveTab!;
            tab.Navigate(ResultSetProvider.LocationOf(set));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Location?.Session == set.Id);
            Assert.False(tab.CanAnalyzeMetadata(FileDetailsFields.Allocated));
            set.IsComplete = true;
            Assert.True(tab.CanAnalyzeMetadata(FileDetailsFields.Allocated));
            Assert.Contains(vm.AnalysisFields(tab), f => f.Id == FileDetailsFields.Allocated);
            await tab.AnalyzeAsync(FileDetailsFields.Allocated);

            Assert.Equal(items.Where(item => item.FileSystemPath is not null).Select(item => item.FileSystemPath).Order(), records.Paths.Order());
            Assert.Contains("4 items", tab.Banner);
            Assert.Contains("2 current values", tab.Banner);
            Assert.Contains("1 gone", tab.Banner);
            Assert.Contains("1 unsupported", tab.Banner);
            Assert.Equal(4, tab.Listing.AppliedMetadataCoverage?.Count);
            string[] Names() => Enumerable.Range(0, tab.Listing.VisibleCount).Select(i => tab.Listing.GetVisible(i))
                .Where(entry => entry.Kind != EntryKind.Parent).Select(entry => entry.Name).ToArray();
            Assert.Equal(new[] { "z-small.txt", "a-large.txt" }, Names()[..2]);
            tab.Listing.Sort = tab.Listing.Sort with { Descending = true };
            await Until(() => tab.Listing.AppliedViewSpecVersion == tab.Listing.ViewSpecVersion);
            Assert.Equal(new[] { "a-large.txt", "z-small.txt" }, Names()[..2]);
            Assert.Equal(3, records.Calls);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    /// <summary>Per-name hard-link facts: known counts, a partial lower bound and an unreturned (unknown) zero.</summary>
    private sealed class LinkRecords(IReadOnlyDictionary<string, ObservedFact<uint>> links) : IFileRecords
    {
        public bool IsSupported => true;
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy reader invoked.");
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) => throw new InvalidOperationException("Full reader invoked.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct) => new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
        {
            Kind = FileObjectKind.File,
            LinkCount = links.TryGetValue(Path.GetFileName(path), out var fact) ? fact : ObservedFact<uint>.Known(1, FactScope.Entry, "fixture"),
            ReparseTag = ObservedFact<uint>.Unavailable(FactState.NotApplicable, FactScope.Entry, "fixture", "No reparse tag."),
        };
    }

    /// <summary>Names-only inventories: a complete count, a partial lower bound, or nothing inspected.</summary>
    private sealed class InventoryData(IReadOnlyDictionary<string, (int Items, bool Complete)> counts) : Core.HiddenData.IHiddenData
    {
        public bool IsSupported => true;
        public IReadOnlyList<Core.HiddenData.HiddenItem> List(string path) => throw new InvalidOperationException("The combined list reads values.");
        public Core.HiddenData.HiddenInventory ReadInventory(string path, Core.HiddenData.HiddenInventoryRequest request, CancellationToken ct)
        {
            var (count, complete) = counts.TryGetValue(Path.GetFileName(path), out var known) ? known : (0, true);
            return new(path, DateTimeOffset.UtcNow,
                [.. Enumerable.Range(0, count).Select(i => new Core.HiddenData.HiddenItem("stream" + i, Core.HiddenData.HiddenKind.Stream, 1))],
                [new(Core.HiddenData.HiddenKind.Stream, Core.HiddenData.HiddenInventoryState.Complete, count, "fixture"),
                 new(Core.HiddenData.HiddenKind.NtfsAttribute, complete ? Core.HiddenData.HiddenInventoryState.Complete : Core.HiddenData.HiddenInventoryState.NotInspected, 0, "fixture")],
                "fixture") { BoundToObject = true };
        }
        public byte[] Read(string path, Core.HiddenData.HiddenItem item, int max) => throw new InvalidOperationException("Value read.");
        public Stream Open(string path, Core.HiddenData.HiddenItem item) => throw new InvalidOperationException("Value open.");
        public void Delete(string path, Core.HiddenData.HiddenItem item) => throw new InvalidOperationException("Deletion.");
    }

    [AvaloniaFact]
    public async Task Partial_and_unknown_zero_counts_sort_last_in_both_directions_and_never_as_exact_numbers()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        var previousHidden = BuiltInFields.HiddenDataSource;
        static ObservedFact<uint> Links(uint count) => ObservedFact<uint>.Known(count, FactScope.Entry, "fixture");
        var links = new Dictionary<string, ObservedFact<uint>>(StringComparer.Ordinal)
        {
            ["a.txt"] = Links(5), ["b.txt"] = Links(2), ["c-three.bin"] = Links(3), ["d-zero.bin"] = Links(0), ["e-one.bin"] = Links(1),
            // A lower bound with the same value as b.txt, and an unreturned field whose default value is zero.
            ["f-partial.bin"] = new(FactState.Partial, 2, FactScope.Entry, "fixture", "At least two names were enumerated."),
            ["g-unknown.bin"] = ObservedFact<uint>.Unavailable(FactState.Unsupported, FactScope.Entry, "fixture", "The link count was not returned."),
        };
        var inventories = new Dictionary<string, (int, bool)>(StringComparer.Ordinal)
        {
            ["a.txt"] = (5, true), ["b.txt"] = (2, true), ["c-three.bin"] = (3, true), ["d-zero.bin"] = (0, true), ["e-one.bin"] = (1, true),
            ["f-partial.bin"] = (2, false), ["g-unknown.bin"] = (0, false),
        };
        foreach (var field in FileDetailsFields.Create(new LinkRecords(links), OperatingSystem.IsWindows())) services.Metadata.Register(field);
        BuiltInFields.HiddenDataSource = new InventoryData(inventories);
        try
        {
            var tab = vm.Workspace.ActiveTab!;
            foreach (string name in new[] { "c-three.bin", "d-zero.bin", "e-one.bin", "f-partial.bin", "g-unknown.bin" })
                File.WriteAllText(Path.Combine(root, "files", name), name);
            tab.Refresh();
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing && tab.Listing.TotalCount == 7);
            string[] Names() => Enumerable.Range(0, tab.Listing.VisibleCount).Select(i => tab.Listing.GetVisible(i))
                .Where(entry => entry.Kind != EntryKind.Parent).Select(entry => entry.Name).ToArray();
            string Text(string name, string field)
            {
                int index = tab.Listing.FindStoreIndex(name);
                return tab.GetMetadataText(tab.Listing.Store[index], index, field, out _);
            }
            string[] exact = ["d-zero.bin", "e-one.bin", "b.txt", "c-three.bin", "a.txt"];
            string[] unknown = ["f-partial.bin", "g-unknown.bin"];
            foreach (string field in new[] { "hidden", FileDetailsFields.HardLinks })
            {
                await tab.AnalyzeAsync(field);
                await Until(() => tab.Listing.AppliedViewSpecVersion == tab.Listing.ViewSpecVersion && tab.Listing.Sort.MetadataId == field);
                Assert.False(tab.Listing.Sort.Descending);
                Assert.Equal(exact, Names()[..5]);
                Assert.Equal(unknown, Names()[5..].Order(StringComparer.Ordinal));
                tab.Listing.Sort = tab.Listing.Sort with { Descending = true };
                await Until(() => tab.Listing.AppliedViewSpecVersion == tab.Listing.ViewSpecVersion);
                Assert.Equal(Enumerable.Reverse(exact), Names()[..5]);
                Assert.Equal(unknown, Names()[5..].Order(StringComparer.Ordinal));
            }
            // The lower bound and the uninspected zero read as such, never as the exact numbers they resemble.
            Assert.Equal("2+", Text("f-partial.bin", "hidden"));
            Assert.Equal("Not inspected", Text("g-unknown.bin", "hidden"));
            Assert.Equal("0", Text("d-zero.bin", "hidden"));
            Assert.Equal("Partial", Text("f-partial.bin", FileDetailsFields.HardLinks));
            Assert.NotEqual("0", Text("g-unknown.bin", FileDetailsFields.HardLinks));
            Assert.Equal("0", Text("d-zero.bin", FileDetailsFields.HardLinks));
        }
        finally
        {
            BuiltInFields.HiddenDataSource = previousHidden;
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task Canceling_result_analysis_retires_publication_while_the_original_read_finishes()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var records = new Records(_ =>
        {
            entered.TrySetResult();
            release.Wait(TimeSpan.FromSeconds(15));
            exited.TrySetResult();
        });
        foreach (var field in FileDetailsFields.Create(records, OperatingSystem.IsWindows())) services.Metadata.Register(field);
        try
        {
            var set = services.ResultSets.Create("blocked native column", "test");
            set.IsComplete = true;
            foreach (string name in new[] { "a.txt", "b.txt" })
                set.Add(ItemRef.ForFileSystemPath(Path.Join(root, "files", name), EntryKind.File), "");
            var tab = vm.Workspace.ActiveTab!;
            tab.Navigate(ResultSetProvider.LocationOf(set));
            await Until(() => tab.Location?.Session == set.Id && tab.Listing.State == ListingState.Complete);
            var ct = TestContext.Current.CancellationToken;
            var analysis = tab.AnalyzeAsync(FileDetailsFields.Allocated);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.True(tab.CancelAnalysis());
            await analysis.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.False(exited.Task.IsCompleted);
            Assert.Contains("No retained success snapshot", tab.Banner);
            release.Set();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Equal(1, records.Calls);
        }
        finally { release.Set(); AccessibilityTests.Close(services, window, root); }
    }
}
