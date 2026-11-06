using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>Bounded, inert graph header. It does not carry backend ancestry, native sources or operation approval.</summary>
internal sealed record AnalysisIndexedGraphHeader(SessionIdentity Identity, ImmutableArray<RootBinding> Roots, ScanPolicy Policy,
    AnalysisPhase Phase, NamespaceCoverage Coverage, AnalysisFreshness Freshness = AnalysisFreshness.Observed,
    AnalysisOrigin Origin = AnalysisOrigin.LiveSession, AnalysisTaint Taint = AnalysisTaint.OrdinarySensitive)
{
    internal void Validate() => AnalysisValidation.Validate(new(Identity, Roots, [], [], [], [], Policy, Phase, Coverage, Freshness, Origin, Taint));
}

/// <summary>
/// Dense typed observation tables and exact secondary indexes, without a managed object per row.
/// Appends and native lifetimes run on admitted workers. Seal freezes the input; failed index creation
/// can be retried against that same prefix, but never exposes a half-indexed graph as a successful result.
/// This is not yet the production scanner/store or projection integration.
/// </summary>
internal sealed class AnalysisIndexedGraph : IAnalysisScratchOwned
{
    internal const int Paths = 0, Objects = 1, Facts = 2, Issues = 3;
    private readonly object _gate = new();
    private readonly AnalysisScratchComposite _tables;
    private readonly AnalysisIndexedGraphHeader _header;
    private readonly Dictionary<string, int> _roots = new(StringComparer.Ordinal);
    private readonly ulong[] _rootNodes;
    private readonly ulong[] _counts = new ulong[4];
    private DateTimeOffset _observed;
    private AnalysisIndexedGraphHeader? _sealedHeader;
    private bool _sealed, _disposed;

    // The caller retains arena ownership on constructor failure.
    internal AnalysisIndexedGraph(AnalysisScratchArena arena, AnalysisIndexedGraphHeader header)
    {
        ArgumentNullException.ThrowIfNull(arena); ArgumentNullException.ThrowIfNull(header); header.Validate();
        _header = header with { Taint = header.Origin == AnalysisOrigin.ImportedInventory ? AnalysisPrivacy.Imported(header.Taint) : AnalysisPrivacy.Join(header.Taint) };
        for (int i = 0; i < header.Roots.Length; i++) _roots.Add(header.Roots[i].Id, i);
        _rootNodes = new ulong[header.Roots.Length]; _observed = header.Identity.StartedUtc;
        _tables = new(arena, AnalysisLimits.Observations, AnalysisLimits.Observations, AnalysisLimits.Observations, AnalysisLimits.Issues);
    }

    internal void AppendPath(AnalysisNode node, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            GuardWriter(); cancellation.ThrowIfCancellationRequested();
            if (node is null || node.Id != _counts[Paths] + 1 || !_roots.TryGetValue(node.RootId, out int root)) throw Invalid();
            int depth = 0;
            if (node.ParentId == 0) { if (_rootNodes[root] != 0) throw Invalid(); }
            else
            {
                if (node.ParentId >= node.Id) throw Invalid();
                var parent = AnalysisIndexedPathCodec.Decode(_tables.ReadCurrent(Paths, node.ParentId, cancellation), node.ParentId, _header.Roots);
                if (parent.RootIndex != root || parent.Node.Kind != AnalysisKind.Directory || !parent.Node.IncludedByPolicy || parent.Node.MetadataState != MeasurementState.Known) throw Invalid();
                depth = checked(parent.Depth + 1);
            }
            if (node.Times is { } times && times.ObservedUtc < _header.Identity.StartedUtc) throw Invalid();
            _tables.Append(Paths, AnalysisIndexedPathCodec.Encode(node, root, depth, _header.Roots), cancellation); _counts[Paths]++;
            if (node.ParentId == 0) _rootNodes[root] = node.Id;
            if (node.Times is { } observed && observed.ObservedUtc > _observed) _observed = observed.ObservedUtc;
        }
    }
    internal ulong AppendObject(AnalysisObject value, CancellationToken cancellation = default)
    { lock (_gate) { GuardWriter(); ulong id = _tables.Append(Objects, AnalysisIndexedValueCodec.EncodeObject(value), cancellation); _counts[Objects]++; return id; } }
    internal ulong AppendFact(StorageFact value, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            GuardWriter();
            if (value is null || value.ObservedUtc < _header.Identity.StartedUtc) throw Invalid();
            ulong id = _tables.Append(Facts, AnalysisIndexedValueCodec.EncodeFact(value), cancellation); _counts[Facts]++;
            if (value.ObservedUtc > _observed) _observed = value.ObservedUtc; return id;
        }
    }
    internal ulong AppendIssue(AnalysisIssue value, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            GuardWriter();
            if (value is null || value.RootId is { } root && !_roots.ContainsKey(root)) throw Invalid();
            ulong id = _tables.Append(Issues, AnalysisIndexedValueCodec.EncodeIssue(value), cancellation); _counts[Issues]++; return id;
        }
    }

    internal async Task<AnalysisIndexedGraphRevision> SealAsync(AnalysisScratchWorkspace workspace, DeviceIoScheduler io, string device,
        SessionIdentity identity, AnalysisPhase phase, NamespaceCoverage coverage, CancellationToken cancellation = default)
    {
        var data = await AnalysisIo.Run(io, device, token =>
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                var start = _header.Identity;
                if (identity is null || identity.SessionId != start.SessionId || identity.Epoch != start.Epoch || identity.StartedUtc != start.StartedUtc ||
                    identity.HostProfile != start.HostProfile || identity.TimeZoneId != start.TimeZoneId || identity.Revision < start.Revision || identity.ObservedUtc < _observed)
                    throw new InvalidOperationException("An indexed graph cannot change acquisition identity or predate its observations.");
                var final = _header with { Identity = identity, Phase = phase, Coverage = coverage }; final.Validate();
                if (_sealedHeader is { } prior && prior != final)
                    throw new InvalidOperationException("A frozen indexed graph cannot be reinterpreted with another header.");
                _sealedHeader = final;
                _sealed = true;
                if (!_tables.Failed) _tables.Publish(token);
                else throw new IOException("The typed graph tables could not publish together. The prior raw publication remains owned.");
                return (Header: final, Data: _tables.AcquireCommitted());
            }
        }, cancellation).ConfigureAwait(false);
        return await AnalysisIndexedGraphRevision.BuildAsync(data.Header, data.Data, workspace, io, device, cancellation).ConfigureAwait(false);
    }
    private void GuardWriter()
    { ObjectDisposedException.ThrowIf(_disposed, this); if (_sealed) throw new InvalidOperationException("The indexed graph input is frozen."); }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _tables.Dispose(); } }
    internal static InvalidDataException Invalid() => new("Invalid indexed analysis graph relationship.");
}

/// <summary>Owns one validated immutable prefix and its exact indexes; it has no live filesystem capability.</summary>
internal sealed class AnalysisIndexedGraphRevision : IAnalysisScratchOwned
{
    internal const int Children = 0, ObjectIds = 1, Aliases = 2, FactOwners = 3, FactIds = 4, IssueOwners = 5, IndexCount = 6;
    private readonly object _gate = new();
    private readonly AnalysisScratchCompositeRevision _data;
    private readonly AnalysisSortedIndex[] _indexes;
    private readonly AnalysisGraphConsumers _consumers = new();
    private bool _disposed;
    internal AnalysisIndexedGraphHeader Header { get; }
    private AnalysisIndexedGraphRevision(AnalysisIndexedGraphHeader header, AnalysisScratchCompositeRevision data, AnalysisSortedIndex[] indexes)
    { Header = header; _data = data; _indexes = indexes; }

    // Consumes data on success OR failure; workspace remains caller owned.
    internal static async Task<AnalysisIndexedGraphRevision> BuildAsync(AnalysisIndexedGraphHeader header, AnalysisScratchCompositeRevision data,
        AnalysisScratchWorkspace workspace, DeviceIoScheduler io, string device, CancellationToken cancellation)
    {
        var ownership = new GraphBuildOwnership(data);
        try
        {
            for (int kind = 0; kind < IndexCount; kind++)
            {
                using var source = Entries(kind, header.Roots, data, cancellation).GetEnumerator();
                ownership.Indexes.Add(await AnalysisExternalSort.BuildSparseAsync(workspace, io, device, source, cancellation).ConfigureAwait(false));
            }
            var result = new AnalysisIndexedGraphRevision(header, data, ownership.Indexes.ToArray());
            var reader = await AnalysisIo.Run(io, device, _ => result.Acquire(), cancellation).ConfigureAwait(false);
            try { await AnalysisIndexedGraphValidation.ValidateAsync(reader, io, device, cancellation).ConfigureAwait(false); }
            finally { await AnalysisIo.ReleaseScratchAsync(io, device, reader).ConfigureAwait(false); }
            ownership.Transferred = true; return result;
        }
        finally { await AnalysisIo.ReleaseScratchAsync(io, device, ownership).ConfigureAwait(false); }
    }

    private static IEnumerable<AnalysisSortInput> Entries(int kind, ImmutableArray<RootBinding> roots, AnalysisScratchCompositeRevision data, CancellationToken cancellation)
    {
        int table = kind switch { Children or Aliases => AnalysisIndexedGraph.Paths, ObjectIds => AnalysisIndexedGraph.Objects,
            FactOwners or FactIds => AnalysisIndexedGraph.Facts, _ => AnalysisIndexedGraph.Issues };
        for (ulong id = 1, count = data.Count(table); id <= count; id++)
        {
            cancellation.ThrowIfCancellationRequested(); byte[] bytes = data.Read(table, id, cancellation);
            if (table == AnalysisIndexedGraph.Paths)
            {
                var path = AnalysisIndexedPathCodec.Decode(bytes, id, roots);
                if (kind == Children) yield return new(new(AnalysisGraphKeys.Child(path), id), bytes.Length);
                else yield return new(path.Node.ObjectId is { } objectId ? new AnalysisSortEntry(AnalysisGraphKeys.Text(objectId), id) : null, bytes.Length);
            }
            else if (table == AnalysisIndexedGraph.Objects) yield return new(new(AnalysisGraphKeys.Text(AnalysisIndexedValueCodec.DecodeObject(bytes).Id), id), bytes.Length);
            else if (table == AnalysisIndexedGraph.Facts)
            {
                var fact = AnalysisIndexedValueCodec.DecodeFact(bytes);
                yield return new(new(kind == FactIds ? AnalysisGraphKeys.Text(fact.Id) : AnalysisGraphKeys.FactOwner(fact.ObjectId, fact.DirectoryId), id), bytes.Length);
            }
            else
            {
                var issue = AnalysisIndexedValueCodec.DecodeIssue(bytes);
                yield return new(new(AnalysisGraphKeys.IssueOwner(issue.NodeId, issue.RootId, roots), id), bytes.Length);
            }
        }
    }

    internal AnalysisIndexedGraphReader Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return AnalysisIndexedGraphReader.Open(Header, _consumers.Reserve(), () => _data.Borrow(), i => _indexes[i].Acquire());
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            try { foreach (var index in _indexes) index.Dispose(); }
            finally { _data.Dispose(); }
        }
    }
    private sealed class GraphBuildOwnership(AnalysisScratchCompositeRevision data) : IAnalysisScratchOwned
    {
        internal readonly List<AnalysisSortedIndex> Indexes = []; internal bool Transferred;
        public void Dispose() { if (Transferred) return; try { foreach (var index in Indexes) index.Dispose(); } finally { data.Dispose(); } }
    }
}

internal sealed class AnalysisIndexedGraphReader : IAnalysisScratchOwned
{
    private readonly object _gate = new(); private bool _disposed;
    private readonly AnalysisScratchCompositeRevision _data;
    private readonly AnalysisSortedIndexReader[] _indexes;
    private readonly AnalysisGraphConsumers.Ticket _consumer;
    internal AnalysisIndexedGraphHeader Header { get; }
    private AnalysisIndexedGraphReader(AnalysisIndexedGraphHeader header, AnalysisGraphConsumers.Ticket consumer,
        AnalysisScratchCompositeRevision data, AnalysisSortedIndexReader[] indexes)
    { Header = header; _consumer = consumer; _data = data; _indexes = indexes; }
    internal static AnalysisIndexedGraphReader Open(AnalysisIndexedGraphHeader header, AnalysisGraphConsumers.Ticket consumer,
        Func<AnalysisScratchCompositeRevision> dataSource, Func<int, AnalysisSortedIndexReader> indexSource)
    {
        AnalysisScratchCompositeRevision? data = null; var indexes = new AnalysisSortedIndexReader[AnalysisIndexedGraphRevision.IndexCount];
        try
        {
            data = dataSource(); for (int i = 0; i < indexes.Length; i++) indexes[i] = indexSource(i);
            return new(header, consumer, data, indexes);
        }
        catch { try { foreach (var index in indexes) index?.Dispose(); } finally { try { data?.Dispose(); } finally { consumer.Dispose(); } } throw; }
    }
    internal AnalysisIndexedGraphReader Retain()
    { lock (_gate) { Guard(); return Open(Header, _consumer.Retain(), () => _data.Borrow(), i => _indexes[i].Retain()); } }
    internal ulong Count(int table) { lock (_gate) { Guard(); return _data.Count(table); } }
    internal AnalysisNode ReadPath(ulong id, CancellationToken cancellation = default)
    { lock (_gate) { Guard(); return ReadPathRecord(id, cancellation).Node; } }
    internal AnalysisIndexedPathRecord ReadPathRecord(ulong id, CancellationToken cancellation)
    { lock (_gate) { Guard(); return AnalysisIndexedPathCodec.Decode(_data.Read(AnalysisIndexedGraph.Paths, id, cancellation), id, Header.Roots); } }
    internal AnalysisObject ReadObject(ulong row, CancellationToken cancellation = default)
    { lock (_gate) { Guard(); return AnalysisIndexedValueCodec.DecodeObject(_data.Read(AnalysisIndexedGraph.Objects, row, cancellation)); } }
    internal StorageFact ReadFact(ulong row, CancellationToken cancellation = default)
    { lock (_gate) { Guard(); return AnalysisIndexedValueCodec.DecodeFact(_data.Read(AnalysisIndexedGraph.Facts, row, cancellation)); } }
    internal AnalysisIssue ReadIssue(ulong row, CancellationToken cancellation = default)
    { lock (_gate) { Guard(); return AnalysisIndexedValueCodec.DecodeIssue(_data.Read(AnalysisIndexedGraph.Issues, row, cancellation)); } }
    internal ulong IndexCount(int index) { lock (_gate) { Guard(); return Index(index).Count; } }
    internal AnalysisSortEntry ReadIndex(int index, ulong row, CancellationToken cancellation = default)
    { lock (_gate) { Guard(); return Index(index).Read(row, cancellation); } }
    internal AnalysisIndexRange Find(int index, ReadOnlySpan<byte> key, bool prefix = false, CancellationToken cancellation = default)
    { lock (_gate) { Guard(); return prefix ? Index(index).PrefixRange(key, cancellation) : Index(index).FindRange(key, cancellation); } }
    // Related values are paged through ReadIndex then ReadObject/ReadFact/ReadIssue; no alias list is materialized.
    internal AnalysisIndexRange Children(int rootOrdinal, ulong parent, CancellationToken cancellation = default) =>
        Find(AnalysisIndexedGraphRevision.Children, AnalysisGraphKeys.ChildPrefix(rootOrdinal, parent), prefix: true, cancellation);
    internal AnalysisIndexRange Aliases(string objectId, CancellationToken cancellation = default) =>
        Find(AnalysisIndexedGraphRevision.Aliases, AnalysisGraphKeys.Text(objectId), cancellation: cancellation);
    private AnalysisSortedIndexReader Index(int index)
    { if ((uint)index >= (uint)_indexes.Length) throw new ArgumentOutOfRangeException(nameof(index)); return _indexes[index]; }
    private void Guard() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            try { foreach (var index in _indexes) index.Dispose(); }
            finally { try { _data.Dispose(); } finally { _consumer.Dispose(); } }
        }
    }
}

internal sealed class AnalysisGraphConsumers
{
    private readonly object _gate = new(); private int _readers;
    internal Ticket Reserve()
    {
        lock (_gate)
        {
            if (_readers >= AnalysisLimits.SnapshotConsumers) throw new IOException("The indexed graph reader limit was reached.");
            var result = new Ticket(this); _readers++; return result;
        }
    }
    internal sealed class Ticket(AnalysisGraphConsumers owner) : IDisposable
    {
        private bool _disposed;
        internal Ticket Retain() { lock (owner._gate) { ObjectDisposedException.ThrowIf(_disposed, this); return owner.Reserve(); } }
        public void Dispose() { lock (owner._gate) { if (_disposed) return; _disposed = true; owner._readers--; } }
    }
}

internal static class AnalysisGraphKeys
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    internal static byte[] Text(string text)
    {
        if (text is not { Length: > 0 and <= 512 } || text.Contains('\0')) throw AnalysisIndexedGraph.Invalid();
        try { if (Utf8.GetByteCount(text) > 512) throw AnalysisIndexedGraph.Invalid(); return Utf8.GetBytes(text); }
        catch (EncoderFallbackException) { throw AnalysisIndexedGraph.Invalid(); }
    }
    internal static byte[] ChildPrefix(int root, ulong parent)
    {
        if (root is < 0 or >= AnalysisLimits.Roots || parent > AnalysisLimits.Observations) throw new ArgumentOutOfRangeException(nameof(root));
        var key = new byte[9]; key[0] = (byte)root; BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(1), parent); return key;
    }
    internal static byte[] Child(AnalysisIndexedPathRecord path)
    {
        var node = path.Node; byte[] raw = node.RawName.IsEmpty ? AnalysisRawNames.Encode(node.NameEncoding, node.Name) : node.RawName.ToArray();
        byte[] key = new byte[10 + raw.Length]; ChildPrefix(path.RootIndex, node.ParentId).CopyTo(key, 0);
        key[9] = node.NameEncoding switch { "utf8" => 1, "utf16le" => 2, "unix-bytes" => 3, _ => throw AnalysisIndexedGraph.Invalid() };
        raw.CopyTo(key, 10); return key;
    }
    internal static byte[] FactOwner(string? objectId, ulong? directory)
    {
        if ((objectId is null) == (directory is null) || directory == 0) throw AnalysisIndexedGraph.Invalid();
        if (objectId is { } text) { byte[] id = Text(text); var result = new byte[id.Length + 1]; id.CopyTo(result, 1); return result; }
        var key = new byte[9]; key[0] = 1; BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(1), directory!.Value); return key;
    }
    internal static byte[] IssueOwner(ulong? node, string? root, ImmutableArray<RootBinding> roots)
    {
        if (node is { } id) { if (id == 0) throw AnalysisIndexedGraph.Invalid(); var key = new byte[9]; BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(1), id); return key; }
        if (root is null) return [2];
        for (int i = 0; i < roots.Length; i++) if (roots[i].Id == root) return [1, (byte)i];
        throw AnalysisIndexedGraph.Invalid();
    }
}
