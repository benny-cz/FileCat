using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Tests;

/// <summary>
/// Renames on Linux and macOS without replacing (review finding F-H1 of f134ba37): a new letter case on a volume that
/// ignores case, and file systems that take no rename flags (NFS, 9p, FUSE without rename2), which claim the new name with
/// a hard link instead.
/// </summary>
public sealed partial class UnixRenameTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int HardLink(string existing, string link);

    private static string[] Names(string folder) => Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToArray();

    [Fact]
    public void A_new_letter_case_takes_hold_on_a_volume_that_ignores_case()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The Linux and macOS rename.");
        // macOS's default volume ignores case; elsewhere FILECAT_TEST_CASE_INSENSITIVE_DIR names a folder on one (FAT).
        string? given = Environment.GetEnvironmentVariable("FILECAT_TEST_CASE_INSENSITIVE_DIR");
        string folder = string.IsNullOrEmpty(given) ? _dir.Path
            : Directory.CreateDirectory(Path.Combine(given, "filecat-case-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string upper = Path.Combine(folder, "photo.JPG");
            File.WriteAllText(upper, "pixels");
            Assert.SkipUnless(File.Exists(Path.Combine(folder, "photo.jpg")), "This folder's file system tells letter cases apart.");

            new UnixFileOperations().Move(upper, Path.Combine(folder, "photo.jpg"), replaceExisting: false);

            Assert.Equal(["photo.jpg"], Names(folder));
            Assert.Equal("pixels", File.ReadAllText(Path.Combine(folder, "photo.jpg")));
        }
        finally
        {
            if (folder != _dir.Path) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_rename_onto_another_link_of_the_same_file_is_refused_and_leaves_both()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The Linux and macOS rename.");
        string a = Path.Combine(_dir.Path, "a.txt"), b = Path.Combine(_dir.Path, "b.txt");
        File.WriteAllText(a, "one file");
        Assert.Equal(0, HardLink(a, b));

        var taken = Assert.ThrowsAny<IOException>(() => new UnixFileOperations().Move(a, b, replaceExisting: false));

        Assert.Equal("exists", Jobs.ErrorText.Classify(taken));
        Assert.Equal(["a.txt", "b.txt"], Names(_dir.Path));
    }

    [Fact]
    public void Without_rename_flags_a_new_name_is_claimed_by_hard_link_and_nothing_is_replaced()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The Linux and macOS rename.");
        string a = Path.Combine(_dir.Path, "a.txt"), moved = Path.Combine(_dir.Path, "moved.txt"), b = Path.Combine(_dir.Path, "b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");

        Assert.Equal(0, UnixRename.NoReplaceWithoutFlags(a, moved));
        Assert.False(File.Exists(a));
        Assert.Equal("a", File.ReadAllText(moved));

        Assert.Equal(17, UnixRename.NoReplaceWithoutFlags(moved, b));
        Assert.Equal("a", File.ReadAllText(moved));
        Assert.Equal("b", File.ReadAllText(b));

        // A folder moves, but never onto an existing one, empty or not.
        string full = Directory.CreateDirectory(Path.Combine(_dir.Path, "full")).FullName;
        File.WriteAllText(Path.Combine(full, "x.txt"), "x");
        string empty = Directory.CreateDirectory(Path.Combine(_dir.Path, "empty")).FullName;
        Assert.Equal(17, UnixRename.NoReplaceWithoutFlags(full, empty));
        Assert.Empty(Directory.GetFileSystemEntries(empty));
        Assert.Equal(0, UnixRename.NoReplaceWithoutFlags(full, Path.Combine(_dir.Path, "renamed")));
        Assert.Equal("x", File.ReadAllText(Path.Combine(_dir.Path, "renamed", "x.txt")));

        // A symbolic link moves as itself, never as what it points to.
        string link = Path.Combine(_dir.Path, "link"), linkMoved = Path.Combine(_dir.Path, "link-moved");
        File.CreateSymbolicLink(link, b);
        Assert.Equal(0, UnixRename.NoReplaceWithoutFlags(link, linkMoved));
        Assert.Null(UnixFiles.Stat(link));
        Assert.True(UnixFiles.Stat(linkMoved)?.IsLink);
        Assert.Equal(b, new FileInfo(linkMoved).LinkTarget);
        Assert.Equal("b", File.ReadAllText(b));

        Assert.Equal(2, UnixRename.NoReplaceWithoutFlags(Path.Combine(_dir.Path, "gone"), Path.Combine(_dir.Path, "anything")));
    }
}
