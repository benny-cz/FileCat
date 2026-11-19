using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Fact]
    public void Zfs_mount_records_preserve_escaped_spaces_and_require_an_unambiguous_case_mode()
    {
        Assert.Equal(new UnixFileRecords.LinuxZfsMount("pool/a b", "Case insensitive"),
            UnixFileRecords.ParseLinuxZfsMount(@"pool/a\040b", "rw,xattr,caseinsensitive"));
        Assert.Equal(new UnixFileRecords.LinuxZfsMount("pool/outer", "Mixed"),
            UnixFileRecords.ParseLinuxZfsMount("pool/outer", "rw,casemixed"));
        Assert.Equal(new UnixFileRecords.LinuxZfsMount(null, null),
            UnixFileRecords.ParseLinuxZfsMount(@"pool/a\012b", "ro,casesensitive,caseinsensitive"));
    }

    [Theory]
    [InlineData("valid", FactState.Known)]
    [InlineData("short", FactState.Failed)]
    [InlineData("different", FactState.Changed)]
    [InlineData("unit", FactState.Failed)]
    [InlineData("free", FactState.Failed)]
    [InlineData("available", FactState.Failed)]
    public void Zfs_accounting_validates_the_returned_filesystem_unit_and_counters(string fixture, FactState expected)
    {
        var fields = new byte[80];
        BinaryPrimitives.WriteUInt64LittleEndian(fields, fixture == "different" ? 0xef53UL : 0x2fc12fc1UL);
        BinaryPrimitives.WriteUInt64LittleEndian(fields.AsSpan(16), ulong.MaxValue);
        BinaryPrimitives.WriteUInt64LittleEndian(fields.AsSpan(24), fixture == "available" ? 5UL : ulong.MaxValue - 1);
        BinaryPrimitives.WriteUInt64LittleEndian(fields.AsSpan(32), 6);
        BinaryPrimitives.WriteUInt64LittleEndian(fields.AsSpan(72), fixture == "unit" ? 0UL : 4096UL);
        if (fixture == "free") BinaryPrimitives.WriteUInt64LittleEndian(fields.AsSpan(16), 5);

        var space = UnixFileRecords.ParseLinuxZfsSpace(fixture == "short" ? fields.AsSpan(0, 79) : fields);
        Assert.Equal(expected, space.State);
        Assert.Equal(FactScope.Dataset, space.Scope);
        if (expected != FactState.Known) Assert.Null(space.Value);
        else Assert.Equal(new UnixFileRecords.LinuxZfsSpace((UInt128)ulong.MaxValue * 4096,
            (UInt128)(ulong.MaxValue - 1) * 4096, 6 * 4096), space.Value);
    }

    [Fact]
    public void Linux_full_report_uses_the_actual_nested_ZFS_mount_and_held_accounting()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Requires Linux x64 OpenZFS.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_ZFS_ROOT");
        string? pool = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_ZFS_POOL");
        if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(pool)) Assert.Skip("Requires a prepared ZFS pool with nested filesystems.");

        const string title = "Location · ZFS dataset";
        var reader = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        string outer = pool + "/outer", child = outer + "/child space";
        Assert.False(File.Exists(Path.Join(root, "outer", "ITEM.BIN")));
        Assert.True(File.Exists(Path.Join(root, "outer", "child", "ITEM.BIN")));
        var expectedSpace = File.ReadLines(Path.Join(root, "space.tsv")).Select(line => line.Split('\t'))
            .ToDictionary(fields => fields[0], fields => fields.Skip(1).Select(value => UInt128.Parse(value, CultureInfo.InvariantCulture)).ToArray());
        foreach (var (leaf, expectedSource) in new[]
        {
            ("outer", outer), ("outer/item.bin", outer), ("outer/child", child),
            ("outer/child/item.bin", child), ("alias/item.bin", child),
        })
        {
            string path = Path.Join(root, leaf);
            var summary = reader.ReadSummary(path, ct);
            Assert.Equal("zfs", summary.FileSystem.Value);
            Assert.DoesNotContain(title, summary.ToReport().ToText());
            var full = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.Equal(summary.Identity.Value, full.Summary.Identity.Value);
            var dataset = Assert.Single(full.Technical.Sections, section => section.Title == title);
            Assert.Contains(("Mounted source", expectedSource), dataset.Fields);
            Assert.Contains(("Mounted case mode", expectedSource == outer ? "Case sensitive" : "Case insensitive"), dataset.Fields);
            Assert.Contains(("Accounting source", "fstatfs on the held ZFS object"), dataset.Fields);
            Assert.Contains(("Dataset compression policy", "Not inspected"), dataset.Fields);
            Assert.Contains(("Dataset encryption and key status", "Not inspected"), dataset.Fields);
            var total = NumberOfBytes("Filesystem total");
            var free = NumberOfBytes("Filesystem free");
            var available = NumberOfBytes("Available to this user");
            Assert.Equal(expectedSpace[leaf], new[] { total, free, available });

            UInt128 NumberOfBytes(string field)
            {
                string text = Assert.Single(dataset.Fields, row => row.Name == field).Value;
                Assert.EndsWith(" bytes", text);
                return UInt128.Parse(text.AsSpan(0, text.Length - 6), NumberStyles.AllowThousands, CultureInfo.CurrentCulture);
            }
        }

        string unreadable = Path.Join(root, "outer", "child", "item.bin");
        Assert.Throws<UnauthorizedAccessException>(() => { using var ignored = File.OpenRead(unreadable); });
        var link = reader.ReadFull(Path.Join(root, "outer", "leaf-link"), FileDetailsPolicy.MetadataOnly, ct);
        Assert.Equal(FileObjectKind.SymbolicLink, link.Summary.Kind);
        Assert.DoesNotContain(link.Technical.Sections, section => section.Title == title);
    }
}
