using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.Core.Listing;

namespace FileCat.App.Tests;

/// <summary>A panel's status line: what is here, and, with items marked, what is marked and how big it is.</summary>
public sealed class PanelStatusTests
{
    [AvaloniaFact]
    public async Task Marking_says_what_is_marked_and_how_big_and_counts_folders_on_request()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            string folder = Path.Combine(root, "files");
            Directory.CreateDirectory(Path.Combine(folder, "sub"));
            File.WriteAllBytes(Path.Combine(folder, "sub", "inside.bin"), new byte[3000]);
            var tab = vm.ActiveTab!;
            tab.Refresh();
            var listing = tab.Listing;
            for (int i = 0; i < 250 && !(listing.State == ListingState.Complete && listing.Store.Totals.Directories == 1); i++) await Task.Delay(20, ct);
            int Index(string name)
            {
                for (int i = 0; i < listing.VisibleCount; i++)
                    if (listing.GetVisible(i).Name == name) return i;
                throw new InvalidOperationException(name + " is not listed");
            }

            // Nothing marked: what is here.
            Assert.Equal(string.Empty, tab.StatusMarked);
            Assert.StartsWith($"1 folder, 2 files · {Formatters.SizeWithUnit(9)}", tab.StatusLeft, StringComparison.Ordinal);
            // Files: how many of how many, and their size; the rest of the line follows.
            listing.SetMark(Index("a.txt"), true);
            listing.SetMark(Index("b.txt"), true);
            Assert.Equal($"Marked 2 of 2 files · {Formatters.SizeWithUnit(9)}", tab.StatusMarked);
            Assert.False(tab.CanCountMarked);
            Assert.DoesNotContain("2 files", tab.StatusLeft, StringComparison.Ordinal);
            // A folder marked without its size (Insert, a mask, Ctrl+A): said, not guessed, and counted on request.
            listing.SetMark(Index("sub"), true);
            Assert.Equal($"Marked 1 of 1 folder, 2 of 2 files · {Formatters.SizeWithUnit(9)}, not counting 1 folder", tab.StatusMarked);
            Assert.True(tab.CanCountMarked);
            vm.CountFolderSizes(tab);
            string counted = $"Marked 1 of 1 folder, 2 of 2 files · {Formatters.SizeWithUnit(3009)}";
            for (int i = 0; i < 250 && tab.StatusMarked != counted; i++) await Task.Delay(20, ct);
            Assert.Equal(counted, tab.StatusMarked);
            Assert.False(tab.CanCountMarked);
            // The item under the cursor is still described on the right.
            listing.SetFocus(Index("a.txt"));
            Assert.StartsWith("a.txt · ", tab.StatusRight, StringComparison.Ordinal);
            // Only the folder: its size alone.
            listing.SetMark(Index("a.txt"), false);
            listing.SetMark(Index("b.txt"), false);
            Assert.Equal($"Marked 1 of 1 folder · {Formatters.SizeWithUnit(3000)}", tab.StatusMarked);
            listing.UnmarkEverything();
            Assert.Equal(string.Empty, tab.StatusMarked);
            Assert.False(tab.CanCountMarked);
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
