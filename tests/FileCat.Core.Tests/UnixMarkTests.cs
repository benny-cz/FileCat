using FileCat.Core.FileSystem;
using FileCat.Core.Edit;
using FileCat.Core.Resources;

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

    [Fact]
    public void Linux_zone_only_internet_mark_cannot_report_success_without_an_xattr()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux origin-url xattr semantics only.");
        var ops = new UnixFileOperations();
        string file = _dir.File("zone-only.bin", "owned");
        Assert.False(ops.WriteOriginMark(file, "[ZoneTransfer]\r\nZoneId=3\r\n"));
        Assert.Null(ops.ReadOriginMark(file));
    }

    [Fact]
    public void Linux_zone_only_remote_edit_refuses_an_unmarked_private_copy()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Linux origin-url xattr semantics only.");
        var ops = new UnixFileOperations();
        var store = new EditSessionStore(Path.Combine(_dir.Path, "zone-only-sessions"), ops);
        using var source = new MemoryContentSource("remote.bin", "owned"u8.ToArray());
        var revision = source.GetRevision()!.Value;
        Assert.Throws<IOException>(() => store.CreateRemote("owned", "owned", "/remote.bin", source, revision,
            "[ZoneTransfer]\r\nZoneId=3\r\n", TestContext.Current.CancellationToken));
        Assert.Empty(store.LoadAll());
        if (Directory.Exists(store.Root)) Assert.Empty(Directory.EnumerateFileSystemEntries(store.Root));
    }

    [Fact]
    public void A_missing_source_is_not_reported_as_having_no_origin_mark()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Unix xattr semantics only.");
        var ops = new UnixFileOperations();
        Assert.Throws<IOException>(() => ops.ReadOriginMark(Path.Combine(_dir.Path, "missing.bin")));
    }

    [Fact]
    public void An_empty_origin_attribute_is_not_reported_as_an_absent_mark()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("Unix xattr semantics only.");
        var ops = new UnixFileOperations();
        string file = _dir.File("empty-origin.bin", "owned");
        string name = OperatingSystem.IsMacOS() ? UnixFileOperations.QuarantineAttribute : UnixFileOperations.OriginAttribute;
        Assert.True(Xattr.Set(file, name, string.Empty));
        Assert.Throws<IOException>(() => ops.ReadOriginMark(file));
    }

    [Fact]
    public void An_oversized_quarantine_attribute_is_refused_before_materialization()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("macOS quarantine xattr size only.");
        var ops = new UnixFileOperations();
        string file = _dir.File("large-quarantine.bin", "owned");
        if (!Xattr.Set(file, UnixFileOperations.QuarantineAttribute, new string('x', 64 * 1024 + 1)))
            Assert.Skip("This filesystem does not admit a quarantine attribute larger than 64 KiB.");
        Assert.Throws<IOException>(() => ops.ReadOriginMark(file));
    }

    [Fact]
    public void Mac_empty_quarantine_value_is_replaced_by_a_readable_mark()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("macOS quarantine semantics only.");
        var ops = new UnixFileOperations();
        string file = _dir.File("empty-quarantine-write.bin", "owned");
        Assert.True(ops.WriteOriginMark(file, "[ZoneTransfer]\r\nZoneId=3\r\nQuarantine=\r\n"));
        Assert.Contains("Quarantine=0081;", Assert.IsType<string>(ops.ReadOriginMark(file)));
    }

    [Fact]
    public void Mac_oversized_quarantine_value_is_refused_before_write()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("macOS quarantine semantics only.");
        var ops = new UnixFileOperations();
        string file = _dir.File("large-quarantine-write.bin", "owned");
        Assert.False(ops.WriteOriginMark(file, "[ZoneTransfer]\r\nZoneId=3\r\nQuarantine=" + new string('x', 64 * 1024 + 1) + "\r\n"));
        Assert.Null(ops.ReadOriginMark(file));
    }
}
