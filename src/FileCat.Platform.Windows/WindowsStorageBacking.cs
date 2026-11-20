using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>Bounded held-object WOF metadata. Ordinary attributes/reparse APIs may hide transparent backing.</summary>
internal static unsafe partial class WindowsStorageBacking
{
    private const uint GetExternalBacking = 0x90310, GetReparsePoint = 0x900A8;
    private const int InvalidFunction = 1, ObjectNotExternallyBacked = 342, NotAReparsePoint = 4390;

    internal sealed record Observation(bool? External, uint? Provider, string? Reason)
    {
        internal string? Description { get; init; }
    }

    internal static Observation Query(SafeFileHandle file, CancellationToken ct = default) => Query(file, localNtfs: false, ct);

    // The caller has already bound this ordinary, no-follow object to a local NTFS volume.
    internal static Observation QueryLocalNtfs(SafeFileHandle file, CancellationToken ct) => Query(file, localNtfs: true, ct);

    private static Observation Query(SafeFileHandle file, bool localNtfs, CancellationToken ct)
    {
        byte* data = stackalloc byte[128];
        int error = Read(file, GetExternalBacking, data, out uint returned, ct);
        if (localNtfs && error == InvalidFunction && Read(file, GetReparsePoint, data, out _, ct) == NotAReparsePoint)
        {
            // Require a separate negative reparse-point result on the same handle. Check for
            // backing again in case the volume's overlay service became available meanwhile.
            error = Read(file, GetExternalBacking, data, out returned, ct);
            if (error == InvalidFunction) return new(false, null, null);
        }
        if (error != 0) return error == ObjectNotExternallyBacked ? new(false, null, null) : new(null, null, new Win32Exception(error).Message);
        if (returned is < 8 or > 128) return new(null, null, "External-backing metadata returned an invalid length.");
        var payload = new ReadOnlySpan<byte>(data, (int)returned);
        return new(true, BinaryPrimitives.ReadUInt32LittleEndian(payload[4..]), null)
        {
            Description = FileCat.Core.Records.RecordText.ReparseTarget(0x80000017, payload),
        };
    }

    private static int Read(SafeFileHandle file, uint control, byte* data, out uint returned, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        bool success = QueryBacking(file, control, null, 0, data, 128, out returned, 0);
        int error = success ? 0 : Marshal.GetLastPInvokeError();
        ct.ThrowIfCancellationRequested();
        return error;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryBacking(SafeFileHandle file, uint code, void* input, uint inputSize, void* output, uint outputSize, out uint returned, nint overlapped);
}
