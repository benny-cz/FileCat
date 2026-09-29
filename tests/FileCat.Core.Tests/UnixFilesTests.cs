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

    [Fact]
    public void Links_resolve_to_where_they_lead_and_hard_links_share_an_identity()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("Linux and macOS read these through their C libraries.");
        var ops = new UnixFileOperations();
        string folder = Directory.CreateDirectory(Path.Combine(_dir.Path, "real", "inner")).FullName;
        string alias = Path.Combine(_dir.Path, "alias");
        Directory.CreateSymbolicLink(alias, folder);
        string? real = ops.GetFinalPath(folder);
        Assert.NotNull(real);
        Assert.Equal(real, ops.GetFinalPath(alias));
        Assert.Equal(real, ops.GetFinalPath(Path.Combine(alias, ".")));
        Assert.Null(ops.GetFinalPath(Path.Combine(_dir.Path, "missing")));

        string file = Path.Combine(folder, "data.txt");
        File.WriteAllText(file, "d");
        Assert.Equal(ops.GetFileIdentity(file), ops.GetFileIdentity(Path.Combine(alias, "data.txt")));
        string hard = Path.Combine(_dir.Path, "hard.txt");
        ops.CreateLink(hard, file, LinkKind.Hard, isDirectory: false);
        Assert.Equal(ops.GetFileIdentity(file), ops.GetFileIdentity(hard));
        Assert.NotEqual(ops.GetFileIdentity(file), ops.GetFileIdentity(alias));
    }

    [Fact]
    public void A_rename_never_replaces_unless_asked()
    {
        if (OperatingSystem.IsWindows()) Assert.Skip("The Linux and macOS rename.");
        var ops = new UnixFileOperations();
        string a = Path.Combine(_dir.Path, "a.txt"), b = Path.Combine(_dir.Path, "b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        var taken = Assert.ThrowsAny<IOException>(() => ops.Move(a, b, replaceExisting: false));
        Assert.Equal("exists", Jobs.ErrorText.Classify(taken));
        Assert.Equal("b", File.ReadAllText(b));
        Assert.Equal("a", File.ReadAllText(a));
        ops.Move(a, b, replaceExisting: true);
        Assert.Equal("a", File.ReadAllText(b));
        Assert.False(File.Exists(a));

        // An empty folder is not taken over by another folder's rename either (rename(2) alone would replace it).
        string one = Directory.CreateDirectory(Path.Combine(_dir.Path, "one")).FullName;
        File.WriteAllText(Path.Combine(one, "x.txt"), "x");
        string two = Directory.CreateDirectory(Path.Combine(_dir.Path, "two")).FullName;
        Assert.ThrowsAny<IOException>(() => ops.Move(one, two, replaceExisting: false));
        Assert.Empty(Directory.GetFileSystemEntries(two));
        ops.Move(one, Path.Combine(_dir.Path, "three"), replaceExisting: false);
        Assert.Equal("x", File.ReadAllText(Path.Combine(_dir.Path, "three", "x.txt")));
        Assert.Throws<FileNotFoundException>(() => ops.Move(Path.Combine(_dir.Path, "gone.txt"), Path.Combine(_dir.Path, "any.txt"), false));
    }

    [Fact]
    public void Error_numbers_are_read_as_the_platform_means_them()
    {
        Assert.Equal("exists", Jobs.ErrorText.ClassifyErrno(17));
        Assert.Equal("crossdevice", Jobs.ErrorText.ClassifyErrno(18));
        Assert.Equal("diskfull", Jobs.ErrorText.ClassifyErrno(28));
        Assert.Equal("writeprotect", Jobs.ErrorText.ClassifyErrno(30));
        // .NET on Linux and macOS reports the error number itself; a Windows-style code means the same everywhere.
        Assert.Equal(OperatingSystem.IsWindows() ? "crossdevice" : "exists", Jobs.ErrorText.Classify(new IOException("x", 17)));
        Assert.Equal("exists", Jobs.ErrorText.Classify(new IOException("x", unchecked((int)0x800700B7))));
        Assert.Equal("crossdevice", Jobs.ErrorText.Classify(new IOException("x", unchecked((int)0x80070011))));
    }
}
