using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;

namespace FileCat.Rendering.Tests;

public sealed class OwnershipAnalysisPerformanceTests
{
    [AvaloniaFact]
    public async Task Capture_bounded_real_metadata_analysis_and_accepted_order_cost()
    {
        string? output = Environment.GetEnvironmentVariable("FILECAT_OWNER_ANALYSIS_OUT");
        if (string.IsNullOrEmpty(output)) Assert.Skip("Set FILECAT_OWNER_ANALYSIS_OUT for the bounded O5 analysis measurement.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-owner-analysis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var samples = new List<object>();
        long clock = 0;
        var source = new FixedOwners();
        try
        {
            using var services = AppServices.CreateForPathsWithOwnership(AppPaths.Resolve(overrideRoot: root), source,
                clock: () => Interlocked.Read(ref clock));
            var inner = services.Providers.Get(Schemes.FileSystem);
            foreach (int count in new[] { 100_000, 1_000_000 })
            {
                services.Providers.Register(new SyntheticListingProvider(inner, root, count));
                var workspace = new WorkspaceViewModel(services);
                var panel = new PanelViewModel(workspace, services); workspace.Panels.Add(panel);
                var tabs = Enumerable.Range(0, 2).Select(i => panel.OpenTab(Location.FileSystem(Path.Combine(root, $"synthetic-{count}-{i}")))).ToArray();
                try
                {
                    await Until(() => tabs.All(t => t.Listing.State == ListingState.Complete));
                    Assert.All(tabs, t => Assert.Null(t.Listing.Error));
                    long baseline = services.ListingIndexes.ReservedBytes;
                    foreach (int panels in new[] { 1, 2 })
                    for (int run = -1; run < 5; run++)
                    {
                        foreach (var tab in tabs) if (tab.Listing.Sort.Field != SortField.Name) tab.SortBy(SortField.Name);
                        await Until(() => tabs.All(t => t.Listing.AppliedViewSpecVersion == t.Listing.ViewSpecVersion));
                        await Until(() => services.ListingIndexes.ReservedBytes == baseline);
                        Interlocked.Add(ref clock, 6000);
                        int reads = source.Reads;
                        long allocated = GC.GetTotalAllocatedBytes();
                        var pause = GC.GetTotalPauseDuration();
                        using var process = Process.GetCurrentProcess();
                        var cpu = process.TotalProcessorTime;
                        var time = Stopwatch.StartNew();
                        await Task.WhenAll(tabs.Take(panels).Select(t => t.AnalyzeAsync("owner")));
                        time.Stop();
                        long bytes = GC.GetTotalAllocatedBytes() - allocated;
                        double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                        double gcPauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds;
                        Assert.All(tabs.Take(panels), t =>
                        {
                            Assert.Contains($"Read Owner for {count:N0} items: {count:N0} current values.", t.Banner);
                            Assert.Equal(t.Listing.ViewSpecVersion, t.Listing.AppliedViewSpecVersion);
                            Assert.NotNull(t.Listing.AppliedMetadataCoverage);
                        });
                        Assert.InRange(services.ListingIndexes.ReservedBytes, 1, services.ListingIndexes.LimitBytes);
                        Assert.InRange(services.ListingIndexes.PeakReservedBytes, 1, services.ListingIndexes.LimitBytes);
                        if (run >= 0) samples.Add(new { count, panels, run, elapsedMs = time.Elapsed.TotalMilliseconds,
                            allocatedBytes = bytes, cpuMs, gcPauseMs, ownerReads = source.Reads - reads, nameLookups = source.Names,
                            coverage = tabs.Take(panels).Select(t => t.Listing.AppliedMetadataCoverage).ToArray(),
                            reservedBytes = services.ListingIndexes.ReservedBytes, peakReservedBytes = services.ListingIndexes.PeakReservedBytes,
                            reservationLimitBytes = services.ListingIndexes.LimitBytes, baselineReservedBytes = baseline,
                            managedBytes = GC.GetTotalMemory(false), retainedManagedBytes = GC.GetTotalMemory(true),
                            privateBytes = process.PrivateMemorySize64, peakWorkingSetBytes = process.PeakWorkingSet64 });
                    }
                    foreach (var tab in tabs) tab.SortBy(SortField.Name);
                    await Until(() => tabs.All(t => t.Listing.AppliedViewSpecVersion == t.Listing.ViewSpecVersion));
                    await Until(() => services.ListingIndexes.ReservedBytes == baseline);
                }
                finally { foreach (var tab in tabs) tab.Dispose(); }
                await Until(() => services.ListingIndexes.ReservedBytes == 0);
            }
            File.WriteAllText(output!, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString(), samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task Until(Func<bool> predicate)
    {
        var time = Stopwatch.StartNew();
        while (!predicate())
        {
            Assert.True(time.Elapsed < TimeSpan.FromSeconds(30), "Analysis fixture failed to reach its bounded checkpoint.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
    private sealed class FixedOwners : IOwnership
    {
        private readonly Principal[] _ids = Enumerable.Range(1000, 4).Select(i => new Principal(PrincipalKind.UnixUser, i.ToString(CultureInfo.InvariantCulture))).ToArray();
        public int Reads, Names;
        public bool IsSupported => true;
        public OwnerRead ReadOwner(string path)
        {
            Interlocked.Increment(ref Reads);
            int index = int.Parse(Path.GetFileName(path).AsSpan(5, 7), CultureInfo.InvariantCulture) % 4;
            return new(OwnerReadStatus.Ok, _ids[index]);
        }
        public OwnerRead ReadGroup(string path) => new(OwnerReadStatus.Unsupported);
        public PrincipalName LookUp(Principal principal) { Interlocked.Increment(ref Names); return new(NameStatus.Resolved, "fixture-owner-" + principal.Id); }
    }
}
