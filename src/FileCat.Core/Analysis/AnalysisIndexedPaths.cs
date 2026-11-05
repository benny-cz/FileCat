using System.Collections.Immutable;
using System.Text;

namespace FileCat.Core.Analysis;

/// <summary>
/// Dense live-observation path storage. Header identity and privacy classification stay in the owning
/// runtime; no decoded path creates a live source. Sibling/object/fact/issue indexes are separate work.
/// All reads, writes, publication and disposal run on admitted workers, never through UI indexers.
/// </summary>
internal sealed class AnalysisIndexedPaths : IDisposable
{
    private readonly AnalysisScratchVector _records;
    private readonly SessionIdentity _acquisition;
    private readonly ImmutableArray<RootBinding> _roots;
    private readonly Dictionary<string, int> _rootIndexes = new(StringComparer.Ordinal);
    private readonly ulong[] _rootNodes;
    private readonly object _gate = new();
    private readonly AnalysisOrigin _origin;
    private readonly AnalysisTaint _taint;
    private DateTimeOffset _acceptedObserved, _committedObserved;
    private SessionIdentity? _lastPublished;
    private ulong _lastPublishedCount;
    private bool _disposed;

    internal AnalysisIndexedPaths(AnalysisScratchArena arena, SessionIdentity acquisition, ImmutableArray<RootBinding> roots,
        AnalysisOrigin origin = AnalysisOrigin.LiveSession, AnalysisTaint taint = AnalysisTaint.OrdinarySensitive)
    {
        ArgumentNullException.ThrowIfNull(arena); ArgumentNullException.ThrowIfNull(acquisition);
        if (acquisition.SessionId == Guid.Empty || acquisition.Epoch < 0 || acquisition.Revision < 0 || acquisition.ObservedUtc < acquisition.StartedUtc ||
            acquisition.HostProfile is not { Length: > 0 and <= 4096 } || acquisition.TimeZoneId is not { Length: > 0 and <= 256 } ||
            roots.IsDefault || roots.Length is < 1 or > AnalysisLimits.Roots || !Enum.IsDefined(origin) || taint == 0 || (taint & ~AnalysisPrivacy.All) != 0)
            throw Invalid();
        for (int i = 0; i < roots.Length; i++)
        {
            var root = roots[i];
            if (root is null || string.IsNullOrEmpty(root.Id) || root.Id.Length > 256 || root.Id.Contains('\0') ||
                root.CanonicalLocator is not { Length: > 0 and <= AnalysisLimits.LocatorBytes } || root.CanonicalLocator.Contains('\0') ||
                !Enum.IsDefined(root.Kind) || !Enum.IsDefined(root.NameRules) ||
                !Enum.IsDefined(root.Disposition) || !_rootIndexes.TryAdd(root.Id, i)) throw Invalid();
        }
        _records = new(arena); _acquisition = acquisition; _roots = roots; _rootNodes = new ulong[roots.Length];
        _origin = origin; _taint = origin == AnalysisOrigin.ImportedInventory ? AnalysisPrivacy.Imported(taint) : AnalysisPrivacy.Join(taint);
        _acceptedObserved = _committedObserved = acquisition.StartedUtc;
    }

    internal ulong AcceptedCount => _records.AcceptedCount;
    internal ulong CommittedCount => _records.CommittedCount;
    internal bool Failed => _records.Failed;

    internal void Append(AnalysisNode node, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); cancellation.ThrowIfCancellationRequested();
            if (node is null || node.Id != _records.AcceptedCount + 1 || !_rootIndexes.TryGetValue(node.RootId, out int root)) throw Invalid();
            int depth = 0;
            if (node.ParentId == 0)
            { if (_rootNodes[root] != 0) throw Invalid(); }
            else
            {
                if (node.ParentId >= node.Id) throw Invalid();
                var parent = ReadCurrent(node.ParentId, cancellation);
                if (parent.RootIndex != root || parent.Node.Kind != AnalysisKind.Directory || !parent.Node.IncludedByPolicy || parent.Node.MetadataState != MeasurementState.Known) throw Invalid();
                depth = checked(parent.Depth + 1);
            }
            if (node.Times is { } times && times.ObservedUtc < _acquisition.StartedUtc) throw Invalid();
            byte[] record = AnalysisIndexedPathCodec.Encode(node, root, depth, _roots);
            ulong before = _records.CommittedCount;
            _records.Append(record, cancellation);
            // An automatic flush commits the previously accepted buffer BEFORE this new observation.
            if (_records.CommittedCount != before) _committedObserved = _acceptedObserved;
            if (node.Times is { } observed && observed.ObservedUtc > _acceptedObserved) _acceptedObserved = observed.ObservedUtc;
            if (node.ParentId == 0) _rootNodes[root] = node.Id;
        }
    }

    private AnalysisIndexedPathRecord ReadCurrent(ulong id, CancellationToken cancellation) =>
        AnalysisIndexedPathCodec.Decode(_records.ReadCurrent(id, cancellation), id, _roots);

    internal void Flush(CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this); _records.Flush(cancellation); _committedObserved = _acceptedObserved;
        }
    }

    internal AnalysisIndexedPathRevision CaptureCommitted(SessionIdentity revision)
    {
        ArgumentNullException.ThrowIfNull(revision);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (revision.SessionId != _acquisition.SessionId || revision.Epoch != _acquisition.Epoch || revision.StartedUtc != _acquisition.StartedUtc ||
                revision.HostProfile != _acquisition.HostProfile || revision.TimeZoneId != _acquisition.TimeZoneId ||
                revision.Revision < _acquisition.Revision || revision.ObservedUtc < _committedObserved ||
                _lastPublished is { } last && (revision.Revision < last.Revision || revision.ObservedUtc < last.ObservedUtc ||
                    revision.Revision == last.Revision && (revision != last || _records.CommittedCount != _lastPublishedCount)))
                throw new InvalidOperationException("A published path revision cannot change acquisition identity or reinterpret an existing revision.");
            var records = _records.CaptureCommitted();
            try
            {
                var result = new AnalysisIndexedPathRevision(records, revision, _roots, _origin, _taint);
                _lastPublished = revision; _lastPublishedCount = records.Count; return result;
            }
            catch { records.Dispose(); throw; }
        }
    }

    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _records.Dispose(); } }
    private static InvalidDataException Invalid() => new("Invalid indexed analysis path structure.");
}

/// <summary>Inert, identity-qualified path observations; this type has no backend, binding or operation methods.</summary>
internal sealed class AnalysisIndexedPathRevision : IDisposable
{
    private readonly AnalysisScratchVectorCheckpoint _records;
    private readonly object _gate = new();
    private bool _disposed;
    internal SessionIdentity Identity { get; }
    internal ImmutableArray<RootBinding> Roots { get; }
    internal AnalysisOrigin Origin { get; }
    internal AnalysisTaint Taint { get; }
    internal ulong Count => _records.Count;
    internal AnalysisIndexedPathRevision(AnalysisScratchVectorCheckpoint records, SessionIdentity identity,
        ImmutableArray<RootBinding> roots, AnalysisOrigin origin, AnalysisTaint taint)
    { _records = records; Identity = identity; Roots = roots; Origin = origin; Taint = taint; }

    internal AnalysisNode Read(ulong id, CancellationToken cancellation = default)
    { lock (_gate) { Guard(); return ReadRecord(id, cancellation).Node; } }
    private AnalysisIndexedPathRecord ReadRecord(ulong id, CancellationToken cancellation)
    {
        var record = AnalysisIndexedPathCodec.Decode(_records.Read(id, cancellation), id, Roots);
        if (record.Node.Times is { } times && (times.ObservedUtc < Identity.StartedUtc || times.ObservedUtc > Identity.ObservedUtc))
            throw new InvalidDataException("The path observation is outside this published observation interval.");
        return record;
    }

    internal ImmutableArray<AnalysisNode> ReadLineage(ulong id, CancellationToken cancellation = default)
    {
        lock (_gate)
        {
            Guard(); var result = ImmutableArray.CreateBuilder<AnalysisNode>();
            var current = ReadRecord(id, cancellation); int root = current.RootIndex, depth = current.Depth;
            var binding = Roots[root];
            long bytes = binding.NameRules == AnalysisNameRules.Windows ? checked(binding.CanonicalLocator.Length * 2L) : Encoding.UTF8.GetByteCount(binding.CanonicalLocator);
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                if (current.RootIndex != root || current.Depth != depth || result.Count > AnalysisLimits.Depth) throw InvalidLineage();
                var node = current.Node; result.Add(node);
                if (node.ParentId == 0) break;
                int raw = node.RawName.IsEmpty ? AnalysisRawNames.Encode(node.NameEncoding, node.Name).Length : node.RawName.Length;
                bytes = checked(bytes + raw + (binding.NameRules == AnalysisNameRules.Windows ? 2 : 1));
                if (bytes > AnalysisLimits.LocatorBytes) throw InvalidLineage();
                current = ReadRecord(node.ParentId, cancellation); depth--;
                if (current.Node.Kind != AnalysisKind.Directory || !current.Node.IncludedByPolicy || current.Node.MetadataState != MeasurementState.Known) throw InvalidLineage();
            }
            if (depth != 0 || bytes > AnalysisLimits.LocatorBytes) throw InvalidLineage();
            result.Reverse(); return result.ToImmutable();
        }
    }
    private static InvalidDataException InvalidLineage() => new("The observed path exceeds its captured structural or locator bounds.");
    private void Guard() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose() { lock (_gate) { if (_disposed) return; _disposed = true; _records.Dispose(); } }
}
