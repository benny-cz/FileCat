using System.Collections.Immutable;
using FileCat.Core.Analysis;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

public sealed partial class AnalysisDatasetSummaryTests
{
    [Fact]
    public async Task IndexedComparisonPathsMatchRawComponentsUnderMatchedParentsWithArbitraryIds()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var old = AnalysisAccountingTests.Golden();
        var now = old with { Nodes = old.Nodes.Select(n => n with { Id = n.Id * 17, ParentId = n.ParentId * 17 }).ToImmutableArray() };
        await using var a = await Build(old, io); await using var b = await Build(now, io, arbitraryPathIds: true);
        await using var before = await a.AcquireAsync(Cancellation); await using var current = await b.AcquireAsync(Cancellation);
        await using var paths = await current.MatchPathsAsync(before, cancellation: Cancellation);
        Assert.Equal(before.Descriptor.Identity, paths.Previous.Identity); Assert.Equal(current.Descriptor.Identity, paths.Current.Identity);
        var page = await paths.ReadCurrentAsync(cancellation: Cancellation); Assert.Equal(6UL, page.Total);
        Assert.All(page.Rows, pair => { Assert.True(pair.Mapped); Assert.Equal(pair.Previous!.Id * 17, pair.Current!.Id); });
        Assert.Equal(page.Rows, (await paths.ReadPreviousAsync(cancellation: Cancellation)).Rows);
    }

    [Theory]
    [InlineData("Name", "name", "utf8", "utf8")]
    [InlineData("é", "e\u0301", "utf8", "utf8")]
    [InlineData("same", "same", "utf8", "unix-bytes")]
    [InlineData("unpaired:D800", "unpaired:D801", "utf16le", "utf16le")]
    [InlineData("trailing.", "trailing", "utf16le", "utf16le")]
    public async Task IndexedComparisonPathsNeverNormalizeCaseUnicodeEncodingOrRawNames(string oldName, string newName, string oldEncoding, string newEncoding)
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var original = AnalysisAccountingTests.Golden();
        // Attribute/discovery transport uses UTF-8 and cannot carry an unpaired code unit.
        // Construct it in the test so the two actual native raw inputs remain distinct.
        static string NativeName(string text) => text.StartsWith("unpaired:", StringComparison.Ordinal)
            ? "bad" + (char)Convert.ToUInt16(text[9..], 16) : text;
        oldName = NativeName(oldName); newName = NativeName(newName);
        AnalysisSnapshot One(string name, string encoding) => original with
        {
            Nodes = [original.Nodes[0], original.Nodes[3] with { Id = 2, ParentId = 1, Name = name, NameEncoding = encoding,
                RawName = AnalysisRawNames.Encode(encoding, name).ToImmutableArray() }],
            Objects = [original.Objects[0] with { ObservedAliases = [2], LinkCount = AnalysisMeasurement.Known(1) }], Facts = [original.Facts[0]]
        };
        var old = One(oldName, oldEncoding); var nowSnapshot = One(newName, newEncoding);
        Assert.False(oldEncoding == newEncoding && old.Nodes[1].RawName.AsSpan().SequenceEqual(nowSnapshot.Nodes[1].RawName.AsSpan()));
        await using var a = await Build(old, io); await using var b = await Build(nowSnapshot, io);
        await using var before = await a.AcquireAsync(Cancellation); await using var current = await b.AcquireAsync(Cancellation);
        await using var paths = await current.MatchPathsAsync(before, cancellation: Cancellation);
        var now = (await paths.ReadCurrentAsync(cancellation: Cancellation)).Rows;
        Assert.NotNull(now[0].Previous); Assert.Null(now[1].Previous); Assert.True(now[1].Mapped);
        Assert.Null((await paths.ReadPreviousAsync(cancellation: Cancellation)).Rows[1].Current);
    }

    [Fact]
    public async Task IndexedComparisonPathsDoNotMatchAChildThroughAnUnmatchedOrDifferentParent()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var old = AnalysisAccountingTests.Golden();
        var now = old with { Nodes = old.Nodes.SetItem(1, old.Nodes[1] with { Name = "renamed-A" }) };
        await using var a = await Build(old, io); await using var b = await Build(now, io);
        await using var before = await a.AcquireAsync(Cancellation); await using var current = await b.AcquireAsync(Cancellation);
        await using var paths = await current.MatchPathsAsync(before, cancellation: Cancellation);
        var rows = (await paths.ReadCurrentAsync(cancellation: Cancellation)).Rows;
        Assert.Null(rows[1].Previous); Assert.Null(rows[3].Previous); Assert.Null(rows[5].Previous);
        Assert.Equal(5UL, rows[4].Previous!.Id); // Same alias identity elsewhere is not path continuity.
    }

    [Fact]
    public async Task IndexedComparisonPathsPreserveInvalidUnixBytesEvenWhenDisplayTextCouldBeConfused()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        AnalysisSnapshot One(byte value) => s with
        {
            Nodes = [s.Nodes[0], s.Nodes[3] with { Id = 2, ParentId = 1, NameEncoding = "unix-bytes",
                Name = AnalysisRawNames.Decode("unix-bytes", [value]), RawName = [value] }],
            Objects = [s.Objects[0] with { ObservedAliases = [2], LinkCount = AnalysisMeasurement.Known(1) }], Facts = [s.Facts[0]]
        };
        await using var a = await Build(One(0xff), io); await using var b = await Build(One(0xfe), io);
        await using var before = await a.AcquireAsync(Cancellation); await using var current = await b.AcquireAsync(Cancellation);
        await using var paths = await current.MatchPathsAsync(before, cancellation: Cancellation);
        Assert.Null((await paths.ReadCurrentAsync(1, cancellation: Cancellation)).Rows.Single().Previous);
        Assert.Equal(new byte[] { 0xff }, (await paths.ReadPreviousAsync(1, cancellation: Cancellation)).Rows.Single().Previous!.RawName.ToArray());
    }

    [Fact]
    public async Task IndexedComparisonPathsDoNotExpandDeepPrefixesIntoTheMemoryFallbackPathBudget()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden();
        var nodes = ImmutableArray.CreateBuilder<AnalysisNode>(); nodes.Add(s.Nodes[0]);
        for (ulong id = 2; id <= 128; id++) nodes.Add(new(id, "r", id - 1, new string('a', 1024), AnalysisKind.Directory));
        s = s with { Nodes = nodes.ToImmutable(), Objects = [], Facts = [] };
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(s, s, AnalysisMetric.Files, cancellation: Cancellation));
        await using var a = await Build(s, io); await using var before = await a.AcquireAsync(Cancellation);
        await using var paths = await before.MatchPathsAsync(before, cancellation: Cancellation);
        var tail = Assert.Single((await paths.ReadCurrentAsync(127, cancellation: Cancellation)).Rows);
        Assert.Equal(128UL, tail.Previous!.Id); Assert.Equal(128UL, tail.Current!.Id);
    }

    [Fact]
    public async Task IndexedComparisonPathsCancellationInsideActualReadWaitsForCleanupAndAllowsRetry()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden(); var directory = new MemoryDirectory();
        await using var a = await Build(s, io); await using var b = await Build(s, io, directory);
        await using var before = await a.AcquireAsync(Cancellation); await using var current = await b.AcquireAsync(Cancellation);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        directory.BeforeNextRead = cancel.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => current.MatchPathsAsync(before, cancellation: cancel.Token));
        Assert.True(cancel.IsCancellationRequested);
        await using var paths = await current.MatchPathsAsync(before, cancellation: Cancellation);
        Assert.Equal(6UL, (await paths.ReadCurrentAsync(cancellation: Cancellation)).Total);
    }

    [Fact]
    public async Task IndexedComparisonPathsRequireReviewedMappingForDifferentCapturedScopesAndKeepUnmappedRootsExplicit()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var old = AnalysisAccountingTests.Golden();
        var now = old with
        {
            Roots = [old.Roots[0] with { Id = "new", CanonicalLocator = "Elsewhere" }, old.Roots[0] with { Id = "unmapped", CanonicalLocator = "Other" }],
            Nodes = old.Nodes.Select(n => n with { RootId = "new" }).Append(new(7, "unmapped", 0, "Other", AnalysisKind.Directory)).ToImmutableArray()
        };
        await using var a = await Build(old, io); await using var b = await Build(now, io);
        await using var before = await a.AcquireAsync(Cancellation); await using var current = await b.AcquireAsync(Cancellation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => current.MatchPathsAsync(before, cancellation: Cancellation));
        await Assert.ThrowsAsync<ArgumentException>(() => current.MatchPathsAsync(before, new Dictionary<string, string> { ["missing"] = "new" }, Cancellation));
        await using var paths = await current.MatchPathsAsync(before, new Dictionary<string, string> { ["r"] = "new" }, Cancellation);
        var rows = (await paths.ReadCurrentAsync(cancellation: Cancellation)).Rows;
        Assert.All(rows.Take(6), p => { Assert.True(p.Mapped); Assert.NotNull(p.Previous); });
        Assert.False(rows[6].Mapped); Assert.Null(rows[6].Previous);
    }

    [Fact]
    public async Task IndexedComparisonPathsRetainBothStoresAndAdmissionUntilTheActualResultCloses()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden(); var left = new MemoryDirectory(); var right = new MemoryDirectory();
        var a = await Build(s, io, left); var b = await Build(s, io, right);
        var before = await a.AcquireAsync(Cancellation); var current = await b.AcquireAsync(Cancellation);
        var paths = await current.MatchPathsAsync(before, cancellation: Cancellation);
        await Assert.ThrowsAsync<InvalidOperationException>(() => current.MatchPathsAsync(before, cancellation: Cancellation));
        await a.DisposeAsync(); await b.DisposeAsync(); await before.DisposeAsync(); await current.DisposeAsync();
        try
        {
            Assert.False(left.Closed); Assert.False(right.Closed);
            Assert.All((await paths.ReadPreviousAsync(cancellation: Cancellation)).Rows, pair => Assert.NotNull(pair.Current));
        }
        finally { await paths.DisposeAsync(); }
        Assert.True(left.Closed); Assert.True(right.Closed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => paths.ReadCurrentAsync(cancellation: Cancellation));
    }

    [Fact]
    public async Task IndexedComparisonPathsReleaseFailedOrCanceledConstructionWithoutPublishingAPartialIndex()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden(); var directory = new MemoryDirectory();
        await using var a = await Build(s, io); await using var b = await Build(s, io, directory);
        await using var before = await a.AcquireAsync(Cancellation); await using var current = await b.AcquireAsync(Cancellation);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => current.MatchPathsAsync(before, cancellation: cancel.Token));
        directory.BeforeNextRead = () => throw new IOException("injected comparison read failure");
        await Assert.ThrowsAsync<IOException>(() => current.MatchPathsAsync(before, cancellation: Cancellation));
        await using var valid = await current.MatchPathsAsync(before, cancellation: Cancellation);
        Assert.Equal(6UL, (await valid.ReadCurrentAsync(cancellation: Cancellation)).Total);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => valid.ReadCurrentAsync(7, cancellation: Cancellation));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => valid.ReadPreviousAsync(maximum: AnalysisLimits.BatchRecords + 1, cancellation: Cancellation));
    }

    [Fact]
    public async Task IndexedComparisonPathsPageBeyondTheMemoryComparisonInputLimitWithoutFullPathExpansion()
    {
        Ordinary(); using var io = new DeviceIoScheduler(); var s = AnalysisAccountingTests.Golden(); const int count = 14000;
        AnalysisSnapshot Scope(int start) => s with
        {
            Nodes = ImmutableArray.Create(s.Nodes[0]).AddRange(Enumerable.Range(start, count).Select((name, row) =>
                new AnalysisNode((ulong)row + 2, "r", 1, "entry-" + name.ToString("D6"), AnalysisKind.File, "X"))),
            Objects = [s.Objects[0] with { ObservedAliases = [], LinkCount = AnalysisMeasurement.Known(count) }], Facts = [s.Facts[0]]
        };
        var old = Scope(0); var now = Scope(100);
        Assert.Throws<InvalidOperationException>(() => AnalysisComparison.Build(old, now, AnalysisMetric.FileSize, cancellation: Cancellation));
        await using var a = await Build(old, io); await using var b = await Build(now, io);
        await using var before = await a.AcquireAsync(Cancellation); await using var current = await b.AcquireAsync(Cancellation);
        await using var paths = await current.MatchPathsAsync(before, cancellation: Cancellation);
        foreach (bool previous in new[] { false, true })
        {
            ulong at = 0, matched = 0, absent = 0;
            do
            {
                var page = previous ? await paths.ReadPreviousAsync(at, 113, Cancellation) : await paths.ReadCurrentAsync(at, 113, Cancellation);
                Assert.Equal((ulong)count + 1, page.Total); Assert.InRange(page.Rows.Length, 1, 113);
                foreach (var pair in page.Rows)
                    if (pair.Previous is not null && pair.Current is not null) { Assert.Equal(pair.Previous.Name, pair.Current.Name); matched++; }
                    else absent++;
                Assert.True(page.NextOffset > at); at = page.NextOffset;
                if (!page.HasMore) break;
            } while (true);
            Assert.Equal((ulong)count - 99, matched); Assert.Equal(100UL, absent);
        }
    }
}
