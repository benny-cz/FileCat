using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileCat.App.Services;

/// <summary>
/// Starts the Linux picture worker inside a filesystem and network domain before the .NET worker process exists.
/// The small trusted launcher applies Landlock and seccomp, then replaces itself with the normal FileCat worker.
/// Applying the policy before <c>execv</c> means every runtime thread created by the worker inherits it, including on
/// kernels whose Landlock ABI predates thread synchronization.
/// </summary>
internal static unsafe partial class LinuxPictureSandbox
{
    internal const string Argument = "--picture-sandbox";

    private const ulong Execute = 1UL << 0;
    private const ulong ReadFile = 1UL << 2;
    private const ulong ReadDirectory = 1UL << 3;
    private const ulong Refer = 1UL << 13;
    private const ulong Truncate = 1UL << 14;
    private const ulong IoctlDevice = 1UL << 15;
    private const ulong ResolveUnix = 1UL << 16;
    private const ulong ScopeAbstractUnixSocket = 1UL << 0;
    private const ulong ScopeSignal = 1UL << 1;
    private const uint LandlockCreateRulesetVersion = 1;
    private const int LandlockRulePathBeneath = 1;
    private const int OpenPath = 0x200000;
    private const int OpenCloseOnExec = 0x80000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RulesetAttributes
    {
        public ulong HandledAccessFileSystem;
        public ulong HandledAccessNetwork;
        public ulong Scoped;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PathBeneathAttributes
    {
        public ulong AllowedAccess;
        public int ParentFileDescriptor;
    }

    /// <summary>Wrap one trusted child command with FileCat's own sandbox-launch mode.</summary>
    internal static ProcessStartInfo StartInfo(string helperExecutable, IReadOnlyList<string> helperArguments,
        string childExecutable, IReadOnlyList<string> childArguments)
    {
        int worker = -1;
        for (int i = 0; i < helperArguments.Count; i++)
            if (helperArguments[i] == PictureWorker.Argument) { worker = i; break; }
        if (worker < 0) throw new InvalidOperationException("The picture-worker launch has no worker argument.");

        var start = new ProcessStartInfo(helperExecutable)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // A framework-dependent launch starts with FileCat.dll; an apphost has no prefix.
        for (int i = 0; i < worker; i++) start.ArgumentList.Add(helperArguments[i]);
        start.ArgumentList.Add(Argument);
        start.ArgumentList.Add(childExecutable);
        foreach (string argument in childArguments) start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>Launcher entry point. Success never returns because this process becomes the picture worker.</summary>
    internal static int Run(string[] args)
    {
        using var output = Console.OpenStandardOutput();
        try
        {
            if (!OperatingSystem.IsLinux() || args.Length < 2 || string.IsNullOrEmpty(args[1]))
                throw new IOException("The Linux picture sandbox received an invalid worker command.");
            Apply();
            Exec(args[1], args.AsSpan(2));
            throw new IOException($"The Linux picture sandbox could not start its worker ({Marshal.GetLastPInvokeError()}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return PictureWorker.Fail(output, ex.Message);
        }
    }

    /// <summary>Apply before replacing this trusted single entry thread with the actual worker.</summary>
    private static void Apply()
    {
        var abi = LinuxPictureProcessPolicy.ForArchitecture(RuntimeInformation.ProcessArchitecture);
        CloseInheritedOnExec(abi);
        LinuxPictureProcessPolicy.SetNoNewPrivileges("filesystem and network access");
        ApplyFileSystemPolicy();
        // execv keeps the process ID, so the worker is the process this one is.
        LinuxPictureProcessPolicy.ApplyFilter(abi, BuildPreExecFilter(abi, (uint)Environment.ProcessId), synchronizeThreads: false,
            "The picture decoder could not restrict network access and file metadata changes.");
    }

    /// <summary>
    /// Marks every descriptor but the standard three, and the lifetime pipe FileCat gives the worker, close-on-exec
    /// (review finding R-M6). One this launcher inherited without it, such as the app's X11 or D-Bus connection or an
    /// open file, would otherwise reach the worker, past Landlock, which checks only what is opened.
    /// </summary>
    private static void CloseInheritedOnExec(LinuxPictureProcessPolicy.Abi abi)
    {
        const int CloseRange = 436, CloseRangeCloexec = 4, SetDescriptorFlags = 2, CloseOnExec = 1;
        int fcntl = abi.AuditArchitecture == 0xc000003e ? 72 : 25;
        if (Syscall(CloseRange, 3, unchecked((nint)uint.MaxValue), CloseRangeCloexec, 0, 0, 0) != 0)
        {
            // Before Linux 5.11, which has no CLOSE_RANGE_CLOEXEC: each descriptor open now, from /proc/self/fd.
            foreach (string entry in Directory.GetFileSystemEntries("/proc/self/fd"))
                if (int.TryParse(Path.GetFileName(entry), out int descriptor) && descriptor > 2)
                    Syscall(fcntl, descriptor, SetDescriptorFlags, CloseOnExec, 0, 0, 0);
        }
        if (int.TryParse(Environment.GetEnvironmentVariable(PictureWorkerLifetime.EnvironmentName), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int lifetime) && lifetime > 2)
            Syscall(fcntl, lifetime, SetDescriptorFlags, 0, 0, 0, 0);
    }

    private static void ApplyFileSystemPolicy()
    {
        long queried = Syscall(444, 0, 0, (nint)LandlockCreateRulesetVersion, 0, 0, 0);
        if (queried < 1) Refuse("The picture decoder requires an enabled Landlock filesystem policy.");
        int abi = checked((int)queried);
        ulong handled = (1UL << 13) - 1; // ABI 1: execute through make-symlink.
        if (abi >= 2) handled |= Refer;
        if (abi >= 3) handled |= Truncate;
        if (abi >= 5) handled |= IoctlDevice;
        if (abi >= 9) handled |= ResolveUnix;
        var attributes = new RulesetAttributes
        {
            HandledAccessFileSystem = handled,
            // No rules are added: supported TCP and UDP bind/connect/send operations are denied as a second layer.
            HandledAccessNetwork = abi >= 10 ? 0xfUL : abi >= 4 ? 0x3UL : 0,
            // From ABI 6 (Linux 6.12): no signal to, and no abstract Unix socket of, any process outside this domain (review
            // finding R-M6). The seccomp filter keeps signals to this process on older kernels too.
            Scoped = abi >= 6 ? ScopeAbstractUnixSocket | ScopeSignal : 0,
        };
        long ruleset;
        ruleset = Syscall(444, (nint)(&attributes), sizeof(RulesetAttributes), 0, 0, 0, 0);
        if (ruleset < 0) Refuse("The picture decoder could not create its Landlock ruleset.");
        int descriptor = checked((int)ruleset);
        try
        {
            foreach (string path in ReadRoots()) AddReadRule(descriptor, handled, path);
            if (Syscall(446, descriptor, 0, 0, 0, 0, 0) != 0)
                Refuse("The picture decoder could not enforce its Landlock ruleset.");
        }
        finally { Close(descriptor); }
    }

    private static IEnumerable<string> ReadRoots()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal)
        {
            Path.GetDirectoryName(typeof(PictureWorker).Assembly.Location)!,
            RuntimeRoot(),
            "/usr", "/lib", "/lib64",
            "/proc/self", "/sys/devices/system/cpu", "/sys/fs/cgroup",
            "/dev/null", "/dev/urandom", "/dev/random",
            "/etc/ld.so.cache", "/etc/localtime", "/etc/fonts", "/var/cache/fontconfig",
        };
        return paths.Where(path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)));
    }

    internal static string RuntimeRoot()
    {
        var runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        string application = Path.GetDirectoryName(typeof(PictureWorker).Assembly.Location)!;
        // A self-contained worker carries CoreCLR beside the application rather than below a dotnet root.
        if (File.Exists(Path.Combine(application, "libcoreclr.so"))) return application;
        var framework = runtimeDirectory.Parent;
        var shared = framework?.Parent;
        var root = shared?.Parent;
        if (framework?.Name == "Microsoft.NETCore.App" && shared?.Name == "shared" && root is not null &&
            File.Exists(Path.Combine(root.FullName, "dotnet"))) return root.FullName;
        // An extracted single-file worker can keep CoreCLR in its private runtime directory.
        if (File.Exists(Path.Combine(runtimeDirectory.FullName, "libcoreclr.so"))) return runtimeDirectory.FullName;
        throw new IOException("The .NET runtime root for the picture sandbox could not be verified.");
    }

    private static void AddReadRule(int ruleset, ulong handled, string path)
    {
        int parent = Open(path, OpenPath | OpenCloseOnExec);
        if (parent < 0) Refuse($"The picture decoder could not admit a required runtime path ({path}).");
        try
        {
            ulong allowed = (ReadFile | Execute) & handled;
            if (Directory.Exists(path)) allowed |= ReadDirectory & handled;
            var attributes = new PathBeneathAttributes { AllowedAccess = allowed, ParentFileDescriptor = parent };
            if (Syscall(445, ruleset, LandlockRulePathBeneath, (nint)(&attributes), 0, 0, 0) != 0)
                Refuse($"The picture decoder could not admit a required runtime path ({path}).");
        }
        finally { Close(parent); }
    }

    /// <summary>
    /// The launcher's seccomp filter, which the worker inherits: no network, no io_uring (whose operations would bypass
    /// this filter), and no change to any file's metadata. Landlock governs no chmod, chown, extended attribute or time
    /// change, and a path's truncation only from its ABI 3 (Linux 6.2), so a decoder exploit could otherwise make the
    /// user's keys readable to all or empty a file the user owns (review finding R-H3). ftruncate stays: it needs a
    /// descriptor open for writing, which Landlock refuses for every file, and .NET's double-mapped code memory uses it.
    /// No SysV IPC (other processes' shared memory and queues), and signals only to <paramref name="self"/>, the worker
    /// (review finding R-M6): kill(-1, SIGKILL) would end the user's session. The runtime signals its own threads.
    /// </summary>
    internal static LinuxPictureProcessPolicy.Filter[] BuildPreExecFilter(LinuxPictureProcessPolicy.Abi abi, uint self)
    {
        const uint Deny = 0x00050001, Allow = 0x7fff0000;
        var code = new List<LinuxPictureProcessPolicy.Filter>
        {
            new(0x20, 0, 0, 4), new(0x15, 1, 0, abi.AuditArchitecture), new(0x06, 0, 0, 0x80000000),
            new(0x20, 0, 0, 0),
        };
        if (abi.RejectX32)
        {
            code.Add(new(0x45, 0, 1, 0x40000000));
            code.Add(new(0x06, 0, 0, Deny));
        }
        bool x64 = abi.AuditArchitecture == 0xc000003e;
        uint[] network = x64
            ? [41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 288, 299, 307]
            : [198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 242, 243, 269];
        uint[] ioUring = [425, 426, 427];
        // truncate, then the chmod, chown, extended attribute and time calls: x86-64 has the old path forms too (chmod,
        // chown, lchown, utime, utimes, futimesat), ARM64 only the *at ones. Both: fchmodat2, setxattrat and removexattrat
        // (Linux 6.13) and file_setattr (6.17).
        uint[] metadata = x64
            ? [76, 90, 91, 92, 93, 94, 132, 188, 189, 190, 197, 198, 199, 235, 260, 261, 268, 280, 452, 463, 466, 469]
            : [45, 52, 53, 54, 55, 5, 6, 7, 14, 15, 16, 88, 452, 463, 466, 469];
        // SysV shared memory, semaphores and message queues; tkill, which names a thread of any process; and process file
        // descriptors (pidfd_send_signal, pidfd_open, pidfd_getfd).
        uint[] ipc = x64
            ? [29, 30, 31, 64, 65, 66, 67, 68, 69, 70, 71, 220, 200, 424, 434, 438]
            : [186, 187, 188, 189, 190, 191, 192, 193, 194, 195, 196, 197, 130, 424, 434, 438];
        foreach (uint call in network.Concat(ioUring).Concat(metadata).Concat(ipc))
        {
            code.Add(new(0x15, 0, 1, call));
            code.Add(new(0x06, 0, 0, Deny));
        }
        // kill, tgkill, rt_sigqueueinfo and rt_tgsigqueueinfo, whose first argument is the target process: allowed only
        // for this one (its low word, then its high word, which a negative ID such as -1 sets).
        uint[] signals = x64 ? [62, 234, 129, 297] : [129, 131, 138, 240];
        foreach (uint call in signals)
        {
            code.Add(new(0x15, 0, 6, call));
            code.Add(new(0x20, 0, 0, 16));
            code.Add(new(0x15, 0, 3, self));
            code.Add(new(0x20, 0, 0, 20));
            code.Add(new(0x15, 0, 1, 0));
            code.Add(new(0x06, 0, 0, Allow));
            code.Add(new(0x06, 0, 0, Deny));
        }
        code.Add(new(0x06, 0, 0, Allow));
        return [.. code];
    }

    private static void Exec(string executable, ReadOnlySpan<string> arguments)
    {
        var strings = new nint[arguments.Length + 2];
        try
        {
            strings[0] = Marshal.StringToCoTaskMemUTF8(executable);
            for (int i = 0; i < arguments.Length; i++) strings[i + 1] = Marshal.StringToCoTaskMemUTF8(arguments[i]);
            fixed (nint* pointer = strings) Execv(executable, (nint)pointer);
        }
        finally
        {
            foreach (nint value in strings) if (value != 0) Marshal.FreeCoTaskMem(value);
        }
    }

    private static void Refuse(string reason) => throw new IOException(reason);

    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial long Syscall(long number, nint a, nint b, nint c, nint d, nint e, nint f);
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Open(string path, int flags);
    [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
    private static partial int Close(int descriptor);
    [LibraryImport("libc", EntryPoint = "execv", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Execv(string path, nint arguments);
}
