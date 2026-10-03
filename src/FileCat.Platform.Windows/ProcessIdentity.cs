using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>Read-only executable identity without requesting access to the process's memory or modules.</summary>
public static partial class ProcessIdentity
{
    /// <summary>A Win32 image path, or null when the process cannot be identified. Failure never means absence.</summary>
    public static unsafe string? ImagePath(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0) return null;
        using var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)processId);
        if (process.IsInvalid) return null;
        for (int capacity = 1024; capacity <= 32768; capacity *= 2)
        {
            var image = new char[capacity];
            uint length = (uint)capacity;
            bool success;
            fixed (char* chars = image) success = QueryFullProcessImageName(process, 0, chars, ref length);
            if (success)
                return length > 0 && length < capacity ? new string(image, 0, (int)length) : null;
            if (Marshal.GetLastPInvokeError() != 122 /* ERROR_INSUFFICIENT_BUFFER */) return null;
        }
        return null;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* image, ref uint length);
}
