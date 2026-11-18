using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.Archives;
using FileCat.Core.State;
using FileCat.Platform.Windows.Archives;

namespace FileCat.Platform.Windows.Tests;

// Listing review of 12 November 2026: before any job, the Windows writer's own checks decide whether it can write a
// target at all (an unelevated process, a folder held without following links on local NTFS, a regular single-link
// archive). Where it cannot, a ZIP keeps its old route and the other formats say why. A OneDrive file is a reparse point
// like a link; there is no OneDrive on the test machines, so links and hard links stand in for it.
public sealed partial class WindowsArchiveTargetProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "filecat-probe-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _junctions = [];

    public WindowsArchiveTargetProbeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (string junction in _junctions) if (Directory.Exists(junction)) Directory.Delete(junction);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void An_elevated_FileCat_is_told_to_run_without_administrator_rights()
    {
        if (!OperatingSystem.IsWindows() || !WindowsTokenAuthority.IsHighIntegrity) Assert.Skip("Runs only in an elevated process.");
        Assert.Equal(WindowsArchiveJobBackend.Elevated, WindowsArchiveJobBackend.TargetProblem(Path.Combine(_root, "a.zip")));
    }

    [Fact]
    public void An_ordinary_local_folder_is_writable_and_a_junction_or_another_hard_link_is_not()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows.");
        if (WindowsTokenAuthority.IsHighIntegrity) Assert.Skip("The writer refuses an elevated process; run unelevated.");
        if (new DriveInfo(Path.GetPathRoot(_root)!).DriveFormat != "NTFS") Assert.Skip("The temporary folder is not on NTFS.");
        Assert.Null(WindowsArchiveJobBackend.TargetProblem(Path.Combine(_root, "new.zip")));
        string archive = Path.Combine(_root, "a.zip");
        File.WriteAllBytes(archive, new byte[22]);
        // An existing archive is replaced in an NTFS transaction, which some volumes do not support (this host's own system
        // drive on a recent Windows build): there it is read-only, before anything is rebuilt.
        string? existing = SupportsTransactions(_root) ? null : WindowsArchiveJobBackend.NoTransactions;
        Assert.Equal(existing, WindowsArchiveJobBackend.TargetProblem(archive));
        using (File.Open(archive, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) // Another program writing it is not held up.
            Assert.Equal(existing, WindowsArchiveJobBackend.TargetProblem(archive));

        string real = Directory.CreateDirectory(Path.Combine(_root, "real")).FullName, junction = Path.Combine(_root, "junction");
        Junction(junction, real);
        Assert.Equal(WindowsArchiveJobBackend.LinkedFolder, WindowsArchiveJobBackend.TargetProblem(Path.Combine(junction, "a.zip")));

        Assert.True(CreateHardLink(Path.Combine(_root, "b.zip"), archive, 0), "CreateHardLink failed.");
        Assert.Equal(ArchiveWriteMessages.HardLinkedText(archive), WindowsArchiveJobBackend.TargetProblem(archive));
    }

    [Fact]
    public void A_junction_is_refused_and_an_ordinary_folder_is_allowed()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows.");
        string real = Directory.CreateDirectory(Path.Combine(_root, "moved")).FullName, junction = Path.Combine(_root, "profile-folder");
        Junction(junction, real);
        Assert.Equal(WindowsArchiveJobBackend.LinkedFolder, WindowsArchiveJobBackend.FolderProblem(Path.Combine(junction, "inside")));
        Assert.Null(WindowsArchiveJobBackend.FolderProblem(real));
    }

    // A junction, made as any user can (mklink /J needs no privilege).
    private void Junction(string link, string target)
    {
        var start = new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "cmd.exe")) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        foreach (string argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), "mklink timed out.");
        Assert.Equal(0, process.ExitCode);
        _junctions.Add(link);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateHardLinkW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CreateHardLink(string created, string existing, nint security);

    private static bool SupportsTransactions(string path)
    {
        Assert.True(GetVolumeInformation(Path.GetPathRoot(path)!, 0, 0, 0, 0, out uint flags, 0, 0), "GetVolumeInformation failed.");
        return (flags & 0x0020_0000 /* FILE_SUPPORTS_TRANSACTIONS */) != 0;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformation(string root, nint name, uint nameSize, nint serial, nint maximumComponent,
        out uint flags, nint fileSystemName, uint fileSystemNameSize);
}
