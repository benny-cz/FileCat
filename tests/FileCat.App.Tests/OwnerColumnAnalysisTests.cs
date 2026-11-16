using System.Globalization;
using Avalonia.Headless.XUnit;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed partial class OwnerColumnTests
{
    private static int Sequence(string path) => int.Parse(Path.GetFileName(path).AsSpan(5, 7), CultureInfo.InvariantCulture);
    private static OwnerRead OrderedOwner(string path) => new(OwnerReadStatus.Ok,
        new Principal(PrincipalKind.UnixUser, (121 - Sequence(path)).ToString(CultureInfo.InvariantCulture)));

    [AvaloniaFact]
    public async Task Analysis_retains_all_results_beyond_the_eight_entry_cache_and_reversal_reads_nothing()
    {
        using var f = new OwnerFixture(new OwnerSource { ReadOverride = OrderedOwner }, cacheSize: 8);
        await f.Load();
        await f.Tab.AnalyzeAsync("owner");
        Assert.Equal(120, f.Source.ReadPaths.Count);
        Assert.Contains("Read Owner for 120 items: 120 current values.", f.Tab.Banner);
        Assert.Contains("retains all 120", f.Tab.Banner);
        Assert.Contains("Settings → Columns", f.Tab.Banner);
        Assert.Contains("Owner", f.Tab.SortDescription);
        var store = f.Tab.Listing.Store;
        Assert.Equal(MetadataState.NotRequested, f.Services.Metadata.Peek("owner",
            Path.Combine(f.Tab.Location!.Path, store[0].Name), store[0], false).State);
        Assert.Equal(Enumerable.Range(1, 120).Reverse(), Enumerable.Range(0, 120).Select(i => Sequence(f.Tab.Listing.GetVisible(i).Name)));
        f.Tab.SortByMetadata("owner");
        await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == f.Tab.Listing.ViewSpecVersion);
        Assert.Equal(Enumerable.Range(1, 120), Enumerable.Range(0, 120).Select(i => Sequence(f.Tab.Listing.GetVisible(i).Name)));
        Assert.Equal(120, f.Source.ReadPaths.Count);
    }

    [AvaloniaTheory]
    [InlineData("row")]
    [InlineData("character")]
    [InlineData("shared-memory")]
    public async Task Analysis_reports_actual_coverage_for_each_shrunken_capacity(string limit)
    {
        var budget = new IndexMemoryBudget(limit == "shared-memory" ? 16_000 : 2_000_000);
        using var f = new OwnerFixture(new OwnerSource { ReadOverride = OrderedOwner }, cacheSize: 8, budget: budget);
        f.Services.MetadataSnapshotMaxRows = limit == "row" ? 3 : 1000;
        f.Services.MetadataSnapshotMaxCharacters = limit == "character" ? 120 : 10_000;
        await f.Load();
        await f.Tab.AnalyzeAsync("owner");
        Assert.Equal(120, f.Source.ReadPaths.Count);
        Assert.Contains("120 current values", f.Tab.Banner);
        Assert.Contains(limit, f.Tab.Banner!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("retains all 120", f.Tab.Banner);
        Assert.InRange(f.Tab.Listing.AppliedMetadataCoverage!.Count, 0, limit == "row" ? 3 : 119);
        Assert.InRange(budget.ReservedBytes, 1, budget.LimitBytes);
        if (limit == "row")
        {
            Assert.Equal(3, f.Tab.Listing.AppliedMetadataCoverage.Count);
            Assert.Equal(new[] { 120, 119, 118 }, Enumerable.Range(0, 3).Select(i => Sequence(f.Tab.Listing.GetVisible(i).Name)));
            Assert.Equal(1, Sequence(f.Tab.Listing.GetVisible(3).Name));
        }
        f.Tab.Dispose();
        await FileListFixture.Wait(() => budget.ReservedBytes == 0);
    }

    [AvaloniaFact]
    public async Task Canceling_a_running_native_read_keeps_its_retained_buffer_reserved_until_return()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int reads = 0;
        var source = new OwnerSource { ReadOverride = path =>
        {
            if (Interlocked.Increment(ref reads) == 2) { entered.Set(); release.Wait(TestContext.Current.CancellationToken); }
            return OrderedOwner(path);
        } };
        var budget = new IndexMemoryBudget(2_000_000);
        using var f = new OwnerFixture(source, cacheSize: 8, budget: budget);
        await f.Load();
        f.Tab.SortByMetadata("owner", analyzing: true);
        await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == f.Tab.Listing.ViewSpecVersion);
        long before = budget.ReservedBytes;
        Task analysis = f.Tab.AnalyzeAsync("owner");
        try
        {
            await FileListFixture.Wait(() => entered.IsSet);
            long held = budget.ReservedBytes;
            Assert.True(held > before);
            Assert.True(f.Tab.CancelAnalysis());
            await analysis.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.Equal(held, budget.ReservedBytes);
            Assert.Contains("No retained success snapshot", f.Tab.Banner);
            release.Set();
            await FileListFixture.Wait(() => budget.ReservedBytes == before);
            Assert.Equal(2, reads);
        }
        finally { release.Set(); await analysis; }
    }

    [AvaloniaTheory]
    [InlineData(OwnerReadStatus.Ok)]
    [InlineData(OwnerReadStatus.Denied)]
    [InlineData(OwnerReadStatus.Gone)]
    [InlineData(OwnerReadStatus.Failed)]
    public async Task A_fresh_effective_publication_withdraws_retention_before_the_next_sort(OwnerReadStatus outcome)
    {
        long now = 0;
        var source = new OwnerSource { ReadOverride = OrderedOwner };
        using var f = new OwnerFixture(source, cacheSize: 8, clock: () => now);
        await f.Load();
        await f.Tab.AnalyzeAsync("owner");
        Assert.Contains("retains all 120", f.Tab.Banner);
        var store = f.Tab.Listing.Store;
        var e = store[119]; // This row is still in the eight-entry ordinary cache.
        now = 6000;
        source.ReadOverride = _ => outcome == OwnerReadStatus.Ok ? new(outcome, new Principal(PrincipalKind.UnixUser, "999")) : new(outcome, Reason: "changed fixture");
        var effective = f.Services.Metadata.Compute("owner", Path.Combine(f.Tab.Location!.Path, e.Name), e, TestContext.Current.CancellationToken);
        if (outcome == OwnerReadStatus.Failed) Assert.Equal(MetadataObservation.LastKnown, effective.Observation);
        f.Tab.SortByMetadata("owner"); // Do not wait for the throttled UI value notification.
        await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == f.Tab.Listing.ViewSpecVersion);
        Assert.InRange(f.Tab.Listing.AppliedMetadataCoverage!.Count, 0, 8);
        Assert.DoesNotContain("retains all 120", f.Tab.Banner);
        Assert.Equal(121, f.Source.ReadPaths.Count);
    }

    [AvaloniaFact]
    public async Task Appending_during_the_last_read_cannot_announce_a_whole_listing_result()
    {
        OwnerFixture? fixture = null;
        int calls = 0;
        var source = new OwnerSource { ReadOverride = path =>
        {
            if (Interlocked.Increment(ref calls) == 120) fixture!.Tab.Listing.Store.Append(new EntryData("appended", EntryKind.File));
            return OrderedOwner(path);
        } };
        using var f = fixture = new OwnerFixture(source, cacheSize: 8);
        await f.Load();
        await f.Tab.AnalyzeAsync("owner");
        Assert.Equal(120, calls);
        Assert.Contains("listing changed", f.Tab.Banner);
        Assert.DoesNotContain("retains all", f.Tab.Banner);
    }

    [AvaloniaFact]
    public async Task Invalidation_inside_the_final_native_read_cannot_publish_retained_success()
    {
        OwnerFixture? fixture = null;
        int calls = 0;
        var source = new OwnerSource { ReadOverride = path =>
        {
            if (Interlocked.Increment(ref calls) == 120) fixture!.Services.Metadata.Invalidate();
            return OrderedOwner(path);
        } };
        using var f = fixture = new OwnerFixture(source, cacheSize: 8);
        await f.Load();
        await f.Tab.AnalyzeAsync("owner");
        Assert.Equal(120, calls);
        Assert.Contains("metadata changed", f.Tab.Banner);
        Assert.DoesNotContain("retains all", f.Tab.Banner);
    }

    [AvaloniaFact]
    public async Task Esc_skips_only_name_wait_and_a_late_name_does_not_retire_the_completed_observations()
    {
        using var release = new ManualResetEventSlim();
        var source = new OwnerSource { NameOverride = _ =>
        { release.Wait(TestContext.Current.CancellationToken); return new(NameStatus.Resolved, "arrived later"); } };
        using var f = new OwnerFixture(source, cacheSize: 8);
        await f.Load();
        var analysis = f.Tab.AnalyzeAsync("owner");
        try
        {
            await FileListFixture.Wait(() => f.Tab.AnalysisStatus?.Contains("Esc sorts now", StringComparison.Ordinal) == true);
            Assert.Equal(120, source.ReadPaths.Count);
            Assert.True(f.Tab.CancelAnalysis());
            await analysis.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            Assert.Contains("retains all 120", f.Tab.Banner);
            Assert.Contains("1 name lookups are pending", f.Tab.Banner);
            int version = f.Tab.Listing.AppliedViewSpecVersion;
            release.Set();
            Assert.True(await f.Names.WhenIdle(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.Contains("retains all 120", f.Tab.Banner);
            Assert.Equal(version, f.Tab.Listing.AppliedViewSpecVersion);
            Assert.False(f.Tab.CancelAnalysis());
        }
        finally { release.Set(); await analysis; }
    }

    [AvaloniaFact]
    public async Task Two_tabs_share_capacity_without_unrelated_folder_publications_retiring_the_first_analysis()
    {
        var budget = new IndexMemoryBudget(180_000);
        using var f = new OwnerFixture(new OwnerSource { ReadOverride = OrderedOwner }, cacheSize: 8, budget: budget);
        await f.Load();
        await f.Tab.AnalyzeAsync("owner");
        Assert.Contains("retains all 120", f.Tab.Banner);
        var other = f.Tab.Panel.OpenTab(Location.FileSystem(Path.Combine(f.Root, "synthetic-other")));
        try
        {
            await FileListFixture.Wait(() => other.Listing.State == ListingState.Complete);
            await other.AnalyzeAsync("owner");
            Assert.Contains("retains all 120", f.Tab.Banner);
            Assert.Contains("Shared-memory limit", other.Banner);
            Assert.InRange(other.Listing.AppliedMetadataCoverage!.Count, 0, 119);
            Assert.InRange(budget.ReservedBytes, 1, budget.LimitBytes);
            Assert.Equal(240, f.Source.ReadPaths.Count);
        }
        finally { other.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Ordinary_absence_and_inapplicability_are_completed_missing_values_not_gone()
    {
        using var f = new OwnerFixture();
        await f.Load();
        int reads = 0;
        f.Services.Metadata.Register(new MetadataField("absent-test", "Absent fixture", MetadataCost.Cheap,
            n => Sequence(n) % 2 == 0, (_, _) => { reads++; return null; }, _ => ""));
        await f.Tab.AnalyzeAsync("absent-test");
        Assert.Equal(60, reads);
        Assert.Contains("120 without a value", f.Tab.Banner);
        Assert.DoesNotContain("gone", f.Tab.Banner);
        Assert.Contains("retains all 120", f.Tab.Banner);
    }

    [AvaloniaFact]
    public async Task A_same_key_offline_flag_overrides_a_retained_available_owner_without_starting_work()
    {
        using var f = new OwnerFixture(new OwnerSource { ReadOverride = OrderedOwner }, cacheSize: 8);
        await f.Load();
        await f.Tab.AnalyzeAsync("owner");
        var entry = f.Tab.Listing.Store[0]; entry.Flags |= EntryFlags.Offline;
        f.Tab.Listing.Store.Update(0, entry);
        f.Tab.SortByMetadata("owner");
        await FileListFixture.Wait(() => f.Tab.Listing.AppliedViewSpecVersion == f.Tab.Listing.ViewSpecVersion);
        Assert.Equal(entry.Name, f.Tab.Listing.GetVisible(119).Name);
        Assert.Equal(120, f.Source.ReadPaths.Count);
    }

    [AvaloniaFact]
    public async Task One_name_snapshot_reports_each_mixed_nonresolved_category_per_distinct_retained_principal()
    {
        using var release = new ManualResetEventSlim();
        var source = new OwnerSource { NameOverride = p => p.Id switch
        {
            "1" => new(NameStatus.Resolved, "named"), "2" => new(NameStatus.NotFound), "3" => new(NameStatus.Unavailable),
            _ => HeldName(),
        } };
        PrincipalName HeldName() { release.Wait(TestContext.Current.CancellationToken); return new(NameStatus.Resolved, "late"); }
        using var f = new OwnerFixture(source, cacheSize: 8);
        await f.Load();
        for (int i = 1; i <= 3; i++) f.Names.Request(new Principal(PrincipalKind.UnixUser, i.ToString(CultureInfo.InvariantCulture)));
        Assert.True(await f.Names.WhenIdle(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
        f.Names.Request(new Principal(PrincipalKind.UnixUser, "4"));
        f.Services.Metadata.Register(f.Services.Metadata.Field("owner")! with { Produce = (path, _) =>
            new MetadataValue(MetadataState.Available, new Principal(PrincipalKind.UnixUser, (Sequence(path) % 5 + 1).ToString(CultureInfo.InvariantCulture))) });
        var analysis = f.Tab.AnalyzeAsync("owner");
        try
        {
            await FileListFixture.Wait(() => f.Tab.AnalysisStatus?.Contains("Esc sorts now", StringComparison.Ordinal) == true);
            Assert.True(f.Tab.CancelAnalysis());
            await analysis.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
            foreach (string text in new[] { "1 owners have no name here", "1 name lookups are unavailable", "1 name lookups are pending", "1 owners have no cached name" }) Assert.Contains(text, f.Tab.Banner);
            Assert.Contains("120 current values", f.Tab.Banner);
            Assert.Contains("retains all 120", f.Tab.Banner);
            Assert.Equal(4, source.NameCalls);
            Assert.Empty(source.ReadPaths); // This fake producer deliberately includes a never-requested principal.
        }
        finally { release.Set(); await analysis; }
    }

    [AvaloniaFact]
    public async Task Analysis_counts_current_last_known_gone_denied_placeholder_and_failures_separately_and_includes_folders()
    {
        long now = 0;
        var source = new OwnerSource { ReadOverride = OrderedOwner };
        using var f = new OwnerFixture(source, clock: () => now);
        await f.Load();
        var store = f.Tab.Listing.Store;
        int Index(int seq) => Enumerable.Range(0, store.Count).Single(i => Sequence(store[i].Name) == seq);
        var old = store[Index(5)];
        f.Services.Metadata.Compute("owner", Path.Combine(f.Tab.Location!.Path, old.Name), old, TestContext.Current.CancellationToken);
        now = 6000;
        var placeholder = store[Index(6)]; placeholder.Flags |= EntryFlags.Offline; store.Update(Index(6), placeholder);
        // Fixture identity updates cannot change Kind: append a real directory row instead.
        store.Append(new EntryData("file-0000121-folder", EntryKind.Directory));
        source.ReadOverride = path => Sequence(path) switch
        {
            1 => new(OwnerReadStatus.Denied), 2 => new(OwnerReadStatus.Gone), 3 => new(OwnerReadStatus.Unsupported),
            4 or 5 => new(OwnerReadStatus.Failed, Reason: "busy fixture"),
            _ => new(OwnerReadStatus.Ok, new Principal(PrincipalKind.UnixUser, "42")),
        };
        await f.Tab.AnalyzeAsync("owner");
        string banner = f.Tab.Banner!;
        Assert.Contains("Read Owner for 121 items: 115 current values, 1 last-known values.", banner);
        foreach (string text in new[] { "1 gone", "1 with no access", "1 placeholders not read", "1 unsupported", "1 failed" }) Assert.Contains(text, banner);
        Assert.Contains(source.ReadPaths, path => path.EndsWith("folder", StringComparison.Ordinal));
        Assert.DoesNotContain(source.ReadPaths, path => Sequence(path) == 6);
    }
}
