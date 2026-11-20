using System.Runtime.InteropServices;
using System.Text.Json;
using FileCat.Core.Archives.Linux;

internal static unsafe partial class LinuxArchiveProbe
{
    internal static int Parent()
    {
        if (!OperatingSystem.IsLinux()) return 64;
        var worker = LinuxArchiveProcess.Start(Environment.ProcessPath!, Guid.NewGuid(), TimeSpan.FromSeconds(30), ["archive-idle"]);
        using var reader = new StreamReader(worker.Output);
        string? ready = reader.ReadLine();
        if (ready != worker.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        { worker.DisposeAsync().AsTask().GetAwaiter().GetResult(); return 65; }
        Console.WriteLine(JsonSerializer.Serialize(new { pid = worker.ProcessId, controller = worker.ControlPath })); Console.Out.Flush();
        if (Console.ReadLine() == "die") Exit(90); // Deliberately bypass disposal; child ignores private-pipe EOF.
        worker.DisposeAsync().AsTask().GetAwaiter().GetResult(); return 0;
    }

    internal static int MainExitGuard()
    {
        if (!OperatingSystem.IsLinux()) return 64;
        LinuxArchiveNative.RequireOrdinaryIdentity();
        if (LinuxArchiveProcessPolicy.Prctl(38, 1, 0, 0, 0) != 0) return 65;
        LinuxArchiveProcessPolicy.ProtectMainLifetime();
        var abi = LinuxArchiveProcessPolicy.CurrentAbi();
        long result = LinuxArchiveNative.Syscall(abi.X32 ? 60 : 93, 92, 0, 0, 0, 0, 0);
        bool denied = result == -1 && Marshal.GetLastPInvokeError() == 1;
        Console.WriteLine("MAIN-EXIT-DENIED=" + denied); return denied ? 0 : 9;
    }

    internal static int Descriptor(string[] args)
    {
        if (!OperatingSystem.IsLinux() || args.Length != 2 || !int.TryParse(args[1], out int fd) || fd < 1000) return 64;
        int result = Fcntl(fd, 1 /* GETFD */, 0); int error = Marshal.GetLastPInvokeError();
        bool denied = result == -1 && error == 9;
        bool assetRead = File.ReadAllBytes(typeof(FileCat.Core.Archives.ArchivePlan).Assembly.Location).Length > 0;
        Console.WriteLine(JsonSerializer.Serialize(new { descriptorClosed = denied, assetRead })); return denied && assetRead ? 0 : 9;
    }

    internal static int Authority(string[] args)
    {
        if (!OperatingSystem.IsLinux() || args.Length != 3) return 64;
        bool assetRead = File.ReadAllBytes(typeof(FileCat.Core.Archives.ArchivePlan).Assembly.Location).Length > 0;
        bool readDenied = false, writeDenied = false, metadataDenied = false;
        try { File.ReadAllBytes(args[1]); } catch (UnauthorizedAccessException) { readDenied = true; }
        try { File.WriteAllText(args[2], "unexpected archive write"); } catch (UnauthorizedAccessException) { writeDenied = true; }
        try { File.SetLastWriteTimeUtc(args[1], DateTime.UnixEpoch); } catch (UnauthorizedAccessException) { metadataDenied = true; }
        var abi = LinuxArchiveProcessPolicy.CurrentAbi();
        bool parserThread = LinuxArchiveNative.Syscall(abi.X32 ? 186 : 178, 0, 0, 0, 0, 0, 0) != Environment.ProcessId;
        bool controllerWriteDenied = false;
        string membership = LinuxArchiveNative.ReadPath("/proc/self/cgroup", 4096);
        if (!membership.StartsWith("0::/", StringComparison.Ordinal)) return 65;
        try { using var attempt = File.OpenHandle("/sys/fs/cgroup" + membership[3..].TrimEnd('\n') + "/memory.max", FileMode.Open, FileAccess.Write); }
        catch (UnauthorizedAccessException) { controllerWriteDenied = true; }
        bool tcpDenied = Denied(abi.X32 ? 41 : 198, 2, 1), udpDenied = Denied(abi.X32 ? 41 : 198, 2, 2), unixDenied = Denied(abi.X32 ? 41 : 198, 1, 1);
        bool childDenied = Denied(abi.CloneSyscall, 17), ownSignal = LinuxArchiveNative.Syscall(abi.X32 ? 62 : 129, Environment.ProcessId, 0, 0, 0, 0, 0) == 0;
        bool parentSignalDenied = Denied(abi.X32 ? 62 : 129, GetParentPid(), 0);
        bool extendedAttributeReadsDenied = Denied(abi.X32 ? 191 : 8, 0) && Denied(abi.X32 ? 194 : 11, 0) && Denied(464, 0) && Denied(465, 0);
        bool clearParentDeathDenied = LinuxArchiveProcessPolicy.Prctl(1, 0, 0, 0, 0) == -1 && Marshal.GetLastPInvokeError() == 1;
        bool limitsDenied = Denied(abi.SetRlimit, 0, 0);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            assetRead,
            readDenied,
            writeDenied,
            metadataDenied,
            tcpDenied,
            udpDenied,
            unixDenied,
            childDenied,
            parserThread,
            controllerWriteDenied,
            ownSignal,
            parentSignalDenied,
            extendedAttributeReadsDenied,
            clearParentDeathDenied,
            limitsDenied,
            runtime = Environment.Version.ToString()
        }));
        return 0;
    }

    internal static int Memory()
    {
        if (!OperatingSystem.IsLinux()) return 64;
        byte* small = (byte*)NativeMemory.Alloc(64u << 20);
        for (nuint i = 0; i < 64u << 20; i += 4096) small[i] = 0x71;
        Console.WriteLine("HEALTHY-64-MIB-TOUCHED"); Console.Out.Flush(); NativeMemory.Free(small);
        byte* excessive = (byte*)NativeMemory.Alloc((nuint)(1088L << 20));
        for (nuint i = 0; i < (nuint)(1088L << 20); i += 4096) excessive[i] = 0x39;
        Console.WriteLine("NATIVE MEMORY LIMIT DID NOT STOP THE PROBE"); NativeMemory.Free(excessive); return 9;
    }

    private static bool Denied(long call, nint a, nint b = 0)
    {
        long result = LinuxArchiveNative.Syscall(call, a, b, 0, 0, 0, 0); int error = Marshal.GetLastPInvokeError();
        if (result >= 0 && call is 41 or 198) Close((int)result);
        // clone without CLONE_THREAD should be denied. If this probe reveals a bug, its accidental child exits
        // immediately rather than entering the parent's test/IPC path.
        if (result == 0 && call is 56 or 220) Exit(91);
        if (result > 0 && call is 56 or 220) { int status; WaitPid((int)result, &status, 0); }
        return result == -1 && error == 1;
    }
    [LibraryImport("libc", EntryPoint = "getppid")]
    private static partial int GetParentPid();
    [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static partial int Fcntl(int fd, int command, int value);
    [LibraryImport("libc", EntryPoint = "close")]
    private static partial int Close(int fd);
    [LibraryImport("libc", EntryPoint = "_exit")]
    private static partial void Exit(int code);
    [LibraryImport("libc", EntryPoint = "waitpid")]
    private static partial int WaitPid(int pid, int* status, int options);
}
