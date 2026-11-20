using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    public async Task<AnalysisCapturedTableRange> CaptureTableRangeAsync(string? groupKey, ulong first, ulong last,
        AnalysisTableSort sort = AnalysisTableSort.ActiveMetric, bool descending = true, CancellationToken cancellation = default)
    {
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        if (groupKey == AnalysisTree.RootKey) groupKey = null;
        if (first > last) throw new ArgumentOutOfRangeException(nameof(first));
        using var admission = AnalysisExplorerAdmission.Acquire();
        return await _owned.UseAsync(async state =>
        {
            var start = await AnalysisIo.Run(_io, _device, token =>
            {
                if (groupKey is null)
                { if (last >= Count) throw new ArgumentOutOfRangeException(nameof(last)); return (Index: state.Order!, Records: state.Summaries!, Prefix: new byte[] { (byte)sort }, Reference: false); }
                var group = state.Group(groupKey, token); bool reference = group.Kind != AnalysisGroupKind.Entries;
                if (last >= group.Members) throw new ArgumentOutOfRangeException(nameof(last));
                if (last - first >= AnalysisLimits.ReviewPaths) throw new InvalidOperationException("Refine the captured table range to at most 100,000 real paths.");
                return (Index: reference ? state.ReferenceOrder! : state.MemberOrder!, Records: reference ? state.ReferencePlacements! : state.MemberPlacements!,
                    Prefix: Join(Hash(groupKey), [(byte)sort]), Reference: reference);
            }, cancellation).ConfigureAwait(false);
            var ids = ImmutableArray.CreateBuilder<ulong>(); ulong position = first;
            if (groupKey is not null)
                while (position <= last)
                    await AnalysisIo.Run(_io, _device, token =>
                    {
                        var range = start.Index.PrefixRange(start.Prefix, token); int records = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                        while (position <= last && records++ < AnalysisLimits.BatchRecords)
                        {
                            token.ThrowIfCancellationRequested(); ulong at = OrderedPosition(start.Index, start.Prefix, range, position++, sort, descending, token);
                            var placement = DecodePlacement(start.Records.Read(start.Index.Read(at, token).Value, token));
                            if (placement.Path == 0 || placement.Reference != start.Reference || !placement.GroupHash.AsSpan().SequenceEqual(start.Prefix.AsSpan(0, 32))) throw Invalid();
                            ids.Add(placement.Path); if (clock.ElapsedMilliseconds >= 20) break;
                        }
                        return true;
                    }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); var identity = Descriptor.Identity;
            return new AnalysisCapturedTableRange(new(identity.SessionId, identity.Epoch, identity.Revision, ids.ToImmutable())
                { ScopeSummary = groupKey is null ? "Captured accounting group rows; members not expanded" : "Captured group member rows" }, first, last, groupKey is null ? last - first + 1 : 0);
        }).ConfigureAwait(false);
    }
}
