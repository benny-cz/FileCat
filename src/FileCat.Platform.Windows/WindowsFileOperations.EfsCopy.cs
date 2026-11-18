using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public partial class WindowsFileOperations : IDecryptedCopyReviewProvider
{
    IDecryptedCopyReview IDecryptedCopyReviewProvider.PrepareDecryptedCopy(string source, string destination, bool followLink, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        const uint existing = 3, metadataDirectory = 0x02000000, noFollow = 0x00200000;
        var input = CreateFileForIdentity(Long(source), NtFile.ReadData | NtFile.ReadAttributes | NtFile.Synchronize,
            NtFile.ShareRead, 0, existing, followLink ? 0 : noFollow, 0);
        SafeFileHandle? parent = null;
        bool transferred = false;
        try
        {
            if (input.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
            RequireEfsCopySource(input);
            string parentPath = Path.GetDirectoryName(Path.GetFullPath(destination)) ?? throw new IOException("The output has no destination folder.");
            parent = CreateFileForIdentity(Long(parentPath), NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll, 0, existing,
                metadataDirectory | noFollow, 0);
            if (parent.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError());
            uint attributes = NtFile.GetBasic(parent).FileAttributes;
            if (!NtFile.IsDirectory(attributes) || NtFile.IsReparsePoint(attributes))
                throw new IOException("The destination folder could not be bound to an ordinary directory.");
            var volume = GetVolumeInfo(destination);
            bool allowRemoteFileIndex = volume.IsRemote && volume.FileSystem is { } format &&
                !format.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
            var review = new DecryptedCopyReview(this, source, destination, input, parent, ProtectedHexFile.Identity(input), allowRemoteFileIndex);
            ct.ThrowIfCancellationRequested();
            transferred = true;
            return review;
        }
        catch (Win32Exception ex) { throw new IOException("Windows could not hold the source and destination for review: " + ex.Message, ex); }
        finally { if (!transferred) { input.Dispose(); parent?.Dispose(); } }
    }

    private static void RequireEfsCopySource(SafeFileHandle source)
    {
        uint attributes = NtFile.GetBasic(source).FileAttributes;
        if ((attributes & (uint)FileAttributes.Encrypted) == 0 || NtFile.IsDirectory(attributes) || NtFile.IsReparsePoint(attributes))
            throw new IOException("The source is no longer an ordinary EFS-encrypted file.");
    }

    private sealed class DecryptedCopyReview(WindowsFileOperations owner, string source, string destination,
        SafeFileHandle input, SafeFileHandle parent, FileIdentity inputIdentity, bool allowRemoteFileIndex) : IDecryptedCopyReview
    {
        private bool _used;

        public void Copy(FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
        {
            if (_used || input.IsClosed || parent.IsClosed || !options.AllowDecryptedDestination)
                throw new InvalidOperationException("Review the bound decrypted copy again before making another attempt.");
            _used = true;
            owner.CopyFileCore(source, destination, options, progress, ct, this);
        }

        internal void CheckStream(nint sourceHandle, nint destinationHandle)
        {
            using var actualSource = new SafeFileHandle(sourceHandle, ownsHandle: false);
            using var actualDestination = new SafeFileHandle(destinationHandle, ownsHandle: false);
            try
            {
                RequireEfsCopySource(actualSource);
                if (ProtectedHexFile.Identity(actualSource) != inputIdentity)
                    throw new IOException("Windows opened a different source from the reviewed EFS file.");
                uint parentAttributes = NtFile.GetBasic(parent).FileAttributes;
                if (!NtFile.IsDirectory(parentAttributes) || NtFile.IsReparsePoint(parentAttributes))
                    throw new IOException("The reviewed output folder changed before the copy.");
                int status = NtFile.TryOpen(parent, Path.GetFileName(destination),
                    NtFile.ReadAttributes | NtFile.Synchronize, NtFile.ShareAll, NtFile.Open, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint,
                    out var opened);
                using var expectedDestination = opened;
                if (status < 0)
                    throw new IOException("The output could not be opened under the reviewed folder.",
                        NtFile.Error(status, "Open the reviewed output"));
                if (NtFile.IsReparsePoint(NtFile.GetBasic(expectedDestination).FileAttributes) ||
                    !SameDestination(expectedDestination, actualDestination))
                    throw new IOException("Windows opened a different destination from the reviewed copy.");
            }
            catch (Win32Exception ex) { throw new IOException("Windows could not verify the reviewed copy handles: " + ex.Message, ex); }
        }

        private bool SameDestination(SafeFileHandle expected, SafeFileHandle actual)
        {
            try
            {
                var identity = ProtectedHexFile.Identity(expected);
                if (identity.VolumeSerial == 0 || identity.FileId.All(digit => digit == '0'))
                    throw new IOException("Windows did not provide a usable destination file identity.");
                return identity == ProtectedHexFile.Identity(actual);
            }
            catch (Win32Exception ex) when (allowRemoteFileIndex && ex.NativeErrorCode is 1 or 50 or 87)
            {
                // Some SMB servers provide the documented 64-bit file index but reject FileIdInfo.
                // Compare simultaneous handles, and reject missing fields rather than treating them as equality.
                var expectedIndex = ReadRemoteCopyIndex(expected);
                var actualIndex = ReadRemoteCopyIndex(actual);
                return expectedIndex == actualIndex;
            }
        }

        public void Dispose()
        {
            try { input.Dispose(); } finally { parent.Dispose(); }
        }
    }

    private static (uint Volume, ulong File) ReadRemoteCopyIndex(SafeFileHandle file)
    {
        if (!GetEfsCopyFileInformation(file, out var native))
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        ulong index = ((ulong)native.IndexHigh << 32) | native.IndexLow;
        if (native.VolumeSerial == 0 || index == 0)
            throw new IOException("The remote destination did not provide a usable file identity.");
        return (native.VolumeSerial, index);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EfsCopyFileInformation
    {
        internal uint Attributes;
        internal uint CreatedLow, CreatedHigh, AccessedLow, AccessedHigh, WrittenLow, WrittenHigh;
        internal uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetEfsCopyFileInformation(SafeFileHandle file, out EfsCopyFileInformation info);
}
