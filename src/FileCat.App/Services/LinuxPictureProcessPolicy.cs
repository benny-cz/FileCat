using System.Runtime.InteropServices;

namespace FileCat.App.Services;

/// <summary>
/// Linux x64 picture workers cannot create another process or execute another program after this boundary.
/// This is a per-worker process-creation restriction, not a filesystem/network or memory sandbox.
/// </summary>
internal static unsafe partial class LinuxPictureProcessPolicy
{
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Filter(ushort code, byte yes, byte no, uint value)
    {
        public readonly ushort Code = code;
        public readonly byte Yes = yes, No = no;
        public readonly uint Value = value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FilterProgram { public ushort Count; public Filter* Filters; }

    /// <summary>Apply before reading encoded bytes or entering a native decoder; refusal stops this worker.</summary>
    internal static void Apply()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64) return;
        const uint Deny = 0x00050001 /* SECCOMP_RET_ERRNO | EPERM */, Allow = 0x7fff0000;
        Filter[] code = [
            // Validate the syscall ABI before interpreting its numbers/arguments. Compat ABIs cannot bypass it.
            new(0x20, 0, 0, 4), new(0x15, 1, 0, 0xc000003e), new(0x06, 0, 0, 0x80000000),
            new(0x20, 0, 0, 0), new(0x45, 0, 1, 0x40000000), new(0x06, 0, 0, Deny),
            new(0x15, 0, 1, 57), new(0x06, 0, 0, Deny), // fork
            new(0x15, 0, 1, 58), new(0x06, 0, 0, Deny), // vfork
            new(0x15, 0, 1, 59), new(0x06, 0, 0, Deny), // execve
            new(0x15, 0, 1, 322), new(0x06, 0, 0, Deny), // execveat
            // clone3 passes flags through a pointer that classic BPF cannot safely inspect. ENOSYS makes libc
            // use clone instead for ordinary threads; its register argument can be checked without a race.
            new(0x15, 0, 1, 435), new(0x06, 0, 0, 0x00050026),
            new(0x15, 0, 3, 56), new(0x20, 0, 0, 16), new(0x45, 1, 0, 0x10000), // CLONE_THREAD
            new(0x06, 0, 0, Deny), new(0x06, 0, 0, Allow),
        ];
        if (Prctl(38 /* PR_SET_NO_NEW_PRIVS */, 1, 0, 0, 0) != 0) Refuse();
        fixed (Filter* filters = code)
        {
            var program = new FilterProgram { Count = (ushort)code.Length, Filters = filters };
            // TSYNC covers runtime threads that existed before the worker entry point. Any nonzero result,
            // including the ID of an unsynchronizable thread, refuses rather than decoding with a partial policy.
            if (Syscall(317 /* seccomp */, 1 /* SET_MODE_FILTER */, 1 /* TSYNC */, (nint)(&program), 0, 0, 0) != 0)
                Refuse();
        }
    }

    private static void Refuse() => throw new IOException("The picture decoder could not restrict process creation.");

    [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
    private static partial int Prctl(int option, nint a, nint b, nint c, nint d);
    [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
    private static partial long Syscall(long number, nint a, nint b, nint c, nint d, nint e, nint f);
}
