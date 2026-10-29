using FileCat.Core.Jobs;

namespace FileCat.Core.Tests;

public sealed class JobRateLimitTests
{
    [Fact]
    public async Task A_large_buffer_waits_for_its_entire_rate_deficit()
    {
        var job = NewJob(1_000_000);
        var pace = new TransferPace();
        await Task.Run(() => job.Throttle(2_500_000, pace), TestContext.Current.CancellationToken);
        Assert.True(pace.Clock.Elapsed >= TimeSpan.FromSeconds(2.3),
            $"A 2.5-second allowance was exhausted in {pace.Clock.Elapsed}; a single one-second wait is insufficient.");
    }

    [Fact]
    public async Task A_live_limit_change_releases_a_throttled_worker_within_one_wait_slice()
    {
        var job = NewJob(1);
        var waiting = Task.Run(() => job.Throttle(1_000_000, new TransferPace()), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        job.RateLimit = 0;
        await waiting.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Cancellation_stops_a_throttled_worker_within_one_wait_slice()
    {
        var job = NewJob(1);
        var waiting = Task.Run(() => job.Throttle(1_000_000, new TransferPace()), TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(waiting.IsCompleted);
        job.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => waiting.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
    }

    // Review finding R-M4: the live limit paced each file from its start, so lowering it stalled for the bytes sent at the
    // old one, a resumed job raced through the time it was paused, and a slow stretch was followed by a full-speed burst.

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void A_lowered_limit_paces_from_the_change_not_from_the_files_start()
    {
        var pace = new TransferPace();
        Assert.Equal(TimeSpan.Zero, pace.Delay(0, 10_000_000, 0, S(0)));
        Assert.Equal(TimeSpan.Zero, pace.Delay(50_000_000, 10_000_000, 0, S(5)));
        // Lowered to 1 MB/s after 50 MB: no 45-second stall for what went at the old limit.
        Assert.Equal(TimeSpan.Zero, pace.Delay(50_000_000, 1_000_000, 0, S(5)));
        Assert.Equal(S(1), pace.Delay(51_000_000, 1_000_000, 0, S(5)));
    }

    [Fact]
    public void A_resumed_transfer_does_not_race_through_the_time_it_was_paused()
    {
        var pace = new TransferPace();
        Assert.Equal(TimeSpan.Zero, pace.Delay(0, 1_000_000, 0, S(0)));
        Assert.Equal(TimeSpan.Zero, pace.Delay(1_000_000, 1_000_000, 0, S(1)));
        // Paused for a minute, then resumed: the next 2 MB still take 2 seconds.
        Assert.Equal(TimeSpan.Zero, pace.Delay(1_000_000, 1_000_000, 1, S(61)));
        Assert.Equal(S(2), pace.Delay(3_000_000, 1_000_000, 1, S(61)));
    }

    [Fact]
    public void A_slow_stretch_saves_up_at_most_a_second_of_allowance()
    {
        var pace = new TransferPace();
        Assert.Equal(TimeSpan.Zero, pace.Delay(0, 1_000_000, 0, S(0)));
        Assert.Equal(TimeSpan.Zero, pace.Delay(1_000_000, 1_000_000, 0, S(10))); // the storage was slow
        Assert.Equal(S(2), pace.Delay(4_000_000, 1_000_000, 0, S(10))); // 3 MB more: 3 seconds less the one saved
    }

    [Fact]
    public void Without_a_limit_nothing_waits_and_a_limit_set_later_paces_from_then()
    {
        var pace = new TransferPace();
        Assert.Equal(TimeSpan.Zero, pace.Delay(0, 0, 0, S(0)));
        Assert.Equal(TimeSpan.Zero, pace.Delay(100_000_000, 0, 0, S(1)));
        Assert.Equal(TimeSpan.Zero, pace.Delay(100_000_000, 1_000_000, 0, S(1)));
        Assert.Equal(S(1), pace.Delay(101_000_000, 1_000_000, 0, S(1)));
    }

    private static Job NewJob(long rate) => new(new JobRequest
    {
        Kind = JobKind.Copy,
        Options = new TransferOptions { RateLimit = rate },
    }, "rate test", "test device", [], []);
}
