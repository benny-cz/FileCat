using System.Buffers.Binary;
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

    [Theory]
    [InlineData(0, 12, FactState.Known)]
    [InlineData(2, 24, FactState.Known)]
    [InlineData(2, 12, FactState.Failed)]
    [InlineData(2, 25, FactState.Failed)]
    [InlineData(3, 24, FactState.Unsupported)]
    public void Linux_fscrypt_policy_checks_returned_version_and_length(byte version, int length, FactState expected)
    {
        var reply = new byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(reply, (ulong)length);
        reply[8] = version;
        reply[9] = 1;
        reply[10] = 4;
        reply[11] = 3;
        var policy = UnixFileRecords.ParseLinuxFscryptPolicy(reply);
        Assert.Equal(expected, policy.State);
        if (expected == FactState.Known)
            Assert.Equal(new UnixFileRecords.LinuxFscryptPolicy(version == 0 ? 1 : 2, 1, 4, 3), policy.Value);
        else Assert.Null(policy.Value);
    }

    [Fact]
    public void Linux_full_report_distinguishes_real_ext4_encryption_policies_without_showing_key_or_payload()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Requires a qualified Linux x64 ext4 fixture.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_FSCRYPT_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Requires the disposable ext4 directory with prepared v1 and v2 policies.");
        var reader = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        foreach (var (leaf, version, scope) in new[]
        {
            ("v1", "1", "Directory policy"), ("v2", "2", "Directory policy"), ("v2/covered.dat", "2", "Regular file"),
        })
        {
            string path = Path.Join(root, leaf);
            var summary = reader.ReadSummary(path, ct);
            Assert.Equal("ext4", summary.FileSystem.Value);
            Assert.Equal(FactState.Known, summary.FileEncryption.State);
            Assert.True(summary.FileEncryption.Value);
            Assert.DoesNotContain("fscrypt policy", summary.ToReport().ToText());
            var full = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(summary.Identity.Value, full.Summary.Identity.Value);
            var policy = Assert.Single(full.Technical.Sections, section => section.Title == "fscrypt policy");
            Assert.Contains(("Status", "Policy present"), policy.Fields);
            Assert.Contains(("Scope", scope), policy.Fields);
            Assert.Contains(("Policy version", version), policy.Fields);
            Assert.Contains(("Content mode", "AES-256-XTS"), policy.Fields);
            Assert.Contains(("Filename mode", "AES-256-CTS"), policy.Fields);
            Assert.Contains(policy.Lines, line => line.StartsWith("Key availability", StringComparison.Ordinal));
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.DoesNotContain("fscrypt-payload-sentinel", full.ToText());
        }

        var clear = reader.ReadFull(Path.Join(root, "plain"), FileDetailsPolicy.MetadataOnly, ct);
        var absent = Assert.Single(clear.Technical.Sections, section => section.Title == "fscrypt policy");
        Assert.Contains(("Status", "No policy on this object"), absent.Fields);
        Assert.DoesNotContain(absent.Fields, field => field.Name == "Policy version");
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
            Assert.DoesNotContain(full.Technical.Sections, section => section.Title == "fscrypt policy");
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
