using System.Buffers.Binary;
using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

// A fixed-size accumulator for matching namespace paths. Reference statistics are
// descriptive and must never enter the additive allocation/namespace fold twice.
internal sealed class AnalysisPathStatistics
{
    internal const int EncodedBytes = 64;
    private UInt128 _known;
    private ulong _files;
    private readonly ulong[] _missing = new ulong[6];
    internal void Add(AnalysisMeasurement length)
    {
        _files = checked(_files + 1);
        if (length.IsKnown) _known = checked(_known + length.Value!.Value);
        else _missing[(int)length.State] = checked(_missing[(int)length.State] + 1);
    }
    internal void Add(AnalysisLogicalTotals totals)
    {
        _known = checked(_known + totals.KnownBytes); _files = checked(_files + totals.Files);
        foreach (var pair in totals.Missing) _missing[(int)pair.Key] = checked(_missing[(int)pair.Key] + pair.Value);
    }
    internal void Clear() { _known = 0; _files = 0; Array.Clear(_missing); }
    internal AnalysisLogicalTotals Export() => new(_known, _files,
        Enumerable.Range(1, 5).Where(n => _missing[n] != 0).ToImmutableDictionary(n => (MeasurementState)n, n => _missing[n]));
    internal static void Write(Span<byte> bytes, AnalysisLogicalTotals value)
    {
        if (bytes.Length != EncodedBytes) throw Invalid();
        BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)(value.KnownBytes >> 64));
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], (ulong)value.KnownBytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes[16..], value.Files);
        for (int n = 1; n < 6; n++) BinaryPrimitives.WriteUInt64LittleEndian(bytes[(24 + (n - 1) * 8)..], value.Missing.GetValueOrDefault((MeasurementState)n));
    }
    internal static AnalysisLogicalTotals Read(ReadOnlySpan<byte> bytes, ulong maximumFiles)
    {
        if (bytes.Length != EncodedBytes) throw Invalid();
        UInt128 known = ((UInt128)BinaryPrimitives.ReadUInt64BigEndian(bytes) << 64) | BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
        ulong files = BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]), unknown = 0;
        var missing = ImmutableDictionary.CreateBuilder<MeasurementState, ulong>();
        for (int n = 1; n < 6; n++)
        {
            ulong count = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(24 + (n - 1) * 8)..]);
            unknown = checked(unknown + count); if (count != 0) missing.Add((MeasurementState)n, count);
        }
        if (files > maximumFiles || unknown > files || unknown == files && known != 0) throw Invalid();
        return new(known, files, missing.ToImmutable());
    }
    private static InvalidDataException Invalid() => new("Invalid captured path statistics.");
}
