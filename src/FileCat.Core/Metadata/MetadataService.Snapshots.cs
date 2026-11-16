namespace FileCat.Core.Metadata;

/// <summary>A synchronous retirement reason; a null key denotes field-wide or service-wide invalidation.</summary>
public readonly record struct MetadataPublication(string? FieldId, string? Key, long Revision, object? Origin);

public sealed partial class MetadataService
{
    private readonly int _maxSnapshots;
    private readonly HashSet<SnapshotClaim> _snapshots = [];
    private long _publicationRevision;

    /// <summary>
    /// Starts a bounded field claim before capturing retained values. Changes in another field do not retire it.
    /// Explicit analysis can identify its own productions with the same non-null origin. A capacity refusal
    /// returns an already retired claim; consumers must then refrain from publishing a retained snapshot.
    /// </summary>
    public SnapshotClaim TrackSnapshot(string fieldId, object? origin = null, string? folder = null)
    {
        lock (_publicationLock)
        {
            var claim = new SnapshotClaim(this, fieldId, origin, _publicationRevision, folder);
            if (!_disposed && _snapshots.Count < _maxSnapshots) _snapshots.Add(claim);
            else claim.Retire(new MetadataPublication(fieldId, null, _publicationRevision, null));
            return claim;
        }
    }

    // Called only under the same lock as cache publication/invalidation. Retirement merely flips a claim;
    // it never calls user code, waits on the UI, sorts, or reads providers. Whole-field retirement is conservative
    // when a targeted Forget affects a field whose retained values may already have left the ordinary cache.
    private void RetireSnapshots(string? fieldId, string? key, object? origin, string? folder = null)
    {
        var publication = new MetadataPublication(fieldId, key, ++_publicationRevision, origin);
        foreach (var claim in _snapshots)
            if ((fieldId is null || fieldId == claim.FieldId) &&
                (origin is null || !ReferenceEquals(origin, claim.Origin)) && claim.Contains(key, folder)) claim.Retire(publication);
        _snapshots.RemoveWhere(claim => !claim.IsCurrent);
    }

    /// <summary>Small lifetime claim only; retained buffers and their shared byte reservations belong to the caller.</summary>
    public sealed class SnapshotClaim : IDisposable
    {
        private readonly MetadataService _owner;
        private bool _current = true;
        private MetadataPublication? _retiredBy;

        internal SnapshotClaim(MetadataService owner, string fieldId, object? origin, long revision, string? folder)
        {
            _owner = owner;
            FieldId = fieldId;
            Origin = origin;
            Revision = revision;
            Folder = folder?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public string FieldId { get; }
        internal object? Origin { get; }
        public long Revision { get; }
        public string? Folder { get; }
        internal bool Contains(string? key, string? folder = null)
        {
            if (Folder is not null && folder is not null)
                return string.Equals(Folder, folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            if (Folder is null || key is null) return true;
            int start = 0;
            for (int i = 0; i < 3; i++)
            {
                int separator = key.IndexOf('|', start);
                if (separator < 0) return true; // Unknown key layout: retire conservatively.
                start = separator + 1;
            }
            var path = key.AsSpan(start);
            int last = path.LastIndexOfAny(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return last >= 0 && path[..last].Equals(Folder,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        public bool IsCurrent => Volatile.Read(ref _current);
        public MetadataPublication? RetiredBy { get { lock (_owner._publicationLock) return _retiredBy; } }

        internal void Retire(MetadataPublication publication)
        {
            _retiredBy = publication;
            Volatile.Write(ref _current, false);
        }

        /// <summary>
        /// Atomically checks the claim and attaches the captured result. The callback must only attach memory
        /// state: no I/O, sorting, UI waits or async work. Keep this claim until the retained state is released;
        /// later changes retire it synchronously, before a subsequent sort can accept it.
        /// </summary>
        public bool TryPublish(Action publish)
        {
            lock (_owner._publicationLock)
            {
                if (!_current) return false;
                publish();
                return true;
            }
        }

        public void Dispose()
        {
            lock (_owner._publicationLock)
            {
                Volatile.Write(ref _current, false);
                _owner._snapshots.Remove(this);
            }
        }
    }
}
