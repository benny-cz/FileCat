using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// On Linux an item's birth time costs one more call per item, and .NET's creation time there is not when the item was
/// made (it is the earlier of the modification and status change times). A list reads birth times only while it shows or
/// sorts by Created, and gets them at once when it starts to; Windows and macOS list their creation times anyway.
/// </summary>
public sealed class CreatedColumnTests
{
    private sealed class Sink : IEnumerationSink
    {
        public List<EntryData> Entries { get; } = [];
        public bool IncludeBirthTimes => true;
        public void AddBatch(ReadOnlySpan<EntryData> entries) { foreach (var entry in entries) Entries.Add(entry); }
        public void ReportIssue(string message) => Assert.Fail(message);
    }

    [AvaloniaFact]
    public async Task A_list_that_shows_or_sorts_by_Created_asks_Linux_for_birth_times()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var tab = vm.Workspace.ActiveTab!;
            await Until(() => tab.Listing.State == ListingState.Complete && !tab.Listing.IsRefreshing && tab.Listing.TotalCount == 2);
            long Created() => tab.Listing.Store[tab.Listing.FindStoreIndex("a.txt")].Created;
            Assert.False(tab.Listing.IncludeBirthTimes);
            Assert.Equal(OperatingSystem.IsLinux(), Created() == 0);

            // What the folder lists when asked (on Linux 0 where its file system keeps no birth time).
            var sink = new Sink();
            await new LocalFileSystemProvider().EnumerateAsync(Location.FileSystem(Path.Combine(root, "files")), sink, TestContext.Current.CancellationToken);
            long born = Assert.Single(sink.Entries, e => e.Name == "a.txt").Created;

            int full = Enumerable.Range(0, services.Columns.Count)
                .First(i => services.Columns.Get(i, Schemes.FileSystem).Any(c => c.Field == ColumnField.Created));
            tab.ColumnProfile = full;
            Assert.Equal(OperatingSystem.IsLinux(), tab.Listing.IncludeBirthTimes);
            await Until(() => !tab.Listing.IsRefreshing && tab.Listing.State == ListingState.Complete && Created() == born);

            // A profile without Created no longer asks; sorting by Created does.
            tab.ColumnProfile = 0;
            Assert.False(tab.Listing.IncludeBirthTimes);
            tab.SortBy(SortField.Created);
            Assert.Equal(OperatingSystem.IsLinux(), tab.Listing.IncludeBirthTimes);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }

    private static async Task Until(Func<bool> condition)
    {
        for (int i = 0; i < 1000 && !condition(); i++) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(condition());
    }
}
