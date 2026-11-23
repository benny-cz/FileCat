using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>
/// How a volume's storage is attached, read from its volume device with no data access (STORAGE_DEVICE_DESCRIPTOR).
/// Hot-plug transports can disappear like removable media, which storage changes have not qualified.
/// </summary>
internal static unsafe partial class WindowsStorageDevice
{
    internal enum Attachment { Fixed, HotPlug, Unknown }

    internal readonly record struct Description(Attachment Attachment, int BusType, bool RemovableMedia);

    /// <summary>
    /// USB, IEEE 1394, SD and MMC are hot-plug transports even when their disk reports fixed media. The other known
    /// STORAGE_BUS_TYPE values (SCSI through NVMe over Fabrics, including virtual disks and Storage Spaces) are fixed
    /// attachments; an unknown or future bus type is not assumed to be either.
    /// </summary>
    internal static Attachment Classify(int busType, bool removableMedia) => removableMedia || busType is 4 or 7 or 0xC or 0xD
        ? Attachment.HotPlug
        : busType is >= 1 and <= 0x14 ? Attachment.Fixed : Attachment.Unknown;

    /// <summary>Queries the volume device of a volume GUID ("D" format) without opening it for data.</summary>
    internal static Description Describe(string volumeGuid)
    {
        if (!Guid.TryParseExact(volumeGuid, "D", out var guid)) return new(Attachment.Unknown, -1, false);
        using var volume = CreateFile(@"\\?\Volume{" + guid.ToString("D") + "}", 0, 3 /* FILE_SHARE_READ | FILE_SHARE_WRITE */,
            0, 3 /* OPEN_EXISTING */, 0, 0);
        if (volume.IsInvalid) return new(Attachment.Unknown, -1, false);
        Span<byte> query = stackalloc byte[12]; query.Clear(); // StorageDeviceProperty, PropertyStandardQuery
        Span<byte> output = stackalloc byte[512]; output.Clear();
        uint returned;
        fixed (byte* q = query) fixed (byte* o = output)
            if (!DeviceIoControl(volume, 0x002D1400 /* IOCTL_STORAGE_QUERY_PROPERTY */, q, 12, o, (uint)output.Length, &returned, 0) || returned < 32)
                return new(Attachment.Unknown, -1, false);
        if (BinaryPrimitives.ReadUInt32LittleEndian(output[4..]) < 32) return new(Attachment.Unknown, -1, false);
        int bus = BinaryPrimitives.ReadInt32LittleEndian(output[28..]);
        bool removable = output[10] != 0;
        return new(Classify(bus, removable), bus, removable);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputLength, void* output, uint outputLength,
        uint* returned, nint overlapped);
}
