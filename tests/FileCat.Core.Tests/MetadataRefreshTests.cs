using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed class MetadataRefreshTests
{
    private static readonly EntryData Entry = new("item.bin", EntryKind.File, 10, 123);
    private const string PathName = "owned/item.bin";
    private const string FieldId = "refresh-test";

    private static MetadataField Field(Func<object?> produce, MetadataCost cost = MetadataCost.Cheap, bool refreshable = true) =>
        new(FieldId, "Test", cost, _ => true, (_, _) => produce(), value => value?.ToString() ?? "",
            RefreshAfter: refreshable ? TimeSpan.FromSeconds(5) : null);

    [Theory]
    [InlineData(MetadataState.Denied, MetadataObservation.Current)]
    [InlineData(MetadataState.Unsupported, MetadataObservation.Current)]
    [InlineData(MetadataState.Failed, MetadataObservation.Current)]
    [InlineData(MetadataState.Absent, MetadataObservation.Gone)]
    public void Structured_outcomes_keep_their_state_reason_and_observation(MetadataState state, MetadataObservation observation)
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io);
        var expected = new MetadataValue(state, Detail: "An explicit reason.", Observation: observation);
        metadata.Register(Field(() => expected));
        Assert.Equal(expected, metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken));
        Assert.Equal(expected, metadata.Peek(FieldId, PathName, Entry));
    }

    [Fact]
    public void Access_refusal_is_denied_and_transient_io_failure_is_failed()
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io);
        metadata.Register(Field(() => throw new UnauthorizedAccessException("Access refused")));
        Assert.Equal(new MetadataValue(MetadataState.Denied, Detail: "Access refused."),
            metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken));
        metadata.Register(Field(() => throw new IOException("Temporarily busy")));
        Assert.Equal(new MetadataValue(MetadataState.Failed, Detail: "Temporarily busy."),
            metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false, 5_000)]
    [InlineData(true, 30_000)]
    public void Compute_and_automatic_refresh_share_the_interval_and_effective_last_known_outcome(bool slow, long interval)
    {
        using var io = new DeviceIoScheduler();
        long now = 0;
        var metadata = new MetadataService(io, () => Interlocked.Read(ref now));
        int calls = 0;
        metadata.Register(Field(() => Interlocked.Increment(ref calls) switch
        {
            1 => "original",
            2 => new MetadataValue(MetadataState.Failed, Detail: "Temporarily busy."),
            _ => "recovered",
        }));
        MetadataValue Compute() => metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken, slow);
        Assert.Equal("original", Compute().Value);
        Interlocked.Exchange(ref now, interval - 1);
        Assert.Equal("original", Compute().Value);
        Assert.Equal("original", metadata.Get(FieldId, PathName, Entry, "owned", slow).Value);
        Assert.Equal(1, Volatile.Read(ref calls));
        Interlocked.Exchange(ref now, interval);
        var lastKnown = Compute();
        Assert.Equal(MetadataState.Available, lastKnown.State);
        Assert.Equal(MetadataObservation.LastKnown, lastKnown.Observation);
        Assert.Equal("original", lastKnown.Value);
        Assert.Equal("Temporarily busy. Showing the last value read.", lastKnown.Detail);
        Assert.Equal(lastKnown, metadata.Peek(FieldId, PathName, Entry));
        Interlocked.Exchange(ref now, interval * 2 - 1);
        Assert.Equal(lastKnown, Compute());
        Assert.Equal(lastKnown, metadata.Get(FieldId, PathName, Entry, "owned", slow));
        Assert.Equal(2, Volatile.Read(ref calls));
        Interlocked.Exchange(ref now, interval * 2);
        Assert.Equal(lastKnown, metadata.Get(FieldId, PathName, Entry, "owned", slow));
        Assert.True(SpinWait.SpinUntil(() => metadata.Peek(FieldId, PathName, Entry).Value is "recovered", TimeSpan.FromSeconds(5)));
        var recovered = metadata.Peek(FieldId, PathName, Entry);
        Assert.Equal(MetadataObservation.Current, recovered.Observation);
        Assert.Null(recovered.Detail);
        // The locked admission recheck must also use the slow interval after automatic Store.
        Interlocked.Exchange(ref now, interval * 3 - 1);
        for (int i = 0; i < 100; i++) metadata.Get(FieldId, PathName, Entry, "owned", slow);
        Assert.Equal(3, Volatile.Read(ref calls));
    }

    [Theory]
    [InlineData(MetadataState.Denied)]
    [InlineData(MetadataState.Unsupported)]
    [InlineData(MetadataState.Absent)]
    public void Definitive_outcomes_replace_old_data_and_refusal_requires_explicit_retry(MetadataState outcome)
    {
        using var io = new DeviceIoScheduler();
        long now = 0;
        var metadata = new MetadataService(io, () => now);
        int calls = 0;
        metadata.Register(Field(() => ++calls switch
        {
            1 => "old",
            2 => new MetadataValue(outcome, Detail: "Definitive result.", Observation: outcome == MetadataState.Absent ? MetadataObservation.Gone : MetadataObservation.Current),
            _ => "new",
        }));
        metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken);
        now = 5_000;
        var value = metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken);
        Assert.Equal(outcome, value.State);
        Assert.Null(value.Value);
        if (outcome == MetadataState.Absent)
        {
            Assert.Equal(MetadataObservation.Gone, value.Observation);
            Assert.Equal(value, metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken));
        }
        else
        {
            now = 1_000_000;
            metadata.ExpireRefreshable();
            Assert.Equal(value, metadata.Get(FieldId, PathName, Entry, "owned", false));
            Assert.Equal(2, calls);
            Assert.Equal("new", metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken).Value);
        }
    }

    [Fact]
    public async Task Slow_location_admission_rechecks_a_racing_publication_with_the_effective_interval()
    {
        using var io = new DeviceIoScheduler(threadsPerDevice: 1, maxThreadsPerDevice: 1);
        long now = 0;
        int publishDuringAgeCheck = 0, calls = 0;
        MetadataService? metadata = null;
        long Clock()
        {
            if (Interlocked.Exchange(ref publishDuringAgeCheck, 0) == 1)
            {
                // Get has already read the old timestamp. Another explicit production finishes before
                // its Schedule admission recheck, and is only six seconds old at that recheck.
                metadata!.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken, slowLocation: true);
                now = 36_000;
            }
            return now;
        }
        metadata = new MetadataService(io, Clock);
        metadata.Register(Field(() => Interlocked.Increment(ref calls)));
        metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken, slowLocation: true);
        now = 30_000;
        publishDuringAgeCheck = 1;
        metadata.Get(FieldId, PathName, Entry, "owned", true);
        // Drain the sole device worker: an incorrectly admitted automatic production would precede this.
        await io.Run("owned", IoPriority.Background, _ => { }, TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
        Assert.Equal(2, metadata.Peek(FieldId, PathName, Entry).Value);
    }

    [Fact]
    public void A_failed_first_read_is_retried_automatically_only_when_due()
    {
        using var io = new DeviceIoScheduler();
        long now = 0;
        var metadata = new MetadataService(io, () => Interlocked.Read(ref now));
        int calls = 0;
        metadata.Register(Field(() => Interlocked.Increment(ref calls) == 1 ? new MetadataValue(MetadataState.Failed) : "ready"));
        Assert.Equal(MetadataState.Failed, metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken).State);
        Interlocked.Exchange(ref now, 4_999);
        for (int i = 0; i < 100; i++) Assert.Equal(MetadataState.Failed, metadata.Get(FieldId, PathName, Entry, "owned", false).State);
        Assert.Equal(1, calls);
        Interlocked.Exchange(ref now, 5_000);
        metadata.Get(FieldId, PathName, Entry, "owned", false);
        Assert.True(SpinWait.SpinUntil(() => metadata.Peek(FieldId, PathName, Entry).Value is "ready", TimeSpan.FromSeconds(5)));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void Explicit_analysis_retries_a_failed_result_even_within_its_interval()
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io, () => 0);
        int calls = 0;
        metadata.Register(Field(() => ++calls == 1 ? new MetadataValue(MetadataState.Failed) : "ready"));
        Assert.Equal(MetadataState.Failed, metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken).State);
        Assert.Equal("ready", metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken).Value);
    }

    [Fact]
    public void Missing_production_time_is_stale_for_a_newly_refreshable_field()
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io, () => 0);
        metadata.Register(Field(() => "old", refreshable: false));
        metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken);
        metadata.Register(Field(() => "new"));
        Assert.Equal("new", metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken).Value);
        metadata.Register(Field(() => "automatic", refreshable: false));
        metadata.Forget(FieldId, PathName);
        metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken);
        metadata.Register(Field(() => "refreshed"));
        metadata.Get(FieldId, PathName, Entry, "owned", false);
        Assert.True(SpinWait.SpinUntil(() => metadata.Peek(FieldId, PathName, Entry).Value is "refreshed", TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(MetadataCost.Cheap)]
    [InlineData(MetadataCost.Expensive)]
    [InlineData(MetadataCost.Analysis)]
    public void Offline_gates_precede_cached_values_in_every_production_path(MetadataCost cost)
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io);
        int calls = 0;
        metadata.Register(Field(() => { calls++; return "cached"; }, cost));
        metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken);
        var offline = Entry with { Flags = EntryFlags.Offline };
        Assert.Equal(MetadataState.Unsupported, metadata.Get(FieldId, PathName, offline, "owned", false).State);
        Assert.Equal(MetadataState.Unsupported, metadata.Compute(FieldId, PathName, offline, TestContext.Current.CancellationToken).State);
        Assert.Equal(MetadataState.Unsupported, metadata.Peek(FieldId, PathName, offline, slowLocation: false).State);
        Assert.Equal(MetadataState.Unsupported, metadata.Compute(FieldId, "uncached", offline, TestContext.Current.CancellationToken).State);
        Assert.Equal(MetadataState.Absent, metadata.Peek(FieldId, PathName, Entry with { Kind = EntryKind.Parent }, false).State);
        Assert.Equal(1, calls);
        // Existing plain Peek keeps its compatibility contract; new consumers use the gated overload.
        Assert.Equal("cached", metadata.Peek(FieldId, PathName, offline).Value);
    }

    [Fact]
    public void Slow_expensive_fields_display_explicit_results_without_automatic_production()
    {
        using var io = new DeviceIoScheduler();
        long now = 0;
        var metadata = new MetadataService(io, () => now);
        int calls = 0;
        metadata.Register(Field(() => { calls++; return "explicit"; }, MetadataCost.Expensive));
        Assert.Equal(MetadataState.Unsupported, metadata.Get(FieldId, PathName, Entry, "owned", true).State);
        Assert.Equal(MetadataState.Unsupported, metadata.Peek(FieldId, PathName, Entry, true).State);
        Assert.Equal(0, calls);
        var value = metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken, slowLocation: true);
        now = 100_000;
        metadata.ExpireRefreshable();
        Assert.Equal(value, metadata.Get(FieldId, PathName, Entry, "owned", true));
        Assert.Equal(value, metadata.Peek(FieldId, PathName, Entry, true));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Expiry_requests_repaint_but_only_new_demand_starts_work()
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io, () => 0);
        int calls = 0;
        metadata.Register(Field(() => Interlocked.Increment(ref calls)));
        metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken);
        metadata.Compute(FieldId, "hidden", Entry, TestContext.Current.CancellationToken);
        var repainted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        metadata.ValuesChanged += () => repainted.TrySetResult();
        metadata.ExpireRefreshable();
        await repainted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(2, calls);
        metadata.Get(FieldId, PathName, Entry, "owned", false);
        Assert.True(SpinWait.SpinUntil(() => metadata.Peek(FieldId, PathName, Entry).Value is 3, TimeSpan.FromSeconds(5)));
        Assert.Equal(2, metadata.Peek(FieldId, "hidden", Entry).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cancellation_or_retirement_cannot_publish_a_returned_value(bool invalidate)
    {
        using var io = new DeviceIoScheduler();
        using var cts = new CancellationTokenSource();
        var metadata = new MetadataService(io);
        metadata.Register(Field(() => { if (invalidate) metadata.Invalidate(); else cts.Cancel(); return "obsolete"; }));
        Assert.Equal(MetadataState.NotRequested, metadata.Compute(FieldId, PathName, Entry, cts.Token).State);
        Assert.Equal(MetadataState.NotRequested, metadata.Peek(FieldId, PathName, Entry).State);
    }

    [Fact]
    public void Nonrefreshable_fields_do_not_gain_last_known_or_activation_retry()
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io, () => 0);
        int calls = 0;
        metadata.Register(Field(() => { calls++; return "checked"; }, refreshable: false));
        metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken);
        metadata.ExpireRefreshable();
        Assert.Equal("checked", metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken).Value);
        Assert.Equal("checked", metadata.Get(FieldId, PathName, Entry, "owned", false).Value);
        Assert.Equal(1, calls);
        metadata.Forget(FieldId, PathName);
        metadata.Register(Field(() => new MetadataValue(MetadataState.Failed), refreshable: false));
        Assert.Equal(MetadataState.Failed, metadata.Compute(FieldId, PathName, Entry, TestContext.Current.CancellationToken).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_read_error_after_cancellation_cannot_publish_failed_or_denied(bool accessDenied)
    {
        using var io = new DeviceIoScheduler();
        using var cts = new CancellationTokenSource();
        var metadata = new MetadataService(io);
        metadata.Register(Field(() =>
        {
            cts.Cancel();
            if (accessDenied) throw new UnauthorizedAccessException("Canceled read");
            throw new IOException("Canceled read");
        }));
        Assert.Equal(MetadataState.NotRequested, metadata.Compute(FieldId, PathName, Entry, cts.Token).State);
        Assert.Equal(MetadataState.NotRequested, metadata.Peek(FieldId, PathName, Entry).State);
    }

    [Fact]
    public void Small_cache_eviction_retires_timestamps_as_well_as_values()
    {
        using var io = new DeviceIoScheduler();
        var metadata = new MetadataService(io, () => 0, maxCache: 2);
        int calls = 0;
        metadata.Register(Field(() => ++calls));
        foreach (string path in new[] { "first", "second", "third" }) metadata.Compute(FieldId, path, Entry, TestContext.Current.CancellationToken);
        Assert.Equal(MetadataState.NotRequested, metadata.Peek(FieldId, "first", Entry).State);
        Assert.Equal(4, metadata.Compute(FieldId, "first", Entry, TestContext.Current.CancellationToken).Value);
    }
}
