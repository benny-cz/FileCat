using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>Bounded held-object WOF metadata. Ordinary attributes/reparse APIs may hide transparent backing.</summary>
internal static unsafe partial class WindowsStorageBacking
{
    internal sealed record Observation(bool? External, uint? Provider, string? Reason);
    internal static Observation Query(SafeFileHandle file, CancellationToken ct = default)
    {
        byte* data = stackalloc byte[128];
        ct.ThrowIfCancellationRequested();
        bool success = QueryBacking(file, 0x90310 /* FSCTL_GET_EXTERNAL_BACKING, FILE_ANY_ACCESS */, null, 0, data, 128, out uint returned, 0);
        int error = success ? 0 : Marshal.GetLastPInvokeError();
        ct.ThrowIfCancellationRequested();
        if (!success) return error == 342 /* ERROR_OBJECT_NOT_EXTERNALLY_BACKED */ ? new(false, null, null)
            : new(null, null, new Win32Exception(error).Message);
        if (returned is < 8 or > 128) return new(null, null, "External-backing metadata returned an invalid length.");
        return new(true, BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(data + 4, 4)), null);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryBacking(SafeFileHandle file, uint code, void* input, uint inputSize, void* output, uint outputSize, out uint returned, nint overlapped);
}
