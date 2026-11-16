using System.Globalization;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;

namespace FileCat.Core.Tests;

public sealed class MetadataOrderingSnapshotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Bulk_scan_materialization_preserves_fixed_fields_and_in_memory_provider_payloads(bool spilled)
    {
        using var scratch = new TempDir();
        using var store = new EntryStore(scratch.Path, spilled ? 1 : long.MaxValue);
        object payload = new();
        var entry = new EntryData("scan-name", EntryKind.File, 123, 456)
        { Created = 789, Attributes = 42, Flags = EntryFlags.Offline, Tag = spilled ? null : payload };
        store.Append(Enumerable.Repeat(entry, 300).ToArray());
        Assert.Equal(spilled, store.IsSpilled);
        using var scan = new EntryStore.Scan(store, store.Count);
        Assert.Equal(entry, scan.Materialize(299));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Comparisons_freeze_every_key_before_sorting_and_keep_unknowns_last(bool descending)
    {
        using var store = new EntryStore();
        store.Append([new("..", EntryKind.Parent), new("folder", EntryKind.Directory),
            new("two", EntryKind.File), new("ten", EntryKind.File), new("unknown", EntryKind.File)]);
        int calls = 0;
        bool captured = false;
        using var comparer = EntrySorter.CreateComparer(store,
            new SortSpec(SortField.Metadata, descending) { MetadataId = "owner" }, (_, i, _) =>
            {
                Assert.False(captured);
                calls++;
                return i == 4 ? null : i;
            }, store.Count);
        captured = true;
        int[] indices = [4, 3, 2, 1, 0];
        StableSort.Sort(indices, comparer.Comparison);
        Assert.Equal(descending ? [0, 1, 3, 2, 4] : new[] { 0, 1, 2, 3, 4 }, indices);
        Assert.Equal(store.Count, calls);
    }

    [Theory]
    [InlineData("row")]
    [InlineData("characters")]
    [InlineData("bytes")]
    public void Each_capacity_stops_admission_and_unknown_tail_cannot_be_filled_later(string limit)
    {
        var budget = new IndexMemoryBudget(limit == "bytes" ? 13400 : 1_000_000);
        using var snapshot = new MetadataOrderSnapshot(budget, limit == "row" ? 3 : 100,
            limit == "characters" ? 12 : 10_000);
        for (int i = 0; i < 100; i++) snapshot.TryAdd(i, new(MetadataState.Available, "abcd"), i);
        Assert.InRange(snapshot.Count, 1, 3);
        Assert.Equal(limit switch { "row" => MetadataCapacity.Rows, "characters" => MetadataCapacity.Characters,
            _ => MetadataCapacity.SharedMemory }, snapshot.Capacity);
        Assert.False(snapshot.TryValue(99, out _));
        snapshot.Seal();
        Assert.Null(snapshot.Key(99));
        Assert.Throws<InvalidOperationException>(() => snapshot.TryAdd(99, new(MetadataState.Available, "x"), 1));
        Assert.InRange(budget.ReservedBytes, 1, budget.LimitBytes);
    }

    [Fact]
    public void Detached_snapshots_keep_shared_reservations_until_the_last_actual_worker_releases()
    {
        var budget = new IndexMemoryBudget(27000);
        var first = new MetadataOrderSnapshot(budget);
        Assert.True(first.TryAdd(0, new(MetadataState.Available, "first"), 1));
        var oldWorker = first.Lease();
        first.Dispose(); first.Dispose();
        long retiringBytes = budget.ReservedBytes;
        Assert.True(retiringBytes > 0);
        using var second = new MetadataOrderSnapshot(budget);
        Assert.True(second.TryAdd(0, new(MetadataState.Available, "second"), 2));
        Assert.True(budget.ReservedBytes > retiringBytes);
        Assert.False(second.TryAdd(512, new(MetadataState.Available, "third"), 3));
        Assert.Equal(1, first.Key(0));
        oldWorker.Dispose();
        Assert.Equal(second.ReservedBytes, budget.ReservedBytes);
        second.Dispose();
        Assert.Equal(0, budget.ReservedBytes);
    }

    [Fact]
    public void Numeric_principals_names_and_culture_are_immutable_and_complete_ids_break_equal_name_ties()
    {
        var budget = new IndexMemoryBudget();
        using var snapshot = new MetadataOrderSnapshot(budget);
        var culture = CultureInfo.GetCultureInfo("en-US").CompareInfo;
        snapshot.TryAdd(0, new(MetadataState.Available, new Principal(PrincipalKind.UnixUser, "10")),
            principalName: new(NameStatus.NotFound), comparison: culture);
        snapshot.TryAdd(1, new(MetadataState.Available, new Principal(PrincipalKind.UnixUser, "2")),
            principalName: new(NameStatus.NotRequested), comparison: culture);
        snapshot.TryAdd(2, new(MetadataState.Available, new Principal(PrincipalKind.UnixUser, "11")),
            principalName: new(NameStatus.Resolved, "Same"), comparison: culture);
        snapshot.TryAdd(3, new(MetadataState.Available, new Principal(PrincipalKind.UnixUser, "3")),
            principalName: new(NameStatus.Resolved, "same"), comparison: culture);
        snapshot.Seal();
        Assert.True(snapshot.Key(1)!.CompareTo(snapshot.Key(0)) < 0);
        Assert.True(snapshot.Key(3)!.CompareTo(snapshot.Key(2)) < 0);
        Assert.True(snapshot.Key(2)!.CompareTo(snapshot.Key(0)) < 0);
        Assert.Equal(4, snapshot.PrincipalNames!.Count);
    }

    [Fact]
    public void Unknown_mutable_payloads_are_never_retained()
    {
        var budget = new IndexMemoryBudget();
        using var snapshot = new MetadataOrderSnapshot(budget);
        Assert.False(snapshot.TryAdd(0, new(MetadataState.Available, new byte[1000]), 1));
        Assert.False(snapshot.TryAdd(1, default, new MutableKey()));
        Assert.Equal(0, budget.ReservedBytes);
        Assert.Null(snapshot.Key(0));
    }
    private sealed class MutableKey : IComparable { public int CompareTo(object? other) => 1; }
}
