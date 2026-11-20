using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.App.Tests;

public sealed class StorageHistoryAccountingTests
{
    private static CancellationToken Stop => TestContext.Current.CancellationToken;
    private static StorageHistoryAccounting Result(long journals) => new(128, 2 * 1024 * 1024, journals, 1, 0, true, null);

    [AvaloniaFact]
    public async Task Stop_history_stays_visible_in_a_narrow_drawer_with_wrapped_status_and_an_unrelated_running_job()
    {
        var manager = new JobManager(new PortableFileOperations(), new ProviderRegistry(), Path.GetTempPath());
        var center = new OperationCenterViewModel(manager) { IsOpen = true, IsStorageHistoryAccounting = true, StorageAccountingStatus = new string('x', 1200) };
        var job = new Job(new JobRequest { Kind = JobKind.Copy }, "Unrelated copy", "device", [], []); job.SetState(JobState.Running);
        var row = new JobViewModel(job); row.Refresh(); center.Jobs.Add(row); center.UpdateSummary();
        var view = new OperationsView { DataContext = center }; var window = new Window { Width = 640, Height = 420, Content = view };
        try
        {
            window.Show();
            for (int i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); await Task.Delay(10, Stop); }
            var button = view.FindControl<Button>("StorageHistoryStop")!; var scroll = view.FindControl<ScrollViewer>("RecoveryScroll")!;
            Assert.True(button.IsEffectivelyVisible); Assert.True(button.Focus()); window.UpdateLayout();
            var top = button.TranslatePoint(default, scroll)!.Value;
            Assert.InRange(top.Y, -1, scroll.Viewport.Height); Assert.True(top.Y + button.Bounds.Height <= scroll.Viewport.Height + 1);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task Interactive_review_waits_for_actual_scan_retirement_and_prevents_replacement_until_its_lease_retires()
    {
        var testStop = Stop;
        using var io = new DeviceIoScheduler(hangThreshold: TimeSpan.FromSeconds(30));
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int calls = 0, owners = 0;
        using var runner = new StorageHistoryAccountingRunner(io, "evidence", ct =>
        {
            int call = Interlocked.Increment(ref calls); Interlocked.Increment(ref owners);
            try { if (call == 1) { started.Set(); release.Wait(testStop); } return Result(call); }
            finally { Interlocked.Decrement(ref owners); }
        });
        try
        {
            runner.Start(); Assert.True(started.Wait(TimeSpan.FromSeconds(5), Stop));
            // Other ordinary I/O can still use the device while accounting owns its background delegate.
            Assert.True(await io.Run("evidence", IoPriority.Interactive, _ => true, Stop));
            var foreground = runner.EnterInteractiveAsync(Stop);
            Assert.True(runner.State.Stopping); await Task.Delay(30, Stop);
            Assert.False(foreground.IsCompleted); Assert.Equal(1, Volatile.Read(ref owners));
            runner.Start(); Assert.Equal(1, Volatile.Read(ref calls)); release.Set();
            using (await foreground)
            {
                Assert.Equal(0, Volatile.Read(ref owners)); Assert.True(runner.State.Interactive); Assert.Null(runner.State.Result);
                runner.Start(); Assert.Equal(1, Volatile.Read(ref calls));
            }
            runner.Start(); await runner.WhenIdle();
            Assert.Equal(2, runner.State.Result!.Journals); Assert.False(runner.State.Running); Assert.Equal(0, owners);
        }
        finally { release.Set(); await runner.WhenIdle(); }
    }

    [Fact]
    public async Task Stop_and_dispose_keep_a_late_owner_running_until_it_really_returns()
    {
        var testStop = Stop;
        using var io = new DeviceIoScheduler(hangThreshold: TimeSpan.FromSeconds(30));
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        bool closed = false;
        using var runner = new StorageHistoryAccountingRunner(io, "evidence", _ =>
        {
            started.Set(); try { release.Wait(testStop); return Result(1); } finally { closed = true; }
        });
        try
        {
            runner.Start(); Assert.True(started.Wait(TimeSpan.FromSeconds(5), Stop));
            runner.Stop(); runner.Dispose();
            Assert.True(runner.State.Running); Assert.True(runner.State.Stopping); Assert.True(runner.State.UserStopped);
            Assert.False(closed); Assert.Null(runner.State.Result); release.Set(); await runner.WhenIdle();
            Assert.True(closed); Assert.False(runner.State.Running); Assert.Null(runner.State.Result);
        }
        finally { release.Set(); await runner.WhenIdle(); }
    }

    [Fact]
    public async Task Changed_history_discards_an_old_complete_inventory_and_refresh_can_retry_a_failed_scan()
    {
        var testStop = Stop;
        using var io = new DeviceIoScheduler(hangThreshold: TimeSpan.FromSeconds(30));
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int calls = 0;
        using var runner = new StorageHistoryAccountingRunner(io, "evidence", _ =>
        {
            int call = Interlocked.Increment(ref calls);
            if (call == 1) { started.Set(); release.Wait(testStop); return Result(100); }
            if (call == 2) throw new IOException("Evidence changed during accounting.");
            return Result(3);
        });
        try
        {
            runner.Start(); Assert.True(started.Wait(TimeSpan.FromSeconds(5), Stop)); runner.Invalidate(); release.Set(); await runner.WhenIdle();
            Assert.Null(runner.State.Result); Assert.True(runner.State.NeedsScan);
            runner.Start(); await runner.WhenIdle(); Assert.Contains("changed", runner.State.Error); Assert.False(runner.State.NeedsScan);
            runner.Start(); Assert.Equal(2, calls); // no automatic failure loop
            runner.Invalidate(refresh: true); runner.Start(); await runner.WhenIdle();
            Assert.Null(runner.State.Error); Assert.Equal(3, runner.State.Result!.Journals);
        }
        finally { release.Set(); await runner.WhenIdle(); }
    }

    [Fact]
    public async Task Throwing_observers_cannot_strand_admission_and_user_stop_requires_explicit_refresh()
    {
        using var io = new DeviceIoScheduler(); int calls = 0;
        using var runner = new StorageHistoryAccountingRunner(io, "evidence", _ => Result(Interlocked.Increment(ref calls)));
        runner.Changed += () => throw new InvalidOperationException("Fixture observer failure");
        runner.Start(); await runner.WhenIdle(); Assert.Equal(1, runner.State.Result!.Journals);
        runner.Stop(); runner.Invalidate(); runner.Start(); Assert.Equal(1, calls);
        runner.Invalidate(refresh: true); runner.Start(); await runner.WhenIdle(); Assert.Equal(2, runner.State.Result!.Journals);
    }

    [Fact]
    public async Task Canceled_foreground_wait_does_not_grant_early_access_or_abandon_the_background_owner()
    {
        var testStop = Stop;
        using var io = new DeviceIoScheduler(hangThreshold: TimeSpan.FromSeconds(30));
        using var started = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var runner = new StorageHistoryAccountingRunner(io, "evidence", _ => { started.Set(); release.Wait(testStop); return Result(1); });
        try
        {
            runner.Start(); Assert.True(started.Wait(TimeSpan.FromSeconds(5), Stop));
            using var cancel = new CancellationTokenSource(); var foreground = runner.EnterInteractiveAsync(cancel.Token); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => foreground);
            Assert.True(runner.State.Running); Assert.False(runner.State.Interactive); Assert.Null(runner.State.Result);
            release.Set(); await runner.WhenIdle(); Assert.Null(runner.State.Result);
        }
        finally { release.Set(); await runner.WhenIdle(); }
    }
}
