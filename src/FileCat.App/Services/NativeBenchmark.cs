using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Threading;
using FileCat.App.ViewModels;
using FileCat.App.Views;
using FileCat.Core.Commands;
using FileCat.Core.Diagnostics;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Selection;
using Location = FileCat.Core.Resources.Location;

namespace FileCat.App.Services;

/// <summary>
/// TV-01 in the native window (<c>--benchmark N</c>): N synthetic entries in each of up to four panels, then scripted
/// keys through the real key routing. Records startup, first rows, completion, input-to-frame latency for cursor
/// movement, marking and panel switches, frame intervals under continuous paging, a sort change, and memory;
/// writes JSON and exits. The numbers describe the machine they ran on (plan §21 targets, not guarantees).
/// </summary>
internal static class NativeBenchmark
{
    public static async Task RunAsync(MainWindow window, MainViewModel vm, AppServices services, StartupOptions options, string syntheticRoot)
    {
        var results = new JsonObject();
        // The shared temp folder (Linux, macOS): a name nobody can guess, made new, so no link planted there is followed.
        bool chosen = options.BenchmarkOut is not null;
        var output = options.BenchmarkOut ?? Path.Combine(Path.GetTempPath(), $"filecat-benchmark-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.json");
        try
        {
            await RunCoreAsync(window, vm, services, options, syntheticRoot, results);
        }
        catch (Exception ex)
        {
            results["error"] = ex.ToString();
            AppLog.Error("Benchmark failed", ex);
        }
        try
        {
            string json = results.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            if (chosen) File.WriteAllText(output, json);
            else
            {
                using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.Write(System.Text.Encoding.UTF8.GetBytes(json));
            }
            AppLog.Info("Benchmark results written to " + output);
            Console.WriteLine(output);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("Benchmark results could not be written", ex);
        }
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(results.ContainsKey("error") ? 1 : 0);
    }

    private static async Task RunCoreAsync(MainWindow window, MainViewModel vm, AppServices services, StartupOptions options, string root, JsonObject results)
    {
        await NextFrameRenderedAsync(window);
        results["startup_to_first_frame_ms"] = Math.Round((DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds);
        results["environment"] = Environment(window);
        int count = options.BenchmarkCount;
        int panels = Math.Clamp(options.BenchmarkPanels, 1, 4);
        results["count"] = count;
        results["panels"] = panels;

        var cpu = new CpuLoad();
        var memory = new MemorySampler();
        var sampler = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => memory.Sample());
        sampler.Start();

        // Load: every panel shows its own synthetic listing (separate stores, shared index budget).
        var workspace = vm.Workspace;
        while (workspace.Panels.Count < panels) workspace.AddPanel(null);
        var tabs = new List<TabViewModel>();
        var clock = Stopwatch.StartNew();
        for (int i = 0; i < panels; i++)
        {
            var dir = Path.Combine(root, "synthetic-" + i);
            var tab = workspace.Panels[i].ActiveTab!;
            tab.Navigate(Location.FileSystem(dir));
            tabs.Add(tab);
        }
        double firstRows = -1;
        while (tabs.Any(t => t.Listing.State == ListingState.Loading))
        {
            if (firstRows < 0 && tabs.All(t => t.Listing.VisibleCount > 0)) firstRows = clock.Elapsed.TotalMilliseconds;
            if (clock.Elapsed > TimeSpan.FromMinutes(15)) throw new TimeoutException("Listing did not complete in 15 minutes.");
            await Task.Delay(10);
        }
        results["first_rows_ms"] = Math.Round(firstRows < 0 ? clock.Elapsed.TotalMilliseconds : firstRows);
        results["complete_ms"] = Math.Round(clock.Elapsed.TotalMilliseconds);
        foreach (var t in tabs)
        {
            if (t.Listing.Error is { } error) throw new InvalidOperationException(error);
            if (t.Listing.TotalCount != count) throw new InvalidOperationException($"Expected {count} entries; got {t.Listing.TotalCount}.");
        }
        results["machine_cpu_busy_pct_load"] = cpu.BusyPercentSinceMark();
        results["spill_mib"] = Mib(tabs.Sum(t => t.Listing.Store.SpillBytes));
        results["external_index_mib"] = Mib(tabs.Sum(t => t.Listing.ExternalIndexBytes));
        await SettleAsync(window);

        workspace.Activate(workspace.Panels[0]);
        window.Activate();
        window.ActiveList?.Focus();
        await SettleAsync(window);

        results["cursor_down"] = await KeyLatencyAsync(window, Key.Down, KeyModifiers.None, 300);
        results["focused_after_cursor"] = tabs[0].Listing.FocusedIndex; // sanity: the keys reached the list
        results["mark_insert"] = await KeyLatencyAsync(window, Key.Insert, KeyModifiers.None, 100);
        results["cursor_end_home"] = await KeyLatencyAsync(window, Key.End, KeyModifiers.None, 10, alternate: Key.Home);
        results["panel_switch_tab"] = await KeyLatencyAsync(window, Key.Tab, KeyModifiers.None, 20);
        results["idle_frames"] = await IdleFramesAsync(window, 120);
        results["paging_rendered"] = await SequentialPagingAsync(window, 120);
        results["paging_frames"] = await ContinuousPagingAsync(window, 240);
        results["machine_cpu_busy_pct_interaction"] = cpu.BusyPercentSinceMark();

        // Whole-listing commands on the (spilled) panel-1 listing, timed on the UI thread. Each includes the status
        // line's mark statistics, which update inside the command.
        var panelOne = tabs[0].Listing;
        static double Time(Action action)
        {
            var sw = Stopwatch.StartNew();
            action();
            return Math.Round(sw.Elapsed.TotalMilliseconds, 1);
        }
        results["whole_listing_ms"] = new JsonObject
        {
            ["mark_all"] = Time(() => panelOne.MarkAll(true)),
            ["invert"] = Time(() => panelOne.InvertMarks(includeDirectories: true)),
            ["mark_by_mask"] = Time(() => panelOne.MarkByMask(Mask.Parse("*7-long*"), true, includeDirectories: false)),
            // Quick search compares typed ASCII ordinally.
            ["quick_search_miss"] = Time(() => panelOne.FindVisible(0, true, n => n.StartsWith("zz-none", StringComparison.OrdinalIgnoreCase))),
            ["find_name_miss"] = Time(() => panelOne.FindStoreIndex("zz-none")),
            ["unmark_all"] = Time(panelOne.UnmarkEverything),
        };
        await SettleAsync(window);

        // Sort change on panel 1: by size puts the largest synthetic file first.
        workspace.Activate(workspace.Panels[0]);
        var listing = tabs[0].Listing;
        string expectedFirst = $"file-{count:0000000}-long-αβγ.txt";
        clock.Restart();
        vm.Execute(CommandIds.SortSize);
        while (listing.VisibleCount == 0 || listing.GetVisible(0).Name != expectedFirst)
        {
            if (clock.Elapsed > TimeSpan.FromMinutes(15)) throw new TimeoutException("Sort did not finish in 15 minutes.");
            await Task.Delay(10);
        }
        results["sort_by_size_ms"] = Math.Round(clock.Elapsed.TotalMilliseconds);
        results["cursor_down_after_sort"] = await KeyLatencyAsync(window, Key.Down, KeyModifiers.None, 100);

        if (options.BenchmarkDirectory is { } real && Directory.Exists(real))
        {
            // A real directory, loaded three times in panel 1: the first is closest to cold, the rest are warm.
            var runs = new JsonArray();
            for (int run = 0; run < 3; run++)
            {
                clock.Restart();
                tabs[0].Navigate(Location.FileSystem(real));
                double first = -1;
                while (tabs[0].Listing.State == ListingState.Loading || tabs[0].Listing.Location?.Path != real)
                {
                    if (first < 0 && tabs[0].Listing.VisibleCount > 1 && tabs[0].Listing.Location?.Path == real) first = clock.Elapsed.TotalMilliseconds;
                    await Task.Delay(5);
                }
                runs.Add(new JsonObject
                {
                    ["entries"] = tabs[0].Listing.TotalCount,
                    ["first_rows_ms"] = Math.Round(first < 0 ? clock.Elapsed.TotalMilliseconds : first),
                    ["complete_ms"] = Math.Round(clock.Elapsed.TotalMilliseconds),
                });
                tabs[0].Navigate(Location.FileSystem(Path.Combine(root, "synthetic-0")));
                while (tabs[0].Listing.State == ListingState.Loading) await Task.Delay(10);
            }
            results["real_directory"] = runs;
        }

        memory.Sample();
        results["peak_private_mib"] = Mib(memory.PeakPrivate);
        results["peak_working_set_mib"] = Mib(memory.PeakWorkingSet);
        results["peak_managed_mib"] = Mib(memory.PeakManaged);
        results["final_private_mib"] = Mib(memory.LastPrivate);
        sampler.Stop();
    }

    /// <summary>Presses a key <paramref name="presses"/> times at key auto-repeat pace (~30/s) and reports latencies.</summary>
    private static async Task<JsonObject> KeyLatencyAsync(MainWindow window, Key key, KeyModifiers mods, int presses, Key? alternate = null)
    {
        var handled = new List<double>(presses);
        var frame = new List<double>(presses);
        for (int i = 0; i < presses; i++)
        {
            var k = alternate is { } a && i % 2 == 1 ? a : key;
            var sw = Stopwatch.StartNew();
            Press(window, k, mods);
            handled.Add(sw.Elapsed.TotalMilliseconds);
            await RenderedAsync(window);
            frame.Add(sw.Elapsed.TotalMilliseconds);
            var wait = 33 - (int)sw.ElapsedMilliseconds;
            if (wait > 0) await Task.Delay(wait);
        }
        return new JsonObject
        {
            ["presses"] = presses,
            ["handler_ms"] = Stats(handled),
            ["input_to_rendered_ms"] = Stats(frame),
        };
    }


    /// <summary>Frame intervals with a frame requested every tick but no input: the pacing baseline of this machine.</summary>
    private static async Task<JsonObject> IdleFramesAsync(MainWindow window, int frames)
    {
        var intervals = new List<double>(frames);
        var done = new TaskCompletionSource();
        TimeSpan? last = null;
        int remaining = frames;
        void OnFrame(TimeSpan t)
        {
            if (last is { } l) intervals.Add((t - l).TotalMilliseconds);
            last = t;
            if (remaining-- <= 0) done.TrySetResult();
            else window.RequestAnimationFrame(OnFrame);
        }
        window.RequestAnimationFrame(OnFrame);
        await done.Task;
        return new JsonObject { ["frames"] = frames, ["frame_interval_ms"] = Stats(intervals) };
    }
    /// <summary>PageDown, then wait until the compositor rendered it: the end-to-end cost of showing a new page.</summary>
    private static async Task<JsonObject> SequentialPagingAsync(MainWindow window, int pages)
    {
        var rendered = new List<double>(pages);
        for (int i = 0; i < pages; i++)
        {
            var sw = Stopwatch.StartNew();
            Press(window, i % 60 == 59 ? Key.Home : Key.PageDown, KeyModifiers.None);
            await RenderedAsync(window);
            rendered.Add(sw.Elapsed.TotalMilliseconds);
        }
        return new JsonObject { ["pages"] = pages, ["input_to_rendered_ms"] = Stats(rendered) };
    }

    /// <summary>PageDown on every frame: the intervals between frames show whether paging keeps the frame rate.</summary>
    private static async Task<JsonObject> ContinuousPagingAsync(MainWindow window, int frames)
    {
        var intervals = new List<double>(frames);
        var handler = new List<double>(frames);
        var renders = new List<double>(frames * 4);
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long renderAllocated = 0;
        Controls.FileListControl.RenderTimed = (ms, bytes) => { renders.Add(ms); renderAllocated += bytes; };
        long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        long totalAllocatedStart = GC.GetTotalAllocatedBytes();
        var pauseStart = GC.GetTotalPauseDuration();
        var done = new TaskCompletionSource();
        TimeSpan? last = null;
        int remaining = frames;
        void OnFrame(TimeSpan t)
        {
            if (last is { } l) intervals.Add((t - l).TotalMilliseconds);
            last = t;
            if (remaining-- <= 0)
            {
                done.TrySetResult();
                return;
            }
            var sw = Stopwatch.StartNew();
            Press(window, remaining % 60 == 0 ? Key.Home : Key.PageDown, KeyModifiers.None);
            handler.Add(sw.Elapsed.TotalMilliseconds);
            window.RequestAnimationFrame(OnFrame);
        }
        window.RequestAnimationFrame(OnFrame);
        await done.Task;
        Controls.FileListControl.RenderTimed = null;
        return new JsonObject
        {
            ["frames"] = frames,
            ["frame_interval_ms"] = Stats(intervals),
            ["handler_ms"] = Stats(handler),
            ["list_render_ms"] = Stats(renders),
            ["ui_thread_alloc_kib_per_frame"] = Math.Round((GC.GetAllocatedBytesForCurrentThread() - allocatedStart) / 1024.0 / frames, 1),
            ["list_render_alloc_kib_per_frame"] = Math.Round(renderAllocated / 1024.0 / frames, 1),
            ["process_alloc_kib_per_frame"] = Math.Round((GC.GetTotalAllocatedBytes() - totalAllocatedStart) / 1024.0 / frames, 1),
            ["gc_pause_ms"] = Math.Round((GC.GetTotalPauseDuration() - pauseStart).TotalMilliseconds, 1),
            ["gc_gen0_1_2"] = $"{GC.CollectionCount(0) - gen0}/{GC.CollectionCount(1) - gen1}/{GC.CollectionCount(2) - gen2}",
        };
    }

    private static void Press(MainWindow window, Key key, KeyModifiers mods)
    {
        Avalonia.Interactivity.Interactive target = window.ActiveList is { } list ? list : window;
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = mods, Source = target });
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = mods, Source = target });
    }

    /// <summary>Completes when the changes made so far were rendered by the compositor (render thread included).</summary>
    private static async Task RenderedAsync(TopLevel top)
    {
        var compositor = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(top)?.Compositor;
        if (compositor is null)
        {
            await NextFrameRenderedAsync(top);
            return;
        }
        await compositor.RequestCompositionBatchCommitAsync().Rendered;
    }

    /// <summary>Completes after the next animation tick and the layout/render work queued for it on the UI thread.</summary>
    private static async Task NextFrameRenderedAsync(TopLevel top)
    {
        var tick = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        top.RequestAnimationFrame(_ => tick.TrySetResult());
        await tick.Task;
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private static async Task SettleAsync(TopLevel top)
    {
        for (int i = 0; i < 10; i++) await NextFrameRenderedAsync(top);
    }

    private static JsonObject Stats(List<double> values)
    {
        if (values.Count == 0) return new JsonObject { ["n"] = 0 };
        var sorted = values.OrderBy(v => v).ToArray();
        double P(double q) => Math.Round(sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(q * sorted.Length) - 1)], 2);
        return new JsonObject
        {
            ["n"] = sorted.Length,
            ["p50"] = P(0.50),
            ["p95"] = P(0.95),
            ["p99"] = P(0.99),
            ["max"] = Math.Round(sorted[^1], 2),
            ["mean"] = Math.Round(sorted.Average(), 2),
        };
    }

    private static double Mib(long bytes) => Math.Round(bytes / 1048576.0, 1);

    private static JsonObject Environment(Window window)
    {
        var screen = window.Screens.Primary;
        var env = new JsonObject
        {
            ["os"] = RuntimeInformation.OSDescription,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["logical_processors"] = System.Environment.ProcessorCount,
            ["memory_gib"] = Math.Round(GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0, 1),
            ["screen"] = screen is null ? null : $"{screen.Bounds.Width}x{screen.Bounds.Height} @ {screen.Scaling * 100:0}%",
            ["window"] = $"{window.Bounds.Width:0}x{window.Bounds.Height:0}",
            ["rendering"] = Program.CompatibleRendering ? "Avalonia default composition" : "low-latency DXGI swap chain preferred",
#if DEBUG
            ["build"] = "Debug",
#else
            ["build"] = "Release",
#endif
        };
        if (OperatingSystem.IsWindows())
        {
            using var cpu = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            env["cpu"] = (cpu?.GetValue("ProcessorNameString") as string)?.Trim();
        }
        return env;
    }


    /// <summary>Whole-machine CPU use between marks (other work on the machine skews frame timings).</summary>
    private sealed class CpuLoad
    {
        private long _idle, _total;

        public CpuLoad() => Mark();

        public double? BusyPercentSinceMark()
        {
            if (!OperatingSystem.IsWindows() || !GetSystemTimes(out long idle, out long kernel, out long user)) return null;
            long total = kernel + user; // kernel time includes idle time
            double busy = total - _total <= 0 ? 0 : 100.0 * (1 - (double)(idle - _idle) / (total - _total));
            _idle = idle;
            _total = total;
            return Math.Round(busy, 1);
        }

        private void Mark() => BusyPercentSinceMark();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
    }
    private sealed class MemorySampler
    {
        private readonly Process _process = Process.GetCurrentProcess();

        public long PeakPrivate { get; private set; }
        public long PeakWorkingSet { get; private set; }
        public long PeakManaged { get; private set; }
        public long LastPrivate { get; private set; }

        public void Sample()
        {
            _process.Refresh();
            LastPrivate = _process.PrivateMemorySize64;
            PeakPrivate = Math.Max(PeakPrivate, LastPrivate);
            PeakWorkingSet = Math.Max(PeakWorkingSet, _process.WorkingSet64);
            PeakManaged = Math.Max(PeakManaged, GC.GetTotalMemory(false));
        }
    }
}
