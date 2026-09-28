using System.Text;
using Avalonia.Headless.XUnit;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class CompareWindowTests
{
    private static async Task<CompareWindow> OpenAsync(string left, string right)
    {
        var window = CompareWindow.Open("left.txt", new MemoryContentSource("left.txt", Encoding.UTF8.GetBytes(left)),
            "right.txt", new MemoryContentSource("right.txt", Encoding.UTF8.GetBytes(right)));
        for (int i = 0; i < 250 && window.Summary == "Comparing…"; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        return window;
    }

    [AvaloniaFact]
    public async Task Text_differences_are_counted_navigable_and_never_called_identical_by_mistake()
    {
        var diff = await OpenAsync("one\ntwo\nthree\n", "one\nTWO\nthree\nfour\n");
        try
        {
            Assert.StartsWith("2 differences: 1 changed, 0 only left, 1 only right", diff.Summary);
            var rows = diff.Rows.OfType<CompareRow>().ToList();
            Assert.Equal([DiffKind.Equal, DiffKind.Changed, DiffKind.Equal, DiffKind.RightOnly], rows.Select(r => r.Kind));
            diff.Go(+1);
            diff.Go(+1);
            diff.Go(+1); // wraps to the first difference
        }
        finally
        {
            diff.Close();
        }

        var endings = await OpenAsync("one\r\ntwo\r\n", "one\ntwo\n");
        try { Assert.Equal("The text is the same, but the files differ in bytes: line endings on 2 lines.", endings.Summary); }
        finally { endings.Close(); }

        var same = await OpenAsync("same\n", "same\n");
        try { Assert.Equal("Identical: every byte was compared (5 bytes).", same.Summary); }
        finally { same.Close(); }

        var binary = CompareWindow.Open("a.bin", new MemoryContentSource("a.bin", [0, 1, 2, 3, 0]), "b.bin", new MemoryContentSource("b.bin", [0, 9, 9, 3]));
        for (int i = 0; i < 250 && binary.Summary == "Comparing…"; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
        try
        {
            Assert.StartsWith("2 differing byte ranges at the same offsets; lengths 5 and 4 bytes.", binary.Summary);
            Assert.Equal(2, binary.Rows.Count);
        }
        finally
        {
            binary.Close();
        }
    }

    [AvaloniaFact]
    public async Task A_comparison_opens_on_its_first_difference()
    {
        string same = string.Concat(Enumerable.Range(0, 400).Select(i => $"line {i}\n"));
        var diff = await OpenAsync(same + "only left\n", same);
        try
        {
            for (int i = 0; i < 100 && diff.CurrentRow < 0; i++) await Task.Delay(20, TestContext.Current.CancellationToken);
            Assert.Equal(400, diff.CurrentRow);
            Assert.Equal(DiffKind.LeftOnly, ((CompareRow)diff.Rows[diff.CurrentRow]).Kind);
        }
        finally
        {
            diff.Close();
        }
    }

    [AvaloniaFact]
    public async Task Compare_files_opens_the_two_marked_files()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var ct = TestContext.Current.CancellationToken;
            var listing = vm.ActiveTab!.Listing;
            for (int i = 0; i < 250 && !(listing.State == Core.Listing.ListingState.Complete && listing.VisibleCount == 3); i++) await Task.Delay(20, ct);
            vm.Execute(CommandIds.MarkAll);
            vm.Execute(CommandIds.CompareFiles);
            for (int i = 0; i < 250 && CompareWindow.OpenWindows.Count == 0; i++) await Task.Delay(20, ct);
            var compare = Assert.Single(CompareWindow.OpenWindows);
            for (int i = 0; i < 250 && compare.Summary == "Comparing…"; i++) await Task.Delay(20, ct);
            Assert.StartsWith("1 difference: 1 changed", compare.Summary);
            compare.Close();
        }
        finally
        {
            AccessibilityTests.Close(services, window, root);
        }
    }
}
