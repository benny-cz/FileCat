using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

// The revision reads' lifetime (independent reviews of 12 November 2026), on a plain file standing in for an archive:
// a shown listing's background read and a command's own read, what starts each, what stops each, and what a read once
// saw. The change-token callback runs first in every read, on its thread; holding it there pins a read without races.
public sealed partial class ArchiveListingRevisionTests
{
    [Fact]
    public void An_archive_over_the_write_limit_is_read_only_with_its_reason_and_never_read()
    {
        using var temp = new TempDir(); using var file = Sparse(temp, ArchiveWritePolicy.MaxPhysicalBytes + 1);
        var probe = new Probe(file);
        Assert.Equal(ArchiveListingRevision.TooLarge, probe.Revision.Problem);
        using (probe.Revision.Show(long.MaxValue))
            Assert.Equal(ArchiveListingRevision.TooLarge, Assert.Throws<IOException>(() => probe.Revision.Resolve(Ct)).Message);
        Assert.False(probe.Revision.ReadBegan); Assert.Equal(0, probe.Taken);
    }

    [Fact]
    public void A_listing_over_the_background_limit_is_read_only_by_a_command()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file);
        using (probe.Revision.Show(limit: 1024)) Assert.False(probe.Revision.ReadBegan);
        Assert.Equal(Sha(file), probe.Revision.Resolve(Ct));
        Assert.Equal(0, probe.Taken); // A command's caller keeps the listing's file open, not the revision.
    }

    [Fact]
    public void When_no_panel_shows_the_listing_any_more_its_background_read_stops()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true);
        probe.Revision.HiddenGrace = TimeSpan.Zero;
        var showing = probe.Revision.Show(long.MaxValue);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 1, 5000));
        showing.Dispose(); probe.Release();
        Assert.True(SpinWait.SpinUntil(() => probe.Returned == 1, 10_000), "The hidden listing's read kept going.");
        Assert.Null(probe.Revision.Known); Assert.Equal(0, probe.Resolved);
    }

    [Fact]
    public void A_hidden_listing_s_background_read_stops_once_its_grace_ends_and_a_later_showing_reads_again()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true); var clock = new ManualClock();
        probe.Revision.Clock = clock;
        var showing = probe.Revision.Show(long.MaxValue);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 1, 5000));
        showing.Dispose();
        Assert.False(probe.Revision.BackgroundStopping); // Within the grace the read goes on.
        Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(clock.Timers).Due);
        clock.Timers[0].Fire();
        Assert.True(probe.Revision.BackgroundStopping, "The grace ended, and the hidden listing's read went on.");
        probe.Release();
        Assert.True(SpinWait.SpinUntil(() => probe.Returned == 1, 10_000));
        Assert.Null(probe.Revision.Known); Assert.Equal(0, probe.Resolved);
        using var again = probe.Revision.Show(long.MaxValue);
        Assert.True(SpinWait.SpinUntil(() => probe.Revision.Known is not null, 10_000));
        Assert.Equal(Sha(file), probe.Revision.Known); Assert.Equal(2, probe.Revision.BackgroundReads);
    }

    [Fact]
    public void A_listing_shown_again_within_the_grace_keeps_its_one_background_read_even_if_the_timer_already_fired()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true); var clock = new ManualClock();
        probe.Revision.Clock = clock;
        var first = probe.Revision.Show(long.MaxValue);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 1, 5000));
        first.Dispose(); // The panel moves to another folder of the archive...
        using var second = probe.Revision.Show(long.MaxValue); // ...whose listing shows the same revision.
        var timer = Assert.Single(clock.Timers);
        Assert.True(timer.Disposed);
        timer.Fire(); // A timer that had fired as the listing was shown again: it stops nothing.
        Assert.False(probe.Revision.BackgroundStopping, "The read stopped although the listing was shown again.");
        probe.Release();
        Assert.True(SpinWait.SpinUntil(() => probe.Revision.Known is not null, 10_000));
        Assert.Equal(Sha(file), probe.Revision.Known);
        Assert.Equal(1, probe.Revision.BackgroundReads); Assert.Equal(1, probe.Taken); Assert.Equal(1, probe.Resolved);
    }

    [Fact]
    public void A_grace_left_from_an_earlier_hiding_does_not_cut_a_later_one_short()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true); var clock = new ManualClock();
        probe.Revision.Clock = clock;
        var first = probe.Revision.Show(long.MaxValue);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 1, 5000));
        first.Dispose();
        var second = probe.Revision.Show(long.MaxValue);
        second.Dispose();
        Assert.Equal(2, clock.Timers.Count);
        clock.Timers[0].Fire();
        Assert.False(probe.Revision.BackgroundStopping, "An earlier hiding's grace stopped the read.");
        clock.Timers[1].Fire();
        Assert.True(probe.Revision.BackgroundStopping);
        probe.Release();
        Assert.True(SpinWait.SpinUntil(() => probe.Returned == 1, 10_000));
        Assert.Null(probe.Revision.Known);
    }

    [Fact]
    public void Hiding_or_showing_a_listing_never_waits_for_the_index_its_read_is_waiting_for()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, holdLease: true);
        var first = probe.Revision.Show(long.MaxValue);
        Assert.True(SpinWait.SpinUntil(() => probe.Taking == 1, 5000), "The read should be waiting for the index.");
        // As a panel's tab closing and another opening on the UI thread, while a viewer holds the index's lock.
        var panel = Task.Factory.StartNew(() =>
        {
            first.Dispose();
            probe.Revision.Show(long.MaxValue).Dispose();
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        bool returned = panel.Wait(TimeSpan.FromSeconds(10), Ct);
        probe.ReleaseLease();
        Assert.True(returned, "Hiding or showing the listing waited for the index the read waits for.");
        Assert.True(SpinWait.SpinUntil(() => probe.Returned == 1, 10_000));
        Assert.Equal(1, probe.Revision.BackgroundReads);
    }

    [Fact]
    public void A_listing_two_panels_show_keeps_reading_while_either_does()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true);
        probe.Revision.HiddenGrace = TimeSpan.Zero;
        using var first = probe.Revision.Show(long.MaxValue);
        var second = probe.Revision.Show(long.MaxValue);
        second.Dispose(); probe.Release();
        Assert.True(SpinWait.SpinUntil(() => probe.Revision.Known is not null, 10_000));
        Assert.Equal(Sha(file), probe.Revision.Known);
        Assert.Equal(1, probe.Taken); Assert.Equal(1, probe.Revision.BackgroundReads);
    }

    [Fact]
    public void Closing_the_listing_stops_its_background_read_and_none_begins_again()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true);
        using var showing = probe.Revision.Show(long.MaxValue);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 1, 5000));
        probe.Revision.Close(); probe.Release();
        Assert.True(SpinWait.SpinUntil(() => probe.Returned == 1, 10_000), "Closing the listing did not stop its read.");
        Assert.Null(probe.Revision.Known); Assert.Equal(0, probe.Resolved);
        using (probe.Revision.Show(long.MaxValue)) Assert.Equal(1, probe.Revision.BackgroundReads);
    }

    [Fact]
    public void Closing_the_listing_does_not_stop_a_command_s_own_read()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true);
        var command = Command(probe.Revision, Ct);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 1, 5000));
        probe.Revision.Close(); probe.Release();
        Assert.Equal(Sha(file), Await(command));
        Assert.Equal(1, probe.Resolved); Assert.Equal(0, probe.Revision.BackgroundReads);
    }

    [Fact]
    public void A_command_reads_the_archive_itself_and_the_background_read_gives_way()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true);
        using var showing = probe.Revision.Show(long.MaxValue);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 1, 5000));
        var command = Command(probe.Revision, Ct);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 2, 5000));
        probe.Release();
        Assert.Equal(Sha(file), Await(command));
        Assert.True(SpinWait.SpinUntil(() => probe.Returned == 1, 10_000), "The background read did not end.");
        // Only the command's read finished: the background read stopped when the command began its own.
        Assert.Equal(1, probe.Resolved); Assert.Equal(Sha(file), probe.Revision.Known);
    }

    [Fact]
    public void A_listing_shown_while_a_command_reads_it_starts_no_second_read()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true);
        var command = Command(probe.Revision, Ct);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 1, 5000));
        using var showing = probe.Revision.Show(long.MaxValue);
        Assert.Equal(0, probe.Revision.BackgroundReads);
        probe.Release();
        Assert.Equal(Sha(file), Await(command));
        Assert.Equal(0, probe.Taken); Assert.Equal(1, probe.Resolved);
    }

    [Fact]
    public void A_command_that_gives_up_stops_its_own_read_and_a_later_showing_or_command_reads_again()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true);
        using var stop = new CancellationTokenSource();
        var command = Command(probe.Revision, stop.Token);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 1, 5000));
        stop.Cancel(); probe.Release();
        Assert.ThrowsAny<OperationCanceledException>(() => Await(command));
        Assert.Null(probe.Revision.Known); Assert.Equal(0, probe.Resolved);
        using (probe.Revision.Show(long.MaxValue))
        {
            Assert.Equal(1, probe.Revision.BackgroundReads);
            Assert.True(SpinWait.SpinUntil(() => probe.Revision.Known is not null, 10_000));
        }
        Assert.Equal(Sha(file), probe.Revision.Resolve(Ct));
    }

    [Fact]
    public void A_panel_showing_a_listing_does_not_keep_its_revision_in_memory()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var (showing, revision) = ShowAndForget(file);
        for (int i = 0; i < 5 && Alive(revision); i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.False(Alive(revision), "A panel's showing kept the revision, and so its index's catalog, in memory.");
        showing.Dispose(); // Nothing left to hide.
    }

    [Fact]
    public void A_change_a_read_saw_is_final_for_the_listing_and_makes_its_index_stale()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file) { Same = false };
        Assert.Equal(ArchiveListingRevision.Changed, Assert.Throws<IOException>(() => probe.Revision.Resolve(Ct)).Message);
        probe.Same = true; // The change token restored: the listing still showed something else.
        Assert.Equal(ArchiveListingRevision.Changed, Assert.Throws<IOException>(() => probe.Revision.Resolve(Ct)).Message);
        using (probe.Revision.Show(long.MaxValue)) Assert.Equal(0, probe.Revision.BackgroundReads);
        Assert.Equal(1, probe.Stale); Assert.Null(probe.Revision.Known); Assert.Equal(0, probe.Taken);
    }

    [Fact]
    public void A_change_token_that_cannot_be_read_now_is_tried_again_not_taken_for_a_change()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file) { Unreadable = true };
        Assert.Equal(ArchiveListingRevision.Unreadable, Assert.Throws<IOException>(() => probe.Revision.Resolve(Ct)).Message);
        probe.Unreadable = false;
        Assert.Equal(Sha(file), probe.Revision.Resolve(Ct));
        Assert.Equal(0, probe.Stale);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A command on a thread of its own, as the application's preparation work runs one.
    private static Task<string> Command(ArchiveListingRevision revision, CancellationToken ct) =>
        Task.Factory.StartNew(() => revision.Resolve(ct), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static string Await(Task<string> task) => task.WaitAsync(TimeSpan.FromSeconds(30), Ct).GetAwaiter().GetResult();

    // A panel's showing of a listing whose revision nothing else keeps (its index left the cache).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IDisposable Showing, WeakReference<ArchiveListingRevision> Revision) ShowAndForget(FileStream file)
    {
        var revision = new ArchiveListingRevision(file.SafeFileHandle, () => true, () => null, null);
        return (revision.Show(limit: 0), new WeakReference<ArchiveListingRevision>(revision));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool Alive(WeakReference<ArchiveListingRevision> revision) => revision.TryGetTarget(out _);

    // A clock whose timers fire only when a test says so (and may fire after being disposed, as a real one can).
    private sealed class ManualClock : TimeProvider
    {
        private readonly List<ManualTimer> timers = [];
        internal IReadOnlyList<ManualTimer> Timers { get { lock (timers) return [.. timers]; } }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime);
            lock (timers) timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due) : ITimer
    {
        internal TimeSpan Due { get; private set; } = due;
        internal bool Disposed { get; private set; }
        internal void Fire() => callback(state);
        public bool Change(TimeSpan dueTime, TimeSpan period) { Due = dueTime; return !Disposed; }
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    // The revision of one file, with its change-token callback and leases observed.
    private sealed class Probe
    {
        private readonly ManualResetEventSlim held = new(), leaseHeld = new();
        private readonly bool hold, holdLease;
        internal volatile bool Same = true, Unreadable;
        internal int Taken, Taking, Returned, Entered, Stale, Resolved;
        internal ArchiveListingRevision Revision { get; }
        internal Probe(FileStream file, bool hold = false, bool holdLease = false)
        {
            this.hold = hold; this.holdLease = holdLease;
            Revision = new ArchiveListingRevision(file.SafeFileHandle, Unchanged, Take, null,
                resolved: (_, _) => Interlocked.Increment(ref Resolved), stale: () => Interlocked.Increment(ref Stale));
        }
        internal void Release() => held.Set();
        // The index's lock, busy (a viewer decompressing a member) until this.
        internal void ReleaseLease() => leaseHeld.Set();
        private bool Unchanged()
        {
            Interlocked.Increment(ref Entered);
            if (hold) held.Wait(TimeSpan.FromSeconds(30));
            if (Unreadable) throw new IOException("The change token cannot be read now (test).");
            return Same;
        }
        private IDisposable? Take()
        {
            Interlocked.Increment(ref Taking);
            if (holdLease) leaseHeld.Wait(TimeSpan.FromSeconds(30));
            Interlocked.Increment(ref Taken); return new Lease(this);
        }
        private sealed class Lease(Probe owner) : IDisposable
        {
            private int done;
            public void Dispose() { if (Interlocked.Exchange(ref done, 1) == 0) Interlocked.Increment(ref owner.Returned); }
        }
    }

    private static FileStream Plain(TempDir temp)
    {
        var stream = new FileStream(Path.Combine(temp.Path, "listed.bin"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(RandomNumberGenerator.GetBytes(64 * 1024)); stream.Flush();
        return stream;
    }

    // A file of the given length that occupies no space: sparse on Windows (marked so first), and on ext4 and APFS.
    private static FileStream Sparse(TempDir temp, long length)
    {
        var stream = new FileStream(Path.Combine(temp.Path, "sparse.bin"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        if (OperatingSystem.IsWindows() && !DeviceIoControl(stream.SafeFileHandle, 0x0009_00C4 /* FSCTL_SET_SPARSE */, 0, 0, 0, 0, out _, 0))
        {
            stream.Dispose(); Assert.Skip("This volume has no sparse files.");
        }
        stream.SetLength(length);
        return stream;
    }

    private static string Sha(FileStream file)
    {
        using var copy = new FileStream(file.Name, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(SHA256.HashData(copy));
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle handle, uint code, nint input, uint inputSize, nint output, uint outputSize,
        out uint returned, nint overlapped);
}
