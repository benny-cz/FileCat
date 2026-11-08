using System.ComponentModel;
using System.Runtime.InteropServices;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsAnalysisPrivateDirectory
{
    private static void KeepPublishedStaging(SafeHandle file)
    {
        // FILE_DISPOSITION_ON_CLOSE without FILE_DISPOSITION_DELETE clears the per-open
        // on-close state. Unlike classic disposition, this applies to FILE_DELETE_ON_CLOSE.
        // https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/ntddk/ns-ntddk-_file_disposition_information_ex
        uint flags = 8;
        if (!SetStagingDisposition(file, 21, &flags, sizeof(uint)))
            throw new IOException("The published inventory's cleanup state could not be confirmed.", new Win32Exception(Marshal.GetLastPInvokeError()));
    }
    [LibraryImport("kernel32.dll", EntryPoint = "SetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetStagingDisposition(SafeHandle file, int informationClass, void* buffer, uint bytes);
}
