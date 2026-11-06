using System.Buffers.Binary;
using System.Collections.Immutable;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public enum AnalysisLogicalSort { FileSize, Files }
public sealed record AnalysisLogicalTotals(UInt128 KnownBytes, ulong Files, ImmutableDictionary<MeasurementState, ulong> Missing)
{
    public ulong UnknownFiles => Missing.Values.Aggregate(0UL, (sum, value) => checked(sum + value));
}
public sealed record AnalysisLogicalHierarchyRow(AnalysisPathMetadata Path, AnalysisLogicalTotals Totals, bool NamespaceIncomplete);

public sealed partial class AnalysisDatasetLease
{
    /// <summary>Builds exact File size/Files directory rollups and ranked pages in the retained private workspace.</summary>
    public Task<AnalysisLogicalHierarchy> BuildLogicalHierarchyAsync(CancellationToken cancellation = default) =>
        _owned.UseAsync(reader => AnalysisLogicalHierarchy.BuildAsync(reader, Descriptor, _io, _device, cancellation));
}

/// <summary>Immutable logical accounting over the whole revision. No source opening or operation authority.</summary>
public sealed class AnalysisLogicalHierarchy : IAsyncDisposable
{
    private static readonly SemaphoreSlim Builds = new(AnalysisLimits.ActiveScans, AnalysisLimits.ActiveScans);
    private readonly AnalysisOwnedDatasetIo<State> _owned;
    public AnalysisDatasetDescriptor Descriptor { get; }
    public AnalysisLogicalTotals Totals { get; }
    private AnalysisLogicalHierarchy(State state, AnalysisDatasetDescriptor descriptor, DeviceIoScheduler io, AnalysisIoDevices device)
    { Descriptor = descriptor; Totals = state.Total.Export(); _owned = new(state, io, device); }

    internal static async Task<AnalysisLogicalHierarchy> BuildAsync(AnalysisIndexedGraphReader source, AnalysisDatasetDescriptor descriptor,
        DeviceIoScheduler io, AnalysisIoDevices device, CancellationToken cancellation)
    {
        await Builds.WaitAsync(cancellation).ConfigureAwait(false); State? state = null;
        try
        {
            state = await AnalysisIo.Run(io, device, _ => State.Create(source), cancellation).ConfigureAwait(false);
            while (!state.Finished)
                await AnalysisIo.Run(io, device, token =>
                {
                    int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (!state.Finished && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    { token.ThrowIfCancellationRequested(); bytes += state.Step(token); if (clock.ElapsedMilliseconds >= 20) break; }
                    return true;
                }, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(io, device, token => { state.SealRows(token); return true; }, cancellation).ConfigureAwait(false);
            for (int kind = 0; kind < 3; kind++)
            {
                using var rows = state.Entries(kind, cancellation).GetEnumerator();
                var index = await AnalysisExternalSort.BuildSparseAsync(state.Workspace!, io, device, rows, cancellation).ConfigureAwait(false);
                try { await AnalysisIo.Run(io, device, _ => { state.Indexes.Add(index.Acquire()); return true; }, cancellation).ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(io, device, index).ConfigureAwait(false); }
            }
            await AnalysisIo.Run(io, device, _ => { state.Workspace!.Dispose(); state.Workspace = null; return true; }, cancellation).ConfigureAwait(false);
            var result = new AnalysisLogicalHierarchy(state, descriptor, io, device); state = null; return result;
        }
        finally
        {
            try { if (state is not null) await AnalysisIo.ReleaseScratchAsync(io, device, state).ConfigureAwait(false); }
            finally { Builds.Release(); }
        }
    }
    public Task<AnalysisLogicalHierarchyRow> ReadAsync(ulong path, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) =>
        {
            _ = state.Graph.PathRow(path, token);
            var range = state.Indexes[0].FindRange(Key(path), token);
            if (range.Count != 1) throw Invalid();
            return state.Read(state.Indexes[0].Read(range.First, token).Value, token);
        }, cancellation);
    public Task<AnalysisDatasetPage<AnalysisLogicalHierarchyRow>> ReadChildrenAsync(ulong parent, AnalysisLogicalSort sort, ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) =>
        {
            if (!Enum.IsDefined(sort) || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(parent));
            state.Graph.RequireParent(parent, token);
            var index = state.Indexes[(int)sort + 1]; var range = index.PrefixRange(Key(parent), token);
            if (offset > range.Count) throw new ArgumentOutOfRangeException(nameof(offset));
            var rows = ImmutableArray.CreateBuilder<AnalysisLogicalHierarchyRow>(); long bytes = 0;
            while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var row = state.Read(index.Read(range.First + offset + (ulong)rows.Count, token).Value, token);
                long cost = 512L + row.Path.Name.Length * 2L + row.Path.RawName.Length + row.Path.RootId.Length * 2L + (row.Path.ObjectId?.Length ?? 0) * 2L + (row.Path.ObservedIdentity?.Length ?? 0) * 2L;
                if (cost > AnalysisLimits.BatchBytes) throw Invalid(); if (cost > AnalysisLimits.BatchBytes - bytes) break;
                rows.Add(row); bytes += cost;
            }
            return new AnalysisDatasetPage<AnalysisLogicalHierarchyRow>(offset, range.Count, rows.ToImmutable());
        }, cancellation);
    public ValueTask DisposeAsync() => _owned.DisposeAsync();

    private sealed class Amount
    {
        internal UInt128 Bytes; internal ulong Files; internal readonly ulong[] Missing = new ulong[6];
        internal void Add(Amount other)
        { Bytes = checked(Bytes + other.Bytes); Files = checked(Files + other.Files); for (int n = 0; n < Missing.Length; n++) Missing[n] = checked(Missing[n] + other.Missing[n]); }
        internal AnalysisLogicalTotals Export() => new(Bytes, Files, Enumerable.Range(1, 5).Where(n => Missing[n] != 0).ToImmutableDictionary(n => (MeasurementState)n, n => Missing[n]));
    }
    private sealed class Frame(ulong id, ulong parent, AnalysisIndexRange children, bool incomplete)
    {
        internal readonly ulong Id = id, Parent = parent; internal readonly AnalysisIndexRange Children = children; internal ulong Next;
        internal readonly bool Incomplete = incomplete; internal readonly Amount Amount = new();
    }
    private sealed class State : IAnalysisScratchOwned
    {
        internal readonly AnalysisIndexedGraphReader Graph;
        internal AnalysisScratchWorkspace? Workspace;
        private AnalysisScratchVector? _writer;
        private AnalysisScratchVectorCheckpoint? _rows;
        private readonly Stack<Frame> _pending = [];
        private int _root;
        private ulong _visited;
        internal bool Finished;
        internal readonly Amount Total = new();
        internal readonly List<AnalysisSortedIndexReader> Indexes = [];
        private State(AnalysisIndexedGraphReader graph, AnalysisScratchWorkspace workspace, AnalysisScratchVector rows)
        { Graph = graph; Workspace = workspace; _writer = rows; }
        internal static State Create(AnalysisIndexedGraphReader source)
        {
            var graph = source.Retain(); AnalysisScratchWorkspace? workspace = null; AnalysisScratchArena? arena = null; AnalysisScratchVector? rows = null;
            try
            {
                workspace = graph.BorrowWorkspace(); arena = workspace.CreateArena(); rows = new(arena); arena = null;
                return new(graph, workspace, rows);
            }
            catch { try { rows?.Dispose(); arena?.Dispose(); } finally { try { workspace?.Dispose(); } finally { graph.Dispose(); } } throw; }
        }
        internal long Step(CancellationToken token)
        {
            if (_pending.Count == 0)
            {
                if (_root == Graph.Header.Roots.Length) { Finished = true; return 1; }
                int root = _root++; var range = Graph.Children(root, 0, token);
                if (range.Count > 1) throw Invalid();
                return range.Count == 0 ? 1 : Push(Graph.ReadIndex(AnalysisIndexedGraphRevision.Children, range.First, token).Value, token);
            }
            var parent = _pending.Peek();
            if (parent.Next < parent.Children.Count)
            { ulong child = Graph.ReadIndex(AnalysisIndexedGraphRevision.Children, parent.Children.First + parent.Next++, token).Value; return Push(child, token); }
            _pending.Pop(); _writer!.Append(Encode(parent), token); _visited++;
            if (_pending.Count == 0) Total.Add(parent.Amount); else _pending.Peek().Amount.Add(parent.Amount);
            return 128;
        }
        private long Push(ulong row, CancellationToken token)
        {
            var path = Graph.ReadPathRowRecord(row, token); var node = path.Node; ulong id = node.Id;
            if (_pending.Count > AnalysisLimits.Depth) throw Invalid();
            var children = node.Kind == AnalysisKind.Directory ? Graph.Children(path.RootIndex, id, token) : default;
            var frame = new Frame(id, node.ParentId, children, node.IncludedByPolicy && node.Kind == AnalysisKind.Directory && Graph.Header.Coverage != NamespaceCoverage.CompleteUnderPolicy);
            long bytes = 512L + node.Name.Length * 2L + node.RawName.Length;
            if (node.Kind == AnalysisKind.File && node.IncludedByPolicy)
            {
                frame.Amount.Files = 1;
                var objects = Graph.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(node.ObjectId!), cancellation: token);
                if (objects.Count != 1) throw Invalid();
                var obj = Graph.ReadObject(Graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, objects.First, token).Value, token);
                if (obj.LogicalLength.IsKnown) frame.Amount.Bytes = obj.LogicalLength.Value!.Value;
                else frame.Amount.Missing[(int)obj.LogicalLength.State] = 1;
                bytes += 512L + obj.Id.Length * 2L + (obj.VerifiedIdentity?.Length ?? 0) * 2L + (obj.VersionEvidence?.Length ?? 0) * 2L;
            }
            _pending.Push(frame); return bytes;
        }
        internal void SealRows(CancellationToken token)
        {
            if (!Finished || _visited != Graph.Count(AnalysisIndexedGraph.Paths)) throw Invalid();
            _writer!.Flush(token); _rows = _writer.CaptureCommitted(); _writer.Dispose(); _writer = null;
        }
        internal IEnumerable<AnalysisSortInput> Entries(int kind, CancellationToken token)
        {
            for (ulong row = 1; row <= _rows!.Count; row++)
            {
                token.ThrowIfCancellationRequested(); var value = Decode(_rows.Read(row, token)); byte[] key;
                if (kind == 0) key = Key(value.Id);
                else if (kind == 1)
                {
                    key = new byte[33]; Key(value.Parent).CopyTo(key, 0); key[8] = value.Amount.Missing.Skip(1).Any(n => n != 0) && value.Amount.Bytes == 0 ? (byte)1 : (byte)0;
                    Write128(key.AsSpan(9), UInt128.MaxValue - value.Amount.Bytes); Key(value.Id).CopyTo(key, 25);
                }
                else { key = new byte[24]; Key(value.Parent).CopyTo(key, 0); BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(8), ulong.MaxValue - value.Amount.Files); Key(value.Id).CopyTo(key, 16); }
                yield return new(new(key, row), 128);
            }
        }
        internal AnalysisLogicalHierarchyRow Read(ulong row, CancellationToken token)
        {
            var value = Decode(_rows!.Read(row, token)); var path = Graph.ReadPath(value.Id, token);
            if (path.ParentId != value.Parent) throw Invalid();
            return new(AnalysisPathMetadata.From(path), value.Amount.Export(), value.Incomplete);
        }
        public void Dispose()
        {
            try { foreach (var index in Indexes) index.Dispose(); _writer?.Dispose(); _rows?.Dispose(); }
            finally { try { Workspace?.Dispose(); } finally { Graph.Dispose(); } }
        }
    }
    private static byte[] Key(ulong value) { var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); return bytes; }
    private static byte[] Encode(Frame frame)
    {
        var bytes = new byte[94]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x31484c41); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4), frame.Id);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12), frame.Parent); Write128(bytes.AsSpan(20), frame.Amount.Bytes);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(36), frame.Amount.Files);
        for (int n = 0; n < 6; n++) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(44 + n * 8), frame.Amount.Missing[n]);
        bytes[92] = frame.Incomplete ? (byte)1 : (byte)0; return bytes;
    }
    private static Frame Decode(byte[] bytes)
    {
        if (bytes.Length != 94 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x31484c41 || bytes[92] > 1 || bytes[93] != 0) throw Invalid();
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4)), parent = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12));
        if (id == 0 || parent == id) throw Invalid();
        var frame = new Frame(id, parent, default, bytes[92] != 0); frame.Amount.Bytes = Read128(bytes.AsSpan(20)); frame.Amount.Files = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(36));
        if (frame.Amount.Files > AnalysisLimits.Observations) throw Invalid(); ulong missing = 0;
        for (int n = 0; n < 6; n++) { frame.Amount.Missing[n] = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(44 + n * 8)); missing = checked(missing + frame.Amount.Missing[n]); }
        if (frame.Amount.Missing[0] != 0 || missing > frame.Amount.Files) throw Invalid(); return frame;
    }
    private static void Write128(Span<byte> bytes, UInt128 value)
    { BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)(value >> 64)); BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], (ulong)value); }
    private static UInt128 Read128(ReadOnlySpan<byte> bytes) => ((UInt128)BinaryPrimitives.ReadUInt64BigEndian(bytes) << 64) | BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
    private static InvalidDataException Invalid() => new("Invalid logical hierarchy structure.");
}
