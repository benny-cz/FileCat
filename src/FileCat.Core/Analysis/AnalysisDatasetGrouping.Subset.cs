using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    /// <summary>Retains an exact geometry suffix of the overview or a captured group's children.</summary>
    public Task<Subset> CreateSubsetAsync(string? groupKey, int skipped, CancellationToken cancellation = default) => Subset.CreateAsync(this, groupKey, skipped, cancellation);

    /// <summary>An immutable accounting scope. Synthetic keys, captured IDs and references grant no live authority.</summary>
    public sealed class Subset : IAsyncDisposable
    {
        private readonly AnalysisOwnedDatasetIo<SubsetState> _owned;
        private readonly DeviceIoScheduler _io;
        private readonly AnalysisIoDevices _device;
        public AnalysisDatasetDescriptor Descriptor { get; }
        public AnalysisMetric Metric { get; }
        public AnalysisGroupingDimension Dimension { get; }
        public AnalysisFilterSummary? Filter { get; }
        public string? ParentKey { get; }
        public int Skipped { get; }
        public string ScopeKey => AnalysisTree.OtherKey(ParentKey ?? AnalysisTree.RootKey, Skipped);
        public ulong Rows { get; }
        public ulong GeometryCount { get; }
        public ulong MemberPaths { get; }
        public ulong ChargeCount { get; }
        public AnalysisTotals Totals { get; }
        public AnalysisLogicalTotals LogicalTotals { get; }
        public AnalysisDatasetGroup? ScopeGroup { get; }
        public bool Unknown { get; }
        internal AnalysisReportBases? MeasurementBases { get; }
        private Subset(SubsetState state, AnalysisDatasetDescriptor descriptor, AnalysisFilterSummary? filter, DeviceIoScheduler io, AnalysisIoDevices device)
        {
            Descriptor = descriptor; Metric = state.Snapshot.Metric; Dimension = state.Snapshot.Dimension; Filter = filter; ParentKey = state.ParentKey; Skipped = state.Skipped;
            Rows = state.Rows; GeometryCount = state.Geometry.Count - (ulong)Skipped; MemberPaths = state.MemberCount; ChargeCount = state.ChargeCount;
            Totals = state.Amount.Export(state.Snapshot.Totals.CompatibleBasis, state.Logical.Files); LogicalTotals = state.Logical.ExportLogical(); Unknown = state.Unknown;
            MeasurementBases = state.Snapshot.MeasurementBases;
            ScopeGroup = state.Parent is null ? null : state.Parent with { Totals = Totals, Logical = LogicalTotals, Members = MemberPaths, Charges = ChargeCount,
                Shared = state.Shared ? state.Parent.Shared : new(0, 0, 0, ImmutableDictionary<MeasurementState, ulong>.Empty, Totals.CompatibleBasis), SharedCharges = state.Shared ? state.Parent.SharedCharges : 0,
                ReferenceLogical = state.Parent.Kind == AnalysisGroupKind.SharedAcrossGroups ? LogicalTotals : state.Parent.ReferenceLogical };
            _io = io; _device = device; _owned = new(state, io, device);
        }
        internal static async Task<Subset> CreateAsync(AnalysisDatasetGrouping owner, string? parent, int skipped, CancellationToken cancellation)
        {
            using var admission = AnalysisExplorerAdmission.Acquire();
            return await owner._owned.UseAsync(state => BuildAsync(state, owner.Descriptor, owner.Filter, owner._io, owner._device, parent, skipped, cancellation)).ConfigureAwait(false);
        }
        public async Task<Subset> CreateSiblingAsync(string? parent, int skipped, CancellationToken cancellation = default)
        {
            using var admission = AnalysisExplorerAdmission.Acquire();
            return await _owned.UseAsync(state => BuildAsync(state.Snapshot, Descriptor, Filter, _io, _device, parent, skipped, cancellation)).ConfigureAwait(false);
        }
        public Task<AnalysisDatasetGrouping> OpenSnapshotAsync(CancellationToken cancellation = default) => _owned.RunAsync((state, _) =>
        {
            var retained = state.Snapshot.Retain();
            try { return new AnalysisDatasetGrouping(retained, Descriptor, _io, _device, Filter); }
            catch { AnalysisIo.CloseOnWorker(retained); throw; }
        }, cancellation);
        private static async Task<Subset> BuildAsync(State snapshot, AnalysisDatasetDescriptor descriptor, AnalysisFilterSummary? filter,
            DeviceIoScheduler io, AnalysisIoDevices device, string? parent, int skipped, CancellationToken cancellation)
        {
            SubsetState? state = await AnalysisIo.Run(io, device, token => SubsetState.Create(snapshot, parent, skipped, token), cancellation).ConfigureAwait(false);
            try
            {
                await Build(state.SelectedEntries(cancellation), value => state.Selected = value, AnalysisLimits.GroupGeometryRows);
                await Build(state.PathEntries(cancellation), value => state.Paths = value, AnalysisLimits.GroupGeometryRows);
                await Build(state.OrderEntries(cancellation), value => state.Order = value, AnalysisLimits.GroupOrderRows);
                await Build(state.ChargeEntries(cancellation), value => state.Charges = value, AnalysisLimits.Observations);
                await Build(state.SharedEntries(cancellation), value => state.SharedObjects = value, AnalysisLimits.Observations);
                await Build(state.CandidateEntries(cancellation), value => state.Candidates = value, AnalysisLimits.Observations * 2);
                await Build(state.MemberEntries(cancellation), value => state.Members = value, AnalysisLimits.Observations * 2);
                await AnalysisIo.Run(io, device, token =>
                {
                    if (state.Members!.Count != state.MemberCount || state.Charges!.Count != state.ChargeCount) throw Invalid();
                    foreach (var field in Enum.GetValues<AnalysisTableSort>())
                        if (state.Order!.PrefixRange([(byte)field], token).Count != state.Rows) throw Invalid();
                    state.Candidates!.Dispose(); state.Candidates = null; state.Workspace!.Dispose(); state.Workspace = null; return true;
                }, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested(); var result = new Subset(state, descriptor, filter, io, device); state = null; return result;
            }
            finally { if (state is not null) await AnalysisIo.ReleaseScratchAsync(io, device, state).ConfigureAwait(false); }
            async Task Build(IEnumerable<AnalysisSortInput> input, Action<AnalysisSortedIndexReader> attach, int maximum)
            {
                using var entries = input.GetEnumerator();
                var index = await AnalysisExternalSort.BuildSparseAsync(state.Workspace!, io, device, entries, cancellation, maximumRecords: maximum).ConfigureAwait(false);
                try { await AnalysisIo.Run(io, device, _ => { attach(index.Acquire()); return true; }, cancellation).ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(io, device, index).ConfigureAwait(false); }
            }
        }
        public Task<AnalysisDatasetPage<AnalysisTreeItem>> ReadGeometryAsync(ulong offset = 0, int maximum = 128, CancellationToken cancellation = default) =>
            _owned.RunAsync((state, token) => state.ReadGeometry(offset, maximum, token), cancellation);
        public Task<AnalysisDatasetPage<AnalysisTreeItem>> ReadChildGeometryAsync(string groupKey, ulong offset = 0, int maximum = 128, CancellationToken cancellation = default) =>
            _owned.RunAsync((state, token) =>
            {
                if (!state.Overview) throw new InvalidOperationException("This Other scope has no child accounting groups.");
                _ = state.RequireGroup(groupKey, token); return state.Snapshot.ReadGeometry(groupKey, offset, maximum, token);
            }, cancellation);
        public Task<AnalysisDatasetGroup> ReadGroupAsync(string groupKey, CancellationToken cancellation = default) => _owned.RunAsync((state, token) =>
        {
            var group = state.RequireGroup(groupKey, token); return state.Overview ? group : ScopeGroup!;
        }, cancellation);
        public Task<AnalysisDatasetPage<AnalysisDatasetGroup>> ReadGroupsAsync(ulong offset = 0, int maximum = 128,
            AnalysisTableSort sort = AnalysisTableSort.ActiveMetric, bool descending = true, CancellationToken cancellation = default) => _owned.RunAsync((state, token) =>
        {
            if (!state.Overview) throw new InvalidOperationException("This Other scope contains group members, not group categories.");
            return state.ReadTable(sort, offset, maximum, descending, (source, ct) =>
            { byte[] data = state.Snapshot.Summaries!.Read(source, ct); return (DecodeSummary(data), data.Length * 3L + 1024); }, token);
        }, cancellation);
        public Task<AnalysisDatasetPage<AnalysisDatasetGroupRow>> ReadMemberRowsAsync(ulong offset = 0, int maximum = 128,
            AnalysisTableSort sort = AnalysisTableSort.ActiveMetric, bool descending = true, CancellationToken cancellation = default) => _owned.RunAsync((state, token) =>
        {
            if (state.Overview) throw new InvalidOperationException("This Other scope contains group categories. Choose a captured group before reading member rows.");
            return state.ReadTable(sort, offset, maximum, descending, (source, ct) =>
            { var row = state.Snapshot.ReadMemberRow(source, ct, out _, out _, out long bytes); return (row, bytes); }, token);
        }, cancellation);
        public Task<AnalysisDatasetPage<AnalysisObjectCharge>> ReadChargesAsync(string? groupKey = null, ulong? ownerPath = null, ulong offset = 0,
            int maximum = 128, CancellationToken cancellation = default) => _owned.RunAsync((state, token) =>
        {
            groupKey ??= ParentKey;
            if (groupKey is not null)
                _ = state.RequireGroup(groupKey, token);
            else if (ownerPath is not null) throw new InvalidOperationException("Choose a captured group before reading a particular owner's charges.");
            byte[] prefix = groupKey is null ? [] : ownerPath is { } owner ? Join(Hash(groupKey), Number(owner)) : Hash(groupKey);
            var range = prefix.Length == 0 ? new AnalysisIndexRange(1, state.Charges!.Count) : state.Charges!.PrefixRange(prefix, token);
            ValidatePage(offset, maximum, range.Count); var rows = ImmutableArray.CreateBuilder<AnalysisObjectCharge>(); long bytes = 0;
            while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
            {
                byte[] data = state.Snapshot.Charges!.Read(state.Charges!.Read(range.First + offset + (ulong)rows.Count, token).Value, token);
                if (!Fits(data.Length * 3L + 1024, ref bytes)) break; rows.Add(DecodeEvent(data).Charge ?? throw Invalid());
            }
            return new AnalysisDatasetPage<AnalysisObjectCharge>(offset, range.Count, rows.ToImmutable());
        }, cancellation);
        public Task<AnalysisSelection> CaptureMembersAsync(int maximumPaths = AnalysisLimits.ReviewPaths, CancellationToken cancellation = default) => CaptureAsync(false, maximumPaths, cancellation);
        public Task<AnalysisSelection> CaptureMatchingFilesAsync(int maximumPaths = AnalysisLimits.ReviewPaths, CancellationToken cancellation = default) => CaptureAsync(true, maximumPaths, cancellation);
        public Task<AnalysisSelection> CaptureGroupMembersAsync(string groupKey, int maximumPaths = AnalysisLimits.ReviewPaths, CancellationToken cancellation = default) => CaptureAsync(false, maximumPaths, cancellation, groupKey);
        public Task<AnalysisSelection> CaptureGroupMatchingFilesAsync(string groupKey, int maximumPaths = AnalysisLimits.ReviewPaths, CancellationToken cancellation = default) => CaptureAsync(true, maximumPaths, cancellation, groupKey);
        private async Task<AnalysisSelection> CaptureAsync(bool filesOnly, int maximum, CancellationToken cancellation, string? groupKey = null)
        {
            if (maximum is < 1 or > AnalysisLimits.ReviewPaths) throw new ArgumentOutOfRangeException(nameof(maximum));
            using var admission = AnalysisExplorerAdmission.Acquire();
            return await _owned.UseAsync(async state =>
            {
                var group = groupKey is null ? null : await AnalysisIo.Run(_io, _device, token => state.RequireGroup(groupKey, token), cancellation).ConfigureAwait(false);
                bool narrow = state.Overview && group is not null;
                if (!narrow && (filesOnly ? LogicalTotals.Files : MemberPaths) > (ulong)maximum) throw new InvalidOperationException($"Refine this captured Other scope to at most {maximum:N0} real paths. No paths were added.");
                var ids = ImmutableArray.CreateBuilder<ulong>(); ulong at = 1;
                while (at <= state.Members!.Count)
                    await AnalysisIo.Run(_io, _device, token =>
                    {
                        int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                        while (at <= state.Members.Count && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                        {
                            token.ThrowIfCancellationRequested(); var entry = state.Members.Read(at++, token); var path = state.Snapshot.Graph.ReadPathRow(entry.Value, token);
                            if ((!filesOnly || path.Kind == AnalysisKind.File && path.IncludedByPolicy) &&
                                (!narrow || state.Snapshot.HasMember(groupKey!, path.Id, group!.Kind != AnalysisGroupKind.Entries, token)))
                            {
                                if (ids.Count == maximum) throw new InvalidOperationException($"Refine this captured Other selection to at most {maximum:N0} real paths. No paths were added.");
                                ids.Add(path.Id);
                            }
                            bytes += AnalysisIndexedFilter.Cost(path); if (clock.ElapsedMilliseconds >= 20) break;
                        }
                        return true;
                    }, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested(); var identity = Descriptor.Identity;
                return new AnalysisSelection(identity.SessionId, identity.Epoch, identity.Revision, ids.ToImmutable())
                    { Kind = filesOnly ? AnalysisSelectionKind.VisibleMatches : AnalysisSelectionKind.RealGroupMembers, ScopeSummary = "Captured grouped Other scope", FilterSummary = Filter?.Request.Text ?? "" };
            }).ConfigureAwait(false);
        }
        public async Task<AnalysisSelectionVisibility> ClassifyMarksAsync(ImmutableHashSet<ulong> marks, CancellationToken cancellation = default)
        {
            using var admission = AnalysisExplorerAdmission.Acquire(); using var counter = new AnalysisMarkCounter(marks);
            return await _owned.UseAsync(async state =>
            {
                while (!counter.Complete)
                    await AnalysisIo.Run(_io, _device, token =>
                    {
                        int records = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                        while (records++ < AnalysisLimits.BatchRecords && counter.Next(out ulong id))
                        {
                            token.ThrowIfCancellationRequested(); var visible = state.Snapshot.SelectionVisibility(id, token);
                            counter.Count(visible is null ? AnalysisMarkPlace.Missing : !visible.Value ? AnalysisMarkPlace.HiddenByFilter :
                                state.Members!.FindRange(Number(id), token).Count != 0 ? AnalysisMarkPlace.InScope : AnalysisMarkPlace.Elsewhere);
                            if (clock.ElapsedMilliseconds >= 20) break;
                        }
                        return true;
                    }, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested(); return counter.Result(Descriptor.Identity);
            }).ConfigureAwait(false);
        }
        public async Task<AnalysisCapturedTableRange> CaptureTableRangeAsync(ulong first, ulong last, AnalysisTableSort sort = AnalysisTableSort.ActiveMetric,
            bool descending = true, CancellationToken cancellation = default)
        {
            if (first > last || last >= Rows || last - first >= AnalysisLimits.ReviewPaths) throw new ArgumentOutOfRangeException(nameof(first));
            using var admission = AnalysisExplorerAdmission.Acquire();
            return await _owned.UseAsync(async state =>
            {
                var ids = ImmutableArray.CreateBuilder<ulong>(); ulong position = first;
                if (!state.Overview)
                    while (position <= last)
                    {
                        var page = await AnalysisIo.Run(_io, _device, token => state.ReadTable(sort, position, (int)Math.Min(128UL, last - position + 1), descending,
                            (source, ct) => { var row = state.Snapshot.ReadMemberRow(source, ct, out _, out _, out long bytes); return (row, bytes); }, token), cancellation).ConfigureAwait(false);
                        foreach (var row in page.Rows) ids.Add(row.Member.Path.Id); position = page.NextOffset;
                    }
                else if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
                cancellation.ThrowIfCancellationRequested(); var identity = Descriptor.Identity;
                return new AnalysisCapturedTableRange(new(identity.SessionId, identity.Epoch, identity.Revision, ids.ToImmutable())
                    { ScopeSummary = state.Overview ? "Captured Other accounting group rows; members not expanded" : "Captured Other group member rows" }, first, last, state.Overview ? last - first + 1 : 0);
            }).ConfigureAwait(false);
        }
        public ValueTask DisposeAsync() => _owned.DisposeAsync();
    }
}
