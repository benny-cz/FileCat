using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Linux;

/// <summary>
/// Unregistered Linux worker lane. The launch thread remains alive through reap: Linux parent-death signals follow
/// the creating thread, not merely the managed parent's PID. Native cgroup/pidfd controls never fall back to PID kill.
/// </summary>
internal sealed class LinuxArchiveProcess : IArchiveWorkerProcess
{
    private readonly Process process;
    private readonly LinuxArchiveCgroup group;
    private readonly SafeFileHandle pidfd;
    private readonly Thread owner;
    private readonly ManualResetEventSlim release;
    private readonly Task<string> errorDrain;
    private readonly object gate = new();
    private Task? disposing;
    private bool disposed;
    private uint finalExit;
    private TimeSpan finalCpu;
    private int errorExceeded;
    private Exception? stopFailure;
    internal int ProcessId { get; }
    internal string ControlPath => group.Path;
    internal string FinalMemoryEvents { get; private set; } = "";
    internal string FinalMemoryPeak { get; private set; } = "";
    internal Task<string> ErrorForQualification => errorDrain;
    public Stream Input => process.StandardInput.BaseStream;
    public Stream Output => process.StandardOutput.BaseStream;
    public bool HasExited { get { lock (gate) return disposed || process.HasExited; } }
    public uint ExitCode { get { lock (gate) return disposed ? finalExit : unchecked((uint)process.ExitCode); } }
    public TimeSpan TotalProcessorTime { get { lock (gate) return disposed ? finalCpu : Cpu(); } }
    public bool ErrorLimitExceeded => Volatile.Read(ref errorExceeded) != 0;

    private LinuxArchiveProcess(Process process, LinuxArchiveCgroup group, SafeFileHandle pidfd, Thread owner, ManualResetEventSlim release)
    {
        this.process = process; this.group = group; this.pidfd = pidfd; this.owner = owner; this.release = release;
        ProcessId = process.Id; errorDrain = DrainError();
    }

    internal static LinuxArchiveProcess Start(string executable, Guid nonce, TimeSpan cpuLimit, IReadOnlyList<string> workerArguments, bool qualificationDiagnostics = false)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (nonce == Guid.Empty || cpuLimit < TimeSpan.FromSeconds(1) || cpuLimit > TimeSpan.FromHours(2) ||
            workerArguments.Count > 60 || workerArguments.Any(a => a is null || a.Length > 4096 || a.IndexOf('\0') >= 0))
            throw new ArgumentException("A fixed bounded Linux archive launch is required.");
        var assets = LinuxArchiveIsolation.ValidateBundle(executable);
        var group = LinuxArchiveCgroup.Create(nonce);
        var ready = new TaskCompletionSource<LinuxArchiveProcess>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim(); Thread? owner = null;
        owner = new Thread(() =>
        {
            Process? process = null; SafeFileHandle? pidfd = null;
            try
            {
                var start = new ProcessStartInfo(executable)
                {
                    WorkingDirectory = assets.Root,
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
                if (qualificationDiagnostics) start.Environment["FILECAT_ARCHIVE_LINUX_QUAL_DIAGNOSTICS"] = "1";
                start.ArgumentList.Add(LinuxArchiveIsolation.LauncherArgument); start.ArgumentList.Add(nonce.ToString("D"));
                start.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                start.ArgumentList.Add(checked((int)Math.Ceiling(cpuLimit.TotalSeconds)).ToString(CultureInfo.InvariantCulture));
                foreach (string argument in workerArguments) start.ArgumentList.Add(argument);
                process = Process.Start(start) ?? throw LinuxArchiveNative.Unavailable("The Linux archive launcher did not start.");
                long fd = LinuxArchiveNative.Syscall(434 /* pidfd_open */, process.Id, 0, 0, 0, 0, 0);
                if (fd < 0) throw LinuxArchiveNative.Unavailable("The Linux archive worker could not be bound to a native process descriptor.");
                pidfd = new SafeFileHandle((nint)fd, ownsHandle: true);
                var worker = new LinuxArchiveProcess(process, group, pidfd, owner!, release);
                process.StandardInput.BaseStream.Write(LinuxArchiveIsolation.Authorization(nonce));
                process.StandardInput.BaseStream.Flush();
                ready.SetResult(worker);
                release.Wait();
            }
            catch (Exception ex)
            {
                // No archive input was consumed. Without authorization the fixed trusted launcher exits on EOF.
                // Keep its creating thread alive through that exit; never fall back to a potentially reused PID.
                try { process?.StandardInput.Close(); }
                catch (Exception cleanup) when (cleanup is IOException or InvalidOperationException) { ex = new AggregateException(ex, cleanup); }
                try { group.Kill(); } catch (IOException cleanup) { ex = new AggregateException(ex, cleanup); }
                if (pidfd is not null) LinuxArchiveNative.Syscall(424, pidfd.DangerousGetHandle(), 9, 0, 0, 0, 0);
                try { process?.WaitForExit(); }
                catch (Exception cleanup) when (cleanup is InvalidOperationException or System.ComponentModel.Win32Exception) { ex = new AggregateException(ex, cleanup); }
                pidfd?.Dispose(); process?.Dispose();
                try { group.Dispose(); } catch (IOException cleanup) { group.RetainForReview(); ex = new AggregateException(ex, cleanup); }
                ready.TrySetException(ex);
            }
        })
        { IsBackground = true, Name = "FileCat archive process owner" };
        try { owner.Start(); return ready.Task.GetAwaiter().GetResult(); }
        catch { release.Dispose(); group.Dispose(); throw; }
    }

    public void Kill()
    {
        lock (gate)
        {
            if (disposed) return;
            try { group.Kill(); } catch (IOException ex) { stopFailure ??= ex; }
            // Covers the trusted launcher before it attaches, and an already-exited child without PID-reuse risk.
            if (LinuxArchiveNative.Syscall(424 /* pidfd_send_signal */, pidfd.DangerousGetHandle(), 9, 0, 0, 0, 0) != 0 && Marshal.GetLastPInvokeError() != 3)
                stopFailure ??= LinuxArchiveNative.Unavailable("The archive worker could not be stopped through its process descriptor.");
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate) return new(disposing ??= Finish());
    }

    private async Task Finish()
    {
        Kill();
        await process.WaitForExitAsync().ConfigureAwait(false);
        Exception? failure = null;
        try
        {
            await errorDrain.ConfigureAwait(false);
            lock (gate)
            {
                finalExit = unchecked((uint)process.ExitCode); finalCpu = Cpu();
                FinalMemoryEvents = group.ReadControl("memory.events"); FinalMemoryPeak = group.ReadControl("memory.peak");
                group.Dispose(); disposed = true;
                if (stopFailure is not null) failure = stopFailure;
            }
        }
        catch (Exception ex)
        {
            failure = ex; group.RetainForReview();
        }
        finally
        {
            // Keep the creator alive until the kernel child is reaped, including when resource-fact/cleanup review
            // fails. The controller's exact retained path remains available to the caller in that failure case.
            lock (gate) { finalExit = unchecked((uint)process.ExitCode); disposed = true; }
            release.Set(); owner.Join(); release.Dispose(); pidfd.Dispose(); process.Dispose();
        }
        if (failure is not null) throw new IOException("The archive worker stopped with an unresolved native-control failure: " + ControlPath, failure);
    }

    private TimeSpan Cpu()
    {
        foreach (string line in group.ReadControl("cpu.stat").Split('\n'))
            if (line.StartsWith("usage_usec ", StringComparison.Ordinal) &&
                long.TryParse(line.AsSpan(11), NumberStyles.None, CultureInfo.InvariantCulture, out long microseconds) && microseconds >= 0)
                return TimeSpan.FromTicks(checked(microseconds * 10));
        throw LinuxArchiveNative.Unavailable("The archive worker's native CPU accounting is unavailable.");
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
}
