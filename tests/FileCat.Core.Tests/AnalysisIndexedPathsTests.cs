using System.Collections.Immutable;
using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

public sealed class AnalysisIndexedPathsTests
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-06T00:00:00Z");
    private static SessionIdentity Acquisition => new(Guid.NewGuid(), 1, 0, "fixture", Start, Start) { TimeZoneId = "UTC" };
    private static readonly ImmutableArray<RootBinding> Roots =
    [
        new("a", "file", "/source", "/source", null, IdentityAssurance.Unknown, "source") { NameRules = AnalysisNameRules.Unix },
        new("b", "file", "/another", "/another", null, IdentityAssurance.Unknown, "another") { NameRules = AnalysisNameRules.Unix }
    ];
    private static void Ordinary() => Assert.SkipWhen(AnalysisAdmission.Refusal() is not null, "Needs an ordinary process.");

    [Fact]
    public void PendingParentsAreReadableButPublishedLineagesAndAcquisitionStayImmutableAfterWriterClosure()
    {
        Ordinary(); var directory = new MemoryDirectory(); using var arena = new AnalysisScratchArena(directory); var identity = Acquisition;
        using var paths = new AnalysisIndexedPaths(arena, identity, Roots);
        paths.Append(new(1, "a", 0, "source", AnalysisKind.Directory), Cancellation);
        paths.Append(new(2, "a", 1, "folder", AnalysisKind.Directory), Cancellation);
        paths.Append(new(3, "a", 2, "café 😀.txt", AnalysisKind.File, "object-1") { Times = new(Start.AddSeconds(2), null, 1) }, Cancellation);
        using var empty = paths.CaptureCommitted(identity); Assert.Equal(0UL, empty.Count);
        paths.Flush(Cancellation); using var old = paths.CaptureCommitted(identity with { Revision = 1, ObservedUtc = Start.AddSeconds(10) });
        Assert.Equal(new[] { "source", "folder", "café 😀.txt" }, old.ReadLineage(3, Cancellation).Select(n => n.Name));
        paths.Append(new(4, "b", 0, "another", AnalysisKind.Directory), Cancellation);
        paths.Append(new(5, "b", 4, "next.txt", AnalysisKind.File, "object-2"), Cancellation); paths.Flush(Cancellation);
        using var current = paths.CaptureCommitted(identity with { Revision = 2, ObservedUtc = Start.AddSeconds(20) });
        Assert.Equal(3UL, old.Count); Assert.Equal(5UL, current.Count); Assert.Throws<ArgumentOutOfRangeException>(() => old.Read(5, Cancellation));
        Assert.Equal(new ulong[] { 4, 5 }, current.ReadLineage(5, Cancellation).Select(n => n.Id));
        paths.Dispose(); Assert.False(directory.Closed); Assert.Equal("object-1", old.Read(3, Cancellation).ObjectId);
        Assert.Equal(identity.SessionId, old.Identity.SessionId); Assert.Equal(identity.Epoch, old.Identity.Epoch);
        empty.Dispose(); old.Dispose(); current.Dispose(); Assert.True(directory.Closed);
    }

    [Fact]
    public void ParentCyclesCrossRootChildrenAndUnreadableParentsCannotEnterThePathStore()
    {
        Ordinary(); using var arena = new AnalysisScratchArena(new MemoryDirectory()); using var paths = new AnalysisIndexedPaths(arena, Acquisition, Roots);
        paths.Append(new(1, "a", 0, "source", AnalysisKind.Directory), Cancellation);
        paths.Append(new(2, "a", 1, "file", AnalysisKind.File, "object-1"), Cancellation);
        foreach (var invalid in new[]
        {
            new AnalysisNode(3, "a", 3, "self", AnalysisKind.Directory), new AnalysisNode(3, "a", 9, "future", AnalysisKind.Directory),
            new AnalysisNode(3, "a", 2, "under-file", AnalysisKind.Directory), new AnalysisNode(3, "b", 1, "cross-root", AnalysisKind.Directory),
            new AnalysisNode(3, "a", 0, "duplicate-root", AnalysisKind.Directory), new AnalysisNode(4, "a", 1, "skipped-id", AnalysisKind.Directory)
        }) Assert.Throws<InvalidDataException>(() => paths.Append(invalid, Cancellation));
        Assert.Equal(2UL, paths.AcceptedCount);
        paths.Append(new(3, "a", 1, "excluded", AnalysisKind.Directory) { MetadataState = MeasurementState.Excluded, IncludedByPolicy = false }, Cancellation);
        Assert.Throws<InvalidDataException>(() => paths.Append(new(4, "a", 3, "inside-exclusion", AnalysisKind.Directory), Cancellation));
        paths.Append(new(4, "a", 1, "denied", AnalysisKind.Directory) { MetadataState = MeasurementState.Failed }, Cancellation);
        Assert.Throws<InvalidDataException>(() => paths.Append(new(5, "a", 4, "inside-denied", AnalysisKind.Directory), Cancellation));
    }

    [Fact]
    public void EpochProfileAndRevisionCannotRetagExistingDenseIds()
    {
        Ordinary(); using var arena = new AnalysisScratchArena(new MemoryDirectory()); var identity = Acquisition;
        using var paths = new AnalysisIndexedPaths(arena, identity, Roots);
        paths.Append(new(1, "a", 0, "source", AnalysisKind.Directory) { Times = new(Start.AddSeconds(5), null, 1) }, Cancellation); paths.Flush(Cancellation);
        Assert.Throws<InvalidOperationException>(() => paths.CaptureCommitted(identity with { Revision = 1, ObservedUtc = Start.AddSeconds(4) }));
        var published = identity with { Revision = 1, ObservedUtc = Start.AddSeconds(10) }; using var old = paths.CaptureCommitted(published);
        foreach (var invalid in new[]
        {
            published with { SessionId = Guid.NewGuid() }, published with { Epoch = 2 }, published with { HostProfile = "other" },
            published with { StartedUtc = Start.AddSeconds(-1) }, published with { TimeZoneId = "another-zone" },
            published with { Revision = 0 }, published with { ObservedUtc = Start.AddSeconds(11) }
        }) Assert.Throws<InvalidOperationException>(() => paths.CaptureCommitted(invalid));
        using var same = paths.CaptureCommitted(published); Assert.Equal(old.Identity, same.Identity); Assert.Equal(old.Count, same.Count);
        paths.Append(new(2, "a", 1, "new.txt", AnalysisKind.File, "object-1"), Cancellation); paths.Flush(Cancellation);
        Assert.Throws<InvalidOperationException>(() => paths.CaptureCommitted(published));
        using var next = paths.CaptureCommitted(published with { Revision = 2 }); Assert.Equal(2UL, next.Count); Assert.Equal(1UL, old.Count);
    }

    [Fact]
    public void MaximumDepthIsExplicitAndAnOversizedLocatorDoesNotTruncateOrLoseItsLeaf()
    {
        Ordinary(); var identity = Acquisition;
        using var arena = new AnalysisScratchArena(new MemoryDirectory()); using var paths = new AnalysisIndexedPaths(arena, identity, Roots);
        paths.Append(new(1, "a", 0, "source", AnalysisKind.Directory), Cancellation);
        for (ulong id = 2; id <= AnalysisLimits.Depth + 1; id++) paths.Append(new(id, "a", id - 1, "x", AnalysisKind.Directory), Cancellation);
        Assert.Throws<InvalidDataException>(() => paths.Append(new(258, "a", 257, "too-deep", AnalysisKind.Directory), Cancellation));
        paths.Flush(Cancellation); using var deep = paths.CaptureCommitted(identity with { Revision = 1 }); Assert.Equal(257, deep.ReadLineage(257, Cancellation).Length);
        using var otherArena = new AnalysisScratchArena(new MemoryDirectory()); using var wide = new AnalysisIndexedPaths(otherArena, identity, Roots);
        wide.Append(new(1, "a", 0, "source", AnalysisKind.Directory), Cancellation);
        for (ulong id = 2; id <= 5; id++) wide.Append(new(id, "a", id - 1, new string((char)('a' + id), AnalysisLimits.RawNameBytes), AnalysisKind.Directory), Cancellation);
        wide.Flush(Cancellation); using var large = wide.CaptureCommitted(identity with { Revision = 1 });
        Assert.Equal(AnalysisLimits.RawNameBytes, large.Read(5, Cancellation).Name.Length);
        Assert.Throws<InvalidDataException>(() => large.ReadLineage(5, Cancellation)); Assert.Equal(5UL, large.Count);
    }

    [Fact]
    public void ImportedPrivacyIsJoinedAndCannotLosePrivilegedOrUnverifiedClassification()
    {
        Ordinary(); using var arena = new AnalysisScratchArena(new MemoryDirectory()); var identity = Acquisition;
        using var paths = new AnalysisIndexedPaths(arena, identity, Roots, AnalysisOrigin.ImportedInventory, AnalysisTaint.PrivilegedDerived);
        paths.Append(new(1, "a", 0, "source", AnalysisKind.Directory), Cancellation); paths.Flush(Cancellation);
        using var revision = paths.CaptureCommitted(identity with { Revision = 1 });
        Assert.Equal(AnalysisOrigin.ImportedInventory, revision.Origin);
        Assert.Equal(AnalysisTaint.OrdinarySensitive | AnalysisTaint.PrivilegedDerived | AnalysisTaint.ImportedUnverified, revision.Taint);
        Assert.Equal(1UL, revision.Read(1, Cancellation).Id);
    }

    private sealed class MemoryDirectory : AnalysisPrivateDirectory
    {
        private readonly MemoryFile _file = new(); internal bool Closed;
        internal override void Verify() => ObjectDisposedException.ThrowIf(Closed, this);
        internal override (UInt128 Capacity, UInt128 Available) Space() => ((UInt128)1024 * 1024 * 1024, (UInt128)1024 * 1024 * 1024);
        internal override AnalysisPrivateScratchFile CreateScratch() => _file;
        internal override AnalysisPrivateFile CreateStaging() => throw new NotSupportedException();
        internal override AnalysisPrivateFile OpenRead(string name) => throw new NotSupportedException();
        public override void Dispose() { Closed = true; }
    }
    private sealed class MemoryFile : AnalysisPrivateScratchFile
    {
        private readonly MemoryStream _bytes = new();
        public override long Length => _bytes.Length;
        public override void Verify() { }
        public override int ReadAt(Span<byte> buffer, long offset) { _bytes.Position = offset; return _bytes.Read(buffer); }
        public override void WriteAt(ReadOnlySpan<byte> buffer, long offset) { _bytes.Position = offset; _bytes.Write(buffer); }
        public override void Dispose() => _bytes.Dispose();
    }
}
