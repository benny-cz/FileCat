using System.Diagnostics;
using System.Runtime.CompilerServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Platform.Windows.Tests;

public sealed class WindowsEnumerationTests
{
    private sealed class Sink(Action<int>? batchPublished = null) : IEnumerationSink
    {
        public readonly Dictionary<string, EntryData> Entries = new(StringComparer.Ordinal);
        public int LargestBatch;
        public void AddBatch(ReadOnlySpan<EntryData> entries)
        {
            LargestBatch = Math.Max(LargestBatch, entries.Length);
            foreach (var entry in entries) Assert.True(Entries.TryAdd(entry.Name, entry));
            batchPublished?.Invoke(entries.Length);
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
            var provider = new WindowsFileSystemProvider { UseNativeEnumeration = true };
            await provider.EnumerateAsync(location, native, TestContext.Current.CancellationToken);
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
            Assert.Equal(56, Unsafe.SizeOf<EntryData>()); // 64-bit managed layout; spill remains 48 bytes
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            cancel.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.EnumerateAsync(location, new Sink(), cancel.Token));

            using var midstream = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var prefix = new Sink(_ => midstream.Cancel());
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.EnumerateAsync(location, prefix, midstream.Token));
            Assert.Equal(512, prefix.Entries.Count);

            using var terminal = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var allEntries = new Sink(length => { if (length < 512) terminal.Cancel(); });
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.EnumerateAsync(location, allEntries, terminal.Token));
            Assert.Equal(native.Entries.Count, allEntries.Entries.Count);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Native_listing_matches_long_paths_and_distinguishes_junction_and_symlink_tags()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows() && Environment.GetEnvironmentVariable("FILECAT_ENUM_NATIVE_LINKS") == "1",
            "Requires the prepared Windows NTFS link fixture.");
        string root = Directory.CreateTempSubdirectory("fc-enum-reparse-").FullName;
        string junction = Path.Join(root, "junction"), link = Path.Join(root, "file-link");
        try
        {
            string folder = Directory.CreateDirectory(Path.Join(root, new string('d', 128))).FullName;
            string file = Path.Join(folder, new string('字', 140) + ".bin");
            File.WriteAllBytes(file, [6, 12, 24]);
            Assert.True(file.Length > 260);
            Junction.Create(junction, folder);
            File.CreateSymbolicLink(link, file);

            var ct = TestContext.Current.CancellationToken;
            var native = new WindowsFileSystemProvider { UseNativeEnumeration = true };
            var legacy = new WindowsFileSystemProvider();
            var rootLocation = Location.FileSystem(root);
            var managedResult = new Sink();
            var nativeResult = new Sink();
            await legacy.EnumerateAsync(rootLocation, managedResult, ct);
            await native.EnumerateAsync(rootLocation, nativeResult, ct);
            Assert.Equal(managedResult.Entries.Keys.Order(StringComparer.Ordinal), nativeResult.Entries.Keys.Order(StringComparer.Ordinal));
            foreach (var (name, entry) in managedResult.Entries)
            {
                Assert.Equal(entry.Kind, nativeResult.Entries[name].Kind);
                Assert.Equal(entry.Flags, nativeResult.Entries[name].Flags);
            }
            Assert.Equal(0xA0000003u, nativeResult.Entries["junction"].NativeReparseTag);
            Assert.Equal(0xA000000Cu, nativeResult.Entries["file-link"].NativeReparseTag);

            var longPath = new Sink();
            await native.EnumerateAsync(Location.FileSystem(folder), longPath, ct);
            var item = Assert.Single(longPath.Entries).Value;
            Assert.Equal(Path.GetFileName(file), item.Name);
            Assert.Equal(3, item.Size);
            Assert.Equal(0u, item.NativeReparseTag);
        }
        finally
        {
            if (File.Exists(link)) File.Delete(link);
            if (Directory.Exists(junction)) Directory.Delete(junction);
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_empty_directory_has_no_entries_and_a_missing_directory_fails_before_publication(bool native)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows enumeration.");
        string root = Directory.CreateTempSubdirectory("fc-enum-empty-").FullName;
        try
        {
            var provider = new WindowsFileSystemProvider { UseNativeEnumeration = native };
            var entries = new Sink();
            var ct = TestContext.Current.CancellationToken;
            await provider.EnumerateAsync(Location.FileSystem(root), entries, ct);
            Assert.Empty(entries.Entries);
            var missing = Location.FileSystem(Path.Join(root, "missing"));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.EnumerateAsync(missing, entries, ct));
            Assert.Empty(entries.Entries);
        }
        finally { Directory.Delete(root); }
    }

    [Fact]
    public async Task Native_listing_matches_managed_listing_on_a_prepared_mapped_and_unc_smb_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Requires the Windows SMB client.");
        string? mapped = Environment.GetEnvironmentVariable("FILECAT_ENUM_MAPPED_SMB_ROOT");
        string? unc = Environment.GetEnvironmentVariable("FILECAT_ENUM_NATIVE_SMB_ROOT");
        if (string.IsNullOrWhiteSpace(mapped) || string.IsNullOrWhiteSpace(unc))
            Assert.Skip("Set FILECAT_ENUM_MAPPED_SMB_ROOT and FILECAT_ENUM_NATIVE_SMB_ROOT to the same prepared SMB fixture folder.");
        var ct = TestContext.Current.CancellationToken;
        var nativeProvider = new WindowsFileSystemProvider { UseNativeEnumeration = true };
        var managedProvider = new WindowsFileSystemProvider();
        foreach (string root in new[] { mapped, unc })
        {
            var location = Location.FileSystem(root);
            var native = new Sink(); var managed = new Sink();
            await managedProvider.EnumerateAsync(location, managed, ct);
            await nativeProvider.EnumerateAsync(location, native, ct);
            Assert.Contains("Case.txt", native.Entries.Keys);
            Assert.Contains("case.txt", native.Entries.Keys);
            Assert.Contains("Unicode 名前.bin", native.Entries.Keys);
            Assert.Equal(EntryKind.Directory, native.Entries["owned-folder"].Kind);
            Assert.Equal(managed.Entries.Keys.Order(StringComparer.Ordinal), native.Entries.Keys.Order(StringComparer.Ordinal));
            foreach (var (name, entry) in managed.Entries)
            {
                var got = native.Entries[name];
                Assert.Equal(entry.Kind, got.Kind);
                Assert.Equal(entry.Attributes, got.Attributes);
                Assert.Equal(entry.Flags, got.Flags);
                Assert.Equal(entry.Size, got.Size);
                Assert.Equal(entry.Modified, got.Modified);
                Assert.Equal(entry.Created, got.Created);
            }

            var missing = Location.FileSystem(Path.Join(root, "fixture-directory-does-not-exist"));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => managedProvider.EnumerateAsync(missing, new Sink(), ct));
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => nativeProvider.EnumerateAsync(missing, new Sink(), ct));
        }
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
