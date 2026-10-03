using System.Diagnostics;
using System.Text;
using FileCat.Tests;

namespace FileCat.Platform.Windows.Tests;

public sealed class LiveUsbLeaseTests(ITestOutputHelper output) : IDisposable
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
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(_directory);
        string path = LiveUsbGuard.LeasePath(_directory, "fixture-serial");
        string encodedPath = Convert.ToBase64String(Encoding.Unicode.GetBytes(path));
        string readyPath = path + ".ready";
        // The child independently opens the same path with the same sharing rules, then holds its handle until killed.
        string command = $$"""
            $ErrorActionPreference='Stop'
            $path=[Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{encodedPath}}'))
            $lease=[IO.File]::Open($path,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
            [IO.File]::WriteAllText($path+'.starting','held')
            [IO.File]::Move($path+'.starting',$path+'.ready')
            [Console]::In.ReadLine() | Out-Null
            $lease.Dispose()
            """;
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardInput = true };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) })
            start.ArgumentList.Add(arg);
        using var child = Process.Start(start)!;
        // Keep draining through cancellation/cleanup so a failed helper's diagnostic is still retained.
        Task<string> errors = child.StandardError.ReadToEndAsync(CancellationToken.None);
        var startup = Stopwatch.StartNew();
        try
        {
            // Readiness is independent of PowerShell's redirected console. A loaded CI runner may take longer to
            // start the shell; the lease assertions only begin after the child has acquired its exclusive handle.
            while (!File.Exists(readyPath))
            {
                if (child.HasExited)
                    Assert.Fail($"Lease helper exited with code {child.ExitCode} before readiness: {await errors}");
                if (startup.Elapsed >= TimeSpan.FromSeconds(60))
                    throw new TimeoutException($"Lease helper PID {child.Id} did not report readiness within {startup.Elapsed}.");
                await Task.Delay(25, ct);
            }
            Assert.Equal("held", File.ReadAllText(readyPath));
            Assert.False(child.HasExited, "Lease helper exited before the contention check.");
            output.WriteLine($"Lease helper PID {child.Id} acquired its handle after {startup.Elapsed}.");
            Assert.Throws<IOException>(() => LiveUsbGuard.AcquireLease(_directory, "fixture-serial"));
            child.Kill();
            await child.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(15), ct);
            // Process exit may be signaled just before Windows finishes closing its handles.
            using var afterCrash = await AcquireAfterExit(ct);
        }
        finally
        {
            if (!child.HasExited) child.Kill();
            await child.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
            output.WriteLine($"Lease helper exit {child.ExitCode}; stderr: {await errors}");
        }
    }

    private async Task<FileStream> AcquireAfterExit(CancellationToken ct)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try { return LiveUsbGuard.AcquireLease(_directory, "fixture-serial"); }
            catch (IOException) when (deadline.Elapsed < TimeSpan.FromSeconds(5)) { await Task.Delay(50, ct); }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
