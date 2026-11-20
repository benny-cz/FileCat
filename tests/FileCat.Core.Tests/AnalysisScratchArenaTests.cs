using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisScratchArenaTests
{
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void CheckpointsRetainTheirOwnHighWaterAndQuotaAfterWriterCloses()
    {
        Ordinary(); var directory = new FakeDirectory(); var budget = new AnalysisScratchBudget(1024);
        using var arena = new AnalysisScratchArena(directory, budget, 1024);
        var first = arena.Append([1, 2, 3], Cancellation); using var old = arena.Capture();
        var later = arena.Append([4, 5], Cancellation); using var current = arena.Capture();
        Assert.Equal(new byte[] { 1, 2, 3 }, old.Read(first, Cancellation));
        Assert.Throws<InvalidDataException>(() => old.Read(later, Cancellation));
        Assert.Equal(new byte[] { 4, 5 }, current.Read(later, Cancellation));
        long retained = budget.ReservedBytes; Assert.True(retained > 5);
        arena.Dispose(); Assert.False(directory.Disposed); Assert.Equal(retained, budget.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => arena.Append([9], Cancellation));
        Assert.Throws<ObjectDisposedException>(() => arena.Capture());
        Assert.Equal(new byte[] { 1, 2, 3 }, old.Read(first, Cancellation));
        old.Dispose(); Assert.False(directory.Disposed);
        current.Dispose(); current.Dispose(); arena.Dispose();
        Assert.True(directory.Disposed); Assert.True(directory.File.Disposed); Assert.Equal(0, budget.ReservedBytes);
        Assert.Throws<ObjectDisposedException>(() => current.Read(first, Cancellation));
    }

    [Theory]
    [InlineData("header")]
    [InlineData("payload")]
    [InlineData("tag")]
    [InlineData("truncate")]
    public void DamagedScratchNeverReturnsSuccessfulObservations(string fault)
    {
        Ordinary(); var directory = new FakeDirectory(); using var arena = new AnalysisScratchArena(directory);
        var record = arena.Append([1, 2, 3], Cancellation); using var checkpoint = arena.Capture();
        long position = fault switch { "header" => 4, "payload" => 16, _ => directory.File.Length - 1 };
        if (fault == "truncate") directory.File.Data.SetLength(position);
        else directory.File.Data.GetBuffer()[checked((int)position)] ^= 0x80;
        Assert.Throws<InvalidDataException>(() => checkpoint.Read(record, Cancellation));
    }

    [Fact]
    public void InvalidExtentsAndForeignTagsAreRefusedBeforeUnboundedAllocation()
    {
        Ordinary(); var a = new FakeDirectory(); var b = new FakeDirectory();
        using var left = new AnalysisScratchArena(a); using var right = new AnalysisScratchArena(b);
        var record = left.Append([1, 2, 3], Cancellation); right.Append([1, 2, 3], Cancellation); using var checkpoint = right.Capture();
        foreach (var invalid in new[] { new AnalysisScratchRecord(-1, 1), new AnalysisScratchRecord(long.MaxValue, 1),
            new AnalysisScratchRecord(0, -1), new AnalysisScratchRecord(0, int.MaxValue), new AnalysisScratchRecord(1, 2) })
            Assert.Throws<InvalidDataException>(() => checkpoint.Read(invalid, Cancellation));
        a.File.Data.ToArray().CopyTo(b.File.Data.GetBuffer(), 0);
        Assert.Throws<InvalidDataException>(() => checkpoint.Read(record, Cancellation));
    }

    [Fact]
    public void FailedNativeWriteRetainsPreviousCheckpointAndEntireClaimedExtent()
    {
        Ordinary(); var directory = new FakeDirectory(); var budget = new AnalysisScratchBudget(1024);
        using var arena = new AnalysisScratchArena(directory, budget, 1024);
        var first = arena.Append([1, 2, 3], Cancellation); using var checkpoint = arena.Capture(); long before = budget.ReservedBytes;
        directory.File.FailWrite = true;
        Assert.Throws<IOException>(() => arena.Append(new byte[100], Cancellation));
        Assert.True(arena.Failed); Assert.Equal(before + 148, budget.ReservedBytes);
        Assert.Equal(new byte[] { 1, 2, 3 }, checkpoint.Read(first, Cancellation));
        int calls = directory.File.Writes;
        Assert.Throws<IOException>(() => arena.Append([4], Cancellation)); Assert.Equal(calls, directory.File.Writes);
        arena.Dispose(); Assert.False(directory.Disposed); Assert.True(budget.ReservedBytes > before);
        checkpoint.Dispose(); Assert.True(directory.Disposed); Assert.Equal(0, budget.ReservedBytes);
    }

    [Fact]
    public void FreeSpaceReserveStopsBeforeAnotherWriteAndKeepsPublishedPrefix()
    {
        Ordinary(); var directory = new FakeDirectory(); using var arena = new AnalysisScratchArena(directory);
        var first = arena.Append([1], Cancellation); using var checkpoint = arena.Capture(); int calls = directory.File.Writes;
        directory.Available = 256UL * 1024 * 1024;
        Assert.Equal(AnalysisScratchLimit.FreeSpaceReserve, Assert.Throws<AnalysisScratchLimitException>(() => arena.Append([2], Cancellation)).Limit);
        Assert.Equal(calls, directory.File.Writes); Assert.True(arena.Failed); Assert.Equal(new byte[] { 1 }, checkpoint.Read(first, Cancellation));
    }

    [Fact]
    public void SharedQuotaIncludesReaderRetainedFilesAndSessionLimitIsIndependent()
    {
        Ordinary(); var budget = new AnalysisScratchBudget(100);
        using var first = new AnalysisScratchArena(new FakeDirectory(), budget, 100);
        first.Append([1], Cancellation); using var checkpoint = first.Capture(); first.Dispose();
        using var second = new AnalysisScratchArena(new FakeDirectory(), budget, 100);
        Assert.Equal(AnalysisScratchLimit.Budget, Assert.Throws<AnalysisScratchLimitException>(() => second.Append([2, 3, 4, 5], Cancellation)).Limit);
        Assert.Equal(49, budget.ReservedBytes);
        checkpoint.Dispose(); Assert.Equal(0, budget.ReservedBytes);
        using var third = new AnalysisScratchArena(new FakeDirectory(), budget, 50);
        third.Append([1], Cancellation);
        Assert.Equal(AnalysisScratchLimit.Budget, Assert.Throws<AnalysisScratchLimitException>(() => third.Append([], Cancellation)).Limit);
        Assert.Equal(49, budget.ReservedBytes);
    }

    [Fact]
    public void PreAdmissionCancellationAndOversizedPayloadDoNotConsumeScratch()
    {
        Ordinary(); var directory = new FakeDirectory(); var budget = new AnalysisScratchBudget(2_000_000);
        using var arena = new AnalysisScratchArena(directory, budget, 2_000_000);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.Throws<OperationCanceledException>(() => arena.Append([1], cancel.Token));
        Assert.Throws<ArgumentOutOfRangeException>(() => arena.Append(new byte[AnalysisLimits.RecordBytes + 1], Cancellation));
        Assert.False(arena.Failed); Assert.Equal(0, budget.ReservedBytes); Assert.Equal(0, directory.File.Writes);
        var empty = arena.Append([], Cancellation); using var checkpoint = arena.Capture(); Assert.Empty(checkpoint.Read(empty, Cancellation));
    }

    [Fact]
    public void NativeCreationFailureClosesTheTransferredDirectoryAndFile()
    {
        Ordinary(); var directory = new FakeDirectory(); directory.File.Data.WriteByte(1);
        Assert.Throws<IOException>(() => new AnalysisScratchArena(directory));
        Assert.True(directory.Disposed); Assert.True(directory.File.Disposed);
    }

    [Fact]
    public void RevisionLeaseLimitIsBoundedAndReleasedLeasesCanBeReplaced()
    {
        Ordinary(); using var arena = new AnalysisScratchArena(new FakeDirectory());
        var checkpoints = Enumerable.Range(0, AnalysisLimits.Sessions).Select(_ => arena.Capture()).ToArray();
        try
        {
            Assert.Throws<IOException>(() => arena.Capture()); checkpoints[0].Dispose();
            using var replacement = arena.Capture();
        }
        finally { foreach (var checkpoint in checkpoints) checkpoint.Dispose(); }
    }

    [Fact]
    public async Task CanceledBlockedNativeWriteKeepsAdmissionAndQuotaUntilActualReturnAndReaderRelease()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); using var cancel = new CancellationTokenSource();
        var directory = new FakeDirectory(); var budget = new AnalysisScratchBudget(1024);
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        using var arena = new AnalysisScratchArena(directory, budget, 1024);
        var first = arena.Append([1], Cancellation); using var checkpoint = arena.Capture();
        directory.File.BeforeWrite = () => { entered.Set(); if (!release.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException(); };
        string device = "analysis-scratch-fixture-" + Guid.NewGuid().ToString("N");
        var actual = AnalysisIo.Run(io, device, token => arena.Append([2], token), cancel.Token);
        Task<bool>? closing = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5), Cancellation)); cancel.Cancel();
            closing = AnalysisIo.Run(io, device, _ => { arena.Dispose(); return true; }, Cancellation);
            Assert.False(actual.IsCompleted); Assert.False(closing.IsCompleted);
            Assert.False(directory.Disposed); Assert.Equal(98, budget.ReservedBytes);
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual.WaitAsync(TimeSpan.FromSeconds(10), Cancellation));
        Assert.True(await closing!.WaitAsync(TimeSpan.FromSeconds(10), Cancellation));
        Assert.Equal(new byte[] { 1 }, checkpoint.Read(first, Cancellation)); Assert.False(directory.Disposed);
        checkpoint.Dispose(); Assert.Equal(0, budget.ReservedBytes); Assert.True(directory.Disposed);
    }

    private sealed class FakeDirectory : AnalysisPrivateDirectory
    {
        internal readonly FakeFile File = new();
        internal bool Disposed, FailClose; internal int Creates;
        internal UInt128 Available = (UInt128)1024 * 1024 * 1024;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Disposed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, Available);
        internal override AnalysisPrivateScratchFile CreateScratch() { Creates++; return File; }
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { Disposed = true; if (FailClose) throw new IOException("Injected directory close result."); }
    }
    private sealed class FakeFile : AnalysisPrivateScratchFile
    {
        internal readonly MemoryStream Data = new();
        internal bool Disposed, FailWrite, FailClose;
        internal int Writes;
        internal Action? BeforeWrite;
        public override long Length => Data.Length;
        public override void Verify() => ObjectDisposedException.ThrowIf(Disposed, this);
        public override int ReadAt(Span<byte> buffer, long offset) { Data.Position = offset; return Data.Read(buffer[..Math.Min(7, buffer.Length)]); }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset)
        {
            Writes++; BeforeWrite?.Invoke(); Data.Position = offset;
            if (FailWrite) { Data.Write(buffer[..Math.Min(3, buffer.Length)]); FailWrite = false; throw new IOException("Injected partial native write."); }
            Data.Write(buffer);
        }
        public override void Dispose() { Disposed = true; Data.Dispose(); if (FailClose) throw new IOException("Injected file close result."); }
    }
}
