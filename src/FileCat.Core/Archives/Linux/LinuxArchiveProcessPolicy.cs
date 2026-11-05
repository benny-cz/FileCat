using System.Runtime.InteropServices;

namespace FileCat.Core.Archives.Linux;

/// <summary>Irreversible all-thread syscall policy. Filesystem opens are separately restricted with Landlock.</summary>
internal static unsafe partial class LinuxArchiveProcessPolicy
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Filter(ushort code, byte yes, byte no, uint value)
    { internal readonly ushort Code = code; internal readonly byte Yes = yes, No = no; internal readonly uint Value = value; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Program { internal ushort Count; internal Filter* Code; }
    internal readonly record struct Abi(uint Audit, long Seccomp, uint CloneSyscall, uint Exec, uint ExecAt, uint? Fork, uint? Vfork,
        uint Prctl, uint SetRlimit, uint Prlimit, bool X32);

    internal static Abi CurrentAbi() => ForArchitecture(RuntimeInformation.ProcessArchitecture);
    internal static Abi ForArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => new(0xc000003e, 317, 56, 59, 322, 57, 58, 157, 160, 302, true),
        Architecture.Arm64 => new(0xc00000b7, 277, 220, 221, 281, null, null, 167, 164, 261, false),
        _ => throw new PlatformNotSupportedException("The archive syscall policy supports Linux x64 and ARM64 only."),
    };

    internal static void Apply()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        if (Prctl(39 /* GET_NO_NEW_PRIVS */, 0, 0, 0, 0) != 1)
            throw LinuxArchiveNative.Unavailable("The archive launcher did not establish no-new-privileges.");
        Abi abi = CurrentAbi(); Filter[] code = Build(abi, checked((uint)Environment.ProcessId));
        fixed (Filter* pointer = code)
        {
            var program = new Program { Count = checked((ushort)code.Length), Code = pointer };
            if (LinuxArchiveNative.Syscall(abi.Seccomp, 1 /* SET_MODE_FILTER */, 1 /* TSYNC */, (nint)(&program), 0, 0, 0) != 0)
                throw LinuxArchiveNative.Unavailable("The archive syscall policy could not cover every runtime thread.");
        }
    }

    internal static void ProtectMainLifetime()
    {
        // Install only after the parser thread exists, and before releasing it to consume any hostile byte. Other
        // runtime threads retain normal thread exit; the main task which owns PDEATHSIG cannot disappear alone.
        Abi abi = CurrentAbi(); Filter[] code = BuildMainLifetime(abi);
        fixed (Filter* pointer = code)
        {
            var program = new Program { Count = checked((ushort)code.Length), Code = pointer };
            if (LinuxArchiveNative.Syscall(abi.Seccomp, 1, 0, (nint)(&program), 0, 0, 0) != 0)
                throw LinuxArchiveNative.Unavailable("The archive main-thread lifetime guard could not be installed.");
        }
    }

    internal static Filter[] BuildMainLifetime(Abi abi) =>
    [new(0x20,0,0,4),new(0x15,1,0,abi.Audit),new(0x06,0,0,0x80000000),new(0x20,0,0,0),
        new(0x15,0,1,abi.X32?60u:93u),new(0x06,0,0,0x00050001),new(0x06,0,0,0x7fff0000)];

    internal static Filter[] Build(Abi abi, uint self)
    {
        if (self == 0) throw new ArgumentException("A positive worker process identity is required.");
        const uint Deny = 0x00050001, Allow = 0x7fff0000;
        List<Filter> code = [new(0x20, 0, 0, 4), new(0x15, 1, 0, abi.Audit), new(0x06, 0, 0, 0x80000000), new(0x20, 0, 0, 0)];
        if (abi.X32) { code.Add(new(0x45, 0, 1, 0x40000000)); code.Add(new(0x06, 0, 0, Deny)); }
        // This policy was reviewed against the Linux 7.0 native tables, through rseq_slice_yield (471). New kernel
        // operations do not silently acquire authority. Pinned runtime fallback behavior is qualified separately.
        code.Add(new(0x25, 0, 1, 471)); code.Add(new(0x06, 0, 0, Deny));
        bool x64 = abi.X32;
        uint[] denied = x64 ?
        [
            41,42,43,44,45,46,47,48,49,50,51,52,53,54,55,288,299,307, // sockets, including Unix/UDP and passed descriptors
            76,90,91,92,93,94,132,188,189,190,197,198,199,235,260,261,268,280, // metadata mutations (ftruncate stays for runtime memfd)
            29,30,31,64,65,66,67,68,69,70,71,220,200, // SysV IPC and unrestricted tkill
            101,310,311,312,298,321,323,248,249,250, // ptrace, remote memory, kcmp, perf, BPF, userfaultfd, keyrings
            165,166,155,272,308,303,304, // mount, pivot-root, namespaces and file-handle paths
            105,106,113,114,116,117,119,122,123,126, // credentials/capabilities (also protect parent-death state)
            73,141,142,144,251,256,279,314, // locks and cross-process scheduling/page-placement mutations
        ] :
        [
            198,199,200,201,202,203,204,205,206,207,208,209,210,211,212,242,243,269,
            45,52,53,54,55,5,6,7,14,15,16,88,
            186,187,188,189,190,191,192,193,194,195,196,197,130,
            117,270,271,272,241,280,282,217,218,219,
            40,39,41,97,268,264,265,
            143,144,145,146,147,149,151,152,159,91,
            32,140,118,119,30,238,239,274,
        ];
        foreach (uint number in denied.Concat(new uint[]
        { 425,426,427,424,434,438,428,429,430,431,432,433,442,447,452,460,463,464,465,466,467,469, abi.Exec,abi.ExecAt,abi.SetRlimit })) DenyCall(number);
        if (abi.Fork is { } fork) DenyCall(fork); if (abi.Vfork is { } vfork) DenyCall(vfork);
        // CoreCLR initializes a new thread's affinity through pid=0 (the calling thread). An explicit target,
        // including another thread/process or a high-word alias, remains unavailable.
        GuardCall(x64 ? 203u : 122u, [new(0x20,0,0,16),new(0x15,0,2,0),new(0x20,0,0,20),new(0x15,1,0,0),
            new(0x06,0,0,Deny),new(0x06,0,0,Allow)]);
        // Landlock's device-ioctl right does not cover regular-file metadata ioctls. Permit only the harmless
        // console/pipe read queries needed by this route. F_SETOWN/F_SETSIG/leases/locks cannot target other actors.
        var ioctl = Options([0x5401, 0x5413, 0x541b]); ioctl.Add(new(0x06, 0, 0, Deny));
        GuardCall(x64 ? 16u : 29u, ioctl);
        var fcntl = Options([0, 1, 2, 3, 1030, 1032, 1033, 1034]);
        fcntl.Add(new(0x15, 0, 7, 4)); fcntl.Add(new(0x20, 0, 0, 36)); fcntl.Add(new(0x15, 1, 0, 0)); fcntl.Add(new(0x06, 0, 0, Deny));
        fcntl.Add(new(0x20, 0, 0, 32)); fcntl.Add(new(0x45, 0, 1, 0x2000 /* O_ASYNC */));
        fcntl.Add(new(0x06, 0, 0, Deny)); fcntl.Add(new(0x06, 0, 0, Allow)); fcntl.Add(new(0x06, 0, 0, Deny));
        GuardCall(x64 ? 72u : 25u, fcntl);
        // Explicit hugetlb allocations have a different controller/accounting domain. Ordinary anonymous/transparent
        // huge pages and CLR's normal memfd mappings stay available under memory.max; explicit huge/secret memory does not.
        DenyFlag(x64 ? 9u : 222u, 40, 0x40000 /* mmap MAP_HUGETLB */);
        DenyFlag(x64 ? 319u : 279u, 24, 4 /* memfd_create MFD_HUGETLB */);
        // clone3 hides mutable flags behind a pointer. Force libc's register-based clone fallback for threads.
        code.Add(new(0x15, 0, 1, 435)); code.Add(new(0x06, 0, 0, 0x00050026 /* ENOSYS */));
        // CLONE_THREAD is required and every namespace/process-altering flag is excluded. VM/SIGHAND are required by
        // the kernel for CLONE_THREAD; the allowed remainder is libc's ordinary TLS/TID/stack bookkeeping.
        const uint ThreadFlags = 0x100 | 0x200 | 0x400 | 0x800 | 0x10000 | 0x40000 | 0x80000 | 0x100000 | 0x200000 | 0x1000000;
        code.Add(new(0x15, 0, 9, abi.CloneSyscall));
        code.Add(new(0x20, 0, 0, 20)); code.Add(new(0x15, 0, 6, 0));
        code.Add(new(0x20, 0, 0, 16)); code.Add(new(0x45, 0, 4, 0x10000));
        code.Add(new(0x54, 0, 0, ~ThreadFlags)); code.Add(new(0x15, 0, 2, 0));
        code.Add(new(0x06, 0, 0, Allow)); code.Add(new(0x06, 0, 0, Deny));
        code.Add(new(0x06, 0, 0, Deny));
        // Only read-only prlimit64 queries. The worker cannot raise its CPU limit or change another process's limits.
        code.Add(new(0x15, 0, 5, abi.Prlimit)); code.Add(new(0x20, 0, 0, 32)); code.Add(new(0x15, 0, 2, 0));
        code.Add(new(0x20, 0, 0, 36)); code.Add(new(0x15, 1, 0, 0)); code.Add(new(0x06, 0, 0, Deny));
        // A matching read-only query still reaches the normal allow return; reload the syscall after argument loads.
        code.Add(new(0x20, 0, 0, 0));
        // No clearing parent-death/no-new-privileges/dumpability controls through prctl. Name and anonymous-VMA labels
        // are harmless runtime bookkeeping; only setting dumpable to zero is accepted.
        var prctl = new List<Filter> { new(0x20, 0, 0, 20), new(0x15, 0, 1, 0), new(0x05, 0, 0, 1), new(0x06, 0, 0, Deny), new(0x20, 0, 0, 16) };
        foreach (uint option in new uint[] { 2, 3, 15, 16, 21, 23, 39, 42, 0x53564d41 })
        { prctl.Add(new(0x15, 0, 1, option)); prctl.Add(new(0x06, 0, 0, Allow)); }
        prctl.Add(new(0x15, 0, 6, 4)); prctl.Add(new(0x20, 0, 0, 24)); prctl.Add(new(0x15, 0, 2, 0));
        prctl.Add(new(0x20, 0, 0, 28)); prctl.Add(new(0x15, 1, 0, 0)); prctl.Add(new(0x06, 0, 0, Deny));
        prctl.Add(new(0x06, 0, 0, Allow)); prctl.Add(new(0x06, 0, 0, Deny));
        code.Add(new(0x15, 0, checked((byte)prctl.Count), abi.Prctl)); code.AddRange(prctl);
        code.Add(new(0x20, 0, 0, 0));
        foreach (uint signal in x64 ? new uint[] { 62, 234, 129, 297 } : new uint[] { 129, 131, 138, 240 })
        {
            code.Add(new(0x15, 0, 6, signal)); code.Add(new(0x20, 0, 0, 16)); code.Add(new(0x15, 0, 3, self));
            code.Add(new(0x20, 0, 0, 20)); code.Add(new(0x15, 0, 1, 0)); code.Add(new(0x06, 0, 0, Allow)); code.Add(new(0x06, 0, 0, Deny));
        }
        // Fixed runtime/proxy operations only. The earlier argument guards handle clone, ioctl, fcntl, prctl,
        // resource queries and signals; ordinary file opens still pass through the all-thread Landlock policy.
        // No future/gap syscall or unlisted host-effect operation is admitted by a default allow.
        uint[] ordinary = x64 ?
        [0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,17,18,19,20,21,22,23,24,25,26,27,28,32,33,34,35,36,37,38,39,40,
            60,61,63,74,75,77,78,79,80,81,89,95,96,97,98,99,100,102,104,107,108,110,111,115,118,120,121,124,125,
            127,128,130,131,137,138,140,143,145,146,147,148,158,186,187,202,204,213,217,218,219,
            222,223,224,225,226,228,229,230,231,232,233,247,257,262,267,269,270,271,273,274,275,276,277,278,
            280,281,282,283,284,285,286,287,289,290,291,292,293,294,295,296,301,302,303,309,315,317,318,319,324,326,327,328,332,334,
            436,437,439,441,444,445,446,449,451,453,454,455,456,457,458,459,461,462,468,470,471] :
        [17,19,20,21,22,23,24,29,35,36,37,38,43,44,46,48,49,50,56,57,59,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80,
            84,85,86,87,89,90,93,94,95,98,99,100,101,102,103,107,108,109,110,111,112,113,114,115,
            120,121,123,124,125,126,127,132,133,134,135,136,137,139,141,148,150,153,155,156,158,160,163,165,166,168,169,172,173,174,175,176,177,178,179,
            214,215,216,222,226,227,232,233,243,244,260,261,263,266,267,273,275,276,277,278,279,283,285,286,287,291,293,
            436,437,439,441,444,445,446,449,451,453,454,455,456,457,458,459,461,462,468,470,471];
        foreach (uint number in ordinary) { code.Add(new(0x15, 0, 1, number)); code.Add(new(0x06, 0, 0, Allow)); }
        code.Add(new(0x06, 0, 0, Deny)); return [.. code];

        void DenyCall(uint number) { code.Add(new(0x15, 0, 1, number)); code.Add(new(0x06, 0, 0, Deny)); }
        void DenyFlag(uint number, uint argument, uint mask)
        {
            code.Add(new(0x15, 0, 3, number)); code.Add(new(0x20, 0, 0, argument));
            code.Add(new(0x45, 0, 1, mask)); code.Add(new(0x06, 0, 0, Deny)); code.Add(new(0x20, 0, 0, 0));
        }
        static List<Filter> Options(uint[] allowed)
        {
            List<Filter> body = [new(0x20, 0, 0, 28), new(0x15, 1, 0, 0), new(0x06, 0, 0, Deny), new(0x20, 0, 0, 24)];
            foreach (uint value in allowed) { body.Add(new(0x15, 0, 1, value)); body.Add(new(0x06, 0, 0, Allow)); }
            return body;
        }
        void GuardCall(uint number, List<Filter> body)
        { code.Add(new(0x15, 0, checked((byte)body.Count), number)); code.AddRange(body); code.Add(new(0x20, 0, 0, 0)); }
    }

    [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
    internal static partial int Prctl(int option, nint a, nint b, nint c, nint d);
}
