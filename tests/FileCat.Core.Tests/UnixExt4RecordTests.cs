using System.Globalization;
using System.Text.RegularExpressions;
using FileCat.Core.FileSystem;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using FileCat.Tests;

namespace FileCat.Core.Tests;

/// <summary>
/// D-56 on Linux: an item's inode as an ext4 volume's inode table keeps it, read as root from a mounted volume (CI
/// loop-mounts one) and checked against debugfs; without root, the record says what root would add.
/// </summary>
public sealed class UnixExt4RecordTests
{
    private static InspectionSection InodeSection(string path) =>
        new UnixFileRecords().Read(path, TestContext.Current.CancellationToken).Sections.Single(s => s.Title.StartsWith("Inode on disk (", StringComparison.Ordinal));

    private static string Field(InspectionSection section, string name) => section.Fields.Single(f => f.Name == name).Value;

    private static string[] Row(InspectionSection section, string name) => section.Table!.Rows.Single(r => r[0] == name);

    [Fact]
    public void As_root_an_item_on_ext4_shows_its_inode_with_a_matching_checksum_and_what_debugfs_says()
    {
        string? mount = Environment.GetEnvironmentVariable("FILECAT_TEST_EXT4_MOUNT");
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(mount))
        {
            Assert.Skip("Set FILECAT_TEST_EXT4_MOUNT to an ext4 mount, and run as root.");
            return;
        }
        Assert.True(Environment.IsPrivilegedProcess, "FILECAT_TEST_EXT4_MOUNT is set, but the test does not run as root.");
        string debugfs = Ext4InodeTests.Tool("debugfs") ?? throw new InvalidOperationException("debugfs is not installed.");
        string findmnt = Ext4InodeTests.Tool("findmnt") ?? throw new InvalidOperationException("findmnt is not installed.");
        string device = Ext4InodeTests.Run(findmnt, "-n", "-o", "SOURCE", "--target", mount).Output.Trim();
        string folder = Path.Combine(mount, "inode test " + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            // Just written, through a stream that allocates nothing ahead (File.WriteAllText preallocates): delayed
            // allocation keeps the new size and blocks in memory, which the record says is not damage. Writeback may
            // still have come first on a busy system, and then the table already agrees.
            string file = Path.Combine(folder, "written.txt");
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write)) stream.Write(new byte[5000]);
            var fresh = InodeSection(file);
            Assert.StartsWith(device + ": block group ", Field(fresh, "Read from"), StringComparison.Ordinal);
            Assert.Matches(@"^0x[0-9A-F]{8} matches \(crc32c", Field(fresh, "Inode checksum"));
            Assert.StartsWith("the superblock's matches; block group ", Field(fresh, "Other checksums"), StringComparison.Ordinal);
            Assert.Contains(fresh.Lines, l => l.Contains("page cache (not O_DIRECT)", StringComparison.Ordinal));
            Assert.DoesNotContain(fresh.Lines, l => l.StartsWith("⚠", StringComparison.Ordinal));
            bool behind = Row(fresh, "Size")[2] != "the same";
            if (behind)
            {
                Assert.Equal(5000.ToString("N0", CultureInfo.CurrentCulture), Row(fresh, "Size")[2]);
                Assert.Matches(@"• The inode table's size( and blocks)? (is|are) behind the kernel's", string.Join(" ", fresh.Lines));
                Assert.Contains("Not yet written, not damage.", string.Join(" ", fresh.Lines), StringComparison.Ordinal);
            }
            else Assert.All(fresh.Table!.Rows, r => Assert.Equal("the same", r[2]));
            TestContext.Current.TestOutputHelper?.WriteLine($"Right after the write the inode table's size was {(behind ? "behind the kernel's" : "already the kernel's")}:");
            TestContext.Current.TestOutputHelper?.WriteLine(new InspectionReport(file, [fresh], []).ToText());

            // Changed again (so that i_version moves) and synced: the table holds all of it, and debugfs reads the same.
            File.AppendAllText(file, "more");
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.ReadWrite)) stream.Flush(flushToDisk: true);
            foreach (string path in new[] { file, folder, mount })
            {
                var section = InodeSection(path);
                TestContext.Current.TestOutputHelper?.WriteLine(new InspectionReport(path, [section], []).ToText());
                Assert.All(section.Table!.Rows, r => Assert.True(r[2] == "the same", $"{path}: {r[0]} is {r[1]} in the table, {r[2]} in the kernel"));
                ulong inode = UnixFiles.Stat(path)!.Value.Inode;
                var (_, stat) = Ext4InodeTests.Run(debugfs, "-R", $"stat <{inode}>", device);
                TestContext.Current.TestOutputHelper?.WriteLine("debugfs: " + string.Join(" | ", stat.Split('\n').Where(l => l.StartsWith("Inode", StringComparison.Ordinal) || l.StartsWith("Generation", StringComparison.Ordinal) || l.Contains("Size: ", StringComparison.Ordinal))));
                var expected = Ext4InodeTests.ParseStat(stat);
                var checksum = Regex.Match(Field(section, "Inode checksum"), "^0x([0-9A-F]{8}) matches");
                Assert.True(checksum.Success, Field(section, "Inode checksum"));
                Assert.Equal(expected.Checksum, uint.Parse(checksum.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                Assert.Equal(expected.Generation.ToString("N0", CultureInfo.CurrentCulture), Row(section, "Generation")[1]);
                Assert.True(expected.VersionHigh);
                Assert.EndsWith($"(0x{expected.Version >> 32:X8}:{expected.Version & 0xFFFF_FFFF:X8})", Field(section, "i_version"), StringComparison.Ordinal);
                Assert.Equal(expected.Size.ToString("N0", CultureInfo.CurrentCulture), Row(section, "Size")[1]);
            }
        }
        finally { TestCleanup.DeleteTree(folder); }
    }

    [Fact]
    public void Without_root_an_item_on_ext4_says_what_root_would_add()
    {
        if (!OperatingSystem.IsLinux() || Environment.IsPrivilegedProcess)
        {
            Assert.Skip("Linux, without root.");
            return;
        }
        string dir = Path.Combine(Path.GetTempPath(), "filecat-ext4-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "note.txt");
            File.WriteAllText(file, "x");
            var report = new UnixFileRecords().Read(file, TestContext.Current.CancellationToken);
            if (!Regex.IsMatch(report.Format, "· ext[234]$"))
            {
                Assert.Skip($"The temporary folder is not on ext2, ext3, or ext4 ({report.Format}).");
                return;
            }
            var section = report.Sections.Single(s => s.Title.StartsWith("Inode on disk (", StringComparison.Ordinal));
            Assert.Empty(section.Fields);
            Assert.Null(section.Table);
            Assert.EndsWith("which needs root.", string.Join(" ", section.Lines), StringComparison.Ordinal);
        }
        finally { TestCleanup.DeleteTree(dir); }
    }
}
