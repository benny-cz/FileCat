using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetHierarchy
{
    /// <summary>Retains an exact suffix of geometry children, including accounting buckets. Table sorting cannot change membership.</summary>
    public Task<Subset> CreateSubsetAsync(ulong parent, int skipped, CancellationToken cancellation = default) =>
        Subset.CreateAsync(this, parent, skipped, cancellation);

    /// <summary>An immutable, independently retained display scope. Its IDs and synthetic rows grant no filesystem authority.</summary>
    public sealed class Subset : IAsyncDisposable
    {
        internal static async Task<Subset> CreateAsync(AnalysisDatasetHierarchy owner, ulong parent, int skipped, CancellationToken cancellation)
        {
            if (skipped < 1) throw new ArgumentOutOfRangeException(nameof(skipped));
            using var admission = AnalysisExplorerAdmission.Acquire();
            return await owner._owned.UseAsync(source => BuildAsync(source, owner.Descriptor, owner.Metric, owner.Filter,
                owner._io, owner._device, parent, skipped, cancellation)).ConfigureAwait(false);
        }
        internal async Task<Subset> CreateSiblingAsync(ulong parent, int skipped, CancellationToken cancellation)
        {
            if (skipped < 1) throw new ArgumentOutOfRangeException(nameof(skipped));
            using var admission = AnalysisExplorerAdmission.Acquire();
            return await _owned.UseAsync(state => BuildAsync(state.Snapshot, Descriptor, Metric, Filter, _io, _device, parent, skipped, cancellation)).ConfigureAwait(false);
        }
        private static async Task<Subset> BuildAsync(State source, AnalysisDatasetDescriptor descriptor, AnalysisMetric metric, AnalysisFilterSummary? filter,
            DeviceIoScheduler io, AnalysisIoDevices device, ulong parent, int skipped, CancellationToken cancellation)
        {
            if (parent == 0 && !source.Compatible)
                throw new InvalidOperationException("These locations report different allocation components. Choose one location to explore On disk.");
            SubsetState? state = await AnalysisIo.Run(io, device, token => SubsetState.Create(source, parent, skipped, token), cancellation).ConfigureAwait(false);
            try
            {
                await Build(state, state.SelectedEntries(cancellation), index => state.Selected = index, AnalysisLimits.GroupRows);
                await Build(state, state.TableEntries(cancellation), index => state.Order = index, AnalysisLimits.GroupOrderRows);
                if (state.Shared)
                    await Build(state, state.SharedEntries(cancellation), index => state.SharedObjects = index, AnalysisLimits.Observations);
                await AnalysisIo.Run(io, device, _ => { state.BeginMatches(); return true; }, cancellation).ConfigureAwait(false);
                while (!state.MatchesFinished)
                    await AnalysisIo.Run(io, device, token =>
                    {
                        int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                        while (!state.MatchesFinished && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                        { token.ThrowIfCancellationRequested(); bytes += state.MatchStep(token); if (clock.ElapsedMilliseconds >= 20) break; }
                        return true;
                    }, cancellation).ConfigureAwait(false);
                await AnalysisIo.Run(io, device, token => { state.SealMatches(token); return true; }, cancellation).ConfigureAwait(false);
                await Build(state, state.MatchingEntries(cancellation), index => state.Matches = index, AnalysisLimits.Observations);
                await AnalysisIo.Run(io, device, _ => { state.Finish(); return true; }, cancellation).ConfigureAwait(false);
                var result = new Subset(state, descriptor, metric, filter, io, device); state = null; return result;
            }
            finally { if (state is not null) await AnalysisIo.ReleaseScratchAsync(io, device, state).ConfigureAwait(false); }

            async Task Build(SubsetState target, IEnumerable<AnalysisSortInput> input, Action<AnalysisSortedIndexReader> attach, int maximum)
            {
                using var entries = input.GetEnumerator();
                var index = await AnalysisExternalSort.BuildSparseAsync(target.Workspace!, io, device, entries, cancellation, maximumRecords: maximum).ConfigureAwait(false);
                try { await AnalysisIo.Run(io, device, _ => { attach(index.Acquire()); return true; }, cancellation).ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(io, device, index).ConfigureAwait(false); }
            }
        }

        private readonly AnalysisOwnedDatasetIo<SubsetState> _owned;
        private readonly DeviceIoScheduler _io;
        private readonly AnalysisIoDevices _device;
        public AnalysisDatasetDescriptor Descriptor { get; }
        public AnalysisMetric Metric { get; }
        public AnalysisFilterSummary? Filter { get; }
        public ulong ParentId { get; }
        public int Skipped { get; }
        public string ScopeKey => AnalysisTree.OtherKey(ParentId == 0 ? AnalysisTree.RootKey : AnalysisTree.Key(ParentId), Skipped);
        public AnalysisHierarchyRow? Parent { get; }
        public AnalysisTotals Totals { get; }
        public AnalysisLogicalTotals LogicalTotals { get; }
        public bool HasUnmeasuredEvidence { get; }
        public bool HasUnmeasuredLogicalEvidence { get; }
        public ulong GeometryCount { get; }
        public ulong Rows { get; }
        internal AnalysisReportBases? MeasurementBases { get; }
        private Subset(SubsetState state, AnalysisDatasetDescriptor descriptor, AnalysisMetric metric, AnalysisFilterSummary? filter,
            DeviceIoScheduler io, AnalysisIoDevices device)
        {
            Descriptor = descriptor; Metric = metric; Filter = filter; ParentId = state.Parent; Skipped = state.Skipped; Parent = state.ParentRow;
            GeometryCount = state.Geometry.Count - (ulong)Skipped; Rows = state.ChildCount;
            LogicalTotals = state.Logical.ExportLogical(); Totals = state.Amount.Export(state.ParentRow?.Totals.CompatibleBasis ?? state.Snapshot.Compatible) with { MatchingPaths = LogicalTotals.Files };
            HasUnmeasuredEvidence = state.Unknown; HasUnmeasuredLogicalEvidence = state.LogicalUnknown || LogicalTotals.UnknownFiles != 0;
            MeasurementBases = state.Snapshot.MeasurementBases;
            _owned = new(state, io, device); _io = io; _device = device;
        }
        public Task<AnalysisDatasetPage<AnalysisHierarchyRow>> ReadChildrenAsync(AnalysisHierarchySort sort = AnalysisHierarchySort.Metric,
            ulong offset = 0, int maximum = 128, CancellationToken cancellation = default, bool descending = true) =>
            _owned.RunAsync((state, token) => state.ReadChildren(sort, offset, maximum, descending, token), cancellation);
        public Task<AnalysisDatasetPage<AnalysisTreeItem>> ReadGeometryAsync(ulong offset = 0, int maximum = 128, CancellationToken cancellation = default) =>
            _owned.RunAsync((state, token) =>
            {
                if (offset > GeometryCount) throw new ArgumentOutOfRangeException(nameof(offset));
                var page = GeometryPage(state.Snapshot, ParentId, checked((ulong)Skipped + offset), maximum, token);
                return new AnalysisDatasetPage<AnalysisTreeItem>(offset, GeometryCount, page.Rows.Select(item => item with { ParentKey = ScopeKey }).ToImmutableArray());
            }, cancellation);
        public Task<ImmutableArray<AnalysisHierarchyBucket>> ReadBucketsAsync(CancellationToken cancellation = default) =>
            _owned.RunAsync((state, token) => state.Buckets.Select(row => state.Snapshot.ReadBucket(row, token)).ToImmutableArray(), cancellation);
        internal Task<AnalysisHierarchyRow> ReadSnapshotRowAsync(ulong path, CancellationToken cancellation) =>
            _owned.RunAsync((state, token) => ReadPath(state.Snapshot, path, token), cancellation);
        internal Task<AnalysisDatasetPage<AnalysisTreeItem>> ReadSnapshotGeometryAsync(ulong parent, int maximum, CancellationToken cancellation) =>
            _owned.RunAsync((state, token) => GeometryPage(state.Snapshot, parent, 0, maximum, token), cancellation);
        internal Task<AnalysisDatasetHierarchy> OpenHierarchyAsync(CancellationToken cancellation) =>
            _owned.RunAsync((state, _) => RetainedHierarchy(state.Snapshot, Descriptor, _io, _device), cancellation);
        public Task<AnalysisTablePosition> LocateChildAsync(ulong path, AnalysisHierarchySort sort = AnalysisHierarchySort.Metric,
            bool descending = true, CancellationToken cancellation = default) => _owned.RunAsync((state, token) =>
            {
                if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
                var frame = state.Snapshot.SelectionFrame(path, token) ?? throw new ArgumentOutOfRangeException(nameof(path));
                if (frame.Parent != ParentId || !state.HasChild(path, token)) throw new InvalidOperationException("This captured entry is outside the Other scope.");
                var node = state.Snapshot.Graph.ReadPath(path, token); byte[] prefix = [(byte)sort];
                var range = state.Order!.PrefixRange(prefix, token); var found = state.Order.FindRange(TableKey(sort, state.Snapshot.TableKey(frame, node, sort)), token);
                if (found.Count != 1 || found.First < range.First || found.First - range.First >= range.Count) throw Invalid();
                var known = state.Known(range, sort, token); ulong relative = found.First - range.First;
                ulong position = !descending && relative < known.Count ? known.Count - 1 - relative :
                    !descending && sort == AnalysisHierarchySort.MetadataState ? known.Count + range.Count - 1 - relative : relative;
                return new AnalysisTablePosition(ParentId, position, range.Count);
            }, cancellation);
        public async Task<AnalysisCapturedTableRange> CaptureTableRangeAsync(ulong first, ulong last, AnalysisHierarchySort sort = AnalysisHierarchySort.Metric,
            bool descending = true, CancellationToken cancellation = default)
        {
            if (first > last || last - first >= AnalysisLimits.ReviewPaths || last >= Rows) throw new ArgumentOutOfRangeException(nameof(first));
            using var admission = AnalysisExplorerAdmission.Acquire();
            return await _owned.UseAsync(async state =>
            {
                var ids = ImmutableArray.CreateBuilder<ulong>(); ulong position = first;
                while (position <= last)
                {
                    var page = await AnalysisIo.Run(_io, _device, token => state.ReadChildren(sort, position,
                        (int)Math.Min(128UL, last - position + 1), descending, token), cancellation).ConfigureAwait(false);
                    foreach (var row in page.Rows) ids.Add(row.Path.Id); position = page.NextOffset;
                }
                cancellation.ThrowIfCancellationRequested(); var identity = Descriptor.Identity;
                return new AnalysisCapturedTableRange(new(identity.SessionId, identity.Epoch, identity.Revision, ids.ToImmutable())
                    { ScopeSummary = "Captured Other table rows" }, first, last, 0);
            }).ConfigureAwait(false);
        }
        public async Task<AnalysisSelection> CaptureMatchingFilesAsync(int maximumPaths = AnalysisLimits.ReviewPaths, CancellationToken cancellation = default)
        {
            if (maximumPaths is < 1 or > AnalysisLimits.ReviewPaths) throw new ArgumentOutOfRangeException(nameof(maximumPaths));
            using var admission = AnalysisExplorerAdmission.Acquire();
            return await _owned.UseAsync(async state =>
            {
                    cancellation.ThrowIfCancellationRequested(); ulong count = state.Matches!.Count;
                    if (count > (ulong)maximumPaths) throw new InvalidOperationException($"Refine this captured Other scope to at most {maximumPaths:N0} matching files. No matches were added.");
                    var ids = ImmutableArray.CreateBuilder<ulong>((int)count); ulong at = 1;
                    while (at <= count)
                        await AnalysisIo.Run(_io, _device, token =>
                        {
                            int records = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                            while (at <= count && records++ < AnalysisLimits.BatchRecords)
                            { token.ThrowIfCancellationRequested(); ids.Add(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(state.Matches!.Read(at++, token).Key)); if (clock.ElapsedMilliseconds >= 20) break; }
                            return true;
                        }, cancellation).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested(); var identity = Descriptor.Identity;
                    return new AnalysisSelection(identity.SessionId, identity.Epoch, identity.Revision, ids.ToImmutable())
                    { Kind = AnalysisSelectionKind.VisibleMatches, ScopeSummary = "Matching files in captured Other scope", FilterSummary = Filter?.Request.Text ?? "" };
            }).ConfigureAwait(false);
        }
        public async Task<AnalysisSelectionVisibility> ClassifyMarksAsync(ImmutableHashSet<ulong> marks, CancellationToken cancellation = default)
        {
            using var admission = AnalysisExplorerAdmission.Acquire(); using var counter = new AnalysisMarkCounter(marks);
            return await _owned.UseAsync(async state =>
            {
                Membership? membership = null;
                while (!counter.Complete)
                    await AnalysisIo.Run(_io, _device, token =>
                    {
                        int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                        while (!counter.Complete && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                        {
                            token.ThrowIfCancellationRequested();
                            if (membership is not null)
                            {
                                bytes += membership.Step(token);
                                if (membership.Complete) { counter.Count(membership.Included ? AnalysisMarkPlace.InScope : AnalysisMarkPlace.Elsewhere); membership = null; }
                            }
                            else if (counter.Next(out ulong id))
                            {
                                var frame = state.Snapshot.SelectionFrame(id, token);
                                if (frame is null) counter.Count(AnalysisMarkPlace.Missing);
                                else if (!frame.Visible) counter.Count(AnalysisMarkPlace.HiddenByFilter);
                                else membership = new(state, id);
                                bytes += 512;
                            }
                            if (clock.ElapsedMilliseconds >= 20) break;
                        }
                        return true;
                    }, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested(); return counter.Result(Descriptor.Identity);
            }).ConfigureAwait(false);
        }
        public ValueTask DisposeAsync() => _owned.DisposeAsync();
    }

    private static byte[] TableKey(AnalysisHierarchySort sort, byte[] key) => [(byte)sort, .. key];
    private sealed class SubsetState : IAnalysisScratchOwned
    {
        internal readonly State Snapshot;
        internal readonly ulong Parent;
        internal readonly int Skipped;
        internal readonly AnalysisIndexRange Geometry;
        internal readonly AnalysisHierarchyRow? ParentRow;
        internal readonly Amount Amount = new(), Logical = new();
        internal readonly List<ulong> Buckets = [];
        internal bool Shared, Unknown, LogicalUnknown;
        internal ulong ChildCount;
        internal AnalysisScratchWorkspace? Workspace;
        internal AnalysisSortedIndexReader? Selected, Order, SharedObjects, Matches;
        private AnalysisScratchVector? _matchingWriter;
        private AnalysisScratchVectorCheckpoint? _matchingRows;
        private Membership? _matching;
        private ulong _matchAt, _matchSource;
        internal bool MatchesFinished => _matching is null && _matchAt == Snapshot.Indexes[4].Count;
        private SubsetState(State snapshot, ulong parent, int skipped, AnalysisIndexRange geometry, AnalysisHierarchyRow? row)
        { Snapshot = snapshot; Parent = parent; Skipped = skipped; Geometry = geometry; ParentRow = row; }
        internal static SubsetState Create(State source, ulong parent, int skipped, CancellationToken token)
        {
            source.Graph.RequireParent(parent, token); var range = source.Indexes[8].PrefixRange(Key(parent), token);
            if ((ulong)skipped > range.Count) throw new ArgumentOutOfRangeException(nameof(skipped));
            var frame = parent == 0 ? null : source.SelectionFrame(parent, token);
            if (parent != 0 && (frame?.Visible != true || source.Graph.ReadPath(parent, token).Kind != AnalysisKind.Directory))
                throw new InvalidOperationException("Only a visible captured folder can define an Other scope.");
            State? snapshot = source.Retain(); SubsetState? result = null;
            try
            {
                result = new(snapshot, parent, skipped, range, frame is null ? null : source.Read(source.Indexes[0].Read(source.Indexes[0].FindRange(Key(parent), token).First, token).Value, token)); snapshot = null;
                result.Workspace = result.Snapshot.Graph.BorrowWorkspace(); return result;
            }
            catch { if (result is not null) result.Dispose(); else snapshot?.Dispose(); throw; }
        }
        internal IEnumerable<AnalysisSortInput> SelectedEntries(CancellationToken token)
        {
            for (ulong at = (ulong)Skipped; at < Geometry.Count; at++)
            {
                token.ThrowIfCancellationRequested(); ulong source = Snapshot.Indexes[8].Read(Geometry.First + at, token).Value;
                if ((source & AnalysisGeometryOrder.BucketToken) != 0)
                {
                    ulong row = source & ~AnalysisGeometryOrder.BucketToken; var bucket = Snapshot.ReadBucket(row, token); Buckets.Add(row);
                    Unknown |= AnalysisGeometryOrder.Bucket(bucket, Snapshot.Metric).Unknown;
                    Shared |= bucket.Kind == AnalysisHierarchyBucketKind.SharedFiles; Add(bucket.Totals);
                }
                else
                {
                    var observed = Snapshot.Read(source, token); Add(observed.Totals);
                    var item = AnalysisIndexedChart.Row(observed, Snapshot.Metric); Unknown |= item.Unknown; LogicalUnknown |= item.LogicalUnknown;
                    yield return new(new(Key(source), source), checked((int)(1536L + observed.Path.Name.Length * 2L + observed.Path.RawName.Length +
                        observed.Path.RootId.Length * 2L + (observed.Path.ObjectId?.Length ?? 0) * 2L + (observed.Path.ObservedIdentity?.Length ?? 0) * 2L))); continue;
                }
                yield return new(new(Key(source), source), 2048);
            }
            void Add(AnalysisTotals totals)
            {
                Amount.Known = checked(Amount.Known + totals.Known); Amount.Unverified = checked(Amount.Unverified + totals.IdentityUnverified);
                _ = checked(Amount.Known + Amount.Unverified);
                foreach (var pair in totals.Missing) Amount.Missing[(int)pair.Key] = checked(Amount.Missing[(int)pair.Key] + pair.Value);
            }
        }
        internal IEnumerable<AnalysisSortInput> TableEntries(CancellationToken token)
        {
            for (ulong at = 1; at <= Selected!.Count; at++)
            {
                token.ThrowIfCancellationRequested(); ulong source = Selected.Read(at, token).Value;
                if ((source & AnalysisGeometryOrder.BucketToken) != 0) { yield return new(null, 256); continue; }
                var row = Snapshot.Read(source, token); var frame = Snapshot.SelectionFrame(row.Path.Id, token) ?? throw Invalid();
                var path = Snapshot.Graph.ReadPath(row.Path.Id, token);
                foreach (var sort in Enum.GetValues<AnalysisHierarchySort>())
                    yield return new(new(TableKey(sort, Snapshot.TableKey(frame, path, sort)), source),
                        checked((int)(AnalysisIndexedFilter.Cost(path) - (sort == AnalysisHierarchySort.Name ? path.Name.Length * 2L : 0) + 1024)));
            }
        }
        internal IEnumerable<AnalysisSortInput> SharedEntries(CancellationToken token)
        {
            var range = Snapshot.Placements?.PrefixRange(Key(Parent), token) ?? default;
            for (ulong at = 0; at < range.Count; at++)
            {
                token.ThrowIfCancellationRequested(); var entry = Snapshot.Placements!.Read(range.First + at, token);
                if (entry.Key.Length is not (50 or 114)) throw Invalid();
                if ((entry.Key[49] & 1) == 0) { yield return new(null, 256); continue; }
                ulong path = System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(entry.Key.AsSpan(16)); var node = Snapshot.Graph.ReadPath(path, token);
                ulong objectRow = ObjectRow(node.ObjectId, token, out long objectCost); if (objectRow == 0) throw Invalid();
                yield return new(new(Key(objectRow), entry.Value), checked((int)(AnalysisIndexedFilter.Cost(node) + objectCost + 512)));
            }
        }
        internal void BeginMatches() => _matchingWriter = new(Workspace!.CreateArena());
        internal long MatchStep(CancellationToken token)
        {
            if (_matching is null)
            {
                _matchSource = Snapshot.Indexes[4].Read(++_matchAt, token).Value;
                _matching = new(this, Snapshot.SelectionId(_matchSource, token)); return 512;
            }
            long cost = _matching.Step(token); if (!_matching.Complete) return cost;
            if (_matching.Included)
            {
                var row = Snapshot.Read(_matchSource, token); Logical.Known = checked(Logical.Known + row.Logical.KnownBytes); Logical.Files++;
                foreach (var pair in row.Logical.Missing) Logical.Missing[(int)pair.Key] = checked(Logical.Missing[(int)pair.Key] + pair.Value);
                byte[] record = [.. Key(row.Path.Id), .. Key(_matchSource)]; _matchingWriter!.Append(record, token);
                cost += 1536L + row.Path.Name.Length * 2L + row.Path.RawName.Length;
            }
            _matching = null; return cost;
        }
        internal void SealMatches(CancellationToken token)
        { _matchingWriter!.Flush(token); _matchingRows = _matchingWriter.CaptureCommitted(); _matchingWriter.Dispose(); _matchingWriter = null; }
        internal IEnumerable<AnalysisSortInput> MatchingEntries(CancellationToken token)
        {
            for (ulong at = 1; at <= _matchingRows!.Count; at++)
            {
                token.ThrowIfCancellationRequested(); byte[] record = _matchingRows.Read(at, token); if (record.Length != 16) throw Invalid();
                yield return new(new(record.AsSpan(0, 8).ToArray(), System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(record.AsSpan(8))), 256);
            }
        }
        internal void Finish()
        {
            ChildCount = Order!.PrefixRange([(byte)AnalysisHierarchySort.Metric]).Count;
            _matchingRows?.Dispose(); _matchingRows = null; Workspace!.Dispose(); Workspace = null;
        }
        internal bool HasChild(ulong id, CancellationToken token)
        {
            var range = Snapshot.Indexes[0].FindRange(Key(id), token); if (range.Count != 1) return false;
            ulong row = Snapshot.Indexes[0].Read(range.First, token).Value; return Selected!.FindRange(Key(row), token).Count == 1;
        }
        internal ulong ObjectRow(string? id, CancellationToken token, out long bytes)
        {
            bytes = 0; if (id is null) return 0;
            var range = Snapshot.Graph.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: token);
            if (range.Count != 1) throw Invalid(); ulong row = Snapshot.Graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value;
            var observed = Snapshot.Graph.ReadObject(row, token);
            bytes = 1024L + observed.Id.Length * 2L + (observed.VerifiedIdentity?.Length ?? 0) * 2L + (observed.VersionEvidence?.Length ?? 0) * 2L;
            // The accounting cohort can contain multiple inert object IDs for one verified identity.
            // Use the first row in that exact cohort, as the charge index does, rather than a representative ID.
            var cohort = Snapshot.Graph.Find(AnalysisIndexedGraphRevision.ObjectIdentities, AnalysisGraphKeys.ObjectIdentity(observed), cancellation: token);
            if (cohort.Count == 0) throw Invalid(); return Snapshot.Graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIdentities, cohort.First, token).Value;
        }
        internal AnalysisIndexRange Known(AnalysisIndexRange range, AnalysisHierarchySort sort, CancellationToken token) => sort == AnalysisHierarchySort.Name ? range :
            Order!.PrefixRange([(byte)sort, .. Key(Parent), 0], token);
        internal AnalysisDatasetPage<AnalysisHierarchyRow> ReadChildren(AnalysisHierarchySort sort, ulong offset, int maximum, bool descending, CancellationToken token)
        {
            if (!Enum.IsDefined(sort) || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(sort));
            var range = Order!.PrefixRange([(byte)sort], token); var known = Known(range, sort, token);
            if (offset > range.Count) throw new ArgumentOutOfRangeException(nameof(offset)); var rows = ImmutableArray.CreateBuilder<AnalysisHierarchyRow>(); long bytes = 0;
            while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); ulong position = offset + (ulong)rows.Count, at = OrderedIndex(range, known, position, sort, descending);
                var row = Snapshot.Read(Order.Read(at, token).Value, token); long cost = 1536L + row.Path.Name.Length * 2L + row.Path.RawName.Length +
                    row.Path.RootId.Length * 2L + (row.Path.ObjectId?.Length ?? 0) * 2L + (row.Path.ObservedIdentity?.Length ?? 0) * 2L;
                if (cost > AnalysisLimits.BatchBytes) throw Invalid(); if (cost > AnalysisLimits.BatchBytes - bytes) break; rows.Add(row); bytes += cost;
            }
            return new(offset, range.Count, rows.ToImmutable());
        }
        public void Dispose()
        {
            Exception? error = null;
            foreach (IDisposable? owned in new IDisposable?[] { Matches, SharedObjects, Order, Selected, _matchingWriter, _matchingRows, Workspace, Snapshot })
                try { owned?.Dispose(); } catch (Exception failure) { error ??= failure; }
            if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
    private sealed class Membership(SubsetState state, ulong id)
    {
        private ulong _walk = id;
        private int _depth;
        private bool _first = true;
        internal bool Complete { get; private set; }
        internal bool Included { get; private set; }
        internal long Step(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); if (Complete) return 1;
            var node = state.Snapshot.Graph.ReadPath(_walk, token); long cost = AnalysisIndexedFilter.Cost(node) + 1024;
            if (_first)
            {
                _first = false;
                if (state.SharedObjects is not null && node.Kind == AnalysisKind.File)
                {
                    ulong row = state.ObjectRow(node.ObjectId, token, out long objectCost); cost += objectCost;
                    if (row != 0 && state.SharedObjects.FindRange(Key(row), token).Count != 0) { Included = Complete = true; return cost; }
                }
            }
            if (++_depth > AnalysisLimits.Depth + 1) throw Invalid();
            if (node.Id == state.Parent) Complete = true;
            else if (node.ParentId == state.Parent) { Included = state.HasChild(node.Id, token); Complete = true; }
            else if (node.ParentId == 0) Complete = true;
            else _walk = node.ParentId;
            return cost;
        }
    }
}
