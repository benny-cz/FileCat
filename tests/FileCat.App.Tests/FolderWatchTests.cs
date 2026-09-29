using Avalonia.Headless.XUnit;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

/// <summary>
/// A shown folder follows what other programs do in it, through the system's change notifications (plan §8.2):
/// ReadDirectoryChangesW on Windows, inotify on Linux, FSEvents on macOS.
/// </summary>
public sealed class FolderWatchTests
{
    [AvaloniaFact]
    public async Task A_shown_folder_that_another_program_deletes_leaves_the_panel_at_its_nearest_parent()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string files = Path.Combine(root, "files");
            string deep = Directory.CreateDirectory(Path.Combine(files, "gone", "deeper")).FullName;
            var tab = vm.Workspace.Panels[1].ActiveTab!;
            tab.Navigate(Location.FileSystem(deep));
            for (int i = 0; i < 250 && (tab.Location?.Path != deep || tab.Listing.State != ListingState.Complete); i++) await Task.Delay(20, ct);

            Directory.Delete(Path.Combine(files, "gone"), recursive: true);
            for (int i = 0; i < 500 && tab.Location?.Path != files; i++) await Task.Delay(20, ct);
            Assert.True(files == tab.Location?.Path, $"at {tab.Location}, banner: {tab.Banner}, state {tab.Listing.State}");
            for (int i = 0; i < 250 && tab.Listing.State != ListingState.Complete; i++) await Task.Delay(20, ct);
            // The panel says why it moved.
            Assert.Contains("“deeper” is no longer there", tab.Banner);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }

    [AvaloniaFact]
    public async Task A_shown_folder_follows_files_made_changed_renamed_and_deleted_by_other_programs()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files");
            // Both panels show the folder; the left one is active, the right one is only shown.
            var tabs = vm.Workspace.Panels.Select(p => p.ActiveTab!).ToList();
            foreach (var tab in tabs)
                for (int i = 0; i < 250 && (tab.Location?.Path != folder || tab.Listing.State != ListingState.Complete); i++) await Task.Delay(20, ct);

            static List<EntryData> Rows(TabViewModel tab) =>
                Enumerable.Range(0, tab.Listing.VisibleCount).Select(tab.Listing.GetVisible).Where(e => e.Kind != EntryKind.Parent).ToList();
            async Task Until(string what, Func<TabViewModel, bool> done, int seconds = 10)
            {
                for (int i = 0; i < seconds * 50 && !tabs.All(done); i++) await Task.Delay(20, ct);
                foreach (var tab in tabs)
                    Assert.True(done(tab), $"{what}: panel {tabs.IndexOf(tab) + 1} shows {string.Join(", ", Rows(tab).Select(e => $"{e.Name} ({e.Size})"))}");
            }

            string made = Path.Combine(folder, "made-elsewhere.txt");
            File.WriteAllText(made, "x");
            await Until("a new file", t => Rows(t).Any(e => e.Name == "made-elsewhere.txt"));

            File.WriteAllText(made, new string('x', 5000));
            await Until("a file that grew", t => Rows(t).Any(e => e.Name == "made-elsewhere.txt" && e.Size == 5000));

            File.Move(made, Path.Combine(folder, "renamed.txt"));
            await Until("a renamed file", t => Rows(t).Any(e => e.Name == "renamed.txt") && Rows(t).All(e => e.Name != "made-elsewhere.txt"));

            File.Delete(Path.Combine(folder, "renamed.txt"));
            await Until("a deleted file", t => Rows(t).All(e => e.Name != "renamed.txt"));

            Directory.CreateDirectory(Path.Combine(folder, "new folder"));
            await Until("a new folder", t => Rows(t).Any(e => e.Name == "new folder" && e.Kind == EntryKind.Directory));

            // A burst, written while the panels reread after its first files: every file shows once it settles.
            for (int i = 0; i < 300; i++)
            {
                File.WriteAllText(Path.Combine(folder, $"burst-{i:000}.txt"), "b");
                if (i % 50 == 0) await Task.Delay(30, ct);
            }
            await Until("a burst of 300 files", t => Rows(t).Count(e => e.Name.StartsWith("burst-", StringComparison.Ordinal)) == 300, seconds: 20);

            // A steady stream for three seconds, so that changes arrive while the panels are rereading: the last ones
            // are not lost with the reread they arrived during.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int streamed = 0;
            while (clock.Elapsed < TimeSpan.FromSeconds(3))
            {
                File.WriteAllText(Path.Combine(folder, $"stream-{streamed++:0000}.txt"), "s");
                await Task.Delay(7, ct);
            }
            await Until($"a stream of {streamed} files", t => Rows(t).Count(e => e.Name.StartsWith("stream-", StringComparison.Ordinal)) == streamed, seconds: 20);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
