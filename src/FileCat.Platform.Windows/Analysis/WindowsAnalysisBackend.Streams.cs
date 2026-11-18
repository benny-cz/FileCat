using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.Analysis;

namespace FileCat.Platform.Windows.Analysis;

public sealed unsafe partial class WindowsAnalysisBackend
{
    private const int StreamBufferBytes = 64 * 1024;
    private static readonly UnicodeEncoding StreamNames = new(false, false, true);
    string IAnalysisDetailedBackend.ExtraAllocationBasis => AnalysisComponents.NamedStreamBasis;

    AnalysisEntry IAnalysisDetailedBackend.ReadDetailedRoot(CancellationToken cancellation)
    {
        Guard();
        return ReadNamedAllocation(_root.Value, RootEntry, cancellation);
    }

    IEnumerable<AnalysisEntry> IAnalysisDetailedBackend.ReadDetailedChildren(AnalysisEntry directory, Func<AnalysisEntry, bool> mayMeasure,
        CancellationToken cancellation) => ChildrenCore(directory, mayMeasure, cancellation);

    private AnalysisEntry ReadNamedAllocation(SafeHandle handle, AnalysisEntry file, CancellationToken cancellation)
    {
        if (file is not { Kind: AnalysisKind.File, IncludedByPolicy: true } || file.Cloud == true) return file;
        var measurement = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
        if (_nativeTimePrecisionTicks == 1 && file.Identity is not null && file.LogicalLength.Value is { } length &&
            file.Allocation.Value is { } primary)
        {
            cancellation.ThrowIfCancellationRequested();
            var buffer = ArrayPool<byte>.Shared.Rent(StreamBufferBytes);
            try
            {
                int status;
                StreamQueryStatus io = default;
                fixed (byte* destination = buffer)
                    status = QueryNamedStreamMetadata(handle, &io, destination, StreamBufferBytes, 22 /* FileStreamInformation */);
                cancellation.ThrowIfCancellationRequested();
                if (status == 0 && io.Information <= StreamBufferBytes)
                {
                    measurement = ParseNamedAllocation(buffer.AsSpan(0, (int)io.Information), length, primary);
                    var current = Observe(handle, [], [], _namespace, _nativeTimePrecisionTicks);
                    if (current.Identity != file.Identity || current.Version != file.Version || current.Allocation != file.Allocation ||
                        current.Kind != AnalysisKind.File || current.Cloud != false)
                        measurement = AnalysisMeasurement.Missing(MeasurementState.Changed);
                }
                else if ((uint)status is not (0x80000005 or 0xC0000004 or 0xC0000023 or 0xC0000003 or 0xC00000BB))
                    measurement = AnalysisMeasurement.Missing(MeasurementState.Failed);
            }
            catch (IOException error) when (!AnalysisCloseUnconfirmedException.IsPresent(error))
            {
                measurement = AnalysisMeasurement.Missing(MeasurementState.Failed);
            }
            finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
        }
        return file with { ExtraAllocation = new(AnalysisComponents.NamedStreamComponent, AnalysisComponents.NamedStreamBasis,
            AnalysisComponents.NamedStreamMethod, measurement) };
    }

    internal static AnalysisMeasurement ParseNamedAllocation(ReadOnlySpan<byte> bytes, UInt128 primaryLength, UInt128 primaryAllocation)
    {
        var malformed = AnalysisMeasurement.Missing(MeasurementState.Failed);
        var names = new HashSet<string>(StringComparer.Ordinal);
        UInt128 named = 0;
        bool primary = false;
        for (int offset = 0; offset <= bytes.Length - 24;)
        {
            var record = bytes[offset..];
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(record);
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]);
            long length = BinaryPrimitives.ReadInt64LittleEndian(record[8..]);
            long allocated = BinaryPrimitives.ReadInt64LittleEndian(record[16..]);
            if (size == 0 || (size & 1) != 0 || size > record.Length - 24 || length < 0 || allocated < 0 ||
                next != 0 && ((next & 7) != 0 || next < 24 + size || next > record.Length - 24))
                return malformed;
            string raw;
            try { raw = StreamNames.GetString(record.Slice(24, (int)size)); }
            catch (DecoderFallbackException) { return malformed; }
            if (raw == "::$DATA")
            {
                if (primary) return malformed;
                if (primaryLength != (UInt128)length || primaryAllocation != (UInt128)allocated)
                    return AnalysisMeasurement.Missing(MeasurementState.Unsupported);
                primary = true;
            }
            else
            {
                if (raw.Length <= 7 || raw[0] != ':' || !raw.EndsWith(":$DATA", StringComparison.Ordinal) ||
                    raw.AsSpan(1, raw.Length - 7).IndexOfAny('\0', ':', '\\') >= 0 || raw.Contains('/') || !names.Add(raw))
                    return malformed;
                if (names.Count > AnalysisLimits.Components)
                    return AnalysisMeasurement.Missing(MeasurementState.Unsupported);
                named = checked(named + (UInt128)allocated);
            }
            if (next == 0)
                return primary && record.Length - 24 - size <= 7 ? AnalysisMeasurement.Known(named) : malformed;
            offset += (int)next;
        }
        return malformed;
    }

    private struct StreamQueryStatus { internal nint Status; internal nuint Information; }
    [LibraryImport("ntdll.dll", EntryPoint = "NtQueryInformationFile")]
    private static partial int QueryNamedStreamMetadata(SafeHandle file, StreamQueryStatus* status, byte* buffer, uint length, int informationClass);
}
