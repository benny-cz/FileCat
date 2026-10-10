using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.App.Views;
using FileCat.Core.Compare;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed class ComparisonWindowRetirementTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private sealed record Observed(Window Window, WeakReference[] Owners);
    public static TheoryData<string, int, bool> CompletedCases
    {
        get
        {
            var cases = new TheoryData<string, int, bool>();
            foreach (string kind in new[] { "directory", "sync", "compare" })
            foreach (int count in new[] { 16, 4096, 32768 })
            foreach (bool close in new[] { false, true }) cases.Add(kind, count, close);
            return cases;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(CompletedCases))]
    public async Task Completed_previews_keep_only_the_materialized_owners_their_live_consumers_need(string kind, int count, bool close)
    {
        var observed = await Open(kind, count);
        try
        {
            Assert.True(Readable(observed.Window, count));
            if (close) observed.Window.Close();
            await Drain();
            Collect();
            bool[] alive = observed.Owners.Select(v => v.IsAlive).ToArray();
            bool readable = Readable(observed.Window, count);
            TestContext.Current.TestOutputHelper?.WriteLine("COMPARISON_RETIRE " + JsonSerializer.Serialize(new
            {
                kind, count, close, alive, readable, windowHeld = true,
                publicWindowEntryPoints = true, syntheticOwnedInputs = true, nativeDesktopInput = false,
                reflectionReadsOnly = true, managedReachabilityNotProcessPeak = true,
            }));
            Assert.NotEmpty(alive);
            Assert.All(alive, value => Assert.Equal(!close, value));
            Assert.Equal(!close, readable);
            GC.KeepAlive(observed.Window);
        }
        finally { observed.Window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(16)]
    [InlineData(4096)]
    [InlineData(32768)]
    public async Task Closing_one_sync_preview_preserves_another_borrower_until_that_borrower_closes(int count)
    {
        var (first, second, owners) = Borrowed(count);
        try
        {
            first.Close();
            await Drain(); Collect();
            Assert.All(owners, v => Assert.True(v.IsAlive));
            Assert.Equal(count, second.Items.Count);
            second.Choose(true, SyncMode.Mirror);
            Assert.Equal(count, second.Items.Count);
            second.Close();
            await Drain(); Collect();
            bool[] alive = owners.Select(v => v.IsAlive).ToArray();
            TestContext.Current.TestOutputHelper?.WriteLine("SYNC_BORROW_RETIRE " + JsonSerializer.Serialize(new
            { count, alive, bothClosedWindowsHeld = true, otherBorrowerUsableUntilClose = true, callerEntriesNotMutated = true }));
            Assert.All(alive, Assert.False);
            GC.KeepAlive(first); GC.KeepAlive(second);
        }
        finally { first.Close(); second.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(16)]
    [InlineData(4096)]
    [InlineData(32768)]
    public async Task A_late_directory_result_cannot_repopulate_a_closed_preview_or_change_the_callers_entries(int count)
    {
        var gate = new TaskCompletionSource<TreeCompareResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        int callbacks = 0;
        var window = DirectoryDiffWindow.StartAsync("owned-left", "owned-right", "owned delayed preview", (_, _) => gate.Task, (_, _) => callbacks++);
        window.Close();
        var result = Result(count);
        gate.SetResult(result);
        await Drain();
        window.OpenSide(true);
        Assert.Empty(window.ShownEntries);
        Assert.Null(window.OpenSync());
        Assert.Equal(0, callbacks);
        Assert.Equal(count, result.Entries.Count);
        TestContext.Current.TestOutputHelper?.WriteLine("DIRECTORY_LATE_RETIRE " + JsonSerializer.Serialize(new
        { count, shown = window.ShownEntries.Count, callbacks, callerEntries = result.Entries.Count, resultArrivesAfterClose = true }));
        GC.KeepAlive(window); GC.KeepAlive(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<Observed> Open(string kind, int count)
    {
        if (kind == "compare")
        {
            byte[] left = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, count).Select(i => $"owned-row-{i:D6}\n")));
            byte[] right = (byte[])left.Clone(); right[^2] = (byte)'x';
            var window = CompareWindow.Open("owned-left.txt", new MemoryContentSource("left", left), "owned-right.txt", new MemoryContentSource("right", right));
            await Wait(() => !window.IsComparing && Get<Task>(window, "_loading").IsCompleted && Get<Task>(window, "_runs").IsCompleted && !Get<bool>(window, "_checkingInputs"));
            return new Observed(window, Capture(window, "_leftText", "_rightText", "_descriptions", "_left", "_right", "_leftView", "_rightView").Concat(new[] { new WeakReference(window.Rows) }).ToArray());
        }
        var result = Result(count);
        if (kind == "sync")
        {
            var window = new SyncWindow(result, Context()); window.Show();
            return new Observed(window, Capture(window, "_comparison", "_items").Concat(new[] { new WeakReference(result.Entries), new WeakReference(result.Entries[0]) }).ToArray());
        }
        var directory = DirectoryDiffWindow.StartAsync("owned-left", "owned-right", "owned completed preview", (_, _) => Task.FromResult(result), (_, _) => { });
        await Wait(() => !directory.IsComparing);
        return new Observed(directory, Capture(directory, "_result").Concat(new[] { new WeakReference(result.Entries), new WeakReference(result.Entries[0]), new WeakReference(Get<ListBox>(directory, "_list").ItemsSource!) }).ToArray());
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (SyncWindow, SyncWindow, WeakReference[]) Borrowed(int count)
    {
        var result = Result(count);
        var first = new SyncWindow(result, Context()); var second = new SyncWindow(result, Context()); first.Show(); second.Show();
        return (first, second, new[] { new WeakReference(result), new WeakReference(result.Entries), new WeakReference(result.Entries[0]) });
    }
    private static SyncContext Context() => new("owned-left", "owned-right", true, true, true, true, false, static (_, _, _) => false);
    private static TreeCompareResult Result(int count) => new(Enumerable.Range(0, count).Select(i => new TreeDiffEntry($"owned-{i:D6}", TreeDiffKind.LeftOnly,
        new EntryData { Name = $"owned-{i:D6}", Kind = EntryKind.File, Size = i + 1, Modified = 638000000000000000 }, null)).ToArray(), true, 1);
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Fields)!.GetValue(target)!;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] Capture(object target, params string[] fields) => fields.Select(n => new WeakReference(target.GetType().GetField(n, Fields)!.GetValue(target)!)).ToArray();
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Readable(Window window, int count) => window switch
    {
        CompareWindow compare => compare.Rows.Count >= count,
        SyncWindow sync => sync.Items.Count == count,
        DirectoryDiffWindow diff => diff.ShownEntries.Count == count,
        _ => false,
    };
    private static async Task Wait(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(30), "Owned comparison fixture did not finish.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }
    private static async Task Drain()
    {
        Dispatcher.UIThread.RunJobs();
        await Task.Delay(60, TestContext.Current.CancellationToken);
        Dispatcher.UIThread.RunJobs();
    }
    private static void Collect()
    {
        for (int i = 0; i < 4; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Dispatcher.UIThread.RunJobs(); }
    }
}
