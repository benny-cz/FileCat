using FileCat.Core.Analysis;

namespace FileCat.Core.Tests;

[Collection("Analysis bounded work capacity")]
public sealed class AnalysisScratchReadCacheTests
{
    [Fact]
    public void SharedBudgetEvictsLeastRecentlyUsedBlocksAndSeparatesOwners()
    {
        var cache = new AnalysisScratchReadCache(1024); AnalysisScratchReadCache.Owner a = new(), b = new();
        cache.Put(a, 0, new byte[200]); cache.Put(b, 0, Enumerable.Repeat((byte)1, 200).ToArray());
        Assert.True(cache.TryGet(a, 0, out var first)); Assert.Equal(0, first[0]);
        cache.Put(b, 1, Enumerable.Repeat((byte)2, 200).ToArray());
        Assert.False(cache.TryGet(b, 0, out _)); Assert.True(cache.TryGet(a, 0, out _)); Assert.True(cache.TryGet(b, 1, out var second)); Assert.Equal(2, second[0]);
        Assert.InRange(cache.Snapshot.ReservedBytes, 1, 1024); Assert.Equal(2, cache.Snapshot.Entries);
        cache.RemoveOwner(a); Assert.False(cache.TryGet(a, 0, out _)); Assert.True(cache.TryGet(b, 1, out _));
        cache.RemoveOwner(b); Assert.Equal((0L, 0), cache.Snapshot);
        cache.Put(a, 0, new byte[1024]); Assert.Equal((0L, 0), cache.Snapshot);
    }

    [Fact]
    public void TinyBlocksCannotBypassTheEntryCountLimit()
    {
        var cache = new AnalysisScratchReadCache(AnalysisScratchReadCache.MaximumBytes); var owner = new AnalysisScratchReadCache.Owner();
        for (int at = 0; at < AnalysisScratchReadCache.MaximumEntries + 100; at++) cache.Put(owner, at, [1]);
        Assert.Equal(AnalysisScratchReadCache.MaximumEntries, cache.Snapshot.Entries); Assert.False(cache.TryGet(owner, 0, out _));
        Assert.True(cache.TryGet(owner, AnalysisScratchReadCache.MaximumEntries + 99, out _)); cache.RemoveOwner(owner); Assert.Equal((0L, 0), cache.Snapshot);
    }

    [Fact]
    public async Task ConcurrentOwnersCannotOverdrawOrStrandTheirReservations()
    {
        var cache = new AnalysisScratchReadCache(8192); var owners = Enumerable.Range(0, 8).Select(_ => new AnalysisScratchReadCache.Owner()).ToArray();
        await Task.WhenAll(owners.Select(owner => Task.Run(() =>
        {
            for (int at = 0; at < 500; at++)
            {
                cache.Put(owner, at, new byte[512]); _ = cache.TryGet(owner, at, out _);
                Assert.InRange(cache.Snapshot.ReservedBytes, 0, 8192); Assert.InRange(cache.Snapshot.Entries, 0, AnalysisScratchReadCache.MaximumEntries);
            }
        }, TestContext.Current.CancellationToken)));
        foreach (var owner in owners) cache.RemoveOwner(owner); Assert.Equal((0L, 0), cache.Snapshot);
    }
}
