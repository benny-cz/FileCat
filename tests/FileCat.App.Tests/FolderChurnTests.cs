using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// V12: a shown folder churned at full speed (12,000 changes: files made, renamed, rewritten and deleted) ends as the disk
/// is, and the marks and the cursor on files the churn did not touch stay where they were. Whether the folder's change
/// notifications overflowed is reported; an overflow is forced and observed in ChangeMonitorOverflowTests.
/// </summary>
public sealed class FolderChurnTests
{
    [AvaloniaFact]
    public async Task A_folder_churned_at_full_speed_ends_as_the_disk_is_with_marks_and_cursor_kept()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Directory.CreateDirectory(Path.Combine(root, "churn")).FullName;
            for (int i = 0; i < 50; i++) File.WriteAllText(Path.Combine(folder, $"keep-{i:00}.txt"), "keep");
            var tab = vm.Workspace.Panels[0].ActiveTab!;
            vm.Workspace.Activate(vm.Workspace.Panels[0]);
            tab.Navigate(Location.FileSystem(folder));
            var listing = tab.Listing;
            static List<string> Names(ListingModel l) =>
                [.. Enumerable.Range(0, l.VisibleCount).Select(l.GetVisible).Where(e => e.Kind != EntryKind.Parent).Select(e => e.Name).Order(StringComparer.Ordinal)];
            for (int i = 0; i < 300 && !(listing.State == ListingState.Complete && Names(listing).Count == 50); i++) await Task.Delay(20, ct);
            var marked = Enumerable.Range(10, 10).Select(i => $"keep-{i:00}.txt").ToList();
            listing.MarkNames(marked, true);
            Assert.True(listing.FocusName("keep-25.txt"));

            // The churn, on another thread, as fast as the disk takes it.
            int operations = await Task.Run(() =>
            {
                int n = 0;
                for (int i = 0; i < 6000; i++, n++) File.WriteAllText(Path.Combine(folder, $"churn-{i:0000}.txt"), "c");
                for (int i = 0; i < 2000; i++, n++) File.Move(Path.Combine(folder, $"churn-{i:0000}.txt"), Path.Combine(folder, $"renamed-{i:0000}.txt"));
                for (int i = 2000; i < 4000; i++, n++) File.Delete(Path.Combine(folder, $"churn-{i:0000}.txt"));
                for (int i = 4000; i < 6000; i++, n++) File.WriteAllText(Path.Combine(folder, $"churn-{i:0000}.txt"), "rewritten");
                return n;
            }, ct);

            // Settled: the panel shows what the disk holds, no more and no less.
            var disk = Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(4050 + 2000 - 2000, disk.Count);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(60) && !(listing.State == ListingState.Complete && !listing.IsRefreshing && Names(listing).SequenceEqual(disk)))
                await Task.Delay(50, ct);
            var shown = Names(listing);
            Assert.True(shown.SequenceEqual(disk),
                $"after {operations} changes and {clock.Elapsed.TotalSeconds:F1} s the panel shows {shown.Count} items for {disk.Count} on disk; " +
                $"missing {string.Join(", ", disk.Except(shown).Take(5))}; extra {string.Join(", ", shown.Except(disk).Take(5))}");
            TestContext.Current.TestOutputHelper?.WriteLine($"{operations} changes, {tab.WatcherOverflows} notification overflow(s); the panel matched the disk ({disk.Count} items) {clock.Elapsed.TotalSeconds:F1} s after they ended");

            // Marks and the cursor on files the churn did not touch stayed where they were.
            var marks = Enumerable.Range(0, listing.VisibleCount).Where(listing.IsVisibleMarked).Select(i => listing.GetVisible(i).Name).Order(StringComparer.Ordinal).ToList();
            Assert.Equal(marked, marks);
            Assert.True(listing.TryGetFocused(out var focused));
            Assert.Equal("keep-25.txt", focused.Name);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
