using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Records;

namespace FileCat.Core.Tests;

/// <summary>
/// F-EXPORT omission sentinels on ext4 with inline data. A small file whose bytes live in its inode, and an xattr value,
/// each carry a distinct sentinel. The summary and the metadata-only full report contain neither, so neither does any copy
/// or save variant built from that text. The run needs an ext4 volume made with <c>-O inline_data</c>.
/// </summary>
public sealed partial class UnixFileDetailsNativeTests
{
    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int SetXattr(string path, string name, byte[] value, nint size, int flags);

    [Fact]
    public void Metadata_reports_carry_no_inline_data_or_xattr_value_sentinel_on_ext4()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "ext4 inline data is a Linux fixture.");
        string? volume = Environment.GetEnvironmentVariable("FILECAT_TEST_EXT4_INLINE_ROOT");
        if (string.IsNullOrWhiteSpace(volume)) Assert.Skip("Set FILECAT_TEST_EXT4_INLINE_ROOT to a writable folder on an ext4 volume made with -O inline_data.");
        string root = Directory.CreateDirectory(Path.Join(volume, "fc-omission-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string file = Path.Join(root, "carrier.bin");
            File.WriteAllText(file, "FCSENTINELINLINE"); // small enough for the inode on an inline_data volume
            byte[] xattr = Encoding.ASCII.GetBytes("FCSENTINELXATTRVALUE");
            Assert.True(SetXattr(file, "user.carrier", xattr, xattr.Length, 0) == 0, $"setxattr: {Marshal.GetLastPInvokeError()}");

            var reader = new UnixFileRecords();
            var ct = TestContext.Current.CancellationToken;
            string summary = reader.ReadSummary(file, ct).ToReport().ToText();
            var full = reader.ReadFull(file, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            string text = full.ToText();
            TestContext.Current.TestOutputHelper?.WriteLine(text);
            Assert.DoesNotContain("FCSENTINEL", summary);
            Assert.DoesNotContain("FCSENTINEL", text);
            Assert.Equal("FCSENTINELINLINE", File.ReadAllText(file));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
