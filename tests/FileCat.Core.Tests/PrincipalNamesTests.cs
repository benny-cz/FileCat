using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

[CollectionDefinition("Ownership resolution", DisableParallelization = true)]
public sealed class OwnershipResolutionCollection;

[Collection("Ownership resolution")]
public sealed class PrincipalNamesTests
{
    private static Principal Id(uint id) => new(PrincipalKind.UnixUser, id.ToString(System.Globalization.CultureInfo.InvariantCulture));

    [Theory]
    [InlineData(NameStatus.Resolved, 600_000)]
    [InlineData(NameStatus.NotFound, 120_000)]
    [InlineData(NameStatus.Unavailable, 30_000)]
    public async Task Expiry_is_request_based_and_peek_or_capture_never_retry(NameStatus status, long lifetime)
    {
        long now = 0;
        var source = new FakeOwnership { Lookup = _ => new(status, status == NameStatus.Resolved ? "fixture" : null) };
        using var names = new PrincipalNames(source, () => Interlocked.Read(ref now));
        Assert.Equal(NameStatus.NotRequested, names.Peek(Id(1)).Status);
        names.Request(Id(1));
        Assert.True(await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Interlocked.Exchange(ref now, lifetime - 1);
        names.Request(Id(1));
        Assert.Equal(1, source.NameCalls);
        Interlocked.Exchange(ref now, lifetime);
        Assert.Equal(status, names.Peek(Id(1)).Status);
        Assert.Equal(status, names.Capture()[Id(1)].Status);
        Assert.Equal(1, source.NameCalls);
        names.Request(Id(1));
        Assert.True(await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.Equal(2, source.NameCalls);
    }

    [Fact]
    public async Task Same_identity_coalesces_and_queue_drops_are_not_pending()
    {
        using var entered = new CountdownEvent(2);
        using var release = new ManualResetEventSlim();
        var source = new FakeOwnership { Lookup = _ => { entered.Signal(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return new(NameStatus.Resolved, "fixture"); } };
        using var names = new PrincipalNames(source, () => 0, maxEntries: 8, maxChars: 1024, maxQueued: 2);
        try
        {
            names.Request(Id(1));
            names.Request(Id(2));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            for (int i = 0; i < 100; i++) Assert.Equal(NameStatus.Pending, names.Request(Id(1)).Status);
            names.Request(Id(3));
            names.Request(Id(4));
            Assert.Equal(NameStatus.NotRequested, names.Request(Id(5)).Status);
            Assert.Equal(NameStatus.NotRequested, names.Peek(Id(5)).Status);
            Assert.Equal(2, names.Usage.Queued);
            Assert.Equal(2, source.NameCalls);
            names.Invalidate(); // queued/dropped entries have no stale Pending record
            Assert.Equal(NameStatus.NotRequested, names.Peek(Id(3)).Status);
            Assert.Equal(NameStatus.NotRequested, names.Peek(Id(1)).Status);
            Assert.Equal(2, names.Usage.Entries); // retiring native calls remain charged
        }
        finally { release.Set(); Assert.True(await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken)); }
        Assert.Equal(0, names.Usage.Entries);
        Assert.Equal(0, names.Usage.Characters);
    }

    [Fact]
    public async Task Invalidation_suppresses_old_generation_but_allows_new_requests()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        var source = new FakeOwnership { Lookup = _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            { entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return new(NameStatus.Resolved, "old generation"); }
            return new(NameStatus.Resolved, "new generation");
        } };
        using var names = new PrincipalNames(source);
        try
        {
            names.Request(Id(1));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            names.Invalidate();
            names.Request(Id(1));
            Assert.True(SpinWait.SpinUntil(() => names.Peek(Id(1)).Name == "new generation", TimeSpan.FromSeconds(5)));
            release.Set();
            Assert.True(await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal("new generation", names.Peek(Id(1)).Name);
            Assert.Equal(1, names.Usage.Entries);
        }
        finally { release.Set(); await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task Native_slots_include_blocked_disposed_resolvers_and_are_process_bounded()
    {
        using var entered = new CountdownEvent(4);
        using var release = new ManualResetEventSlim();
        int active = 0, peak = 0;
        var source = new FakeOwnership { Lookup = _ =>
        {
            int count = Interlocked.Increment(ref active);
            int old;
            do { old = Volatile.Read(ref peak); } while (count > old && Interlocked.CompareExchange(ref peak, count, old) != old);
            entered.Signal();
            try { Assert.True(release.Wait(TimeSpan.FromSeconds(15))); return new(NameStatus.Resolved, "retiring"); }
            finally { Interlocked.Decrement(ref active); }
        } };
        using var retiring = new PrincipalNames(source, () => Environment.TickCount64, slowAfter: TimeSpan.Zero);
        var currentSource = new FakeOwnership { Lookup = _ => new(NameStatus.Resolved, "current") };
        using var current = new PrincipalNames(currentSource);
        try
        {
            // Extra queued demand permits expansion from the normal two workers to four.
            for (uint i = 0; i < 6; i++) retiring.Request(Id(i));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));
            retiring.Dispose();
            Assert.Equal(4, retiring.Usage.Running);
            Assert.Equal(4, retiring.Usage.Entries);
            current.Request(Id(100));
            Assert.False(await current.WhenIdle(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
            Assert.Equal(0, currentSource.NameCalls);
            // Repeated create/dispose cannot obtain a new native quota or leave waiting workers alive.
            for (uint i = 0; i < 4; i++)
            {
                using var abandoned = new PrincipalNames(currentSource);
                abandoned.Request(Id(200 + i));
            }
            Assert.Equal(4, peak);
            release.Set();
            Assert.True(await current.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.True(await retiring.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal("current", current.Peek(Id(100)).Name);
            Assert.Equal(NameStatus.NotRequested, retiring.Peek(Id(0)).Status);
            Assert.Equal(0, retiring.Usage.Characters);
        }
        finally { release.Set(); await retiring.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); await current.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task Lru_and_all_retained_text_are_bounded_and_oversize_answers_are_not_kept()
    {
        var source = new FakeOwnership { Lookup = p => new(NameStatus.Resolved, p.Id == "99" ? new string('x', 1025) : new string('n', 20), "User", "Reason.") };
        using var names = new PrincipalNames(source, () => 0, maxEntries: 4, maxChars: 160);
        for (uint i = 0; i < 20; i++)
        {
            names.Request(Id(i));
            Assert.True(await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.InRange(names.Usage.Entries, 1, 4);
            Assert.InRange(names.Usage.Characters, 1, 160);
        }
        Assert.Equal(NameStatus.NotRequested, names.Peek(Id(0)).Status);
        names.Request(Id(99));
        Assert.True(await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        var answer = names.Peek(Id(99));
        Assert.Equal(NameStatus.Unavailable, answer.Status);
        Assert.Null(answer.Name);
        Assert.InRange(names.Usage.Characters, 1, 160);
    }

    [Fact]
    public async Task Disposal_suppresses_notifications_and_when_idle_observes_timeout_and_cancellation()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        string? workerName = null;
        var source = new FakeOwnership { Lookup = _ => { workerName = Thread.CurrentThread.Name; entered.Set(); Assert.True(release.Wait(TimeSpan.FromSeconds(10))); return new(NameStatus.Resolved, "fixture"); } };
        using var names = new PrincipalNames(source);
        int notifications = 0;
        names.Changed += () => Interlocked.Increment(ref notifications);
        try
        {
            names.Request(Id(1));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal("FileCat principal names", workerName);
            Assert.False(await names.WhenIdle(TimeSpan.FromMilliseconds(10), TestContext.Current.CancellationToken));
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => names.WhenIdle(TimeSpan.FromSeconds(1), canceled.Token));
            names.Dispose();
            release.Set();
            Assert.True(await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            Assert.Equal(0, notifications);
            Assert.Equal(NameStatus.NotRequested, names.Request(Id(2)).Status);
        }
        finally { release.Set(); await names.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); }
    }
}
