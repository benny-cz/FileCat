using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    private static int s_chart;

    internal async Task<Chart> BuildChartAsync(bool previous, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested(); var reader = Retain();
        AnalysisDatasetHierarchy? hierarchy = null; ChartState? state = null; bool admitted = false, transferred = false;
        try
        {
            using var admission = AnalysisExplorerAdmission.Acquire();
            if (Interlocked.CompareExchange(ref s_chart, 1, 0) != 0)
                throw new InvalidOperationException("The indexed comparison chart place is in use. Close the other chart first.");
            admitted = true;
            await reader.UseAsync(async source =>
            {
                var side = previous ? source.Previous : source.Current; var descriptor = (previous ? _previous : _current).Descriptor;
                if (Metric is AnalysisMetric.FileSize or AnalysisMetric.Files)
                    hierarchy = await AnalysisDatasetHierarchy.BuildAsync(side.Graph, descriptor, Metric, null, true, _io, _devices, cancellation).ConfigureAwait(false);
                else
                {
                    var charges = await AnalysisObjectChargeIndex.BuildAsync(side.Graph, descriptor, Metric, _io, _devices, cancellation).ConfigureAwait(false);
                    hierarchy = await AnalysisComparisonEntities.BuildAfterProducerCloseAsync(charges, () => charges.BuildHierarchyAsync(cancellation)).ConfigureAwait(false);
                }
                state = new(source, previous, Metric);
                await AnalysisIo.Run(_io, _devices, _ => { state.Initialize(); return true; }, cancellation).ConfigureAwait(false);
                using (var entries = state.RelationEntries(false, cancellation).GetEnumerator())
                    state.Relations = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations * 2, _io, _devices, cancellation).ConfigureAwait(false);
                using (var entries = state.RelationEntries(true, cancellation).GetEnumerator())
                    state.Placements = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations * 2, _io, _devices, cancellation).ConfigureAwait(false);
                while (!state.Finished)
                    await AnalysisIo.Run(_io, _devices, token =>
                    {
                        int records = 0; var watch = System.Diagnostics.Stopwatch.StartNew(); long bytes = 0;
                        while (!state.Finished && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                        { token.ThrowIfCancellationRequested(); bytes += state.Step(token); if (watch.ElapsedMilliseconds >= 20) break; }
                        return true;
                    }, cancellation).ConfigureAwait(false);
                await AnalysisIo.Run(_io, _devices, token => { state.Seal(token); return true; }, cancellation).ConfigureAwait(false);
                using (var entries = state.ColorEntries(cancellation).GetEnumerator())
                    state.Colors = await BuildReaderAsync(state.Workspace!, entries, AnalysisLimits.Observations * 3 + 1, _io, _devices, cancellation).ConfigureAwait(false);
                await AnalysisIo.Run(_io, _devices, _ => { state.CloseBuildInputs(); return true; }, cancellation).ConfigureAwait(false);
                return true;
            }).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested();
            var result = new Chart(this, reader, hierarchy!, state!, previous); transferred = true; return result;
        }
        finally
        {
            if (!transferred)
            {
                // Release the place only after every unpublished owner actually closes.
                try { if (state is not null) await AnalysisIo.ReleaseScratchAsync(_io, _devices, state).ConfigureAwait(false); }
                finally { await CloseAsync(hierarchy, reader).ConfigureAwait(false); }
                if (admitted) Interlocked.Exchange(ref s_chart, 0);
            }
        }
    }

    internal sealed class Chart : IAsyncDisposable
    {
        private readonly AnalysisIndexedComparison _owner;
        private readonly ReadLease _source;
        private readonly AnalysisDatasetHierarchy _hierarchy;
        private readonly AnalysisOwnedDatasetIo<ChartState> _owned;
        private readonly object _gate = new(); private Task? _closed;
        internal bool Previous { get; }
        internal bool CombinedAreaAvailable => _hierarchy.Totals.CompatibleBasis;
        internal AnalysisDatasetDescriptor Descriptor => _hierarchy.Descriptor;
        internal Chart(AnalysisIndexedComparison owner, ReadLease source, AnalysisDatasetHierarchy hierarchy, ChartState state, bool previous)
        { _owner = owner; _source = source; _hierarchy = hierarchy; _owned = new(state, owner._io, owner._devices); Previous = previous; }
        internal Task<ImmutableArray<(ulong Id, string Label)>> ReadRootsAsync(CancellationToken cancellation = default) =>
            _owned.UseAsync(async state =>
            {
                using var admission = AnalysisExplorerAdmission.Acquire();
                var roots = ImmutableArray.CreateBuilder<(ulong, string)>();
                for (int n = 0; n < Descriptor.Roots.Length; n++)
                {
                    int index = n;
                    var root = await AnalysisIo.Run(_owner._io, _owner._devices, token => state.Root(index, token), cancellation).ConfigureAwait(false);
                    if (root is { } row) roots.Add(row);
                }
                return roots.ToImmutable();
            });
        internal Task<AnalysisIndexedComparisonChartProjection> LoadAsync(string scope = AnalysisTree.RootKey, CancellationToken cancellation = default) =>
            _owned.UseAsync(async state =>
            {
                using var admission = AnalysisExplorerAdmission.Acquire();
                AnalysisIndexedChart geometry;
                if (AnalysisTree.Other(scope, out int skip, out string parent))
                {
                    for (int depth = 0; AnalysisTree.Other(parent, out int more, out string ancestor); depth++)
                    {
                        if (depth >= AnalysisLimits.Depth) throw new InvalidDataException("Display aggregation nesting limit.");
                        skip = checked(skip + more); parent = ancestor;
                    }
                    await using var subset = await _hierarchy.CreateSubsetAsync(PathId(parent), skip, cancellation).ConfigureAwait(false);
                    geometry = await AnalysisIndexedChart.BuildAsync(subset, cancellation).ConfigureAwait(false);
                }
                else
                {
                    ulong folder = PathId(scope);
                    if (folder == 0 && !CombinedAreaAvailable) throw new InvalidOperationException("These locations report different allocation components. Choose one recorded location to explore On disk.");
                    geometry = await AnalysisIndexedChart.BuildAsync(_hierarchy, folder, cancellation).ConfigureAwait(false);
                }
                var items = geometry.Items.ToBuilder(); var keys = items.Keys.ToArray(); int next = 0;
                while (next < keys.Length)
                    await AnalysisIo.Run(_owner._io, _owner._devices, token =>
                    {
                        int count = 0; var watch = System.Diagnostics.Stopwatch.StartNew();
                        while (next < keys.Length && count++ < AnalysisLimits.BatchRecords)
                        {
                            token.ThrowIfCancellationRequested(); string key = keys[next++]; var item = items[key];
                            items[key] = item with { ComparisonChange = state.Evidence(item, token).Change ?? AnalysisChange.Unknown };
                            if (watch.ElapsedMilliseconds >= 20) break;
                        }
                        return true;
                    }, cancellation).ConfigureAwait(false);
                cancellation.ThrowIfCancellationRequested();
                return new AnalysisIndexedComparisonChartProjection(geometry.WithItems(items.ToImmutable()), Previous);
            });
        internal Task<AnalysisIndexedComparisonRow?> RowForAsync(AnalysisTreeItem item, CancellationToken cancellation = default) =>
            _owned.UseAsync(async state =>
            {
                ulong id = await AnalysisIo.Run(_owner._io, _owner._devices, token => state.Evidence(item, token).Row, cancellation).ConfigureAwait(false);
                return id == 0 ? null : await _source.UseAsync(source => AnalysisIo.Run(_owner._io, _owner._devices,
                    token => source.ReadRow(id, token, out _), cancellation)).ConfigureAwait(false);
            });
        public ValueTask DisposeAsync() { lock (_gate) return new(_closed ??= CloseCoreAsync()); }
        private async Task CloseCoreAsync()
        {
            await CloseAsync(_owned, _hierarchy, _source).ConfigureAwait(false);
            Interlocked.Exchange(ref s_chart, 0);
        }
    }

    internal readonly record struct ChartEvidence(AnalysisChange? Change, ulong Row)
    {
        internal ChartEvidence Merge(ChartEvidence other) => Change is null ? other : other.Change is null ? this :
            new(Change == other.Change ? Change : AnalysisChange.Unknown, Row == other.Row ? Row : 0);
        internal ulong Packed => (Row << 8) | (Change is { } change ? (ulong)(byte)change + 1 : 0);
        internal static ChartEvidence Unpack(ulong value)
        {
            byte tag = (byte)value;
            if (tag == 0 || !Enum.IsDefined((AnalysisChange)(tag - 1))) throw AnalysisIndexedGraph.Invalid();
            return new((AnalysisChange)(tag - 1), value >> 8);
        }
    }
    internal sealed class ChartState(State source, bool previous, AnalysisMetric metric) : IAnalysisScratchOwned
    {
        private AnalysisComparisonEntities.State Side => previous ? source.Previous : source.Current;
        internal AnalysisScratchWorkspace? Workspace;
        internal AnalysisSortedIndexReader? Relations, Placements, Colors;
        private AnalysisScratchArena? _arena;
        private AnalysisScratchVector? _writer;
        private AnalysisScratchVectorCheckpoint? _input;
        private readonly Stack<Frame> _pending = [];
        private int _root;
        internal bool Finished;
        private Exception? _closeFailure;
        private bool _disposed;
        internal void Initialize()
        {
            Workspace = Side.Graph.BorrowWorkspace(); _arena = Workspace.CreateArena();
            _writer = new(_arena, recordLimit: AnalysisLimits.Observations * 3 + 1); _arena = null;
            _pending.Push(new(0, default, default));
        }
        internal (ulong Id, string Label)? Root(int index, CancellationToken token)
        {
            var range = Side.Graph.Children(index, 0, token); if (range.Count > 1) throw AnalysisIndexedGraph.Invalid();
            if (range.Count == 0) return null;
            var path = Side.Graph.ReadPathRow(Side.Graph.ReadIndex(AnalysisIndexedGraphRevision.Children, range.First, token).Value, token);
            if (path.Kind != AnalysisKind.Directory) return null;
            return (path.Id, AnalysisNames.DisplayName(path, 128) + " [" + AnalysisNames.Display(path.RootId, 64) + "]");
        }
        internal IEnumerable<AnalysisSortInput> RelationEntries(bool placement, CancellationToken token)
        {
            for (ulong id = 1; id <= source.Rows!.Count; id++)
            {
                token.ThrowIfCancellationRequested(); var data = DecodeRow(source.Rows.Read(id, token)); ulong entity = previous ? data.Previous : data.Current;
                if (entity == 0) { yield return new(null, 64); continue; }
                if (!placement) { yield return new(new(AnalysisGraphKeys.PathId(entity), id), 128); continue; }
                var side = Side.ReadEvidence(entity, token, out long bytes);
                byte kind = side.DirectorySelf ? (byte)2 : side.ObservedPaths > 1 && side.ObservedIdentity is not null && metric is AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk ? (byte)1 : (byte)0;
                yield return new(kind != 0 && side.AttributionParent is { } parent ? new(ChartKey(kind, parent), id) : null, checked((int)bytes));
            }
        }
        internal long Step(CancellationToken token)
        {
            var frame = _pending.Peek();
            if (frame.NextRelation < frame.Relations.Count)
            {
                ulong row = Relations!.Read(frame.Relations.First + frame.NextRelation++, token).Value;
                frame.Amount = frame.Amount.Merge(RowEvidence(row, token)); return 128;
            }
            if (frame.Kind < 3)
            {
                if (!frame.LoadedPlacement) { frame.Placements = Placements!.FindRange(ChartKey(frame.Kind, frame.Id), token); frame.LoadedPlacement = true; }
                if (frame.NextPlacement < frame.Placements.Count)
                {
                    ulong row = Placements!.Read(frame.Placements.First + frame.NextPlacement++, token).Value;
                    frame.Bucket = frame.Bucket.Merge(RowEvidence(row, token)); return 128;
                }
                if (frame.Bucket.Change is not null) { Append(frame.Kind, frame.Id, frame.Bucket, token); frame.Amount = frame.Amount.Merge(frame.Bucket); }
                frame.Kind++; frame.Bucket = default; frame.NextPlacement = 0; frame.LoadedPlacement = false; return 128;
            }
            if (frame.Id == 0 && _root < Side.Graph.Header.Roots.Length)
            {
                var roots = Side.Graph.Children(_root++, 0, token); if (roots.Count > 1) throw AnalysisIndexedGraph.Invalid();
                return roots.Count == 0 ? 1 : Push(Side.Graph.ReadIndex(AnalysisIndexedGraphRevision.Children, roots.First, token).Value, token);
            }
            if (frame.NextChild < frame.Children.Count)
                return Push(Side.Graph.ReadIndex(AnalysisIndexedGraphRevision.Children, frame.Children.First + frame.NextChild++, token).Value, token);
            _pending.Pop(); if (frame.Amount.Change is null) frame.Amount = new(AnalysisChange.Unknown, 0);
            Append(0, frame.Id, frame.Amount, token);
            if (_pending.TryPeek(out var parent)) parent.Amount = parent.Amount.Merge(frame.Amount); else Finished = true;
            return 128;
        }
        private long Push(ulong row, CancellationToken token)
        {
            if (_pending.Count > AnalysisLimits.Depth + 1) throw AnalysisIndexedGraph.Invalid();
            var path = Side.Graph.ReadPathRowRecord(row, token); var node = path.Node;
            ulong entity = Side.EntityForPathRow(row, token);
            var relations = entity == 0 ? default : Relations!.FindRange(AnalysisGraphKeys.PathId(entity), token);
            var frame = new Frame(node.Id, node.Kind == AnalysisKind.Directory ? Side.Graph.Children(path.RootIndex, node.Id, token) : default, relations);
            if (node.Kind != AnalysisKind.Directory && relations.Count == 0 || node.MetadataState != MeasurementState.Known || !node.IncludedByPolicy)
                frame.Amount = new(AnalysisChange.Unknown, 0);
            _pending.Push(frame); return 1024L + node.Name.Length * 2L + node.RawName.Length;
        }
        private ChartEvidence RowEvidence(ulong id, CancellationToken token) => new(DecodeRow(source.Rows!.Read(id, token)).Change, id);
        private void Append(byte kind, ulong path, ChartEvidence evidence, CancellationToken token)
        {
            byte[] bytes = new byte[17]; ChartKey(kind, path).CopyTo(bytes, 0); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(9), evidence.Packed); _writer!.Append(bytes, token);
        }
        internal void Seal(CancellationToken token) { _writer!.Flush(token); _input = _writer.CaptureCommitted(); _writer.Dispose(); _writer = null; }
        internal IEnumerable<AnalysisSortInput> ColorEntries(CancellationToken token)
        {
            for (ulong id = 1; id <= _input!.Count; id++)
            { token.ThrowIfCancellationRequested(); byte[] b = _input.Read(id, token); if (b.Length != 17) throw AnalysisIndexedGraph.Invalid(); yield return new(new(b.AsSpan(0, 9).ToArray(), BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(9))), 128); }
        }
        private ChartEvidence EvidenceCore(byte kind, ulong id, CancellationToken token)
        {
            var range = Colors!.FindRange(ChartKey(kind, id), token);
            return range.Count == 0 ? new(AnalysisChange.Unknown, 0) : range.Count == 1 ? ChartEvidence.Unpack(Colors.Read(range.First, token).Value) : throw AnalysisIndexedGraph.Invalid();
        }
        internal ChartEvidence Evidence(AnalysisTreeItem item, CancellationToken token)
        {
            if (item.NodeId is { } id) return EvidenceCore(0, id, token);
            if (item.Key == AnalysisTree.RootKey) return EvidenceCore(0, 0, token);
            if (item.Family is "shared" or "directory-allocation") return EvidenceCore(item.Family == "shared" ? (byte)1 : (byte)2, PathId(item.ParentKey), token);
            return new(AnalysisChange.Unknown, 0);
        }
        internal void CloseBuildInputs()
        {
            var a = Relations; var b = Placements; var input = _input; var workspace = Workspace;
            Relations = Placements = null; _input = null; Workspace = null;
            try { CloseAll(a, b, input, workspace); } catch (Exception error) { _closeFailure = error; throw; }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; Exception? failure = _closeFailure;
            try { CloseAll(Colors, Relations, Placements, _input, _writer, _arena, Workspace); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
        private sealed class Frame(ulong id, AnalysisIndexRange children, AnalysisIndexRange relations)
        {
            internal readonly ulong Id = id;
            internal readonly AnalysisIndexRange Children = children, Relations = relations;
            internal AnalysisIndexRange Placements;
            internal ulong NextChild, NextRelation, NextPlacement;
            internal byte Kind = 1;
            internal bool LoadedPlacement;
            internal ChartEvidence Amount, Bucket;
        }
    }
    private static byte[] ChartKey(byte kind, ulong path) { byte[] key = new byte[9]; key[0] = kind; BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(1), path); return key; }
    private static ulong PathId(string key) => key == AnalysisTree.RootKey ? 0 : key.StartsWith("n:", StringComparison.Ordinal) &&
        ulong.TryParse(key.AsSpan(2), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id) && id != 0 ? id : throw new ArgumentException("Choose a recorded folder chart scope.", nameof(key));
}

internal sealed class AnalysisIndexedComparisonChartProjection(AnalysisIndexedChart geometry, bool previous) : IAnalysisComparisonChartData
{
    public AnalysisMetric Metric => geometry.Metric;
    public DateTimeOffset ObservedUtc => geometry.ObservedUtc;
    public bool Previous => previous;
    public string Scope => geometry.Scope;
    public bool DetailLimited => geometry.DetailLimited;
    public ImmutableDictionary<string, AnalysisTreeItem> Items => geometry.Items;
    public string ParentOf(string key) => geometry.ParentOf(key);
    public string NameOf(string key) => geometry.NameOf(key);
    public UInt128 ScopeValue(string key) => geometry.ScopeValue(key);
    public ImmutableArray<AnalysisTreeItem> ChildrenOf(string key) => geometry.ChildrenOf(key);
    public bool CanExplore(string key) => AnalysisTree.Other(key, out _, out _) || Items.GetValueOrDefault(key) is { Kind: AnalysisKind.Directory };
}
