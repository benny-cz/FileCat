using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed record AnalysisTablePosition(ulong ParentId, ulong Offset, ulong Total);
public sealed record AnalysisCapturedTableRange(AnalysisSelection Selection, ulong FirstOffset, ulong LastOffset, ulong SyntheticRows);

public sealed partial class AnalysisDatasetHierarchy
{
    public async Task<AnalysisSelectionVisibility> ClassifyMarksAsync(ImmutableHashSet<ulong> marks, ulong scope = 0, CancellationToken cancellation = default)
    {
        using var counter = new AnalysisMarkCounter(marks); cancellation.ThrowIfCancellationRequested();
        using var admission = AnalysisExplorerAdmission.Acquire();
        return await _owned.UseAsync(async state =>
        {
            var cursor = await AnalysisIo.Run(_io, _device, token => new MarkCursor(state, counter, scope, token), cancellation).ConfigureAwait(false);
            while (!counter.Complete)
                await AnalysisIo.Run(_io, _device, token =>
                {
                    int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (!counter.Complete && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    { token.ThrowIfCancellationRequested(); bytes += cursor.Step(token); if (clock.ElapsedMilliseconds >= 20) break; }
                    return true;
                }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); return counter.Result(Descriptor.Identity);
        }).ConfigureAwait(false);
    }

    public async Task<AnalysisTablePosition> LocateChildAsync(ulong path, AnalysisHierarchySort sort = AnalysisHierarchySort.Metric,
        bool descending = true, CancellationToken cancellation = default)
    {
        using var admission = AnalysisExplorerAdmission.Acquire();
        return await _owned.RunAsync((state, token) =>
        {
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        var frame = state.SelectionFrame(path, token) ?? throw new ArgumentOutOfRangeException(nameof(path));
        if (!frame.Visible) throw new InvalidOperationException("This captured entry is outside the displayed query.");
        var node = state.Graph.ReadPath(path, token); var index = state.Indexes[SortIndex(sort)];
        var range = index.PrefixRange(Key(node.ParentId), token); var found = index.FindRange(state.TableKey(frame, node, sort), token);
        if (found.Count != 1 || found.First < range.First || found.First - range.First >= range.Count) throw Invalid();
        ulong relative = found.First - range.First, known = KnownRange(index, range, node.ParentId, sort, token).Count;
        ulong offset = !descending && relative < known ? known - 1 - relative :
            !descending && sort == AnalysisHierarchySort.MetadataState ? known + range.Count - 1 - relative : relative;
        return new AnalysisTablePosition(node.ParentId, offset, range.Count);
        }, cancellation).ConfigureAwait(false);
    }

    public async Task<AnalysisCapturedTableRange> CaptureTableRangeAsync(ulong parent, ulong first, ulong last,
        AnalysisHierarchySort sort = AnalysisHierarchySort.Metric, bool descending = true, CancellationToken cancellation = default)
    {
        if (!Enum.IsDefined(sort)) throw new ArgumentOutOfRangeException(nameof(sort));
        if (first > last || last - first >= AnalysisLimits.ReviewPaths) throw new InvalidOperationException("Refine the captured table range to at most 100,000 real paths.");
        using var admission = AnalysisExplorerAdmission.Acquire();
        return await _owned.UseAsync(async state =>
        {
            var start = await AnalysisIo.Run(_io, _device, token =>
            {
                state.Graph.RequireParent(parent, token); var index = state.Indexes[SortIndex(sort)]; var range = index.PrefixRange(Key(parent), token);
                if (last >= range.Count) throw new ArgumentOutOfRangeException(nameof(last));
                return (Index: index, Range: range, Known: KnownRange(index, range, parent, sort, token));
            }, cancellation).ConfigureAwait(false);
            var ids = ImmutableArray.CreateBuilder<ulong>(); ulong position = first;
            while (position <= last)
                await AnalysisIo.Run(_io, _device, token =>
                {
                    var clock = System.Diagnostics.Stopwatch.StartNew(); int records = 0;
                    while (position <= last && records++ < AnalysisLimits.BatchRecords)
                    {
                        token.ThrowIfCancellationRequested(); ulong at = OrderedIndex(start.Range, start.Known, position++, sort, descending);
                        ids.Add(state.SelectionId(start.Index.Read(at, token).Value, token)); if (clock.ElapsedMilliseconds >= 20) break;
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); var identity = Descriptor.Identity;
            return new AnalysisCapturedTableRange(new(identity.SessionId, identity.Epoch, identity.Revision, ids.ToImmutable())
                { ScopeSummary = parent == 0 ? "Captured top-level table rows" : "Captured folder table rows" }, first, last, 0);
        }).ConfigureAwait(false);
    }

    private static int SortIndex(AnalysisHierarchySort sort) => sort switch
    { AnalysisHierarchySort.FileSize => 5, AnalysisHierarchySort.Name => 6, AnalysisHierarchySort.MetadataState => 7, _ => (int)sort + 1 };
    private static AnalysisIndexRange KnownRange(AnalysisSortedIndexReader index, AnalysisIndexRange range, ulong parent, AnalysisHierarchySort sort, CancellationToken token)
    { byte[] prefix = new byte[9]; Key(parent).CopyTo(prefix, 0); return sort == AnalysisHierarchySort.Name ? range : index.PrefixRange(prefix, token); }
    private static ulong OrderedIndex(AnalysisIndexRange range, AnalysisIndexRange known, ulong position, AnalysisHierarchySort sort, bool descending) =>
        !descending && position < known.Count ? known.First + known.Count - 1 - position :
        !descending && sort == AnalysisHierarchySort.MetadataState && position >= known.Count ? range.First + range.Count - 1 - (position - known.Count) : range.First + position;

    private sealed class MarkCursor
    {
        private readonly State _state;
        private readonly AnalysisMarkCounter _counter;
        private readonly ulong _scope;
        private readonly AnalysisIndexedPathRecord _scopePath;
        private ulong _walk;
        private int _depth;
        internal MarkCursor(State state, AnalysisMarkCounter counter, ulong scope, CancellationToken token)
        {
            _state = state; _counter = counter; _scope = scope;
            if (scope != 0)
            {
                _scopePath = state.Graph.ReadPathRecord(scope, token);
                if (_scopePath.Node.Kind != AnalysisKind.Directory || state.SelectionFrame(scope, token)?.Visible != true)
                    throw new InvalidOperationException("Only a captured visible directory can define this mark scope.");
            }
        }
        internal long Step(CancellationToken token)
        {
            if (_walk != 0)
            {
                var path = _state.Graph.ReadPathRecord(_walk, token); if (++_depth > AnalysisLimits.Depth + 1) throw Invalid();
                if (path.Node.Id == _scope) { _counter.Count(AnalysisMarkPlace.InScope); _walk = 0; }
                else if (path.Depth <= _scopePath!.Depth) { _counter.Count(AnalysisMarkPlace.Elsewhere); _walk = 0; }
                else _walk = path.Node.ParentId;
                return AnalysisIndexedFilter.Cost(path.Node);
            }
            if (!_counter.Next(out ulong id)) return 1;
            var row = _state.SelectionFrame(id, token);
            if (row is null) _counter.Count(AnalysisMarkPlace.Missing);
            else if (!row.Visible) _counter.Count(AnalysisMarkPlace.HiddenByFilter);
            else if (_scope == 0 || _scope == id) _counter.Count(AnalysisMarkPlace.InScope);
            else
            {
                var path = _state.Graph.ReadPathRecord(id, token);
                if (path.RootIndex != _scopePath!.RootIndex || path.Depth <= _scopePath.Depth) _counter.Count(AnalysisMarkPlace.Elsewhere);
                else { _walk = path.Node.ParentId; _depth = 0; }
                return AnalysisIndexedFilter.Cost(path.Node);
            }
            return 512;
        }
    }

    private sealed partial class State
    {
        internal Frame? SelectionFrame(ulong id, CancellationToken token)
        {
            var range = Indexes[0].FindRange(Key(id), token); if (range.Count == 0) return null; if (range.Count != 1) throw Invalid();
            var frame = Decode(_rows!.Read(Indexes[0].Read(range.First, token).Value, token)); if (frame.Id != id) throw Invalid(); return frame;
        }
        internal ulong SelectionId(ulong row, CancellationToken token) => Decode(_rows!.Read(row, token)).Id;
        internal byte[] TableKey(Frame value, AnalysisNode path, AnalysisHierarchySort sort)
        {
            if (sort == AnalysisHierarchySort.Name) return AnalysisHierarchyNameKey.Create(value.Parent, value.Id, path.Name);
            bool nonFile = !path.IncludedByPolicy || path.Kind is not (AnalysisKind.File or AnalysisKind.Directory);
            UInt128 area = sort == AnalysisHierarchySort.MetadataState ? (uint)path.MetadataState : sort == AnalysisHierarchySort.FileSize ? value.Logical.Known : sort == AnalysisHierarchySort.Files ? value.Amount.Files :
                Metric == AnalysisMetric.OnDisk ? checked(value.Amount.Known + value.Amount.Unverified) : value.Amount.Known;
            bool missing = sort == AnalysisHierarchySort.MetadataState ? path.MetadataState != MeasurementState.Known : nonFile ||
                sort == AnalysisHierarchySort.FileSize && area == 0 && value.Logical.Missing.Skip(1).Any(n => n != 0) ||
                sort == AnalysisHierarchySort.Metric && area == 0 && (value.Elsewhere || value.Amount.Missing.Skip(1).Any(n => n != 0) || Metric == AnalysisMetric.UniqueFileSize && value.Amount.Unverified != 0);
            byte[] key = new byte[33]; Key(value.Parent).CopyTo(key, 0); key[8] = missing ? (byte)1 : (byte)0;
            Write128(key.AsSpan(9), UInt128.MaxValue - area); Key(value.Id).CopyTo(key, 25); return key;
        }
    }
}
