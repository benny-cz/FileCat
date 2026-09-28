using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>P9: download origins on Linux (user.xdg.origin.url) and macOS (com.apple.quarantine).</summary>
public sealed class UnixMarkTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void A_download_origin_travels_in_extended_attributes()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Extended-attribute marks are for Linux and macOS.");
        var ops = new UnixFileOperations();
        var file = _dir.File("download.bin", "x");
        Assert.Null(ops.ReadOriginMark(file));
        Assert.True(ops.WriteOriginMark(file, "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=sftp://files.example/\r\n"));
        var mark = ops.ReadOriginMark(file);
        Assert.NotNull(mark);
        if (OperatingSystem.IsMacOS()) Assert.Contains("Quarantine=0081;", mark);
        else Assert.Contains("HostUrl=sftp://files.example/", mark);

        // A mark read from one file (an archive, say) marks what is extracted from it identically.
        var extracted = _dir.File("extracted.bin", "y");
        Assert.True(ops.WriteOriginMark(extracted, mark!));
        Assert.Equal(mark, ops.ReadOriginMark(extracted));

        // Local zones are not internet downloads: nothing is marked.
        var local = _dir.File("local.bin", "z");
        Assert.True(ops.WriteOriginMark(local, "[ZoneTransfer]\r\nZoneId=1\r\n"));
        Assert.Null(ops.ReadOriginMark(local));
    }

    [Fact]
    public void Zone_text_parses_on_every_platform()
    {
        Assert.Equal((3, "https://example.com/a.zip", (string?)null), UnixFileOperations.Parse("[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://example.com/a.zip\r\n"));
        Assert.Equal((0, (string?)null, "0081;5f;Safari;"), UnixFileOperations.Parse("[ZoneTransfer]\nQuarantine=0081;5f;Safari;\n"));
    }
}
