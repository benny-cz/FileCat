using Avalonia.Headless.XUnit;
using FileCat.Core.Commands;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;

namespace FileCat.App.Tests;

public sealed partial class OwnerColumnTests
{
    [AvaloniaFact]
    public async Task Same_location_replacement_retires_owner_and_group_before_enumeration_and_suppresses_old_view_demand()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var f = new OwnerFixture();
        await f.Load(); await f.ShowOwner();
        var entry = f.FocusedEntry;
        string path = Path.Combine(f.Tab.Location!.Path, entry.Name);
        var store = f.Tab.Listing.Store;
        f.Services.Metadata.Register(new("group", "Group fixture", MetadataCost.Cheap, _ => true, (_, _) => "old group", _ => ""));
        f.Services.Metadata.Register(new("unrelated", "Unrelated", MetadataCost.Cheap, _ => true, (_, _) => "keep", _ => ""));
        f.Services.Metadata.Compute("group", path, entry, TestContext.Current.CancellationToken);
        f.Services.Metadata.Compute("unrelated", path, entry, TestContext.Current.CancellationToken);
        f.Source.ReadOverride = _ => new(OwnerReadStatus.Ok, new Principal(PrincipalKind.UnixUser, "84"));
        f.Provider.EnumerateOverride = (location, sink, ct) =>
        {
            Assert.Equal(MetadataState.NotRequested, f.Services.Metadata.Peek("owner", path, entry, false).State);
            Assert.Equal(MetadataState.NotRequested, f.Services.Metadata.Peek("group", path, entry, false).State);
            entered.Set(); release.Wait(ct);
            return f.Provider.EnumerateInner(location, sink, ct);
        };
        try
        {
            f.Tab.Refresh();
            await FileListFixture.Wait(() => entered.IsSet);
            int reads = f.Source.ReadPaths.Count;
            f.Frame();
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.Equal(reads, f.Source.ReadPaths.Count);
            Assert.Equal("keep", f.Services.Metadata.Peek("unrelated", path, entry).Value);
            release.Set();
            await FileListFixture.Wait(() => !ReferenceEquals(store, f.Tab.Listing.Store) && !f.Tab.Listing.IsRefreshing);
            Assert.Equal(entry.Name, f.FocusedEntry.Name);
            Assert.Equal(entry.Size, f.FocusedEntry.Size);
            Assert.Equal(entry.Modified, f.FocusedEntry.Modified);
            f.Frame();
            await FileListFixture.Wait(() => f.FocusedValue.Value is Principal p && p.Id == "84");
        }
        finally { release.Set(); }
    }

    [AvaloniaFact]
    public async Task A_native_owner_read_started_for_the_previous_store_cannot_publish_into_its_identical_replacement()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        var source = new OwnerSource { ReadOverride = _ =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            { entered.Set(); release.Wait(TestContext.Current.CancellationToken); return new(OwnerReadStatus.Ok, new Principal(PrincipalKind.UnixUser, "42")); }
            return new(OwnerReadStatus.Ok, new Principal(PrincipalKind.UnixUser, "84"));
        } };
        using var f = new OwnerFixture(source);
        await f.Load();
        f.Tab.ColumnProfile = 1; f.Frame();
        try
        {
            await FileListFixture.Wait(() => entered.IsSet);
            string held = source.ReadPaths.First();
            var oldStore = f.Tab.Listing.Store;
            var entry = Enumerable.Range(0, oldStore.Count).Select(i => oldStore[i]).Single(e => Path.GetFileName(held) == e.Name);
            f.Tab.Refresh();
            await FileListFixture.Wait(() => !ReferenceEquals(oldStore, f.Tab.Listing.Store) && !f.Tab.Listing.IsRefreshing);
            release.Set();
            await FileListFixture.Wait(() => f.Services.Metadata.Peek("owner", held, entry, false).State != MetadataState.Pending);
            Assert.False(f.Services.Metadata.Peek("owner", held, entry, false).Value is Principal { Id: "42" });
            f.Frame();
            await FileListFixture.Wait(() => f.FocusedValue.Value is Principal p && p.Id == "84");
        }
        finally { release.Set(); }
    }

    [AvaloniaFact]
    public async Task Replacing_the_listing_during_the_final_analysis_read_cancels_promptly_without_old_status_or_success()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        var source = new OwnerSource { ReadOverride = path =>
        {
            if (Interlocked.Increment(ref calls) == 120) { entered.Set(); release.Wait(TestContext.Current.CancellationToken); }
            return OrderedOwner(path);
        } };
        using var f = new OwnerFixture(source, cacheSize: 8);
        await f.Load();
        var analysis = f.Tab.AnalyzeAsync("owner");
        try
        {
            await FileListFixture.Wait(() => entered.IsSet);
            var store = f.Tab.Listing.Store;
            f.Tab.Refresh();
            await analysis.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            Assert.Null(f.Tab.AnalysisStatus);
            Assert.DoesNotContain("retains all", f.Tab.Banner ?? "");
            await FileListFixture.Wait(() => !ReferenceEquals(store, f.Tab.Listing.Store));
            release.Set();
            await Task.Delay(150, TestContext.Current.CancellationToken);
            Assert.Null(f.Tab.AnalysisStatus);
            Assert.DoesNotContain("Read Owner for", f.Tab.Banner ?? "");
            Assert.Equal(120, calls);
        }
        finally { release.Set(); await analysis; }
    }

    [AvaloniaFact]
    public async Task Activation_explicitly_requests_repaint_but_only_visible_demand_re_reads_the_owner()
    {
        long now = 0;
        using var f = new OwnerFixture(clock: () => now);
        await f.Load(); await f.ShowOwner();
        Assert.Equal("42", ((Principal)f.FocusedValue.Value!).Id);
        f.Tab.ColumnProfile = 0;
        await Task.Delay(150, TestContext.Current.CancellationToken);
        int reads = f.Source.ReadPaths.Count, signals = 0;
        f.Services.Metadata.ValuesChanged += () => Interlocked.Increment(ref signals);
        f.Source.ReadOverride = _ => new(OwnerReadStatus.Ok, new Principal(PrincipalKind.UnixUser, "84"));
        f.Tab.IsActiveTab = false; f.Tab.IsActiveTab = true;
        await FileListFixture.Wait(() => signals > 0);
        f.Frame();
        Assert.Equal(reads, f.Source.ReadPaths.Count);
        f.Tab.ColumnProfile = 1; f.Frame();
        await FileListFixture.Wait(() => f.FocusedValue.Value is Principal p && p.Id == "84");
        Assert.InRange(f.Source.ReadPaths.Count - reads, 1, f.List.VisibleRowCapacity + 1);
    }

    [AvaloniaFact]
    public async Task Reread_command_invalidates_the_name_generation_even_when_owner_is_off_profile()
    {
        var (services, vm, window, root) = AccessibilityTests.OpenMainWindow();
        try
        {
            var principal = new Principal(PrincipalKind.WindowsSid, "S-1-5-18");
            services.PrincipalNames.Request(principal);
            Assert.True(await services.PrincipalNames.WhenIdle(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken));
            Assert.NotEqual(NameStatus.NotRequested, services.PrincipalNames.Peek(principal).Status);
            await vm.ExecuteAsync(CommandIds.Refresh);
            Assert.Equal(NameStatus.NotRequested, services.PrincipalNames.Peek(principal).Status);
        }
        finally { AccessibilityTests.Close(services, window, root); }
    }
}
