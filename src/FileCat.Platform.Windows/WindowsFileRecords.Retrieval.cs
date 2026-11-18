using System.Buffers.Binary;
using System.ComponentModel;
using FileCat.Core.Inspect;
using FileCat.Core.Records;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsFileRecords
{
    internal const int RetrievalRecordLimit = 4096;
    internal const int RetrievalQueryLimit = 32;
    private const string RetrievalSource = "FSCTL_GET_RETRIEVAL_POINTERS on the held main stream";

    internal static ObservedFact<IReadOnlyList<NtfsRun>> ReadRetrievalPointers(
        Func<long, byte[], (int Error, int Returned)> query, CancellationToken ct)
    {
        const int headerBytes = 16, recordBytes = 16;
        var records = new List<NtfsRun>();
        var output = new byte[16 * 1024];
        long cursor = 0;

        ObservedFact<IReadOnlyList<NtfsRun>> Result(FactState state, string? reason = null) =>
            new(records.Count > 0 && state != FactState.Known ? FactState.Partial : state,
                records.ToArray(), FactScope.ExtentSample, RetrievalSource, reason);

        for (int call = 0; call < RetrievalQueryLimit; call++)
        {
            ct.ThrowIfCancellationRequested();
            var (error, returned) = query(cursor, output);
            ct.ThrowIfCancellationRequested();
            if (error == ErrorHandleEof)
                return cursor == 0 ? Result(FactState.Known)
                    : Result(FactState.Changed, "The mapping ended after Windows reported more records.");
            if (error is not (0 or ErrorMoreData))
            {
                var state = error switch { 1 or 50 => FactState.Unsupported, 5 => FactState.Denied, _ => FactState.Failed };
                return Result(state, $"The mapping query failed with Windows error {error}. {new Win32Exception(error).Message}");
            }
            if (returned < headerBytes || returned > output.Length || (returned - headerBytes) % recordBytes != 0)
                return Result(FactState.Failed, "Windows returned an invalid extent response length.");
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(output);
            if (count != (returned - headerBytes) / recordBytes)
                return Result(FactState.Failed, "The extent count does not match the returned byte count.");
            if (count == 0)
                return error == 0 && cursor == 0 ? Result(FactState.Known)
                    : Result(FactState.Failed, "The mapping query returned no records to continue from.");

            long previous = BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(8));
            if (previous != cursor)
                return Result(FactState.Changed, "The returned mapping did not start at the previous record boundary.");
            int pageStart = records.Count;
            int remaining = RetrievalRecordLimit - pageStart;
            for (int i = 0; i < count && i < remaining; i++)
            {
                int at = headerBytes + i * recordBytes;
                long next = BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(at));
                long lcn = BinaryPrimitives.ReadInt64LittleEndian(output.AsSpan(at + 8));
                if (next <= previous || lcn < -1 || lcn >= 0 && next - previous > long.MaxValue - lcn)
                {
                    records.RemoveRange(pageStart, records.Count - pageStart);
                    return Result(FactState.Failed, "The mapping contains a non-advancing or out-of-range extent.");
                }
                records.Add(new(previous, lcn, next - previous));
                previous = next;
            }
            if (error == 0 && count <= remaining) return Result(FactState.Known);
            if (records.Count == RetrievalRecordLimit)
                return Result(FactState.Partial, $"The mapping exceeds the {RetrievalRecordLimit:N0}-record inspection limit.");
            cursor = previous;
        }
        return Result(FactState.Partial, $"The mapping exceeded the {RetrievalQueryLimit}-query inspection limit.");
    }

    private static InspectionSection RetrievalSection(SafeFileHandle item, CancellationToken ct)
    {
        var observation = ReadRetrievalPointers((cursor, output) =>
        {
            Span<byte> startingVcn = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(startingVcn, cursor);
            int error = Ioctl(item, FsctlGetRetrievalPointers, startingVcn, output, out int returned);
            return (error, returned);
        }, ct);
        var records = observation.Value!;
        bool complete = observation.State == FactState.Known;
        string returnedCount = complete || records.Count > 0 ? records.Count.ToString("N0") : "Unavailable";
        const int displayLimit = 40;
        return new("Main-stream extent records",
            [("Returned extent records", returnedCount),
             ("Inventory", complete ? "Complete" : observation.State.ToString()),
             ("Source", observation.Source)])
        {
            Table = records.Count == 0 ? null : new InspectionTable(["Start VCN", "Clusters", "Volume LCN"],
                records.Take(displayLimit).Select(run => new[] { run.Vcn.ToString("N0"), run.Length.ToString("N0"),
                    run.Lcn < 0 ? "Unallocated or compressed" : run.Lcn.ToString("N0") }).ToArray())
            { More = records.Count > displayLimit ? $"Showing the first {displayLimit} returned records." : null },
            Lines = observation.Reason is { } reason ? [reason] : [],
        };
    }
}
