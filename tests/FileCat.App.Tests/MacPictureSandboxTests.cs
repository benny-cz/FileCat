using System.Diagnostics;
using System.Runtime.InteropServices;
using FileCat.App.Services;

namespace FileCat.App.Tests;

public sealed class MacPictureSandboxTests(ITestOutputHelper output)
{
    [Fact]
    public void Generated_policy_removes_filesystem_authority_before_narrow_read_grants()
    {
        string executable = Path.Combine(Path.GetTempPath(), "quoted\"dir", "worker");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetDirectoryName(executable)!));
        string parent = Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(root)!);
        string quotedRoot = root.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        string quotedParent = parent.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
        string profile = MacPictureSandbox.ProfileFor(executable);
        int denyRead = profile.IndexOf("(deny file-read*)", StringComparison.Ordinal);
        int systemBaseline = profile.IndexOf("(import \"system.sb\")", StringComparison.Ordinal);
        int allowWorker = profile.IndexOf($"(allow file-read* (subpath \"{quotedRoot}\"))", StringComparison.Ordinal);
        int allowParentMetadata = profile.IndexOf($"(allow file-read-metadata (literal \"{quotedParent}\"))", StringComparison.Ordinal);
        int denyWrite = profile.IndexOf("(deny file-write*)", StringComparison.Ordinal);
        int denyFork = profile.IndexOf("(deny process-fork)", StringComparison.Ordinal);
        int denyNetwork = profile.IndexOf("(deny network*)", StringComparison.Ordinal);
        Assert.True(denyRead >= 0);
        Assert.True(systemBaseline > denyRead);
        Assert.True(allowWorker > systemBaseline);
        Assert.True(allowParentMetadata > allowWorker);
        Assert.True(denyWrite > allowParentMetadata);
        Assert.True(denyFork > denyWrite);
        Assert.True(denyNetwork > denyFork);
        Assert.Contains("quoted\\\"dir", profile, StringComparison.Ordinal);
        // Review finding R-H4: no other program, launchd job, service, Apple event, signal, I/O Kit device or terminal.
        int denyExec = profile.IndexOf("(deny process-exec*)", StringComparison.Ordinal);
        int allowWorkerExec = profile.IndexOf("(allow process-exec (literal ", StringComparison.Ordinal);
        Assert.True(denyExec > denyNetwork && allowWorkerExec > denyExec);
        foreach (string rule in new[] { "(deny job-creation)", "(deny mach-lookup)", "(deny appleevent-send)",
                     "(deny signal)(allow signal (target self))", "(deny iokit-open)", "(deny pseudo-tty)" })
            Assert.True(profile.IndexOf(rule, StringComparison.Ordinal) > allowWorkerExec, rule);
        Assert.DoesNotContain("(subpath \"/dev\")", profile, StringComparison.Ordinal);
        Assert.Contains("(allow file-read* (literal \"/dev/urandom\"))", profile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task macOS_policy_runs_no_other_program_signals_no_other_process_and_creates_no_launchd_job()
    {
        if (!OperatingSystem.IsMacOS()) { Assert.Skip("The macOS Seatbelt policy is native to macOS."); return; }
        // Review finding R-H4: the worker could run another program, have launchd start an unsandboxed job for it, or
        // signal the user's processes.
        var exec = await Run(MacPictureSandbox.StartInfo("/bin/sh", ["-c", "exec /bin/echo R-H4-EXEC"]));
        var signal = await Run(MacPictureSandbox.StartInfo("/bin/kill",
            ["-0", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)]));
        string label = "io.github.benny-cz.filecat.picture-probe." + Guid.NewGuid().ToString("N");
        var submit = await Run(MacPictureSandbox.StartInfo("/bin/launchctl", ["submit", "-l", label, "--", "/usr/bin/true"]));
        var listed = await Run(new ProcessStartInfo("/bin/launchctl", ["list", label])
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
        if (listed.ExitCode == 0) using (Process.Start("/bin/launchctl", ["remove", label])) { }
        output.WriteLine($"MAC_PICTURE_AUTHORITY exec={exec.ExitCode}/{exec.Output.Trim()} signal={signal.ExitCode} submit={submit.ExitCode}/{submit.Error.Trim()} listed={listed.ExitCode}");
        Assert.NotEqual(0, exec.ExitCode);
        Assert.DoesNotContain("R-H4-EXEC", exec.Output, StringComparison.Ordinal);
        Assert.NotEqual(0, signal.ExitCode);
        Assert.NotEqual(0, listed.ExitCode);
    }

    [Fact]
    public async Task Actual_macOS_worker_policy_denies_descendants_and_network()
    {
        if (!OperatingSystem.IsMacOS()) { Assert.Skip("The macOS Seatbelt policy is native to macOS."); return; }
        var (executable, _) = PictureDecoder.WorkerCommand(64);
        string[] arguments = Path.GetFileNameWithoutExtension(executable).Equals("FileCat", StringComparison.OrdinalIgnoreCase)
            ? [PictureWorker.Argument, "64"]
            : [typeof(PictureDecoder).Assembly.Location, PictureWorker.Argument, "64"];
        var start = MacPictureSandbox.StartInfo(executable, arguments);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The policy probe did not start.");
        try
        {
            var clock = Stopwatch.StartNew();
            while (!process.HasExited && !process.ProcessName.Equals("FileCat", StringComparison.OrdinalIgnoreCase) &&
                   clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                process.Refresh();
            }
            Assert.False(process.HasExited);
            Assert.Equal("FileCat", process.ProcessName, ignoreCase: true);
            int fork = SandboxCheck(process.Id, "process-fork", 0);
            int outbound = SandboxCheck(process.Id, "network-outbound", 0);
            int inbound = SandboxCheck(process.Id, "network-inbound", 0);
            output.WriteLine($"MAC_PICTURE_SANDBOX pid={process.Id} executable={executable} process={process.ProcessName} process-fork={fork} network-outbound={outbound} network-inbound={inbound}");
            Assert.Equal(1, fork);
            Assert.Equal(1, outbound);
            Assert.Equal(1, inbound);
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task macOS_policy_allows_application_reads_but_refuses_unlisted_reads_and_all_writes()
    {
        if (!OperatingSystem.IsMacOS()) { Assert.Skip("The macOS Seatbelt policy is native to macOS."); return; }
        string root = Path.Combine(Path.GetTempPath(), $"filecat-picture-fs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string secret = Path.Combine(root, "secret.txt");
        string write = Path.Combine(root, "write.txt");
        await File.WriteAllTextAsync(secret, "I550_SECRET", TestContext.Current.CancellationToken);
        try
        {
            var deniedRead = await Run(MacPictureSandbox.StartInfo("/bin/cat", [secret]));
            var deniedWrite = await Run(MacPictureSandbox.StartInfo("/usr/bin/touch", [write]));
            var allowedRead = await Run(MacPictureSandbox.StartInfo("/bin/cat", [typeof(PictureDecoder).Assembly.Location]));
            output.WriteLine($"MAC_PICTURE_FS denied-read={deniedRead.ExitCode}/{deniedRead.Error} denied-write={deniedWrite.ExitCode}/{deniedWrite.Error} allowed-read={allowedRead.ExitCode}/{allowedRead.Output.Length}");
            Assert.NotEqual(0, deniedRead.ExitCode);
            Assert.DoesNotContain("I550_SECRET", deniedRead.Output, StringComparison.Ordinal);
            Assert.NotEqual(0, deniedWrite.ExitCode);
            Assert.False(File.Exists(write));
            Assert.Equal(0, allowedRead.ExitCode);
            Assert.NotEmpty(allowedRead.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)> Run(ProcessStartInfo start)
    {
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The policy probe did not start.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        return (process.ExitCode, await output, await error);
    }

    [DllImport("/usr/lib/libsandbox.1.dylib", EntryPoint = "sandbox_check", CharSet = CharSet.Ansi)]
    private static extern int SandboxCheck(int pid, string operation, int filterType);

}
