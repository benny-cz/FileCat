using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Core.Metadata;
using FileCat.Core.Resources;
using FileCat.Core.Threading;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// F-META on Windows: the optional Allocated, Hard links and Reparse type columns read through MetadataService.Compute
/// on the real metadata reader, and the expiry of a cached value whose size and modification time did not change.
/// </summary>
public sealed partial class WindowsDetailsNativeGapTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MetadataService Columns(DeviceIoScheduler io, Func<long>? clock = null)
    {
        var metadata = clock is null ? new MetadataService(io) : new MetadataService(io, clock);
        foreach (var field in FileDetailsFields.Create(new WindowsFileRecords(), windows: true)) metadata.Register(field);
        return metadata;
    }

    /// <summary>The row a listing would hold: size and modification time are the cache key's revision evidence.</summary>
    private static EntryData Entry(string path, EntryKind kind)
    {
        FileSystemInfo info = kind == EntryKind.Directory ? new DirectoryInfo(path) : new FileInfo(path);
        return new EntryData(Path.GetFileName(path), kind, info is FileInfo file ? file.Length : -1, info.LastWriteTimeUtc.Ticks)
            { Attributes = (uint)info.Attributes };
    }

    private sealed record Observed(MetadataState State, MetadataObservation Observation, string? Text, IComparable? Key, string Detail);

    private static Observed Read(MetadataService metadata, string field, string path, EntryKind kind) => Describe(metadata.Compute(field, path, Entry(path, kind), Ct));

    private static Observed Describe(MetadataValue value) => value.Value is FileDetailsFields.Cell cell
        ? new(value.State, value.Observation, cell.Text, cell.Key, cell.Explanation)
        : new(value.State, value.Observation, null, null, value.Detail ?? "");

    [Fact]
    public void Native_columns_report_allocation_names_and_reparse_types_for_files_links_sparse_files_and_folders()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native metadata reader.");
        string root = Directory.CreateTempSubdirectory("fc-meta-columns-").FullName;
        try
        {
            Assert.SkipUnless(new DriveInfo(root).DriveFormat == "NTFS", "The allocation fixture needs NTFS.");
            Assert.True(GetDiskFreeSpace(Path.GetPathRoot(root)!, out uint sectorsPerCluster, out uint bytesPerSector, out _, out _));
            long cluster = (long)sectorsPerCluster * bytesPerSector;
            using var io = new DeviceIoScheduler();
            using var metadata = Columns(io);
            const string allocated = FileDetailsFields.Allocated, links = FileDetailsFields.HardLinks, reparse = FileDetailsFields.ReparseType;

            string plain = Path.Join(root, "plain.bin"), linked = Path.Join(root, "linked.bin"), other = Path.Join(root, "other-name.bin");
            File.WriteAllBytes(plain, new byte[10_000]); // beyond what an MFT record keeps resident
            File.WriteAllBytes(linked, new byte[10_000]);
            Assert.True(CreateHardLink(other, linked, 0), $"CreateHardLink: {Marshal.GetLastPInvokeError()}");
            string sparse = Path.Join(root, "sparse.bin");
            long cachedOnly;
            using (var stream = new FileStream(sparse, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.True(SetSparse(stream.SafeFileHandle), $"FSCTL_SET_SPARSE: {Marshal.GetLastPInvokeError()}");
                stream.SetLength(16 << 20);
                stream.Position = 8 << 20;
                stream.WriteByte(1);
                stream.Flush(); // to the system cache only: NTFS may still hold a reservation rather than an allocation
                cachedOnly = new WindowsFileRecords().ReadSummary(sparse, Ct).Allocated.Value;
                stream.Flush(flushToDisk: true);
            }
            TestContext.Current.TestOutputHelper?.WriteLine($"sparse allocation while its only write was cached: {cachedOnly:N0} B; cluster {cluster:N0} B");
            string folder = Directory.CreateDirectory(Path.Join(root, "folder")).FullName;
            string junction = Path.Join(root, "junction");
            Junction.Create(junction, folder);
            string link = Path.Join(root, "file-link");
            File.CreateSymbolicLink(link, plain);

            var rows = new (string Name, string Path, EntryKind Kind)[]
            {
                ("plain", plain, EntryKind.File), ("linked", linked, EntryKind.File), ("other name", other, EntryKind.File),
                ("sparse", sparse, EntryKind.File), ("folder", folder, EntryKind.Directory), ("junction", junction, EntryKind.Directory),
                ("file link", link, EntryKind.File),
            };
            var seen = new Dictionary<(string, string), Observed>();
            foreach (var (name, path, kind) in rows)
                foreach (string field in new[] { allocated, links, reparse })
                {
                    var value = seen[(name, field)] = Read(metadata, field, path, kind);
                    TestContext.Current.TestOutputHelper?.WriteLine($"{name,-10} {field,-12} {value.State,-11} {value.Text ?? "-",-26} key={value.Key?.ToString() ?? "none"}");
                }

            // Regular files: allocation in whole clusters; names counted per object.
            Assert.Equal(MetadataState.Available, seen[("plain", allocated)].State);
            Assert.Equal((IComparable)((10_000 + cluster - 1) / cluster * cluster), seen[("plain", allocated)].Key);
            Assert.Contains("not exclusive ownership", seen[("plain", allocated)].Detail);
            Assert.Equal((IComparable)1u, seen[("plain", links)].Key);
            Assert.Equal((IComparable)2u, seen[("linked", links)].Key);
            Assert.Equal((IComparable)2u, seen[("other name", links)].Key);
            Assert.Equal(MetadataState.Absent, seen[("plain", reparse)].State);

            // A sparse file reports only what is allocated: here one compression unit around the single written byte.
            long sparseAllocated = Assert.IsType<long>(seen[("sparse", allocated)].Key);
            Assert.InRange(sparseAllocated, cluster, (16 << 20) - 1);
            Assert.Equal(0, sparseAllocated % cluster);
            if (cluster == 4096) Assert.Equal(64 * 1024, sparseAllocated);
            Assert.Equal((IComparable)1u, seen[("sparse", links)].Key);

            // Folders: the directory object's own allocation; the Hard links column does not apply to them, junctions included.
            Assert.Equal(MetadataState.Available, seen[("folder", allocated)].State);
            Assert.Contains("contents are not counted", seen[("folder", allocated)].Detail);
            Assert.Equal(MetadataState.Absent, seen[("folder", links)].State);
            Assert.Equal(MetadataState.Absent, seen[("folder", reparse)].State);
            Assert.Equal(MetadataState.Available, seen[("junction", allocated)].State);
            Assert.Equal(MetadataState.Absent, seen[("junction", links)].State);
            Assert.Equal("Junction or volume mount", seen[("junction", reparse)].Text);
            Assert.Equal((IComparable)"Junction or volume mount", seen[("junction", reparse)].Key);
            Assert.Contains("does not distinguish a junction from a volume mount", seen[("junction", reparse)].Detail);

            // A file symbolic link is described as itself; its link count is refused rather than shown as a regular file's.
            Assert.Equal("Symbolic link", seen[("file link", reparse)].Text);
            Assert.Equal((IComparable)"Symbolic link", seen[("file link", reparse)].Key);
            Assert.Equal(MetadataState.Unsupported, seen[("file link", links)].State);
            Assert.Contains("qualified regular-file observation", seen[("file link", links)].Detail);
            Assert.Equal(MetadataState.Available, seen[("file link", allocated)].State);
        }
        finally
        {
            if (Directory.Exists(Path.Join(root, "junction"))) Directory.Delete(Path.Join(root, "junction"));
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_new_name_with_the_same_size_and_time_stays_cached_until_expiry_and_Get_and_Compute_then_read_again()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows native metadata reader.");
        string root = Directory.CreateTempSubdirectory("fc-meta-expiry-").FullName;
        try
        {
            long now = 1_000_000;
            using var io = new DeviceIoScheduler();
            using var metadata = Columns(io, () => Volatile.Read(ref now));
            const string links = FileDetailsFields.HardLinks;
            string file = Path.Join(root, "data.bin"), other = Path.Join(root, "second-name.bin");
            File.WriteAllBytes(file, new byte[10_000]);
            var entry = Entry(file, EntryKind.File);
            Assert.Equal((IComparable)1u, Read(metadata, links, file, EntryKind.File).Key);

            Assert.True(CreateHardLink(other, file, 0), $"CreateHardLink: {Marshal.GetLastPInvokeError()}");
            var unchanged = Entry(file, EntryKind.File);
            Assert.Equal((entry.Size, entry.Modified), (unchanged.Size, unchanged.Modified));
            // Inside the 20-second interval the (path, size, time) key cannot see the new name.
            var cached = Describe(metadata.Compute(links, file, unchanged, Ct));
            Assert.Equal(MetadataObservation.Current, cached.Observation);
            Assert.Equal((IComparable)1u, cached.Key);

            Interlocked.Add(ref now, 21_000);
            var expired = metadata.Peek(links, file, unchanged);
            Assert.Equal(MetadataObservation.LastKnown, expired.Observation);
            Assert.Null(metadata.Field(links)!.CaptureKey(expired)); // an expired value keeps its text but has no sort key
            var fresh = Describe(metadata.Compute(links, file, unchanged, Ct));
            Assert.Equal(MetadataObservation.Current, fresh.Observation);
            Assert.Equal((IComparable)2u, fresh.Key);

            // Get shares the expiry: past the next interval it shows the last value and schedules a read on its own.
            File.Delete(other);
            Interlocked.Add(ref now, 21_000);
            var shown = Describe(metadata.Get(links, file, unchanged, PathUtil.GetDeviceKey(file), slowLocation: false));
            Assert.Equal(MetadataObservation.LastKnown, shown.Observation);
            Assert.Equal((IComparable)2u, shown.Key);
            var deadline = DateTime.UtcNow.AddSeconds(20);
            Observed current;
            while ((current = Describe(metadata.Peek(links, file, unchanged))).Key is not 1u && DateTime.UtcNow < deadline)
                await Task.Delay(10, Ct);
            Assert.Equal(MetadataObservation.Current, current.Observation);
            Assert.Equal((IComparable)1u, current.Key);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string name, string existing, nint security);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);

    private static unsafe bool SetSparse(SafeFileHandle file) => Control(file, 0x900C4 /* FSCTL_SET_SPARSE */, null, 0, null, 0, out _, 0);

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool Control(SafeFileHandle file, uint code, void* input, uint inputLength, void* output, uint outputLength, out uint returned, nint overlapped);
}
