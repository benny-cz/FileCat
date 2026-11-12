using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Linux;

/// <summary>Unregistered, stream-only Linux bootstrap. No archive byte is consumed by the launcher or policy setup.</summary>
internal static unsafe partial class LinuxArchiveIsolation
{
    internal const string LauncherArgument = "--archive-linux-launch", WorkerArgument = "--archive-linux-worker";
    internal const long ReadableAssetMaximum = 256L << 20;
    internal readonly record struct Startup(Guid Nonce, int Parent, int CpuSeconds, int CancellationDescriptor);
    [StructLayout(LayoutKind.Sequential)]
    private struct RLimit { internal ulong Current, Maximum; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Ruleset { internal ulong FileSystem, Network, Scoped; }
    [StructLayout(LayoutKind.Sequential)]
    private struct PathRule { internal ulong Access; internal int Parent; }

    internal static Startup Parse(string[] args, string mode)
    {
        if (args.Length is < 6 or > 64 || args[0] != mode || !Guid.TryParseExact(args[1], "D", out Guid nonce) || nonce == Guid.Empty ||
            !int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int parent) || parent <= 1 ||
            !int.TryParse(args[3], NumberStyles.None, CultureInfo.InvariantCulture, out int cpu) || cpu is < 1 or > 7200 ||
            args[2] != parent.ToString(CultureInfo.InvariantCulture) || args[3] != cpu.ToString(CultureInfo.InvariantCulture) ||
            args.Any(a => a.Length > 4096 || a.IndexOf('\0') >= 0))
            throw new ArgumentException("Invalid Linux archive startup facts.");
        long cancellation = ArchiveWorkerCancellation.Parse(args[^2], args[^1]);
        if (cancellation > int.MaxValue) throw new ArgumentException("Invalid Linux archive cancellation descriptor.");
        return new(nonce, parent, cpu, (int)cancellation);
    }

    internal static int RunLauncher(string[] args)
    {
        try
        {
            var startup = Parse(args, LauncherArgument);
            LinuxArchiveNative.RequireOrdinaryIdentity();
            if (GetParentPid() != startup.Parent || LinuxArchiveProcessPolicy.Prctl(1 /* SET_PDEATHSIG */, 9, 0, 0, 0) != 0 ||
                GetParentPid() != startup.Parent) throw LinuxArchiveNative.Unavailable("The archive parent lifetime could not be established.");
            Span<byte> authorization = stackalloc byte[32];
            using (var input = Console.OpenStandardInput()) input.ReadExactly(authorization);
            if (!authorization.SequenceEqual(Authorization(startup.Nonce)))
                throw LinuxArchiveNative.Unavailable("The archive launcher did not receive its fixed startup authorization.");
            string executable = Environment.ProcessPath ?? throw LinuxArchiveNative.Unavailable("The archive worker executable is unavailable.");
            ValidateBundle(executable);
            LinuxArchiveCgroup.AttachCurrent(startup.Nonce);
            Trace("launcher attached");
            var limit = new RLimit { Current = (ulong)startup.CpuSeconds, Maximum = (ulong)startup.CpuSeconds };
            if (SetRlimit(0 /* CPU */, &limit) != 0 || LinuxArchiveProcessPolicy.Prctl(38 /* NO_NEW_PRIVS */, 1, 0, 0, 0) != 0)
                throw LinuxArchiveNative.Unavailable("The archive worker's CPU/privilege controls could not be applied.");
            // Preserve only the dedicated, read-only cancellation pipe across exec. Every unrelated inheritable
            // descriptor is closed. A fresh CLR then allocates inside the memory cgroup.
            VerifyCancellationDescriptor(startup.CancellationDescriptor);
            if (LinuxArchiveNative.Syscall(436 /* close_range */, 3, unchecked((nint)uint.MaxValue), 4 /* CLOEXEC */, 0, 0, 0) != 0)
                throw LinuxArchiveNative.Unavailable("The archive worker could not exclude inherited descriptors.");
            if (Fcntl(startup.CancellationDescriptor, 2 /* SETFD */, 0) != 0)
                throw LinuxArchiveNative.Unavailable("The archive cancellation pipe could not be retained.");
            string[] child = (string[])args.Clone(); child[0] = WorkerArgument;
            Trace("launcher exec");
            Exec(executable, child);
            throw LinuxArchiveNative.Unavailable("The confined archive runtime could not start.");
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException)
        { Console.Error.WriteLine(ex.Message); return 65; }
    }

    internal static byte[] Authorization(Guid nonce)
    {
        if (nonce == Guid.Empty) throw new ArgumentException("A startup nonce is required.");
        byte[] bytes = new byte[32]; "FCALNX1\0"u8.CopyTo(bytes);
        nonce.TryWriteBytes(bytes.AsSpan(8, 16), bigEndian: true, out _); return bytes;
    }

    internal static int RunWorker(string[] args, Func<Startup, int> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Startup startup = ApplyWorker(args);
        return RunParser(() => action(startup));
    }

    internal static int RunParser(Func<int> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        int result = 65; Exception? failure = null; bool authorized = false;
        Trace("worker global policies installed");
        using var begin = new ManualResetEventSlim();
        var parser = new Thread(() =>
        {
            begin.Wait(); if (!authorized) return;
            try { result = action(); } catch (Exception ex) { failure = ex; }
        })
        { IsBackground = true, Name = "FileCat confined archive parser" };
        parser.Start();
        Trace("worker parser thread created");
        try { LinuxArchiveProcessPolicy.ProtectMainLifetime(); authorized = true; }
        finally { begin.Set(); parser.Join(); }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    private static Startup ApplyWorker(string[] args)
    {
        var startup = Parse(args, WorkerArgument);
        LinuxArchiveNative.RequireOrdinaryIdentity(); VerifyEnvironment();
        int parentSignal = 0;
        if (GetParentPid() != startup.Parent || LinuxArchiveProcessPolicy.Prctl(2 /* GET_PDEATHSIG */, (nint)(&parentSignal), 0, 0, 0) != 0 ||
            parentSignal != 9 || LinuxArchiveProcessPolicy.Prctl(39 /* GET_NO_NEW_PRIVS */, 0, 0, 0, 0) != 1)
            throw LinuxArchiveNative.Unavailable("The archive worker's actual parent/privilege controls do not match its startup facts.");
        var limit = new RLimit();
        if (GetRlimit(0, &limit) != 0 || limit.Current != (ulong)startup.CpuSeconds || limit.Maximum != (ulong)startup.CpuSeconds)
            throw LinuxArchiveNative.Unavailable("The archive worker has no exact finite CPU limit.");
        string groupPath = LinuxArchiveCgroup.VerifyCurrent(startup.Nonce);
        Trace("worker resource controls verified");
        VerifyDescriptors(startup.CancellationDescriptor);
        Trace("worker descriptor inventory verified");
        string executable = Environment.ProcessPath ?? throw LinuxArchiveNative.Unavailable("The archive worker executable is unavailable.");
        var assets = ValidateBundle(executable);
        // Warm the codec/crypto native libraries before capturing exact OS read leaves. This consumes no hostile data.
        _ = SHA256.HashData([]);
        using (var output = new MemoryStream())
        { using var encoder = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true); encoder.WriteByte(0); }
        var reads = ReadLeaves(assets.Root, assets.Bytes, groupPath);
        Trace("worker assets warmed");
        if (LinuxArchiveProcessPolicy.Prctl(4 /* SET_DUMPABLE */, 0, 0, 0, 0) != 0)
            throw LinuxArchiveNative.Unavailable("The archive worker could not refuse external tracing.");
        LinuxArchiveProcessPolicy.Apply();
        Trace("worker syscall policy installed");
        ApplyReadPolicy(reads);
        Trace("worker filesystem policy installed");
        if (GetParentPid() != startup.Parent) throw LinuxArchiveNative.Unavailable("The archive parent exited during isolation.");
        return startup;
    }

    private static void Trace(string phase)
    { if (Environment.GetEnvironmentVariable("FILECAT_ARCHIVE_LINUX_QUAL_DIAGNOSTICS") == "1") Console.Error.WriteLine("Archive qualification: " + phase); }

    // Bundles found valid stay so for this process; availability is asked on keypresses.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string?> bundleProblems = new(StringComparer.Ordinal);

    /// <summary>
    /// Why the confined worker cannot run from the application at <paramref name="executable"/>, or null: the same checks a
    /// worker start makes (ext4, a self-contained bundle, private assets), answered once per process for availability.
    /// </summary>
    internal static string? BundleProblem(string executable) => bundleProblems.GetOrAdd(executable, path =>
    {
        try { ValidateBundle(path); return null; }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException or UnauthorizedAccessException) { return ex.Message; }
    });

    internal static (string Root, long Bytes) ValidateBundle(string executable)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (!Path.IsPathFullyQualified(executable) || UnixFiles.RealPath(executable) != executable)
            throw LinuxArchiveNative.Unavailable("The Linux archive executable must be an exact local package path.");
        string root = Path.GetDirectoryName(executable)!;
        // The AppImage runtime sets APPIMAGE for the application it runs from its mounted image (squashfs over FUSE), which
        // is not a qualified place for the confined worker. Say so before any filesystem check fails less helpfully.
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 })
            throw LinuxArchiveNative.Unavailable("Archive writing is not available from the AppImage: its mounted image is not a qualified place for " +
                "the confined archive worker. Install FileCat's .deb package, or extract the AppImage (--appimage-extract) into a folder on an ext4 drive.");
        using (var held = LinuxArchiveNative.OpenDirectory(root))
            if (Unix.UnixArchiveVolume.Observe(held).FileSystem != "ext4")
                throw LinuxArchiveNative.Unavailable("This Linux archive bundle filesystem has not been qualified.");
        if (!File.Exists(Path.Combine(root, "libcoreclr.so")) || !File.Exists(Path.Combine(root, "FileCat.Core.dll")) ||
            !File.Exists(Path.Combine(root, "System.Private.CoreLib.dll")))
            throw LinuxArchiveNative.Unavailable("This Linux archive route requires a separately qualified self-contained worker bundle.");
        uint uid = LinuxArchiveNative.RequireOrdinaryIdentity(); long bytes = 0; int count = 0;
        var pending = new Queue<(string Path, int Depth)>(); pending.Enqueue((root, 0));
        while (pending.TryDequeue(out var entry))
        {
            // Depth 16, as on Windows: the packaged notices nest nine folders deep (licenses/dependencies/upstream/…/<commit>).
            if (++count > 16_384 || entry.Depth > 16) throw LinuxArchiveNative.Unavailable("The Linux archive bundle exceeds its inventory limit.");
            var info = UnixFiles.Stat(entry.Path) ?? throw LinuxArchiveNative.Unavailable("The Linux archive bundle cannot be inspected.");
            if (info.IsLink || (info.Owner != 0 && info.Owner != uid) || (info.Mode & 0x12) != 0)
                throw LinuxArchiveNative.Unavailable("The Linux archive bundle has linked or externally writable assets.");
            if (info.IsDirectory)
            {
                foreach (string path in Directory.EnumerateFileSystemEntries(entry.Path))
                { if (pending.Count + count >= 16_384) throw LinuxArchiveNative.Unavailable("The Linux archive bundle has too many assets."); pending.Enqueue((path, entry.Depth + 1)); }
            }
            else
            {
                if (!info.IsRegular || info.Size < 0 || info.Size > ReadableAssetMaximum - bytes)
                    throw LinuxArchiveNative.Unavailable("The Linux archive bundle exceeds its read-only mapping budget.");
                bytes += info.Size;
            }
        }
        return (root, bytes);
    }

    private static IReadOnlyList<(string Path, bool Directory)> ReadLeaves(string root, long bytes, string groupPath)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            [root] = true,
            ["/proc/self"] = true,
            [groupPath] = true,
            ["/sys/devices/system/cpu"] = true
        };
        string maps = LinuxArchiveNative.ReadPath("/proc/self/maps", 64 << 10);
        foreach (string line in maps.Split('\n'))
        {
            string[] fields = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 6 || fields[5][0] != '/' || fields[5].StartsWith("/memfd:", StringComparison.Ordinal)) continue;
            string path = fields[5];
            if (path.Contains('\\') || path.EndsWith(" (deleted)", StringComparison.Ordinal))
                throw LinuxArchiveNative.Unavailable("A mapped runtime asset has an unsupported or deleted name.");
            if (path.StartsWith(root + "/", StringComparison.Ordinal)) continue;
            string resolved = UnixFiles.RealPath(path) ?? throw LinuxArchiveNative.Unavailable("A mapped system asset cannot be resolved.");
            if (!resolved.StartsWith("/usr/lib/", StringComparison.Ordinal))
                throw LinuxArchiveNative.Unavailable("The archive runtime mapped an asset outside its package or system libraries.");
            AddSystemFile(resolved);
        }
        foreach (string path in new[] { "/etc/ld.so.cache", "/etc/localtime", "/usr/share/zoneinfo/UTC" })
            if (File.Exists(path)) AddSystemFile(UnixFiles.RealPath(path) ?? throw LinuxArchiveNative.Unavailable("A runtime system read cannot be resolved."));
        foreach (string path in new[] { "/dev/null", "/dev/urandom", "/dev/random" }) result[path] = false;
        return result.Select(p => (p.Key, p.Value)).ToArray();

        void AddSystemFile(string path)
        {
            if (result.ContainsKey(path)) return;
            var info = UnixFiles.Stat(path) ?? throw LinuxArchiveNative.Unavailable("The archive runtime's system leaf cannot be inspected.");
            if (!info.IsRegular || info.IsLink || info.Owner != 0 || (info.Mode & 0x12) != 0 || info.Size < 0 || info.Size > ReadableAssetMaximum - bytes)
                throw LinuxArchiveNative.Unavailable("The archive runtime's system reads exceed their permission/mapping budget.");
            bytes += info.Size; result[path] = false;
        }
    }

    private static void ApplyReadPolicy(IReadOnlyList<(string Path, bool Directory)> paths)
    {
        long abi = LinuxArchiveNative.Syscall(444, 0, 0, 1 /* VERSION */, 0, 0, 0);
        if (abi < 8) throw LinuxArchiveNative.Unavailable("This archive route requires Landlock ABI 8 all-thread enforcement.");
        var attributes = new Ruleset { FileSystem = 0xffff, Network = 3, Scoped = 3 };
        long fd = LinuxArchiveNative.Syscall(444, (nint)(&attributes), sizeof(Ruleset), 0, 0, 0, 0);
        if (fd < 0) throw LinuxArchiveNative.Unavailable("The archive filesystem policy could not be created.");
        using var ruleset = new SafeFileHandle((nint)fd, ownsHandle: true);
        foreach (var (path, directory) in paths)
        {
            int handle = OpenPath(path, 0x200000 /* O_PATH */ | LinuxArchiveNative.CloseOnExec);
            if (handle < 0) throw LinuxArchiveNative.Unavailable("A required archive runtime read could not be pinned.");
            using var held = new SafeFileHandle((nint)handle, ownsHandle: true);
            var rule = new PathRule { Access = (1UL << 2) | (directory ? 1UL << 3 : 0), Parent = handle };
            if (LinuxArchiveNative.Syscall(445, (nint)fd, 1 /* PATH_BENEATH */, (nint)(&rule), 0, 0, 0) != 0)
                throw LinuxArchiveNative.Unavailable("The archive filesystem read rule could not be installed.");
        }
        if (LinuxArchiveNative.Syscall(446, (nint)fd, 1 << 3 /* RESTRICT_SELF_TSYNC, Linux 7.0 UAPI */, 0, 0, 0, 0) != 0)
            throw LinuxArchiveNative.Unavailable("The archive filesystem policy could not cover every runtime thread.");
    }

    private static void VerifyDescriptors(int cancellation)
    {
        foreach (string path in Directory.GetFileSystemEntries("/proc/self/fd"))
        {
            if (!int.TryParse(Path.GetFileName(path), out int fd) || fd < 0) throw LinuxArchiveNative.Unavailable("The archive descriptor inventory is invalid.");
            int flags = Fcntl(fd, 1 /* GETFD */, 0);
            if (flags < 0) { if (Marshal.GetLastPInvokeError() == 9) continue; throw LinuxArchiveNative.Unavailable("The archive descriptor flags are unavailable."); }
            if (fd > 2 && fd != cancellation && (flags & 1) == 0) throw LinuxArchiveNative.Unavailable("The archive runtime inherited an unapproved descriptor.");
        }
        byte* facts = stackalloc byte[256]; byte* empty = stackalloc byte[1]; empty[0] = 0;
        long statx = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 291 : 332;
        for (int fd = 0; fd < 3; fd++)
        {
            long result = LinuxArchiveNative.Syscall(statx, fd, (nint)empty, 0x1000, 1, (nint)facts, 0);
            if (result != 0 || (*(ushort*)(facts + 28) & 0xf000) != 0x1000 /* FIFO */)
                throw LinuxArchiveNative.Unavailable("Archive standard streams must be parent-owned pipes.");
        }
        VerifyCancellationDescriptor(cancellation);
    }

    private static void VerifyCancellationDescriptor(int descriptor)
    {
        byte* facts = stackalloc byte[256]; byte* empty = stackalloc byte[1]; empty[0] = 0;
        long statx = RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 291 : 332;
        int flags = Fcntl(descriptor, 3 /* GETFL */, 0);
        if (descriptor < 3 || flags < 0 || (flags & 3 /* O_ACCMODE */) != 0 /* O_RDONLY */ ||
            LinuxArchiveNative.Syscall(statx, descriptor, (nint)empty, 0x1000, 1, (nint)facts, 0) != 0 ||
            (*(ushort*)(facts + 28) & 0xf000) != 0x1000 /* FIFO */)
            throw LinuxArchiveNative.Unavailable("The archive cancellation descriptor must be a read-only parent pipe.");
    }

    private static void VerifyEnvironment()
    {
        foreach (string key in Environment.GetEnvironmentVariables().Keys)
            if (key.StartsWith("LD_", StringComparison.Ordinal) || key.StartsWith("DYLD_", StringComparison.Ordinal) ||
                key.StartsWith("CORECLR_", StringComparison.Ordinal) || key.StartsWith("COMPlus_", StringComparison.Ordinal) ||
                key.StartsWith("DOTNET_STARTUP_", StringComparison.Ordinal) || key.StartsWith("DOTNET_ADDITIONAL_", StringComparison.Ordinal) ||
                key.StartsWith("DOTNET_SHARED_STORE", StringComparison.Ordinal))
                throw LinuxArchiveNative.Unavailable("The archive runtime inherited an unsupported loader environment.");
    }

    private static void Exec(string executable, IReadOnlyList<string> arguments)
    {
        nint[] strings = new nint[arguments.Count + 2];
        try
        {
            strings[0] = Marshal.StringToCoTaskMemUTF8(executable);
            for (int i = 0; i < arguments.Count; i++) strings[i + 1] = Marshal.StringToCoTaskMemUTF8(arguments[i]);
            fixed (nint* argv = strings) Execv(executable, (nint)argv);
        }
        finally { foreach (nint value in strings) if (value != 0) Marshal.FreeCoTaskMem(value); }
    }

    [LibraryImport("libc", EntryPoint = "getppid")]
    private static partial int GetParentPid();
    [LibraryImport("libc", EntryPoint = "getrlimit", SetLastError = true)]
    private static partial int GetRlimit(int resource, RLimit* limit);
    [LibraryImport("libc", EntryPoint = "setrlimit", SetLastError = true)]
    private static partial int SetRlimit(int resource, RLimit* limit);
    [LibraryImport("libc", EntryPoint = "execv", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Execv(string path, nint arguments);
    [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int OpenPath(string path, int flags);
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command, int argument);
}
