using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;

namespace FileCat.Core.Tests;

// Listing reviews of 12 November 2026: before any job, the Unix writer's own checks decide whether it can write a target at
// all: the folder as the job resolves it, and an existing archive by the job's own update admission. Where it cannot, a ZIP
// keeps its old route and the other formats say why; macOS's home, Desktop, Documents and Downloads folders, whose access
// list only denies deleting the folder itself, stay writable.
public sealed partial class UnixArchiveTargetProbeTests
{
    [Fact]
    public void An_ordinary_private_folder_and_a_single_link_archive_are_writable()
    {
        RequireUnix();
        using var temp = new TempDir(); string folder = Private(temp, "plain");
        Assert.Null(UnixArchiveJobBackend.TargetProblem(Path.Combine(folder, "new.tar")));
        File.WriteAllBytes(Path.Combine(folder, "old.tar"), new byte[512]);
        Assert.Null(UnixArchiveJobBackend.TargetProblem(Path.Combine(folder, "old.tar")));
    }

    [Fact]
    public void A_folder_reached_through_a_symbolic_link_is_judged_where_it_really_is()
    {
        RequireUnix();
        using var temp = new TempDir(); string folder = Private(temp, "real"), shared = Private(temp, "shared");
        File.SetUnixFileMode(shared, (UnixFileMode)0x1FF); // 0777
        string link = Path.Combine(Real(temp.Path), "linked"), sharedLink = Path.Combine(Real(temp.Path), "linked-shared");
        Directory.CreateSymbolicLink(link, folder); Directory.CreateSymbolicLink(sharedLink, shared);
        // The job resolves the folder through the link (ArchiveLocalPath), so the probe does too.
        Assert.Null(UnixArchiveJobBackend.TargetProblem(Path.Combine(link, "a.zip")));
        Assert.StartsWith("Archive writing needs a folder you own", UnixArchiveJobBackend.TargetProblem(Path.Combine(sharedLink, "a.zip")));
    }

    [Fact]
    public void An_archive_that_is_a_link_or_has_another_hard_link_is_not_writable()
    {
        RequireUnix();
        using var temp = new TempDir(); string folder = Private(temp, "files");
        string archive = Path.Combine(folder, "a.zip");
        File.WriteAllBytes(archive, new byte[64]);
        File.CreateSymbolicLink(Path.Combine(folder, "b.zip"), archive);
        Assert.Equal(UnixArchiveJobBackend.LinkedArchive, UnixArchiveJobBackend.TargetProblem(Path.Combine(folder, "b.zip")));
        Assert.Equal(0, Link(archive, Path.Combine(folder, "c.zip")));
        Assert.StartsWith("This archive has other hard links", UnixArchiveJobBackend.TargetProblem(archive));
    }

    // Safari, Chrome and Firefox tag every macOS download (com.apple.metadata:kMDItemWhereFroms), and Linux tools add
    // user attributes: the job's update admission refuses what it cannot reproduce, so the probe says so first.
    [Fact]
    public void An_archive_with_extended_attributes_FileCat_cannot_keep_is_not_writable()
    {
        RequireUnix();
        using var temp = new TempDir(); string folder = Private(temp, "downloads");
        string archive = Path.Combine(folder, "a.zip");
        File.WriteAllBytes(archive, new byte[64]);
        Assert.Null(UnixArchiveJobBackend.TargetProblem(archive));
        byte[] value = "https://example.invalid/a.zip"u8.ToArray();
        int set = OperatingSystem.IsMacOS()
            ? MacSetXattr(archive, "com.apple.metadata:kMDItemWhereFroms", value, (nuint)value.Length, 0, 0)
            : LinuxSetXattr(archive, "user.filecat.test", value, (nuint)value.Length, 0);
        if (set != 0) Assert.Skip("This file system takes no user extended attributes (errno " + Marshal.GetLastPInvokeError() + ").");
        Assert.StartsWith("This archive carries extended attributes", UnixArchiveJobBackend.TargetProblem(archive));
    }

    [Fact]
    public void A_folder_other_users_can_change_is_not_writable()
    {
        RequireUnix();
        using var temp = new TempDir(); string folder = Private(temp, "shared");
        File.SetUnixFileMode(folder, (UnixFileMode)0x1FF); // 0777
        Assert.StartsWith("Archive writing needs a folder you own", UnixArchiveJobBackend.TargetProblem(Path.Combine(folder, "a.zip")));
    }

    [Fact]
    public void On_macOS_a_folder_whose_access_list_only_denies_is_writable_and_one_that_allows_or_inherits_is_not()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("macOS access lists.");
        RequireUnix();
        using var temp = new TempDir();
        string denies = Private(temp, "denies");
        Chmod("+a", "group:everyone deny delete", denies); // As on the home folder, Desktop, Documents and Downloads.
        Assert.Null(UnixArchiveJobBackend.TargetProblem(Path.Combine(denies, "a.zip")));
        string allows = Private(temp, "allows");
        Chmod("+a", "group:everyone allow list", allows);
        Assert.StartsWith("Archive writing needs a folder whose access list", UnixArchiveJobBackend.TargetProblem(Path.Combine(allows, "a.zip")));
        string blocks = Private(temp, "blocks"); // A deny of adding files would stop the exchange part way.
        Chmod("+a", "group:everyone deny add_file", blocks);
        Assert.StartsWith("Archive writing needs a folder whose access list", UnixArchiveJobBackend.TargetProblem(Path.Combine(blocks, "a.zip")));
        string inherits = Private(temp, "inherits");
        Chmod("+a", "group:everyone deny delete,file_inherit,directory_inherit", inherits);
        Assert.StartsWith("Archive writing needs a folder whose access list", UnixArchiveJobBackend.TargetProblem(Path.Combine(inherits, "a.zip")));
    }

    private static void RequireUnix()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) Assert.Skip("The Unix writer.");
        if (Environment.IsPrivilegedProcess) Assert.Skip("The writer refuses root; run as an ordinary user.");
        // The writer runs only on its qualified volumes (ext4, APFS); a temporary folder elsewhere (tmpfs) proves nothing.
        using var temp = new TempDir();
        try { using var held = UnixArchiveDirectory.Open(Real(temp.Path)); }
        catch (Exception error) when (error is IOException or NotSupportedException) { Assert.Skip("The temporary folder is not on a volume the writer runs on."); }
    }

    // A folder only its owner can change, at its real path (macOS reaches the temporary folder through /var, a link).
    private static string Private(TempDir temp, string name)
    {
        string folder = Directory.CreateDirectory(Path.Combine(Real(temp.Path), name)).FullName;
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return folder;
    }

    private static string Real(string path)
    {
        nint resolved = RealPath(path, 0);
        try { return resolved == 0 ? path : Marshal.PtrToStringUTF8(resolved)!; }
        finally { if (resolved != 0) Free(resolved); }
    }

    private static void Chmod(params string[] arguments)
    {
        var start = new ProcessStartInfo("/bin/chmod") { RedirectStandardError = true, UseShellExecute = false };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
    }

    [LibraryImport("libc", EntryPoint = "realpath", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RealPath(string path, nint resolved);
    [LibraryImport("libc", EntryPoint = "free")]
    private static partial void Free(nint pointer);
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Link(string existing, string created);
    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int LinuxSetXattr(string path, string name, byte[] value, nuint size, int flags);
    [LibraryImport("libc", EntryPoint = "setxattr", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int MacSetXattr(string path, string name, byte[] value, nuint size, uint position, int options);
}
