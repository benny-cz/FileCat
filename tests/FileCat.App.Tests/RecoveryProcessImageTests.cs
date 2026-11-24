using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;

namespace FileCat.App.Tests;

/// <summary>
/// I646: the recovery process census runs as root on Linux and reads each process's executable image to recognise a
/// renamed FileCat apphost. Linux names a deleted image "&lt;path&gt; (deleted)" (Process.MainModule), and any account
/// that can write the folder can create that name again, as a FIFO or as a link. The census reads the image the process
/// runs instead, without waiting; macOS, which only names images, opens the name without following links or waiting.
/// </summary>
public sealed class RecoveryProcessImageTests
{
    // Ample for a loaded CI host to read a small image; a census stuck on a FIFO fails here instead of hanging the run.
    private static readonly TimeSpan CensusLimit = TimeSpan.FromSeconds(10);

    // How a published apphost binds its entry assembly (SingleInstance.IsFileCatAppHost).
    private static readonly byte[] Binding = "\0FileCat.dll\0"u8.ToArray();

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenNative(string path, int flags);

    [DllImport("libc", EntryPoint = "close")]
    private static extern int CloseNative(int fd);

    public enum Planted { Fifo, LinkToFile, LinkToDevice, File }

    public enum Named { Fifo, Link, FolderLink, Regular, AppHost }

    [Theory]
    [InlineData(Planted.Fifo, true)]
    [InlineData(Planted.Fifo, false)]
    [InlineData(Planted.LinkToFile, true)]
    [InlineData(Planted.LinkToFile, false)]
    [InlineData(Planted.LinkToDevice, true)]
    [InlineData(Planted.LinkToDevice, false)]
    [InlineData(Planted.File, true)]
    [InlineData(Planted.File, false)]
    public void Linux_census_reads_a_deleted_image_itself_and_never_what_was_planted_at_its_name(Planted planted, bool fileCat)
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Linux names a deleted image after its old path."); return; }
        string folder = NewFolder();
        string image = CopyImage(folder, fileCat);
        using var process = Run(image);
        try
        {
            File.Delete(image);
            string name = image + " (deleted)";
            // The premise: the runtime names the deleted image by a path that the folder's writers control.
            using (var named = Process.GetProcessById(process.Id)) Assert.Equal(name, named.MainModule?.FileName);
            // A planted file says the opposite of the image, so reading it would change the answer.
            byte[] decoy = fileCat ? new byte[64] : Binding;
            switch (planted)
            {
                case Planted.Fifo:
                    Assert.Equal(0, MkFifo(name, 0x180));
                    break;
                case Planted.LinkToFile:
                    File.WriteAllBytes(Path.Combine(folder, "decoy"), decoy);
                    File.CreateSymbolicLink(name, Path.Combine(folder, "decoy"));
                    break;
                case Planted.LinkToDevice:
                    File.CreateSymbolicLink(name, "/dev/zero");
                    break;
                case Planted.File:
                    File.WriteAllBytes(name, decoy);
                    break;
            }
            Assert.Equal(fileCat, CensusWithin(process.Id, planted == Planted.Fifo ? name : null));
            Assert.False(process.HasExited); // A process gone before the census would have been skipped instead.
        }
        finally { Stop(process); Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Linux_census_tells_a_running_image_by_reading_it(bool fileCat)
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Linux reads images through /proc/<pid>/exe."); return; }
        string folder = NewFolder();
        using var process = Run(CopyImage(folder, fileCat));
        try
        {
            Assert.Equal(fileCat, CensusWithin(process.Id, null));
            Assert.False(process.HasExited);
        }
        finally { Stop(process); Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void Linux_reads_through_an_exited_process_are_unknown_and_the_census_skips_a_process_already_gone()
    {
        if (!OperatingSystem.IsLinux()) { Assert.Skip("Linux /proc process directories."); return; }
        string folder = NewFolder();
        using var process = Run(CopyImage(folder, fileCat: true));
        try
        {
            var listed = Process.GetProcessById(process.Id);
            using var pinned = SingleInstance.OpenLinuxProcess(process.Id);
            Assert.True(SingleInstance.LinuxImageIsFileCat(pinned, out _));
            Stop(process);
            Assert.True(process.HasExited);
            // The image is still on disk under its name, yet nothing is read for the process that is gone, and no
            // process that later takes over its ID can answer through the descriptor either.
            Assert.Null(SingleInstance.LinuxImageIsFileCat(pinned, out string? reason));
            Assert.False(string.IsNullOrEmpty(reason));
            Assert.Throws<IOException>(() => SingleInstance.ReadLinuxProcessStat(pinned));
            TestContext.Current.TestOutputHelper?.WriteLine($"Image of the exited process: {reason}");
            // A process gone before its turn in the census is skipped, as before; the census disposes the entry.
            Assert.False(SingleInstance.OtherFileCatRunning(() => [listed], inspectInventory: true));
        }
        finally { Stop(process); Directory.Delete(folder, recursive: true); }
    }

    [Theory]
    [InlineData(Named.Fifo, null)]
    [InlineData(Named.Link, null)]
    [InlineData(Named.FolderLink, null)]
    [InlineData(Named.Regular, false)]
    [InlineData(Named.AppHost, true)]
    public void A_named_image_opens_without_following_links_or_waiting_and_is_read_only_when_regular(Named kind, bool? expected)
    {
        if (OperatingSystem.IsWindows()) { Assert.Skip("Unix executable names."); return; }
        if (kind == Named.FolderLink && !OperatingSystem.IsMacOS())
        {
            Assert.Skip("Only the macOS census opens images by name; there no link on any folder of the name is followed.");
            return;
        }
        string folder = NewFolder();
        try
        {
            string appHost = Path.Combine(folder, "apphost");
            File.WriteAllBytes(appHost, Binding);
            string path = Path.Combine(folder, "image");
            switch (kind)
            {
                case Named.Fifo:
                    Assert.Equal(0, MkFifo(path, 0x180));
                    break;
                case Named.Link:
                    File.CreateSymbolicLink(path, appHost);
                    break;
                case Named.FolderLink:
                    Directory.CreateSymbolicLink(Path.Combine(folder, "linked"), folder);
                    path = Path.Combine(folder, "linked", "apphost");
                    break;
                case Named.Regular:
                    File.WriteAllBytes(path, new byte[64]);
                    break;
                case Named.AppHost:
                    path = appHost;
                    break;
            }
            Assert.Equal(expected, Within(() => SingleInstance.ImageAtPathIsFileCat(path, out _),
                $"Reading the image named as {kind}", kind == Named.Fifo ? path : null));
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void Mac_census_reads_running_images_by_the_names_Darwin_gives_them()
    {
        if (!OperatingSystem.IsMacOS()) { Assert.Skip("macOS names process images through proc_pidpath."); return; }
        // The copy is started through the temporary folder's /var link; Darwin names it by its real folders, which the
        // census opens without following any link.
        string folder = Path.Combine(Path.GetTempPath(), "filecat-i646-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var system = Run("/bin/sleep");
        Process? copy = null;
        try
        {
            copy = Run(CopyImage(folder, fileCat: false));
            Assert.False(CensusWithin(system.Id, null));
            Assert.False(CensusWithin(copy.Id, null));
            // Both still run, so the census read them rather than skipping them as gone.
            Assert.False(system.HasExited);
            Assert.False(copy.HasExited);
        }
        finally
        {
            Stop(system);
            if (copy is not null) { Stop(copy); copy.Dispose(); }
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Mac_census_does_not_wait_on_a_fifo_planted_at_a_deleted_images_name()
    {
        if (!OperatingSystem.IsMacOS()) { Assert.Skip("macOS names process images through proc_pidpath."); return; }
        string folder = NewFolder();
        string image = CopyImage(folder, fileCat: false);
        using var process = Run(image);
        try
        {
            File.Delete(image);
            Assert.Equal(0, MkFifo(image, 0x180));
            bool? result = CensusWithin(process.Id, image);
            // Darwin either no longer names the image (unknown), names the FIFO, which is not read (unknown), or names
            // the image where it still is (not FileCat). What it must not do is wait.
            TestContext.Current.TestOutputHelper?.WriteLine($"Census of the deleted image: {result?.ToString() ?? "unknown"}");
            Assert.NotEqual(true, result);
            Assert.False(process.HasExited);
        }
        finally { Stop(process); Directory.Delete(folder, recursive: true); }
    }

    private static bool? CensusWithin(int pid, string? fifo) =>
        Within(() => SingleInstance.OtherFileCatRunning(() => [Process.GetProcessById(pid)], inspectInventory: true),
            "The census", fifo);

    // On a thread of its own, since a stuck read must not hold a pool thread the test host needs.
    private static T Within<T>(Func<T> work, string what, string? fifo)
    {
        var task = Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        if (!task.Wait(CensusLimit))
        {
            // Stuck on the FIFO: a writer releases it, so the failure leaves no blocked thread behind.
            if (fifo is not null) ReleaseReader(fifo);
            Assert.Fail($"{what} did not finish within {CensusLimit.TotalSeconds:0} s.");
        }
        return task.Result;
    }

    private static void ReleaseReader(string fifo)
    {
        // Write-only and nonblocking opens a FIFO only while a reader waits (ENXIO otherwise); closing it ends that read.
        int fd = OpenNative(fifo, OperatingSystem.IsMacOS() ? 1 | 0x4 : 1 | 0x800);
        if (fd >= 0) CloseNative(fd);
    }

    // A folder with no link on its path, so that macOS can open names under it with no link followed.
    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "filecat-i646-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return UnixFiles.RealPath(folder) ?? folder;
    }

    // A copy of sleep. With the binding appended, which ELF loaders ignore, the census must call it FileCat.
    private static string CopyImage(string folder, bool fileCat)
    {
        string image = Path.Combine(folder, "i646-image");
        File.Copy(File.Exists("/usr/bin/sleep") ? "/usr/bin/sleep" : "/bin/sleep", image);
        if (fileCat)
            using (var output = new FileStream(image, FileMode.Append)) output.Write(Binding);
        File.SetUnixFileMode(image, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return image;
    }

    private static Process Run(string image)
    {
        var start = new ProcessStartInfo(image)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("120");
        for (int attempt = 0; ; attempt++)
        {
            try { return Process.Start(start)!; }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 26 && attempt < 40)
            {
                // ETXTBSY: another test's fork can briefly hold the new image's write descriptor.
                Thread.Sleep(50);
            }
        }
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill();
            process.WaitForExit(10_000);
        }
        catch (InvalidOperationException)
        {
        }
    }
}
