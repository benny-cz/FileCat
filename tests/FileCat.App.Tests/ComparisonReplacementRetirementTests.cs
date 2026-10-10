using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class ComparisonReplacementRetirementTests
{
    [AvaloniaTheory]
    [InlineData("directory", 16)]
    [InlineData("directory", 4096)]
    [InlineData("directory", 32768)]
    [InlineData("compare", 16)]
    [InlineData("compare", 4096)]
    [InlineData("compare", 32768)]
    public async Task Removed_rows_do_not_keep_their_data_when_the_window_closes_after_a_view_change(string kind, int count)
    {
        var (window, oldRow, owner) = await Open(kind, count);
        try
        {
            if (window is DirectoryDiffWindow directory)
            {
                directory.ShowFilter(2); Assert.Empty(directory.ShownEntries);
            }
            else
            {
                var compare = (CompareWindow)window;
                compare.GetVisualDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, "Binary")).IsChecked = true;
                await Wait(() => !compare.IsComparing && compare.Rows.Count == 0);
            }
            window.Close();
            await Task.Delay(60, TestContext.Current.CancellationToken);
            for (int i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            }
            bool alive = owner.IsAlive;
            TestContext.Current.TestOutputHelper?.WriteLine("COMPARISON_REPLACEMENT_RETIRE " + JsonSerializer.Serialize(new
            { kind, count, alive, oldRealizedRowAndClosedWindowHeld = true, publicViewChange = true }));
            Assert.False(alive);
            GC.KeepAlive(window); GC.KeepAlive(oldRow);
        }
        finally { window.Close(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(Window, ListBoxItem, WeakReference)> Open(string kind, int count)
    {
        Window window;
        if (kind == "directory")
        {
            var result = new TreeCompareResult(Enumerable.Range(0, count).Select(i => new TreeDiffEntry($"owned-{i}", TreeDiffKind.LeftOnly,
                new EntryData { Name = $"owned-{i}", Kind = EntryKind.File, Size = i + 1 }, null)).ToArray(), true, 1);
            var directory = DirectoryDiffWindow.StartAsync("owned-left", "owned-right", "owned filter replacement", (_, _) => Task.FromResult(result), (_, _) => { });
            await Wait(() => !directory.IsComparing);
            window = directory;
        }
        else
        {
            byte[] left = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, count).Select(i => $"owned-row-{i:D6}\n")));
            byte[] right = (byte[])left.Clone(); right[^2] = (byte)'x';
            var compare = CompareWindow.Open("owned-left.txt", new MemoryContentSource("left", left), "owned-right.txt", new MemoryContentSource("right", right));
            await Wait(() => !compare.IsComparing && compare.Rows.Count >= count);
            window = compare;
        }
        Dispatcher.UIThread.RunJobs();
        var row = window.GetVisualDescendants().OfType<ListBoxItem>().First(c => c.DataContext is TreeDiffEntry or CompareRow);
        return (window, row, new WeakReference(row.DataContext));
    }

    private static async Task Wait(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), "Owned comparison view did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
}
