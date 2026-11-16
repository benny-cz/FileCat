using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.FileSystem;
using FileCat.Core.Listing;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.Rendering.Tests;

/// <summary>Opt-in PF2: fixed source, real demand/cache/name presentation, matched visible column geometry.</summary>
public sealed class OwnerColumnPerformanceTests
{
    [AvaloniaFact]
    public async Task Measure_owner_first_demand_and_steady_state_against_plain_metadata()
    {
        string? output = Environment.GetEnvironmentVariable("FILECAT_OWNER_PERFORMANCE_OUT");
        if (string.IsNullOrEmpty(output)) Assert.Skip("Set FILECAT_OWNER_PERFORMANCE_OUT for the bounded PF2 run.");
        string root = Path.Combine(Path.GetTempPath(), "filecat-owner-performance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var source = new FixedOwnership();
        var samples = new List<object>();
        try
        {
            using var services = AppServices.CreateForPathsWithOwnership(AppPaths.Resolve(overrideRoot: root), source);
            services.Metadata.Register(new MetadataField("owner-plain", "Owner", MetadataCost.Cheap, _ => true,
                (path, _) => source.Plain(path), value => (string)value!, Folders: true, RefreshAfter: TimeSpan.FromSeconds(5)));
            services.Providers.Register(new SyntheticListingProvider(services.Providers.Get(Schemes.FileSystem), root, 1_000_000));
            var workspace = new WorkspaceViewModel(services);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
            var tabs = new List<TabViewModel>();
            var lists = new List<FileListControl>();
            for (int i = 0; i < 4; i++)
            {
                var panel = new PanelViewModel(workspace, services);
                workspace.Panels.Add(panel);
                var tab = panel.OpenTab(Location.FileSystem(Path.Combine(root, "synthetic-" + i)));
                var list = new FileListControl { Tab = tab };
                Grid.SetColumn(list, i);
                grid.Children.Add(list);
                tabs.Add(tab);
                lists.Add(list);
            }
            var window = new Window { Width = 1920, Height = 900, Content = grid };
            window.Show();
            try
            {
                await Until(() => tabs.All(t => t.Listing.State == ListingState.Complete));
                foreach (string id in new[] { "owner-plain", "owner" })
                {
                    ColumnSpec[] columns = [.. ColumnProfiles.Defaults[0].Columns,
                        new(ColumnField.Metadata, "Owner", 160) { MetadataId = id }];
                    services.Columns.Replace(new[] { ("Measured", columns) });
                    foreach (var tab in tabs) tab.ColumnProfile = 0;
                    window.UpdateLayout();
                    foreach (var list in lists) list.ScrollToHorizontalOffset(double.MaxValue);
                    long firstAllocated = GC.GetTotalAllocatedBytes();
                    var firstClock = Stopwatch.StartNew();
                    var firstRenders = new List<double>();
                    FileListControl.RenderTimed = (ms, _) => firstRenders.Add(ms);
                    try { foreach (var list in lists) list.InvalidateVisual(); window.CaptureRenderedFrame()?.Dispose(); }
                    finally { FileListControl.RenderTimed = null; }
                    firstClock.Stop();
                    long firstBytes = GC.GetTotalAllocatedBytes() - firstAllocated;
                    await Until(() => tabs.All(t => t.VisibleStoreIndices.All(index =>
                    {
                        var entry = t.Listing.Store[index];
                        return services.Metadata.Peek(id, Path.Combine(t.Location!.Path, entry.Name), entry, false).State == MetadataState.Available;
                    })));
                    Assert.True(await services.PrincipalNames.WhenIdle(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                    samples.Add(new { phase = "first-demand", field = id, elapsedMs = firstClock.Elapsed.TotalMilliseconds,
                        allocatedBytes = firstBytes, renderCount = firstRenders.Count, renderP95Ms = Percentile(firstRenders, .95),
                        ownerReads = source.OwnerReads, plainReads = source.PlainReads, nameLookups = source.Names,
                        visibleRows = tabs.Sum(t => t.VisibleStoreIndices.Count),
                        horizontalOffsets = lists.Select(l => l.HorizontalOffset).ToArray() });
                    for (int run = -1; run < 5; run++)
                    {
                        var renders = new List<double>();
                        long renderBytes = 0;
                        long allocated = GC.GetTotalAllocatedBytes();
                        var pause = GC.GetTotalPauseDuration();
                        using var process = Process.GetCurrentProcess();
                        var cpu = process.TotalProcessorTime;
                        var clock = Stopwatch.StartNew();
                        FileListControl.RenderTimed = (ms, bytes) => { renders.Add(ms); renderBytes += bytes; };
                        try
                        {
                            for (int frame = 0; frame < 240; frame++)
                            {
                                foreach (var (tab, list) in tabs.Zip(lists))
                                {
                                    tab.Listing.SetFocus((frame % 60) * 30);
                                    list.EnsureFocusVisible();
                                    list.InvalidateVisual();
                                }
                                window.CaptureRenderedFrame()?.Dispose();
                            }
                        }
                        finally { FileListControl.RenderTimed = null; }
                        clock.Stop();
                        long totalBytes = GC.GetTotalAllocatedBytes() - allocated;
                        double cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                        double gcPauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds;
                        if (run >= 0) samples.Add(new { phase = "steady", field = id, run, elapsedMs = clock.Elapsed.TotalMilliseconds,
                            renderCount = renders.Count, renderMedianMs = Percentile(renders, .5), renderP95Ms = Percentile(renders, .95),
                            allocatedBytes = totalBytes, renderAllocatedBytes = renderBytes, cpuMs, gcPauseMs,
                            managedBytes = GC.GetTotalMemory(false), retainedManagedBytes = GC.GetTotalMemory(true),
                            privateBytes = process.PrivateMemorySize64, peakWorkingSetBytes = process.PeakWorkingSet64,
                            ownerReads = source.OwnerReads, plainReads = source.PlainReads, nameLookups = source.Names });
                    }
                }
                Assert.InRange(source.Names, 1, 4);
            }
            finally { window.Close(); foreach (var tab in tabs) tab.Dispose(); }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output!))!);
            File.WriteAllText(output!, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString(), count = 1_000_000, panels = 4, frames = 240, samples },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static double Percentile(List<double> values, double percentile)
    { values.Sort(); return values.Count == 0 ? 0 : values[Math.Min(values.Count - 1, (int)(values.Count * percentile))]; }
    private static async Task Until(Func<bool> ready)
    {
        var clock = Stopwatch.StartNew();
        while (!ready())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromMinutes(3), "PF2 demand/listing timed out.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private sealed class FixedOwnership : IOwnership
    {
        private readonly Principal[] _principals = Enumerable.Range(0, 4)
            .Select(i => new Principal(PrincipalKind.WindowsSid, $"S-1-5-21-10-20-30-{1000 + i}")).ToArray();
        private readonly string[] _names = Enumerable.Range(0, 4).Select(i => @"FIXTURE\owner-" + i).ToArray();
        public int OwnerReads, PlainReads, Names;
        public bool IsSupported => true;
        private static int Index(string path) => int.Parse(Path.GetFileName(path).AsSpan(5, 7), CultureInfo.InvariantCulture) % 4;
        public string Plain(string path) { Interlocked.Increment(ref PlainReads); return _names[Index(path)]; }
        public OwnerRead ReadOwner(string path) { Interlocked.Increment(ref OwnerReads); return new(OwnerReadStatus.Ok, _principals[Index(path)]); }
        public OwnerRead ReadGroup(string path) => new(OwnerReadStatus.Unsupported);
        public PrincipalName LookUp(Principal principal) { Interlocked.Increment(ref Names); return new(NameStatus.Resolved, _names[principal.Id[^1] - '0'], "User account"); }
    }
}
