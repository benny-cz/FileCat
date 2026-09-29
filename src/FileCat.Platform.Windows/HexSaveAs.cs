using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <param name="BytesWritten">Bytes actually written; less than the length when holes of a sparse source stayed holes.</param>
/// <param name="Sparse">The new file kept the source's unallocated ranges as holes.</param>
/// <param name="OriginMark">The source's Mark-of-the-Web: <c>Copied</c>, <c>None</c> (the source had none), or <c>Lost</c>.</param>
public sealed record HexSaveAsResult(long BytesWritten, bool Sparse, HexOriginMark OriginMark);

public enum HexOriginMark { None, Copied, Lost }

/// <summary>Write a new file from a protected baseline plus sparse edits; never overwrite a target.</summary>
public static partial class HexSaveAs
{
    private const string ZoneStream = ":Zone.Identifier";
    private const int ChunkBytes = 1024 * 1024;
    private const long FreeSpaceMargin = 64L * 1024 * 1024;

    /// <summary>
    /// Copies the edited content to a temporary sibling, flushes it, and publishes it under <paramref name="destination"/>
    /// only if that name is still free. A sparse source keeps its holes where the destination volume supports them.
    /// </summary>
    public static HexSaveAsResult CreateNew(ProtectedHexFile source, HexPatchOverlay overlay, string destination,
        CancellationToken ct = default, IProgress<(long Done, long Total)>? progress = null)
    {
        string full = Path.GetFullPath(destination);
        if (File.Exists(full) || Directory.Exists(full)) throw new IOException("Save As requires a new file name; this one already exists.");
        string directory = Path.GetDirectoryName(full) ?? throw new IOException("Choose a destination inside a folder.");
        var ranges = overlay.FreezeForSave();
        string temp = Path.Combine(directory, ".filecat-hex-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            source.ValidateForSave(ranges);
            var allocated = source.IsSparse ? AllocatedRanges(source.Handle, source.Length) : null;
            var regions = allocated is null ? [(0L, source.Length)] : Merge(allocated, ranges);
            long needed = regions.Sum(r => r.Length);
            long free = FreeBytes(directory);
            if (free >= 0 && free < needed + FreeSpaceMargin)
                throw new IOException($"The destination drive has {Format(free)} free, but the new file needs about {Format(needed)}. Choose another drive or free up space.");
            bool sparse = false;
            long written = 0;
            using (var output = File.OpenHandle(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                if (allocated is not null) sparse = TrySetSparse(output);
                if (!sparse && allocated is not null)
                {
                    // Holes must be written as zeros on a volume without sparse files.
                    regions = [(0L, source.Length)];
                    needed = source.Length;
                    if (free >= 0 && free < needed + FreeSpaceMargin)
                        throw new IOException($"The destination drive cannot keep the file sparse, and writing all {Format(needed)} does not fit in {Format(free)} of free space.");
                }
                var buffer = new byte[ChunkBytes];
                long lastReport = 0;
                foreach (var (start, length) in regions)
                {
                    for (long offset = start, end = start + length; offset < end;)
                    {
                        ct.ThrowIfCancellationRequested();
                        int want = (int)Math.Min(buffer.Length, end - offset);
                        int read = overlay.Read(offset, buffer.AsSpan(0, want));
                        if (read != want) throw new IOException($"Could not read the source at 0x{offset:X}.");
                        RandomAccess.Write(output, buffer.AsSpan(0, read), offset);
                        offset += read;
                        written += read;
                        if (Environment.TickCount64 - lastReport >= 100)
                        {
                            lastReport = Environment.TickCount64;
                            progress?.Report((written, needed));
                        }
                    }
                }
                progress?.Report((written, needed));
                RandomAccess.SetLength(output, source.Length);
                RandomAccess.FlushToDisk(output);
            }
            var mark = CopyOriginMark(source.LocalPath!, temp);
            source.ValidateForSave(ranges);
            File.Move(temp, full, overwrite: false);
            return new HexSaveAsResult(written, sparse, mark);
        }
        finally
        {
            overlay.FinishSave(false);
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>
    /// Mark-of-the-Web is security metadata (plan §8.1): a derived copy of downloaded content keeps it (on Linux and
    /// macOS, the extended attributes that stand for it).
    /// </summary>
    private static HexOriginMark CopyOriginMark(string source, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            var unix = new UnixFileOperations();
            if (unix.ReadOriginMark(source) is not { } origin) return HexOriginMark.None;
            return unix.WriteOriginMark(target, origin) ? HexOriginMark.Copied : HexOriginMark.Lost;
        }
        string? mark;
        try { mark = File.Exists(source + ZoneStream) ? File.ReadAllText(source + ZoneStream) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { mark = null; }
        if (mark is null) return HexOriginMark.None;
        try
        {
            File.WriteAllText(target + ZoneStream, mark);
            return HexOriginMark.Copied;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return HexOriginMark.Lost;
        }
    }

    /// <summary>Allocated ranges plus edited ranges (which may fall into holes), sorted and merged.</summary>
    private static List<(long Start, long Length)> Merge(List<(long Start, long Length)> allocated, IReadOnlyList<HexPatchRange> edits)
    {
        var all = allocated.Concat(edits.Select(e => (e.Offset, (long)e.Original.Length))).OrderBy(r => r.Item1).ToList();
        var merged = new List<(long Start, long Length)>();
        foreach (var (start, length) in all)
        {
            if (merged.Count > 0 && start <= merged[^1].Start + merged[^1].Length)
            {
                var last = merged[^1];
                merged[^1] = (last.Start, Math.Max(last.Length, start + length - last.Start));
            }
            else merged.Add((start, length));
        }
        return merged;
    }

    private static unsafe List<(long Start, long Length)> AllocatedRanges(SafeFileHandle handle, long length)
    {
        const uint QueryAllocatedRanges = 0x000940CF;
        const int MoreData = 234;
        var result = new List<(long, long)>();
        var output = new AllocatedRange[512];
        long from = 0;
        while (from < length)
        {
            var query = new AllocatedRange { Offset = from, Length = length - from };
            uint returned;
            bool ok;
            fixed (AllocatedRange* outPtr = output)
                ok = DeviceIoControl(handle, QueryAllocatedRanges, &query, (uint)sizeof(AllocatedRange), outPtr,
                    (uint)(sizeof(AllocatedRange) * output.Length), out returned, IntPtr.Zero);
            int error = ok ? 0 : Marshal.GetLastPInvokeError();
            if (!ok && error != MoreData) throw new Win32Exception(error);
            int count = (int)(returned / (uint)sizeof(AllocatedRange));
            for (int i = 0; i < count; i++) result.Add((output[i].Offset, output[i].Length));
            if (ok || count == 0) break;
            from = output[count - 1].Offset + output[count - 1].Length;
        }
        return result;
    }

    private static unsafe bool TrySetSparse(SafeFileHandle handle)
    {
        const uint SetSparse = 0x000900C4;
        return DeviceIoControl(handle, SetSparse, null, 0, null, 0, out _, IntPtr.Zero);
    }

    private static long FreeBytes(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            try { return UnixFiles.MountOf(Path.GetFullPath(directory))?.AvailableFreeSpace ?? -1; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return -1; }
        }
        return Native.NativeMethods.GetDiskFreeSpaceEx(directory, out ulong available, out _, out _) ? (long)Math.Min(available, long.MaxValue) : -1;
    }

    private static string Format(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GiB" : $"{bytes / (double)(1L << 20):0.#} MiB";

    // FILE_ALLOCATED_RANGE_BUFFER
    private struct AllocatedRange
    {
        public long Offset;
        public long Length;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint code, void* input, uint inputSize,
        void* output, uint outputSize, out uint returned, IntPtr overlapped);
}
