using FileCat.Core.Operations;

namespace FileCat.Core.Tests;

public sealed class ChecksumManifestLineLengthTests(ITestOutputHelper output)
{
    [Fact]
    public void An_impossible_path_line_is_reported_without_materializing_the_whole_line()
    {
        string content = new('x', 4 * 1024 * 1024);
        long before = GC.GetAllocatedBytesForCurrentThread();
        ChecksumManifest parsed = ChecksumManifests.Parse(Path.Combine(Path.GetTempPath(), "SHA256SUMS"), content);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        ManifestEntry row = Assert.Single(parsed.Entries);
        output.WriteLine($"MANIFEST_LONG_LINE inputChars={content.Length} allocatedBytes={allocated} budgetBytes={1024 * 1024}");
        Assert.Equal(1, row.Line);
        Assert.Contains("too long", row.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.InRange(allocated, 0, 1024 * 1024);
    }

    [Fact]
    public void A_long_comment_is_ignored_before_a_valid_checksum_line()
    {
        string content = "#" + new string('x', 4 * 1024 * 1024) + "\n" + new string('a', 64) + " *target.bin\n";
        ChecksumManifest parsed = ChecksumManifests.Parse(Path.Combine(Path.GetTempPath(), "SHA256SUMS"), content);
        ManifestEntry row = Assert.Single(parsed.Entries);
        Assert.Equal(2, row.Line);
        Assert.Null(row.Problem);
    }
}
