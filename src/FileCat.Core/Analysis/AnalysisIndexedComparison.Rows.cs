using System.Buffers.Binary;
using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    private sealed record RowData(ulong Previous, ulong Current, AnalysisChange Change, AnalysisComparisonEvidence Evidence, AnalysisDelta? Delta, byte Reason);
    private static byte[] EncodeRow(RowData row)
    {
        byte[] bytes = new byte[40]; BinaryPrimitives.WriteUInt64LittleEndian(bytes, row.Previous); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), row.Current);
        bytes[16] = (byte)row.Change; bytes[17] = (byte)row.Evidence; bytes[18] = row.Delta.HasValue ? (byte)1 : (byte)0;
        bytes[19] = row.Delta?.Negative == true ? (byte)1 : (byte)0; bytes[20] = row.Reason;
        BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(24), row.Delta?.Magnitude ?? 0); return bytes;
    }
    private static RowData DecodeRow(byte[] bytes)
    {
        if (bytes.Length != 40 || !Enum.IsDefined((AnalysisChange)bytes[16]) || !Enum.IsDefined((AnalysisComparisonEvidence)bytes[17]) ||
            bytes[18] > 1 || bytes[19] > 1 || bytes[20] > 6 || bytes[21] != 0 || bytes[22] != 0 || bytes[23] != 0) throw AnalysisIndexedGraph.Invalid();
        ulong a = BinaryPrimitives.ReadUInt64LittleEndian(bytes), b = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8));
        UInt128 amount = BinaryPrimitives.ReadUInt128LittleEndian(bytes.AsSpan(24));
        if (a == 0 && b == 0 || bytes[18] == 0 && (bytes[19] != 0 || amount != 0)) throw AnalysisIndexedGraph.Invalid();
        return new(a, b, (AnalysisChange)bytes[16], (AnalysisComparisonEvidence)bytes[17], bytes[18] == 1 ? new(bytes[19] == 1, amount) : null, bytes[20]);
    }
    private static string Explanation(RowData row) => row.Reason switch
    {
        1 => "The measurements, scope, coverage freshness or policy do not support this difference.",
        2 => "Alias groups split, merge or match ambiguously. No zero baseline or identity continuity was inferred.",
        3 => "The earlier alias group has an ambiguous current relationship.",
        4 => "The other inventory did not establish complete comparable coverage here. Missing is unknown, not zero.",
        5 => "Absent from the earlier complete captured policy scope. New observation, not a creation-date assertion.",
        6 => "Absent from the current complete captured policy scope. This historical row has no live target.",
        _ => row.Change switch
        {
            AnalysisChange.Replaced => "A different observed object occupies this path. This is a path-value difference, not continuity of the old file.",
            AnalysisChange.Growing or AnalysisChange.Shrinking => "Observed metric difference; reported allocation is not a promise of consumed or reclaimable physical space.",
            AnalysisChange.Attribution => "Aliases or their common-parent accounting changed with the same observed amount. No storage growth or unchanged-content proof is inferred.",
            AnalysisChange.Moved => "Observed path movement with compatible source identity and version evidence; still no content or current action authority.",
            AnalysisChange.MetadataChanged => "Observed paths or metadata changed. Identity continuity is not established by matching size and timestamps.",
            _ => "No observed metadata change. File contents were not compared."
        }
    };
    private sealed partial class State
    {
        internal AnalysisDatasetPage<AnalysisIndexedComparisonRow> Page(ulong offset, int maximum, CancellationToken token)
        {
            if (offset > Rows!.Count || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(offset));
            var rows = ImmutableArray.CreateBuilder<AnalysisIndexedComparisonRow>(); long bytes = 0;
            while (offset + (ulong)rows.Count < Rows.Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); ulong id = Order!.Read(offset + (ulong)rows.Count + 1, token).Value;
                var row = DecodeRow(Rows.Read(id, token)); long ac = 0, bc = 0;
                var a = row.Previous == 0 ? null : previous.ReadEvidence(row.Previous, token, out ac);
                var b = row.Current == 0 ? null : current.ReadEvidence(row.Current, token, out bc);
                long cost = ac + bc + 1024;
                if (cost > AnalysisLimits.BatchBytes) throw new IOException("A comparison scalar row exceeds the bounded page payload.");
                if (cost > AnalysisLimits.BatchBytes - bytes) break;
                rows.Add(new(id, a, b, row.Change, row.Evidence, row.Delta, Explanation(row))); bytes += cost;
            }
            return new(offset, Rows.Count, rows.ToImmutable());
        }
    }
}
