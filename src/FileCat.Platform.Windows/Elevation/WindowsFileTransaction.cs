using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Elevation;

/// <summary>Isolates clear NTFS file operations from Windows EFS and stream changes.</summary>
internal sealed partial class WindowsFileTransaction : IDisposable
{
    private const uint SupportsEncryption = 0x00020000;
    private const uint VolumeNameGuid = 1;
    private const uint OpenReparsePoint = 0x00200000;
    private readonly SafeFileHandle transaction;
    private SafeFileHandle? sourcePin;

    internal WindowsFileTransaction(string description = "FileCat administrator copy")
    {
        transaction = CreateTransaction(0, 0, 0, 0, 0, 0, description);
        if (!transaction.IsInvalid) return;
        int error = Marshal.GetLastPInvokeError();
        transaction.Dispose();
        throw new Win32Exception(error);
    }

    internal static bool CanEncrypt(SafeFileHandle file)
    {
        if (!GetVolumeInformationByHandle(file, 0, 0, out _, out _, out uint flags, 0, 0))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        return (flags & SupportsEncryption) != 0;
    }

    internal static WindowsFileTransaction? PinSource(SafeFileHandle expected, string protectedMessage)
    {
        if (!CanEncrypt(expected)) return null;
        var scope = new WindowsFileTransaction();
        try
        {
            scope.sourcePin = scope.OpenExisting(HeldPath(expected), NtFile.GenericRead | NtFile.Synchronize, NtFile.ShareRead,
                "Hold the copy source against EFS changes");
            RequireOrdinaryFile(scope.sourcePin, protectedMessage);
            if (ProtectedHexFile.Identity(scope.sourcePin) != ProtectedHexFile.Identity(expected))
                throw new IOException("The source changed while the administrator helper was holding its EFS state.");
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    internal static string HeldPath(SafeFileHandle handle) => ElevationPaths.FinalPath(handle, VolumeNameGuid)
        ?? throw new Win32Exception(Marshal.GetLastPInvokeError());

    internal SafeFileHandle OpenExisting(string path, uint access, uint share, string operation) =>
        Open(path, access, share, disposition: 3 /* OPEN_EXISTING */, operation);

    internal SafeFileHandle CreateFile(string path, uint access, uint share, string operation) =>
        Open(path, access, share, disposition: 1 /* CREATE_NEW */, operation);

    private SafeFileHandle Open(string path, uint access, uint share, uint disposition, string operation)
    {
        var file = CreateFileTransacted(path, access, share, 0, disposition, OpenReparsePoint, 0, transaction, 0, 0);
        if (!file.IsInvalid) return file;
        int error = Marshal.GetLastPInvokeError();
        file.Dispose();
        throw new Win32Exception(error, $"{operation} ({error}): {new Win32Exception(error).Message}");
    }

    internal static void RequireOrdinaryFile(SafeFileHandle handle, string encryptedMessage)
    {
        var attributes = (FileAttributes)NtFile.GetBasic(handle).FileAttributes;
        if (attributes.HasFlag(FileAttributes.Encrypted)) throw new IOException(encryptedMessage);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 || NtFile.IsLink(handle))
            throw new IOException("The administrator copy requires an ordinary file at this path.");
    }

    internal static void RemoveOnClose(SafeFileHandle old, bool clearReadOnly = false)
    {
        if (clearReadOnly)
        {
            // The administrator copy's old object is already under a private transaction name.
            // Its attribute changes roll back with deletion.
            var basic = NtFile.GetBasic(old);
            if ((basic.FileAttributes & (uint)FileAttributes.ReadOnly) != 0)
            {
                basic.FileAttributes &= ~(uint)FileAttributes.ReadOnly;
                if (basic.FileAttributes == 0) basic.FileAttributes = (uint)FileAttributes.Normal;
                basic.CreationTime = basic.LastAccessTime = basic.LastWriteTime = basic.ChangeTime = 0;
                NtFile.SetBasic(old, basic);
            }
        }
        byte delete = 1;
        if (!SetFileInformationByHandle(old, 4 /* FileDispositionInfo */, ref delete, 1))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    internal void Commit(string? failureMessage = null)
    {
        if (!CommitTransaction(transaction))
        {
            int error = Marshal.GetLastPInvokeError();
            throw new Win32Exception(error, failureMessage ?? $"The administrator copy could not confirm its replacement ({error}). Check the destination before retrying.");
        }
    }

    public void Dispose()
    {
        sourcePin?.Dispose();
        // After closing each file handle, closing the last transaction handle rolls back a pending transaction.
        transaction.Dispose();
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationByHandleW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformationByHandle(SafeFileHandle handle, nint label, uint labelSize,
        out uint serial, out uint maximumNameLength, out uint flags, nint fileSystem, uint fileSystemSize);

    [LibraryImport("ktmw32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateTransaction(nint security, nint unitOfWork, uint options, uint isolation,
        uint isolationFlags, uint timeout, string description);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileTransactedW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandle CreateFileTransacted(string name, uint access, uint share, nint security,
        uint disposition, uint flags, nint template, SafeFileHandle transaction, nint version, nint extended);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetFileInformationByHandle(SafeFileHandle file, int information, ref byte value, uint length);

    [LibraryImport("ktmw32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CommitTransaction(SafeFileHandle transaction);
}
