using System.Buffers.Binary;
using System.Runtime.InteropServices;
using FileCat.Core.Jobs;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

internal sealed unsafe partial class WindowsStorageTransforms
{
    internal const int SparseRangeLimit = 4096;

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct AllocatedRange(long Offset, long Length);

    internal sealed record SparseLayout(long Length, long Allocation, AllocatedRange[] Ranges)
    {
        internal static bool SameHoles(SparseLayout? first, SparseLayout? second) =>
            first is null ? second is null :
            second is not null && first.Length == second.Length && first.Ranges.AsSpan().SequenceEqual(second.Ranges);
    }

    // Observe only this held data stream. A different stream's sparse state need not appear on this handle.
    internal static SparseLayout? ReadSparseLayout(SafeFileHandle file, CancellationToken ct)
    {
        if (!HasSparseFlag(file, ct)) return null;
        var first = ReadStreamSize(file, ct);
        if (first.Length < 0 || first.Allocation < 0)
            throw Refuse("sparse-size", "The held sparse stream returned an invalid length or allocation size.");
        var ranges = first.Length == 0 ? [] : ReadAllocatedRanges(file, first.Length, ct);
        if (!HasSparseFlag(file, ct) || ReadStreamSize(file, ct) != first)
            throw Changed("sparse-observation-changed", "The held sparse stream changed during its allocation query.");
        return new(first.Length, first.Allocation, ranges);
    }

    internal static bool HasSparseFlag(SafeFileHandle file, CancellationToken ct)
    {
        Span<byte> basic = stackalloc byte[40];
        Query(file, 0 /* FileBasicInfo */, basic, ct);
        return (BinaryPrimitives.ReadUInt32LittleEndian(basic[32..]) & (uint)FileAttributes.SparseFile) != 0;
    }

    private static (long Length, long Allocation) ReadStreamSize(SafeFileHandle file, CancellationToken ct)
    {
        Span<byte> standard = stackalloc byte[24];
        Query(file, 1 /* FileStandardInfo */, standard, ct);
        return (BinaryPrimitives.ReadInt64LittleEndian(standard[8..]), BinaryPrimitives.ReadInt64LittleEndian(standard));
    }

    private static AllocatedRange[] ReadAllocatedRanges(SafeFileHandle file, long length, CancellationToken ct)
    {
        const uint rangeSize = 16;
        var input = new AllocatedRange(0, length);
        var output = new AllocatedRange[SparseRangeLimit];
        ct.ThrowIfCancellationRequested();
        uint bytes;
        int error;
        fixed (AllocatedRange* buffer = output)
            error = QuerySparseRanges(file, 0x000940CF /* FSCTL_QUERY_ALLOCATED_RANGES */,
                &input, rangeSize, buffer, SparseRangeLimit * rangeSize, out bytes, 0) ? 0 : Marshal.GetLastPInvokeError();
        ct.ThrowIfCancellationRequested();
        if (error == 234 /* ERROR_MORE_DATA */)
            throw Refuse("sparse-range-limit", $"The held sparse stream has more than {SparseRangeLimit:N0} allocated-range records.");
        if (error != 0) throw NativeFailure(error, "Query the held sparse stream's allocated ranges");
        if (bytes > SparseRangeLimit * rangeSize || bytes % rangeSize != 0)
            throw Refuse("sparse-range-invalid", "Windows returned an invalid allocated-range record length.");

        int retained = 0;
        for (int i = 0; i < bytes / rangeSize; i++)
        {
            var range = output[i];
            long precedingEnd = retained == 0 ? 0 : output[retained - 1].Offset + output[retained - 1].Length;
            if (range.Offset < precedingEnd || range.Offset >= length || range.Length <= 0 || range.Length > long.MaxValue - range.Offset)
                throw Refuse("sparse-range-invalid", "Windows returned an invalid or overlapping allocated range.");
            // NTFS can round the last range past EOF. Compare the logical range union only.
            long end = Math.Min(length, range.Offset + range.Length);
            if (retained != 0 && range.Offset == precedingEnd)
            {
                var prior = output[retained - 1];
                output[retained - 1] = prior with { Length = end - prior.Offset };
            }
            else output[retained++] = range with { Length = end - range.Offset };
        }
        Array.Resize(ref output, retained);
        return output;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QuerySparseRanges(SafeFileHandle file, uint code, AllocatedRange* input, uint inputSize,
        AllocatedRange* output, uint outputSize, out uint returned, nint overlapped);
}
