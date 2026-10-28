using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using FileCat.Core.FileSystem;

namespace FileCat.Platform.Windows.Mtp;

/// <summary>Retire only the bytes uploaded to the device, using the held file handle for deletion.</summary>
internal static class MtpMoveSafety
{
    internal static void RetireVerifiedSource(string source, byte[] uploadedDigest, FileIdentity uploadedIdentity)
    {
        string held = source + ".filecat-move-" + Guid.NewGuid().ToString("N") + ".hold";
        File.Move(source, held);
        bool retired = false;
        try
        {
            if ((File.GetAttributes(held) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The source changed to a link before Move could retire it.");

            // Read and mark the same held object for deletion. A concurrent pathname replacement cannot
            // redirect SetFileInformationByHandle to another object.
            using var handle = CreateFileW(held, 0x8001_0000 /* GENERIC_READ | DELETE */,
                0x0000_0001 /* FILE_SHARE_READ */, 0, 3 /* OPEN_EXISTING */,
                0x0200_0000 /* FILE_FLAG_OPEN_REPARSE_POINT */, 0);
            if (handle.IsInvalid) throw new IOException("The moved source could not be held for verification.", new Win32Exception(Marshal.GetLastWin32Error()));
            if (ProtectedHexFile.Identity(handle) != uploadedIdentity)
                throw new IOException("The source file object changed after upload; the replacement was retained.");
            using var input = new FileStream(handle, FileAccess.Read, 1024 * 1024);
            byte[] heldDigest = SHA256.HashData(input);
            if (!CryptographicOperations.FixedTimeEquals(uploadedDigest, heldDigest))
                throw new IOException("The source pathname changed after upload; the replacement was retained.");
            var disposition = new FileDispositionInfo { DeleteFile = 1 };
            if (!SetFileInformationByHandle(handle, 4 /* FileDispositionInfo */, ref disposition, Marshal.SizeOf<FileDispositionInfo>()))
                throw new IOException("The verified source could not be retired.", new Win32Exception(Marshal.GetLastWin32Error()));
            retired = true;
        }
        finally
        {
            if (!retired && File.Exists(held))
            {
                if (File.Exists(source))
                    throw new IOException($"The held source remains at {held} because the original name is occupied; review it before deleting anything.");
                try { File.Move(held, source); }
                catch (IOException ex)
                {
                    throw new IOException($"The held source remains at {held}; review it before deleting anything.", ex);
                }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileDispositionInfo { public int DeleteFile; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, nint security,
        uint disposition, uint flags, nint template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass,
        ref FileDispositionInfo information, int size);
}
