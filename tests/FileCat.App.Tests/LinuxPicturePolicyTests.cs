using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.App.Services;
using SkiaSharp;

namespace FileCat.App.Tests;

/// <summary>Observe the actual worker's kernel boundary before sending any image, then check its complete answer.</summary>
public sealed class LinuxPicturePolicyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("BMP")]
    [InlineData("PNG")]
    [InlineData("invalid")]
    [InlineData("empty")]
    public async Task Linux_worker_restricts_every_thread_before_consuming_input(string kind)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
        {
            Assert.Skip("Linux x64/ARM64 seccomp picture-worker boundary; other platforms retain their own worker policy.");
            return;
        }
        var parentBefore = Status("/proc/thread-self/status");
        var start = PictureDecoder.SandboxedWorkerStartInfo(64);
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        using var worker = Process.Start(start)!;
        var error = worker.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        var response = new MemoryStream();
        var reading = worker.StandardOutput.BaseStream.CopyToAsync(response, TestContext.Current.CancellationToken);
        try
        {
            Dictionary<string, int>? observed = null;
            var clock = Stopwatch.StartNew();
            while (!worker.HasExited && clock.Elapsed < TimeSpan.FromSeconds(10))
            {
                observed = Status($"/proc/{worker.Id}/status");
                // The trusted pre-exec launcher contributes the network filter; the worker then adds the
                // process-creation filter with TSYNC after its runtime threads exist. Do not observe the handoff.
                if (observed["Seccomp_filters"] >= parentBefore["Seccomp_filters"] + 2 &&
                    Directory.GetDirectories($"/proc/{worker.Id}/task").Length > 1) break;
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
            output.WriteLine(JsonSerializer.Serialize(new { kind, RuntimeInformation.ProcessArchitecture, ParentBefore = parentBefore, PID = worker.Id, WorkerBeforeInput = observed }));
            Assert.False(worker.HasExited);
            Assert.NotNull(observed);
            Assert.Equal(1, observed["NoNewPrivs"]);
            Assert.Equal(2, observed["Seccomp"]);
            Assert.True(observed["Seccomp_filters"] >= parentBefore["Seccomp_filters"] + 2);
            var threads = Directory.GetDirectories($"/proc/{worker.Id}/task");
            Assert.True(threads.Length > 1);
            foreach (string thread in threads)
            {
                var state = Status(Path.Combine(thread, "status"));
                output.WriteLine(JsonSerializer.Serialize(new { Thread = thread, State = state }));
                Assert.Equal(2, state["Seccomp"]);
                Assert.True(state["Seccomp_filters"] > parentBefore["Seccomp_filters"]);
            }
            byte[] input = kind switch
            {
                "BMP" => Bmp(), "PNG" => Png(), "invalid" => [1, 7, 23, 45], _ => [],
            };
            await worker.StandardInput.BaseStream.WriteAsync(input, TestContext.Current.CancellationToken);
            worker.StandardInput.Close();
            await worker.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await reading.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            byte[] actual = response.ToArray();
            output.WriteLine(JsonSerializer.Serialize(new { kind, Input = Convert.ToBase64String(input), Answer = Convert.ToBase64String(actual), worker.ExitCode, Stderr = await error }));
            if (kind is "BMP" or "PNG")
            {
                Assert.Equal(0, worker.ExitCode);
                Assert.Equal("FCPX"u8.ToArray(), actual[..4]);
                int[] expected = [3, 2, 3, 2, 1, 0, 3];
                for (int i = 0; i < expected.Length; i++)
                    Assert.Equal(expected[i], BinaryPrimitives.ReadInt32LittleEndian(actual.AsSpan(4 + i * 4)));
                Assert.Equal(kind, System.Text.Encoding.ASCII.GetString(actual, 32, 3));
                Assert.Equal(Pixels(), actual[35..]);
            }
            else
            {
                Assert.Equal(2, worker.ExitCode);
                Assert.Equal("FCPE"u8.ToArray(), actual[..4]);
                int length = BinaryPrimitives.ReadInt32LittleEndian(actual.AsSpan(4));
                Assert.InRange(length, 1, 4096);
                Assert.Equal(8 + length, actual.Length);
            }
            Assert.Equal(parentBefore, Status("/proc/thread-self/status"));
        }
        finally
        {
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await reading.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await error.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            response.Dispose();
        }
    }

    [Theory]
    [InlineData(Architecture.X64, 0xc000003eU, 317L, 56U, 59U, 322U, 57U, 58U, true, 21)]
    [InlineData(Architecture.Arm64, 0xc00000b7U, 277L, 220U, 221U, 281U, null, null, false, 15)]
    public void Linux_process_policy_uses_the_kernel_abi_for_each_supported_architecture(Architecture architecture,
        uint audit, long seccomp, uint clone, uint execve, uint execveat, uint? fork, uint? vfork, bool rejectX32,
        int filterLength)
    {
        var abi = LinuxPictureProcessPolicy.ForArchitecture(architecture);
        Assert.Equal((audit, seccomp, clone, execve, execveat, fork, vfork, rejectX32),
            (abi.AuditArchitecture, abi.Seccomp, abi.CloneSyscall, abi.ExecveSyscall, abi.ExecveAtSyscall,
                abi.ForkSyscall, abi.VforkSyscall, abi.RejectX32));
        Assert.Equal(filterLength, LinuxPictureProcessPolicy.BuildFilter(abi).Length);
    }

    [Theory]
    [InlineData(Architecture.X64)]
    [InlineData(Architecture.Arm64)]
    public void Linux_process_policy_filter_denies_process_creation_but_keeps_thread_creation(Architecture architecture)
    {
        var abi = LinuxPictureProcessPolicy.ForArchitecture(architecture);
        var filter = LinuxPictureProcessPolicy.BuildFilter(abi);
        const uint denied = 0x00050001, noSystemCall = 0x00050026, allowed = 0x7fff0000, killed = 0x80000000;

        Assert.Equal(denied, Evaluate(filter, abi.AuditArchitecture, abi.ExecveSyscall));
        Assert.Equal(denied, Evaluate(filter, abi.AuditArchitecture, abi.ExecveAtSyscall));
        if (abi.ForkSyscall is { } fork) Assert.Equal(denied, Evaluate(filter, abi.AuditArchitecture, fork));
        if (abi.VforkSyscall is { } vfork) Assert.Equal(denied, Evaluate(filter, abi.AuditArchitecture, vfork));
        Assert.Equal(noSystemCall, Evaluate(filter, abi.AuditArchitecture, 435));
        Assert.Equal(denied, Evaluate(filter, abi.AuditArchitecture, abi.CloneSyscall));
        Assert.Equal(allowed, Evaluate(filter, abi.AuditArchitecture, abi.CloneSyscall, 0x10000));
        Assert.Equal(allowed, Evaluate(filter, abi.AuditArchitecture, architecture == Architecture.X64 ? 0U : 63U));
        Assert.Equal(killed, Evaluate(filter, architecture == Architecture.X64 ? 0xc00000b7U : 0xc000003eU, abi.ExecveSyscall));
        if (abi.RejectX32) Assert.Equal(denied, Evaluate(filter, abi.AuditArchitecture, 0x40000000));
    }

    [Theory]
    [InlineData(Architecture.X64, 41U)]
    [InlineData(Architecture.Arm64, 198U)]
    public void Linux_pre_exec_filter_denies_network_io_uring_and_metadata_changes_but_keeps_ordinary_io(
        Architecture architecture, uint socket)
    {
        var abi = LinuxPictureProcessPolicy.ForArchitecture(architecture);
        var filter = LinuxPictureSandbox.BuildPreExecFilter(abi);
        const uint denied = 0x00050001, allowed = 0x7fff0000, killed = 0x80000000;
        Assert.Equal(denied, Evaluate(filter, abi.AuditArchitecture, socket));
        Assert.Equal(denied, Evaluate(filter, abi.AuditArchitecture, 425));
        // Metadata Landlock does not govern (review finding R-H3): truncate, fchmodat, fchownat, setxattr, utimensat.
        uint[] metadata = architecture == Architecture.X64 ? [76, 90, 268, 260, 188, 280, 452] : [45, 53, 54, 5, 88, 452];
        foreach (uint call in metadata) Assert.Equal(denied, Evaluate(filter, abi.AuditArchitecture, call));
        Assert.Equal(allowed, Evaluate(filter, abi.AuditArchitecture, architecture == Architecture.X64 ? 77U : 46U)); // ftruncate
        Assert.Equal(allowed, Evaluate(filter, abi.AuditArchitecture, architecture == Architecture.X64 ? 0U : 63U));
        Assert.Equal(killed, Evaluate(filter,
            architecture == Architecture.X64 ? 0xc00000b7U : 0xc000003eU, socket));
        if (abi.RejectX32) Assert.Equal(denied, Evaluate(filter, abi.AuditArchitecture, 0x40000000));
    }

    [Fact]
    public async Task Linux_pre_exec_domain_denies_unlisted_files_writes_and_network_but_loads_the_worker()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64))
        {
            Assert.Skip("Linux x64/ARM64 Landlock and seccomp picture-worker boundary.");
            return;
        }
        string root = Path.Combine(Path.GetTempPath(), "filecat-picture-domain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string secret = Path.Combine(root, "secret.txt"), written = Path.Combine(root, "written.txt");
        await File.WriteAllTextAsync(secret, "not available to the decoder", TestContext.Current.CancellationToken);
        var modeBefore = File.GetUnixFileMode(secret);
        var timeBefore = File.GetLastWriteTimeUtc(secret);
        // Landlock leaves metadata to the seccomp filter (review finding R-H3): no chmod, chown, time, truncation or
        // extended attribute change reaches a file the user owns.
        const string probe = """
            import os, socket, sys
            def denied(action, code):
                try:
                    action()
                except OSError:
                    return
                raise SystemExit(code)
            denied(lambda: open(sys.argv[1], 'rb').read(1), 31)
            denied(lambda: open(sys.argv[2], 'wb').write(b'x'), 32)
            denied(lambda: socket.socket(), 33)
            denied(lambda: os.chmod(sys.argv[1], 0o666), 35)
            denied(lambda: os.chown(sys.argv[1], os.getuid(), os.getgid()), 36)
            denied(lambda: os.utime(sys.argv[1], (0, 0)), 37)
            denied(lambda: os.truncate(sys.argv[1], 0), 38)
            denied(lambda: os.setxattr(sys.argv[1], 'user.filecat', b'x'), 39)
            with open(sys.argv[3], 'rb') as allowed:
                if len(allowed.read(1)) != 1:
                    raise SystemExit(34)
            """;
        var start = PictureDecoder.SandboxedProbeStartInfo("/usr/bin/python3",
            ["-c", probe, secret, written, typeof(PictureDecoder).Assembly.Location]);
        start.Environment.Clear();
        start.Environment["DOTNET_EnableDiagnostics"] = "0";
        start.Environment["DOTNET_ROOT"] = LinuxPictureSandbox.RuntimeRoot();
        try
        {
            using var process = Process.Start(start)!;
            string stdout = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            string stderr = await process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            output.WriteLine(JsonSerializer.Serialize(new { process.ExitCode, stdout, stderr, secret, written }));
            Assert.Equal(0, process.ExitCode);
            Assert.False(File.Exists(written));
            Assert.Equal("not available to the decoder", await File.ReadAllTextAsync(secret, TestContext.Current.CancellationToken));
            Assert.Equal(modeBefore, File.GetUnixFileMode(secret));
            Assert.Equal(timeBefore, File.GetLastWriteTimeUtc(secret));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Unsupported_Linux_architecture_is_not_silently_unrestricted() =>
        Assert.Throws<PlatformNotSupportedException>(() => LinuxPictureProcessPolicy.ForArchitecture(Architecture.Arm));

    private static uint Evaluate(LinuxPictureProcessPolicy.Filter[] filter, uint architecture, uint syscall, ulong firstArgument = 0)
    {
        uint accumulator = 0;
        for (int index = 0; index < filter.Length; index++)
        {
            var instruction = filter[index];
            switch (instruction.Code)
            {
                case 0x20:
                    accumulator = instruction.Value switch
                    {
                        0 => syscall, 4 => architecture, 16 => (uint)firstArgument,
                        _ => throw new InvalidOperationException($"Unexpected seccomp_data offset {instruction.Value}."),
                    };
                    break;
                case 0x15:
                    index += accumulator == instruction.Value ? instruction.Yes : instruction.No;
                    break;
                case 0x45:
                    index += (accumulator & instruction.Value) != 0 ? instruction.Yes : instruction.No;
                    break;
                case 0x06:
                    return instruction.Value;
                default:
                    throw new InvalidOperationException($"Unexpected BPF opcode {instruction.Code:x}.");
            }
        }
        throw new InvalidOperationException("The seccomp program returned no decision.");
    }

    private static Dictionary<string, int> Status(string path) => File.ReadAllLines(path)
        .Where(line => line.StartsWith("NoNewPrivs:") || line.StartsWith("Seccomp:") || line.StartsWith("Seccomp_filters:"))
        .ToDictionary(line => line.Split(':')[0], line => int.Parse(line.Split(':')[1].Trim(), CultureInfo.InvariantCulture));

    private static byte[] Pixels() => [21, 43, 65, 255, 91, 17, 39, 255, 5, 129, 9, 255, 71, 53, 15, 255, 28, 46, 85, 255, 0, 255, 127, 255];

    private static byte[] Bmp()
    {
        var bytes = new byte[54 + 24]; bytes[0] = (byte)'B'; bytes[1] = (byte)'M';
        foreach (var (at, value) in new[] { (2, bytes.Length), (10, 54), (14, 40), (18, 3), (22, -2) })
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at), value);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(28), 32);
        Pixels().CopyTo(bytes, 54); return bytes;
    }

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(3, 2, SKColorType.Bgra8888, SKAlphaType.Premul));
        Pixels().CopyTo(bitmap.GetPixelSpan());
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
