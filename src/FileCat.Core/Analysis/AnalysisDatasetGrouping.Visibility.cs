using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public sealed partial class AnalysisDatasetGrouping
{
    public async Task<AnalysisSelectionVisibility> ClassifyMarksAsync(ImmutableHashSet<ulong> marks, string? scope = null, CancellationToken cancellation = default)
    {
        using var counter = new AnalysisMarkCounter(marks); cancellation.ThrowIfCancellationRequested();
        using var admission = AnalysisExplorerAdmission.Acquire();
        return await _owned.UseAsync(async state =>
        {
            AnalysisDatasetGroup? group = scope is null || scope == AnalysisTree.RootKey ? null :
                await AnalysisIo.Run(_io, _device, token => state.Group(scope, token), cancellation).ConfigureAwait(false);
            while (!counter.Complete)
                await AnalysisIo.Run(_io, _device, token =>
                {
                    int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes && counter.Next(out ulong id))
                    {
                        token.ThrowIfCancellationRequested();
                        var visible = state.SelectionVisibility(id, token);
                        if (visible is null) counter.Count(AnalysisMarkPlace.Missing);
                        else if (!visible.Value) counter.Count(AnalysisMarkPlace.HiddenByFilter);
                        else
                        {
                            bool member;
                            if (group is not null) member = state.HasMember(group.Definition.Key, id, group.Kind != AnalysisGroupKind.Entries, token);
                            else
                            {
                                var node = state.Graph.ReadPath(id, token); bytes += AnalysisIndexedFilter.Cost(node);
                                member = node.Kind == AnalysisKind.Directory ? state.HasMember(AnalysisGroupingRules.Directory.Key, id, true, token) :
                                    node.Kind == AnalysisKind.File && node.IncludedByPolicy && state.HasMember(AnalysisGroupingRules.Classify(Dimension, node,
                                        Dimension == AnalysisGroupingDimension.ModifiedAge && node.Times is null ? state.Object(node.ObjectId, token) : null,
                                        Descriptor.Identity, Descriptor.Roots).Key, id, false, token);
                            }
                            counter.Count(member ? AnalysisMarkPlace.InScope : AnalysisMarkPlace.Elsewhere);
                        }
                        bytes += 1024; if (clock.ElapsedMilliseconds >= 20) break;
                    }
                    return true;
                }, cancellation).ConfigureAwait(false);
            cancellation.ThrowIfCancellationRequested(); return counter.Result(Descriptor.Identity);
        }).ConfigureAwait(false);
    }

    private sealed partial class State
    {
        private AnalysisScratchVector? _visibilityWriter;
        internal AnalysisScratchVectorCheckpoint? Visibility;
        internal AnalysisSortedIndexReader? VisibilityIndex;
        private readonly Stack<VisibilityFrame> _visibilityFrames = [];
        private bool _visibilityStarted;
        private int _visibilityRoot;
        private ulong _visibilitySeen;
        internal bool VisibilityFinished { get; private set; }
        private sealed class VisibilityFrame(ulong id, AnalysisIndexRange children, bool visible)
        { internal readonly ulong Id = id; internal readonly AnalysisIndexRange Children = children; internal ulong Next; internal bool Visible = visible; }
        internal long VisibilityStep(AnalysisIndexedFilter? query, CancellationToken token)
        {
            if (!_visibilityStarted) { _visibilityStarted = true; _visibilityFrames.Push(new(0, default, true)); return 1; }
            var frame = _visibilityFrames.Peek();
            if (frame.Id == 0 && _visibilityRoot < Graph.Header.Roots.Length)
            {
                var roots = Graph.Children(_visibilityRoot++, 0, token); if (roots.Count > 1) throw Invalid();
                return roots.Count == 0 ? 1 : Push(Graph.ReadIndex(AnalysisIndexedGraphRevision.Children, roots.First, token).Value);
            }
            if (frame.Next < frame.Children.Count) return Push(Graph.ReadIndex(AnalysisIndexedGraphRevision.Children, frame.Children.First + frame.Next++, token).Value);
            _visibilityFrames.Pop();
            if (frame.Id == 0)
            {
                if (_visibilitySeen != Graph.Count(AnalysisIndexedGraph.Paths)) throw Invalid(); VisibilityFinished = true; return 1;
            }
            byte[] record = new byte[9]; BinaryPrimitives.WriteUInt64LittleEndian(record, frame.Id); record[8] = frame.Visible ? (byte)1 : (byte)0;
            _visibilityWriter!.Append(record, token); _visibilitySeen++;
            _visibilityFrames.Peek().Visible |= frame.Visible; return 256;
            long Push(ulong row)
            {
                if (_visibilityFrames.Count > AnalysisLimits.Depth + 1) throw Invalid();
                var path = Graph.ReadPathRowRecord(row, token); var node = path.Node;
                bool visible = query is null || query.IsUnfiltered || node.Kind == AnalysisKind.File && node.IncludedByPolicy && query.Matches(node.Id, token) ||
                    node.Kind == AnalysisKind.Directory && node.IncludedByPolicy && query.NamespaceMatches(node, _visibilityFrames.Select(f => f.Id), Graph.Header.Freshness);
                _visibilityFrames.Push(new(node.Id, node.Kind == AnalysisKind.Directory ? Graph.Children(path.RootIndex, node.Id, token) : default, visible));
                return AnalysisIndexedFilter.Cost(node);
            }
        }
        internal void SealVisibility(CancellationToken token)
        { _visibilityWriter!.Flush(token); Visibility = _visibilityWriter.CaptureCommitted(); _visibilityWriter.Dispose(); _visibilityWriter = null; }
        internal IEnumerable<AnalysisSortInput> VisibilityEntries(CancellationToken token)
        {
            for (ulong row = 1; row <= Visibility!.Count; row++)
            { byte[] data = Visibility.Read(row, token); if (data.Length != 9 || data[8] > 1) throw Invalid(); yield return new(new(Number(BinaryPrimitives.ReadUInt64LittleEndian(data)), row), 256); }
        }
        internal bool? SelectionVisibility(ulong id, CancellationToken token)
        {
            var range = VisibilityIndex!.FindRange(Number(id), token); if (range.Count == 0) return null; if (range.Count != 1) throw Invalid();
            byte[] record = Visibility!.Read(VisibilityIndex.Read(range.First, token).Value, token);
            if (record.Length != 9 || record[8] > 1 || BinaryPrimitives.ReadUInt64LittleEndian(record) != id) throw Invalid(); return record[8] == 1;
        }
        internal bool HasMember(string group, ulong id, bool reference, CancellationToken token)
        {
            var index = reference ? ReferenceIndex! : MemberIndex!; var range = index.FindRange(Join(Hash(group), Number(id)), token);
            if (range.Count > 1) throw Invalid(); return range.Count != 0;
        }
    }
}
