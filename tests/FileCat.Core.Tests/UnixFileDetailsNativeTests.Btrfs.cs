using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

public sealed partial class UnixFileDetailsNativeTests
{
    [Theory]
    [InlineData("zlib", "zlib")]
    [InlineData("lzo", "lzo")]
    [InlineData("zstd", "zstd")]
    [InlineData("no", "Disabled by the inode property")]
    [InlineData("none", "Disabled by the inode property")]
    [InlineData("zstd\0", null)]
    [InlineData("zstd:5", null)]
    [InlineData("unsupported-value", null)]
    public void Btrfs_compression_policy_only_reports_qualified_exact_values(string stored, string? expected)
    {
        var policy = UnixFileRecords.ParseBtrfsCompression(Encoding.UTF8.GetBytes(stored), FactScope.DirectoryPolicy);
        Assert.Equal(expected is null ? FactState.Unsupported : FactState.Known, policy.State);
        Assert.Equal(FactScope.DirectoryPolicy, policy.Scope);
        Assert.Equal(expected, policy.Value);
    }

    [Fact]
    public void Linux_full_report_reads_Btrfs_policy_and_subvolume_from_the_held_object()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64,
            "Requires Linux x64 Btrfs.");
        string? root = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_BTRFS_ROOT");
        if (string.IsNullOrWhiteSpace(root)) Assert.Skip("Requires a disposable Btrfs mount with a prepared nested subvolume.");
        string? nestedId = Environment.GetEnvironmentVariable("FILECAT_TEST_LINUX_BTRFS_SUBVOLUME_ID");
        Assert.True(ulong.TryParse(nestedId, NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id > 5);

        var reader = new UnixFileRecords();
        var ct = TestContext.Current.CancellationToken;
        foreach (var test in new[]
        {
            (Path: "default.dat", Subvolume: "5", Policy: "No explicit inode property", Scope: "Regular file inode policy"),
            (Path: "nested", Subvolume: nestedId!, Policy: "No explicit inode property", Scope: "Directory policy for new entries"),
            (Path: "nested/policy", Subvolume: nestedId!, Policy: "zstd", Scope: "Directory policy for new entries"),
            (Path: "nested/policy/covered.dat", Subvolume: nestedId!, Policy: "zstd", Scope: "Regular file inode policy"),
            (Path: "nested/lzo.dat", Subvolume: nestedId!, Policy: "lzo", Scope: "Regular file inode policy"),
            (Path: "nested/zlib.dat", Subvolume: nestedId!, Policy: "zlib", Scope: "Regular file inode policy"),
            (Path: "nested/none.dat", Subvolume: nestedId!, Policy: "Disabled by the inode property", Scope: "Regular file inode policy"),
            (Path: "nested/denied.dat", Subvolume: "Denied", Policy: "Denied", Scope: "Regular file inode policy"),
        })
        {
            string path = Path.Join(root, test.Path);
            var summary = reader.ReadSummary(path, ct);
            Assert.Equal(FactState.Known, summary.FileSystem.State);
            Assert.Equal("btrfs", summary.FileSystem.Value);
            Assert.DoesNotContain("Btrfs properties", summary.ToReport().ToText());
            var full = reader.ReadFull(path, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(summary.Identity.Value, full.Summary.Identity.Value);
            var section = Assert.Single(full.Technical.Sections, s => s.Title == "Btrfs properties");
            Assert.Contains(("Containing subvolume ID", test.Subvolume), section.Fields);
            Assert.Contains(("Compression policy", test.Policy), section.Fields);
            Assert.Contains(("Policy scope", test.Scope), section.Fields);
            Assert.Contains(section.Lines, line => line.Contains("Existing extent compression", StringComparison.Ordinal));
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            Assert.DoesNotContain("btrfs-payload-sentinel", full.ToText());
        }

        foreach (var (leaf, kind) in new[] { ("link", FileObjectKind.SymbolicLink), ("pipe", FileObjectKind.Fifo) })
        {
            var full = reader.ReadFull(Path.Join(root, "nested", leaf), FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(kind, full.Summary.Kind);
            Assert.Equal("btrfs", full.Summary.FileSystem.Value);
            Assert.DoesNotContain(full.Technical.Sections, section => section.Title == "Btrfs properties");
        }
    }
}
