using System.Diagnostics;
using System.Runtime.CompilerServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsEnumerationTests
{
    private sealed class Sink : IEnumerationSink
    {
        public readonly Dictionary<string, EntryData> Entries = new(StringComparer.Ordinal);
        public int LargestBatch;
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            LargestBatch = Math.Max(LargestBatch, entries.Length);
            foreach (var entry in entries) Assert.True(Entries.TryAdd(entry.Name, entry));
        }
        public void ReportIssue(string message) => Assert.Fail(message);
    }

    [Fact]
    public async Task Native_listing_preserves_names_attributes_batches_and_metadata_without_child_opens()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows native enumeration."); return; }
        string root = Directory.CreateTempSubdirectory("fc-enum-").FullName;
        try
        {
            for (int i = 0; i < 1300; i++) File.WriteAllText(Path.Join(root, "file" + i + ".txt"), "sample");
            Directory.CreateDirectory(Path.Join(root, "folder"));
            File.WriteAllText(Path.Join(root, "Unicode 名前.txt"), "unicode");
            File.SetAttributes(Path.Join(root, "file0.txt"), FileAttributes.Hidden | FileAttributes.System);
            var managed = new Sink(); var native = new Sink();
            var location = Location.FileSystem(root);
            await new LocalFileSystemProvider().EnumerateAsync(location, managed, TestContext.Current.CancellationToken);
            await new WindowsFileSystemProvider { UseNativeEnumeration = true }.EnumerateAsync(location, native, TestContext.Current.CancellationToken);
            Assert.Equal(managed.Entries.Keys.Order(StringComparer.Ordinal), native.Entries.Keys.Order(StringComparer.Ordinal));
            Assert.Equal(512, native.LargestBatch);
            foreach (var (name, entry) in managed.Entries)
            {
                var got = native.Entries[name];
                Assert.Equal(entry.Attributes, got.Attributes);
                Assert.Equal(entry.Flags, got.Flags);
                Assert.Equal(entry.Kind, got.Kind);
                Assert.Equal(entry.Size, got.Size);
                Assert.Equal(entry.Modified, got.Modified);
                Assert.Equal(entry.Created, got.Created);
            }
            Assert.Equal(56, Unsafe.SizeOf<EntryData>()); // qualified x64 managed layout; spill remains 48 bytes
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => new WindowsFileSystemProvider { UseNativeEnumeration = true }.EnumerateAsync(location, new Sink(), cancel.Token));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Enumeration_performance_records_native_and_managed_timings_on_a_prepared_directory()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Skip("Windows enumeration performance."); return; }
        if (Environment.GetEnvironmentVariable("FILECAT_ENUM_FIXTURE") is not { Length: > 0 } path)
        { Assert.Skip("Set FILECAT_ENUM_FIXTURE to a deliberately prepared scale directory; no user-tree scan."); return; }
        var location = Location.FileSystem(path);
        if (Environment.GetEnvironmentVariable("FILECAT_ENUM_ROUTE") is { Length: > 0 } route)
        {
            bool native = route switch
            {
                "managed" => false,
                "native" => true,
                _ => throw new ArgumentException("FILECAT_ENUM_ROUTE must be managed or native."),
            };
            await Measure(0, native);
            return;
        }

        for (int run = 0; run < 5; run++)
        {
            foreach (bool useNative in run % 2 == 0 ? new[] { false, true } : new[] { true, false })
                await Measure(run, useNative);
        }

        async Task Measure(int run, bool native)
        {
            using var process = Process.GetCurrentProcess();
            int handlesBefore = process.HandleCount;
            long workingSetBefore = process.WorkingSet64, allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var sink = new CountingSink();
            var clock = Stopwatch.StartNew();
            await new WindowsFileSystemProvider { UseNativeEnumeration = native }.EnumerateAsync(location, sink, TestContext.Current.CancellationToken);
            clock.Stop();
            long allocated = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            process.Refresh();
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"run={run} native={native} count={sink.Count} elapsedMs={clock.Elapsed.TotalMilliseconds:F2} allocatedBytes={allocated} " +
                $"handlesBefore={handlesBefore} handlesAfter={process.HandleCount} " +
                $"workingSetBefore={workingSetBefore} workingSetAfter={process.WorkingSet64} peakWorkingSet={process.PeakWorkingSet64}");
            if (int.TryParse(Environment.GetEnvironmentVariable("FILECAT_ENUM_EXPECTED_COUNT"), out int expected))
                Assert.Equal(expected, sink.Count);
        }
    }

    private sealed class CountingSink : IEnumerationSink
    {
        public int Count;
        public void AddBatch(ReadOnlySpan<EntryData> entries) => Count += entries.Length;
        public void ReportIssue(string message) => Assert.Fail(message);
    }
}
