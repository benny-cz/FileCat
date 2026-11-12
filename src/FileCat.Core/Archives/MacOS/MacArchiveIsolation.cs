using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace FileCat.Core.Archives.MacOS;

/// <summary>
/// The worker side of the macOS archive boundary (route decision of 12 November 2026; MACOS_SANDBOX_WORKER_SPIKE.md). The
/// parent starts <see cref="LauncherFileName"/>, a C launcher signed with only the App Sandbox entitlement. The launcher
/// applies what the sandbox leaves open (no new processes, no core files, the CPU allowance, only the parent's pipes, no
/// bootstrap port) and executes this application in worker mode. Before the role reads one byte, the worker checks every
/// one of those facts itself, and that the sandbox actually refuses its user's home folder and a network socket. It
/// refuses to run when any check fails, so a build whose launcher lost its entitlement fails closed. It then watches its
/// parent and exits at once when the parent does. No archive byte is consumed by the launcher or by these checks.
/// </summary>
internal static unsafe partial class MacArchiveIsolation
{
    internal const string LauncherArgument = "--archive-mac-launch", WorkerArgument = "--archive-mac-worker";
    internal const string LauncherFileName = "FileCatArchiveWorker";
    internal const string ContainerIdentifier = "io.github.benny-cz.filecat.archive-worker";
    internal const int MaximumRequest = 16;
    internal readonly record struct Startup(Guid Nonce, int Parent, int CpuSeconds, string[] Request, int CancellationDescriptor);

    /// <summary>The worker's command line after the launcher: mode, nonce, parent, CPU seconds, request…, cancellation.</summary>
    internal static Startup Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length < 7 || args.Length > 6 + MaximumRequest || args[0] != WorkerArgument ||
            args.Any(a => a is null || a.Length > 4096 || a.IndexOf('\0') >= 0) ||
            !Guid.TryParseExact(args[1], "D", out Guid nonce) || nonce == Guid.Empty || args[1] != nonce.ToString("D") ||
            !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int parent) || parent <= 1 ||
            args[2] != parent.ToString(CultureInfo.InvariantCulture) ||
            !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int cpu) || cpu is < 1 or > 7200 ||
            args[3] != cpu.ToString(CultureInfo.InvariantCulture))
            throw new ArgumentException("Invalid macOS archive startup facts.");
        long cancellation = ArchiveWorkerCancellation.Parse(args[^2], args[^1]);
        if (cancellation > 1023) throw new ArgumentException("Invalid macOS archive cancellation descriptor.");
        return new(nonce, parent, cpu, args[4..^2], (int)cancellation);
    }

    /// <summary>Checks the boundary, starts the parent watch, then runs <paramref name="action"/>; throws on any missing fact.</summary>
    internal static int RunWorker(string[] args, Func<Startup, int> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException();
        var startup = Parse(args);
        if (GetParentPid() != startup.Parent) throw Unavailable("The archive worker's parent does not match its startup facts.");
        VerifyEnvironment();
        VerifyLimits(startup.CpuSeconds);
        VerifyBootstrap();
        VerifyDescriptors(startup.CancellationDescriptor);
        VerifyDenials();
        WatchParent(startup.Parent);
        return action(startup);
    }

    internal static ArchiveWriteException Unavailable(string message) => new(ArchiveWriteReason.WorkerUnavailable, message);

    private static void VerifyEnvironment()
    {
        // The system sets this while it applies the sandbox; the denials below prove that the sandbox is in force.
        if (Environment.GetEnvironmentVariable("APP_SANDBOX_CONTAINER_ID") != ContainerIdentifier)
            throw Unavailable("The archive worker is not inside its App Sandbox container.");
        foreach (string key in Environment.GetEnvironmentVariables().Keys)
            if (key.StartsWith("DYLD_", StringComparison.Ordinal) || key.StartsWith("CORECLR_", StringComparison.Ordinal) ||
                key.StartsWith("COMPlus_", StringComparison.Ordinal) || key.StartsWith("DOTNET_STARTUP_", StringComparison.Ordinal) ||
                key.StartsWith("DOTNET_ADDITIONAL_", StringComparison.Ordinal) || key.StartsWith("DOTNET_SHARED_STORE", StringComparison.Ordinal))
                throw Unavailable("The archive runtime inherited an unsupported loader environment.");
    }

    private static void VerifyLimits(int cpuSeconds)
    {
        RLimit limit;
        if (GetRlimit(0 /* CPU */, &limit) != 0 || limit.Current != (ulong)cpuSeconds || limit.Maximum != (ulong)cpuSeconds)
            throw Unavailable("The archive worker has no exact finite CPU limit.");
        if (GetRlimit(7 /* NPROC */, &limit) != 0 || limit.Current != 0 || limit.Maximum != 0)
            throw Unavailable("The archive worker could still start processes.");
        if (GetRlimit(4 /* CORE */, &limit) != 0 || limit.Current != 0 || limit.Maximum != 0)
            throw Unavailable("The archive worker could still write a core file.");
    }

    private static void VerifyBootstrap()
    {
        if (TaskGetSpecialPort(TaskSelfTrap(), 4 /* TASK_BOOTSTRAP_PORT */, out uint port) != 0 || port != 0)
            throw Unavailable("The archive worker could still reach system services.");
    }

    // Standard streams and the cancellation descriptor are the parent's pipes; anything else open is the runtime's own
    // (close-on-exec). The launcher closed every other inherited descriptor; this checks it did.
    private static void VerifyDescriptors(int cancellation)
    {
        var descriptors = OpenDescriptors();
        foreach (int fd in new[] { 0, 1, 2, cancellation })
            if (!descriptors.Contains(fd) || !IsPipe(fd)) throw Unavailable("The archive worker's streams must be its parent's pipes.");
        int flags = Fcntl(cancellation, 3 /* F_GETFL */, 0);
        if (flags < 0 || (flags & 3 /* O_ACCMODE */) != 0 /* O_RDONLY */) throw Unavailable("The archive cancellation descriptor must be read-only.");
        foreach (int fd in descriptors)
        {
            if (fd <= 2 || fd == cancellation) continue;
            int descriptorFlags = Fcntl(fd, 1 /* F_GETFD */, 0);
            if (descriptorFlags < 0) { if (Marshal.GetLastPInvokeError() == 9 /* EBADF */) continue; throw Unavailable("The archive descriptor flags are unavailable."); }
            if ((descriptorFlags & 1 /* FD_CLOEXEC */) == 0) throw Unavailable("The archive runtime inherited an unapproved descriptor.");
        }
    }

    internal static int[] OpenDescriptors()
    {
        const int Maximum = 1024;
        ProcFdInfo* buffer = stackalloc ProcFdInfo[Maximum];
        int bytes = ProcPidInfo(Environment.ProcessId, 1 /* PROC_PIDLISTFDS */, 0, buffer, Maximum * sizeof(ProcFdInfo));
        if (bytes <= 0 || bytes >= Maximum * sizeof(ProcFdInfo)) throw Unavailable("The archive descriptor inventory is unavailable.");
        int[] result = new int[bytes / sizeof(ProcFdInfo)];
        for (int i = 0; i < result.Length; i++) result[i] = buffer[i].Descriptor;
        return result;
    }

    internal static bool IsPipe(int fd)
    {
        byte* facts = stackalloc byte[256];
        int result = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? Fstat(fd, facts) : FstatInode64(fd, facts);
        return result == 0 && (*(ushort*)(facts + 4) & 0xF000) == 0x1000; // st_mode: S_IFIFO
    }

    // The sandbox in force: the user's own home folder and a loopback socket are refused. HOME is the container's
    // folder below the user's home; with no bootstrap port the directory service cannot be asked for the real one.
    private static void VerifyDenials()
    {
        const string Suffix = "/Library/Containers/" + ContainerIdentifier + "/Data";
        string? home = Environment.GetEnvironmentVariable("HOME");
        if (home is null || home.Length <= Suffix.Length + 1 || !home.EndsWith(Suffix, StringComparison.Ordinal) || home[0] != '/')
            throw Unavailable("The archive worker's container folder is not where the sandbox puts it.");
        string user = home[..^Suffix.Length];
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(user).GetEnumerator();
            entries.MoveNext();
            throw Unavailable("The archive worker can read its user's home folder.");
        }
        catch (UnauthorizedAccessException) { }
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
            throw Unavailable("The archive worker can open network sockets.");
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied) { }
        catch (SocketException) { throw Unavailable("The archive worker's network denial could not be established."); }
    }

    // Exits as soon as the parent does, even when the role is busy or blocked: the parent's own stop then cannot reach it.
    private static void WatchParent(int parent)
    {
        int queue = Kqueue();
        if (queue < 0) throw Unavailable("The archive worker could not watch its parent.");
        var change = new KEvent { Ident = (nuint)parent, Filter = -5 /* EVFILT_PROC */, Flags = 0x0001 | 0x0010 /* EV_ADD | EV_ONESHOT */,
            FilterFlags = 0x80000000 /* NOTE_EXIT */ };
        if (Kevent(queue, &change, 1, null, 0, null) != 0) throw Unavailable("The archive worker could not watch its parent.");
        if (GetParentPid() != parent) Exit(70); // The parent ended before the watch was in place.
        new Thread(() =>
        {
            KEvent fired;
            while (Kevent(queue, null, 0, &fired, 1, null) < 0 && Marshal.GetLastPInvokeError() == 4 /* EINTR */) { }
            Exit(70);
        })
        { IsBackground = true, Name = "FileCat archive parent watch" }.Start();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RLimit { internal ulong Current, Maximum; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEvent { internal nuint Ident; internal short Filter; internal ushort Flags; internal uint FilterFlags; internal nint Data; internal nint UserData; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcFdInfo { internal int Descriptor; internal uint Type; }
    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec { internal long Seconds, Nanoseconds; }

    [LibraryImport("libc", EntryPoint = "getppid")]
    internal static partial int GetParentPid();
    [LibraryImport("libc", EntryPoint = "getrlimit", SetLastError = true)]
    internal static partial int GetRlimit(int resource, RLimit* limit);
    [LibraryImport("libc", EntryPoint = "task_self_trap")]
    internal static partial uint TaskSelfTrap();
    [LibraryImport("libc", EntryPoint = "task_get_special_port")]
    internal static partial int TaskGetSpecialPort(uint task, int which, out uint port);
    [LibraryImport("libc", EntryPoint = "proc_pidinfo", SetLastError = true)]
    private static partial int ProcPidInfo(int pid, int flavor, ulong argument, void* buffer, int size);
    // fcntl is variadic: on Apple arm64 its third argument travels on the stack, which this fixed signature does not
    // provide. Only F_GETFD and F_GETFL are used here; both ignore that argument.
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command, int argument);
    [LibraryImport("libc", EntryPoint = "fstat")]
    private static partial int Fstat(int fd, byte* buffer);
    [LibraryImport("libc", EntryPoint = "fstat$INODE64")]
    private static partial int FstatInode64(int fd, byte* buffer);
    [LibraryImport("libc", EntryPoint = "kqueue", SetLastError = true)]
    private static partial int Kqueue();
    [LibraryImport("libc", EntryPoint = "kevent", SetLastError = true)]
    private static partial int Kevent(int queue, KEvent* changes, int changeCount, KEvent* events, int eventCount, TimeSpec* timeout);
    [LibraryImport("libc", EntryPoint = "_exit")]
    private static partial void Exit(int status);
}
