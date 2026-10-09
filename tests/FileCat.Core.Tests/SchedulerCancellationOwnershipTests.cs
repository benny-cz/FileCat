using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class SchedulerCancellationOwnershipTests(ITestOutputHelper output)
{
    private sealed class Probe { public int Calls; public int CheckedBytes; }
    private sealed class Payload(byte[] bytes)
    {
        public int Check()
        {
            Assert.Equal(32768, bytes.Length);
            for (int i = 0; i < bytes.Length; i++) Assert.Equal((byte)(i * 31 + 7), bytes[i]);
            return bytes.Length;
        }
    }
    private sealed record Demand(WeakReference Owner, Task<int> Task);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Demand Queue(DeviceIoScheduler io, string path, CancellationToken token, Probe probe)
    {
        var payload = new Payload(File.ReadAllBytes(path));
        var weak = new WeakReference(payload);
        var task = io.Run("owned-held", IoPriority.Normal, _ =>
        {
            Interlocked.Increment(ref probe.Calls);
            int bytes = payload.Check();
            Interlocked.Add(ref probe.CheckedBytes, bytes);
            return bytes;
        }, token);
        return new Demand(weak, task);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Demand Running(DeviceIoScheduler io, string path, CancellationToken token, Probe probe,
        ManualResetEventSlim entered, ManualResetEventSlim release, ManualResetEventSlim finished)
    {
        var payload = new Payload(File.ReadAllBytes(path));
        var weak = new WeakReference(payload);
        var task = io.Run("owned-running", IoPriority.Normal, _ =>
        {
            try
            {
                Interlocked.Increment(ref probe.Calls); entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned running callback not released");
                int bytes = payload.Check(); Interlocked.Add(ref probe.CheckedBytes, bytes); return bytes;
            }
            finally { finished.Set(); }
        }, token);
        return new Demand(weak, task);
    }

    private static int CollectAndCount(IEnumerable<Demand> demands)
    {
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        return demands.Count(d => d.Owner.IsAlive);
    }
    private static string WriteOwned(string root)
    {
        string path = Path.Combine(root, "owned.bin");
        File.WriteAllBytes(path, Enumerable.Range(0, 32768).Select(i => (byte)(i * 31 + 7)).ToArray());
        return path;
    }

    [Theory]
    [InlineData("cancel", 1)] [InlineData("cancel", 16)] [InlineData("cancel", 64)]
    [InlineData("shutdown", 1)] [InlineData("shutdown", 16)] [InlineData("shutdown", 64)]
    [InlineData("already-cancelled", 1)] [InlineData("already-cancelled", 16)] [InlineData("already-cancelled", 64)]
    [InlineData("live", 1)] [InlineData("live", 16)] [InlineData("live", 64)]
    public void Retired_queued_callbacks_release_captured_bytes_while_a_device_call_is_held(string route, int count)
    {
        using var dir = new TempDir(); string path = WriteOwned(dir.Path); byte[] original = File.ReadAllBytes(path);
        using var io = new DeviceIoScheduler(TimeSpan.FromHours(1), threadsPerDevice: 1, maxThreadsPerDevice: 1);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var probe = new Probe(); var tokens = Enumerable.Range(0, count).Select(_ => new CancellationTokenSource()).ToArray();
        var demands = new List<Demand>(); Task? held = null;
        try
        {
            held = io.Run("owned-held", IoPriority.Interactive, _ =>
            { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Owned held callback not released"); });
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            if (route == "already-cancelled") foreach (var stop in tokens) stop.Cancel();
            foreach (var stop in tokens) demands.Add(Queue(io, path, stop.Token, probe));
            if (route == "cancel") foreach (var stop in tokens) stop.Cancel();
            if (route == "shutdown") io.Dispose();
            int retainedWhileHeld = CollectAndCount(demands);
            Assert.Equal(0, probe.Calls); Assert.False(held.IsCompleted);
            Assert.All(demands, d => Assert.Equal(route != "live", d.Task.IsCanceled));
            release.Set(); held.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            if (route == "live") foreach (var d in demands) Assert.Equal(32768, d.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult());
            // Drain every remaining cancelled slot before the completed-lifetime observation. External CTS remain alive.
            if (route != "shutdown") io.Run("owned-held", IoPriority.Background, _ => { }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            io.Dispose();
            int retainedAfterDrain = CollectAndCount(demands);
            Assert.Equal(route == "live" ? count : 0, probe.Calls); Assert.Equal(probe.Calls * 32768, probe.CheckedBytes);
            Assert.Equal(original, File.ReadAllBytes(path));
            output.WriteLine("SCHEDULER_CANCEL_OWNERSHIP " + JsonSerializer.Serialize(new { route, count, retainedWhileHeld, retainedAfterDrain, probe.Calls, probe.CheckedBytes,
                sourceBytes = original.Length, sourceSHA256 = Convert.ToHexStringLower(SHA256.HashData(original)), root = dir.Path,
                externalCancellationSourcesStillAlive = true, boundedExplicitCollectionNotProcessPeak = true, heldDeviceReleasedAndLiveKnownBytesVerified = true }));
            GC.KeepAlive(tokens); GC.KeepAlive(io); GC.KeepAlive(demands);
            Assert.Equal(route == "live" ? count : 0, retainedWhileHeld); Assert.Equal(0, retainedAfterDrain);
        }
        finally
        {
            release.Set(); io.Dispose(); foreach (var stop in tokens) stop.Dispose();
            if (held is not null) held.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
    }

    [Theory] [InlineData(1)] [InlineData(4)] [InlineData(16)]
    public void Cancelling_running_work_preserves_its_captured_bytes_until_the_callback_returns(int repetitions)
    {
        using var dir = new TempDir(); string path = WriteOwned(dir.Path); byte[] original = File.ReadAllBytes(path);
        using var io = new DeviceIoScheduler(TimeSpan.FromHours(1), threadsPerDevice: 1, maxThreadsPerDevice: 1);
        var probe = new Probe(); var stops = new List<CancellationTokenSource>(); var demands = new List<Demand>();
        try
        {
            for (int i = 0; i < repetitions; i++)
            {
                using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); using var finished = new ManualResetEventSlim();
                var stop = new CancellationTokenSource(); stops.Add(stop);
                try
                {
                    var demand = Running(io, path, stop.Token, probe, entered, release, finished); demands.Add(demand);
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5))); stop.Cancel(); Assert.True(demand.Task.IsCanceled);
                    Assert.True(demand.Owner.IsAlive); // running callback still needs the payload at its safe boundary
                }
                finally { release.Set(); Assert.True(finished.Wait(TimeSpan.FromSeconds(5))); }
                io.Run("owned-running", IoPriority.Background, _ => { }).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            }
            io.Dispose(); int retainedAfterDrain = CollectAndCount(demands);
            Assert.Equal(repetitions, probe.Calls); Assert.Equal(repetitions * 32768, probe.CheckedBytes); Assert.Equal(original, File.ReadAllBytes(path));
            output.WriteLine("SCHEDULER_RUNNING_OWNERSHIP " + JsonSerializer.Serialize(new { repetitions, retainedAfterDrain, probe.Calls, probe.CheckedBytes,
                sourceBytes = original.Length, sourceSHA256 = Convert.ToHexStringLower(SHA256.HashData(original)), root = dir.Path, externalCancellationSourcesStillAlive = true, safeBoundaryKnownBytesVerified = true }));
            GC.KeepAlive(stops); GC.KeepAlive(io); GC.KeepAlive(demands); Assert.Equal(0, retainedAfterDrain);
        }
        finally { io.Dispose(); foreach (var stop in stops) stop.Dispose(); }
    }
}
