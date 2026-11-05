using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Linux;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Tests;

public sealed partial class LinuxArchiveProcessTests(ITestOutputHelper diagnostics)
{
    [Fact]
    public async Task The_kernel_main_thread_guard_refuses_thread_only_exit_but_allows_normal_process_exit()
    {
        string probe = Probe();
        using var driver = Process.Start(Driver(probe, "archive-linux-main-exit"))!;
        using var pid = PidHandle(driver.Id);
        try
        {
            string output = await driver.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            await driver.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(0, driver.ExitCode); Assert.Equal("MAIN-EXIT-DENIED=True", output.Trim());
        }
        finally { Signal(pid); await driver.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None); }
    }

    [Fact]
    public async Task An_unrelated_explicitly_inheritable_native_descriptor_is_closed_before_parser_dispatch()
    {
        string probe = Probe(); using var root = new TempDir(); string secret = root.File("private", "descriptor canary");
        using var original = File.OpenHandle(secret, FileMode.Open, FileAccess.Read, FileShare.Read);
        int duplicate = Fcntl(checked((int)original.DangerousGetHandle()), 0 /* DUPFD, deliberately not CLOEXEC */, 1000);
        Assert.True(duplicate >= 1000);
        using var inheritable = new SafeFileHandle((nint)duplicate, ownsHandle: true);
        Assert.Equal(0, Fcntl(duplicate, 1, 0));
        byte[] control = new byte[16]; Assert.Equal(control.Length, RandomAccess.Read(inheritable, control, 0));
        Assert.Equal("descriptor canar", Encoding.UTF8.GetString(control));
        var worker = LinuxArchiveProcess.Start(probe, Guid.NewGuid(), TimeSpan.FromSeconds(30), ["archive-linux-fd", duplicate.ToString(CultureInfo.InvariantCulture)]);
        string output;
        await using (worker) { output = await Text(worker); Assert.Equal(0u, worker.ExitCode); }
        using var facts = JsonDocument.Parse(output);
        Assert.True(facts.RootElement.GetProperty("descriptorClosed").GetBoolean()); Assert.True(facts.RootElement.GetProperty("assetRead").GetBoolean());
        Assert.False(Directory.Exists(worker.ControlPath));
    }

    [Fact]
    public async Task Abrupt_parent_death_stops_a_positively_started_worker_which_ignores_pipe_EOF()
    {
        string probe = Probe(); using var driver = Process.Start(Driver(probe, "archive-linux-parent"))!;
        using var driverPid = PidHandle(driver.Id);
        SafeFileHandle? workerPid = null, controller = null, controllerRoot = null; string? leaf = null; FileIdentity? identity = null;
        try
        {
            string? line = await driver.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken).AsTask().WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            Assert.NotNull(line); using var facts = JsonDocument.Parse(line);
            int pid = facts.RootElement.GetProperty("pid").GetInt32(); string path = facts.RootElement.GetProperty("controller").GetString()!;
            uint uid = LinuxArchiveNative.RequireOrdinaryIdentity();
            string root = $"/sys/fs/cgroup/user.slice/user-{uid}.slice/user@{uid}.service";
            Assert.Equal(root, Path.GetDirectoryName(path)); leaf = Path.GetFileName(path);
            Assert.StartsWith("filecat-archive-", leaf); Assert.True(Guid.TryParseExact(leaf[16..], "N", out _));
            controllerRoot = LinuxArchiveNative.OpenDirectory(root); controller = LinuxArchiveNative.OpenAt(controllerRoot, leaf, directory: true);
            LinuxArchiveNative.RequireCgroupFileSystem(controller);
            var held = UnixFiles.Stat(controller) ?? throw new IOException("The owned native controller could not be inspected."); identity = held.Identity;
            Assert.Equal(uid, held.Owner); Assert.Equal(pid.ToString(CultureInfo.InvariantCulture) + "\n", LinuxArchiveNative.ReadAt(controller, "cgroup.procs"));
            workerPid = PidHandle(pid); Assert.False(IsDead(workerPid));
            await driver.StandardInput.WriteLineAsync("die"); await driver.StandardInput.FlushAsync(TestContext.Current.CancellationToken);
            await driver.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Equal(90, driver.ExitCode);
            var watch = Stopwatch.StartNew();
            while (!IsDead(workerPid) && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(IsDead(workerPid), "A worker which ignores EOF survived abrupt parent death.");
            diagnostics.WriteLine("Parent-death controller: " + path);
        }
        finally
        {
            Signal(driverPid); await driver.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            if (workerPid is not null) { Signal(workerPid); workerPid.Dispose(); }
            if (controller is not null && controllerRoot is not null && leaf is not null)
            {
                // Qualification cleanup only: the crashed parent's exact held, owned native controller, never a
                // guessed path or recursive delete. An active/mismatched group is retained with a failing test.
                using var current = LinuxArchiveNative.OpenAt(controllerRoot, leaf, directory: true);
                Assert.Equal(identity, UnixFiles.Stat(current)?.Identity);
                var wait = Stopwatch.StartNew();
                while (LinuxArchiveNative.ReadAt(controller, "cgroup.events").Contains("populated 1", StringComparison.Ordinal) && wait.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10, CancellationToken.None);
                Assert.Empty(LinuxArchiveNative.ReadAt(controller, "cgroup.procs").Trim());
                Assert.DoesNotContain("populated 1", LinuxArchiveNative.ReadAt(controller, "cgroup.events"));
                Assert.Equal(0, LinuxArchiveNative.UnlinkAt(checked((int)controllerRoot.DangerousGetHandle()), leaf, 0x200));
            }
            controller?.Dispose(); controllerRoot?.Dispose();
        }
    }

    private static ProcessStartInfo Driver(string probe, string command)
    {
        var start = new ProcessStartInfo(probe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(command); return start;
    }
    private static SafeFileHandle PidHandle(int pid)
    { long fd = LinuxArchiveNative.Syscall(434, pid, 0, 0, 0, 0, 0); Assert.True(fd >= 0); return new((nint)fd, ownsHandle: true); }
    private static void Signal(SafeFileHandle pid)
    { long result = LinuxArchiveNative.Syscall(424, pid.DangerousGetHandle(), 9, 0, 0, 0, 0); Assert.True(result == 0 || Marshal.GetLastPInvokeError() == 3); }
    [StructLayout(LayoutKind.Sequential)]
    private struct PollDescriptor { internal int Fd; internal short Events, Revents; }
    private static unsafe bool IsDead(SafeFileHandle pid)
    { var descriptor = new PollDescriptor { Fd = checked((int)pid.DangerousGetHandle()), Events = 1 }; int result = Poll(&descriptor, 1, 0); Assert.InRange(result, 0, 1); return result == 1 && (descriptor.Revents & 1) != 0; }
    [LibraryImport("libc", EntryPoint = "poll", SetLastError = true)]
    private static unsafe partial int Poll(PollDescriptor* descriptors, nuint count, int timeout);
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command, int value);

    [Fact]
    public async Task Real_kernel_denials_keep_unrelated_content_and_metadata_unchanged()
    {
        string probe = Probe(); using var root = new TempDir(); string source = root.File("unrelated", "owned private bytes");
        string target = Path.Combine(root.Path, "unexpected"); DateTime modified = File.GetLastWriteTimeUtc(source);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            using var positive = new TcpClient(); await positive.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port, TestContext.Current.CancellationToken);
            using var accepted = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            var worker = LinuxArchiveProcess.Start(probe, Guid.NewGuid(), TimeSpan.FromSeconds(30), ["archive-linux-authority", source, target], qualificationDiagnostics: true);
            string output;
            await using (worker) { output = await Text(worker); Assert.Equal(0u, worker.ExitCode); }
            using var facts = JsonDocument.Parse(output);
            foreach (string field in new[] {"assetRead","readDenied","writeDenied","metadataDenied","tcpDenied","udpDenied","unixDenied",
                "childDenied","ownSignal","parentSignalDenied","extendedAttributeReadsDenied","clearParentDeathDenied","limitsDenied","parserThread","controllerWriteDenied"}) Assert.True(facts.RootElement.GetProperty(field).GetBoolean(), field);
            Assert.Equal("owned private bytes", File.ReadAllText(source)); Assert.Equal(modified, File.GetLastWriteTimeUtc(source));
            Assert.False(File.Exists(target)); Assert.False(Directory.Exists(worker.ControlPath));
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task A_real_unmanaged_allocation_hits_the_kernel_memory_limit_after_a_healthy_control()
    {
        var worker = LinuxArchiveProcess.Start(Probe(), Guid.NewGuid(), TimeSpan.FromSeconds(30), ["archive-linux-memory"]);
        string output;
        await using (worker)
        {
            output = await Text(worker); Assert.Equal(137u, worker.ExitCode);
        }
        Assert.Contains("HEALTHY-64-MIB-TOUCHED", output); Assert.DoesNotContain("DID NOT STOP", output);
        Assert.True(Counter(worker.FinalMemoryEvents, "oom_kill") >= 1);
        diagnostics.WriteLine("Final native memory events: " + worker.FinalMemoryEvents);
        diagnostics.WriteLine("Final native memory peak bytes: " + worker.FinalMemoryPeak);
        long peak = long.Parse(worker.FinalMemoryPeak.Trim(), CultureInfo.InvariantCulture);
        Assert.InRange(peak, 64L << 20, ArchiveWritePolicy.MaxWorkerMemoryBytes);
        Assert.False(Directory.Exists(worker.ControlPath));
    }

    [Fact]
    public async Task A_positively_started_busy_worker_is_stopped_by_its_native_CPU_limit()
    {
        var worker = LinuxArchiveProcess.Start(Probe(), Guid.NewGuid(), TimeSpan.FromSeconds(1), ["archive-cpu"]);
        string output;
        await using (worker) { output = await Text(worker); Assert.Equal(137u, worker.ExitCode); }
        Assert.Contains("CPU-PROBE-STARTED", output); Assert.DoesNotContain("DID NOT STOP", output);
        Assert.InRange(worker.TotalProcessorTime, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(5));
        diagnostics.WriteLine("Final native CPU: " + worker.TotalProcessorTime);
        Assert.False(Directory.Exists(worker.ControlPath));
    }

    [Fact]
    public async Task Stderr_floods_are_drained_capped_stopped_and_reaped()
    {
        var worker = LinuxArchiveProcess.Start(Probe(), Guid.NewGuid(), TimeSpan.FromSeconds(30), ["archive-stderr"]);
        string output;
        await using (worker) { output = await Text(worker); Assert.True(worker.ErrorLimitExceeded); Assert.NotEqual(0u, worker.ExitCode); }
        Assert.Contains("STDERR-PROBE-STARTED", output); Assert.DoesNotContain("DID NOT STOP", output);
        Assert.InRange((await worker.ErrorForQualification).Length, 0, 16 * 1024);
        Assert.False(Directory.Exists(worker.ControlPath));
    }

    [Fact]
    public async Task Repeated_concurrent_stop_and_dispose_reap_one_healthy_worker()
    {
        await using var worker = LinuxArchiveProcess.Start(Probe(), Guid.NewGuid(), TimeSpan.FromSeconds(30), ["archive-idle"]);
        diagnostics.WriteLine("Owned controller: " + worker.ControlPath);
        using var text = new StreamReader(worker.Output, Encoding.UTF8, leaveOpen: true);
        string? first = await text.ReadLineAsync(TestContext.Current.CancellationToken);
        if (first is null) diagnostics.WriteLine(await worker.ErrorForQualification);
        Assert.Equal(worker.ProcessId.ToString(CultureInfo.InvariantCulture), first);
        Assert.False(worker.HasExited);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(async () => { worker.Kill(); await worker.DisposeAsync(); }, TestContext.Current.CancellationToken)));
        Assert.True(worker.HasExited); worker.Kill(); await worker.DisposeAsync();
        Assert.False(Directory.Exists(worker.ControlPath));
    }

    [Fact]
    public async Task A_healthy_64_MiB_stream_proxy_copy_and_complete_readback_need_no_filesystem_grants()
    {
        string probe = Probe(); Guid nonce = Guid.NewGuid(); byte[] source = new byte[64 << 20]; new Random(854).NextBytes(source);
        using var stage = new MemoryStream();
        var worker = LinuxArchiveProcess.Start(probe, nonce, TimeSpan.FromSeconds(60), ["archive-proxy", nonce.ToString("D"), source.Length.ToString(CultureInfo.InvariantCulture)]);
        await using (worker)
        {
            await Task.Run(() =>
            {
                var send = new ArchiveWorkerProtocol(nonce, 4L * source.Length + (16 << 20));
                var receive = new ArchiveWorkerProtocol(nonce, 4L * source.Length + (16 << 20));
                using var io = new ArchiveIoAuthority([new(0, source.Length)], _ => new MemoryStream(source, writable: false),
                    new ArchiveStageProxy(stage, source.Length, 2L * source.Length), source.Length);
                while (true)
                {
                    var frame = receive.Read(worker.Output);
                    if (frame.Kind == ArchiveWorkerMessage.Completed)
                    { Assert.Equal(SHA256.HashData(source), frame.Payload); break; }
                    var reply = io.Handle(frame); send.Write(worker.Input, reply.Kind, reply.Payload);
                }
                Assert.Equal(-1, worker.Output.ReadByte());
            }, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            await Exit(worker); Assert.Equal(0u, worker.ExitCode);
        }
        Assert.Equal(source, stage.ToArray()); Assert.False(Directory.Exists(worker.ControlPath));
    }

    [Fact]
    public async Task Actual_TAR_assessment_runs_after_native_all_thread_isolation()
    {
        string probe = Probe(); using var bytes = new MemoryStream();
        using (var writer = new TarWriter(bytes, TarEntryFormat.Pax, leaveOpen: true))
        { using var data = new MemoryStream("owned native assessment"u8.ToArray()); writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "owned") { DataStream = data }); }
        byte[] source = bytes.ToArray(); LinuxArchiveProcess? actual = null;
        var client = new ArchiveAssessmentClient((nonce, cpu) => actual = LinuxArchiveProcess.Start(probe, nonce, cpu, ["archive-assess", nonce.ToString("D")]));
        var result = await client.AssessAsync(new(new(ArchiveWriteFormat.Tar), source.Length, Convert.ToHexString(SHA256.HashData(source))),
            () => new MemoryStream(source, writable: false), TestContext.Current.CancellationToken);
        Assert.Equal("owned", Assert.Single(result.Manifest.Members).Name);
        Assert.NotNull(actual); Assert.True(actual.HasExited); Assert.Equal(0u, actual.ExitCode); Assert.False(Directory.Exists(actual.ControlPath));
    }

    private static string Probe()
    {
        if (!OperatingSystem.IsLinux()) Assert.Skip("Native Linux archive confinement lane.");
        if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_NATIVE") != "1") Assert.Skip("Explicit owned native lane required.");
        string? path = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_PROBE");
        Assert.True(!string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) && File.Exists(path), "A published owned self-contained native probe is required.");
        return path!;
    }
    private async Task<string> Text(LinuxArchiveProcess worker)
    {
        using var reader = new StreamReader(worker.Output, Encoding.UTF8, leaveOpen: true);
        string result = await reader.ReadToEndAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        await Exit(worker); diagnostics.WriteLine("Owned controller: " + worker.ControlPath);
        diagnostics.WriteLine("Worker stderr: " + await worker.ErrorForQualification);
        Assert.True(result.Length < 4096, "Unexpected probe stdout volume."); return result;
    }
    private static async Task Exit(LinuxArchiveProcess worker)
    {
        var watch = Stopwatch.StartNew();
        while (!worker.HasExited && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(worker.HasExited, await worker.ErrorForQualification.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken));
    }
    private static long Counter(string text, string key) => long.Parse(text.Split('\n').Single(l => l.StartsWith(key + " ", StringComparison.Ordinal))[(key.Length + 1)..], CultureInfo.InvariantCulture);
}
