using System.Text;
using FileCat.Core.Records;

namespace FileCat.Platform.Windows.Tests;

/// <summary>
/// F-EXPORT omission sentinels on NTFS. Distinct sentinel bytes are placed in:
/// <list type="bullet">
/// <item>a small resident main stream;</item>
/// <item>the stale tail of that MFT record after a longer value was shortened;</item>
/// <item>a named stream value;</item>
/// <item>an EA value.</item>
/// </list>
/// The summary and the metadata-only full report contain none of them. Every copy and save variant is built from that
/// report text, so they carry none either.
/// </summary>
public sealed partial class WindowsHiddenDataTests
{
    [Fact]
    public void Metadata_reports_carry_no_resident_slack_stream_or_EA_value_sentinel()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "NTFS sentinels.");
        string root = Directory.CreateTempSubdirectory("fc-omission-").FullName;
        try
        {
            Assert.SkipUnless(new DriveInfo(root).DriveFormat == "NTFS", "The sentinel fixture needs NTFS.");
            string file = Path.Join(root, "carrier.bin");
            // A longer resident value first, then a shorter one: the MFT record keeps the old bytes beyond the new end.
            File.WriteAllText(file, "FCSENTINELSLACK" + new string('s', 400));
            File.WriteAllText(file, "FCSENTINELRESIDENT");
            File.WriteAllText(file + ":carrier-stream", "FCSENTINELSTREAMVALUE");
            WriteEa(file, "CARRIER.EA", Encoding.ASCII.GetBytes("FCSENTINELEAVALUE"));

            var reader = new WindowsFileRecords();
            var ct = TestContext.Current.CancellationToken;
            string summary = reader.ReadSummary(file, ct).ToReport().ToText();
            var full = reader.ReadFull(file, FileDetailsPolicy.MetadataOnly, ct);
            Assert.Equal(ReportContentKind.MetadataOnly, full.ContentKind);
            string text = full.ToText();
            Assert.DoesNotContain("FCSENTINEL", summary);
            Assert.DoesNotContain("FCSENTINEL", text);
            Assert.DoesNotContain("ssssssss", text); // nor the slack filler
            // The stored value never reaches the report, while the file's own bytes are unchanged.
            Assert.Equal("FCSENTINELRESIDENT", File.ReadAllText(file));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
