using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

// The revision read's lifetime (third independent review of 12 November 2026), on a plain file standing in for an
// archive: what starts a read, what stops it, what keeps it going, and what a read once saw. The change-token callback
// runs first on the reading thread; holding it there pins a read in progress without timing races.
public sealed partial class ArchiveListingRevisionTests
{
    [Fact]
    public void An_archive_over_the_write_limit_is_read_only_with_its_reason_and_never_read()
    {
        using var temp = new TempDir(); using var file = Sparse(temp, ArchiveWritePolicy.MaxPhysicalBytes + 1);
        var leases = new Leases();
        var revision = new ArchiveListingRevision(file.SafeFileHandle, () => true, leases.Take, null);
        Assert.Equal(ArchiveListingRevision.TooLarge, revision.Problem);
        revision.ReadInBackground(long.MaxValue);
        Assert.Equal(ArchiveListingRevision.TooLarge,
            Assert.Throws<IOException>(() => revision.Resolve(TestContext.Current.CancellationToken)).Message);
        Assert.False(revision.ReadBegan); Assert.Equal(0, leases.Taken);
    }

    [Fact]
    public void A_listing_over_the_background_limit_is_read_only_when_a_command_needs_it()
    {
        using var temp = new TempDir(); using var file = Plain(temp, 64 * 1024);
        var leases = new Leases();
        var revision = new ArchiveListingRevision(file.SafeFileHandle, () => true, leases.Take, null);
        revision.ReadInBackground(limit: 1024);
        Assert.False(revision.ReadBegan); Assert.Equal(0, leases.Taken);
        Assert.Equal(Sha(file), revision.Resolve(TestContext.Current.CancellationToken));
        Assert.True(SpinWait.SpinUntil(() => leases.Returned == 1, 5000));
    }

    [Fact]
    public void Closing_the_listing_stops_a_background_read_and_returns_its_lease()
    {
        using var temp = new TempDir(); using var file = Plain(temp, 64 * 1024);
        using var hold = new ManualResetEventSlim(); var leases = new Leases();
        var revision = new ArchiveListingRevision(file.SafeFileHandle, () => hold.Wait(TimeSpan.FromSeconds(30)), leases.Take, null);
        revision.ReadInBackground(long.MaxValue);
        Assert.True(revision.ReadBegan);
        revision.Close(); hold.Set();
        Assert.True(SpinWait.SpinUntil(() => leases.Returned == 1, 10_000), "The stopped read kept the listing open.");
        Assert.Null(revision.Known);
        Assert.Equal(ArchiveListingRevision.Closed,
            Assert.Throws<IOException>(() => revision.Resolve(TestContext.Current.CancellationToken)).Message);
    }

    [Fact]
    public void A_command_waiting_for_the_read_keeps_it_going_when_the_listing_closes()
    {
        using var temp = new TempDir(); using var file = Plain(temp, 64 * 1024);
        using var hold = new ManualResetEventSlim(); var leases = new Leases();
        var revision = new ArchiveListingRevision(file.SafeFileHandle, () => hold.Wait(TimeSpan.FromSeconds(30)), leases.Take, null);
        var command = Task.Factory.StartNew(() => revision.Resolve(TestContext.Current.CancellationToken), CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        Assert.True(SpinWait.SpinUntil(() => revision.ReadBegan, 5000));
        revision.Close(); hold.Set();
        Assert.Equal(Sha(file), command.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken).GetAwaiter().GetResult());
        Assert.True(SpinWait.SpinUntil(() => leases.Returned == 1, 5000));
    }

    [Fact]
    public void A_command_that_gives_up_stops_a_read_only_it_needed_and_a_later_command_reads_again()
    {
        using var temp = new TempDir(); using var file = Plain(temp, 64 * 1024);
        using var hold = new ManualResetEventSlim(); var leases = new Leases();
        var revision = new ArchiveListingRevision(file.SafeFileHandle, () => hold.Wait(TimeSpan.FromSeconds(30)), leases.Take, null);
        using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            Assert.ThrowsAny<OperationCanceledException>(() => revision.Resolve(stop.Token));
        hold.Set();
        Assert.True(SpinWait.SpinUntil(() => leases.Returned == 1, 10_000), "The read nothing waited for kept going.");
        Assert.Null(revision.Known);
        Assert.Equal(Sha(file), revision.Resolve(TestContext.Current.CancellationToken));
        Assert.Equal(2, leases.Taken);
    }

    [Fact]
    public void A_shown_listing_s_read_goes_on_in_the_background_after_a_command_gives_up()
    {
        using var temp = new TempDir(); using var file = Plain(temp, 64 * 1024);
        using var hold = new ManualResetEventSlim(); var leases = new Leases();
        var revision = new ArchiveListingRevision(file.SafeFileHandle, () => hold.Wait(TimeSpan.FromSeconds(30)), leases.Take, null);
        revision.ReadInBackground(long.MaxValue);
        using (var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(50)))
            Assert.ThrowsAny<OperationCanceledException>(() => revision.Resolve(stop.Token));
        hold.Set();
        Assert.True(SpinWait.SpinUntil(() => revision.Known is not null, 10_000));
        Assert.Equal(Sha(file), revision.Known); Assert.Equal(1, leases.Taken);
    }

    [Fact]
    public void A_change_a_read_saw_stays_seen_even_if_the_change_token_is_restored()
    {
        using var temp = new TempDir(); using var file = Plain(temp, 64 * 1024);
        bool same = false; var leases = new Leases();
        var revision = new ArchiveListingRevision(file.SafeFileHandle, () => Volatile.Read(ref same), leases.Take, null);
        Assert.Equal(ArchiveListingRevision.Changed,
            Assert.Throws<IOException>(() => revision.Resolve(TestContext.Current.CancellationToken)).Message);
        Volatile.Write(ref same, true);
        Assert.Equal(ArchiveListingRevision.Changed,
            Assert.Throws<IOException>(() => revision.Resolve(TestContext.Current.CancellationToken)).Message);
        revision.ReadInBackground(long.MaxValue);
        Assert.Null(revision.Known); Assert.Equal(1, leases.Taken);
    }

    private static FileStream Plain(TempDir temp, int size)
    {
        var stream = new FileStream(Path.Combine(temp.Path, "listed.bin"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        stream.Write(RandomNumberGenerator.GetBytes(size)); stream.Flush();
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

    private sealed class Leases
    {
        internal int Taken, Returned;
        internal IDisposable? Take() { Interlocked.Increment(ref Taken); return new Lease(this); }
        private sealed class Lease(Leases owner) : IDisposable
        {
            private int done;
            public void Dispose() { if (Interlocked.Exchange(ref done, 1) == 0) Interlocked.Increment(ref owner.Returned); }
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle handle, uint code, nint input, uint inputSize, nint output, uint outputSize,
        out uint returned, nint overlapped);
}
