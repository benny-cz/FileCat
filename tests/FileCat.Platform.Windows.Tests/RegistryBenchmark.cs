using System.Diagnostics;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// Plan §21.2 ("Registry: throughput, first results, memory, cancellation, completeness accuracy"), read-only on this
/// machine's own Registry: listing HKEY_CLASSES_ROOT's top level and searching HKLM\SOFTWARE by names and by data.
/// Set FILECAT_REGISTRY_BENCH=1; docs/validation/TV-08.md records the results with search and comparison.
/// </summary>
public sealed class RegistryBenchmark
{
    private sealed class Sink : IEnumerationSink
    {
        public int Count;
        public long FirstBatchTicks = -1;
        public Stopwatch Clock { get; } = Stopwatch.StartNew();

        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            if (FirstBatchTicks < 0 && entries.Length > 0) FirstBatchTicks = Clock.ElapsedTicks;
            Count += entries.Length;
        }

        public void ReportIssue(string message) { }
    }

    [Fact]
    public async Task Registry_listing_and_search_are_fast_bounded_and_cancelable()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("The Registry is part of Windows.");
        if (Environment.GetEnvironmentVariable("FILECAT_REGISTRY_BENCH") is not { Length: > 0 }) Assert.Skip("Set FILECAT_REGISTRY_BENCH=1 to measure the Registry.");
        var log = TestContext.Current.TestOutputHelper;
        var provider = new WindowsRegistryProvider();
        var classes = new Location(Schemes.Registry, "HKCR");
        var sink = new Sink();
        await provider.EnumerateAsync(classes, sink, TestContext.Current.CancellationToken);
        var listed = sink.Clock.Elapsed;
        log?.WriteLine($"Listing HKEY_CLASSES_ROOT: {sink.Count:N0} keys in {listed.TotalMilliseconds:F0} ms, first batch after {TimeSpan.FromTicks(sink.FirstBatchTicks).TotalMilliseconds:F0} ms.");

        var software = new Location(Schemes.Registry, @"HKLM\SOFTWARE");
        RegistrySearchReport Search(string what, RegistrySearchQuery query, CancellationToken ct)
        {
            int found = 0;
            var clock = Stopwatch.StartNew();
            var report = RegistrySearch.Run(query, (_, _) => found++, _ => { }, ct);
            log?.WriteLine($"{what}: {report.KeysVisited:N0} keys and {report.ValuesVisited:N0} values in {clock.Elapsed.TotalMilliseconds:F0} ms, " +
                           $"{report.Matches:N0} matches{(report.StoppedAtLimit ? ", stopped at its limit (said so)" : "")}.");
            return report;
        }
        var names = Search(@"Names containing ""microsoft"" under HKLM\SOFTWARE", new RegistrySearchQuery(software, "microsoft", true, true, false, null, false, true), TestContext.Current.CancellationToken);
        Assert.True(names.Matches > 0);
        Search(@"Data containing ""windows"" under HKLM\SOFTWARE", new RegistrySearchQuery(software, "windows", false, false, true, null, false, true), TestContext.Current.CancellationToken);

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var stopClock = Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => RegistrySearch.Run(new RegistrySearchQuery(software, "no such text anywhere", true, true, true, null, false, true),
            (_, _) => { }, _ => { }, cancel.Token));
        var stoppedAfter = stopClock.Elapsed - TimeSpan.FromMilliseconds(100);
        log?.WriteLine($"Cancel a search of HKLM\\SOFTWARE 100 ms in: stopped {Math.Max(0, stoppedAfter.TotalMilliseconds):F0} ms after the request.");
        Assert.True(stoppedAfter < TimeSpan.FromMilliseconds(250), $"Canceling took {stoppedAfter}.");
    }
}
