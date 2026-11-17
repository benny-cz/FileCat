using System.Collections.Immutable;
using FileCat.Core.Resources;

namespace FileCat.Core.Content;

public sealed record HexPatchRange(long Offset, byte[] Original, byte[] Replacement)
{
    public long End => checked(Offset + Original.Length);
}

/// <summary>A change seen through the overlay: new content at an offset, or (Bytes null) only a save-state change.</summary>
public readonly record struct HexOverlayChange(long Offset, byte[]? Bytes);

/// <summary>Fixed-length sparse byte overlay with bounded touched bytes and undo history.</summary>
public sealed class HexPatchOverlay : IContentSource
{
    public const int MaxTouchedBytes = 8 * 1024 * 1024;
    public const int MaxUndoBytes = 32 * 1024 * 1024;
    private const int MaxActions = 10_000;
    private readonly IContentSource _baseline;
    private Dictionary<long, byte> _original = new();
    private Dictionary<long, byte> _patch = new();
    private readonly LinkedList<PatchAction> _undo = new();
    private readonly Stack<PatchAction> _redo = new();
    private readonly object _gate = new();
    private long _revision;
    private int _undoBytes;
    private bool _saving;
    private bool _disposed;
    private HexContentContributor? _baselineOrigin;
    private List<HexContentContributor> _contributors = new();
    private int _contributorBytes;
    public const int MaxContributors = 64;
    public const int MaxContributorBytes = 256 * 1024;

    private sealed record PatchAction(long Offset, byte[] Before, byte[] After)
    {
        public int Cost => checked(Before.Length + After.Length);
    }

    public HexPatchOverlay(IContentSource baseline)
    {
        if (!baseline.CanSeek || baseline.Length < 0) throw new ArgumentException("Hex editing requires known random-access length.");
        _baseline = baseline;
        Length = baseline.Length;
        _baselineOrigin = baseline is IHexContentOrigin origin ? origin.CaptureHexOrigin() : HexContentContributor.Unknown;
        IncludeContributor(_baselineOrigin);
    }

    public string DisplayName => _baseline.DisplayName;
    public long Length { get; }
    public bool CanSeek => true;
    public string? LocalPath => _baseline.LocalPath;
    public int DirtyBytes { get { lock (_gate) return _patch.Count; } }
    public int TouchedBytes { get { lock (_gate) return _original.Count; } }
    public bool CanUndo { get { lock (_gate) return _undo.Count > 0; } }
    public bool CanRedo { get { lock (_gate) return _redo.Count > 0; } }
    public bool IsModified(long offset) { lock (_gate) return _patch.ContainsKey(offset); }

    /// <summary>Attach before the first imported Write. Failure and Undo/Redo never remove a contributor.</summary>
    public void IncludeContributor(HexContentContributor contributor)
    {
        ArgumentNullException.ThrowIfNull(contributor);
        if (!Enum.IsDefined(contributor.Protection) || contributor.InputPath is null || contributor.InputPath.Length > 32768 || contributor.Identity is { Length: > 256 })
            throw new ArgumentException("The hex contributor metadata is invalid or exceeds its bound.", nameof(contributor));
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_saving) throw new InvalidOperationException("A hex save is in progress.");
            if (_contributors.Contains(contributor)) return;
            int bytes = checked(128 + 2 * (contributor.InputPath.Length + (contributor.Identity?.Length ?? 0)));
            if (_contributors.Count >= MaxContributors || bytes > MaxContributorBytes - _contributorBytes)
                throw new IOException("The hex editor's input provenance limit was reached. Reload before importing another patch.");
            _contributors.Add(contributor); _contributorBytes += bytes;
            Interlocked.Increment(ref _revision);
        }
    }

    /// <summary>Bytes of these ranges no edit has touched yet: what writing them all would add to <see cref="TouchedBytes"/>.</summary>
    public int NewlyTouched(IEnumerable<HexPatchRange> ranges)
    {
        lock (_gate)
        {
            int count = 0;
            foreach (var range in ranges)
                for (int i = 0; i < range.Original.Length; i++)
                    if (!_original.ContainsKey(range.Offset + i)) count++;
            return count;
        }
    }

    /// <summary>Raised outside the lock on the thread that changed the overlay.</summary>
    public event Action<HexOverlayChange>? Changed;

    public ContentRevision? GetRevision() => new(Length, Interlocked.Read(ref _revision), "hex-overlay");

    public int Read(long offset, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (offset >= Length) return 0;
        int length = (int)Math.Min(buffer.Length, Length - offset);
        int read = _baseline.Read(offset, buffer[..length]);
        lock (_gate)
            for (int i = 0; i < read; i++)
                if (_patch.TryGetValue(offset + i, out byte b)) buffer[i] = b;
        return read;
    }

    public void Write(long offset, ReadOnlySpan<byte> bytes)
    {
        if (offset < 0 || bytes.Length > 1024 * 1024 || offset > Length || bytes.Length > Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset), "Hex edits must stay within the original length and one action is limited to 1 MiB.");
        if (bytes.Length == 0) return;
        var baseBytes = new byte[bytes.Length];
        if (_baseline.Read(offset, baseBytes) != bytes.Length) throw new IOException("The protected baseline could not be read completely.");
        var after = bytes.ToArray();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_saving) throw new InvalidOperationException("A hex save is in progress.");
            var before = new byte[bytes.Length];
            for (int i = 0; i < bytes.Length; i++)
            {
                long at = offset + i;
                before[i] = _patch.TryGetValue(at, out byte existing) ? existing :
                    _original.TryGetValue(at, out byte captured) ? captured : baseBytes[i];
            }
            if (before.AsSpan().SequenceEqual(after)) return;
            int newlyTouched = 0;
            for (int i = 0; i < bytes.Length; i++) if (!_original.ContainsKey(offset + i)) newlyTouched++;
            if (_original.Count + newlyTouched > MaxTouchedBytes)
                throw new IOException($"This edit exceeds the {MaxTouchedBytes / (1024 * 1024)} MiB touched-byte limit. Save or export the patch first.");
            for (int i = 0; i < bytes.Length; i++)
            {
                long at = offset + i;
                if (!_original.ContainsKey(at)) _original[at] = baseBytes[i];
            }
            Apply(offset, after);
            var action = new PatchAction(offset, before, after);
            _undo.AddLast(action);
            _undoBytes += action.Cost;
            _redo.Clear();
            while (_undoBytes > MaxUndoBytes || _undo.Count > MaxActions)
            {
                _undoBytes -= _undo.First!.Value.Cost;
                _undo.RemoveFirst();
            }
            Interlocked.Increment(ref _revision);
        }
        Changed?.Invoke(new HexOverlayChange(offset, after));
    }

    public bool Undo() => Undo(out _);

    /// <summary>Reverts the last action; <paramref name="change"/> says where, so an editor can show it.</summary>
    public bool Undo(out HexOverlayChange change)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_saving) throw new InvalidOperationException("A hex save is in progress.");
            if (_undo.Last is null) { change = default; return false; }
            var action = _undo.Last.Value;
            _undo.RemoveLast();
            _undoBytes -= action.Cost;
            Apply(action.Offset, action.Before);
            _redo.Push(action);
            Interlocked.Increment(ref _revision);
            change = new HexOverlayChange(action.Offset, action.Before);
        }
        Changed?.Invoke(change);
        return true;
    }

    public bool Redo() => Redo(out _);

    public bool Redo(out HexOverlayChange change)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_saving) throw new InvalidOperationException("A hex save is in progress.");
            if (_redo.Count == 0) { change = default; return false; }
            var action = _redo.Pop();
            Apply(action.Offset, action.After);
            _undo.AddLast(action);
            _undoBytes += action.Cost;
            Interlocked.Increment(ref _revision);
            change = new HexOverlayChange(action.Offset, action.After);
        }
        Changed?.Invoke(change);
        return true;
    }

    private void Apply(long offset, byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            long at = offset + i;
            if (bytes[i] == _original[at]) _patch.Remove(at);
            else _patch[at] = bytes[i];
        }
    }

    public IReadOnlyList<HexPatchRange> SnapshotRanges()
    {
        lock (_gate)
        {
            var sorted = _patch.Keys.Order().ToArray();
            var result = new List<HexPatchRange>();
            for (int i = 0; i < sorted.Length;)
            {
                long start = sorted[i];
                var old = new List<byte>();
                var replacement = new List<byte>();
                do
                {
                    long at = sorted[i];
                    old.Add(_original[at]);
                    replacement.Add(_patch[at]);
                    i++;
                } while (i < sorted.Length && sorted[i] == sorted[i - 1] + 1);
                result.Add(new HexPatchRange(start, old.ToArray(), replacement.ToArray()));
            }
            return result;
        }
    }

    public IReadOnlyList<HexPatchRange> FreezeForSave() => FreezeForPersistence().Ranges;

    public HexSaveSnapshot FreezeForPersistence()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_saving) throw new InvalidOperationException("A hex save is already in progress.");
            _saving = true;
            return new(SnapshotRanges(), _contributors.ToImmutableArray());
        }
    }

    public void FinishSave(bool committed)
    {
        lock (_gate)
        {
            if (!_saving) throw new InvalidOperationException("No hex save is in progress.");
            if (committed)
            {
                _patch.Clear(); _original.Clear(); _undo.Clear(); _redo.Clear(); _undoBytes = 0;
                if (!_disposed) ResetContributors();
                Interlocked.Increment(ref _revision);
            }
            _saving = false;
        }
        if (committed) Changed?.Invoke(new HexOverlayChange(0, null));
    }

    /// <summary>Drop committed edits; subsequent undo applies only to the next unsaved session.</summary>
    public void AcceptSave()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_saving) throw new InvalidOperationException("A hex save is in progress.");
            _patch.Clear(); _original.Clear(); _undo.Clear(); _redo.Clear(); _undoBytes = 0;
            ResetContributors();
            Interlocked.Increment(ref _revision);
        }
        Changed?.Invoke(new HexOverlayChange(0, null));
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            // A retired reader/editor may remain alive in a submitted frame or a completed operation.
            // Replace the dictionaries so retirement retains no backing arrays, including minimum capacity.
            _patch = new(); _original = new();
            _undo.Clear(); _redo.Clear(); _redo.TrimExcess(); _undoBytes = 0;
            _contributors = new(); _baselineOrigin = null; _contributorBytes = 0;
            Changed = null;
        }
        // Retire owned memory even when closing the source fails; never close the source twice.
        _baseline.Dispose();
    }

    private void ResetContributors()
    {
        // Called with the lock held, including while FinishSave still owns the saving state.
        // The baseline was validated on construction; only imported contributors are retired.
        _contributors = [_baselineOrigin!];
        _contributorBytes = checked(128 + 2 * (_baselineOrigin!.InputPath.Length + (_baselineOrigin.Identity?.Length ?? 0)));
    }
}
