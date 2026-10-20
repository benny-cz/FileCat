using System.Diagnostics;
using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class JobRateLimitTests
{
    [Fact]
    public async Task A_large_buffer_waits_for_its_entire_rate_deficit()
    {
        var job = NewJob(1_000_000);
        var clock = Stopwatch.StartNew();
        await Task.Run(() => job.Throttle(2_500_000, clock), TestContext.Current.CancellationToken);
        Assert.True(clock.Elapsed >= TimeSpan.FromSeconds(2.3),
            $"A 2.5-second allowance was exhausted in {clock.Elapsed}; a single one-second wait is insufficient.");
    }

    [Fact]
    public async Task A_live_limit_change_releases_a_throttled_worker_within_one_wait_slice()
    {
        var job = NewJob(1);
        var clock = Stopwatch.StartNew();
        var waiting = Task.Run(() => job.Throttle(1_000_000, clock), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        job.RateLimit = 0;
        await waiting.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cancellation_stops_a_throttled_worker_within_one_wait_slice()
    {
        var job = NewJob(1);
        var clock = Stopwatch.StartNew();
        var waiting = Task.Run(() => job.Throttle(1_000_000, clock), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        job.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waiting.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    private static Job NewJob(long rate) => new(new JobRequest
    {
        Kind = JobKind.Copy,
        Options = new TransferOptions { RateLimit = rate },
    }, "rate test", "test device", [], []);
}
