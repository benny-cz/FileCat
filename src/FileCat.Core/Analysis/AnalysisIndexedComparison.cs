using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetLease
{
    internal Task<AnalysisIndexedComparison> CompareIndexedAsync(AnalysisDatasetLease previous, AnalysisMetric metric,
        IReadOnlyDictionary<string, string>? structuralRoots = null, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(previous); if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        cancellation.ThrowIfCancellationRequested();
        var devices = _device; foreach (string key in previous._device.Keys) devices = devices.Include(key);
        var mapping = AnalysisComparison.MapRoots(previous.Descriptor.Roots, previous.Descriptor.Identity, Descriptor.Roots, Descriptor.Identity, structuralRoots);
        return _owned.UseAsync(current => previous._owned.UseAsync(async old =>
        {
            AnalysisComparisonPaths? paths = null; AnalysisComparisonEntities? a = null, b = null; bool transferred = false;
            try
            {
                paths = await AnalysisComparisonPaths.BuildAsync(old, current, previous.Descriptor, Descriptor, mapping, _io, devices, cancellation).ConfigureAwait(false);
                a = await AnalysisComparisonEntities.CreateAsync(old, previous.Descriptor, metric, _io, devices, cancellation).ConfigureAwait(false);
                b = await AnalysisComparisonEntities.CreateAsync(current, Descriptor, metric, _io, devices, cancellation).ConfigureAwait(false);
                var result = await paths.UseAsync(p => a.UseAsync(x => b.UseAsync(y =>
                    AnalysisIndexedComparison.BuildAsync(paths, p, a, x, b, y, structuralRoots is not null, _io, devices, cancellation)))).ConfigureAwait(false);
                transferred = true; return result;
            }
            finally { if (!transferred) await AnalysisIndexedComparison.CloseAsync(b, a, paths).ConfigureAwait(false); }
        }));
    }
}

internal sealed record AnalysisIndexedComparisonRow(ulong Id, AnalysisComparisonAccountingEntity? Previous,
    AnalysisComparisonAccountingEntity? Current, AnalysisChange Change, AnalysisComparisonEvidence Evidence, AnalysisDelta? Delta,
    string Explanation)
{
    internal double? RelativePercent => Delta is { } d && Previous?.Value is { IsKnown: true } p ? d.RelativePercent(p.Value!.Value) : null;
}

/// <summary>Inert paged comparison. All actual work retains both captured inputs and all temporary owners.</summary>
internal sealed partial class AnalysisIndexedComparison : IAsyncDisposable
{
    private readonly AnalysisOwnedDatasetIo<State> _owned;
    private readonly AnalysisComparisonEntities _previous, _current;
    private readonly AnalysisComparisonPaths _paths;
    private readonly object _gate = new(); private Task? _closed;
    internal AnalysisComparisonCompatibility Compatibility { get; }
    internal AnalysisTotals PreviousTotals => _previous.Totals;
    internal AnalysisTotals CurrentTotals => _current.Totals;
    internal AnalysisMetric Metric => _current.Metric;
    internal AnalysisTaint Taint { get; }
    internal AnalysisDelta? NetChange { get; }
    internal ulong Count { get; }
    internal ImmutableDictionary<string, string> RootMapping => _paths.RootMapping;
    private AnalysisIndexedComparison(State state, AnalysisComparisonPaths paths, AnalysisComparisonEntities previous, AnalysisComparisonEntities current,
        AnalysisComparisonCompatibility compatibility, DeviceIoScheduler io, AnalysisIoDevices devices)
    {
        _owned = new(state, io, devices); _paths = paths; _previous = previous; _current = current;
        Count = state.Rows!.Count; Compatibility = compatibility; Taint = AnalysisPrivacy.Join(previous.Descriptor.Taint, current.Descriptor.Taint);
        var a = previous.Totals; var b = current.Totals;
        NetChange = compatibility.ComparableTotals && a.CompatibleBasis && b.CompatibleBasis && a.Missing.Count == 0 && b.Missing.Count == 0 &&
            a.IdentityUnverified == 0 && b.IdentityUnverified == 0 ? AnalysisDelta.Between(a.Known, b.Known) : null;
    }
    internal Task<AnalysisDatasetPage<AnalysisIndexedComparisonRow>> ReadAsync(ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) => state.Page(offset, maximum, token), cancellation);
    internal Task<AnalysisDatasetPage<AnalysisPathMetadata>> ReadMembersAsync(bool previous, ulong entity, ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.UseAsync(_ => (previous ? _previous : _current).ReadMembersAsync(entity, offset, maximum, cancellation));
    internal Task<AnalysisComparisonAccountingEntity> ReadSideAsync(bool previous, ulong entity, CancellationToken cancellation = default) =>
        _owned.UseAsync(async _ =>
        {
            if (entity == 0) throw new ArgumentOutOfRangeException(nameof(entity));
            var page = await (previous ? _previous : _current).ReadAsync(entity - 1, 1, cancellation).ConfigureAwait(false);
            return page.Rows.Length == 1 ? page.Rows[0] : throw new ArgumentOutOfRangeException(nameof(entity));
        });
    public ValueTask DisposeAsync() { lock (_gate) return new(_closed ??= DisposeCoreAsync()); }
    private async Task DisposeCoreAsync()
    {
        Exception? failure = null;
        try { await _owned.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { failure = error; }
        try { await CloseAsync(_current, _previous, _paths).ConfigureAwait(false); }
        catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    internal static async Task CloseAsync(params IAsyncDisposable?[] owners)
    {
        Exception? failure = null;
        foreach (var owner in owners)
            if (owner is not null) try { await owner.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    internal static async Task<AnalysisIndexedComparison> BuildAsync(AnalysisComparisonPaths paths, AnalysisComparisonPaths.State pathState,
        AnalysisComparisonEntities previous, AnalysisComparisonEntities.State a, AnalysisComparisonEntities current, AnalysisComparisonEntities.State b,
        bool structural, DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        var state = new State(pathState, a, b, previous.Descriptor, current.Descriptor, paths.RootMapping, structural);
        bool transferred = false;
        try
        {
            await AnalysisIo.Run(io, devices, _ => { state.Initialize(); return true; }, cancellation).ConfigureAwait(false);
            var oldInfo = new SourceInfo(previous.Descriptor, a.Graph, current.Descriptor);
            var newInfo = new SourceInfo(current.Descriptor, b.Graph, previous.Descriptor);
            using (var entries = oldInfo.Entries(cancellation, current.Metric == AnalysisMetric.OnDisk).GetEnumerator())
                state.OldBases = oldInfo.Bases = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations * 2, io, devices, cancellation, oldInfo.Duplicate).ConfigureAwait(false);
            using (var entries = newInfo.Entries(cancellation, current.Metric == AnalysisMetric.OnDisk).GetEnumerator())
                state.NewBases = newInfo.Bases = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations * 2, io, devices, cancellation, newInfo.Duplicate).ConfigureAwait(false);
            var compatibility = await CompatibilityAsync(oldInfo, newInfo, previous.Descriptor, current.Descriptor, current.Metric,
                previous.Totals, current.Totals, paths.RootMapping, structural, io, devices, cancellation).ConfigureAwait(false);
            state.Compatibility = compatibility;
            await AnalysisIo.Run(io, devices, _ => { state.CloseBases(); return true; }, cancellation).ConfigureAwait(false);
            using (var entries = state.PathEntries(cancellation).GetEnumerator())
                state.PathEdges = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations, io, devices, cancellation, static (_, _, _) => true).ConfigureAwait(false);
            if (compatibility.VerifiedIdentityContext)
            {
                using (var entries = state.IdentityEntries(a, cancellation).GetEnumerator())
                    state.OldIdentities = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations, io, devices, cancellation).ConfigureAwait(false);
                using (var entries = state.IdentityEntries(b, cancellation).GetEnumerator())
                    state.NewIdentities = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations, io, devices, cancellation).ConfigureAwait(false);
                await BuildIdentityLinksAsync(state, current.Count, io, devices, cancellation).ConfigureAwait(false);
            }
            await AnalysisIo.Run(io, devices, token => { state.SealFallback(token); return true; }, cancellation).ConfigureAwait(false);
            using (var entries = state.EdgeEntries(false, cancellation).GetEnumerator())
                state.Edges = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations * 2, io, devices, cancellation, static (_, _, _) => true).ConfigureAwait(false);
            using (var entries = state.EdgeEntries(true, cancellation).GetEnumerator())
                state.Incoming = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations * 2, io, devices, cancellation, static (_, _, _) => true).ConfigureAwait(false);
            await BuildRowsAsync(state, previous.Count, current.Count, current.Metric, io, devices, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(io, devices, token => { state.SealRows(token); return true; }, cancellation).ConfigureAwait(false);
            using (var entries = state.RankEntries(cancellation).GetEnumerator())
                state.Order = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations * 2, io, devices, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(io, devices, _ => { state.FinishBuild(); return true; }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var result = new AnalysisIndexedComparison(state, paths, previous, current, compatibility, io, devices); transferred = true; return result;
        }
        finally { if (!transferred) await AnalysisIo.ReleaseScratchAsync(io, devices, state).ConfigureAwait(false); }
    }
}
