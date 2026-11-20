using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace FileCat.Core.FileSystem;

/// <summary>Preserves known holes in a new Windows sparse file when both volumes support the control codes.</summary>
internal static partial class WindowsSparseCopy
{
    private const uint QueryAllocatedRanges = 0x000940CF, SetSparse = 0x000900C4;
    private const int MoreData = 234, InvalidFunction = 1, NotSupported = 50, InvalidParameter = 87;
    private const long MinimumLength = 1024 * 1024;

    internal static unsafe bool TryCopy(FileStream source, FileStream destination, long length, byte[] buffer,
        bool disablePreallocation, Action<long> advance, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows() || length < MinimumLength) return false;
        if (source.Length != length) throw new IOException("The source file changed size before its copy began.");

        var ranges = new AllocatedRange[256];
        using var copiedRanges = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, length);
        copiedRanges.AppendData(lengthBytes);
        long done = 0;
        bool prepared = false;
        while (done < length)
        {
            ct.ThrowIfCancellationRequested();
            var query = new AllocatedRange(done, length - done);
            uint returned;
            bool ok;
            fixed (AllocatedRange* output = ranges)
                ok = DeviceIoControl(source.SafeFileHandle, QueryAllocatedRanges, &query, (uint)sizeof(AllocatedRange),
                    output, (uint)(ranges.Length * sizeof(AllocatedRange)), out returned, 0);
            int error = ok ? 0 : Marshal.GetLastPInvokeError();
            if (!ok && error != MoreData)
            {
                if (!prepared && error is InvalidFunction or NotSupported or InvalidParameter) return false;
                throw new IOException($"The source file's allocated ranges could not be read (Windows error {error}).");
            }
            if (returned % sizeof(AllocatedRange) != 0 || returned > ranges.Length * sizeof(AllocatedRange))
                throw new IOException("The source file returned malformed allocated ranges.");
            int count = (int)(returned / sizeof(AllocatedRange));
            if (!ok && count == 0) throw new IOException("The source file returned no progress while listing allocated ranges.");

            if (!prepared)
            {
                bool marked = DeviceIoControl(destination.SafeFileHandle, SetSparse, null, 0, null, 0, out _, 0);
                if (!marked)
                {
                    int setError = Marshal.GetLastPInvokeError();
                    if (setError is InvalidFunction or NotSupported or InvalidParameter) return false;
                    throw new IOException($"The destination could not be marked sparse (Windows error {setError}).");
                }
                prepared = true;
                if (!disablePreallocation) destination.SetLength(length);
            }

            for (int i = 0; i < count; i++)
            {
                long start = ranges[i].Offset;
                long rangeLength = ranges[i].Length;
                if (start < 0 || rangeLength <= 0 || start >= length || rangeLength > long.MaxValue - start)
                    throw new IOException("The source file returned an invalid allocated range.");
                // NTFS may round the query length beyond EOF to an allocation boundary.
                long end = Math.Min(length, start + rangeLength);
                // NTFS can round the query start down. Do not copy or report an extent twice.
                start = Math.Max(start, done);
                if (end <= start) continue;
                AppendRange(copiedRanges, start, end);
                if (start > done) { done = start; advance(done); }
                source.Position = start;
                destination.Position = start;
                while (done < end)
                {
                    ct.ThrowIfCancellationRequested();
                    int read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, end - done));
                    if (read <= 0) throw new IOException("The source file ended during its copy.");
                    destination.Write(buffer, 0, read);
                    done += read;
                    advance(done);
                }
            }
            if (ok)
            {
                if (done < length) advance(length);
                break;
            }
            if (done <= query.Offset)
                throw new IOException("The source file's allocated-range listing did not advance.");
        }
        if (source.Length != length) throw new IOException("The source file changed size during its copy.");
        // A writer can allocate a previously skipped hole after the first range query without changing EOF.
        // A second bounded pass rejects a range-map change observed before publication.
        if (!copiedRanges.GetHashAndReset().AsSpan().SequenceEqual(RangeDigest(source, length, ct)) ||
            source.Length != length || (File.GetAttributes(source.SafeFileHandle) & FileAttributes.SparseFile) == 0)
            throw new IOException("The source file's allocated ranges changed during its copy.");
        ct.ThrowIfCancellationRequested();
        destination.SetLength(length); // includes a trailing hole and all-hole files
        return true;
    }

    private static unsafe byte[] RangeDigest(FileStream source, long length, CancellationToken ct)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> lengthBytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(lengthBytes, length);
        digest.AppendData(lengthBytes);
        var ranges = new AllocatedRange[256];
        long done = 0;
        while (done < length)
        {
            ct.ThrowIfCancellationRequested();
            var query = new AllocatedRange(done, length - done);
            uint returned;
            bool ok;
            fixed (AllocatedRange* output = ranges)
                ok = DeviceIoControl(source.SafeFileHandle, QueryAllocatedRanges, &query, (uint)sizeof(AllocatedRange),
                    output, (uint)(ranges.Length * sizeof(AllocatedRange)), out returned, 0);
            int error = ok ? 0 : Marshal.GetLastPInvokeError();
            if (!ok && error != MoreData)
                throw new IOException($"The source file's allocated ranges could not be verified (Windows error {error}).");
            if (returned % sizeof(AllocatedRange) != 0 || returned > ranges.Length * sizeof(AllocatedRange))
                throw new IOException("The source file returned malformed allocated ranges during verification.");
            int count = (int)(returned / sizeof(AllocatedRange));
            if (!ok && count == 0)
                throw new IOException("The source file returned no progress while verifying allocated ranges.");
            long before = done;
            for (int i = 0; i < count; i++)
            {
                long start = ranges[i].Offset;
                long rangeLength = ranges[i].Length;
                if (start < 0 || rangeLength <= 0 || start >= length || rangeLength > long.MaxValue - start)
                    throw new IOException("The source file returned an invalid allocated range during verification.");
                long end = Math.Min(length, start + rangeLength);
                start = Math.Max(start, done);
                if (end <= start) continue;
                AppendRange(digest, start, end);
                done = end;
            }
            if (ok) break;
            if (done <= before)
                throw new IOException("The source file's allocated-range verification did not advance.");
        }
        return digest.GetHashAndReset();
    }

    private static void AppendRange(IncrementalHash digest, long start, long end)
    {
        Span<byte> bytes = stackalloc byte[2 * sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, start);
        BinaryPrimitives.WriteInt64LittleEndian(bytes[sizeof(long)..], end);
        digest.AppendData(bytes);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AllocatedRange(long offset, long length)
    {
        public long Offset = offset;
        public long Length = length;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle handle, uint code, void* input, uint inputSize,
        void* output, uint outputSize, out uint returned, nint overlapped);
}
