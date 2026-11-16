using System.Diagnostics;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using FileCat.App.Controls;
using FileCat.App.Services;
using FileCat.App.ViewModels;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.State;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.Rendering.Tests;

/// <summary>Opt-in, fixed before/after workload. No native ownership or real million-file folder.</summary>
public sealed class ColumnOwnershipBaselineTests
{
    [AvaloniaFact]
    public async Task Capture_feature_off_render_and_metadata_sort_baselines()
    {
        string? output = Environment.GetEnvironmentVariable("FILECAT_COLUMN_BASELINE_OUT");
        if (string.IsNullOrEmpty(output)) Assert.Skip("Set FILECAT_COLUMN_BASELINE_OUT to capture the bounded performance run.");
        bool diagnostic = Environment.GetEnvironmentVariable("FILECAT_COLUMN_BASELINE_MODE") == "diagnostic";
        bool reuseTarget = Environment.GetEnvironmentVariable("FILECAT_COLUMN_REUSE_TARGET") == "1";
        string root = Path.Combine(Path.GetTempPath(), "filecat-column-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var samples = new List<object>();
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var inner = services.Providers.Get(Schemes.FileSystem);
            foreach (int count in new[] { 100_000, 1_000_000 })
            {
                services.Providers.Register(new SyntheticListingProvider(inner, root, count));
                var workspace = new WorkspaceViewModel(services);
                var tabs = new List<TabViewModel>();
                var lists = new List<FileListControl>();
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
                for (int i = 0; i < 4; i++)
                {
                    var panel = new PanelViewModel(workspace, services);
                    workspace.Panels.Add(panel);
                    var tab = panel.OpenTab(Location.FileSystem(Path.Combine(root, $"synthetic-{count}-{i}")));
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
                    await Until(() => tabs.All(t => t.Listing.State != ListingState.Loading));
                    Assert.All(tabs, t => { Assert.Null(t.Listing.Error); Assert.Equal(count, t.Listing.VisibleCount); });
                    window.CaptureRenderedFrame()?.Dispose();
                    if (count == 1_000_000)
                    {
                        using var target = reuseTarget ? new RenderTargetBitmap(new PixelSize(1920, 900)) : null;
                        for (int run = -1; run < 5; run++)
                        {
                            var times = new List<double>();
                            long allocated = 0;
                            FileListControl.RenderTimed = (ms, bytes) => { times.Add(ms); allocated += bytes; };
                            var clock = Stopwatch.StartNew();
                            using var process = Process.GetCurrentProcess();
                            var cpu = process.TotalProcessorTime;
                            var pause = GC.GetTotalPauseDuration();
                            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
                            long before = GC.GetTotalAllocatedBytes();
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
                                    if (target is not null) target.Render(grid);
                                    else window.CaptureRenderedFrame()?.Dispose();
                                }
                            }
                            finally { FileListControl.RenderTimed = null; }
                            clock.Stop();
                            long allocatedBytes = GC.GetTotalAllocatedBytes() - before;
                            if (run >= 0) samples.Add(new { check = "PF1-diagnostics", count, panels = 4, run,
                                cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                                gcPauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds,
                                gcGen0 = GC.CollectionCount(0) - gen0, gcGen1 = GC.CollectionCount(1) - gen1, gcGen2 = GC.CollectionCount(2) - gen2 });
                            if (run >= 0) samples.Add(new { check = "PF1", count, panels = 4, run, elapsedMs = clock.Elapsed.TotalMilliseconds,
                                renderCount = times.Count, renderMedianMs = Percentile(times, .5), renderP95Ms = Percentile(times, .95),
                                renderAllocatedBytes = allocated, allocatedBytes, memory = Memory() });
                            Assert.Equal(960, times.Count);
                        }
                    }
                    // Immutable fake cached keys isolate the actual listing pipeline/comparer cost. O5 adds
                    // metadata-service snapshot/retention measurements; this is not a live-cache benchmark.
                    IComparable[] keys = Enumerable.Range(0, count).Select(i => (IComparable)((i * 17L) % 64)).ToArray();
                    foreach (var tab in tabs) tab.Listing.MetadataKeys = (_, index, _) => keys[index];
                    foreach (int panels in new[] { 1, 2 })
                    foreach (bool supersede in new[] { false, true })
                    {
                        if (diagnostic && (count != 100_000 || panels == 1 && supersede || panels == 2 && !supersede)) continue;
                        for (int run = -1; run < 5; run++)
                        {
                            long capturedBefore = tabs.Take(panels).Sum(t => Counter(t.Listing, "MetadataCapturedRows"));
                            long capturesBefore = tabs.Take(panels).Sum(t => Counter(t.Listing, "MetadataCaptures"));
                            long before = GC.GetTotalAllocatedBytes();
                            var clock = Stopwatch.StartNew();
                            using var process = Process.GetCurrentProcess();
                            var cpu = process.TotalProcessorTime;
                            var pause = GC.GetTotalPauseDuration();
                            foreach (var tab in tabs.Take(panels))
                            {
                                var sort = new SortSpec(SortField.Metadata, Descending: run % 2 == 0) { MetadataId = "baseline" };
                                tab.Listing.Sort = sort;
                                if (supersede)
                                {
                                    await Task.Delay(2, TestContext.Current.CancellationToken);
                                    tab.Listing.Sort = sort with { Descending = !sort.Descending };
                                }
                            }
                            await Until(() => tabs.Take(panels).All(t => t.Listing.AppliedViewSpecVersion == t.Listing.ViewSpecVersion));
                            clock.Stop();
                            long allocatedBytes = GC.GetTotalAllocatedBytes() - before;
                            if (run >= 0) samples.Add(new { check = "PF3-diagnostics", count, panels, supersede, run,
                                cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds,
                                gcPauseMs = (GC.GetTotalPauseDuration() - pause).TotalMilliseconds });
                            if (run >= 0) samples.Add(new { check = "PF3", count, panels, supersede, run,
                                elapsedMs = clock.Elapsed.TotalMilliseconds, allocatedBytes,
                                capturedRows = tabs.Take(panels).Sum(t => Counter(t.Listing, "MetadataCapturedRows")) - capturedBefore,
                                captures = tabs.Take(panels).Sum(t => Counter(t.Listing, "MetadataCaptures")) - capturesBefore,
                                reservedBytes = services.ListingIndexes.ReservedBytes, reservationLimitBytes = services.ListingIndexes.LimitBytes,
                                memory = Memory() });
                        }
                    }
                }
                finally
                {
                    window.Close();
                    foreach (var tab in tabs) tab.Dispose();
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output!))!);
            File.WriteAllText(output!, JsonSerializer.Serialize(new { utc = DateTime.UtcNow, runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString(), processors = Environment.ProcessorCount, diagnostic, reuseTarget,
                label = Environment.GetEnvironmentVariable("FILECAT_COLUMN_BASELINE_LABEL"), samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static double Percentile(List<double> values, double p)
    {
        values.Sort();
        return values.Count == 0 ? 0 : values[(int)Math.Min(values.Count - 1, Math.Floor(values.Count * p))];
    }

    // This identical source also builds on the O4 control, before snapshot diagnostics existed.
    private static long Counter(ListingModel listing, string property) =>
        typeof(ListingModel).GetProperty(property)?.GetValue(listing) is long value ? value : 0;

    private static object Memory()
    {
        using var process = Process.GetCurrentProcess();
        return new { managedBytes = GC.GetTotalMemory(false), retainedManagedBytes = GC.GetTotalMemory(true),
            privateBytes = process.PrivateMemorySize64, peakWorkingSetBytes = process.PeakWorkingSet64 };
    }

    private static async Task Until(Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done())
        {
            Assert.True(clock.Elapsed < TimeSpan.FromMinutes(3), "Baseline listing/sort exceeded three minutes.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
