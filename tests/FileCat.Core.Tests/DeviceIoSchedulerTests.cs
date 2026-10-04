using System.Reflection;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class DeviceIoSchedulerTests
{
    [Fact]
    public async Task The_real_watchdog_replaces_a_held_call_and_a_healthy_device_keeps_working()
    {
        using var io = new DeviceIoScheduler(TimeSpan.FromMilliseconds(20), threadsPerDevice: 1, maxThreadsPerDevice: 2);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? held = null;
        try
        {
            held = io.Run("held", IoPriority.Interactive, _ =>
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned held call was not released");
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // One base worker is occupied, so this completes only after the real timer adds a replacement.
            int replacement = await io.Run("held", IoPriority.Interactive, _ => 17).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(17, replacement);
            Assert.False(held.IsCompleted);
            Assert.Equal(DeviceHealth.NotResponding, io.GetHealth("held"));
            Assert.Equal(42, await io.Run("healthy", IoPriority.Interactive, _ => 42).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(DeviceHealth.Responsive, io.GetHealth("healthy"));
        }
        finally
        {
            release.Set();
            if (held is not null) await held.WaitAsync(TimeSpan.FromSeconds(5));
        }
        // The returned call no longer contributes to the device's unhealthy state.
        for (int i = 0; i < 100 && io.GetHealth("held") != DeviceHealth.Responsive; i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(DeviceHealth.Responsive, io.GetHealth("held"));
    }

    [Fact]
    public async Task The_worker_cap_keeps_live_work_queued_and_drops_canceled_work_until_a_call_returns()
    {
        using var io = new DeviceIoScheduler(TimeSpan.FromMilliseconds(1), threadsPerDevice: 1, maxThreadsPerDevice: 2);
        // Control only this scheduler's watchdog checkpoints. The first test exercises its actual timer.
        ((Timer)typeof(DeviceIoScheduler).GetField("_watchdog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(io)!)
            .Change(Timeout.Infinite, Timeout.Infinite);
        using var release = new ManualResetEventSlim();
        using var abandonedStop = new CancellationTokenSource();
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0, peak = 0, abandonedCalls = 0, liveCalls = 0;
        void Enter()
        {
            int count = Interlocked.Increment(ref active), old;
            do { old = Volatile.Read(ref peak); }
            while (count > old && Interlocked.CompareExchange(ref peak, count, old) != old);
        }
        void Hold(TaskCompletionSource entered)
        {
            Enter(); entered.TrySetResult();
            try { if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned held call was not released"); }
            finally { Interlocked.Decrement(ref active); }
        }
        static void Watch(DeviceIoScheduler scheduler) =>
            typeof(DeviceIoScheduler).GetMethod("Watch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scheduler, null);
        Task? first = null, second = null, abandoned = null;
        Task<int>? live = null;
        try
        {
            first = io.Run("capped", IoPriority.Interactive, _ => Hold(firstEntered));
            await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second = io.Run("capped", IoPriority.Interactive, _ => Hold(secondEntered));
            await Task.Delay(5, TestContext.Current.CancellationToken);
            Watch(io);
            await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(5, TestContext.Current.CancellationToken);
            Watch(io); // both slots now hold quarantined calls

            abandoned = io.Run("capped", IoPriority.Interactive, _ => Interlocked.Increment(ref abandonedCalls), abandonedStop.Token);
            live = io.Run("capped", IoPriority.Interactive, _ =>
            {
                Enter(); liveEntered.TrySetResult();
                try { Interlocked.Increment(ref liveCalls); return 99; }
                finally { Interlocked.Decrement(ref active); }
            });
            abandonedStop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
            Assert.Equal(42, await io.Run("healthy", IoPriority.Interactive, _ => 42).WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.NotSame(liveEntered.Task, await Task.WhenAny(liveEntered.Task, Task.Delay(500, TestContext.Current.CancellationToken)));
            Assert.Equal(2, Volatile.Read(ref active));
            Assert.Equal(2, Volatile.Read(ref peak));
            Assert.Equal(0, Volatile.Read(ref abandonedCalls));

            release.Set();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(99, await live.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, Volatile.Read(ref liveCalls));
            Assert.Equal(0, Volatile.Read(ref abandonedCalls));
            Assert.Equal(0, Volatile.Read(ref active));
            Assert.Equal(2, Volatile.Read(ref peak));
        }
        finally
        {
            abandonedStop.Cancel();
            release.Set();
            foreach (var task in new[] { first, second, live }.OfType<Task>())
                await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
