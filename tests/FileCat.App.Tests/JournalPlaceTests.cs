using FileCat.App.Controls;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using Avalonia.Headless.XUnit;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Tests;

/// <summary>D-56 in the window: a change journal lists newest first in its own columns, and Enter goes to an entry's item.</summary>
public sealed class JournalPlaceTests
{
    private static async Task Until(Func<bool> condition, string what)
    {
        for (int i = 0; i < 400 && !condition(); i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        Assert.True(condition(), what);
    }

    /// <summary>A journal row about an item that is (or is no longer) somewhere.</summary>
    private sealed record Row(string What, string Folder, (string, string)? At) : IDisplayDetails, ILocatableEntry
    {
        public string KindText => What;
        public string DetailsText => Folder;
        public (string Folder, string Name)? Locate() => At;
    }

    private sealed class FakeJournal(IReadOnlyList<EntryData> rows) : ResourceProvider
    {
        public override string Scheme => Schemes.Journal;
        public override string GetDisplayPath(Location location) => location.Path + " › change journal";
        public override Location? GetParent(Location location) => Location.FileSystem(location.Path);
        public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
        public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
        public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
        {
            sink.AddBatch(rows.ToArray());
            return Task.CompletedTask;
        }
    }

    [AvaloniaFact]
    public async Task A_journal_lists_newest_first_and_Enter_goes_to_the_item_where_it_is_now()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            string folder = Directory.CreateDirectory(Path.Combine(root, "files")).FullName;
            File.WriteAllText(Path.Combine(folder, "renamed.log"), "x");
            var now = DateTime.UtcNow;
            services.Providers.Register(new FakeJournal(
            [
                new EntryData("older.txt", EntryKind.File, -1, now.AddMinutes(-5).Ticks) { Tag = new Row("created, closed", folder, null) },
                new EntryData("report.txt", EntryKind.File, -1, now.AddMinutes(-1).Ticks) { Tag = new Row("renamed from", folder, (folder, "renamed.log")) },
                new EntryData("gone.tmp", EntryKind.File, -1, now.AddMinutes(-3).Ticks) { Tag = new Row("deleted, closed", folder, null) },
            ]));
            var tab = vm.Workspace.Panels[0].ActiveTab!;
            var before = tab.Listing.Sort;
            tab.Navigate(new Location(Schemes.Journal, folder));
            await Until(() => tab.Listing.State == ListingState.Complete && tab.Listing.VisibleCount >= 3, "the journal is listed");
            // Newest first, whatever the tab's own order.
            var names = Enumerable.Range(0, tab.Listing.VisibleCount).Select(tab.Listing.GetVisible).Where(e => e.Kind != EntryKind.Parent).Select(e => e.Name).ToList();
            Assert.Equal(["report.txt", "gone.tmp", "older.txt"], names);
            Assert.Equal(["Name", "Time", "What happened", "Folder"], ColumnProfiles.Journal.Select(c => c.Header));
            Assert.True(ColumnProfiles.Journal[1].Seconds);
            Assert.Equal("renamed from", tab.GetKindText(tab.Listing.GetVisible(tab.Listing.HasParentRow ? 1 : 0)));
            Assert.Equal("3 entries", tab.StatusLeft.Split(" · ")[0]);

            // Enter on an item that is gone does nothing; on one that exists, its folder opens with it under the cursor.
            tab.Listing.SetFocus(names.IndexOf("gone.tmp") + (tab.Listing.HasParentRow ? 1 : 0));
            Assert.False(tab.TryEnterFocused(out _));
            tab.Listing.SetFocus(names.IndexOf("report.txt") + (tab.Listing.HasParentRow ? 1 : 0));
            Assert.True(tab.TryEnterFocused(out _));
            await Until(() => tab.Location?.IsFileSystem == true && tab.Listing.State == ListingState.Complete, "the item's folder opens");
            Assert.Equal(folder, tab.Location!.Path);
            Assert.True(tab.Listing.TryGetFocused(out var focused));
            Assert.Equal("renamed.log", focused.Name);
            // Leaving the journal gives the tab its own order back.
            Assert.Equal(before, tab.Listing.Sort);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
