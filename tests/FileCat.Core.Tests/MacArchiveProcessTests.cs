using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Archives.MacOS;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

// The macOS archive boundary (route decision of 12 November 2026; MACOS_SANDBOX_WORKER_SPIKE.md), run with the published
// worker probe and the application's own sandbox launcher beside it. Each workload runs inside the sandboxed worker after
// the same checks FileCat's worker passes.
[Collection(ArchiveWorkerIntegrationCollection.Name)]
public sealed partial class MacArchiveProcessTests(ITestOutputHelper diagnostics)
{
    private const string Container = "io.github.benny-cz.filecat.archive-worker";

    [Fact]
    public async Task The_sandboxed_worker_reaches_no_file_socket_process_or_system_service_of_its_user()
    {
        string probe = Probe(); string fixture = HomeFixture();
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port; int accepted = 0;
        _ = AcceptAsync();
        try
        {
            string file = Path.Combine(fixture, "private.txt"); File.WriteAllText(file, "private user data");
            var written = File.GetLastWriteTimeUtc(file);
            var worker = MacArchiveProcess.Start(probe, Guid.NewGuid(), TimeSpan.FromSeconds(30),
                ["archive-mac-boundary", file, fixture, port.ToString(CultureInfo.InvariantCulture)]);
            string output;
            await using (worker) { output = await Text(worker); Assert.Equal(0u, worker.ExitCode); }
            diagnostics.WriteLine(output);
            using var facts = JsonDocument.Parse(output); var root = facts.RootElement;
            foreach (string denied in new[] { "readDenied", "listDenied", "writeDenied", "metadataDenied", "tmpWriteDenied", "connectDenied",
                "bindDenied", "udpDenied", "dnsDenied", "processStartDenied" })
                Assert.True(root.GetProperty(denied).GetBoolean(), denied);
            Assert.True(root.GetProperty("assetRead").GetBoolean());
            Assert.Equal(35, root.GetProperty("posixSpawnErrno").GetInt32()); // EAGAIN: no new process under RLIMIT_NPROC 0
            Assert.True(root.GetProperty("bootstrapNull").GetBoolean());
            Assert.Equal(0, root.GetProperty("machServicesReachable").GetInt32());
            Assert.Equal(Container, root.GetProperty("container").GetString());
            Assert.Equal("private user data", File.ReadAllText(file)); Assert.Equal(written, File.GetLastWriteTimeUtc(file));
            Assert.False(File.Exists(Path.Combine(fixture, "sandbox-write.txt")));
            Assert.Equal(0, Volatile.Read(ref accepted));
        }
        finally { listener.Stop(); Directory.Delete(fixture, true); }

        async Task AcceptAsync()
        {
            try { while (true) { using var client = await listener.AcceptTcpClientAsync(); Interlocked.Increment(ref accepted); } }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException) { }
        }
    }

    [Fact]
    public async Task An_unrelated_inheritable_descriptor_is_closed_before_the_worker_starts()
    {
        string probe = Probe(); string fixture = HomeFixture();
        try
        {
            string secret = Path.Combine(fixture, "descriptor.txt"); File.WriteAllText(secret, "descriptor canary");
            using var original = File.OpenHandle(secret, FileMode.Open, FileAccess.Read, FileShare.Read);
            int duplicate = Fcntl(checked((int)original.DangerousGetHandle()), 0 /* F_DUPFD, deliberately not close-on-exec */, 200);
            Assert.True(duplicate >= 200);
            using var inheritable = new SafeFileHandle((nint)duplicate, ownsHandle: true);
            Assert.Equal(0, Fcntl(duplicate, 1 /* F_GETFD */, 0));
            var worker = MacArchiveProcess.Start(probe, Guid.NewGuid(), TimeSpan.FromSeconds(30), ["archive-mac-fd", duplicate.ToString(CultureInfo.InvariantCulture)]);
            string output;
            await using (worker) { output = await Text(worker); Assert.Equal(0u, worker.ExitCode); }
            using var facts = JsonDocument.Parse(output);
            Assert.True(facts.RootElement.GetProperty("descriptorClosed").GetBoolean());
            Assert.True(facts.RootElement.GetProperty("assetRead").GetBoolean());
        }
        finally { Directory.Delete(fixture, true); }
    }

    [Fact]
    public async Task The_memory_monitor_stops_a_worker_above_its_budget_and_spares_one_below_it()
    {
        string probe = Probe(); const long Budget = 256L << 20;
        var over = MacArchiveProcess.Start(probe, Guid.NewGuid(), TimeSpan.FromSeconds(60), ["archive-mac-memory", "2048"], Budget);
        string output;
        await using (over) { output = await Text(over, TimeSpan.FromSeconds(30)); }
        diagnostics.WriteLine($"Peak sampled footprint {over.PeakFootprint >> 20} MiB; last output: {output.Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()}");
        Assert.True(over.MemoryLimitExceeded);
        Assert.DoesNotContain("allocated", output);
        Assert.InRange(over.PeakFootprint, Budget + 1, Budget + (128L << 20)); // a sampled bound: some overshoot, never unbounded

        var under = MacArchiveProcess.Start(probe, Guid.NewGuid(), TimeSpan.FromSeconds(60), ["archive-mac-memory", "64"], Budget);
        await using (under)
        {
            using var reader = new StreamReader(under.Output, Encoding.UTF8, leaveOpen: true);
            string? line;
            while ((line = await reader.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken)) != "allocated")
                Assert.NotNull(line);
            await Task.Delay(200, TestContext.Current.CancellationToken); // further samples while it holds its memory
            Assert.False(under.MemoryLimitExceeded); Assert.False(under.HasExited);
        }
        Assert.False(under.MemoryLimitExceeded);
    }

    [Fact]
    public async Task The_kernel_CPU_limit_stops_a_spinning_worker()
    {
        string probe = Probe();
        var worker = MacArchiveProcess.Start(probe, Guid.NewGuid(), TimeSpan.FromSeconds(2), ["archive-cpu"]);
        var watch = Stopwatch.StartNew(); string output;
        await using (worker) { output = await Text(worker, TimeSpan.FromSeconds(20)); }
        diagnostics.WriteLine($"Stopped after {watch.Elapsed.TotalSeconds:F1} s, exit {worker.ExitCode}, CPU {worker.TotalProcessorTime.TotalSeconds:F1} s");
        Assert.Contains("CPU-PROBE-STARTED", output); Assert.DoesNotContain("DID NOT STOP", output);
        Assert.NotEqual(0u, worker.ExitCode); Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task A_worker_flooding_standard_error_is_stopped()
    {
        string probe = Probe();
        var worker = MacArchiveProcess.Start(probe, Guid.NewGuid(), TimeSpan.FromSeconds(30), ["archive-stderr"]);
        string output;
        await using (worker) { output = await Text(worker, TimeSpan.FromSeconds(20)); }
        Assert.True(worker.ErrorLimitExceeded); Assert.DoesNotContain("DID NOT STOP", output);
    }

    [Theory]
    [InlineData("archive-assess")]
    [InlineData("archive-verify")]
    [InlineData("archive-transform")]
    [InlineData("archive-cancel-ignore")]
    [InlineData("archive-cancel-output")]
    public async Task Cancellation_ends_the_sandboxed_worker_and_reaps_it_before_releasing_the_queue(string role)
    {
        string probe = Probe(); MacArchiveProcess? actual = null;
        await ArchiveWorkerCancellationAssertions.Run((nonce, cpu) => actual = MacArchiveProcess.Start(probe, nonce, cpu, [role, nonce.ToString("D")]), role);
        Assert.NotNull(actual); Assert.False(actual.MemoryLimitExceeded);
    }

    [Fact]
    public async Task Abrupt_parent_death_stops_a_worker_which_ignores_pipe_EOF()
    {
        string probe = Probe();
        var start = new ProcessStartInfo(probe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("archive-mac-parent");
        using var driver = Process.Start(start)!;
        int pid = 0;
        try
        {
            string? line = await driver.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.True(line is not null, await driver.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken));
            using (var facts = JsonDocument.Parse(line)) pid = facts.RootElement.GetProperty("pid").GetInt32();
            Assert.True(Alive(pid));
            await driver.StandardInput.WriteLineAsync("die"); await driver.StandardInput.FlushAsync(TestContext.Current.CancellationToken);
            await driver.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(90, driver.ExitCode);
            var watch = Stopwatch.StartNew();
            while (Alive(pid) && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.False(Alive(pid), "A worker which ignores EOF survived its parent's abrupt death.");
            diagnostics.WriteLine($"Worker {pid} ended {watch.ElapsedMilliseconds} ms after its parent.");
        }
        finally
        {
            if (!driver.HasExited) driver.Kill();
            if (pid > 0 && Alive(pid)) Kill(pid, 9);
        }
    }

    [Fact]
    public async Task Actual_TAR_assessment_runs_in_the_sandboxed_worker()
    {
        string probe = Probe(); using var bytes = new MemoryStream();
        using (var writer = new TarWriter(bytes, TarEntryFormat.Pax, leaveOpen: true))
        { using var data = new MemoryStream("owned sandboxed assessment"u8.ToArray()); writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "owned") { DataStream = data }); }
        byte[] source = bytes.ToArray(); MacArchiveProcess? actual = null;
        var client = new ArchiveAssessmentClient((nonce, cpu) => actual = MacArchiveProcess.Start(probe, nonce, cpu, ["archive-assess-v2", nonce.ToString("D")]), protocolVersion: 2);
        var result = await client.AssessAsync(new(new(ArchiveWriteFormat.Tar), source.Length, Convert.ToHexString(SHA256.HashData(source))),
            () => new MemoryStream(source, writable: false), TestContext.Current.CancellationToken);
        Assert.Equal("owned", Assert.Single(result.Manifest.Members).Name);
        Assert.NotNull(actual); Assert.True(actual.HasExited); Assert.Equal(0u, actual.ExitCode); Assert.False(actual.MemoryLimitExceeded);
    }

    // A launcher whose signature lost the App Sandbox entitlement runs unconfined; it must refuse before executing anything.
    [Fact]
    public async Task The_launcher_refuses_to_run_without_its_sandbox()
    {
        string probe = Probe(); string fixture = HomeFixture();
        try
        {
            string copy = Path.Combine(fixture, MacArchiveIsolation.LauncherFileName);
            File.Copy(Path.Combine(Path.GetDirectoryName(probe)!, MacArchiveIsolation.LauncherFileName), copy);
            using (var resign = Process.Start(new ProcessStartInfo("/usr/bin/codesign", ["--force", "--sign", "-", "--timestamp=none", copy])
                   { RedirectStandardError = true, UseShellExecute = false })!)
            { await resign.WaitForExitAsync(TestContext.Current.CancellationToken); Assert.Equal(0, resign.ExitCode); }
            var (exit, error) = await RunLauncher(copy, Guid.NewGuid().ToString("D"), Environment.ProcessId);
            Assert.Equal(65, exit); Assert.Contains("not inside the App Sandbox", error);

            // The real launcher refuses facts that do not match: another parent, a malformed nonce.
            string launcher = Path.Combine(Path.GetDirectoryName(probe)!, MacArchiveIsolation.LauncherFileName);
            (exit, error) = await RunLauncher(launcher, Guid.NewGuid().ToString("D"), Environment.ProcessId + 1);
            Assert.Equal(65, exit); Assert.Contains("unexpected parent", error);
            (exit, error) = await RunLauncher(launcher, Guid.NewGuid().ToString("D").ToUpperInvariant(), Environment.ProcessId);
            Assert.Equal(65, exit); Assert.Contains("invalid launch facts", error);
        }
        finally { Directory.Delete(fixture, true); }
    }

    // Started without the launcher, the worker image has none of the boundary and must refuse before reading input.
    [Fact]
    public async Task The_worker_image_refuses_to_run_outside_its_sandbox()
    {
        string probe = Probe();
        foreach (bool pretend in new[] { false, true })
        {
            using var cancellation = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            var start = new ProcessStartInfo(probe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            if (pretend) start.Environment["APP_SANDBOX_CONTAINER_ID"] = Container;
            foreach (string argument in new[] { MacArchiveIsolation.WorkerArgument, Guid.NewGuid().ToString("D"), Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                "30", "archive-idle", ArchiveWorkerCancellation.Argument, cancellation.GetClientHandleAsString() })
                start.ArgumentList.Add(argument);
            using var worker = Process.Start(start)!; cancellation.DisposeLocalCopyOfClientHandle();
            string error = await worker.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            await worker.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            diagnostics.WriteLine(error);
            Assert.Equal(65, worker.ExitCode); Assert.Equal("", await worker.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken));
        }
    }

    private static async Task<(int Exit, string Error)> RunLauncher(string launcher, string nonce, int parent)
    {
        using var cancellation = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var start = new ProcessStartInfo(launcher) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment.Clear(); start.Environment["PATH"] = "/nonexistent";
        foreach (string argument in new[] { MacArchiveIsolation.LauncherArgument, "FileCat.WorkerProbe", nonce, parent.ToString(CultureInfo.InvariantCulture),
            "30", "archive-idle", ArchiveWorkerCancellation.Argument, cancellation.GetClientHandleAsString() })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; cancellation.DisposeLocalCopyOfClientHandle();
        string error = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        return (process.ExitCode, error);
    }

    private static string Probe()
    {
        if (!OperatingSystem.IsMacOS()) Assert.Skip("Native macOS archive sandbox lane.");
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_MAC_NATIVE") != "1") Assert.Skip("Explicit owned native lane required.");
        if (Environment.IsPrivilegedProcess) Assert.Skip("Archive workers are not started from an administrator process.");
        string? path = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_MAC_PROBE");
        Assert.True(!string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && File.Exists(path) &&
            File.Exists(Path.Combine(Path.GetDirectoryName(path)!, MacArchiveIsolation.LauncherFileName)),
            "A published self-contained worker probe with the sandbox launcher beside it is required.");
        return path!;
    }

    // Below the user's home, which the sandbox refuses: the denials are then about the user's own data, not a system folder.
    private static string HomeFixture()
    {
        string home = Environment.GetEnvironmentVariable("HOME") ?? throw new InvalidOperationException("HOME is required.");
        return Directory.CreateDirectory(Path.Combine(home, ".filecat-mac-boundary-" + Guid.NewGuid().ToString("N"))).FullName;
    }

    private async Task<string> Text(MacArchiveProcess worker, TimeSpan? limit = null)
    {
        using var reader = new StreamReader(worker.Output, Encoding.UTF8, leaveOpen: true);
        string result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken).WaitAsync(limit ?? TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        var watch = Stopwatch.StartNew();
        while (!worker.HasExited && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(worker.HasExited, await worker.ErrorForQualification.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
        diagnostics.WriteLine("Worker stderr: " + await worker.ErrorForQualification);
        return result;
    }

    private static bool Alive(int pid) => Kill(pid, 0) == 0 || Marshal.GetLastPInvokeError() != 3 /* ESRCH */;

    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command, int argument);
    [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static partial int Kill(int pid, int signal);
}
