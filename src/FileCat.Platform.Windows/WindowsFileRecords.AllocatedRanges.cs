using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsFileRecords
{
    internal const int AllocatedRangeRecordLimit = 100_000;
    internal const int AllocatedRangeQueryLimit = 256;
    private const string RangeSource = "FSCTL_QUERY_ALLOCATED_RANGES on the held main stream";

    internal static ObservedFact<IReadOnlyList<WindowsStorageTransforms.AllocatedRange>> ReadAllocatedRangePages(long eof,
        Func<long, long, byte[], (int Error, int Returned)> query, CancellationToken ct)
    {
        const int recordBytes = 16;
        var ranges = new List<WindowsStorageTransforms.AllocatedRange>();
        var output = new byte[16 * 1024];
        long cursor = 0;
        ObservedFact<IReadOnlyList<WindowsStorageTransforms.AllocatedRange>> Result(FactState state, string? reason = null) =>
            new(ranges.Count > 0 && state != FactState.Known ? FactState.Partial : state,
                ranges.AsReadOnly(), FactScope.MainData, RangeSource, reason);

        ct.ThrowIfCancellationRequested();
        if (eof < 0) return Result(FactState.Failed, "The main-stream logical size is invalid.");
        if (eof == 0) return Result(FactState.Known);
        for (int call = 0; call < AllocatedRangeQueryLimit; call++)
        {
            ct.ThrowIfCancellationRequested();
            var (error, returned) = query(cursor, eof - cursor, output);
            ct.ThrowIfCancellationRequested();
            if (error is not (0 or ErrorMoreData))
                return Result(RangeFailure(error), $"The range query failed with Windows error {error}. {new Win32Exception(error).Message}");
            if (returned < 0 || returned > output.Length || returned % recordBytes != 0 || error == ErrorMoreData && returned == 0)
                return Result(FactState.Failed, "Windows returned an invalid allocated-range response length.");
            if (returned == 0 && cursor != 0)
                return Result(FactState.Changed, "The range inventory ended after Windows reported more records.");
            int count = returned / recordBytes;
            int pageStart = ranges.Count;
            int remaining = AllocatedRangeRecordLimit - pageStart;
            for (int i = 0; i < count && i < remaining; i++)
            {
                int at = i * recordBytes;
                long offset = BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(at));
                long length = BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(at + 8));
                if (offset < cursor || offset >= eof || length <= 0 || length > long.MaxValue - offset)
                {
                    ranges.RemoveRange(pageStart, ranges.Count - pageStart);
                    return Result(FactState.Failed, "The inventory contains an overlapping, non-advancing or out-of-range record.");
                }
                cursor = Math.Min(eof, offset + length); // The native terminal range can extend past logical EOF.
                ranges.Add(new(offset, cursor - offset));
            }
            if (error == 0 && count <= remaining) return Result(FactState.Known);
            if (ranges.Count == AllocatedRangeRecordLimit)
                return Result(FactState.Partial, $"The inventory exceeds the {AllocatedRangeRecordLimit:N0}-record inspection limit.");
            if (cursor == eof) return Result(FactState.Failed, "Windows reported additional records after the requested range was exhausted.");
        }
        return Result(FactState.Partial, $"The inventory exceeded the {AllocatedRangeQueryLimit}-query inspection limit.");
    }

    private static InspectionSection AllocatedRangesSection(SafeFileHandle item, ReadOnlySpan<byte> fileId, long eof, CancellationToken ct)
    {
        ObservedFact<IReadOnlyList<WindowsStorageTransforms.AllocatedRange>> Unavailable(FactState state, string reason) =>
            new(state, Array.Empty<WindowsStorageTransforms.AllocatedRange>(), FactScope.MainData, RangeSource, reason);

        ObservedFact<IReadOnlyList<WindowsStorageTransforms.AllocatedRange>> Observe(ReadOnlySpan<byte> expectedId)
        {
            // Read-only metadata query with a data-access requirement. Keep the original handle, exclude observed
            // recall-managed objects, and reopen the same local file without reparse processing.
            ct.ThrowIfCancellationRequested();
            Span<byte> initial = stackalloc byte[24];
            if (!TryRangeFacts(item, initial, eof, ct))
                return Unavailable(FactState.Changed, "The stream's size or eligibility changed before the query.");
            if (WindowsStorageTransforms.SyncRootProblem(item, ct) is { } cloud)
                return Unavailable(FactState.NotInspected, cloud.Message);
            // Local NTFS rejects FILE_FLAG_OPEN_NO_RECALL in ReOpenFile. No original pathname is reopened.
            using var file = ReOpenForRanges(item, FileReadData | FileReadAttributes | Synchronize, ShareAll, OpenReparsePoint);
            int error = file.IsInvalid ? Marshal.GetLastPInvokeError() : 0;
            ct.ThrowIfCancellationRequested();
            if (file.IsInvalid) return Unavailable(RangeFailure(error), $"The held stream could not be reopened for the range query. {new Win32Exception(error).Message}");
            Span<byte> before = stackalloc byte[24], after = stackalloc byte[24], id = stackalloc byte[24];
            bool sameFile = Info(file, 18 /* FileIdInfo */, id) && id.SequenceEqual(expectedId);
            ct.ThrowIfCancellationRequested();
            if (!sameFile)
                return Unavailable(FactState.Changed, "The mapping descriptor did not identify the held file.");
            if (!TryRangeFacts(file, before, eof, ct) || !initial.SequenceEqual(before))
                return Unavailable(FactState.Changed, "The stream's size or eligibility changed before the query.");
            var result = ReadAllocatedRangePages(eof, (cursor, length, output) =>
            {
                Span<byte> input = stackalloc byte[16];
                BinaryPrimitives.WriteInt64LittleEndian(input, cursor);
                BinaryPrimitives.WriteInt64LittleEndian(input[8..], length);
                int status = Ioctl(file, FsctlQueryAllocatedRanges, input, output, out int returned);
                return (status, returned);
            }, ct);
            if (!TryRangeFacts(file, after, eof, ct) || !before.SequenceEqual(after))
                return result with { State = result.Value!.Count > 0 ? FactState.Partial : FactState.Changed,
                    Reason = "The main stream's reported metadata changed during the inventory." };
            return result;
        }

        var observation = Observe(fileId);
        var ranges = observation.Value!;
        bool complete = observation.State == FactState.Known;
        bool available = complete || ranges.Count > 0;
        const int displayLimit = 40;
        return new("Main-stream allocated byte ranges",
            [("Returned ranges", available ? ranges.Count.ToString("N0") : "Unavailable"),
             ("Bytes covered by returned ranges", available ? ranges.Sum(range => range.Length).ToString("N0") : "Unavailable"),
             ("Inventory", complete ? "Complete" : observation.State.ToString()), ("Source", observation.Source)])
        {
            Table = ranges.Count == 0 ? null : new InspectionTable(["Logical offset", "Length"],
                ranges.Take(displayLimit).Select(range => new[] { range.Offset.ToString("N0"), range.Length.ToString("N0") }).ToArray())
            { More = ranges.Count > displayLimit ? $"Showing the first {displayLimit} returned ranges." : null },
            Lines = observation.Reason is { } reason ? [reason, "These ranges can contain zero bytes."] : ["These ranges can contain zero bytes."],
        };
    }

    private static bool TryRangeFacts(SafeFileHandle file, Span<byte> standard, long eof, CancellationToken ct)
    {
        const uint ineligible = (uint)(FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.Directory) | 0x580000u;
        Span<byte> basic = stackalloc byte[40];
        ct.ThrowIfCancellationRequested();
        bool valid = Info(file, 0 /* FileBasicInfo */, basic) && (BinaryPrimitives.ReadUInt32LittleEndian(basic[32..]) & ineligible) == 0;
        ct.ThrowIfCancellationRequested();
        if (!valid) return false;
        standard.Clear();
        bool size = Info(file, 1 /* FileStandardInfo */, standard) && BinaryPrimitives.ReadInt64LittleEndian(standard[8..]) == eof;
        ct.ThrowIfCancellationRequested();
        return size;
    }

    private static FactState RangeFailure(int error) => error switch { 1 or 50 => FactState.Unsupported, 5 => FactState.Denied, _ => FactState.Failed };

    [LibraryImport("kernel32.dll", EntryPoint = "ReOpenFile", SetLastError = true)]
    private static partial SafeFileHandle ReOpenForRanges(SafeFileHandle original, uint access, uint share, uint flags);
}
