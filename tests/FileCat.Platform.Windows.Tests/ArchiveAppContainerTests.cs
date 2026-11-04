using System.Net;
using System.Net.Sockets;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Shell;

namespace FileCat.Platform.Windows.Tests;

/// <summary>Explicit native qualification lane; ordinary test runs never alter an installed runtime's ACLs.</summary>
public sealed partial class ArchiveAppContainerTests
{
    private static string Probe()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_CONTAINMENT") != "1")
            Assert.Skip("Run the explicit Windows archive containment qualification lane.");
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_PROBE_ASSETS");
        if (assets is null || !Path.IsPathFullyQualified(assets) || !File.Exists(Path.Combine(assets, "coreclr.dll")))
            Assert.Fail("Supply a dedicated self-contained FileCat.WorkerProbe bundle.");
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets);
        return Path.Combine(assets, "FileCat.WorkerProbe.exe");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_assessment_protocol_parses_real_TAR_in_the_confined_process(bool application)
    {
        string executable = Probe();
        if (application)
        {
            string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
            Assert.True(assets is not null && Path.IsPathFullyQualified(assets), "Supply a dedicated self-contained FileCat application bundle.");
            ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
            executable = Path.Combine(assets!, "FileCat.exe");
        }
        using var tar = new MemoryStream();
        byte[] payload = new byte[64 << 10]; new Random(711).NextBytes(payload);
        using (var writer = new System.Formats.Tar.TarWriter(tar, leaveOpen: true))
        using (var data = new MemoryStream(payload))
            writer.WriteEntry(new System.Formats.Tar.PaxTarEntry(System.Formats.Tar.TarEntryType.RegularFile, "owned.txt")
                { DataStream = data, ModificationTime = DateTimeOffset.UnixEpoch });
        byte[] bytes = tar.ToArray();
        ArchiveAppContainer? actual = null;
        var client = new ArchiveAssessmentClient((nonce, cpu) => actual = application
            ? ArchiveAppContainer.StartAssessment(executable, nonce, cpu)
            : ArchiveAppContainer.Start(executable, "archive-assess " + nonce, cpu));
        try
        {
            var result = await client.AssessAsync(new(new(ArchiveWriteFormat.Tar), bytes.Length, Convert.ToHexString(SHA256.HashData(bytes))),
                () => new MemoryStream(bytes, writable: false), TestContext.Current.CancellationToken);
            Assert.Equal("owned.txt", Assert.Single(result.Manifest.Members).Name);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)), result.Manifest.Members[0].Sha256);
            Assert.True(actual!.HasExited);
            Assert.Equal(0u, actual.ExitCode);
        }
        catch (Exception ex)
        {
            Assert.Fail($"{ex.GetType().Name}: {ex.Message}; exit={actual?.ExitCode:X8}; stderr={(actual is null ? "" : await actual.ErrorForQualification)}");
        }
    }

    [Fact]
    public async Task A_forged_startup_privilege_identity_is_refused_before_any_archive_input()
    {
        _ = Probe();
        string? assets = Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_APP_ASSETS");
        Assert.True(assets is not null && Path.IsPathFullyQualified(assets));
        ArchiveAppContainer.ProvisionRuntimeReadAccess(assets!);
        int opens = 0;
        ArchiveAppContainer? actual = null;
        var client = new ArchiveAssessmentClient((nonce, cpu) => actual = ArchiveAppContainer.Start(Path.Combine(assets!, "FileCat.exe"),
            $"--archive-worker {nonce:D} FFFFFFFFFFFFFFFF", cpu));
        var failure = await Assert.ThrowsAsync<ArchiveWriteException>(() => client.AssessAsync(
            new(new(ArchiveWriteFormat.Tar), 1024, Convert.ToHexString(SHA256.HashData(new byte[1024]))),
            () => { opens++; throw new InvalidOperationException("No source may be requested before the boot check."); },
            TestContext.Current.CancellationToken));
        Assert.Equal(ArchiveWriteReason.WorkerUnavailable, failure.Reason);
        Assert.Equal(0, opens);
        Assert.True(actual!.HasExited);
        Assert.Equal(65u, actual.ExitCode);
    }

    [Fact]
    public async Task No_capabilities_or_user_files_network_registry_children_or_profile_writes_are_granted()
    {
        string probe = Probe();
        var fixture = Directory.CreateTempSubdirectory("filecat-archive-boundary-");
        string sentinel = Path.Combine(fixture.FullName, "private-sentinel.txt");
        string unwanted = Path.Combine(fixture.FullName, "unwanted.txt");
        string profileWrite = Path.Combine(ArchiveAppContainer.ProfileDirectory, "unexpected-" + Guid.NewGuid().ToString("N"));
        string[] profileTargets = new[] { "", "LocalState", "TempState", "LocalCache", "AC\\Temp" }
            .Select(suffix => Path.Combine(Path.GetDirectoryName(profileWrite)!, suffix, Path.GetFileName(profileWrite))).ToArray();
        string registryKey = "Software\\FileCatArchiveBoundary-" + Guid.NewGuid().ToString("N");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            foreach (string target in profileTargets) Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(sentinel, "owned private sentinel");
            var acl = new FileSecurity();
            acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            using var identity = WindowsIdentity.GetCurrent();
            acl.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(sentinel).SetAccessControl(acl);
            Assert.Equal("owned private sentinel", File.ReadAllText(sentinel));
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            // Positive control: the test parent's ordinary session can really reach this same listening socket.
            using (var normal = new TcpClient())
            {
                await normal.ConnectAsync(IPAddress.Loopback, port, TestContext.Current.CancellationToken);
                using var accepted = await listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken);
            }
            string args = $"archive-boundary \"{sentinel}\" \"{unwanted}\" \"{profileWrite}\" {port} \"{registryKey}\"";
            await using var worker = ArchiveAppContainer.Start(probe, args, TimeSpan.FromSeconds(30));
            using var output = new StreamReader(worker.Output);
            string json = await output.ReadToEndAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            while (!worker.HasExited) await Task.Delay(10, TestContext.Current.CancellationToken);
            Assert.True(worker.ExitCode == 0, $"exit=0x{worker.ExitCode:X8}; output={json}; error={await worker.ErrorForQualification}");
            using var result = JsonDocument.Parse(json);
            Assert.True(result.RootElement.GetProperty("appContainer").GetBoolean());
            Assert.Equal(0, result.RootElement.GetProperty("capabilityCount").GetInt32());
            foreach (string assertion in new[] { "assetRead", "readDenied", "writeDenied", "profileWriteDenied", "childDenied", "registryDenied" })
                Assert.True(result.RootElement.GetProperty(assertion).GetBoolean(), json);
            Assert.Contains(result.RootElement.GetProperty("network").GetString(),
                new[] { "StartupRefused:10107", "SocketRefused:10013", "ConnectRefused:10013" });
            Assert.False(File.Exists(unwanted));
            Assert.All(profileTargets, target => Assert.False(File.Exists(target), target));
            Assert.Equal("owned private sentinel", File.ReadAllText(sentinel));
            Assert.False(listener.Pending());
            TestContext.Current.TestOutputHelper?.WriteLine(json);
        }
        finally
        {
            listener.Stop();
            // Exact owned fixture files only; never recursively clean an uncertain worker-created tree.
            foreach (string file in new[] { sentinel, unwanted }.Concat(profileTargets))
                if (File.Exists(file)) File.Delete(file);
            fixture.Delete();
        }
    }

    [Fact]
    public async Task Job_commit_ceiling_refuses_excess_native_allocation_while_healthy_allocation_works()
    {
        await using var worker = ArchiveAppContainer.Start(Probe(), "archive-memory", TimeSpan.FromSeconds(30));
        using var output = new StreamReader(worker.Output);
        string result = await output.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        while (!worker.HasExited) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(worker.ExitCode == 0, $"exit=0x{worker.ExitCode:X8}; output={result}");
        Assert.Contains("healthy=True refused=True", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cpu_ceiling_terminates_an_actual_busy_worker()
    {
        await using var worker = ArchiveAppContainer.Start(Probe(), "archive-cpu", TimeSpan.FromSeconds(1));
        using var output = new StreamReader(worker.Output);
        string result = await output.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        while (!worker.HasExited) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.NotEqual(0u, worker.ExitCode);
        Assert.Contains("CPU-PROBE-STARTED", result);
        Assert.DoesNotContain("CPU LIMIT DID NOT STOP", result);
    }

    [Fact]
    public async Task Excess_stderr_is_drained_bounded_and_terminates_the_worker()
    {
        await using var worker = ArchiveAppContainer.Start(Probe(), "archive-stderr", TimeSpan.FromSeconds(30));
        using var output = new StreamReader(worker.Output);
        string result = await output.ReadToEndAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        while (!worker.HasExited) await Task.Delay(10, TestContext.Current.CancellationToken);
        Assert.True(worker.ErrorLimitExceeded);
        Assert.NotEqual(0u, worker.ExitCode);
        Assert.Equal(8192, (await worker.ErrorForQualification).Length);
        Assert.Contains("STDERR-PROBE-STARTED", result);
        Assert.DoesNotContain("STDERR LIMIT DID NOT STOP", result);
    }

    [Fact]
    public async Task Actual_parent_death_ends_its_healthy_idle_worker()
    {
        string probe = Probe();
        string? checkout = AppContext.BaseDirectory;
        while (checkout is not null && !File.Exists(Path.Combine(checkout, "FileCat.slnx"))) checkout = Path.GetDirectoryName(checkout);
        Assert.NotNull(checkout);
        string config = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            ? "Release" : "Debug";
        string harness = Path.Combine(checkout, "tests", "FileCat.CoreCrashHarness", "bin", config, "net10.0", "FileCat.CoreCrashHarness.dll");
        Assert.True(File.Exists(harness), "Build the shared CoreCrashHarness before native qualification.");
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in new[] { harness, "--archive-boundary-parent", typeof(ArchiveAppContainer).Assembly.Location, probe })
            start.ArgumentList.Add(argument);
        using var parent = Process.Start(start)!;
        Process? child = null;
        try
        {
            string? line = await parent.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.True(int.TryParse(line, out _), line is null ? await parent.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken) : line);
            child = Process.GetProcessById(int.Parse(line!)); // hold the actual process object before killing its parent
            Assert.False(child.HasExited);
            parent.Kill(entireProcessTree: false);
            await parent.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await child.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(child.HasExited);
        }
        finally
        {
            // Cleanup must still reap owned processes when xUnit has cancelled the test.
#pragma warning disable xUnit1051
            if (!parent.HasExited) { parent.Kill(entireProcessTree: false); await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            if (child is not null)
            {
                if (!child.HasExited) { child.Kill(entireProcessTree: false); await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                child.Dispose();
            }
#pragma warning restore xUnit1051
        }
    }

    [Fact]
    public async Task An_unrelated_inheritable_file_handle_is_not_granted()
    {
        string probe = Probe();
        string sentinel = Path.Combine(Path.GetTempPath(), "filecat-archive-handle-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(sentinel, "private inherited-handle positive control");
        try
        {
            using var handle = File.OpenHandle(sentinel, FileMode.Open, FileAccess.Read, FileShare.Read);
            nint raw = handle.DangerousGetHandle();
            Assert.True(SetHandleInformation(raw, 1, 1)); // deliberately inheritable, excluded from the explicit list
            byte[] info = new byte[52];
            Assert.True(GetFileInformationByHandle(raw, info));
            string identity = Convert.ToHexString(info.AsSpan(28, 4)) + Convert.ToHexString(info.AsSpan(44, 8));
            Assert.Equal("private inherited-handle positive control", File.ReadAllText(sentinel));
            await using var worker = ArchiveAppContainer.Start(probe, "archive-handle " + raw.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
                TimeSpan.FromSeconds(30));
            using var output = new StreamReader(worker.Output);
            string result = await output.ReadToEndAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            while (!worker.HasExited) await Task.Delay(10, TestContext.Current.CancellationToken);
            // Windows can enable strict invalid-handle checks for an AppContainer, terminating the deliberate
            // negative probe with STATUS_INVALID_HANDLE. Require a successful native query control first.
            Assert.Matches("HANDLE-PROBE-CONTROL:[0-9A-F]{24}", result);
            Assert.Contains("HANDLE-PROBE-QUERY", result);
            Assert.True(worker.ExitCode is 0 or 0xC0000008, $"exit=0x{worker.ExitCode:X8}; output={result}");
            Assert.DoesNotContain(identity, result);
        }
        finally { File.Delete(sentinel); }
    }

    [Fact]
    public async Task Repeated_stop_and_dispose_reap_one_actual_worker_without_reusing_closed_handles()
    {
        var worker = ArchiveAppContainer.Start(Probe(), "archive-idle", TimeSpan.FromSeconds(30));
        try
        {
            using var output = new StreamReader(worker.Output);
            string? ready = await output.ReadLineAsync(TestContext.Current.CancellationToken).AsTask()
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Equal(worker.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), ready);
            using var actual = Process.GetProcessById(worker.ProcessId);
            await Task.WhenAll(worker.DisposeAsync().AsTask(), worker.DisposeAsync().AsTask())
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            worker.Kill();
            await worker.DisposeAsync();
            Assert.True(actual.HasExited);
            Assert.True(worker.HasExited);
        }
        finally { await worker.DisposeAsync(); }
    }

    [Fact]
    public async Task A_confined_worker_copies_and_hashes_64_MiB_only_through_parent_stream_grants()
    {
        string probe = Probe();
        byte[] bytes = new byte[64 << 20];
        new Random(515).NextBytes(bytes);
        byte[] expected = SHA256.HashData(bytes);
        using var stage = new MemoryStream();
        using var authority = new ArchiveIoAuthority([new(0, bytes.Length)], _ => new MemoryStream(bytes, writable: false),
            new ArchiveStageProxy(stage, bytes.Length, 4L * bytes.Length), 2L * bytes.Length);
        Guid nonce = Guid.NewGuid();
        await using var worker = ArchiveAppContainer.Start(probe, $"archive-proxy {nonce} {bytes.Length}", TimeSpan.FromSeconds(60));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var terminate = timeout.Token.Register(worker.Kill);
        var receive = new ArchiveWorkerProtocol(nonce, 4L * bytes.Length + (16 << 20));
        var send = new ArchiveWorkerProtocol(nonce, 4L * bytes.Length + (16 << 20));
        byte[] digest = await Task.Run(() =>
        {
            while (true)
            {
                var request = receive.Read(worker.Output);
                if (request.Kind == ArchiveWorkerMessage.Completed) return request.Payload;
                var reply = authority.Handle(request);
                send.Write(worker.Input, reply.Kind, reply.Payload);
            }
        }, timeout.Token).WaitAsync(TimeSpan.FromSeconds(90), timeout.Token);
        while (!worker.HasExited) await Task.Delay(10, timeout.Token);
        Assert.Equal(0u, worker.ExitCode);
        Assert.Equal(expected, digest);
        stage.Position = 0;
        Assert.Equal(expected, SHA256.HashData(stage));
        Assert.Equal("", await worker.ErrorForQualification);
        Assert.False(worker.ErrorLimitExceeded);
    }

    [Fact]
    public void Runtime_provisioning_refuses_a_shared_runtime_shape_and_links_before_changing_its_ACL()
    {
        _ = Probe();
        var fixture = Directory.CreateTempSubdirectory("filecat-archive-assets-");
        string runtime = Path.Combine(fixture.FullName, "coreclr.dll");
        string core = Path.Combine(fixture.FullName, "FileCat.Core.dll");
        string link = Path.Combine(fixture.FullName, "linked-tree");
        string before = fixture.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access);
        try
        {
            File.WriteAllBytes(runtime, []);
            Assert.Throws<ArgumentException>(() => ArchiveAppContainer.ProvisionRuntimeReadAccess(fixture.FullName));
            File.WriteAllBytes(core, []);
            // A junction needs no developer-mode or symbolic-link privilege in an ordinary user session.
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe")) { UseShellExecute = false, CreateNoWindow = true };
            foreach (string argument in new[] { "/d", "/c", "mklink", "/J", link, fixture.Parent!.FullName }) start.ArgumentList.Add(argument);
            using (var junction = Process.Start(start)!)
            {
                try { Assert.True(junction.WaitForExit(10_000)); Assert.Equal(0, junction.ExitCode); }
                finally { if (!junction.HasExited) { junction.Kill(); junction.WaitForExit(10_000); } }
            }
            Assert.Throws<IOException>(() => ArchiveAppContainer.ProvisionRuntimeReadAccess(fixture.FullName));
            Assert.Equal(before, fixture.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.Access));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            File.Delete(core); File.Delete(runtime); fixture.Delete();
        }
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool SetHandleInformation(nint handle, uint mask, uint flags);

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(nint handle, [System.Runtime.InteropServices.Out] byte[] information);
}
