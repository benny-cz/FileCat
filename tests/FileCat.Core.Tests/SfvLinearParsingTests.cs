using System.Diagnostics;
using FileCat.Core.Operations;

namespace FileCat.Core.Tests;

public sealed class SfvLinearParsingTests
{
    [Fact]
    public void Whitespace_heavy_invalid_line_is_bounded_and_valid_line_survives()
    {
        string path = Path.Combine(Path.GetTempPath(), "album.sfv");
        string input = "track.flac " + new string(' ', 64 * 1024 - 20) + "XXXXXXXX\ntrack.flac 0123abcd\n";
        var watch = Stopwatch.StartNew();
        var parsed = ChecksumManifests.Parse(path, input);
        watch.Stop();
        Assert.Equal(2, parsed.Entries.Count);
        Assert.Contains("not a checksum line", parsed.Entries[0].Problem);
        Assert.Equal(("track.flac", "0123abcd"), (parsed.Entries[1].Name, parsed.Entries[1].Expected));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"SFV parse took {watch.Elapsed}.");
        string collection = string.Concat(Enumerable.Repeat("track.flac " + new string(' ', 64 * 1024 - 20) + "XXXXXXXX\n", 64));
        watch.Restart();
        var many = ChecksumManifests.Parse(path, collection);
        watch.Stop();
        Assert.Equal(64, many.Entries.Count);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"4 MiB SFV parse took {watch.Elapsed}.");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Assert.Throws<OperationCanceledException>(() => ChecksumManifests.ParseCancellable(path, collection, canceled.Token));
    }
}
