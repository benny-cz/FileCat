using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>The freedesktop.org trash on Linux and the macOS trash, against private trash folders made for each test.</summary>
public sealed class UnixTrashTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [Theory]
    [InlineData("/home/ann/My Notes/plan (v2).txt", "/home/ann/My%20Notes/plan%20(v2).txt")]
    [InlineData("/data/Příliš žluťoučký.txt", "/data/P%C5%99%C3%ADli%C5%A1%20%C5%BElu%C5%A5ou%C4%8Dk%C3%BD.txt")]
    [InlineData("rel/100%#?.txt", "rel/100%25%23%3F.txt")]
    public void Trash_info_paths_are_url_escaped_keeping_slashes(string path, string escaped) =>
        Assert.Equal(escaped, UnixTrash.Escape(path));

    private UnixTrash.Location Trash(string name, string? top = null)
    {
        var root = Path.Combine(_dir.Path, name);
        return OperatingSystem.IsMacOS()
            ? new UnixTrash.Location(root, root, null, top)
            : new UnixTrash.Location(root, Path.Combine(root, "files"), Path.Combine(root, "info"), top);
    }

    [Fact]
    public void Items_go_to_a_private_trash_with_their_origin_and_come_back()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The Recycle Bin is Windows' own; this is the Linux and macOS trash.");
        var trash = Trash("Trash");
        var first = _dir.File("docs/report.txt", "one");
        var dest1 = UnixTrash.Put(first, trash);
        var second = _dir.File("docs/report.txt", "two"); // the same name again
        var dest2 = UnixTrash.Put(second, trash);
        var folder = _dir.Dir("docs/drafts");
        _dir.File("docs/drafts/a.txt", "a");
        var dest3 = UnixTrash.Put(folder, trash);

        Assert.NotEqual(dest1, dest2);
        Assert.Equal("one", File.ReadAllText(dest1));
        Assert.Equal("two", File.ReadAllText(dest2));
        Assert.True(File.Exists(Path.Combine(dest3, "a.txt")));
        Assert.False(File.Exists(first));
        Assert.False(Directory.Exists(folder));
        // Other users must not even see what you deleted.
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(trash.Root));
        if (trash.Info is { } info)
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(info));
            var text = File.ReadAllText(UnixTrash.InfoFileFor(dest2)!);
            Assert.StartsWith("[Trash Info]\nPath=" + UnixTrash.Escape(first) + "\nDeletionDate=", text);
            Assert.Equal(2, Directory.GetFiles(info, "report*.trashinfo").Length);
        }

        var ops = new PortableFileOperations();
        Assert.True(ops.TryRestoreRecycled(dest2, second, out var error), error);
        Assert.Equal("two", File.ReadAllText(second));
        if (trash.Info is not null) Assert.False(File.Exists(Path.Combine(trash.Info, Path.GetFileName(dest2) + ".trashinfo")));
        // Restoring never replaces what is there now.
        Assert.False(ops.TryRestoreRecycled(dest1, second, out error));
        Assert.Equal("two", File.ReadAllText(second));
    }

    [Fact]
    public void A_volume_trash_records_paths_relative_to_the_volume()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Volume trash folders with .trashinfo files are a Linux (freedesktop.org) feature.");
        var trash = Trash(".Trash-" + UnixPermissions.CurrentUserId, top: _dir.Path);
        var file = _dir.File("music/song.ogg", "la");
        var dest = UnixTrash.Put(file, trash);
        Assert.Contains("\nPath=music/song.ogg\n", File.ReadAllText(UnixTrash.InfoFileFor(dest)!));
    }

    [Fact]
    public void A_trash_folder_that_is_a_link_is_never_used()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The Recycle Bin is Windows' own; this is the Linux and macOS trash.");
        var elsewhere = _dir.Dir("someone-elses");
        var trash = Trash("Trash");
        Directory.CreateSymbolicLink(trash.Root, elsewhere);
        var file = _dir.File("keep.txt", "k");
        Assert.Throws<IOException>(() => UnixTrash.Put(file, trash));
        Assert.True(File.Exists(file));
        Assert.Empty(Directory.EnumerateFileSystemEntries(elsewhere));
    }

    [Fact]
    public void The_home_volume_uses_the_home_trash_without_creating_it()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The Recycle Bin is Windows' own; this is the Linux and macOS trash.");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var trash = UnixTrash.For(Path.Combine(home, "some-file-that-does-not-exist.txt"));
        Assert.NotNull(trash);
        Assert.Null(trash.TopDirectory);
        var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } x ? x : Path.Combine(home, ".local", "share");
        Assert.Equal(OperatingSystem.IsMacOS() ? Path.Combine(home, ".Trash") : Path.Combine(data, "Trash"), trash.Root);
    }
}
