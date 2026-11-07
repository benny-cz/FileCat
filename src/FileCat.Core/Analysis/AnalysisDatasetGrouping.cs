using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record AnalysisDatasetGroup(AnalysisGroupDefinition Definition, AnalysisGroupKind Kind, AnalysisTotals Totals,
    AnalysisLogicalTotals Logical, ulong Members, ulong Charges);
public sealed record AnalysisDatasetGroupMember(AnalysisPathMetadata Path, AnalysisMeasurement LogicalLength,
    bool AccountingReference, DateTimeOffset? ModifiedUtc, ulong ModifiedPrecisionTicks);

public sealed partial class AnalysisDatasetLease
{
    public Task<AnalysisDatasetGrouping> BuildGroupingAsync(AnalysisMetric metric, AnalysisGroupingDimension dimension,
        AnalysisFilterRequest? request = null, CancellationToken cancellation = default)
    {
        if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        if (!Enum.IsDefined(dimension) || dimension == AnalysisGroupingDimension.FolderHierarchy)
            throw new ArgumentOutOfRangeException(nameof(dimension));
        var admission = AnalysisExplorerAdmission.Acquire();
        try { return Run(); } catch { admission.Dispose(); throw; }
        async Task<AnalysisDatasetGrouping> Run()
        {
            using (admission)
                return await _owned.UseAsync(async reader =>
                {
                    using var query = await AnalysisIndexedFilter.CreateAsync(reader, request ?? new(""), _io, _device, cancellation).ConfigureAwait(false);
                    await using var charges = await AnalysisObjectChargeIndex.BuildAsync(reader, Descriptor, metric, _io, _device, cancellation, query).ConfigureAwait(false);
                    return await charges.BuildGroupingAsync(dimension, cancellation).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
    }
}

public sealed partial class AnalysisObjectChargeIndex
{
    internal Task<AnalysisDatasetGrouping> BuildGroupingAsync(AnalysisGroupingDimension dimension, CancellationToken cancellation) =>
        _owned.UseAsync(source => AnalysisDatasetGrouping.BuildAsync(source, Descriptor, Metric, Totals, dimension, _io, _device, cancellation));
}

/// <summary>Disk-backed captured groups and bounded evidence pages. No live source or action capability.</summary>
public sealed partial class AnalysisDatasetGrouping : IAsyncDisposable
{
    private readonly AnalysisOwnedDatasetIo<State> _owned;
    public AnalysisDatasetDescriptor Descriptor { get; }
    public AnalysisMetric Metric { get; }
    public AnalysisGroupingDimension Dimension { get; }
    public AnalysisTotals Totals { get; }
    public AnalysisLogicalTotals LogicalTotals { get; }
    public ulong Count { get; }
    public int RuleVersion => AnalysisGroupingRules.RuleVersion;
    private AnalysisDatasetGrouping(State state, AnalysisDatasetDescriptor descriptor, DeviceIoScheduler io, AnalysisIoDevices device)
    {
        Descriptor = descriptor; Metric = state.Metric; Dimension = state.Dimension; Totals = state.Totals;
        LogicalTotals = state.LogicalTotals; Count = state.Summaries!.Count; _owned = new(state, io, device);
    }
    private async Task<T> ReadAsync<T>(Func<State, CancellationToken, T> read, CancellationToken cancellation)
    { using var admission = AnalysisExplorerAdmission.Acquire(); return await _owned.RunAsync(read, cancellation).ConfigureAwait(false); }
    public Task<AnalysisDatasetPage<AnalysisDatasetGroup>> ReadGroupsAsync(ulong offset = 0, int maximum = 128, CancellationToken cancellation = default) =>
        ReadAsync((state, token) =>
        {
            ValidatePage(offset, maximum, Count); var rows = ImmutableArray.CreateBuilder<AnalysisDatasetGroup>(); long bytes = 0;
            while (offset + (ulong)rows.Count < Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var record = state.Order!.Read(offset + (ulong)rows.Count + 1, token);
                var data = state.Summaries!.Read(record.Value, token); if (!Fits(data.Length * 3L + 1024, ref bytes)) break;
                rows.Add(DecodeSummary(data));
            }
            return new AnalysisDatasetPage<AnalysisDatasetGroup>(offset, Count, rows.ToImmutable());
        }, cancellation);
    public Task<AnalysisDatasetPage<AnalysisDatasetGroupMember>> ReadMembersAsync(string groupKey, ulong offset = 0, int maximum = 128,
        CancellationToken cancellation = default) => ReadAsync((state, token) =>
        {
            var group = state.Group(groupKey, token); var index = group.Kind == AnalysisGroupKind.Entries ? state.MemberIndex! : state.ReferenceIndex!;
            var range = index.PrefixRange(Hash(groupKey), token); ValidatePage(offset, maximum, range.Count);
            if (range.Count != group.Members) throw Invalid();
            var rows = ImmutableArray.CreateBuilder<AnalysisDatasetGroupMember>(); long bytes = 0;
            while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var entry = index.Read(range.First + offset + (ulong)rows.Count, token);
                var record = group.Kind == AnalysisGroupKind.Entries ? state.Members!.Read(entry.Value, token) : state.References!.Read(entry.Value, token);
                var item = DecodeEvent(record); var path = state.Graph.ReadPath(item.Path, token); var obj = state.Object(path.ObjectId, token);
                long cost = 1024L + path.Name.Length * 2L + path.RawName.Length + (obj?.Id.Length ?? 0) * 2L;
                if (!Fits(cost, ref bytes)) break;
                rows.Add(new(AnalysisPathMetadata.From(path), obj?.LogicalLength ?? AnalysisMeasurement.Missing(MeasurementState.Unsupported),
                    group.Kind != AnalysisGroupKind.Entries, path.Times is null ? obj?.ModifiedUtc : path.Times.ModifiedUtc,
                    path.Times is null ? obj?.ModifiedPrecisionTicks ?? 0 : path.Times.ModifiedPrecisionTicks));
            }
            return new AnalysisDatasetPage<AnalysisDatasetGroupMember>(offset, range.Count, rows.ToImmutable());
        }, cancellation);
    public Task<AnalysisDatasetPage<AnalysisObjectCharge>> ReadChargesAsync(string groupKey, ulong? ownerPath = null, ulong offset = 0,
        int maximum = 128, CancellationToken cancellation = default) => ReadAsync((state, token) =>
        {
            var group = state.Group(groupKey, token); byte[] prefix = ownerPath is { } path ? Join(Hash(groupKey), Number(path)) : Hash(groupKey);
            var range = state.ChargeIndex!.PrefixRange(prefix, token); ValidatePage(offset, maximum, range.Count);
            if (ownerPath is null && range.Count != group.Charges) throw Invalid();
            var rows = ImmutableArray.CreateBuilder<AnalysisObjectCharge>(); long bytes = 0;
            while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var data = state.Charges!.Read(state.ChargeIndex.Read(range.First + offset + (ulong)rows.Count, token).Value, token);
                if (!Fits(data.Length * 3L + 1024, ref bytes)) break;
                rows.Add(DecodeEvent(data).Charge ?? throw Invalid());
            }
            return new AnalysisDatasetPage<AnalysisObjectCharge>(offset, range.Count, rows.ToImmutable());
        }, cancellation);
    public ValueTask DisposeAsync() => _owned.DisposeAsync();
    private static void ValidatePage(ulong offset, int maximum, ulong total)
    { if (offset > total) throw new ArgumentOutOfRangeException(nameof(offset)); if (maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(maximum)); }
    private static bool Fits(long cost, ref long bytes)
    { if (cost > AnalysisLimits.BatchBytes) throw new IOException("The captured grouping row exceeds its page budget."); if (cost > AnalysisLimits.BatchBytes - bytes) return false; bytes += cost; return true; }
    private static byte[] Hash(string key)
    {
        if (key is null || key.Length != 72 || !key.StartsWith("group:1:", StringComparison.Ordinal) || key[8..].Any(c => c is not (>= '0' and <= '9' or >= 'a' and <= 'f'))) throw Invalid();
        return Convert.FromHexString(key[8..]);
    }
    private static byte[] Number(ulong value) { byte[] bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); return bytes; }
    private static byte[] Join(params byte[][] parts)
    { var result = new byte[parts.Sum(p => p.Length)]; int at = 0; foreach (var part in parts) { part.CopyTo(result, at); at += part.Length; } return result; }
    private static InvalidDataException Invalid() => new("Invalid captured grouping data.");
}
