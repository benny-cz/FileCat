using System.Collections.Concurrent;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FileCat.App.Services;
using FileCat.App.Views;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

namespace FileCat.App.Tests;

public sealed class StorageCallEvidenceUiTests
{
    private static readonly StorageJournalPrefix Prefix = new(Guid.NewGuid(), 4, 1234, new string('A', 64));
    private static StorageCallEvidencePage Page(long after, string name = "pending")
    {
        var before = new StorageObservation(StorageObservationState.Disabled, StorageObservationState.Disabled, new(1, "held"), false, null);
        return new(Prefix, [new(after + 1, 0, @"C:\source\" + name, before.Identity, before, StorageObservationState.Enabled,
            null, null, null)], after == 0, after, after + 1, 1024);
    }
    private static Button Button(ReportWindow window, string label) => window.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == label);
    private static async Task Wait(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew();
        while (!ready())
        {
            Dispatcher.UIThread.RunJobs();
            if (timer.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("Call evidence UI did not reach its checkpoint.");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    [AvaloniaFact]
    public async Task Whole_prefix_export_waits_for_display_and_keeps_its_ignoring_owner_until_return_after_close()
    {
        var summary = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int started = 0, owners = 0; CancellationToken observed = default;
        var window = new ReportWindow("Captured evidence", "Storage", _ => summary.Task) { Width = 640, Height = 420 };
        window.ConfigureStorageCallExport(async ct =>
        {
            observed = ct; started++; Interlocked.Increment(ref owners);
            try { await finish.Task; } finally { Interlocked.Decrement(ref owners); }
        });
        try
        {
            window.Show(); await Wait(() => window.Reading is not null);
            var save = Button(window, "Save all recorded calls…");
            save.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Assert.Equal(0, started);
            summary.SetResult("Immutable storage summary"); await window.Reading!;
            save.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Wait(() => owners == 1); Assert.False(save.IsEnabled);
            save.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Assert.Equal(1, started);
            var exporting = Assert.IsAssignableFrom<Task>(window.StorageExporting);
            window.Close(); Assert.True(observed.IsCancellationRequested); Assert.False(exporting.IsCompleted); Assert.Equal(1, owners);
            finish.SetResult(); await exporting; Assert.Equal(0, owners);
        }
        finally { summary.TrySetResult("Closed"); finish.TrySetResult(); window.Close(); if (window.StorageExporting is { } exporting) await exporting; }
    }

    [AvaloniaFact]
    public async Task Exact_prefix_navigation_replaces_one_page_and_labels_pending_intent_without_a_crash_claim()
    {
        var requests = new ConcurrentQueue<long>(); int owners = 0;
        var window = ReportWindow.CreateStorageCalls("Storage", Prefix, true, (after, _) =>
        {
            owners++; requests.Enqueue(after); owners--;
            return Task.FromResult(Page(after, "line\n\"name"));
        });
        window.Width = 640; window.Height = 420;
        try
        {
            window.Show(); await Wait(() => window.Reading?.IsCompleted == true && Button(window, "Next recorded call page").IsEnabled);
            Assert.Equal(0, owners); Assert.Contains("Pending at this active snapshot", window.Text);
            Assert.Contains(System.Text.Json.JsonSerializer.Serialize(@"C:\source\" + "line\n\"name"), window.Text);
            Assert.DoesNotContain("crash", window.Text, StringComparison.OrdinalIgnoreCase);
            var next = Button(window, "Next recorded call page"); Assert.True(next.Focus());
            next.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Wait(() => window.Reading?.IsCompleted == true && window.Text.Contains("Displayed calls 2–2", StringComparison.Ordinal));
            Assert.False(next.IsEnabled); Assert.DoesNotContain("Call 1;", window.Text); Assert.Equal(0, owners);
            Button(window, "First recorded call page").RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            await Wait(() => window.Reading?.IsCompleted == true && next.IsEnabled);
            Assert.Equal(new long[] { 0, 1, 0 }, requests.ToArray());
            Assert.True(Button(window, "Save this call page…").IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Closing_a_window_keeps_an_ignoring_producer_owned_until_its_actual_return()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int owners = 0;
        var window = ReportWindow.CreateStorageCalls("Storage", Prefix, true, async (_, _) =>
        {
            Interlocked.Increment(ref owners);
            try { await release.Task; return Page(0); }
            finally { Interlocked.Decrement(ref owners); }
        });
        try
        {
            window.Show(); await Wait(() => Volatile.Read(ref owners) == 1); var read = Assert.IsAssignableFrom<Task>(window.Reading);
            window.Close(); Assert.False(read.IsCompleted); Assert.Equal(1, Volatile.Read(ref owners));
            release.SetResult(); await read; Assert.Equal(0, owners); Assert.DoesNotContain("Call 1;", window.Text);
        }
        finally { release.TrySetResult(); window.Close(); if (window.Reading is { } read) await read; }
    }

    [AvaloniaFact]
    public async Task A_late_canceled_page_cannot_overwrite_a_newer_refresh_or_enable_its_navigation()
    {
        var older = new TaskCompletionSource<StorageCallEvidencePage>(TaskCreationOptions.RunContinuationsAsynchronously); int requests = 0;
        var window = ReportWindow.CreateStorageCalls("Storage", Prefix, true, (_, _) =>
            Interlocked.Increment(ref requests) == 1 ? older.Task : Task.FromResult(Page(0, "newer") with { More = false }));
        try
        {
            window.Show(); await Wait(() => Volatile.Read(ref requests) == 1);
            window.KeyPress(Key.F5, Avalonia.Input.RawInputModifiers.None, PhysicalKey.F5, null);
            await Wait(() => window.Reading?.IsCompleted == true && window.Text.Contains("newer", StringComparison.Ordinal));
            older.SetResult(Page(0, "older")); await Task.Delay(30, TestContext.Current.CancellationToken); Dispatcher.UIThread.RunJobs();
            Assert.Contains("newer", window.Text); Assert.DoesNotContain("older", window.Text);
            Assert.False(Button(window, "Next recorded call page").IsEnabled);
        }
        finally { older.TrySetResult(Page(0)); window.Close(); if (window.Reading is { } read) await read; }
    }
}
