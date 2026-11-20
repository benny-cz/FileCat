using System.Buffers.Binary;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

internal sealed partial class AnalysisIndexedComparison
{
    private static async Task<bool> SameMetadataAsync(AnalysisComparisonEntities.State a, ulong previous,
        AnalysisComparisonEntities.State b, ulong current, DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation)
    {
        bool uniform = await AnalysisIo.Run(io, devices, token =>
        {
            var old = a.ReadEvidence(previous, token, out _); var now = b.ReadEvidence(current, token, out _);
            return old.UniformObjectMetadata && now.UniformObjectMetadata && old.UniformPathMetadata && now.UniformPathMetadata;
        }, cancellation).ConfigureAwait(false);
        if (uniform)
        {
            var x = await AnalysisIo.Run(io, devices, token => UniformMetadata(a, previous, token), cancellation).ConfigureAwait(false);
            var y = await AnalysisIo.Run(io, devices, token => UniformMetadata(b, current, token), cancellation).ConfigureAwait(false);
            return x.Path.AsSpan().SequenceEqual(y.Path) && (x.Object is null ? y.Object is null : y.Object is not null && x.Object.AsSpan().SequenceEqual(y.Object));
        }
        var work = new MetadataWork();
        try
        {
            work.Workspace = await AnalysisIo.Run(io, devices, _ => b.Graph.BorrowWorkspace(), cancellation).ConfigureAwait(false);
            using (var entries = MetadataEntries(a, previous, cancellation).GetEnumerator())
                work.Previous = await BuildReaderAsync(work.Workspace, entries, AnalysisLimits.Observations * 2, io, devices, cancellation, static (_, _, _) => true).ConfigureAwait(false);
            using (var entries = MetadataEntries(b, current, cancellation).GetEnumerator())
                work.Current = await BuildReaderAsync(work.Workspace, entries, AnalysisLimits.Observations * 2, io, devices, cancellation, static (_, _, _) => true).ConfigureAwait(false);
            var compare = new ExactSetComparison(work.Previous, work.Current);
            while (!compare.Finished)
                await AnalysisIo.Run(io, devices, token =>
                {
                    int records = 0, bytes = 0;
                    while (!compare.Finished && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    { token.ThrowIfCancellationRequested(); bytes += compare.Step(token); }
                    return true;
                }, cancellation).ConfigureAwait(false);
            return compare.Equal;
        }
        finally { await AnalysisIo.ReleaseScratchAsync(io, devices, work).ConfigureAwait(false); }
    }
    private static (byte[] Path, byte[]? Object) UniformMetadata(AnalysisComparisonEntities.State source, ulong entity, CancellationToken token)
    {
        var obj = source.FirstObject(entity, token);
        return (PathMetadata(source.Graph.ReadPathRow(source.MemberPathRow(entity, 0, token), token)), obj is null ? null : ObjectMetadata(obj));
    }

    private static IEnumerable<AnalysisSortInput> MetadataEntries(AnalysisComparisonEntities.State source, ulong entity, CancellationToken token)
    {
        ulong count = source.ReadEvidence(entity, token, out _).ObservedPaths;
        for (ulong at = 0; at < count; at++)
        {
            token.ThrowIfCancellationRequested(); ulong pathRow = source.MemberPathRow(entity, at, token); var node = source.Graph.ReadPathRow(pathRow, token);
            yield return new(new(PathMetadata(node), pathRow), checked((int)(1024L + node.Name.Length * 2L + node.RawName.Length)));
            AnalysisObject? obj = null;
            if (node.ObjectId is { } id)
            {
                var range = source.Graph.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(id), cancellation: token);
                if (range.Count != 1) throw AnalysisIndexedGraph.Invalid();
                obj = source.Graph.ReadObject(source.Graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value, token);
            }
            yield return new(obj is null ? null : new(ObjectMetadata(obj), pathRow), 1024 + (obj?.VersionEvidence?.Length ?? 0) * 2);
        }
    }
    private static byte[] PathMetadata(AnalysisNode n)
    {
        byte[] bytes = new byte[69]; bytes[0] = 1; bytes[1] = (byte)n.Kind;
        static byte Flag(bool? value) => value is null ? (byte)0 : value.Value ? (byte)2 : (byte)1;
        bytes[2] = Flag(n.Sparse); bytes[3] = Flag(n.Compressed); bytes[4] = Flag(n.Cloud);
        Instant(bytes.AsSpan(5), n.Times?.ModifiedUtc); Number(bytes.AsSpan(14), n.Times?.ModifiedPrecisionTicks);
        Instant(bytes.AsSpan(23), n.Times?.CreatedUtc); Number(bytes.AsSpan(32), n.Times?.CreatedPrecisionTicks);
        Instant(bytes.AsSpan(41), n.Times?.ChangedUtc); Number(bytes.AsSpan(50), n.Times?.ChangedPrecisionTicks);
        return bytes;
    }
    private static byte[] ObjectMetadata(AnalysisObject o)
    {
        byte[] bytes = new byte[52]; bytes[0] = 0;
        bytes[1] = (byte)o.LogicalLength.State; BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(2), o.LogicalLength.Value ?? 0);
        bytes[18] = (byte)o.LinkCount.State; BinaryPrimitives.WriteUInt128LittleEndian(bytes.AsSpan(19), o.LinkCount.Value ?? 0);
        Instant(bytes.AsSpan(35), o.ModifiedUtc); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(44), o.ModifiedPrecisionTicks); return bytes;
    }
    private static void Instant(Span<byte> bytes, DateTimeOffset? value)
    { bytes[0] = value.HasValue ? (byte)1 : (byte)0; BinaryPrimitives.WriteInt64LittleEndian(bytes[1..], value?.UtcTicks ?? 0); }
    private static void Number(Span<byte> bytes, ulong? value)
    { bytes[0] = value.HasValue ? (byte)1 : (byte)0; BinaryPrimitives.WriteUInt64LittleEndian(bytes[1..], value ?? 0); }

    private sealed class ExactSetComparison(AnalysisSortedIndexReader previous, AnalysisSortedIndexReader current)
    {
        private ulong _a = 1, _b = 1;
        private byte[]? _lastA, _lastB, _keyA, _keyB;
        private bool _endA, _endB;
        internal bool Equal = true, Finished;
        internal int Step(CancellationToken token)
        {
            if (_keyA is null && !_endA)
            {
                if (_a > previous.Count) _endA = true;
                else { byte[] key = previous.Read(_a++, token).Key; if (_lastA is null || !key.AsSpan().SequenceEqual(_lastA)) _keyA = key; return key.Length + 32; }
            }
            if (_keyB is null && !_endB)
            {
                if (_b > current.Count) _endB = true;
                else { byte[] key = current.Read(_b++, token).Key; if (_lastB is null || !key.AsSpan().SequenceEqual(_lastB)) _keyB = key; return key.Length + 32; }
            }
            if (_keyA is null && !_endA || _keyB is null && !_endB) return 1;
            if (_endA || _endB) { Equal = _endA && _endB; Finished = true; return 1; }
            if (!_keyA!.AsSpan().SequenceEqual(_keyB)) { Equal = false; Finished = true; return 1; }
            _lastA = _keyA; _lastB = _keyB; _keyA = _keyB = null; return 1;
        }
    }
    private sealed class MetadataWork : IAnalysisScratchOwned
    {
        internal AnalysisScratchWorkspace? Workspace;
        internal AnalysisSortedIndexReader? Previous, Current;
        public void Dispose() => CloseAll(Current, Previous, Workspace);
    }
    private static async Task<AnalysisSortedIndexReader> BuildReaderAsync(AnalysisScratchWorkspace workspace, IEnumerator<AnalysisSortInput> entries,
        int maximum, DeviceIoScheduler io, AnalysisIoDevices devices, CancellationToken cancellation,
        Func<AnalysisSortEntry, AnalysisSortEntry, CancellationToken, bool>? duplicate = null)
    {
        var index = await AnalysisExternalSort.BuildSparseAsync(workspace, io, devices, entries, cancellation, maximumRecords: maximum, duplicate: duplicate).ConfigureAwait(false);
        AnalysisSortedIndexReader? reader = null;
        try { reader = await AnalysisIo.Run(io, devices, _ => index.Acquire(), cancellation).ConfigureAwait(false); }
        finally
        {
            try { await AnalysisIo.ReleaseScratchAsync(io, devices, index).ConfigureAwait(false); }
            catch (Exception error)
            {
                if (reader is not null)
                    try { await AnalysisIo.ReleaseScratchAsync(io, devices, reader).ConfigureAwait(false); }
                    catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                throw;
            }
        }
        return reader;
    }
    private static void CloseAll(params IDisposable?[] owners)
    {
        Exception? failure = null;
        foreach (var owner in owners) try { owner?.Dispose(); } catch (Exception error) { failure = failure is null ? error : new AggregateException(failure, error); }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
