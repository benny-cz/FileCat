using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>What the POSIX hex editor relies on (ADR-05 on Linux and macOS): identity, links, and mounts.</summary>
public sealed class UnixFilesTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void A_file_its_handle_and_its_link_report_what_they_are()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Linux and macOS read these through their C libraries.");
        string file = Path.Combine(_dir.Path, "data.bin");
        File.WriteAllBytes(file, new byte[1234]);
        var entry = Assert.NotNull(UnixFiles.Stat(file));
        Assert.False(entry.IsLink);
        Assert.False(entry.IsDirectory);
        Assert.Equal(1234, entry.Size);
        Assert.NotEqual(0UL, entry.Inode);

        using (var handle = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            Assert.Equal(entry.Identity, Assert.NotNull(UnixFiles.Stat(handle)).Identity);

        string link = Path.Combine(_dir.Path, "link.bin");
        File.CreateSymbolicLink(link, file);
        var linkEntry = Assert.NotNull(UnixFiles.Stat(link));
        Assert.True(linkEntry.IsLink);
        Assert.NotEqual(entry.Identity, linkEntry.Identity);

        Assert.True(Assert.NotNull(UnixFiles.Stat(_dir.Path)).IsDirectory);
        Assert.Null(UnixFiles.Stat(Path.Combine(_dir.Path, "missing")));
        Assert.Equal(32, entry.Identity.FileId.Length); // as the journal stores it
    }

    [Fact]
    public void A_rename_keeps_the_identity_and_a_new_file_has_another()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Linux and macOS read these through their C libraries.");
        string first = Path.Combine(_dir.Path, "first.bin");
        File.WriteAllBytes(first, [1, 2, 3]);
        var before = UnixFiles.Stat(first)!.Value.Identity;
        string renamed = Path.Combine(_dir.Path, "renamed.bin");
        File.Move(first, renamed);
        Assert.Equal(before, UnixFiles.Stat(renamed)!.Value.Identity);
        File.WriteAllBytes(first, [1, 2, 3]);
        Assert.NotEqual(before, UnixFiles.Stat(first)!.Value.Identity);
        Assert.NotNull(UnixFiles.MountOf(renamed));
    }
}
