using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Views;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class ComparisonRowInteractionTests
{
    [AvaloniaTheory]
    [InlineData(16, false)]
    [InlineData(4096, false)]
    [InlineData(32768, false)]
    [InlineData(16, true)]
    [InlineData(4096, true)]
    [InlineData(32768, true)]
    public void A_sync_checkbox_changes_its_live_item_but_does_not_keep_a_retired_item_alive(int count, bool close)
    {
        var (window, box, owners) = Open(count);
        try
        {
            if (close) window.Close();
            else CheckLiveItem(box);
            for (int i = 0; i < 4; i++)
            {
                Dispatcher.UIThread.RunJobs();
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            }
            bool[] alive = owners.Select(v => v.IsAlive).ToArray();
            TestContext.Current.TestOutputHelper?.WriteLine("SYNC_CHECKBOX_RETIRE " + JsonSerializer.Serialize(new
            { count, close, alive, checkboxAndWindowHeld = true, liveToggleChecked = !close }));
            Assert.All(alive, value => Assert.Equal(!close, value));
            if (close)
            {
                box.IsChecked = false;
                box.IsChecked = true;
                Assert.Empty(window.Items);
            }
            GC.KeepAlive(window); GC.KeepAlive(box);
        }
        finally { window.Close(); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (SyncWindow, CheckBox, WeakReference[]) Open(int count)
    {
        var result = new TreeCompareResult(Enumerable.Range(0, count).Select(i => new TreeDiffEntry($"owned-{i}", TreeDiffKind.LeftOnly,
            new EntryData { Name = $"owned-{i}", Kind = EntryKind.File, Size = i + 1 }, null)).ToArray(), true, 1);
        var window = new SyncWindow(result, new SyncContext("owned-left", "owned-right", true, true, true, true, false, static (_, _, _) => false));
        window.Show(); Dispatcher.UIThread.RunJobs();
        window.Choose(true, SyncMode.Mirror); Dispatcher.UIThread.RunJobs();
        var box = window.GetVisualDescendants().OfType<CheckBox>().First(c => c.DataContext is SyncItem);
        var item = (SyncItem)box.DataContext!;
        return (window, box, [new WeakReference(item), new WeakReference(item.Entry)]);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckLiveItem(CheckBox box)
    {
        var item = Assert.IsType<SyncItem>(box.DataContext);
        Assert.True(item.Include);
        box.IsChecked = false; Assert.False(item.Include);
        box.IsChecked = true; Assert.True(item.Include);
    }
}
