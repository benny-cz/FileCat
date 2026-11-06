using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using FileCat.Archives.Writing;
using FileCat.Core.Archives;

if (args is ["guest"])
{
    if (!OperatingSystem.IsLinux() || Native.getuid() != 1000 || Native.geteuid() != 1000 || Native.getppid() != 1 ||
        Native.prctl(39, 0, 0, 0, 0) != 1 || File.ReadAllText("/sys/fs/cgroup/filecat/memory.max").Trim() != "1073741824" ||
        File.ReadAllText("/sys/fs/cgroup/filecat/memory.swap.max").Trim() != "0" ||
        File.ReadAllText("/sys/fs/cgroup/filecat/pids.max").Trim() != "128" ||
        File.ReadAllText("/proc/self/cgroup").Trim() != "0::/filecat") return 65;
    bool denied = false;
    try { using var attempt = File.OpenWrite("/sys/fs/cgroup/filecat/memory.max"); }
    catch (UnauthorizedAccessException) { denied = true; }
    if (!denied) return 65;
    // Console.OpenStandardInput uses the interactive Unicode line reader for a tty. Preserve arbitrary
    // archive/protocol bytes with raw descriptor streams, after native init has disabled terminal processing.
    using var diagnostics = new FileStream(new SafeFileHandle((nint)2, ownsHandle: false), FileAccess.Write);
    diagnostics.Write(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { guestWorker = true, uid = Native.getuid(), runtime = Environment.Version.ToString(), memoryLimit = 1073741824, limitRaiseDenied = denied }) + "\n"));
    diagnostics.Flush();
    return GuestCancellation.Run(diagnostics, (input, output, ct) =>
    {
    output.Write("FCVMRDY1"u8); output.Flush();
    byte[] header = new byte[32]; input.ReadExactly(header);
    if (!header.AsSpan(0, 8).SequenceEqual("FCVMIPC1"u8) || header.AsSpan(25).IndexOfAnyExcept((byte)0) >= 0) return 64;
    Guid nonce = new(header.AsSpan(8, 16), bigEndian: true);
    if (nonce == Guid.Empty) return 64;
    return header[24] switch
    {
        1 => ArchiveAssessmentWorker.RunAfterIsolation(input, output, nonce, ct),
        2 => ArchiveTransformWorker.RunAfterIsolation(input, output, nonce, ct),
        3 => ArchiveAssessmentWorker.RunVerificationAfterIsolation(input, output, nonce, ct),
        4 => SpinProbe(output),
        5 => FloodProbe(output, ct),
        _ => 64
    };
    });
}
static int SpinProbe(Stream output)
{
    output.Write("FCVMBUSY"u8); output.Flush();
    // Controlled adversarial lifetime probe: no protocol reads or EOF-based exit after this marker.
    while (true) Thread.SpinWait(100000);
}
static int FloodProbe(Stream output, CancellationToken ct)
{
    output.Write("FCVMFLOD"u8); output.Flush();
    byte[] bytes = new byte[65524];
    while (true) { ct.ThrowIfCancellationRequested(); output.Write(bytes); }
}
if (args is ["cancel-host", var cancelNative, var cancelKernel, var cancelInitrd, var cancelResults] && OperatingSystem.IsMacOS())
    return await CancellationProbes.Run(cancelNative, cancelKernel, cancelInitrd, cancelResults);
if (args is not ["host", var native, var kernel, var initrd, var results] || !OperatingSystem.IsMacOS()) return 64;
Directory.CreateDirectory(results);
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
var workers = new List<VmProcess>();
var outcomes = new List<object>();
try
{
foreach (var format in new[] { ArchiveWriteFormat.Tar, ArchiveWriteFormat.TarGzip, ArchiveWriteFormat.TarBzip2, ArchiveWriteFormat.TarZstd })
{
    byte[] before = new byte[131072], after = new byte[196608];
    new Random(42829297).NextBytes(before); new Random(42829298).NextBytes(after);
    var profile = new ArchiveWriteProfile(format);
    string digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    ArchiveTransformInput descriptor(int id, byte[] bytes) => new(id, bytes.Length, digest(bytes), DateTimeOffset.UnixEpoch);
    VmProcess Launch(byte role, Guid nonce, TimeSpan cpu)
    {
        if (cpu < TimeSpan.FromSeconds(30)) throw new InvalidOperationException("The probe uses a fixed 30-second guest CPU ceiling.");
        var worker = new VmProcess(native, kernel, initrd, nonce, role, Path.Combine(results, $"{format}-{workers.Count:D2}.log"));
        workers.Add(worker); return worker;
    }
    var create = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(profile, null, [descriptor(19, before)], [],
        [new("documents/界.txt", ArchiveMemberType.File, 19)], deadline.Token), deadline.Token);
    using var initial = new MemoryStream();
    await new ArchiveTransformClient((nonce, cpu) => Launch(2, nonce, cpu)).RebuildAsync(create, initial,
        () => throw new InvalidOperationException("No source grant exists for creation."), id => id == 19 ? new MemoryStream(before, false) : throw new InvalidOperationException(), deadline.Token);
    byte[] source = initial.ToArray();
    var assessed = await new ArchiveAssessmentClient((nonce, cpu) => Launch(1, nonce, cpu)).AssessAsync(new(profile, source.Length, digest(source)),
        () => new MemoryStream(source, false), deadline.Token);
    var update = ArchiveTransformWire.Prepare(ArchiveTarTransformPlan.Create(assessed, [descriptor(20, after)],
        [new(ArchiveChangeKind.Replace, new(assessed.CatalogRevision, "documents/界.txt", ArchiveMemberType.File, 0), inputId: 20)], [], deadline.Token), deadline.Token);
    using var stage = new MemoryStream();
    await new ArchiveTransformClient((nonce, cpu) => Launch(2, nonce, cpu)).RebuildAsync(update, stage, () => new MemoryStream(source, false),
        id => id == 20 ? new MemoryStream(after, false) : throw new InvalidOperationException(), deadline.Token);
    byte[] output = stage.ToArray();
    var verified = await new ArchiveVerificationClient((nonce, cpu) => Launch(3, nonce, cpu)).VerifyAsync(new("owned-vm-stage", output.Length, digest(output)),
        update.Expected, () => new MemoryStream(output, false), deadline.Token);
    if (verified.ManifestDigest != update.Expected.Digest) throw new InvalidOperationException("Fresh verification differs.");
    File.WriteAllBytes(Path.Combine(results, format + ".archive"), output);
    File.WriteAllBytes(Path.Combine(results, format + ".expected"), after);
    outcomes.Add(new { format = format.ToString(), sourceBytes = source.Length, stageBytes = output.Length, sha256 = digest(output), manifest = verified.ManifestDigest });
}
if (workers.Count != 16 || workers.Select(w => w.Id).Distinct().Count() != 16 || workers.Any(w => !w.Reaped || w.ExitCode != 0 || w.ErrorLimitExceeded))
    throw new InvalidOperationException("Every separate role must succeed and be reaped.");
File.WriteAllText(Path.Combine(results, "facts.json"), JsonSerializer.Serialize(new { experimentPassed = true, productionBackendQualified = false,
    outcomes, lifetimes = workers.Select(w => new { pid = w.Id, reaped = w.Reaped, exit = w.ExitCode }) }, new JsonSerializerOptions { WriteIndented = true }));
return 0;
}
catch (Exception ex)
{
    File.WriteAllText(Path.Combine(results, "failure.txt"), ex.ToString());
    return 1;
}
finally
{
    foreach (var worker in workers) await worker.DisposeAsync();
}

// Development transport only: cancellation stops the whole VM; CPU accounting/host memory and release signing
// are not qualified. No caller in the application references this factory.
sealed class VmProcess : IArchiveWorkerProcess
{
    readonly Process process;
    readonly Task diagnostics;
    bool overflow;
    AnonymousPipeServerStream? cancellation;
    TimeSpan cpu;
    uint exit;
    public bool Reaped { get; private set; }
    public int Id { get; }
    public Stream Input { get; }
    public Stream Output { get; }
    public VmProcess(string native, string kernel, string initrd, Guid nonce, byte role, string log)
    {
        using (var census = Process.Start(new ProcessStartInfo("/bin/ps", "-axo pid=,comm=") { RedirectStandardOutput = true, UseShellExecute = false }) ?? throw new IOException("Census failed."))
        {
            string listing = census.StandardOutput.ReadToEnd(); census.WaitForExit();
            if (census.ExitCode != 0) throw new IOException("Census failed.");
            foreach (string row in listing.Split('\n'))
            {
                var fields = row.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length != 2 || !int.TryParse(fields[0], out int pid) || pid == Environment.ProcessId) continue;
                string name = Path.GetFileName(fields[1]);
                if (name.StartsWith("FileCat", StringComparison.Ordinal) || name.StartsWith("testhost", StringComparison.Ordinal) || name is "dotnet" or "filecat")
                    throw new IOException("Another tester is active; postpone: " + row.Trim());
            }
        }
        var trace = new FileStream(log + ".protocol.bin", FileMode.CreateNew, FileAccess.Write);
        FileStream diagnosticFile;
        try { diagnosticFile = new FileStream(log, FileMode.CreateNew, FileAccess.Write); }
        catch { trace.Dispose(); throw; }
        var start = new ProcessStartInfo(native) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add(kernel); start.ArgumentList.Add(initrd); start.ArgumentList.Add("1536");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            cancellation = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            start.ArgumentList.Add(cancellation.GetClientHandleAsString());
            process = Process.Start(start) ?? throw new IOException("VM helper did not start.");
            cancellation.DisposeLocalCopyOfClientHandle();
        }
        catch { trace.Dispose(); diagnosticFile.Dispose(); cancellation?.Dispose(); throw; }
        Id = process.Id;
        Input = process.StandardInput.BaseStream; Output = new PrefixTrace(process.StandardOutput.BaseStream, trace);
        diagnostics = Task.Run(async () =>
        {
            using var file = diagnosticFile;
            byte[] buffer = new byte[4096]; long bytes = 0;
            while (true)
            {
                int count = await process.StandardError.BaseStream.ReadAsync(buffer); if (count == 0) return;
                if (bytes + count > 1048576) { Volatile.Write(ref overflow, true); Kill(); return; }
                await file.WriteAsync(buffer.AsMemory(0, count)); bytes += count;
            }
        });
        try
        {
            byte[] ready = new byte[8];
            using var readyDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            Output.ReadExactlyAsync(ready, readyDeadline.Token).AsTask().GetAwaiter().GetResult();
            if (!ready.AsSpan().SequenceEqual("FCVMRDY1"u8)) throw new IOException("The guest did not establish its raw protocol channel.");
            byte[] header = new byte[32]; "FCVMIPC1"u8.CopyTo(header); nonce.TryWriteBytes(header.AsSpan(8, 16), bigEndian: true, out _); header[24] = role;
            Input.Write(header); Input.Flush();
        }
        catch { DisposeAsync().AsTask().GetAwaiter().GetResult(); throw; }
    }
    public bool HasExited => Reaped || process.HasExited;
    public uint ExitCode => Reaped ? exit : unchecked((uint)process.ExitCode);
    public TimeSpan TotalProcessorTime
    {
        get
        {
            // macOS cannot query this Process API after exit. Retain the last observed helper-only sample;
            // guest/VM aggregate CPU accounting remains an explicit unqualified production gate.
            if (!Reaped)
                try { cpu = process.TotalProcessorTime; }
                catch (InvalidOperationException) when (process.HasExited) { }
            return cpu;
        }
    }
    public bool ErrorLimitExceeded => Volatile.Read(ref overflow);
    public void RequestCancellation() => Interlocked.Exchange(ref cancellation, null)?.Dispose();
    public void Kill()
    {
        if (Reaped) return;
        try { if (!process.HasExited) process.Kill(); }
        catch (InvalidOperationException) when (process.HasExited) { }
    }
    public async ValueTask DisposeAsync()
    {
        if (Reaped) return;
        try { Kill(); await process.WaitForExitAsync(); await diagnostics; }
        finally
        {
            RequestCancellation();
            if (process.HasExited) { exit = unchecked((uint)process.ExitCode); Reaped = true; }
            Input.Dispose(); Output.Dispose(); process.Dispose();
        }
    }
}
static class Native
{
    [DllImport("libc")] internal static extern uint getuid();
    [DllImport("libc")] internal static extern uint geteuid();
    [DllImport("libc")] internal static extern int getppid();
    [DllImport("libc")] internal static extern int prctl(int option, nuint a, nuint b, nuint c, nuint d);
}

sealed class PrefixTrace(Stream source, FileStream trace) : Stream
{
    int retained;
    void Save(ReadOnlySpan<byte> bytes)
    {
        int count = Math.Min(bytes.Length, 512 - retained);
        if (count > 0) { trace.Write(bytes[..count]); trace.Flush(); retained += count; }
    }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer) { int count = source.Read(buffer); Save(buffer[..count]); return count; }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    { int count = await source.ReadAsync(buffer, ct); Save(buffer.Span[..count]); return count; }
    public override bool CanRead => true;
    public override bool CanWrite => false;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) { trace.Dispose(); source.Dispose(); } base.Dispose(disposing); }
}
