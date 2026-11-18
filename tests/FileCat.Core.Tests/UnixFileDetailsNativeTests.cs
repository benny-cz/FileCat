using System.Runtime.InteropServices;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public void Native_summary_describes_files_directories_and_links_without_legacy_value_reads()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) { Assert.Skip("Unix native metadata fixture."); return; }
        var ct = TestContext.Current.CancellationToken;
        string root = Directory.CreateTempSubdirectory("filecat-details-").FullName;
        try
        {
            string file = Path.Combine(root, "file.txt"), link = Path.Combine(root, "link");
            File.WriteAllText(file, "payload-sentinel-must-not-appear-in-report");
            File.CreateSymbolicLink(link, "file.txt");
            var reader = new UnixFileRecords();
            var regular = reader.ReadSummary(file, ct);
            Assert.Equal(FileObjectKind.File, regular.Kind);
            Assert.Equal(FactState.Known, regular.Identity.State);
            Assert.Equal(FactState.Known, regular.Allocated.State);
            Assert.Equal(FactState.Known, regular.LinkCount.State);
            Assert.Equal(1u, regular.LinkCount.Value);
            Assert.Equal(FileObjectKind.Directory, reader.ReadSummary(root, ct).Kind);
            var symbolic = reader.ReadSummary(link, ct);
            Assert.Equal(FileObjectKind.SymbolicLink, symbolic.Kind);
            Assert.NotEqual(regular.Identity.Value, symbolic.Identity.Value);
            if (OperatingSystem.IsLinux()) Assert.Equal("file.txt", symbolic.Target.Value?.SubstituteName);
            var full = reader.ReadFull(file, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.DoesNotContain("payload-sentinel", full.ToText());
            Assert.Contains("Not acquired:", full.ToText());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Linux_gets_the_mount_type_of_the_held_item_through_a_parent_symlink()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Requires Linux statx and procfs.");
        using var root = new TempDir();
        string link = Path.Join(root.Path, "proc-alias");
        Directory.CreateSymbolicLink(link, "/proc/self");
        var reader = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        var entry = reader.ReadSummary(link, ct);
        Assert.Equal(FileObjectKind.SymbolicLink, entry.Kind);
        Assert.Equal(FactState.Known, entry.FileSystem.State);
        Assert.Equal(reader.ReadSummary(root.Path, ct).FileSystem.Value, entry.FileSystem.Value);
        var proc = reader.ReadSummary(Path.Join(link, "stat"), ct);
        Assert.Equal(FactState.Known, proc.FileSystem.State);
        Assert.Equal(FactScope.Volume, proc.FileSystem.Scope);
        Assert.Equal("proc", proc.FileSystem.Value);
    }

    [Fact]
    public void Linux_summary_of_FIFO_does_not_open_it_for_data_or_wait_for_a_writer()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Linux O_PATH fixture."); return; }
        string root = Directory.CreateTempSubdirectory("filecat-fifo-details-").FullName;
        try
        {
            string fifo = Path.Combine(root, "fifo");
            Assert.Equal(0, MakeFifo(fifo, 0x180));
            var snapshot = new UnixFileRecords().ReadSummary(fifo, TestContext.Current.CancellationToken);
            Assert.Equal(FileObjectKind.Fifo, snapshot.Kind);
            Assert.Equal(FactState.Known, snapshot.Identity.State);
            var full = new UnixFileRecords().ReadFull(fifo, FileDetailsPolicy.MetadataOnly, TestContext.Current.CancellationToken);
            Assert.DoesNotContain(full.Technical.Sections, section => section.Title == "Main-stream extent records");
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Linux_FIEMAP_reopens_the_pinned_inode_even_after_its_path_becomes_a_FIFO()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_FIEMAP") == "1",
            "Requires a disposable Linux filesystem with native FIEMAP support.");
        string root = Directory.CreateTempSubdirectory("filecat-fiemap-details-").FullName;
        var ct = TestContext.Current.CancellationToken;
        try
        {
            string path = Path.Join(root, "source"), retired = Path.Join(root, "renamed");
            const int ranges = 520, stride = 32 * 1024;
            using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                file.SetLength((ranges + 1L) * stride);
                for (int i = 1; i <= ranges; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    file.Position = (long)i * stride;
                    file.WriteByte(42);
                }
                file.Flush(flushToDisk: true);
            }
            var reader = new UnixFileRecords();
            var summary = reader.ReadSummary(path, ct);
            Assert.DoesNotContain("Main-stream extent records", summary.ToReport().ToText());
            var full = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            var extents = Assert.Single(full.Technical.Sections, section => section.Title == "Main-stream extent records");
            Assert.Contains(("Inventory", "Complete"), extents.Fields);
            Assert.NotEmpty(extents.Table!.Rows);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            int pin = OpenPath(path, 0x200000 | 0x80000); // O_PATH | O_CLOEXEC
            Assert.True(pin >= 0);
            try
            {
                File.Move(path, retired);
                Assert.Equal(0, MakeFifo(path, 0x180));
                var map = UnixFileRecords.ReadLinuxPinnedExtents(pin, summary.Identity.Value!, ct);
                Assert.Equal(FactState.Known, map.State);
                Assert.True(map.Value!.Count >= ranges);
                Assert.DoesNotContain(reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct).Technical.Sections,
                    section => section.Title == "Main-stream extent records");
            }
            finally { CloseFile(pin); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [LibraryImport("libc", EntryPoint = "mkfifo", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MakeFifo(string path, uint mode);

    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenPath(string path, int flags);

    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int CloseFile(int fd);
}
