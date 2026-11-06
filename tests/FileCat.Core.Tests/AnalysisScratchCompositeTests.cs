using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisScratchCompositeTests
{
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public void ManyRelatedTablesUseOneConsumerAndRetainExactlyTheirPublicationAfterTheWriterCloses()
    {
        Ordinary(); var directory = new MemoryDirectory(); var budget = new AnalysisScratchBudget(1024 * 1024);
        using var arena = new AnalysisScratchArena(directory, budget, 1024 * 1024);
        using var store = new AnalysisScratchComposite(arena, Enumerable.Repeat(100, AnalysisScratchComposite.MaximumTables).ToArray());
        for (int table = 0; table < AnalysisScratchComposite.MaximumTables; table++) store.Append(table, [(byte)table], Cancellation);
        using var empty = store.AcquireCommitted(); Assert.Equal(0UL, empty.Count(0));
        store.Publish(Cancellation); using var old = store.AcquireCommitted();
        for (int table = 0; table < old.TableCount; table++) Assert.Equal(new byte[] { (byte)table }, old.Read(table, 1, Cancellation));
        store.Append(0, [99], Cancellation); store.Publish(Cancellation); using var current = store.AcquireCommitted();
        Assert.Equal(1, old.Generation); Assert.Equal(2, current.Generation); Assert.Equal(1UL, old.Count(0)); Assert.Equal(2UL, current.Count(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => old.Read(0, 2, Cancellation));
        store.Dispose(); Assert.False(directory.Closed); Assert.True(budget.ReservedBytes > 0);
        using var retained = old.Retain(); old.Dispose(); current.Dispose(); empty.Dispose();
        Assert.False(directory.Closed); Assert.Equal(new byte[] { 0 }, retained.Read(0, 1, Cancellation));
        retained.Dispose(); Assert.True(directory.Closed); Assert.Equal(0, budget.ReservedBytes);
    }

    [Fact]
    public void ConsumerLimitCountsIndependentReadersEvenAfterOwnerClosure()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var arena = new AnalysisScratchArena(directory);
        using var store = new AnalysisScratchComposite(arena, 100, 100, 100);
        store.Append(0, [1], Cancellation); store.Append(1, [2], Cancellation); store.Publish(Cancellation);
        var readers = Enumerable.Range(0, AnalysisLimits.SnapshotConsumers).Select(_ => store.AcquireCommitted()).ToArray();
        try
        {
            Assert.Throws<IOException>(() => store.AcquireCommitted()); Assert.Throws<IOException>(() => readers[0].Retain());
            store.Dispose(); Assert.Throws<IOException>(() => readers[0].Retain()); readers[^1].Dispose();
            using var retained = readers[0].Retain(); Assert.Equal(new byte[] { 2 }, retained.Read(1, 1, Cancellation));
            Assert.False(directory.Closed);
        }
        finally { foreach (var reader in readers) reader.Dispose(); }
        Assert.True(directory.Closed);
    }

    [Fact]
    public void PartialFlushOfAnotherTableNeverAdvancesAnyPublishedTableOrGeneration()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var arena = new AnalysisScratchArena(directory);
        using var store = new AnalysisScratchComposite(arena, 100, 100);
        store.Append(0, [1], Cancellation); store.Append(1, [2], Cancellation); store.Publish(Cancellation);
        using var old = store.AcquireCommitted();
        store.Append(0, [3], Cancellation); store.Append(1, [4], Cancellation);
        directory.File.FailAtWrite = directory.File.Writes + 4; // first table commits; the second header partially writes.
        Assert.Throws<IOException>(() => store.Publish(Cancellation)); Assert.True(store.Failed);
        using var afterFailure = store.AcquireCommitted();
        Assert.Equal(old.Generation, afterFailure.Generation); Assert.Equal(1UL, afterFailure.Count(0)); Assert.Equal(1UL, afterFailure.Count(1));
        Assert.Equal(new byte[] { 1 }, afterFailure.Read(0, 1, Cancellation)); Assert.Equal(new byte[] { 2 }, afterFailure.Read(1, 1, Cancellation));
        Assert.Throws<ArgumentOutOfRangeException>(() => afterFailure.Read(0, 2, Cancellation));
        Assert.Throws<IOException>(() => store.Append(0, [5], Cancellation)); Assert.Throws<IOException>(() => store.Publish(Cancellation));
        store.Dispose(); Assert.Equal(new byte[] { 1 }, old.Read(0, 1, Cancellation));
    }

    [Fact]
    public void TablePublicationRequiresItsOwnArenaAndACompleteExtentButNeverOwnsTheWriter()
    {
        Ordinary(); var a = new MemoryDirectory(); using var arena = new AnalysisScratchArena(a);
        using var first = new AnalysisScratchVector(arena, ownsArena: false); using var second = new AnalysisScratchVector(arena, ownsArena: false);
        first.Append([1], Cancellation); first.Flush(Cancellation); using var early = arena.Capture();
        var publishedFirst = first.CapturePublication(); second.Append([2], Cancellation); second.Flush(Cancellation);
        var publishedSecond = second.CapturePublication();
        Assert.Throws<InvalidDataException>(() => publishedSecond.Open(early));
        using var foreignArena = new AnalysisScratchArena(new MemoryDirectory()); using var foreign = foreignArena.Capture();
        Assert.Throws<InvalidDataException>(() => publishedFirst.Open(foreign));
        using var late = arena.Capture(); using var view = publishedFirst.Open(late);
        first.Dispose(); Assert.False(a.Closed); second.Append([3], Cancellation); second.Flush(Cancellation);
        arena.Dispose(); Assert.Equal(new byte[] { 1 }, view.Read(1, Cancellation));
    }

    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        internal readonly MemoryFile File = new(); internal bool Closed;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, (UInt128)1024 * 1024 * 1024);
        internal override AnalysisPrivateScratchFile CreateScratch() => File;
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { Closed = true; }
    }
    private sealed class MemoryFile : AnalysisPrivateScratchFile
    {
        private readonly MemoryStream _bytes = new(); internal int Writes, FailAtWrite = -1;
        public override long Length => _bytes.Length;
        public override void Verify() { }
        public override int ReadAt(Span<byte> buffer, long offset) { _bytes.Position = offset; return _bytes.Read(buffer); }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset)
        {
            _bytes.Position = offset; Writes++;
            if (Writes == FailAtWrite) { _bytes.Write(buffer[..Math.Min(3, buffer.Length)]); throw new IOException("Injected partial table append."); }
            _bytes.Write(buffer);
        }
        public override void Dispose() => _bytes.Dispose();
    }
}
