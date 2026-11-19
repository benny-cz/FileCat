using FileCat.Core.HiddenData;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsHiddenData
{
    // The original transaction excludes EA writers. NTFS serves the EA values on a separate ordinary handle.
    internal static byte[]? ReadArchiveCopyEas(SafeFileHandle file)
    {
        byte[] buffer = new byte[128 * 1024];
        int length;
        fixed (byte* data = buffer)
        {
            int status = NtQueryEaFile(file, out var io, data, (uint)buffer.Length, 0, null, 0, null, 1);
            if (status is NoEas or NoMoreEas) return [];
            if (status != 0 || io.Information <= 0 || io.Information > buffer.Length) return null;
            length = (int)io.Information;
        }

        var bytes = buffer.AsSpan(0, length);
        var attributes = ParseEaInventory(bytes);
        if (attributes.State != HiddenInventoryState.Complete || attributes.Items.Any(item => item.Role != HiddenMetadataRole.Ordinary || item.Size == 0))
            return null;
        byte[] result = bytes.ToArray();
        fixed (byte* data = buffer)
        {
            int status = NtQueryEaFile(file, out _, data, (uint)buffer.Length, 0, null, 0, null, 0);
            return status == NoMoreEas ? result : null;
        }
    }

    internal static void WriteArchiveCopyEas(SafeFileHandle file, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) return;
        fixed (byte* data = bytes)
        {
            int status = NtSetEaFile(file, out _, data, (uint)bytes.Length);
            if (status != 0) throw NtFile.Error(status, "Preserve the reviewed ZIP extended attributes");
        }
    }
}
