using System.Diagnostics;
using System.Reflection;
using FileCat.Core.Threading;
using FileCat.Recovery.Unix;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

/// <summary>
/// I684: a device call that waits for a person (the system's dialog asking whether FileCat may read a drive) is not a
/// device that stopped responding. Its hang clock stands still while it waits and restarts at the answer.
/// </summary>
public sealed class DeviceIoUserWaitTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    // Only these tests' scheduler: its real timer is stopped, and each watchdog checkpoint is the test's.
    private static DeviceIoScheduler ManualScheduler()
    {
        var io = new DeviceIoScheduler(TimeSpan.FromMilliseconds(1), threadsPerDevice: 1, maxThreadsPerDevice: 2);
        ((Timer)typeof(DeviceIoScheduler).GetField("_watchdog", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(io)!)
            .Change(Timeout.Infinite, Timeout.Infinite);
        return io;
    }

    private static void Watch(DeviceIoScheduler io) =>
        typeof(DeviceIoScheduler).GetMethod("Watch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(io, null);

    [Fact]
    public async Task A_call_waiting_for_a_person_is_not_reported_not_responding_and_is_measured_again_after_the_answer()
    {
        using var io = ManualScheduler();
        using var answer = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int queuedCalls = 0;
        Task? call = null;
        Task? queued = null;
        try
        {
            call = io.Run("drive", IoPriority.Interactive, _ =>
            {
                using (DeviceIoScheduler.AwaitingUser())
                {
                    waiting.TrySetResult();
                    if (!answer.Wait(Patience)) throw new TimeoutException("The test did not answer.");
                }
                answered.TrySetResult();
                if (!release.Wait(Patience)) throw new TimeoutException("The test did not release the call.");
            }, TestContext.Current.CancellationToken);
            await waiting.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            queued = io.Run("drive", IoPriority.Interactive, _ => { Interlocked.Increment(ref queuedCalls); }, TestContext.Current.CancellationToken);
            await Task.Delay(30, TestContext.Current.CancellationToken);

            // Far beyond the 1 ms threshold, yet waiting for a person: no notice, and no replacement worker for a hung call.
            Watch(io);
            Watch(io);
            Assert.Equal(DeviceHealth.Responsive, io.GetHealth("drive"));
            await Task.Delay(30, TestContext.Current.CancellationToken);
            Assert.Equal(0, Volatile.Read(ref queuedCalls));
            Assert.Equal(DeviceHealth.Responsive, io.GetHealth("drive"));

            // After the answer the call is the drive's again, and a call that then stops answering is reported.
            answer.Set();
            await answered.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            await Task.Delay(30, TestContext.Current.CancellationToken);
            Watch(io);
            Assert.Equal(DeviceHealth.NotResponding, io.GetHealth("drive"));
            await queued.WaitAsync(Patience, TestContext.Current.CancellationToken); // the replacement worker ran it
            Assert.Equal(1, Volatile.Read(ref queuedCalls));
        }
        finally
        {
            answer.Set();
            release.Set();
            foreach (var task in new[] { call, queued }.OfType<Task>())
                await task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        }
        var deadline = Stopwatch.StartNew();
        while (io.GetHealth("drive") != DeviceHealth.Responsive && deadline.Elapsed < Patience)
            await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.Equal(DeviceHealth.Responsive, io.GetHealth("drive"));
    }

    [Theory]
    [InlineData("task")]
    [InlineData("await")]
    public async Task A_wait_for_a_person_in_work_the_call_waits_for_on_another_thread_stops_the_calls_clock(string how)
    {
        using var io = ManualScheduler();
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool? onWorker = null;
        async Task AskAsync()
        {
            await Task.Yield(); // continues on a pool thread, as an awaited sign-in dialog does
            using (DeviceIoScheduler.AwaitingUser())
            {
                onWorker = DeviceIoScheduler.IsDeviceWorker;
                waiting.TrySetResult();
                await answer.Task.WaitAsync(Patience);
            }
        }
        void Ask()
        {
            using (DeviceIoScheduler.AwaitingUser())
            {
                onWorker = DeviceIoScheduler.IsDeviceWorker;
                waiting.TrySetResult();
                if (!answer.Task.Wait(Patience)) throw new TimeoutException("The test did not answer.");
            }
        }
        Task? call = null;
        try
        {
            // As a provider's listing does: the device call waits while other threads connect and ask the person.
            call = io.Run("server", IoPriority.Interactive, _ =>
            {
                if (how == "task") Task.Run(Ask).GetAwaiter().GetResult();
                else AskAsync().GetAwaiter().GetResult();
            }, TestContext.Current.CancellationToken);
            await waiting.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            Assert.False(onWorker);
            await Task.Delay(30, TestContext.Current.CancellationToken);
            Watch(io);
            Assert.Equal(DeviceHealth.Responsive, io.GetHealth("server"));
        }
        finally
        {
            answer.TrySetResult();
            if (call is not null) await call.WaitAsync(Patience, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Devices_observed_by_a_call_wait_with_it_and_are_measured_again_after_the_answer()
    {
        using var io = ManualScheduler();
        using var answer = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        static void WatchObserved(DeviceIoScheduler scheduler) =>
            typeof(DeviceIoScheduler).GetMethod("WatchObserved", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(scheduler, null);
        Task? call = null;
        try
        {
            // As an analysis does: the call observes the devices it reads, then a folder read waits for macOS's question.
            call = io.Run("analysis", IoPriority.Background, _ =>
            {
                using var observed = io.ObserveActualDevices(["volume"]);
                using (DeviceIoScheduler.AwaitingUser())
                {
                    waiting.TrySetResult();
                    if (!answer.Wait(Patience)) throw new TimeoutException("The test did not answer.");
                }
                answered.TrySetResult();
                if (!release.Wait(Patience)) throw new TimeoutException("The test did not release the call.");
            }, TestContext.Current.CancellationToken);
            await waiting.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            await Task.Delay(30, TestContext.Current.CancellationToken);
            WatchObserved(io);
            Assert.Equal(DeviceHealth.Responsive, io.GetHealth("volume"));

            answer.Set();
            await answered.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
            await Task.Delay(30, TestContext.Current.CancellationToken);
            WatchObserved(io);
            Assert.Equal(DeviceHealth.NotResponding, io.GetHealth("volume"));
        }
        finally
        {
            answer.Set();
            release.Set();
            if (call is not null) await call.WaitAsync(Patience, TestContext.Current.CancellationToken);
        }
        Assert.Equal(DeviceHealth.Responsive, io.GetHealth("volume"));
    }

    [Fact]
    public async Task The_hang_clock_restarts_when_the_last_wait_for_a_person_ends()
    {
        using var io = ManualScheduler();
        var stamps = await io.Run("drive", IoPriority.Interactive, _ =>
        {
            long started = DeviceIoScheduler.CurrentCallClock ?? throw new InvalidOperationException("No device call on this thread.");
            Assert.False(DeviceIoScheduler.IsAwaitingUser);
            long innerEnded, outerEnding;
            var outer = DeviceIoScheduler.AwaitingUser();
            using (DeviceIoScheduler.AwaitingUser())
            {
                Thread.Sleep(5);
                Assert.True(DeviceIoScheduler.IsAwaitingUser);
            }
            innerEnded = DeviceIoScheduler.CurrentCallClock!.Value;
            Assert.True(DeviceIoScheduler.IsAwaitingUser); // the outer wait still holds the clock
            Thread.Sleep(5);
            outerEnding = Stopwatch.GetTimestamp();
            outer.Dispose();
            long restarted = DeviceIoScheduler.CurrentCallClock!.Value;
            Assert.False(DeviceIoScheduler.IsAwaitingUser);
            outer.Dispose(); // a second disposal changes nothing
            Assert.False(DeviceIoScheduler.IsAwaitingUser);
            Assert.Equal(restarted, DeviceIoScheduler.CurrentCallClock!.Value);
            using (DeviceIoScheduler.AwaitingUser()) Assert.True(DeviceIoScheduler.IsAwaitingUser);
            Assert.False(DeviceIoScheduler.IsAwaitingUser);
            return (started, innerEnded, outerEnding, restarted);
        }, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.True(stamps.innerEnded > stamps.started);
        Assert.True(stamps.restarted >= stamps.outerEnding, "The clock restarts when the last wait ends, not before.");
    }

    [Fact]
    public void Outside_a_device_call_a_wait_for_a_person_changes_nothing()
    {
        Assert.Null(DeviceIoScheduler.CurrentCallClock);
        using (DeviceIoScheduler.AwaitingUser())
        {
            Assert.False(DeviceIoScheduler.IsAwaitingUser);
            Assert.Null(DeviceIoScheduler.CurrentCallClock);
        }
    }

    [Fact]
    public async Task Each_call_has_its_own_clock_and_a_finished_call_leaves_no_wait_behind()
    {
        using var io = ManualScheduler();
        // A wait still open when its call returns belongs to that call only.
        await io.Run("drive", IoPriority.Interactive, token => { _ = DeviceIoScheduler.AwaitingUser(); }, TestContext.Current.CancellationToken)
            .WaitAsync(Patience, TestContext.Current.CancellationToken);
        bool next = await io.Run("drive", IoPriority.Interactive, _ => DeviceIoScheduler.IsAwaitingUser, TestContext.Current.CancellationToken)
            .WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.False(next);
    }

    [Fact]
    public async Task Unix_drive_opens_measure_the_drive_and_not_the_persons_answer()
    {
        using var io = ManualScheduler();
        using var dir = new TempDir();
        string image = Path.Combine(dir.Path, "drive.img");
        File.WriteAllBytes(image, new byte[512]);
        SafeFileHandle Open(string path) => File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // This user may read the drive: nothing asks the system, and the open is measured.
        var direct = await io.Run("drive", IoPriority.Interactive, ct =>
        {
            bool waitedDirect = true, asked = false;
            using var handle = UnixDeviceSource.OpenHandle(image, ct,
                (path, _) => { waitedDirect = DeviceIoScheduler.IsAwaitingUser; return Open(path); },
                (path, _) => { asked = true; return Open(path); });
            return (waitedDirect, asked);
        }, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.False(direct.waitedDirect);
        Assert.False(direct.asked);

        // The user may not: the system asks the person, and only that request is a wait for the person.
        var authorized = await io.Run("drive", IoPriority.Interactive, ct =>
        {
            bool waitedDirect = true, waitedAsking = false;
            using var handle = UnixDeviceSource.OpenHandle(image, ct,
                (_, _) => { waitedDirect = DeviceIoScheduler.IsAwaitingUser; throw new UnauthorizedAccessException(); },
                (path, _) => { waitedAsking = DeviceIoScheduler.IsAwaitingUser; return Open(path); });
            return (waitedDirect, waitedAsking, after: DeviceIoScheduler.IsAwaitingUser);
        }, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.False(authorized.waitedDirect);
        Assert.True(authorized.waitedAsking);
        Assert.False(authorized.after);

        // A refused or dismissed request ends the wait too.
        bool afterRefusal = await io.Run("drive", IoPriority.Interactive, ct =>
        {
            Assert.Throws<OperationCanceledException>(() => UnixDeviceSource.OpenHandle(image, ct,
                (_, _) => throw new UnauthorizedAccessException(),
                (path, _) => throw new OperationCanceledException($"Reading {path} was not approved.")));
            return DeviceIoScheduler.IsAwaitingUser;
        }, TestContext.Current.CancellationToken).WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.False(afterRefusal);
    }
}
