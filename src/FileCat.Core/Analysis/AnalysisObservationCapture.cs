using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

/// <summary>
/// Private tape for already captured metadata. It never serializes RelativeName/Ancestry and cannot
/// reopen a source. Exact identity coalescing happens after capture, without a dictionary per file.
/// All append/publication/disposal operations belong on the captured scratch device's admitted worker.
/// </summary>
internal sealed class AnalysisObservationCapture : IAnalysisScratchOwned
{
    internal const int Paths = 0, Files = 1, Directories = 2, Issues = 3;
    private readonly object _gate = new();
    private readonly AnalysisIndexedGraphHeader _header;
    private readonly AnalysisScratchComposite _raw;
    private readonly Dictionary<string, int> _roots = new(StringComparer.Ordinal);
    private readonly ulong[] _rootNodes;
    private readonly Dictionary<MeasurementState, ulong> _issues = [];
    private readonly Dictionary<AnalysisBoundaryKind, ulong> _boundaries = [];
    private ulong _paths, _samples;
    private AnalysisEnumerationProgress? _enumeration;
    private ulong _staleEvents;
    private uint _staleRoots;
    private bool _unlocatedStale;
    private DateTimeOffset _observed;
    private AnalysisCaptureState _publication;
    private AnalysisIndexedGraphHeader? _frozen;
    private bool _failed, _sealed, _disposed;

    // Ownership transfers only after successful construction.
    internal AnalysisObservationCapture(AnalysisScratchArena arena, AnalysisIndexedGraphHeader header)
    {
        ArgumentNullException.ThrowIfNull(arena); ArgumentNullException.ThrowIfNull(header); header.Validate();
        if (header.IssueTotals.Count != 0 || header.BoundaryTotals.Count != 0)
            throw new ArgumentException("A new observation capture requires empty issue counters.", nameof(header));
        _header = header with { Taint = header.Origin == AnalysisOrigin.ImportedInventory ? AnalysisPrivacy.Imported(header.Taint) : AnalysisPrivacy.Join(header.Taint) };
        for (int i = 0; i < header.Roots.Length; i++) _roots.Add(header.Roots[i].Id, i);
        _rootNodes = new ulong[header.Roots.Length]; _observed = header.Identity.StartedUtc;
        _raw = new(arena, AnalysisLimits.Observations, AnalysisLimits.Observations, AnalysisLimits.Observations, AnalysisLimits.Issues);
        _publication = new(Metadata(), false);
    }
    internal ulong Count { get { lock (_gate) return _paths; } }
    internal bool Failed { get { lock (_gate) return _failed || _raw.Failed; } }
    internal void StopAccepting() { lock (_gate) _failed = true; }
    // The UI never takes a lock held across native writes. The CAS is the publication linearization
    // point: a flush that finishes afterward cannot advance the stopped view.
    internal void RequestStop() => RequestStop(null);
    internal void RequestStop(string? issueCode)
    {
        if (issueCode is { Length: > 256 } || issueCode is not null && (issueCode.Length == 0 || issueCode.Contains('\0'))) throw new ArgumentException("Invalid stop issue.", nameof(issueCode));
        while (true)
        {
            var current = Volatile.Read(ref _publication); if (current.Stopped) return;
            var metadata = current.Published;
            if (issueCode is not null)
            {
                metadata = metadata with { Issues = metadata.Issues.SetItem(MeasurementState.Failed, checked(metadata.Issues.GetValueOrDefault(MeasurementState.Failed) + 1)) };
                if (AnalysisCoverage.IsStaleIssue(issueCode, MeasurementState.Failed))
                    metadata = metadata with { Stale = metadata.Stale with { Events = checked(metadata.Stale.Events + 1), Unlocated = true } };
            }
            if (ReferenceEquals(Interlocked.CompareExchange(ref _publication, new(metadata, true), current), current)) return;
        }
    }
    internal void Enumeration(AnalysisEnumerationProgress value, CancellationToken cancellation)
    {
        lock (_gate)
        {
            GuardWriter(); cancellation.ThrowIfCancellationRequested(); value.Validate();
            if (value.Requested > _paths || _enumeration is { } old && (value.Requested < old.Requested || value.Completed < old.Completed || value.Failed < old.Failed))
                throw new InvalidDataException("Enumeration coverage cannot advance beyond captured paths or move backward.");
            _enumeration = value;
        }
    }

    internal ulong Append(AnalysisEntry entry, ulong parent, string rootId, string basis, bool directoryAllocation = false, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_gate)
        {
            GuardWriter(); cancellation.ThrowIfCancellationRequested(); ulong id = checked(_paths + 1);
            if (!_roots.TryGetValue(rootId, out int root) || parent >= id) throw AnalysisIndexedGraph.Invalid();
            int depth = 0;
            if (parent == 0) { if (_rootNodes[root] != 0) throw AnalysisIndexedGraph.Invalid(); }
            else
            {
                var ancestor = AnalysisIndexedPathCodec.Decode(_raw.ReadCurrent(Paths, parent, cancellation), parent, _header.Roots);
                if (ancestor.RootIndex != root || ancestor.Node.Kind != AnalysisKind.Directory || !ancestor.Node.IncludedByPolicy || ancestor.Node.MetadataState != MeasurementState.Known)
                    throw AnalysisIndexedGraph.Invalid();
                depth = checked(ancestor.Depth + 1);
            }
            var observed = entry.ObservedUtc ?? DateTimeOffset.UtcNow;
            if (observed < _header.Identity.StartedUtc) throw AnalysisIndexedGraph.Invalid();
            string? provisional = entry.Kind == AnalysisKind.File ? Id('p', id) : null;
            var node = new AnalysisNode(id, rootId, parent, entry.Name, entry.Kind, provisional, entry.Sparse, entry.Compressed, entry.Cloud)
            { NameEncoding = entry.NameEncoding, RawName = entry.RawName, Times = new(observed, entry.ModifiedUtc, entry.ModifiedPrecisionTicks),
                ObservedIdentity = entry.Identity, MetadataState = entry.MetadataState, IncludedByPolicy = entry.IncludedByPolicy };
            byte[] path = AnalysisIndexedPathCodec.Encode(node, root, depth, _header.Roots);
            byte[]? candidate = provisional is null ? null : AnalysisCapturedFileCodec.Encode(new(id,
                new(provisional, entry.Identity, entry.LogicalLength, entry.Links, entry.ModifiedUtc, string.IsNullOrEmpty(entry.Version) ? null : entry.Version, entry.ModifiedPrecisionTicks),
                new("f" + id.ToString(CultureInfo.InvariantCulture), provisional, null, "reported-data", basis, entry.Allocation, basis, observed, [])));
            byte[]? directory = directoryAllocation && entry.Kind == AnalysisKind.Directory && entry.IncludedByPolicy
                ? AnalysisIndexedValueCodec.EncodeFact(new("d" + id.ToString(CultureInfo.InvariantCulture), null, id, "directory-self", basis, entry.Allocation, basis, observed, [])) : null;
            try
            {
                _raw.Append(Paths, path, cancellation);
                if (candidate is not null) _raw.Append(Files, candidate, cancellation);
                if (directory is not null) _raw.Append(Directories, directory, cancellation);
            }
            catch { _failed = true; throw; }
            _paths = id; if (parent == 0) _rootNodes[root] = id; if (observed > _observed) _observed = observed;
            return id;
        }
    }
    internal void Issue(string code, ulong? node = null, MeasurementState state = MeasurementState.Failed, string? rootId = null, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            GuardWriter(); cancellation.ThrowIfCancellationRequested();
            if (node is { } id && (id == 0 || id > _paths) || rootId is { } root && !_roots.ContainsKey(root)) throw AnalysisIndexedGraph.Invalid();
            var issue = new AnalysisIssue(code, node, state) { RootId = rootId }; byte[] bytes = AnalysisIndexedValueCodec.EncodeIssue(issue);
            int? staleRoot = null;
            if (AnalysisCoverage.IsStaleIssue(code, state))
            {
                if (node is { } path)
                {
                    staleRoot = AnalysisIndexedPathCodec.Decode(_raw.ReadCurrent(Paths, path, cancellation), path, _header.Roots).RootIndex;
                    if (rootId is not null && _header.Roots[staleRoot.Value].Id != rootId) throw AnalysisIndexedGraph.Invalid();
                }
                else if (rootId is not null) staleRoot = _roots[rootId];
            }
            try { if (_samples < AnalysisLimits.Issues) { _raw.Append(Issues, bytes, cancellation); _samples++; } }
            catch { _failed = true; throw; }
            _issues[state] = checked(_issues.GetValueOrDefault(state) + 1);
            if (AnalysisCoverage.BoundaryKind(code) is { } boundary) _boundaries[boundary] = checked(_boundaries.GetValueOrDefault(boundary) + 1);
            if (AnalysisCoverage.IsStaleIssue(code, state))
            {
                _staleEvents = checked(_staleEvents + 1);
                if (staleRoot is { } ordinal) _staleRoots |= 1U << ordinal; else _unlocatedStale = true;
            }
        }
    }
    internal void Publish(CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            GuardWriter();
            try
            {
                _raw.Publish(cancellation); var metadata = Metadata();
                while (true)
                {
                    var current = Volatile.Read(ref _publication); if (current.Stopped) break;
                    if (ReferenceEquals(Interlocked.CompareExchange(ref _publication, new(metadata, false), current), current)) break;
                }
            }
            catch { _failed = true; throw; }
        }
    }
    private AnalysisCapturePublication Metadata() => new(_raw.PublishedToken(), _observed, _issues.ToImmutableDictionary(), _boundaries.ToImmutableDictionary(), _enumeration,
        new(_staleEvents, _header.Roots.Where((_, i) => (_staleRoots & (1U << i)) != 0).Select(root => root.Id).ToImmutableArray(), _unlocatedStale));

    internal async Task<AnalysisIndexedGraphRevision> CompleteAsync(AnalysisScratchWorkspace workspace, DeviceIoScheduler io, AnalysisIoDevices device,
        SessionIdentity identity, AnalysisPhase phase, NamespaceCoverage coverage, CancellationToken cancellation = default, AnalysisFreshness freshness = AnalysisFreshness.Observed)
    {
        var frozen = await AnalysisIo.Run(io, device, token =>
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_sealed)
                {
                    if (!Failed && !Volatile.Read(ref _publication).Stopped) Publish(token);
                    _sealed = true;
                }
                var state = Volatile.Read(ref _publication); var metadata = state.Published; var start = _header.Identity;
                if (identity.SessionId != start.SessionId || identity.Epoch != start.Epoch || identity.StartedUtc != start.StartedUtc || identity.HostProfile != start.HostProfile ||
                    identity.TimeZoneId != start.TimeZoneId || identity.Revision < start.Revision || identity.ObservedUtc < metadata.Observed) throw AnalysisIndexedGraph.Invalid();
                var final = _header with { Identity = identity, Phase = phase, Coverage = Failed || state.Stopped ? NamespaceCoverage.Partial : coverage,
                    Freshness = freshness, IssueTotals = metadata.Issues, BoundaryTotals = metadata.Boundaries, Enumeration = metadata.Enumeration, StaleEvidence = metadata.Stale };
                if (_frozen is { } previous && previous != final) throw new InvalidOperationException("The captured prefix cannot be reinterpreted by a retry.");
                final.Validate(); _frozen = final;
                return (Header: final, Data: _raw.Acquire(metadata.Data));
            }
        }, cancellation).ConfigureAwait(false);
        return await AnalysisObservationCoalescer.BuildAsync(frozen.Header, frozen.Data, workspace, io, device, cancellation).ConfigureAwait(false);
    }
    private void GuardWriter()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_sealed) throw new InvalidOperationException("The observation capture is frozen.");
        if (Volatile.Read(ref _publication).Stopped) throw new OperationCanceledException("The observation capture was stopped.");
        if (Failed) throw new IOException("Observation storage stopped. Keep the last complete published prefix.");
    }
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _raw.Dispose(); } }
    internal static string Id(char prefix, ulong path) => prefix + path.ToString("D8", CultureInfo.InvariantCulture);
    private sealed record AnalysisCapturePublication(AnalysisScratchCompositePublication Data, DateTimeOffset Observed, ImmutableDictionary<MeasurementState, ulong> Issues,
        ImmutableDictionary<AnalysisBoundaryKind, ulong> Boundaries, AnalysisEnumerationProgress? Enumeration, AnalysisStaleEvidence Stale);
    private sealed record AnalysisCaptureState(AnalysisCapturePublication Published, bool Stopped);
}

internal sealed record AnalysisCapturedFile(ulong PathId, AnalysisObject Object, StorageFact Fact);

/// <summary>Only captured values. Backend-relative bytes and runtime source witnesses are deliberately absent.</summary>
internal static class AnalysisCapturedFileCodec
{
    private const uint Format = 0x31434641;
    internal static byte[] Encode(AnalysisCapturedFile value)
    {
        if (value.PathId is < 1 or > AnalysisLimits.Observations || value.Object.Id != AnalysisObservationCapture.Id('p', value.PathId) || value.Fact.ObjectId != value.Object.Id || value.Fact.DirectoryId is not null)
            throw AnalysisIndexedGraph.Invalid();
        byte[] obj = AnalysisIndexedValueCodec.EncodeObject(value.Object), fact = AnalysisIndexedValueCodec.EncodeFact(value.Fact);
        byte[] bytes = new byte[checked(20 + obj.Length + fact.Length)];
        if (bytes.Length > AnalysisScratchVector.MaximumRecordBytes) throw AnalysisIndexedGraph.Invalid();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, Format); BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(4), value.PathId);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12), obj.Length); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), fact.Length);
        obj.CopyTo(bytes, 20); fact.CopyTo(bytes, 20 + obj.Length); return bytes;
    }
    internal static AnalysisCapturedFile Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 20 || bytes.Length > AnalysisScratchVector.MaximumRecordBytes || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Format) throw AnalysisIndexedGraph.Invalid();
        ulong path = BinaryPrimitives.ReadUInt64LittleEndian(bytes[4..]); int objects = BinaryPrimitives.ReadInt32LittleEndian(bytes[12..]), facts = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        if (path is < 1 or > AnalysisLimits.Observations || objects < 1 || facts < 1 || (long)objects + facts != bytes.Length - 20) throw AnalysisIndexedGraph.Invalid();
        var obj = AnalysisIndexedValueCodec.DecodeObject(bytes.Slice(20, objects)); var fact = AnalysisIndexedValueCodec.DecodeFact(bytes[(20 + objects)..]);
        if (obj.Id != AnalysisObservationCapture.Id('p', path) || fact.ObjectId != obj.Id || fact.DirectoryId is not null) throw AnalysisIndexedGraph.Invalid();
        return new(path, obj, fact);
    }
    internal static byte[] IdentityKey(AnalysisCapturedFile file)
    {
        if (file.Object.VerifiedIdentity is { } identity)
        { byte[] text = new UTF8Encoding(false, true).GetBytes(identity), key = new byte[text.Length + 1]; text.CopyTo(key, 1); return key; }
        var result = new byte[9]; result[0] = 1; BinaryPrimitives.WriteUInt64BigEndian(result.AsSpan(1), file.PathId); return result;
    }

    // Private, authenticated, transient sort data only. Length-prefixed identities remain exact
    // groups; the big-endian path precedes the payload so the first alias is still the lowest ID.
    // Existing value-field limits keep even the maximum object/fact payload below the sort-key cap.
    internal static byte[] SortKey(ReadOnlySpan<byte> record)
    {
        var file = Decode(record); byte[] identity = IdentityKey(file);
        int length = checked(12 + identity.Length + record.Length);
        if (length > AnalysisExternalSort.MaximumKeyBytes) throw AnalysisIndexedGraph.Invalid();
        byte[] key = new byte[length]; BinaryPrimitives.WriteInt32BigEndian(key, identity.Length);
        identity.CopyTo(key, 4); BinaryPrimitives.WriteUInt64BigEndian(key.AsSpan(4 + identity.Length), file.PathId);
        record.CopyTo(key.AsSpan(12 + identity.Length)); return key;
    }

    internal static AnalysisCapturedFile DecodeSortKey(ReadOnlySpan<byte> key, out byte[] identity)
    {
        if (key.Length is < 14 or > AnalysisExternalSort.MaximumKeyBytes) throw AnalysisIndexedGraph.Invalid();
        int length = BinaryPrimitives.ReadInt32BigEndian(key);
        if (length is < 1 or > 4097 || length > key.Length - 12) throw AnalysisIndexedGraph.Invalid();
        var file = Decode(key[(12 + length)..]);
        if (file.PathId != BinaryPrimitives.ReadUInt64BigEndian(key[(4 + length)..]) ||
            !key.Slice(4, length).SequenceEqual(IdentityKey(file))) throw AnalysisIndexedGraph.Invalid();
        identity = key.Slice(4, length).ToArray(); return file;
    }
}
