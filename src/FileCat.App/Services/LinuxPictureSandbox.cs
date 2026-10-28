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
        LinuxPictureProcessPolicy.SetNoNewPrivileges("filesystem and network access");
        ApplyFileSystemPolicy();
        var abi = LinuxPictureProcessPolicy.ForArchitecture(RuntimeInformation.ProcessArchitecture);
        LinuxPictureProcessPolicy.ApplyFilter(abi, BuildNetworkFilter(abi), synchronizeThreads: false,
            "The picture decoder could not restrict network access.");
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

    internal static LinuxPictureProcessPolicy.Filter[] BuildNetworkFilter(LinuxPictureProcessPolicy.Abi abi)
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
        uint[] calls = abi.AuditArchitecture == 0xc000003e
            ? [41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 288, 299, 307, 425, 426, 427]
            : [198, 199, 200, 201, 202, 203, 204, 205, 206, 207, 208, 209, 210, 211, 212, 242, 243, 269, 425, 426, 427];
        foreach (uint call in calls)
        {
            code.Add(new(0x15, 0, 1, call));
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
