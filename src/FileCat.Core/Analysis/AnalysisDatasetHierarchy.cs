using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public enum AnalysisHierarchySort { Metric, Files }
public enum AnalysisHierarchyBucketKind { SharedFiles, DirectoryAllocation }
public sealed record AnalysisHierarchyRow(AnalysisPathMetadata Path, AnalysisTotals Totals, bool NamespaceIncomplete, bool HasChargesElsewhere);
public sealed record AnalysisHierarchyBucket(ulong ParentId, AnalysisHierarchyBucketKind Kind, AnalysisTotals Totals, ulong Objects, ulong ObservedAliases, ulong Charges);

public sealed partial class AnalysisDatasetLease
{
    /// <summary>Exact unfiltered hierarchy for any metric, with bounded rows and explicit non-file buckets.</summary>
    public Task<AnalysisDatasetHierarchy> BuildHierarchyAsync(AnalysisMetric metric, CancellationToken cancellation = default)
    {
        if (!Enum.IsDefined(metric)) throw new ArgumentOutOfRangeException(nameof(metric));
        return _owned.UseAsync(async source =>
        {
            if (metric is AnalysisMetric.FileSize or AnalysisMetric.Files)
                return await AnalysisDatasetHierarchy.BuildAsync(source, Descriptor, metric, null, true, _io, _device, cancellation).ConfigureAwait(false);
            await using var charges = await AnalysisObjectChargeIndex.BuildAsync(source, Descriptor, metric, _io, _device, cancellation).ConfigureAwait(false);
            return await charges.BuildHierarchyAsync(cancellation).ConfigureAwait(false);
        });
    }
}

/// <summary>One immutable captured revision. Real rows and synthetic accounting buckets never grant source authority.</summary>
public sealed class AnalysisDatasetHierarchy : IAsyncDisposable
{
    private static readonly SemaphoreSlim Builds = new(AnalysisLimits.ActiveScans, AnalysisLimits.ActiveScans);
    private readonly AnalysisOwnedDatasetIo<State> _owned;
    public AnalysisDatasetDescriptor Descriptor { get; }
    public AnalysisMetric Metric { get; }
    // When bases differ, combined chart area is disabled. Individual real-root rows retain their own amounts.
    public AnalysisTotals Totals { get; }
    private AnalysisDatasetHierarchy(State state, AnalysisDatasetDescriptor descriptor, DeviceIoScheduler io, string device)
    { Descriptor = descriptor; Metric = state.Metric; Totals = state.Total.Export(state.Compatible); _owned = new(state, io, device); }

    internal static async Task<AnalysisDatasetHierarchy> BuildAsync(AnalysisIndexedGraphReader source, AnalysisDatasetDescriptor descriptor,
        AnalysisMetric metric, IEnumerable<AnalysisSortInput>? charges, bool compatible, DeviceIoScheduler io, string device, CancellationToken cancellation)
    {
        await Builds.WaitAsync(cancellation).ConfigureAwait(false); State? state = null;
        try
        {
            state = await AnalysisIo.Run(io, device, _ => State.Create(source, metric, compatible), cancellation).ConfigureAwait(false);
            if (charges is not null)
            {
                using var entries = charges.GetEnumerator();
                var placements = await AnalysisExternalSort.BuildSparseAsync(state.Workspace!, io, device, entries, cancellation).ConfigureAwait(false);
                try { await AnalysisIo.Run(io, device, _ => { state.Placements = placements.Acquire(); return true; }, cancellation).ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(io, device, placements).ConfigureAwait(false); }
            }
            while (!state.Finished)
                await AnalysisIo.Run(io, device, token =>
                {
                    int records = 0; long bytes = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                    while (!state.Finished && records++ < AnalysisLimits.BatchRecords && bytes < AnalysisLimits.BatchBytes)
                    { token.ThrowIfCancellationRequested(); bytes += state.Step(token); if (clock.ElapsedMilliseconds >= 20) break; }
                    return true;
                }, cancellation).ConfigureAwait(false);
            await AnalysisIo.Run(io, device, token => { state.Seal(token); return true; }, cancellation).ConfigureAwait(false);
            for (int kind = 0; kind < 4; kind++)
            {
                using var entries = state.Entries(kind, cancellation).GetEnumerator();
                var index = await AnalysisExternalSort.BuildSparseAsync(state.Workspace!, io, device, entries, cancellation).ConfigureAwait(false);
                try { await AnalysisIo.Run(io, device, _ => { state.Indexes.Add(index.Acquire()); return true; }, cancellation).ConfigureAwait(false); }
                finally { await AnalysisIo.ReleaseScratchAsync(io, device, index).ConfigureAwait(false); }
            }
            await AnalysisIo.Run(io, device, _ => { state.Workspace!.Dispose(); state.Workspace = null; return true; }, cancellation).ConfigureAwait(false);
            var result = new AnalysisDatasetHierarchy(state, descriptor, io, device); state = null; return result;
        }
        finally
        {
            try { if (state is not null) await AnalysisIo.ReleaseScratchAsync(io, device, state).ConfigureAwait(false); }
            finally { Builds.Release(); }
        }
    }

    public Task<AnalysisHierarchyRow> ReadAsync(ulong path, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) =>
        {
            if (path == 0 || path > Descriptor.Paths) throw new ArgumentOutOfRangeException(nameof(path));
            var range = state.Indexes[0].FindRange(Key(path), token); if (range.Count != 1) throw Invalid();
            return state.Read(state.Indexes[0].Read(range.First, token).Value, token);
        }, cancellation);
    public Task<AnalysisDatasetPage<AnalysisHierarchyRow>> ReadChildrenAsync(ulong parent, AnalysisHierarchySort sort = AnalysisHierarchySort.Metric,
        ulong offset = 0, int maximum = 256, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) =>
        {
            if (parent > Descriptor.Paths || !Enum.IsDefined(sort) || maximum is < 1 or > AnalysisLimits.BatchRecords) throw new ArgumentOutOfRangeException(nameof(parent));
            var index = state.Indexes[(int)sort + 1]; var range = index.PrefixRange(Key(parent), token);
            if (offset > range.Count) throw new ArgumentOutOfRangeException(nameof(offset));
            var rows = ImmutableArray.CreateBuilder<AnalysisHierarchyRow>(); long bytes = 0;
            while (offset + (ulong)rows.Count < range.Count && rows.Count < maximum)
            {
                token.ThrowIfCancellationRequested(); var row = state.Read(index.Read(range.First + offset + (ulong)rows.Count, token).Value, token);
                long cost = 768L + row.Path.Name.Length * 2L + row.Path.RawName.Length + row.Path.RootId.Length * 2L +
                    (row.Path.ObjectId?.Length ?? 0) * 2L + (row.Path.ObservedIdentity?.Length ?? 0) * 2L;
                if (cost > AnalysisLimits.BatchBytes) throw Invalid(); if (cost > AnalysisLimits.BatchBytes - bytes) break;
                rows.Add(row); bytes += cost;
            }
            return new AnalysisDatasetPage<AnalysisHierarchyRow>(offset, range.Count, rows.ToImmutable());
        }, cancellation);
    public Task<ImmutableArray<AnalysisHierarchyBucket>> ReadBucketsAsync(ulong parent, CancellationToken cancellation = default) =>
        _owned.RunAsync((state, token) =>
        {
            if (parent > Descriptor.Paths) throw new ArgumentOutOfRangeException(nameof(parent));
            if (parent == 0 && !Totals.CompatibleBasis) return ImmutableArray<AnalysisHierarchyBucket>.Empty;
            var range = state.Indexes[3].PrefixRange(Key(parent), token); if (range.Count > 2) throw Invalid();
            var rows = ImmutableArray.CreateBuilder<AnalysisHierarchyBucket>();
            for (ulong at = 0; at < range.Count; at++) rows.Add(state.ReadBucket(state.Indexes[3].Read(range.First + at, token).Value, token));
            return rows.ToImmutable();
        }, cancellation);
    public UInt128 ChartArea(AnalysisTotals totals) => !totals.CompatibleBasis ? 0 :
        Metric == AnalysisMetric.OnDisk ? checked(totals.Known + totals.IdentityUnverified) : totals.Known;
    public ValueTask DisposeAsync() => _owned.DisposeAsync();

    private sealed class Amount
    {
        internal UInt128 Known, Unverified;
        internal ulong Files;
        internal readonly ulong[] Missing = new ulong[6];
        internal void Add(Amount value)
        {
            Known = checked(Known + value.Known); Unverified = checked(Unverified + value.Unverified); _ = checked(Known + Unverified);
            Files = checked(Files + value.Files); for (int n = 1; n < 6; n++) Missing[n] = checked(Missing[n] + value.Missing[n]);
        }
        internal void Measure(AnalysisMeasurement value, bool unverified)
        {
            if (!value.IsKnown) Missing[(int)value.State] = checked(Missing[(int)value.State] + 1);
            else if (unverified) Unverified = checked(Unverified + value.Value!.Value);
            else Known = checked(Known + value.Value!.Value);
            _ = checked(Known + Unverified);
        }
        internal AnalysisTotals Export(bool compatible = true) => new(compatible ? Known : 0, compatible ? Unverified : 0, Files,
            Enumerable.Range(1, 5).Where(n => Missing[n] != 0).ToImmutableDictionary(n => (MeasurementState)n, n => Missing[n]), compatible);
    }
    private sealed class Bucket(AnalysisHierarchyBucketKind kind)
    {
        internal readonly AnalysisHierarchyBucketKind Kind = kind;
        internal readonly Amount Amount = new();
        internal ulong Objects, Aliases, Charges, LastPath;
        internal void Add(ulong path, ulong aliases, AnalysisMeasurement measurement, bool unverified)
        {
            Amount.Measure(measurement, unverified); Charges = checked(Charges + 1);
            if (path != LastPath) { Objects = checked(Objects + 1); Aliases = checked(Aliases + aliases); LastPath = path; }
        }
    }
    private sealed class Frame(ulong id, ulong parent, AnalysisIndexRange children, AnalysisIndexRange charges, bool incomplete, bool file)
    {
        internal readonly ulong Id = id, Parent = parent;
        internal readonly AnalysisIndexRange Children = children, Charges = charges;
        internal readonly bool Incomplete = incomplete, File = file;
        internal readonly Amount Amount = new();
        internal ulong NextChild, NextCharge;
        internal bool OwnCharge, Elsewhere;
        internal Bucket? Shared, Directory;
    }
    private sealed class State : IAnalysisScratchOwned
    {
        private readonly AnalysisIndexedGraphReader _graph;
        internal AnalysisScratchWorkspace? Workspace;
        internal AnalysisSortedIndexReader? Placements;
        internal readonly List<AnalysisSortedIndexReader> Indexes = [];
        internal readonly AnalysisMetric Metric;
        internal readonly bool Compatible;
        private AnalysisScratchVector? _writer, _bucketWriter;
        private AnalysisScratchVectorCheckpoint? _rows, _buckets;
        private readonly Stack<Frame> _pending = [];
        private int _root;
        private ulong _visited, _placed;
        private bool _started;
        internal bool Finished;
        internal Amount Total = new();
        private State(AnalysisIndexedGraphReader graph, AnalysisScratchWorkspace workspace, AnalysisScratchVector writer, AnalysisScratchVector buckets, AnalysisMetric metric, bool compatible)
        { _graph = graph; Workspace = workspace; _writer = writer; _bucketWriter = buckets; Metric = metric; Compatible = compatible; }
        internal static State Create(AnalysisIndexedGraphReader source, AnalysisMetric metric, bool compatible)
        {
            var graph = source.Retain(); AnalysisScratchWorkspace? workspace = null; AnalysisScratchArena? arena = null; AnalysisScratchVector? writer = null, buckets = null;
            try
            {
                workspace = graph.BorrowWorkspace(); arena = workspace.CreateArena(); writer = new(arena); arena = null;
                arena = workspace.CreateArena(); buckets = new(arena); arena = null;
                return new(graph, workspace, writer, buckets, metric, compatible);
            }
            catch { try { buckets?.Dispose(); writer?.Dispose(); arena?.Dispose(); } finally { try { workspace?.Dispose(); } finally { graph.Dispose(); } } throw; }
        }
        private AnalysisIndexRange Charges(ulong id, CancellationToken token) => Placements?.PrefixRange(Key(id), token) ?? default;
        internal long Step(CancellationToken token)
        {
            if (!_started) { _started = true; _pending.Push(new(0, 0, default, Charges(0, token), false, false)); return 1; }
            var frame = _pending.Peek();
            if (frame.NextCharge < frame.Charges.Count)
            {
                var entry = Placements!.Read(frame.Charges.First + frame.NextCharge++, token); var key = entry.Key;
                if (key.Length != 50 || BinaryPrimitives.ReadUInt64BigEndian(key) != frame.Id || BinaryPrimitives.ReadUInt64BigEndian(key.AsSpan(8)) != entry.Value || key[48] > 5 || key[49] > 7) throw Invalid();
                ulong path = BinaryPrimitives.ReadUInt64BigEndian(key.AsSpan(16)), aliases = BinaryPrimitives.ReadUInt64BigEndian(key.AsSpan(24));
                UInt128 value = Read128(key.AsSpan(32)); var status = (MeasurementState)key[48];
                if (path is < 1 or > AnalysisLimits.Observations || aliases > AnalysisLimits.Observations || status != MeasurementState.Known && value != 0) throw Invalid();
                var measurement = status == MeasurementState.Known ? AnalysisMeasurement.Known(value) : AnalysisMeasurement.Missing(status); bool unverified = (key[49] & 2) != 0;
                frame.Amount.Measure(measurement, unverified); frame.OwnCharge = true; _placed++;
                if ((key[49] & 1) != 0) (frame.Shared ??= new(AnalysisHierarchyBucketKind.SharedFiles)).Add(path, aliases, measurement, unverified);
                if ((key[49] & 4) != 0) (frame.Directory ??= new(AnalysisHierarchyBucketKind.DirectoryAllocation)).Add(path, 0, measurement, unverified);
                return 128;
            }
            if (frame.Id == 0 && _root < _graph.Header.Roots.Length)
            {
                var range = _graph.Children(_root++, 0, token); if (range.Count > 1) throw Invalid();
                return range.Count == 0 ? 1 : Push(_graph.ReadIndex(AnalysisIndexedGraphRevision.Children, range.First, token).Value, token);
            }
            if (frame.NextChild < frame.Children.Count)
                return Push(_graph.ReadIndex(AnalysisIndexedGraphRevision.Children, frame.Children.First + frame.NextChild++, token).Value, token);
            _pending.Pop();
            if (frame.File && Metric is (AnalysisMetric.UniqueFileSize or AnalysisMetric.OnDisk) && !frame.OwnCharge) frame.Elsewhere = true;
            if (frame.Shared is { } shared) _bucketWriter!.Append(EncodeBucket(frame.Id, shared), token);
            if (frame.Directory is { } directory) _bucketWriter!.Append(EncodeBucket(frame.Id, directory), token);
            if (frame.Id == 0) { Total = frame.Amount; Finished = true; }
            else
            {
                _writer!.Append(Encode(frame), token); _visited++;
                var parent = _pending.Peek(); parent.Amount.Add(frame.Amount); parent.Elsewhere |= frame.Elsewhere;
            }
            return 512;
        }
        private long Push(ulong id, CancellationToken token)
        {
            var path = _graph.ReadPathRecord(id, token); var node = path.Node;
            if (_pending.Count > AnalysisLimits.Depth + 1) throw Invalid();
            bool file = node.Kind == AnalysisKind.File && node.IncludedByPolicy;
            var frame = new Frame(id, node.ParentId, node.Kind == AnalysisKind.Directory ? _graph.Children(path.RootIndex, id, token) : default, Charges(id, token),
                node.Kind == AnalysisKind.Directory && node.IncludedByPolicy && _graph.Header.Coverage != NamespaceCoverage.CompleteUnderPolicy, file);
            long cost = 1024L + node.Name.Length * 2L + node.RawName.Length;
            if (file)
            {
                frame.Amount.Files = 1;
                if (Metric == AnalysisMetric.Files) frame.Amount.Known = 1;
                else if (Metric == AnalysisMetric.FileSize)
                {
                    var range = _graph.Find(AnalysisIndexedGraphRevision.ObjectIds, AnalysisGraphKeys.Text(node.ObjectId!), cancellation: token);
                    if (range.Count != 1) throw Invalid();
                    var obj = _graph.ReadObject(_graph.ReadIndex(AnalysisIndexedGraphRevision.ObjectIds, range.First, token).Value, token);
                    frame.Amount.Measure(obj.LogicalLength, false);
                    cost += 1024L + obj.Id.Length * 2L + (obj.VerifiedIdentity?.Length ?? 0) * 2L + (obj.VersionEvidence?.Length ?? 0) * 2L;
                }
            }
            _pending.Push(frame); return cost;
        }
        internal void Seal(CancellationToken token)
        {
            if (!Finished || _visited != _graph.Count(AnalysisIndexedGraph.Paths) || _placed != (Placements?.Count ?? 0)) throw Invalid();
            _writer!.Flush(token); _rows = _writer.CaptureCommitted(); _writer.Dispose(); _writer = null;
            _bucketWriter!.Flush(token); _buckets = _bucketWriter.CaptureCommitted(); _bucketWriter.Dispose(); _bucketWriter = null;
            Placements?.Dispose(); Placements = null;
        }
        internal IEnumerable<AnalysisSortInput> Entries(int kind, CancellationToken token)
        {
            ulong count = kind == 3 ? _buckets!.Count : _rows!.Count;
            for (ulong row = 1; row <= count; row++)
            {
                token.ThrowIfCancellationRequested(); byte[] key;
                if (kind == 3) { var bucket = DecodeBucket(_buckets!.Read(row, token)); key = new byte[9]; Key(bucket.ParentId).CopyTo(key, 0); key[8] = (byte)bucket.Kind; }
                else
                {
                    var value = Decode(_rows!.Read(row, token));
                    if (kind == 0) key = Key(value.Id);
                    else if (kind == 1)
                    {
                        UInt128 area = Metric == AnalysisMetric.OnDisk ? checked(value.Amount.Known + value.Amount.Unverified) : value.Amount.Known;
                        key = new byte[33]; Key(value.Parent).CopyTo(key, 0);
                        key[8] = area == 0 && (value.Elsewhere || value.Amount.Missing.Skip(1).Any(n => n != 0) || Metric == AnalysisMetric.UniqueFileSize && value.Amount.Unverified != 0) ? (byte)1 : (byte)0;
                        Write128(key.AsSpan(9), UInt128.MaxValue - area); Key(value.Id).CopyTo(key, 25);
                    }
                    else { key = new byte[24]; Key(value.Parent).CopyTo(key, 0); BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(8), ulong.MaxValue - value.Amount.Files); Key(value.Id).CopyTo(key, 16); }
                }
                yield return new(new(key, row), 256);
            }
        }
        internal AnalysisHierarchyRow Read(ulong row, CancellationToken token)
        {
            var value = Decode(_rows!.Read(row, token)); var path = _graph.ReadPath(value.Id, token); if (path.ParentId != value.Parent) throw Invalid();
            return new(AnalysisPathMetadata.From(path), value.Amount.Export(), value.Incomplete, value.Elsewhere);
        }
        internal AnalysisHierarchyBucket ReadBucket(ulong row, CancellationToken token) => DecodeBucket(_buckets!.Read(row, token));
        public void Dispose()
        {
            Exception? error = null;
            foreach (var index in Indexes) Close(index, ref error);
            Close(Placements, ref error); Close(_writer, ref error); Close(_bucketWriter, ref error); Close(_rows, ref error); Close(_buckets, ref error); Close(Workspace, ref error); Close(_graph, ref error);
            if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        }
        private static void Close(IDisposable? resource, ref Exception? error)
        { try { resource?.Dispose(); } catch (Exception ex) { error ??= ex; } }
    }
    private static byte[] Encode(Frame frame)
    {
        var bytes = new byte[112]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x31484441); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4), frame.Id);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(12), frame.Parent); Write128(bytes.AsSpan(20), frame.Amount.Known); Write128(bytes.AsSpan(36), frame.Amount.Unverified);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(52), frame.Amount.Files);
        for (int n = 0; n < 6; n++) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(60 + n * 8), frame.Amount.Missing[n]);
        bytes[108] = (byte)((frame.Incomplete ? 1 : 0) | (frame.Elsewhere ? 2 : 0)); return bytes;
    }
    private static Frame Decode(byte[] bytes)
    {
        if (bytes.Length != 112 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x31484441 || bytes[108] > 3 || bytes[109] != 0 || bytes[110] != 0 || bytes[111] != 0) throw Invalid();
        ulong id = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4)), parent = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12));
        if (id is < 1 or > AnalysisLimits.Observations || parent >= id) throw Invalid();
        var frame = new Frame(id, parent, default, default, (bytes[108] & 1) != 0, false) { Elsewhere = (bytes[108] & 2) != 0 };
        frame.Amount.Known = Read128(bytes.AsSpan(20)); frame.Amount.Unverified = Read128(bytes.AsSpan(36)); _ = checked(frame.Amount.Known + frame.Amount.Unverified);
        frame.Amount.Files = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(52)); if (frame.Amount.Files > AnalysisLimits.Observations) throw Invalid();
        ReadMissing(bytes.AsSpan(60), frame.Amount); return frame;
    }
    private static byte[] EncodeBucket(ulong parent, Bucket bucket)
    {
        var bytes = new byte[120]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x31424441); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4), parent); bytes[12] = (byte)bucket.Kind;
        Write128(bytes.AsSpan(16), bucket.Amount.Known); Write128(bytes.AsSpan(32), bucket.Amount.Unverified);
        for (int n = 0; n < 6; n++) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(48 + n * 8), bucket.Amount.Missing[n]);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(96), bucket.Objects); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(104), bucket.Aliases); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(112), bucket.Charges); return bytes;
    }
    private static AnalysisHierarchyBucket DecodeBucket(byte[] bytes)
    {
        if (bytes.Length != 120 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != 0x31424441 || bytes[12] > 1 || bytes[13] != 0 || bytes[14] != 0 || bytes[15] != 0) throw Invalid();
        ulong parent = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4)), objects = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(96));
        ulong aliases = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(104)), charges = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(112));
        var kind = (AnalysisHierarchyBucketKind)bytes[12];
        if (parent > AnalysisLimits.Observations || objects is < 1 or > AnalysisLimits.Observations || aliases > AnalysisLimits.Observations || charges < objects || charges > AnalysisLimits.Observations ||
            (kind == AnalysisHierarchyBucketKind.DirectoryAllocation ? parent == 0 || objects != 1 || aliases != 0 : aliases < objects * 2)) throw Invalid();
        var amount = new Amount { Known = Read128(bytes.AsSpan(16)), Unverified = Read128(bytes.AsSpan(32)) }; _ = checked(amount.Known + amount.Unverified);
        ReadMissing(bytes.AsSpan(48), amount); return new(parent, kind, amount.Export(), objects, aliases, charges);
    }
    private static void ReadMissing(ReadOnlySpan<byte> bytes, Amount amount)
    {
        ulong count = 0; for (int n = 0; n < 6; n++) { amount.Missing[n] = BinaryPrimitives.ReadUInt64LittleEndian(bytes[(n * 8)..]); count = checked(count + amount.Missing[n]); }
        if (amount.Missing[0] != 0 || count > AnalysisLimits.Observations) throw Invalid();
    }
    private static byte[] Key(ulong value) { var bytes = new byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); return bytes; }
    private static void Write128(Span<byte> bytes, UInt128 value)
    { BinaryPrimitives.WriteUInt64BigEndian(bytes, (ulong)(value >> 64)); BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], (ulong)value); }
    private static UInt128 Read128(ReadOnlySpan<byte> bytes) => ((UInt128)BinaryPrimitives.ReadUInt64BigEndian(bytes) << 64) | BinaryPrimitives.ReadUInt64BigEndian(bytes[8..]);
    private static InvalidDataException Invalid() => new("Invalid dataset hierarchy structure.");
}
