using FileCat.Core.Operations;

namespace FileCat.Core.Tests;

public sealed class ChecksumManifestLineAllocationTests(ITestOutputHelper output)
{
    [Fact]
    public void Millions_of_blank_manifest_lines_do_not_allocate_a_split_array()
    {
        string content = new('\n', 4 * 1024 * 1024);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        long before = GC.GetAllocatedBytesForCurrentThread();
        ChecksumManifest parsed = ChecksumManifests.Parse(Path.Combine(Path.GetTempPath(), "SHA256SUMS"), content);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"MANIFEST_BLANK_LINES inputChars={content.Length} allocatedBytes={allocated} budgetBytes={8 * 1024 * 1024}");
        Assert.Empty(parsed.Entries);
        Assert.InRange(allocated, 0, 8 * 1024 * 1024);
    }

    [Fact]
    public void CrLf_comments_and_blank_lines_keep_their_reported_line_number()
    {
        ChecksumManifest parsed = ChecksumManifests.Parse(
            Path.Combine(Path.GetTempPath(), "SHA256SUMS"), "# comment\r\n\r\nnot a checksum\r\n");
        ManifestEntry bad = Assert.Single(parsed.Entries);
        Assert.Equal(3, bad.Line);
        Assert.NotNull(bad.Problem);
    }
}
