using System.Collections.Concurrent;
using System.Diagnostics;
using FileCat.Core.Listing;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

int count = args.Length > 0 ? int.Parse(args[0]) : 1_000_000;
int panels = args.Length > 1 ? int.Parse(args[1]) : 1;
long indexBudget = args.Length > 2 ? long.Parse(args[2]) * 1024 * 1024 : 512L * 1024 * 1024;
if (count < 1 || panels is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(args));
string scratch = Path.Combine(Path.GetTempPath(), "FileCat-listing-scale-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
try
{
    using var io = new DeviceIoScheduler();
    var ui = new PumpDispatcher();
    var providers = new ProviderRegistry();
    providers.Register(new SyntheticProvider(count));
    var indexes = new IndexMemoryBudget(indexBudget);
    var models = Enumerable.Range(0, panels)
        .Select(_ => new ListingModel(providers, io, ui, scratch, indexBudget: indexes)).ToArray();
    var clock = Stopwatch.StartNew();
    var process = Process.GetCurrentProcess();
    long firstRowsMs = -1;
    long peakPrivate = 0, peakManaged = 0, lastSampleMs = -100;
    foreach (var model in models) model.Load(Location.FileSystem(Path.Combine(scratch, "synthetic")));
    while (models.Any(m => m.State == ListingState.Loading))
    {
        ui.Pump(50);
        if (firstRowsMs < 0 && models.All(m => m.VisibleCount > 0)) firstRowsMs = clock.ElapsedMilliseconds;
        if (clock.ElapsedMilliseconds - lastSampleMs >= 100)
        {
            lastSampleMs = clock.ElapsedMilliseconds;
            process.Refresh();
            peakPrivate = Math.Max(peakPrivate, process.PrivateMemorySize64);
            peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
        }
        if (clock.Elapsed > TimeSpan.FromMinutes(10)) throw new TimeoutException("Listing scale run exceeded ten minutes.");
    }
    ui.Pump(0);
    process.Refresh();
    Console.WriteLine($"count={count} panels={panels} complete_ms={clock.ElapsedMilliseconds} first_rows_ms={firstRowsMs}");
    Console.WriteLine($"visible={string.Join(",", models.Select(m => m.VisibleCount))} spill_mib={models.Sum(m => m.Store.SpillBytes) / 1024.0 / 1024.0:F1} external_index_mib={models.Sum(m => m.ExternalIndexBytes) / 1024.0 / 1024.0:F1} reserved_index_mib={indexes.ReservedBytes / 1024.0 / 1024.0:F1}");
    Console.WriteLine($"managed_mib={GC.GetTotalMemory(true) / 1024.0 / 1024.0:F1} private_mib={process.PrivateMemorySize64 / 1024.0 / 1024.0:F1} peak_managed_mib={peakManaged / 1024.0 / 1024.0:F1} peak_private_mib={peakPrivate / 1024.0 / 1024.0:F1}");
    // Whole-listing commands on the first listing, twice each: the first pass includes mapping the spill files.
    var first = models[0];
    static string Time(Action action)
    {
        var runs = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            action();
            runs.Add(Math.Round(sw.Elapsed.TotalMilliseconds, 1));
        }
        return string.Join("/", runs);
    }
    Console.WriteLine($"external_index={first.HasExternalIndex} spilled={first.Store.IsSpilled}");
    Console.WriteLine($"iterate_visible_ms={Time(() => first.FindVisible(0, true, _ => false))}");
    Console.WriteLine($"quick_search_miss_ms={Time(() => first.FindVisible(0, true, n => n.StartsWith("zz-none", StringComparison.OrdinalIgnoreCase)))}");
    Console.WriteLine($"quick_search_miss_culture_ms={Time(() => first.FindVisible(0, true, n => n.StartsWith("zz-none", StringComparison.CurrentCultureIgnoreCase)))}");
    Console.WriteLine($"find_name_miss_ms={Time(() => first.FindStoreIndex("zz-none" + Guid.NewGuid()))}");
    Console.WriteLine($"mark_by_mask_ms={Time(() => first.MarkByMask(FileCat.Core.Selection.Mask.Parse("*7-long*"), true, false))}");
    var sample = Enumerable.Range(0, 1_000_000).Select(i => $"file-{i:0000000}-long-αβγ.txt").ToArray();
    var parsed = FileCat.Core.Selection.Mask.Parse("*7-long*");
    Console.WriteLine($"mask_only_ms={Time(() => { foreach (var n in sample) parsed.IsMatch(n.AsSpan()); })}");
    Console.WriteLine($"indexof_only_ms={Time(() => { foreach (var n in sample) n.AsSpan().IndexOf("7-long", StringComparison.OrdinalIgnoreCase); })}");
    Console.WriteLine($"indexof_ordinal_ms={Time(() => { foreach (var n in sample) n.AsSpan().IndexOf("7-long", StringComparison.Ordinal); })}");
    Console.WriteLine($"scan_mask_only_ms={Time(() => first.FindVisible(0, true, n => parsed.IsMatch(n) && false))}");
    Console.WriteLine($"mark_by_mask_again_ms={Time(() => first.MarkByMask(parsed, true, false))}");
    Console.WriteLine($"mark_all_ms={Time(() => first.MarkAll(true))}");
    Console.WriteLine($"mark_stats_ms={Time(() => { first.InvertMarks(true); first.InvertMarks(true); first.GetMarkStats(); })}");
    foreach (var model in models)
    {
        if (model.Error is not null) throw new Exception(model.Error);
        if (model.TotalCount != count) throw new Exception($"Expected {count} entries; got {model.TotalCount}.");
        model.Dispose();
    }
    var deadline = Stopwatch.StartNew();
    while (Directory.EnumerateFiles(scratch, "listing-*").Any() && deadline.Elapsed < TimeSpan.FromSeconds(10))
        Thread.Sleep(10);
    if (Directory.EnumerateFiles(scratch, "listing-*").Any()) throw new IOException("Spill files were not deleted.");
}
finally
{
    try { Directory.Delete(scratch, recursive: true); } catch (IOException) { }
}

sealed class PumpDispatcher : IUiDispatcher
{
    private readonly ConcurrentQueue<Action> _actions = new();
    private readonly int _thread = Environment.CurrentManagedThreadId;
    public bool CheckAccess() => Environment.CurrentManagedThreadId == _thread;
    public void Post(Action action) => _actions.Enqueue(action);
    public void Pump(int waitMs)
    {
        if (_actions.TryDequeue(out var action)) action();
        else if (waitMs > 0) Thread.Sleep(waitMs);
        while (_actions.TryDequeue(out action)) action();
    }
}

sealed class SyntheticProvider(int count) : ResourceProvider
{
    public override string Scheme => Schemes.FileSystem;
    public override string GetDisplayPath(Location location) => location.Path;
    public override Location? GetParent(Location location) => null;
    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate;
    public override Location? GetChildLocation(Location parent, in EntryData entry) => null;
    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        var batch = new EntryData[512];
        for (int from = 0; from < count; from += batch.Length)
        {
            ct.ThrowIfCancellationRequested();
            int n = Math.Min(batch.Length, count - from);
            for (int i = 0; i < n; i++)
            {
                int sequence = count - from - i;
                batch[i] = new EntryData($"file-{sequence:0000000}-long-αβγ.txt", EntryKind.File, sequence);
            }
            sink.AddBatch(batch.AsSpan(0, n));
        }
        return Task.CompletedTask;
    }
}

