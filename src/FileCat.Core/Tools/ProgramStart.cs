using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FileCat.Core.Tools;

/// <summary>Starts a program the user named, so that a file Windows cannot run is a start error, not a wait.</summary>
internal static partial class ProgramStart
{
    private const uint FailCriticalErrors = 0x0001; // SEM_FAILCRITICALERRORS

    /// <summary>
    /// 64-bit Windows answers a 16-bit, DOS or unrecognized image inside CreateProcess with the modal "Unsupported 16-Bit
    /// Application" message box, and CreateProcess returns only once someone closes it, unless the caller fails critical
    /// errors. A process inherits its error mode from its parent (Explorer and Windows PowerShell 5.1 fail critical
    /// errors; a program the shell starts gets the default, which shows the box), so the box depends on how this process
    /// was started. Process.Start holds one lock for the whole process across CreateProcess, so an unanswered box also
    /// stops every other program this process starts. Failing critical errors on this thread, for this call only, turns
    /// the box into an ordinary start error such as ERROR_EXE_MACHINE_TYPE_MISMATCH. A started program does not inherit a
    /// thread's error mode, so it keeps this process's.
    /// </summary>
    internal static Process? Start(ProcessStartInfo start)
    {
        if (!OperatingSystem.IsWindows()) return Process.Start(start);
        uint previous = GetThreadErrorMode();
        bool changed = (previous & FailCriticalErrors) == 0 && SetThreadErrorMode(previous | FailCriticalErrors, out _);
        try { return Process.Start(start); }
        finally { if (changed) SetThreadErrorMode(previous, out _); }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetThreadErrorMode();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetThreadErrorMode(uint mode, out uint previous);
}
