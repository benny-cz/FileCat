using System.Buffers.Binary;
using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisComparisonEntities
{
    private readonly record struct Row(ulong First, ulong Count, ulong Object, ulong Parent, AnalysisMeasurement Value, uint Roots, byte Flags);
    private static byte[] Encode(Row row)
    {
        byte[] bytes = new byte[56];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, row.First); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), row.Count);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), row.Object); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), row.Parent);
        BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(32), row.Value.Value ?? 0); BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), row.Roots);
        bytes[52] = (byte)row.Value.State; bytes[53] = row.Flags; return bytes;
    }
    private static Row Decode(byte[] bytes)
    {
        if (bytes.Length != 56 || bytes[54] != 0 || bytes[55] != 0 || (bytes[53] & ~63) != 0 || !Enum.IsDefined((MeasurementState)bytes[52]))
            throw AnalysisIndexedGraph.Invalid();
        UInt128 value = BinaryPrimitives.ReadUInt128LittleEndian(bytes.AsSpan(32)); var state = (MeasurementState)bytes[52];
        if (state != MeasurementState.Known && value != 0) throw AnalysisIndexedGraph.Invalid();
        var row = new Row(BinaryPrimitives.ReadUInt64LittleEndian(bytes), BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(16)), BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(24)),
            state == MeasurementState.Known ? AnalysisMeasurement.Known(value) : AnalysisMeasurement.Missing(state),
            BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(48)), bytes[53]);
        if (row.First == 0 || row.Count == 0 || row.Roots == 0 || state == MeasurementState.Known && (row.Flags & HasArea) == 0)
            throw AnalysisIndexedGraph.Invalid();
        return row;
    }
    internal sealed partial class State
    {
        internal ulong EntityForPathRow(ulong pathRow, CancellationToken token)
        {
            var range = Assignments!.FindRange(AnalysisGraphKeys.PathId(pathRow), token);
            return range.Count == 0 ? 0 : range.Count == 1 ? Assignments.Read(range.First, token).Value : throw AnalysisIndexedGraph.Invalid();
        }
        internal ulong MemberPathRow(ulong entity, ulong offset, CancellationToken token)
        {
            var row = Decode(_rows!.Read(entity, token));
            if (offset >= row.Count) throw new ArgumentOutOfRangeException(nameof(offset));
            return Members!.Read(row.First + offset, token).Value;
        }
        internal AnalysisObject? FirstObject(ulong entity, CancellationToken token)
        { var row = Decode(_rows!.Read(entity, token)); return row.Object == 0 ? null : _graph!.ReadObject(row.Object, token); }
        internal AnalysisComparisonAccountingEntity Read(ulong entity, CancellationToken token, out long cost)
        {
            var row = Decode(_rows!.Read(entity, token)); var evidence = ReadEvidence(entity, token, out cost);
            var path = _graph!.ReadPathRow(Members!.Read(row.First, token).Value, token);
            cost += PathBytes(path); return evidence with { RepresentativePath = path.Id, ModifiedUtc = row.Object == 0 ? path.Times?.ModifiedUtc : evidence.ModifiedUtc, DetailsLoaded = true };
        }
        // Build-time evidence does not expand a representative name; membership is loaded separately.
        internal AnalysisComparisonAccountingEntity ReadEvidence(ulong entity, CancellationToken token, out long cost)
        {
            var row = Decode(_rows!.Read(entity, token));
            var obj = row.Object == 0 ? null : _graph!.ReadObject(row.Object, token);
            cost = 1024L + (obj?.VerifiedIdentity?.Length ?? 0) * 2L + (obj?.VersionEvidence?.Length ?? 0) * 2L;
            return new(entity, null, row.Count, row.Value, (row.Flags & HasArea) != 0 ? row.Parent : null,
                obj?.VerifiedIdentity, (row.Flags & UniformVersion) != 0 ? obj?.VersionEvidence : null,
                obj?.ModifiedUtc, (row.Flags & Fresh) != 0, row.Roots,
                (row.Flags & Directory) != 0, (row.Flags & UniformObject) != 0, (row.Flags & UniformPath) != 0);
        }
        internal AnalysisComparisonAccountingEntity? FindPath(ulong path, CancellationToken token)
        {
            ulong pathRow = _graph!.PathRow(path, token);
            var range = Assignments!.FindRange(AnalysisGraphKeys.PathId(pathRow), token);
            return range.Count == 0 ? null : range.Count == 1 ? Read(Assignments.Read(range.First, token).Value, token, out _) : throw AnalysisIndexedGraph.Invalid();
        }
        internal AnalysisDatasetPage<AnalysisComparisonAccountingEntity> ReadPage(ulong offset, int maximum, CancellationToken token)
        {
            if (offset > Count || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(offset));
            var rows = ImmutableArray.CreateBuilder<AnalysisComparisonAccountingEntity>(); long bytes = 0;
            while (offset + (ulong)rows.Count < Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var row = Read(offset + (ulong)rows.Count + 1, token, out long cost);
                if (cost > AnalysisLimits.BatchBytes) throw new IOException("A comparison accounting row exceeds the bounded page payload.");
                if (cost > AnalysisLimits.BatchBytes - bytes) break;
                rows.Add(row); bytes += cost;
            }
            return new(offset, Count, rows.ToImmutable());
        }
        internal AnalysisDatasetPage<AnalysisPathMetadata> ReadMembers(ulong entity, ulong offset, int maximum, CancellationToken token)
        {
            var row = Decode(_rows!.Read(entity, token));
            if (offset > row.Count || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(offset));
            var paths = ImmutableArray.CreateBuilder<AnalysisPathMetadata>(); long bytes = 0;
            while (offset + (ulong)paths.Count < row.Count && paths.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var path = _graph!.ReadPathRow(Members!.Read(row.First + offset + (ulong)paths.Count, token).Value, token);
                long cost = PathBytes(path);
                if (cost > AnalysisLimits.BatchBytes) throw new IOException("A comparison alias exceeds the bounded page payload.");
                if (cost > AnalysisLimits.BatchBytes - bytes) break;
                paths.Add(AnalysisPathMetadata.From(path)); bytes += cost;
            }
            return new(offset, row.Count, paths.ToImmutable());
        }
    }
}
