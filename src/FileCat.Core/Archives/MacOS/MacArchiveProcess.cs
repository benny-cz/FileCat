using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Archives.MacOS;

/// <summary>
/// The parent side of the macOS archive boundary (route decision of 12 November 2026). It starts the sandbox launcher
/// beside the application with a fixed environment and fixed launch facts, and owns the worker's lifetime: the
/// cancellation pipe, a capped standard error and the memory monitor. macOS gives an ordinary user no hard memory
/// ceiling, so, as the owner's re-scoped gate allows, the parent samples the worker's physical footprint every
/// <see cref="SampleInterval"/> and stops the worker when it exceeds its budget. This is a sampled bound, not a ceiling:
/// between two samples the worker can exceed the budget (by about 12 MiB at 800 MiB/s in the spike).
/// </summary>
internal sealed partial class MacArchiveProcess : IArchiveWorkerProcess
{
    /// <summary>The worker's physical footprint budget, as the Linux worker's memory cgroup limit.</summary>
    internal const long MemoryBudget = 768L << 20;
    internal static readonly TimeSpan SampleInterval = TimeSpan.FromMilliseconds(10);
    private readonly Process process;
    private readonly AnonymousPipeServerStream cancellation;
    private readonly long budget;
    private readonly Task<string> errorDrain;
    private readonly Thread monitor;
    private readonly ManualResetEventSlim stopMonitor = new();
    private readonly object gate = new();
    private Task? disposing;
    private bool disposed;
    private uint finalExit;
    private TimeSpan cpu;
    private int errorExceeded, memoryExceeded;
    private long peak;
    private Exception? stopFailure;

    internal int ProcessId { get; }
    /// <summary>The highest footprint the monitor sampled.</summary>
    internal long PeakFootprint => Interlocked.Read(ref peak);
    internal Task<string> ErrorForQualification => errorDrain;
    public Stream Input => process.StandardInput.BaseStream;
    public Stream Output => process.StandardOutput.BaseStream;
    public bool HasExited { get { lock (gate) return disposed || process.HasExited; } }
    public uint ExitCode { get { lock (gate) return disposed ? finalExit : unchecked((uint)process.ExitCode); } }
    public TimeSpan TotalProcessorTime { get { lock (gate) { if (!disposed) SampleCpu(); return cpu; } } }
    public bool ErrorLimitExceeded => Volatile.Read(ref errorExceeded) != 0;
    public bool MemoryLimitExceeded => Volatile.Read(ref memoryExceeded) != 0;

    private MacArchiveProcess(Process process, AnonymousPipeServerStream cancellation, long budget)
    {
        this.process = process; this.cancellation = cancellation; this.budget = budget; ProcessId = process.Id;
        errorDrain = DrainError();
        monitor = new Thread(Monitor) { IsBackground = true, Name = "FileCat archive memory monitor" };
        monitor.Start();
    }

    /// <summary>
    /// Starts <paramref name="request"/> in the sandboxed worker of the application whose executable is
    /// <paramref name="executable"/> (FileCat, or the boundary tests' probe), with the launcher beside it.
    /// </summary>
    internal static MacArchiveProcess Start(string executable, Guid nonce, TimeSpan cpuLimit, IReadOnlyList<string> request, long memoryBudget = MemoryBudget)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        ArgumentNullException.ThrowIfNull(request);
        if (nonce == Guid.Empty || cpuLimit < TimeSpan.FromSeconds(1) || cpuLimit > TimeSpan.FromHours(2) || memoryBudget < 64L << 20 ||
            request.Count is 0 or > MacArchiveIsolation.MaximumRequest || request.Any(a => a is null || a.Length > 4096 || a.IndexOf('\0') >= 0))
            throw new ArgumentException("A fixed bounded macOS archive launch is required.");
        if (Environment.IsPrivilegedProcess) throw MacArchiveIsolation.Unavailable("Archive workers are not started from an administrator process.");
        var (image, launcher) = ValidateBundle(executable);
        var start = new ProcessStartInfo(launcher)
        {
            WorkingDirectory = Path.GetDirectoryName(launcher)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment.Clear();
        foreach (var (key, value) in new (string, string)[]
        {
            ("DOTNET_EnableDiagnostics", "0"), ("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1"),
            ("DOTNET_GCHeapHardLimit", "18000000"), ("TZ", "UTC"), ("LANG", "C"), ("LC_ALL", "C"), ("PATH", "/nonexistent"),
        }) start.Environment[key] = value;
        foreach (string argument in new[] { MacArchiveIsolation.LauncherArgument, image, nonce.ToString("D"),
            Environment.ProcessId.ToString(CultureInfo.InvariantCulture), checked((int)Math.Ceiling(cpuLimit.TotalSeconds)).ToString(CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(argument);
        foreach (string argument in request) start.ArgumentList.Add(argument);
        var cancellation = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        try
        {
            start.ArgumentList.Add(ArchiveWorkerCancellation.Argument); start.ArgumentList.Add(cancellation.GetClientHandleAsString());
            var process = Process.Start(start) ?? throw MacArchiveIsolation.Unavailable("The macOS archive worker did not start.");
            cancellation.DisposeLocalCopyOfClientHandle();
            return new(process, cancellation, memoryBudget);
        }
        catch { cancellation.Dispose(); throw; }
    }

    /// <summary>
    /// The executable and its launcher: exact local paths in one folder, owned by this user or root, writable by no one else.
    /// </summary>
    internal static (string Image, string Launcher) ValidateBundle(string executable)
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        if (string.IsNullOrEmpty(executable) || !Path.IsPathFullyQualified(executable) || UnixFiles.RealPath(executable) != executable)
            throw MacArchiveIsolation.Unavailable("The macOS archive executable must be an exact local application path.");
        string folder = Path.GetDirectoryName(executable)!, image = Path.GetFileName(executable);
        if (image is not ("FileCat" or "FileCat.WorkerProbe"))
            throw MacArchiveIsolation.Unavailable("The macOS archive worker runs only in FileCat's own executable.");
        string launcher = Path.Combine(folder, MacArchiveIsolation.LauncherFileName);
        uint user = GetUid();
        foreach (string path in new[] { folder, executable, launcher })
        {
            var info = UnixFiles.Stat(path) ?? throw MacArchiveIsolation.Unavailable("The macOS archive worker launcher is missing from this application.");
            if (info.IsLink || (info.Owner != 0 && info.Owner != user) || (info.Mode & 0x12) != 0 || (path == folder ? !info.IsDirectory : !info.IsRegular))
                throw MacArchiveIsolation.Unavailable("The macOS archive worker launcher is not a private part of this application.");
        }
        return (image, launcher);
    }

    public void Kill()
    {
        lock (gate)
        {
            if (disposed) return;
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { } // Exited between the check and the signal.
            catch (System.ComponentModel.Win32Exception ex) { stopFailure ??= ex; }
        }
    }

    public void RequestCancellation() { lock (gate) { if (!disposed) cancellation.Dispose(); } }

    public ValueTask DisposeAsync()
    {
        lock (gate) return new(disposing ??= Finish());
    }

    private async Task Finish()
    {
        Kill();
        await process.WaitForExitAsync().ConfigureAwait(false);
        stopMonitor.Set(); monitor.Join();
        try { await errorDrain.ConfigureAwait(false); }
        finally
        {
            lock (gate) { SampleCpu(); finalExit = unchecked((uint)process.ExitCode); disposed = true; }
            cancellation.Dispose(); process.Dispose(); stopMonitor.Dispose();
        }
        if (stopFailure is not null) throw new IOException("The macOS archive worker could not be stopped.", stopFailure);
    }

    // Caller holds the gate. After exit the last sample stands (the kernel's CPU limit is the hard bound).
    private void SampleCpu()
    {
        try { cpu = process.TotalProcessorTime; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    private unsafe void Monitor()
    {
        ulong* usage = stackalloc ulong[64]; // rusage_info_v4 is 296 bytes
        while (!stopMonitor.Wait(SampleInterval))
        {
            bool sampled = ProcPidRusage(ProcessId, 4 /* RUSAGE_INFO_V4 */, usage) == 0;
            // .NET reaps the child before HasExited reports it, so a sample taken while it has not exited is this
            // worker's own: its process ID cannot have been reused yet.
            lock (gate) { if (disposed || process.HasExited) return; SampleCpu(); }
            if (!sampled) continue;
            long footprint = (long)Math.Min(usage[9] /* ri_phys_footprint */, long.MaxValue);
            long seen = Interlocked.Read(ref peak);
            while (footprint > seen && Interlocked.CompareExchange(ref peak, footprint, seen) != seen) seen = Interlocked.Read(ref peak);
            if (footprint > budget)
            {
                Volatile.Write(ref memoryExceeded, 1);
                Kill();
                return;
            }
        }
    }

    private async Task<string> DrainError()
    {
        byte[] buffer = new byte[4096]; using var retained = new MemoryStream(); int total = 0;
        while (true)
        {
            int count = await process.StandardError.BaseStream.ReadAsync(buffer).ConfigureAwait(false);
            if (count == 0) break;
            int keep = Math.Min(count, Math.Max(0, 16 * 1024 - checked((int)retained.Length)));
            if (keep > 0) retained.Write(buffer, 0, keep);
            total = Math.Min(65_537, total + count);
            if (total > 64 * 1024 && Interlocked.Exchange(ref errorExceeded, 1) == 0) Kill();
        }
        return Encoding.UTF8.GetString(retained.ToArray()); // qualification diagnostics only; never a product reason
    }

    [LibraryImport("libc", EntryPoint = "getuid")]
    private static partial uint GetUid();
    [LibraryImport("libc", EntryPoint = "proc_pid_rusage")]
    private static unsafe partial int ProcPidRusage(int pid, int flavor, void* buffer);
}

/// <summary>The macOS worker roles through <see cref="MacArchiveProcess"/>; see <see cref="Unix.UnixArchiveJobBackend"/>.</summary>
internal static class MacArchiveJobBackend
{
    internal static IArchiveJobBackend Create(ArchiveWriteFormat format, string executable, bool retainArtifacts = false)
    {
        if (!Path.IsPathFullyQualified(executable)) throw new ArgumentException("An absolute application worker is required.", nameof(executable));
        IArchiveWorkerProcess Start(Guid nonce, TimeSpan cpu, string role) => MacArchiveProcess.Start(executable, nonce, cpu, [role, nonce.ToString("D")]);
        return Unix.UnixArchiveJobBackend.Create(format,
            (nonce, cpu) => Start(nonce, cpu, "archive-assess-v2"),
            (nonce, cpu) => Start(nonce, cpu, "archive-transform-v2"),
            (nonce, cpu) => Start(nonce, cpu, "archive-verify-v2"),
            _ => OperatingSystem.IsMacOS() ? ArchiveJobAvailability.Ready : ArchiveJobAvailability.Unqualified, retainArtifacts);
    }
}
