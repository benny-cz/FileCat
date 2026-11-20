using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisIoTests
{
    [Fact]
    public async Task ReversedDevicePairsShareBothTurnsAcrossSchedulers()
    {
        Ordinary(); using var a = new DeviceIoScheduler(); using var b = new DeviceIoScheduler();
        string first = Key(), second = Key();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var held = AnalysisIo.Run(a, new(first, second), _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return 1; }, Cancellation);
        Task<int>? reversed = null, single = null; int calls = 0;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation));
            reversed = AnalysisIo.Run(b, new(second, first), _ => Interlocked.Increment(ref calls), Cancellation);
            single = AnalysisIo.Run(b, second, _ => Interlocked.Increment(ref calls), Cancellation);
            await Task.Delay(40, Cancellation); Assert.Equal(0, Volatile.Read(ref calls));
        }
        finally { release.Set(); await held.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); }
        await Task.WhenAll(reversed!, single!).WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task CancelingASetWaitingForItsSecondTurnReturnsItsFirstTurn()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var cancel = new CancellationTokenSource();
        string prefix = Key(), first = prefix + "-a", second = prefix + "-z";
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); int calls = 0;
        var held = AnalysisIo.Run(io, second, _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return 1; }, Cancellation);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation));
            var waiting = AnalysisIo.Run(io, new(first, second), _ => Interlocked.Increment(ref calls), cancel.Token);
            // Admit is synchronous through the first free turn before it awaits the held second turn.
            Assert.False(waiting.IsCompleted); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5), Cancellation));
            Assert.Equal(0, calls);
            Assert.Equal(42, await AnalysisIo.Run(io, first, _ => 42, Cancellation).WaitAsync(TimeSpan.FromSeconds(5), Cancellation));
            Assert.False(held.IsCompleted);
        }
        finally { release.Set(); await held.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); }
        Assert.Equal(1, await AnalysisIo.Run(io, new(second, first), _ => Interlocked.Increment(ref calls), Cancellation));
    }

    [Fact]
    public async Task EquivalentKeysTakeOneTurnAndRetireTheirRegistryEntry()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); string key = Key();
        Assert.Single(new AnalysisIoDevices(key, key.ToUpperInvariant()).Keys);
        Assert.Equal(7, await AnalysisIo.Run(io, new(key, key.ToUpperInvariant()), _ => 7, Cancellation).WaitAsync(TimeSpan.FromSeconds(5), Cancellation));
        var field = typeof(AnalysisIo).GetField("Devices", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var dictionary = Assert.IsAssignableFrom<System.Collections.IDictionary>(field.GetValue(null));
        Assert.False(dictionary.Contains(key));
    }

    [Fact]
    public async Task ActualStallQuarantinesBothDevicesAndCancellationKeepsThemHeldUntilReturn()
    {
        Ordinary(); using var io = new DeviceIoScheduler(TimeSpan.FromMilliseconds(20)); using var cancel = new CancellationTokenSource();
        string primary = Key(), related = Key(), healthy = Key();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var unhealthy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        io.HealthChanged += (key, health) => { if (key == related && health == DeviceHealth.NotResponding) unhealthy.TrySetResult(); };
        var held = AnalysisIo.Run(io, new(primary, related), _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return 19; }, cancel.Token);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation));
            await unhealthy.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            cancel.Cancel(); Assert.False(held.IsCompleted);
            Assert.Equal(DeviceHealth.NotResponding, io.GetHealth(primary)); Assert.Equal(DeviceHealth.NotResponding, io.GetHealth(related));
            await Assert.ThrowsAsync<AnalysisDeviceUnavailableException>(() => AnalysisIo.Run(io, related, _ => 0, Cancellation));
            await Assert.ThrowsAsync<AnalysisDeviceUnavailableException>(() => AnalysisIo.Run(io, new(healthy, related), _ => 0, Cancellation));
            Assert.Equal(23, await AnalysisIo.Run(io, healthy, _ => 23, Cancellation));
        }
        finally { release.Set(); Assert.Equal(19, await held.WaitAsync(TimeSpan.FromSeconds(5), Cancellation)); }
        var until = DateTime.UtcNow.AddSeconds(5);
        while (io.GetHealth(primary) != DeviceHealth.Responsive && DateTime.UtcNow < until) await Task.Delay(10, Cancellation);
        Assert.Equal(DeviceHealth.Responsive, io.GetHealth(primary)); Assert.Equal(DeviceHealth.Responsive, io.GetHealth(related));
        Assert.Equal(31, await AnalysisIo.Run(io, new(related, primary), _ => 31, Cancellation));
    }

    [Fact]
    public async Task ShutdownCleanupRetainsBothDeviceTurnsAndClosesExactlyOnce()
    {
        Ordinary();
        using var stopped = new DeviceIoScheduler(); stopped.Dispose(); using var other = new DeviceIoScheduler();
        string primary = Key(), related = Key(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int calls = 0, nextCalls = 0;
        var owned = new PairCleanup(() => { Interlocked.Increment(ref calls); entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
        var closing = AnalysisIo.ReleaseScratchAsync(stopped, new(primary, related), owned);
        Task<int>? next = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation));
            next = AnalysisIo.Run(other, related, _ => Interlocked.Increment(ref nextCalls), Cancellation);
            await Task.Delay(40, Cancellation); Assert.False(closing.IsCompleted); Assert.Equal(0, nextCalls); Assert.Equal(1, calls);
        }
        finally { release.Set(); await closing.WaitAsync(TimeSpan.FromSeconds(5), Cancellation); }
        Assert.Equal(1, await next!.WaitAsync(TimeSpan.FromSeconds(5), Cancellation)); Assert.Equal(1, calls);
    }
    private sealed class PairCleanup(Action close) : IAnalysisScratchOwned { public void Dispose() => close(); }
}
