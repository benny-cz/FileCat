using System.Collections.Immutable;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    internal Task<AnalysisDatasetPage<AnalysisPathMetadata>> ReadMembersBeforeAsync(bool previous, ulong entity, ulong before,
        int maximum = 256, CancellationToken cancellation = default) => OwnerUse(() => _owned.UseAsync(_ =>
            (previous ? _previous : _current).ReadMembersBeforeAsync(entity, before, maximum, cancellation)));
    internal Task<AnalysisDatasetPage<AnalysisIndexedComparisonRow>> ReadBeforeAsync(ulong before, int maximum = 256,
        CancellationToken cancellation = default) => OwnerUse(() => _owned.RunAsync(
            (state, token) => RowsBefore(state, state.Order!, before, maximum, token), cancellation));

    internal sealed partial class Query
    {
        internal Task<AnalysisDatasetPage<AnalysisIndexedComparisonRow>> ReadBeforeAsync(ulong before, int maximum = 256,
            CancellationToken cancellation = default) => _owned.RunAsync(
                (state, token) => state.PreviousPage(before, maximum, token), cancellation);
    }
    private static AnalysisDatasetPage<AnalysisIndexedComparisonRow> RowsBefore(State source, AnalysisSortedIndexReader order,
        ulong before, int maximum, CancellationToken token)
    {
        if (before > order.Count || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(before));
        var rows = ImmutableArray.CreateBuilder<AnalysisIndexedComparisonRow>(); long bytes = 0;
        while ((ulong)rows.Count < before && rows.Count < maximum)
        {
            token.ThrowIfCancellationRequested();
            ulong id = order.Read(before - (ulong)rows.Count, token).Value;
            var row = source.ReadRow(id, token, out long cost);
            if (cost > AnalysisLimits.BatchBytes) throw new IOException("A comparison scalar row exceeds the bounded page payload.");
            if (cost > AnalysisLimits.BatchBytes - bytes) break;
            rows.Add(row); bytes += cost;
        }
        return new(before - (ulong)rows.Count, order.Count, rows.ToImmutable().Reverse().ToImmutableArray());
    }
}
