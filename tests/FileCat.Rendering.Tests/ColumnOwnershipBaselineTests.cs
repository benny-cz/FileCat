using System.Diagnostics;
using System.Runtime.CompilerServices;
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
        bool paintProfile = Environment.GetEnvironmentVariable("FILECAT_COLUMN_BASELINE_MODE") == "paint-profile";
        using var currentProcess = Process.GetCurrentProcess();
        if (OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FILECAT_COLUMN_CPU_MASK") is { Length: > 0 } mask)
            currentProcess.ProcessorAffinity = (nint)Convert.ToInt64(mask, 16);
        double calibrationBefore = Calibrate();
        string root = Path.Combine(Path.GetTempPath(), "filecat-column-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var samples = new List<object>();
        try
        {
            using var services = AppServices.CreateForPaths(AppPaths.Resolve(overrideRoot: root));
            var inner = services.Providers.Get(Schemes.FileSystem);
            foreach (int count in paintProfile ? new[] { 1_000_000 } : new[] { 100_000, 1_000_000 })
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
                        samples.Add(new { check = "scene", fonts = lists.Select(l => new { l.FontSize, family = l.FontFamily.Name,
                            l.VisibleRowCapacity, bounds = l.Bounds.ToString() }).ToArray(),
                            columns = tabs[0].Columns.Select(c => new { field = c.Field.ToString(), c.MetadataId, c.Width, c.Star }).ToArray() });
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
                                    int rendersBefore = times.Count;
                                    foreach (var (tab, list) in tabs.Zip(lists))
                                    {
                                        tab.Listing.SetFocus((frame % 60) * 30);
                                        list.EnsureFocusVisible();
                                        list.InvalidateVisual();
                                    }
                                    if (target is not null) target.Render(grid);
                                    else window.CaptureRenderedFrame()?.Dispose();
                                    // Headless capture also drains queued UI paints. Count
                                    // actual work, while requiring every requested frame
                                    // to paint all four invalidated panels.
                                    Assert.True(times.Count - rendersBefore >= lists.Count,
                                        $"Requested frame {frame} did not paint all {lists.Count} panels.");
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
                        }
                        if (target is not null && Environment.GetEnvironmentVariable("FILECAT_COLUMN_PROFILE_IMAGE") is { Length: > 0 } shot)
                            target.Save(shot, PngBitmapEncoderOptions.Default);
                    }
                    if (paintProfile) continue;
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
                cpuMask = Environment.GetEnvironmentVariable("FILECAT_COLUMN_CPU_MASK"),
                tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                calibrationBeforeMs = calibrationBefore, calibrationAfterMs = Calibrate(),
                label = Environment.GetEnvironmentVariable("FILECAT_COLUMN_BASELINE_LABEL"), samples }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Independent, allocation-free work records processor throughput on either side of the workload.
    // Its result is checked so the JIT cannot discard the loop; it never normalizes acceptance metrics.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static double Calibrate()
    {
        var time = Stopwatch.StartNew();
        ulong value = 1;
        for (int i = 0; i < 20_000_000; i++) value = unchecked(value * 6364136223846793005UL + 1442695040888963407UL);
        Assert.NotEqual(0UL, value);
        return time.Elapsed.TotalMilliseconds;
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
