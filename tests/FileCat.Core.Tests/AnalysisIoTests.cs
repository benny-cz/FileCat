using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

[CollectionDefinition("Analysis I/O capacity", DisableParallelization = true)]
public sealed class AnalysisIoCapacityCollection;

[Collection("Analysis I/O capacity")]
public sealed partial class AnalysisIoTests
{
    [Fact]
    public async Task PrivateDirectoryReleaseKeepsItsActualWorkerThroughSchedulerShutdown()
    {
        using var io = new DeviceIoScheduler(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        int caller = Environment.CurrentManagedThreadId;
        var directory = new ClosingDirectory(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
        var closing = AnalysisIo.ReleaseAsync(io, Key(), directory);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)); io.Dispose();
            Assert.False(closing.IsCompleted); Assert.Equal(1, directory.Calls); Assert.NotEqual(caller, directory.Thread);
        }
        finally { release.Set(); await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken); }
        Assert.Equal(1, directory.Calls);
    }
    private sealed class ClosingDirectory(Action close) : AnalysisPrivateDirectory
    {
        internal int Calls, Thread;
        internal override void Verify() => throw new NotSupportedException();
        internal override (UInt128 Capacity, UInt128 Available) Space() => throw new NotSupportedException();
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { Thread = Environment.CurrentManagedThreadId; Interlocked.Increment(ref Calls); close(); }
    }
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static string Key() => "analysis-io-fixture-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task UnhealthyDeviceRefusesNewWorkWithoutAbandoningItsActualRequest()
    {
        Ordinary(); using var io = new DeviceIoScheduler(TimeSpan.FromMilliseconds(40));
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string key = Key(); int extraEntered = 0;
        var actual = AnalysisIo.Run(io, key, _ => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(15)); return 17; }, Cancellation);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Cancellation);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (io.GetHealth(key) != DeviceHealth.NotResponding && DateTime.UtcNow < deadline) await Task.Delay(10, Cancellation);
            Assert.Equal(DeviceHealth.NotResponding, io.GetHealth(key)); Assert.False(actual.IsCompleted);
            await Assert.ThrowsAsync<AnalysisDeviceUnavailableException>(() => AnalysisIo.Run(io, key,
                _ => { Interlocked.Increment(ref extraEntered); return 1; }, Cancellation).WaitAsync(TimeSpan.FromSeconds(2), Cancellation));
            Assert.Equal(0, extraEntered);
            Assert.Equal(23, await AnalysisIo.Run(io, Key(), _ => 23, Cancellation).WaitAsync(TimeSpan.FromSeconds(2), Cancellation));
            Assert.False(actual.IsCompleted);
        }
        finally { release.Set(); Assert.Equal(17, await actual.WaitAsync(TimeSpan.FromSeconds(10), Cancellation)); }
    }

    [Fact]
    public async Task ActualWorkRunsOnTheAdmittedDeviceThread()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); int caller = Environment.CurrentManagedThreadId;
        var result = await AnalysisIo.Run(io, Key(), _ => (Environment.CurrentManagedThreadId, Thread.CurrentThread.IsThreadPoolThread, AnalysisAdmission.Refusal()), Cancellation);
        Assert.NotEqual(caller, result.CurrentManagedThreadId); Assert.False(result.IsThreadPoolThread); Assert.Null(result.Item3);
    }

    [Fact]
    public async Task CancelingRunningWorkDoesNotCompleteOrReleaseItsDeviceUntilItReturns()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var cancel = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); string key = Key();
        var running = AnalysisIo.Run(io, key, _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return 17; }, cancel.Token);
        Task<int>? next = null; int nextEntered = 0;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation)); cancel.Cancel();
            next = AnalysisIo.Run(io, key, _ => { Interlocked.Increment(ref nextEntered); return 23; }, Cancellation);
            await Task.Delay(40, Cancellation); Assert.False(running.IsCompleted); Assert.Equal(0, Volatile.Read(ref nextEntered));
        }
        finally { release.Set(); Assert.Equal(17, await running.WaitAsync(TimeSpan.FromSeconds(10), Cancellation)); }
        Assert.Equal(23, await next!.WaitAsync(TimeSpan.FromSeconds(5), Cancellation));
    }

    [Fact]
    public async Task DeviceAdmissionIsSharedAcrossSchedulers()
    {
        Ordinary(); using var a = new DeviceIoScheduler(); using var b = new DeviceIoScheduler();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); string key = Key(); int secondEntered = 0;
        var first = AnalysisIo.Run(a, key, _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return 1; }, Cancellation);
        Task<int>? second = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation));
            second = AnalysisIo.Run(b, key, _ => Interlocked.Increment(ref secondEntered), Cancellation);
            await Task.Delay(40, Cancellation); Assert.Equal(0, Volatile.Read(ref secondEntered));
        }
        finally { release.Set(); await first.WaitAsync(TimeSpan.FromSeconds(10), Cancellation); }
        Assert.Equal(1, await second!.WaitAsync(TimeSpan.FromSeconds(5), Cancellation));
    }

    [Fact]
    public async Task NinthDeviceWaitsForOneOfTheEightActualRequests()
    {
        Ordinary(); using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); int started = 0;
        using var release = new ManualResetEventSlim();
        var work = Enumerable.Range(0, AnalysisLimits.IoRequests).Select(_ => AnalysisIo.Run(io, Key(), _ =>
        { if (Interlocked.Increment(ref started) == AnalysisLimits.IoRequests) entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(30)); return 1; }, Cancellation)).ToArray();
        Task<int>? ninth = null; int ninthEntered = 0;
        try
        {
            // This test needs the entire process-wide capacity, so its collection excludes other analysis cases.
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
            ninth = AnalysisIo.Run(io, Key(), _ => Interlocked.Increment(ref ninthEntered), Cancellation);
            await Task.Delay(40, Cancellation); Assert.Equal(0, Volatile.Read(ref ninthEntered));
        }
        finally { release.Set(); await Task.WhenAll(work).WaitAsync(TimeSpan.FromSeconds(10), Cancellation); }
        Assert.Equal(1, await ninth!.WaitAsync(TimeSpan.FromSeconds(5), Cancellation));
    }

    [Fact]
    public async Task CanceledQueuedWorkNeverEntersAndDoesNotStrandAdmission()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim(); using var cancel = new CancellationTokenSource(); string key = Key(); int called = 0;
        var running = AnalysisIo.Run(io, key, _ => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); return 1; }, Cancellation);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation));
            var queued = AnalysisIo.Run(io, key, _ => Interlocked.Increment(ref called), cancel.Token); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(5), Cancellation)); Assert.Equal(0, called);
        }
        finally { release.Set(); await running.WaitAsync(TimeSpan.FromSeconds(10), Cancellation); }
        Assert.Equal(1, await AnalysisIo.Run(io, key, _ => 1, Cancellation).WaitAsync(TimeSpan.FromSeconds(5), Cancellation));
    }

    [Fact]
    public async Task ShutdownBeforeOwnedReleaseUsesABoundedWorkerAndClosesExactlyOnce()
    {
        using var io = new DeviceIoScheduler(); io.Dispose();
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim(); int caller = Environment.CurrentManagedThreadId;
        var source = new ClosingSource(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
        var closing = AnalysisIo.ReleaseAsync(io, source);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation)); Assert.False(closing.IsCompleted);
            Assert.NotEqual(caller, source.Thread); Assert.Equal(1, source.Calls);
        }
        finally { release.Set(); await closing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation); }
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public async Task ShutdownDuringOwnedReleaseKeepsActualCompletionAndDoesNotRetry()
    {
        using var io = new DeviceIoScheduler(); using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var source = new ClosingSource(() => { entered.Set(); release.Wait(TimeSpan.FromSeconds(10)); });
        var closing = AnalysisIo.ReleaseAsync(io, source);
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation)); io.Dispose(); await Task.Delay(40, Cancellation);
            Assert.False(closing.IsCompleted); Assert.Equal(1, source.Calls);
        }
        finally { release.Set(); await closing.WaitAsync(TimeSpan.FromSeconds(10), Cancellation); }
        Assert.Equal(1, source.Calls);
    }

    private sealed class ClosingSource(Action close) : IAnalysisBackend
    {
        public int Calls, Thread;
        public RootBinding Root { get; } = new("1", "file", "fixture", "fixture", "fixture", IdentityAssurance.Verified, "fixture");
        public string IoDeviceKey { get; } = Key();
        public string Basis => "fixture-v1";
        public AnalysisEntry RootEntry => throw new NotSupportedException();
        public IEnumerable<AnalysisEntry> Children(AnalysisEntry directory, CancellationToken cancellation) => throw new NotSupportedException();
        public void ValidateReadScope(AnalysisEntry directory) => throw new NotSupportedException();
        public void Dispose() { Thread = Environment.CurrentManagedThreadId; Interlocked.Increment(ref Calls); close(); }
    }
}
