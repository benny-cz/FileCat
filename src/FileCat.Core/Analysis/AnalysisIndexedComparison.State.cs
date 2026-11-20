using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    internal sealed partial class State(AnalysisComparisonPaths.State paths, AnalysisComparisonEntities.State previous,
        AnalysisComparisonEntities.State current, AnalysisDatasetDescriptor oldDescriptor, AnalysisDatasetDescriptor newDescriptor,
        ImmutableDictionary<string, string> mapping, bool structural) : IAnalysisScratchOwned
    {
        internal AnalysisScratchWorkspace? Workspace;
        internal AnalysisSortedIndexReader? OldBases, NewBases, PathEdges, Edges, Incoming, OldIdentities, NewIdentities, Order;
        internal AnalysisSortedIndexReader? PathRows, Positions;
        private AnalysisScratchVector? _fallbackWriter, _rowWriter;
        private AnalysisScratchArena? _pendingArena;
        private AnalysisScratchVectorCheckpoint? _fallback;
        internal AnalysisScratchVectorCheckpoint? Rows;
        internal AnalysisComparisonCompatibility Compatibility = null!;
        internal bool Structural => structural;
        internal bool HasCurrentRows, HasPreviousOnlyRows;
        private uint _mappedPrevious, _mappedCurrent;
        private Exception? _closeFailure;
        private bool _disposed;
        internal void Initialize()
        {
            Workspace = current.Graph.BorrowWorkspace();
            _pendingArena = Workspace.CreateArena(); _fallbackWriter = new(_pendingArena); _pendingArena = null;
            _pendingArena = Workspace.CreateArena(); _rowWriter = new(_pendingArena, recordLimit: AnalysisLimits.Observations * 2); _pendingArena = null;
            for (int n = 0; n < oldDescriptor.Roots.Length; n++) if (mapping.ContainsKey(oldDescriptor.Roots[n].Id)) _mappedPrevious |= 1U << n;
            for (int n = 0; n < newDescriptor.Roots.Length; n++) if (mapping.Values.Contains(newDescriptor.Roots[n].Id, StringComparer.Ordinal)) _mappedCurrent |= 1U << n;
        }
        internal bool Mapped(AnalysisComparisonAccountingEntity entity, bool old) => (entity.RootMask & ~(old ? _mappedPrevious : _mappedCurrent)) == 0;
        internal IEnumerable<AnalysisSortInput> PathEntries(CancellationToken token)
        {
            for (ulong row = 1; row <= newDescriptor.Paths; row++)
            {
                token.ThrowIfCancellationRequested(); ulong beforeRow = paths.PreviousRow(row, token);
                ulong a = beforeRow == 0 ? 0 : previous.EntityForPathRow(beforeRow, token); ulong b = current.EntityForPathRow(row, token);
                bool sameKind = false; long cost = 128;
                if (a != 0 && b != 0)
                {
                    var old = previous.ReadEvidence(a, token, out long ac); var now = current.ReadEvidence(b, token, out long bc);
                    sameKind = old.DirectorySelf == now.DirectorySelf; cost += ac + bc;
                }
                yield return new(sameKind ? new(Pair(b, a), row) : null, checked((int)cost));
            }
        }
        internal IEnumerable<AnalysisSortInput> IdentityEntries(AnalysisComparisonEntities.State source, CancellationToken token)
        {
            for (ulong row = 1; row <= source.Count; row++)
            {
                token.ThrowIfCancellationRequested(); var entity = source.ReadEvidence(row, token, out long cost);
                byte[]? key = IdentityKey(entity); yield return new(key is null ? null : new(key, row), checked((int)cost));
            }
        }
        internal void AddFallback(ulong before, ulong now, CancellationToken token) => _fallbackWriter!.Append(Pair(now, before), token);
        internal void SealFallback(CancellationToken token)
        { _fallbackWriter!.Flush(token); _fallback = _fallbackWriter.CaptureCommitted(); _fallbackWriter.Dispose(); _fallbackWriter = null; }
        internal IEnumerable<AnalysisSortInput> EdgeEntries(bool reverse, CancellationToken token)
        {
            for (ulong row = 1; row <= PathEdges!.Count; row++)
            {
                token.ThrowIfCancellationRequested(); var entry = PathEdges.Read(row, token);
                yield return new(new(reverse ? Pair(Second(entry.Key), First(entry.Key)) : entry.Key, 1), 32);
            }
            for (ulong row = 1; row <= _fallback!.Count; row++)
            {
                token.ThrowIfCancellationRequested(); byte[] pair = _fallback.Read(row, token);
                yield return new(new(reverse ? Pair(Second(pair), First(pair)) : pair, 1), 32);
            }
        }
        internal void SealRows(CancellationToken token)
        { _rowWriter!.Flush(token); Rows = _rowWriter.CaptureCommitted(); _rowWriter.Dispose(); _rowWriter = null; }
        internal IEnumerable<AnalysisSortInput> RankEntries(CancellationToken token)
        {
            for (ulong id = 1; id <= Rows!.Count; id++)
            {
                token.ThrowIfCancellationRequested(); var row = DecodeRow(Rows.Read(id, token)); byte[] key = new byte[24];
                BinaryPrimitives.WriteUInt128BigEndian(key, UInt128.MaxValue - (row.Delta?.Magnitude ?? 0));
                BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(16), id); yield return new(new(key, id), 64);
            }
        }
        private void CloseTransient(bool basesOnly)
        {
            var oldBases = OldBases; var newBases = NewBases; OldBases = NewBases = null;
            if (basesOnly) { TryClose(oldBases, newBases); return; }
            var pathsIndex = PathEdges; var incoming = Incoming; var edges = Edges; var a = OldIdentities; var b = NewIdentities;
            var fallback = _fallback; var workspace = Workspace;
            PathEdges = Incoming = Edges = OldIdentities = NewIdentities = null; _fallback = null; Workspace = null;
            TryClose(oldBases, newBases, pathsIndex, incoming, edges, a, b, fallback, workspace);
        }
        private void TryClose(params IDisposable?[] owners)
        {
            try { CloseAll(owners); }
            catch (Exception error) { _closeFailure = _closeFailure is null ? error : new AggregateException(_closeFailure, error); throw; }
        }
        internal void CloseBases() => CloseTransient(true);
        internal void FinishBuild() => CloseTransient(false);
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; Exception? failure = _closeFailure;
            try { CloseAll(PathRows, Positions, Order, Rows, _rowWriter, _fallbackWriter, _fallback, OldBases, NewBases, PathEdges, Incoming, Edges, OldIdentities, NewIdentities, _pendingArena, Workspace); }
            catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
            if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
    private static byte[] Pair(ulong first, ulong second)
    { byte[] bytes = new byte[16]; BinaryPrimitives.WriteUInt64BigEndian(bytes, first); BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), second); return bytes; }
    private static ulong First(byte[] pair) => pair.Length == 16 ? BinaryPrimitives.ReadUInt64BigEndian(pair) : throw AnalysisIndexedGraph.Invalid();
    private static ulong Second(byte[] pair) => pair.Length == 16 ? BinaryPrimitives.ReadUInt64BigEndian(pair.AsSpan(8)) : throw AnalysisIndexedGraph.Invalid();
    private static byte[]? IdentityKey(AnalysisComparisonAccountingEntity e)
    {
        if (e.ObservedIdentity is null || e.VersionEvidence is null) return null;
        byte[] identity = BasisUtf8.GetBytes(e.ObservedIdentity), version = BasisUtf8.GetBytes(e.VersionEvidence);
        byte[] key = new byte[8 + identity.Length + version.Length]; BinaryPrimitives.WriteInt32BigEndian(key, identity.Length);
        identity.CopyTo(key, 4); BinaryPrimitives.WriteInt32BigEndian(key.AsSpan(4 + identity.Length), version.Length); version.CopyTo(key, 8 + identity.Length); return key;
    }
}
