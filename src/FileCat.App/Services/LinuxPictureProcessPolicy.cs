using System.Runtime.InteropServices;

namespace FileCat.App.Services;

/// <summary>
/// Linux x64 and ARM64 picture workers cannot create another process or execute another program after this boundary.
/// This is a per-worker process-creation restriction, not a filesystem/network or memory sandbox.
/// </summary>
internal static unsafe partial class LinuxPictureProcessPolicy
{
    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Filter(ushort code, byte yes, byte no, uint value)
    {
        public readonly ushort Code = code;
        public readonly byte Yes = yes, No = no;
        public readonly uint Value = value;
    }

    internal readonly record struct Abi(uint AuditArchitecture, long Seccomp, uint CloneSyscall, uint ExecveSyscall,
        uint ExecveAtSyscall, uint? ForkSyscall, uint? VforkSyscall, bool RejectX32);

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterProgram { public ushort Count; public Filter* Filters; }

    /// <summary>Apply before reading encoded bytes or entering a native decoder; refusal stops this worker.</summary>
    internal static void Apply()
    {
        if (!OperatingSystem.IsLinux()) return;
        Abi abi;
        try { abi = ForArchitecture(RuntimeInformation.ProcessArchitecture); }
        catch (PlatformNotSupportedException) { Refuse(); return; }
        SetNoNewPrivileges("process creation");
        ApplyFilter(abi, BuildFilter(abi), synchronizeThreads: true,
            "The picture decoder could not restrict process creation.");
    }

    internal static void SetNoNewPrivileges(string boundary)
    {
        if (Prctl(38 /* PR_SET_NO_NEW_PRIVS */, 1, 0, 0, 0) != 0)
            throw new IOException($"The picture decoder could not restrict {boundary}.");
    }

    internal static void ApplyFilter(Abi abi, Filter[] code, bool synchronizeThreads, string refusal)
    {
        fixed (Filter* filters = code)
        {
            var program = new FilterProgram { Count = (ushort)code.Length, Filters = filters };
            uint flags = synchronizeThreads ? 1U /* TSYNC */ : 0U;
            // TSYNC covers runtime threads that existed before the worker entry point. The earlier launcher filter
            // deliberately applies only to the thread which is about to exec; that thread becomes the whole worker.
            if (Syscall(abi.Seccomp, 1 /* SET_MODE_FILTER */, (nint)flags, (nint)(&program), 0, 0, 0) != 0)
                throw new IOException(refusal);
        }
    }

    internal static Abi ForArchitecture(Architecture architecture) => architecture switch
    {
        Architecture.X64 => new(0xc000003e, 317, 56, 59, 322, 57, 58, true),
        Architecture.Arm64 => new(0xc00000b7, 277, 220, 221, 281, null, null, false),
        _ => throw new PlatformNotSupportedException($"Linux picture-worker process policy does not support {architecture}."),
    };

    internal static Filter[] BuildFilter(Abi abi)
    {
        const uint Deny = 0x00050001 /* SECCOMP_RET_ERRNO | EPERM */, Allow = 0x7fff0000;
        List<Filter> code = [
            // Validate the syscall ABI before interpreting its numbers/arguments. Compat ABIs cannot bypass it.
            new(0x20, 0, 0, 4), new(0x15, 1, 0, abi.AuditArchitecture), new(0x06, 0, 0, 0x80000000),
            new(0x20, 0, 0, 0),
        ];
        if (abi.RejectX32)
        {
            code.Add(new(0x45, 0, 1, 0x40000000));
            code.Add(new(0x06, 0, 0, Deny));
        }
        AddDenied(abi.ForkSyscall);
        AddDenied(abi.VforkSyscall);
        AddDenied(abi.ExecveSyscall);
        AddDenied(abi.ExecveAtSyscall);
        // clone3 passes flags through a pointer that classic BPF cannot safely inspect. ENOSYS makes libc
        // use clone instead for ordinary threads; its register argument can be checked without a race.
        code.Add(new(0x15, 0, 1, 435));
        code.Add(new(0x06, 0, 0, 0x00050026 /* SECCOMP_RET_ERRNO | ENOSYS */));
        code.Add(new(0x15, 0, 3, abi.CloneSyscall));
        code.Add(new(0x20, 0, 0, 16));
        code.Add(new(0x45, 1, 0, 0x10000)); // CLONE_THREAD
        code.Add(new(0x06, 0, 0, Deny));
        code.Add(new(0x06, 0, 0, Allow));
        return [.. code];

        void AddDenied(uint? syscall)
        {
            if (syscall is not { } number) return;
            code.Add(new(0x15, 0, 1, number));
            code.Add(new(0x06, 0, 0, Deny));
        }
    }

    private static void Refuse() => throw new IOException("The picture decoder could not restrict process creation.");

    [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static partial int Prctl(int option, nint a, nint b, nint c, nint d);
    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial long Syscall(long number, nint a, nint b, nint c, nint d, nint e, nint f);
}
