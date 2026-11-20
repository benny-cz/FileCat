using System.Runtime.InteropServices;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsAnalysisPrivateDirectory
{
    private static void RequireReplacementWritable(SafeHandle file)
    {
        byte* basic = stackalloc byte[40];
        if (!GetFileInformationByHandleEx(file, 0, basic, 40) || (*(uint*)(basic + 32) & 1) != 0)
            throw new IOException("Private replacement requires a writable regular file. Existing data was preserved.");
    }
    // Unlike the general rename helper, there is no classic-rename or read-only override fallback.
    // This method is reachable only from generated-payload, contained private replacement leases.
    private static void RenameReplacement(SafeHandle source, SafeHandle directory, string name)
    {
        byte[] data = new byte[24 + name.Length * 2]; bool held = false;
        try
        {
            directory.DangerousAddRef(ref held);
            fixed (byte* buffer = data)
            {
                *(uint*)buffer = 3; // FILE_RENAME_REPLACE_IF_EXISTS | FILE_RENAME_POSIX_SEMANTICS
                *(nint*)(buffer + 8) = directory.DangerousGetHandle(); *(uint*)(buffer + 16) = (uint)(name.Length * 2);
                fixed (char* characters = name) Buffer.MemoryCopy(characters, buffer + 20, data.Length - 20, name.Length * 2);
                ReplacementIoStatus status;
                int result = SetReplacementInformation(source, &status, buffer, (uint)data.Length, 65);
                if (result is unchecked((int)0xC000000D) or unchecked((int)0xC0000003) or unchecked((int)0xC00000BB))
                    throw new NotSupportedException("This private filesystem does not provide the required atomic replacement operation. Keep the existing analysis and choose a new name.");
                if (result < 0) throw new IOException("Atomic private replacement failed (NTSTATUS 0x" + unchecked((uint)result).ToString("X8") + ").");
            }
        }
        finally { if (held) directory.DangerousRelease(); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ReplacementIoStatus { internal nint Status; internal nuint Information; }
    [LibraryImport("ntdll.dll", EntryPoint = "NtSetInformationFile")]
    private static partial int SetReplacementInformation(SafeHandle file, ReplacementIoStatus* status, void* information, uint length, int informationClass);
}
