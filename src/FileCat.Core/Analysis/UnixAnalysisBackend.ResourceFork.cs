using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace FileCat.Core.Analysis;

public sealed unsafe partial class UnixAnalysisBackend
{
    private const uint ReturnedAttributes = 0x80000000;
    private const uint TotalAllocated = 0x00000004, DataAllocated = 0x00000400, ResourceLength = 0x00001000, ResourceAllocated = 0x00002000;
    private const uint ForkAttributes = TotalAllocated | DataAllocated | ResourceLength | ResourceAllocated;
    private const int ReturnedHeaderBytes = 24, ForkResponseBytes = ReturnedHeaderBytes + 4 * sizeof(long);
    string? IAnalysisDetailedBackend.ExtraAllocationBasis => _apfs ? AnalysisComponents.ResourceForkBasis : null;

    AnalysisEntry IAnalysisDetailedBackend.ReadDetailedRoot(CancellationToken cancellation)
    {
        Guard();
        return ReadResourceAllocation(_root, RootEntry, cancellation);
    }

    IEnumerable<AnalysisEntry> IAnalysisDetailedBackend.ReadDetailedChildren(AnalysisEntry directory, Func<AnalysisEntry, bool> mayMeasure,
        CancellationToken cancellation) => ChildrenCore(directory, _apfs ? mayMeasure : null, cancellation);

    private AnalysisEntry ReadResourceAllocation(int fd, AnalysisEntry file, CancellationToken cancellation)
    {
        if (!_apfs || file is not { Kind: AnalysisKind.File, IncludedByPolicy: true } || file.Cloud == true) return file;
        var measurement = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
        if (file.Compressed == false && file.Identity is not null && file.Allocation.Value is { } reported)
        {
            cancellation.ThrowIfCancellationRequested();
            var requested = new DarwinAttributeList { BitmapCount = 5, Common = ReturnedAttributes, File = ForkAttributes };
            Span<byte> response = stackalloc byte[ForkResponseBytes];
            response.Clear();
            int code;
            fixed (byte* bytes = response)
                code = FGetAttrList(fd, &requested, bytes, (nuint)response.Length, 4 /* FSOPT_REPORT_FULLSIZE */);
            int error = code == 0 ? 0 : Marshal.GetLastPInvokeError();
            cancellation.ThrowIfCancellationRequested();
            if (code == 0)
            {
                measurement = ParseResourceAllocation(response, reported);
                try
                {
                    var current = Stat(fd);
                    if (!Same(file, current) || !current.HasAllocation || reported != (UInt128)current.Blocks * 512 ||
                        (current.Flags & (0x40000000u | 0x20u)) != 0)
                        measurement = AnalysisMeasurement.Missing(MeasurementState.Changed);
                }
                catch (IOException) { measurement = AnalysisMeasurement.Missing(MeasurementState.Failed); }
            }
            else if (error is not (22 or 45 or 78))
                measurement = AnalysisMeasurement.Missing(error == 70 ? MeasurementState.Changed : MeasurementState.Failed);
        }
        return file with { ExtraAllocation = new(AnalysisComponents.ResourceForkComponent, AnalysisComponents.ResourceForkBasis,
            AnalysisComponents.ResourceForkMethod, measurement, IncludedInReportedAllocation: true) };
    }

    internal static AnalysisMeasurement ParseResourceAllocation(ReadOnlySpan<byte> response, UInt128 reported)
    {
        var failed = AnalysisMeasurement.Missing(MeasurementState.Failed);
        var unsupported = AnalysisMeasurement.Missing(MeasurementState.Unsupported);
        if (response.Length < ReturnedHeaderBytes) return failed;
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(response);
        if (length < ReturnedHeaderBytes || length > response.Length) return failed;
        if (BinaryPrimitives.ReadUInt32LittleEndian(response[4..]) != ReturnedAttributes ||
            BinaryPrimitives.ReadUInt32LittleEndian(response[8..]) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(response[12..]) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(response[20..]) != 0)
            return unsupported;
        if (BinaryPrimitives.ReadUInt32LittleEndian(response[16..]) != ForkAttributes) return unsupported;
        if (length != ForkResponseBytes) return failed;
        long total = BinaryPrimitives.ReadInt64LittleEndian(response[24..]);
        long primary = BinaryPrimitives.ReadInt64LittleEndian(response[32..]);
        long forkLength = BinaryPrimitives.ReadInt64LittleEndian(response[40..]);
        long fork = BinaryPrimitives.ReadInt64LittleEndian(response[48..]);
        if (total < 0 || primary < 0 || forkLength < 0 || fork < 0) return failed;
        if ((UInt128)total != reported || (UInt128)primary + (UInt128)fork != reported) return unsupported;
        return AnalysisMeasurement.Known((UInt128)fork);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DarwinAttributeList
    {
        internal ushort BitmapCount, Reserved;
        internal uint Common, Volume, Directory, File, Fork;
    }

    [LibraryImport("libc", EntryPoint = "fgetattrlist", SetLastError = true)]
    private static partial int FGetAttrList(int fd, DarwinAttributeList* list, byte* response, nuint capacity, nuint options);
}
