using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Metadata;
using FileCat.Core.Records;
using FileCat.Core.Resources;
using FileCat.Core.Threading;

namespace FileCat.Core.Tests;

/// <summary>
/// F-META on Linux and macOS: the optional Allocated and Hard links columns read through MetadataService.Compute on the
/// real Unix metadata reader. Each value is compared with what the system <c>stat</c> command reports for the same object:
/// 512-byte blocks for allocation and the link count for regular files. On macOS the reader reports all forks.
/// </summary>
public sealed partial class UnixFileDetailsColumnsNativeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Link(string existing, string added);

    private sealed record Observed(MetadataState State, string? Text, IComparable? Key, string Detail);

    private static Observed Read(MetadataService metadata, string field, string path, EntryKind kind)
    {
        FileSystemInfo info = kind == EntryKind.Directory ? new DirectoryInfo(path) : new FileInfo(path);
        var entry = new EntryData(Path.GetFileName(path), kind, info is FileInfo file && kind == EntryKind.File ? file.Length : -1, info.LastWriteTimeUtc.Ticks)
            { Attributes = (uint)info.Attributes };
        var value = metadata.Compute(field, path, entry, Ct);
        return value.Value is FileDetailsFields.Cell cell
            ? new(value.State, cell.Text, cell.Key, cell.Explanation)
            : new(value.State, null, null, value.Detail ?? "");
    }

    /// <summary>The system's own report for the object itself (links are not followed): allocated bytes and link count.</summary>
    private static (long Allocated, uint Links) Stat(string path)
    {
        // The system stat itself: BSD stat on macOS (its link count is %l), GNU stat on Linux.
        var start = new ProcessStartInfo(OperatingSystem.IsMacOS() ? "/usr/bin/stat" : "stat") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (string argument in OperatingSystem.IsMacOS() ? new[] { "-f", "%b %l", path } : new[] { "-c", "%b %B %h", path })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string[] parts = process.StandardOutput.ReadToEnd().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        // macOS reports 512-byte blocks; GNU stat prints the unit (%B) beside the count.
        long unit = OperatingSystem.IsMacOS() ? 512 : long.Parse(parts[1]);
        return (long.Parse(parts[0]) * unit, uint.Parse(parts[^1]));
    }

    [Fact]
    public void Native_columns_report_the_system_allocation_and_link_count_for_files_sparse_files_folders_and_links()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS(), "The Unix metadata reader.");
        using var temp = new TempDir();
        // macOS reaches its temporary folder through /var, a link; the reader does not follow links in the path.
        string root = OperatingSystem.IsMacOS() && temp.Path.StartsWith("/var/", StringComparison.Ordinal) ? "/private" + temp.Path : temp.Path;
        using var io = new DeviceIoScheduler();
        using var metadata = new MetadataService(io);
        foreach (var field in FileDetailsFields.Create(new UnixFileRecords(), windows: false)) metadata.Register(field);
        const string allocated = FileDetailsFields.Allocated, links = FileDetailsFields.HardLinks;

        string plain = Path.Join(root, "plain.bin"), linked = Path.Join(root, "linked.bin"), other = Path.Join(root, "other-name.bin");
        File.WriteAllBytes(plain, new byte[10_000]);
        File.WriteAllBytes(linked, new byte[10_000]);
        Assert.True(Link(linked, other) == 0, $"link: {Marshal.GetLastPInvokeError()}");
        string sparse = Path.Join(root, "sparse.bin");
        using (var stream = new FileStream(sparse, FileMode.CreateNew, FileAccess.Write))
        {
            stream.SetLength(16 << 20);
            stream.Position = 8 << 20;
            stream.WriteByte(1);
            stream.Flush(flushToDisk: true);
        }
        string folder = Directory.CreateDirectory(Path.Join(root, "folder")).FullName;
        string symlink = Path.Join(root, "file-link");
        File.CreateSymbolicLink(symlink, plain);

        var rows = new (string Name, string Path, EntryKind Kind)[]
        {
            ("plain", plain, EntryKind.File), ("linked", linked, EntryKind.File), ("other name", other, EntryKind.File),
            ("sparse", sparse, EntryKind.File), ("folder", folder, EntryKind.Directory), ("file link", symlink, EntryKind.File),
        };
        var seen = new Dictionary<(string, string), Observed>();
        foreach (var (name, path, kind) in rows)
            foreach (string field in new[] { allocated, links })
            {
                var value = seen[(name, field)] = Read(metadata, field, path, kind);
                TestContext.Current.TestOutputHelper?.WriteLine($"{name,-10} {field,-12} {value.State,-11} {value.Text ?? "-",-26} key={value.Key?.ToString() ?? "none"}");
            }

        // Regular files: the system's 512-byte-block allocation and link count for the object.
        foreach (string name in new[] { "plain", "linked", "other name", "sparse" })
        {
            string path = rows.Single(row => row.Name == name).Path;
            var (systemAllocated, systemLinks) = Stat(path);
            Assert.Equal(MetadataState.Available, seen[(name, allocated)].State);
            Assert.Equal((IComparable)systemAllocated, seen[(name, allocated)].Key);
            Assert.Equal((IComparable)systemLinks, seen[(name, links)].Key);
        }
        Assert.Equal((IComparable)1u, seen[("plain", links)].Key);
        Assert.Equal((IComparable)2u, seen[("linked", links)].Key);
        Assert.Equal((IComparable)2u, seen[("other name", links)].Key);
        // Where the filesystem kept the file sparse (ext4 here; APFS allocated it whole), only the allocation is reported.
        if (Stat(sparse).Allocated < 16 << 20) Assert.InRange(Assert.IsType<long>(seen[("sparse", allocated)].Key), 1, (16 << 20) - 1);

        // Linux reports the directory object's own allocation. Darwin returns file attributes only for files, so a folder's
        // allocation is unsupported there rather than guessed. The Hard links column does not apply to folders.
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(MetadataState.Available, seen[("folder", allocated)].State);
            Assert.Equal((IComparable)Stat(folder).Allocated, seen[("folder", allocated)].Key);
        }
        else Assert.NotEqual(MetadataState.Available, seen[("folder", allocated)].State);
        Assert.NotEqual(MetadataState.Available, seen[("folder", links)].State);

        // A symbolic link is described as itself, never as its target: no regular-file link count.
        Assert.NotEqual(MetadataState.Available, seen[("file link", links)].State);
    }
}
