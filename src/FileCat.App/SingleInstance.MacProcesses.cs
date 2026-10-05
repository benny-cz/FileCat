using System.Runtime.InteropServices;

namespace FileCat.App;

public static partial class SingleInstance
{
    // Darwin's short BSD record includes the kernel task and, with arg=1, unreaped zombies.
    // Process.HasExited/MainModule can leave both in a managed snapshot with no executable identity.
    internal static bool? MacProcessCanExecute(int pid)
    {
        try
        {
            int bytes = MacPidInfo(pid, 13 /* PROC_PIDT_SHORTBSDINFO */, 1 /* include zombies */,
                out var info, 64);
            return MacProcessCanExecute(pid, bytes, info);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    internal static bool? MacProcessCanExecute(int pid, int bytes, MacProcessInfo info)
    {
        // Failed, short or inconsistent queries never prove an absent writer. In particular, ESRCH
        // is left unknown here; only a complete native record can exclude the kernel task or a zombie.
        if (pid < 0 || bytes != 64 || info.Pid != (uint)pid) return null;
        if (pid == 0)
            return info.Status == 2 && info.ParentPid == 0 && info.UserId == 0 && (info.Flags & 1) != 0
                ? false : null; // SRUN, PROC_FLAG_SYSTEM: this is the kernel, not a user executable.
        return info.Status switch
        {
            5 => false, // SZOMB: no task remains that can run managed code.
            >= 1 and <= 4 => true, // Creating, runnable, sleeping or stopped: may still write.
            _ => null,
        };
    }

    // proc_bsdshortinfo is 64 bytes. Only the fields used for classification are exposed; native
    // offsets are fixed by Darwin's public sys/proc_info.h rather than the caller's pointer width.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct MacProcessInfo
    {
        [FieldOffset(0)] public uint Pid;
        [FieldOffset(4)] public uint ParentPid;
        [FieldOffset(12)] public uint Status;
        [FieldOffset(32)] public uint Flags;
        [FieldOffset(36)] public uint UserId;
    }

    [LibraryImport("libproc", EntryPoint = "proc_pidinfo", SetLastError = true)]
    private static partial int MacPidInfo(int pid, int flavor, ulong arg, out MacProcessInfo info, int size);
}
