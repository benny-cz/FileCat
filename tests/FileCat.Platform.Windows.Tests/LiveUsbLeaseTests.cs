using System.Diagnostics;
using System.Text;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed class LiveUsbLeaseTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "filecat-usb-lease-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_serial_is_exclusive_until_release_while_another_serial_can_proceed()
    {
        string serial = "fixture-serial";
        string path = LiveUsbGuard.LeasePath(_directory, serial);
        using (LiveUsbGuard.AcquireLease(_directory, serial))
        {
            Assert.Throws<IOException>(() => LiveUsbGuard.AcquireLease(_directory, serial));
            using var otherDisk = LiveUsbGuard.AcquireLease(_directory, "another-serial");
        }
        Assert.True(File.Exists(path));
        using var nextRun = LiveUsbGuard.AcquireLease(_directory, serial);
    }

    [Fact]
    public async Task A_second_process_is_refused_and_process_death_releases_the_lease()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("Windows physical-test interprocess control.");
        Directory.CreateDirectory(_directory);
        string path = LiveUsbGuard.LeasePath(_directory, "fixture-serial");
        string encodedPath = Convert.ToBase64String(Encoding.Unicode.GetBytes(path));
        // The child independently opens the same path with the same sharing rules, then holds its handle until killed.
        string command = $$"""
            $ErrorActionPreference='Stop'
            $path=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{encodedPath}}'))
            $lease=[IO.File]::Open($path,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
            [Console]::Out.WriteLine('held')
            [Console]::Out.Flush()
            [Console]::In.ReadLine() | Out-Null
            $lease.Dispose()
            """;
        var start = new ProcessStartInfo("powershell.exe")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
            start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        try
        {
            Assert.Equal("held", await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)));
            Assert.Throws<IOException>(() => LiveUsbGuard.AcquireLease(_directory, "fixture-serial"));
            child.Kill();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            // Process exit may be signaled just before Windows finishes closing its handles.
            using var afterCrash = await AcquireAfterExit();
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private async Task<FileStream> AcquireAfterExit()
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try { return LiveUsbGuard.AcquireLease(_directory, "fixture-serial"); }
            catch (IOException) when (deadline.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(50); }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
