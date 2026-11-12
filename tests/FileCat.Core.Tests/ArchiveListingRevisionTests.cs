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
        var showing = probe.Revision.Show(long.MaxValue);
        Assert.True(probe.Revision.ReadBegan);
        showing.Dispose(); probe.Release();
        Assert.True(SpinWait.SpinUntil(() => probe.Returned == 1, 10_000), "The hidden listing's read kept going.");
        Assert.Null(probe.Revision.Known);
    }

    [Fact]
    public void A_listing_two_panels_show_keeps_reading_while_either_does()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true);
        using var first = probe.Revision.Show(long.MaxValue);
        var second = probe.Revision.Show(long.MaxValue);
        second.Dispose(); probe.Release();
        Assert.True(SpinWait.SpinUntil(() => probe.Revision.Known is not null, 10_000));
        Assert.Equal(Sha(file), probe.Revision.Known); Assert.Equal(1, probe.Taken);
    }

    [Fact]
    public void Closing_the_listing_stops_its_background_read_but_not_a_command_s_own_read()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true);
        using var showing = probe.Revision.Show(long.MaxValue);
        var command = Command(probe.Revision, Ct);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered >= 2, 5000), "Both reads should be waiting on the change token.");
        probe.Revision.Close(); probe.Release();
        Assert.Equal(Sha(file), Await(command));
        Assert.True(SpinWait.SpinUntil(() => probe.Returned == 1, 10_000));
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
        Assert.True(SpinWait.SpinUntil(() => probe.Returned == 1, 10_000), "The background read did not give way.");
        Assert.Equal(Sha(file), probe.Revision.Known);
    }

    [Fact]
    public void A_command_that_gives_up_stops_its_own_read_and_a_later_command_reads_again()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file, hold: true);
        using var stop = new CancellationTokenSource();
        var command = Command(probe.Revision, stop.Token);
        Assert.True(SpinWait.SpinUntil(() => probe.Entered == 1, 5000));
        stop.Cancel(); probe.Release();
        Assert.ThrowsAny<OperationCanceledException>(() => Await(command));
        Assert.Null(probe.Revision.Known);
        Assert.Equal(Sha(file), probe.Revision.Resolve(Ct));
    }

    [Fact]
    public void A_change_a_read_saw_is_final_for_the_listing_and_makes_its_index_stale()
    {
        using var temp = new TempDir(); using var file = Plain(temp);
        var probe = new Probe(file) { Same = false };
        Assert.Equal(ArchiveListingRevision.Changed, Assert.Throws<IOException>(() => probe.Revision.Resolve(Ct)).Message);
        probe.Same = true; // The change token restored: the listing still showed something else.
        Assert.Equal(ArchiveListingRevision.Changed, Assert.Throws<IOException>(() => probe.Revision.Resolve(Ct)).Message);
        using (probe.Revision.Show(long.MaxValue)) Assert.Equal(0, probe.Taken);
        Assert.Equal(1, probe.Stale); Assert.Null(probe.Revision.Known);
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

    // The revision of one file, with its change-token callback and leases observed.
    private sealed class Probe
    {
        private readonly ManualResetEventSlim held = new();
        private readonly bool hold;
        internal volatile bool Same = true, Unreadable;
        internal int Taken, Returned, Entered, Stale;
        internal ArchiveListingRevision Revision { get; }
        internal Probe(FileStream file, bool hold = false)
        {
            this.hold = hold;
            Revision = new ArchiveListingRevision(file.SafeFileHandle, Unchanged, Take, null, stale: () => Interlocked.Increment(ref Stale));
        }
        internal void Release() => held.Set();
        private bool Unchanged()
        {
            Interlocked.Increment(ref Entered);
            if (hold) held.Wait(TimeSpan.FromSeconds(30));
            if (Unreadable) throw new IOException("The change token cannot be read now (test).");
            return Same;
        }
        private IDisposable? Take() { Interlocked.Increment(ref Taken); return new Lease(this); }
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
