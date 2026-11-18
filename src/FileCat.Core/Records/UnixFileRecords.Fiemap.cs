using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using FileCat.Core.Inspect;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Records;

public sealed unsafe partial class UnixFileRecords
{
    internal const int FiemapRecordsPerQuery = 512;
    internal const int FiemapQueryLimit = 256;
    internal const int FiemapRecordLimit = 100_000;
    private const int FiemapHeader = 32, FiemapRecordBytes = 56;
    private const uint FiemapLast = 1, FiemapShared = 0x2000;
    private const string FiemapSource = "FIEMAP on the held regular file with sync disabled";

    internal readonly record struct LinuxMappedExtent(ulong Logical, ulong Physical, ulong Length, uint Flags)
    {
        internal bool HasKnownLocation => (Flags & ~(FiemapLast | FiemapShared)) == 0 && Physical != 0;
    }

    internal static ObservedFact<IReadOnlyList<LinuxMappedExtent>> ReadLinuxPinnedExtents(int pin, DetailsIdentity expected, CancellationToken ct)
    {
        var (file, state, reason) = OpenLinuxMetadataDescriptor(pin, expected, FileObjectKind.File, ct);
        using var descriptor = file;
        return file is null ? FiemapUnavailable(state, reason!) : ReadLinuxExtents(file, ct);
    }

    private static ObservedFact<IReadOnlyList<LinuxMappedExtent>> ReadLinuxExtents(SafeFileHandle descriptor, CancellationToken ct) =>
        ReadFiemapPages(buffer =>
        {
            fixed (byte* data = buffer)
                return Ioctl((int)descriptor.DangerousGetHandle(), 0xc020660b /* FS_IOC_FIEMAP */, data) == 0 ? 0 : Marshal.GetLastPInvokeError();
        }, ct);

    private static (SafeFileHandle? File, FactState State, string? Reason) OpenLinuxMetadataDescriptor(
        int pin, DetailsIdentity expected, FileObjectKind kind, CancellationToken ct)
    {
        if (kind is not (FileObjectKind.File or FileObjectKind.Directory) || !MatchesLinuxIdentity(pin, expected, kind, ct))
            return (null, FactState.Changed, "The held regular file or directory could not be verified.");
        ct.ThrowIfCancellationRequested();
        int proc = Open("/proc/self/fd", 0x200000 | 0x80000 /* O_PATH | O_CLOEXEC */);
        int error = proc < 0 ? Marshal.GetLastPInvokeError() : 0;
        if (proc < 0)
        {
            ct.ThrowIfCancellationRequested();
            return (null, LinuxMetadataFailure(error), $"The process descriptor directory is unavailable (errno {error}).");
        }
        try
        {
            ct.ThrowIfCancellationRequested();
            byte* filesystem = stackalloc byte[256];
            bool procfs = DetailsFstatfs(proc, filesystem) == 0 && *(nint*)filesystem == 0x9fa0 /* PROC_SUPER_MAGIC */;
            ct.ThrowIfCancellationRequested();
            if (!procfs) return (null, FactState.Unsupported, "The process descriptor directory is unavailable on procfs.");
            // In procfs the numeric descriptor is a kernel reference to the already pinned object. Keep the pin
            // open across the reopen and never consult the original, potentially replaced pathname here.
            int file = DetailsOpenAt(proc, pin.ToString(CultureInfo.InvariantCulture), 0x80000 | 0x800 | 0x100 /* O_CLOEXEC | O_NONBLOCK | O_NOCTTY */);
            error = file < 0 ? Marshal.GetLastPInvokeError() : 0;
            if (file < 0)
            {
                ct.ThrowIfCancellationRequested();
                return (null, LinuxMetadataFailure(error), $"The pinned object could not be reopened for its metadata query (errno {error}).");
            }
            var descriptor = new SafeFileHandle((nint)file, ownsHandle: true);
            try
            {
                if (MatchesLinuxIdentity(file, expected, kind, ct)) return (descriptor, FactState.Known, null);
            }
            catch { descriptor.Dispose(); throw; }
            descriptor.Dispose();
            return (null, FactState.Changed, "The reopened descriptor does not identify the held object.");
        }
        finally { Close(proc); }
    }

    private static bool MatchesLinuxIdentity(int fd, DetailsIdentity expected, FileObjectKind kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Span<byte> bytes = stackalloc byte[256];
        bytes.Clear();
        bool success;
        fixed (byte* data = bytes) success = Statx(fd, "", 0x1000 | 0x100 | 0x800, 0x101, data) == 0;
        ct.ThrowIfCancellationRequested();
        return success && (BinaryPrimitives.ReadUInt32LittleEndian(bytes) & 0x101) == 0x101 &&
            KindFromMode(BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..])) == kind && LinuxIdentity(bytes) == expected;
    }

    internal static ObservedFact<IReadOnlyList<LinuxMappedExtent>> ReadFiemapPages(Func<byte[], int> query, CancellationToken ct)
    {
        var buffer = new byte[FiemapHeader + FiemapRecordsPerQuery * FiemapRecordBytes];
        var extents = new List<LinuxMappedExtent>();
        ulong cursor = 0;
        ObservedFact<IReadOnlyList<LinuxMappedExtent>> Result(FactState state, string? reason = null) =>
            new(extents.Count > 0 && state != FactState.Known ? FactState.Partial : state, extents.AsReadOnly(), FactScope.ExtentSample, FiemapSource, reason);

        for (int call = 0; call < FiemapQueryLimit; call++)
        {
            ct.ThrowIfCancellationRequested();
            Array.Clear(buffer); // fm_flags is zero and never requests FIEMAP_FLAG_SYNC.
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, cursor);
            BinaryPrimitives.WriteUInt64LittleEndian(buffer.AsSpan(8), ulong.MaxValue - cursor);
            BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(24), FiemapRecordsPerQuery);
            int error = query(buffer);
            ct.ThrowIfCancellationRequested();
            if (error != 0) return Result(LinuxMetadataFailure(error), $"The mapping query failed with errno {error}.");
            uint mapped = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(20));
            if (mapped > FiemapRecordsPerQuery) return Result(FactState.Failed, "The kernel returned an invalid extent count.");
            int pageStart = extents.Count;
            int remaining = FiemapRecordLimit - pageStart;
            bool last = false;
            for (int i = 0; i < mapped && i < remaining; i++)
            {
                int at = FiemapHeader + i * FiemapRecordBytes;
                ulong logical = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(at));
                ulong physical = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(at + 8));
                ulong length = BinaryPrimitives.ReadUInt64LittleEndian(buffer.AsSpan(at + 16));
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(at + 40));
                last = (flags & FiemapLast) != 0;
                var extent = new LinuxMappedExtent(logical, physical, length, flags);
                if (logical < cursor || logical > long.MaxValue || length == 0 || length > (ulong)long.MaxValue - logical ||
                    last && i + 1 != mapped || extent.HasKnownLocation && length > ulong.MaxValue - physical)
                {
                    extents.RemoveRange(pageStart, extents.Count - pageStart);
                    return Result(FactState.Failed, "The mapping contains an overlapping, non-advancing or out-of-range extent.");
                }
                extents.Add(extent);
                cursor = logical + length;
            }
            if (mapped <= remaining && (last || mapped < FiemapRecordsPerQuery)) return Result(FactState.Known);
            if (extents.Count == FiemapRecordLimit)
                return Result(FactState.Partial, $"The mapping exceeds the {FiemapRecordLimit:N0}-record inspection limit.");
        }
        return Result(FactState.Partial, $"The mapping exceeded the {FiemapQueryLimit}-query inspection limit.");
    }

    private static FactState LinuxMetadataFailure(int error) => error switch
    {
        1 or 13 or 126 => FactState.Denied,
        25 or 38 or 95 => FactState.Unsupported,
        _ => FactState.Failed,
    };

    private static ObservedFact<IReadOnlyList<LinuxMappedExtent>> FiemapUnavailable(FactState state, string reason) =>
        new(state, Array.Empty<LinuxMappedExtent>(), FactScope.ExtentSample, FiemapSource, reason);

    private static FileDetailsReport AddLinuxExtents(FileDetailsReport report, ObservedFact<IReadOnlyList<LinuxMappedExtent>> observation)
    {
        var extents = observation.Value!;
        bool complete = observation.State == FactState.Known;
        string count = complete || extents.Count > 0 ? extents.Count.ToString("N0") : "Unavailable";
        string sharing = extents.Any(extent => (extent.Flags & FiemapShared) != 0) ? "Shared extents reported"
            : count == "Unavailable" ? "Unavailable" : "No returned record reports sharing";
        const int displayLimit = 40;
        var section = new InspectionSection("Main-stream extent records", [("Returned extent records", count),
            ("Inventory", complete ? "Complete" : observation.State.ToString()), ("Extent sharing", sharing), ("Source", observation.Source)])
        {
            Table = extents.Count == 0 ? null : new InspectionTable(["Logical bytes", "Length", "Physical bytes", "FIEMAP flags"],
                extents.Take(displayLimit).Select(extent => new[] { extent.Logical.ToString("N0"), extent.Length.ToString("N0"),
                    extent.HasKnownLocation ? extent.Physical.ToString("N0") : "Physical location unavailable", $"0x{extent.Flags:X}" }).ToArray())
            { More = extents.Count > displayLimit ? $"Showing the first {displayLimit} returned records." : null },
            Lines = observation.Reason is { } reason ? [reason] : [],
        };
        return report with
        {
            Technical = report.Technical with { Sections = [.. report.Technical.Sections, section],
                Warnings = ["Not acquired: supplementary-data values and previews, raw volumes and journals, security value collectors and hard-link names."] },
        };
    }

    [LibraryImport("libc", EntryPoint = "openat", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int DetailsOpenAt(int directory, string path, int flags);

    [LibraryImport("libc", EntryPoint = "fstatfs", SetLastError = true)]
    private static partial int DetailsFstatfs(int fd, byte* buffer);
}
