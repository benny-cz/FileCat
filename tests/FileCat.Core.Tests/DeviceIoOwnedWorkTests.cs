using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class DeviceIoOwnedWorkTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Owned_cancellation_or_shutdown_keeps_active_work_owned_until_its_native_boundary_returns(bool shutdown)
    {
        using var io = new DeviceIoScheduler(threadsPerDevice: 1); using var stop = CancellationTokenSource.CreateLinkedTokenSource(io.StoppingToken);
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = io.RunOwned("owned-active", IoPriority.Normal, token =>
        { entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); token.ThrowIfCancellationRequested(); return 3; }, stop.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            if (shutdown) io.Dispose(); else stop.Cancel();
            Assert.False(pending.IsCompleted);
            if (!shutdown) Assert.Equal(7, await io.RunOwned("other-device", IoPriority.Normal, _ => 7, TestContext.Current.CancellationToken));
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Owned_queued_cancellation_never_enters_the_delegate_and_does_not_wait_for_a_blocked_device(bool shutdown)
    {
        using var io = new DeviceIoScheduler(threadsPerDevice: 1); using var stop = CancellationTokenSource.CreateLinkedTokenSource(io.StoppingToken);
        using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = io.RunOwned("owned-queue", IoPriority.Normal, _ => { entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(5))); return 1; }, TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            var queued = io.RunOwned<int>("owned-queue", IoPriority.Normal, _ => throw new InvalidOperationException("Canceled delegate ran."), stop.Token);
            if (shutdown) io.Dispose(); else stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        }
        finally { release.Set(); }
        Assert.Equal(1, await held);
    }
}
