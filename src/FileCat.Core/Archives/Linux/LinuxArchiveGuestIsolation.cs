using System.Collections;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;

namespace FileCat.Core.Archives.Linux;

/// <summary>Private-image Linux guest bootstrap. It grants no package/platform admission to its caller.</summary>
internal static unsafe partial class LinuxArchiveGuestIsolation
{
    internal readonly record struct Facts(uint Uid, long PhysicalKiB, ulong RootFileSystem);
    [StructLayout(LayoutKind.Sequential)] private struct Limit { internal ulong Current, Maximum; }

    internal static int Run(Func<Facts, int> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Facts facts = Verify();
        _ = SHA256.HashData([]);
        using (var output = new MemoryStream())
        { using var encoder = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true); encoder.WriteByte(0); }
        if (LinuxArchiveProcessPolicy.Prctl(4 /* SET_DUMPABLE */, 0, 0, 0, 0) != 0)
            throw Unavailable("The guest could not refuse tracing.");
        LinuxArchiveProcessPolicy.Apply(readOnlyPaths: true, privateGuestThreads: true);
        return LinuxArchiveIsolation.RunParser(() => action(facts));
    }

    private static Facts Verify()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.Arm64)
            throw Unavailable("This guest bootstrap requires the qualified ARM64 Linux image.");
        uint uid = LinuxArchiveNative.RequireOrdinaryIdentity();
        if (uid != 1000 || GetParentPid() != 1 || LinuxArchiveProcessPolicy.Prctl(39, 0, 0, 0, 0) != 1)
            throw Unavailable("The guest's identity, parent or privilege state differs from its startup contract.");
        string[] status = LinuxArchiveNative.ReadPath("/proc/self/status", 64 << 10).Split('\n');
        if (status.Single(line => line.StartsWith("Groups:", StringComparison.Ordinal)).Trim() != "Groups:" ||
            status.Single(line => line.StartsWith("Gid:", StringComparison.Ordinal)).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Skip(1).Any(value => value != "1000"))
            throw Unavailable("The guest retained unexpected group authority.");
        foreach (string capability in new[] { "CapInh:", "CapPrm:", "CapEff:", "CapAmb:" })
            if (status.Single(line => line.StartsWith(capability, StringComparison.Ordinal))[capability.Length..].Trim() != "0000000000000000")
                throw Unavailable("The guest retained unexpected capability authority.");
        // musl's pthread_kill uses tkill without a thread-group argument. Its guest-only exception relies on
        // actual kernel identity isolation: the supervisor stays root, and no second ordinary process exists.
        int processCount = 0;
        foreach (string process in Directory.EnumerateDirectories("/proc"))
        {
            if (!int.TryParse(Path.GetFileName(process), NumberStyles.None, CultureInfo.InvariantCulture, out int pid) ||
                pid == 1 || pid == Environment.ProcessId) continue;
            if (++processCount > 4096) throw Unavailable("The private guest process inventory exceeds its bound.");
            string[] other;
            try { other = LinuxArchiveNative.ReadPath(process + "/status", 16 << 10).Split('\n'); }
            catch (FileNotFoundException) { continue; } catch (DirectoryNotFoundException) { continue; }
            // The guest kernel has its own root-owned kworkers. They are not userspace peers and cannot receive
            // a signal from UID 1000. Every other userspace process, including an extra root process, is refused.
            if (!other.Contains("Kthread:\t1", StringComparer.Ordinal) || !HasRootIdentity(other))
                throw Unavailable("The private guest contains an unexpected userspace process.");
        }
        string[] parent = LinuxArchiveNative.ReadPath("/proc/1/status", 64 << 10).Split('\n');
        if (!HasRootIdentity(parent) || !parent.Contains("Kthread:\t0", StringComparer.Ordinal))
            throw Unavailable("The private guest supervisor changed identity.");
        int death = 0;
        var cpu = new Limit();
        if (LinuxArchiveProcessPolicy.Prctl(2, (nint)(&death), 0, 0, 0) != 0 || death != 9 ||
            GetRlimit(0, &cpu) != 0 || cpu.Current != 30 || cpu.Maximum != 30)
            throw Unavailable("The guest's actual lifetime/CPU controls are unavailable.");
        foreach (DictionaryEntry variable in Environment.GetEnvironmentVariables())
            if (variable.Key is not string name || variable.Value is not string value ||
                !((name == "DOTNET_EnableDiagnostics" && value == "0") || (name == "DOTNET_SYSTEM_GLOBALIZATION_INVARIANT" && value == "1")))
                throw Unavailable("The guest environment contains an unapproved runtime override.");
        string[] memory = LinuxArchiveNative.ReadPath("/proc/meminfo", 64 << 10).Split('\n').First(line => line.StartsWith("MemTotal:", StringComparison.Ordinal))
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (memory.Length != 3 || memory[2] != "kB" || !long.TryParse(memory[1], NumberStyles.None, CultureInfo.InvariantCulture, out long physical) ||
            physical <= 512 * 1024 || physical > 1024 * 1024) throw Unavailable("The guest physical memory does not match its fixed profile.");
        if (LinuxArchiveNative.ReadPath("/proc/self/cgroup", 4096) != "0::/filecat\n") throw Unavailable("The guest has no exact controller membership.");
        using (var controller = LinuxArchiveNative.OpenDirectory("/sys/fs/cgroup/filecat"))
        {
            LinuxArchiveNative.RequireCgroupFileSystem(controller);
            RequireRootOwner(UnixFiles.Stat(controller), "controller");
            foreach (var (name, wanted) in new[] { ("memory.max", "1073741824\n"), ("memory.swap.max", "0\n"), ("pids.max", "128\n"),
                ("cgroup.procs", Environment.ProcessId.ToString(CultureInfo.InvariantCulture) + "\n") })
            {
                using var file = LinuxArchiveNative.OpenAt(controller, name);
                RequireRootOwner(UnixFiles.Stat(file), name);
                if (LinuxArchiveNative.ReadAt(controller, name) != wanted) throw Unavailable("A guest controller differs from its fixed profile.");
            }
        }
        using var root = LinuxArchiveNative.OpenDirectory("/");
        ulong magic = LinuxArchiveNative.FileSystemMagic(root);
        if (magic is not (0x858458f6UL or 0x01021994UL)) throw Unavailable("The guest root must be the private RAM image.");
        RequireRootOwner(UnixFiles.Stat(root), "root");
        string executable = Environment.ProcessPath ?? throw Unavailable("The guest executable is unavailable.");
        if (!executable.StartsWith("/worker/", StringComparison.Ordinal) || UnixFiles.RealPath(executable) != executable || Path.GetDirectoryName(executable) != "/worker")
            throw Unavailable("The guest runtime is not in its private image.");
        long bytes = 0; int count = 0;
        foreach (string path in Directory.EnumerateFileSystemEntries("/worker"))
        {
            if (++count > 4096 || UnixFiles.Stat(path) is not { IsRegular: true, IsLink: false } stat || stat.Size < 0 || stat.Size > (256L << 20) - bytes)
                throw Unavailable("The guest runtime asset inventory is invalid.");
            RequireRootOwner(stat, "runtime file"); bytes += stat.Size;
        }
        if (count == 0) throw Unavailable("The guest runtime asset inventory is empty.");
        using (var runtime = LinuxArchiveNative.OpenDirectory("/worker")) RequireRootOwner(UnixFiles.Stat(runtime), "runtime directory");
        VerifyDescriptors();
        return new(uid, physical, magic);
    }

    private static void RequireRootOwner(UnixStat? value, string context)
    { if (value is not { Owner: 0, IsLink: false } info || (info.Mode & 0x12) != 0) throw Unavailable("The guest " + context + " ownership/mode is unavailable or externally writable."); }

    private static bool HasRootIdentity(string[] status)
    {
        foreach (string field in new[] { "Uid:", "Gid:" })
        {
            string[]? line = status.SingleOrDefault(line => line.StartsWith(field, StringComparison.Ordinal))?
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (line is not { Length: 5 } || line.Skip(1).Any(value => value != "0")) return false;
        }
        return true;
    }

    private static void VerifyDescriptors()
    {
        foreach (string path in Directory.GetFileSystemEntries("/proc/self/fd"))
        {
            if (!int.TryParse(Path.GetFileName(path), out int fd)) throw Unavailable("Invalid guest descriptor inventory.");
            int flags = Fcntl(fd, 1, 0);
            if (flags < 0) { if (Marshal.GetLastPInvokeError() == 9) continue; throw Unavailable("Guest descriptor flags are unavailable."); }
            if (fd > 3 && (flags & 1) == 0) throw Unavailable("The guest inherited an unapproved descriptor.");
        }
        byte* facts = stackalloc byte[256]; byte* empty = stackalloc byte[1]; empty[0] = 0;
        for (int fd = 0; fd < 4; fd++)
        {
            if (LinuxArchiveNative.Syscall(291, fd, (nint)empty, 0x1000, 1, (nint)facts, 0) != 0 || (*(ushort*)(facts + 28) & 0xf000) != 0x2000)
                throw Unavailable("Guest standard/control streams are not character devices.");
            uint major = *(uint*)(facts + 128), minor = *(uint*)(facts + 132);
            if (!(fd == 2 ? major == 5 && minor == 1 : major == 229 && minor == (fd == 3 ? 2u : 1u)))
                throw Unavailable("The guest stream device identity differs from its fixed grant.");
        }
        if ((Fcntl(3, 3, 0) & 3) != 0) throw Unavailable("The guest control grant must be read-only.");
    }

    private static ArchiveWriteException Unavailable(string message) => LinuxArchiveNative.Unavailable(message);
    [LibraryImport("libc", EntryPoint = "getppid")] private static partial int GetParentPid();
    [LibraryImport("libc", EntryPoint = "getrlimit", SetLastError = true)] private static partial int GetRlimit(int resource, Limit* value);
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)] private static partial int Fcntl(int fd, int command, int argument);
}
