using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.Core.Inspect;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Records;
using FileCat.Core.Resources;
using FileCat.Core.Search;

namespace FileCat.App.Tests;

public sealed class FileDetailsColumnTests
{
    private sealed class Records : IFileRecords
    {
        public bool IsSupported => true;
        public int Calls;
        public InspectionReport Read(string path, CancellationToken ct) => throw new InvalidOperationException("Legacy reader invoked.");
        public FileDetailsReport ReadFull(string path, FileDetailsPolicy policy, CancellationToken ct) => throw new InvalidOperationException("Full reader invoked.");
        public FileDetailsSnapshot ReadSummary(string path, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return new(path, DateTimeOffset.UtcNow, Guid.NewGuid())
            {
                Kind = Path.GetFileName(path) == "sub" ? FileObjectKind.Directory : FileObjectKind.File,
                Allocated = ObservedFact<long>.Known(4096, FactScope.Entry, "fixture"),
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
            var set = services.ResultSets.Create("native column fixture", "test");
            set.Add(ItemRef.ForFileSystemPath(Path.Combine(root, "files", "a.txt"), EntryKind.File), ""); set.IsComplete = true;
            tab.Navigate(ResultSetProvider.LocationOf(set));
            await Until(() => tab.Listing.State == ListingState.Complete);
            Assert.False(tab.CanAnalyzeMetadata(FileDetailsFields.Allocated));
            Assert.DoesNotContain(vm.AnalysisFields(tab), f => f.ObservationLane);
            await tab.AnalyzeAsync(FileDetailsFields.Allocated);
            Assert.Equal(3, records.Calls);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
