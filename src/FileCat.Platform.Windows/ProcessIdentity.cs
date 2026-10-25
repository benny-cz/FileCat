using System.Runtime.InteropServices;
using System.Management;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>Read-only executable identity without requesting access to the process's memory or modules.</summary>
public static partial class ProcessIdentity
{
    public readonly record struct ImagePathResult(string? Path, int Error);

    /// <summary>A Win32 image path, or null when the process cannot be identified. Failure never means absence.</summary>
    public static string? ImagePath(int processId) => QueryImagePath(processId).Path;

    /// <summary>Read the launch command of a managed host without requesting VM access to that process.</summary>
    public static string? CommandLine(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0) return null;
        try
        {
            using var search = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            using var results = search.Get();
            foreach (ManagementBaseObject item in results)
                return item["CommandLine"] as string;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or COMException)
        {
            // Unreadable process metadata is never proof that a managed FileCat is absent.
        }
        return null;
    }

    public static bool? IsFileCatManagedHostCommand(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return null;
        // The framework-dependent entry assembly is always named FileCat.dll. A broader
        // match can only delay a scan; a narrow argv parser could miss valid host forms.
        return commandLine.Contains("FileCat.dll", StringComparison.OrdinalIgnoreCase);
    }

    public static bool? CombineAppHostAndManagedCommand(bool? apphost, bool? managed) =>
        apphost == true || managed == true ? true : apphost == false && managed == false ? false : null;

    /// <summary>Retain the Win32 failure code so recovery can distinguish kernel-only entries from inaccessible executables.</summary>
    public static unsafe ImagePathResult QueryImagePath(int processId)
    {
        if (!OperatingSystem.IsWindows() || processId <= 0) return new(null, 87 /* ERROR_INVALID_PARAMETER */);
        using var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, (uint)processId);
        if (process.IsInvalid) return new(null, Marshal.GetLastPInvokeError());
        for (int capacity = 1024; capacity <= 32768; capacity *= 2)
        {
            var image = new char[capacity];
            uint length = (uint)capacity;
            bool success;
            fixed (char* chars = image) success = QueryFullProcessImageName(process, 0, chars, ref length);
            if (success)
                return length > 0 && length < capacity ? new(new string(image, 0, (int)length), 0) : new(null, 0);
            int error = Marshal.GetLastPInvokeError();
            if (error != 122 /* ERROR_INSUFFICIENT_BUFFER */) return new(null, error);
        }
        return new(null, 122);
    }

    /// <summary>
    /// These Windows kernel bookkeeping processes have no user-mode executable and cannot host FileCat.
    /// A same-named executable with an unreadable image remains unknown unless the kernel reports the
    /// characteristic no-image failure in session zero; all other identities stay fail-closed.
    /// </summary>
    public static bool IsKernelOnlyProcess(int processId, string processName, int sessionId, ImagePathResult image) =>
        image.Path is null && (processId == 0 && processName == "Idle" && image.Error == 87 ||
                               processId == 4 && processName == "System" && image.Error == 31 ||
                               processId > 4 && sessionId == 0 && image.Error == 31 &&
                               processName is ("Registry" or "Memory Compression" or "Secure System" or "vmmemCmZygote"));

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* image, ref uint length);
}
