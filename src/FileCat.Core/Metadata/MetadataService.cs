using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
using FileCat.Core.Verification;
using FileCat.Core.Threading;

namespace FileCat.Core.Metadata;

/// <summary>Cost classes (plan §10): visible-row work only for Cheap/Expensive; Analysis is always explicit.</summary>
public enum MetadataCost
{
    Immediate,
    Cheap,
    Expensive,
    Analysis,
}

public enum MetadataState
{
    NotRequested,
    Pending,
    Available,
    /// <summary>The item has no such value (e.g. a text file has no pixel dimensions).</summary>
    Absent,
    /// <summary>Not computed here (slow location, unsupported item kind).</summary>
    Unsupported,
    Failed,
    /// <summary>The system refused access to the requested metadata.</summary>
    Denied,
}

public enum MetadataObservation
{
    Current,
    LastKnown,
    Gone,
}

public readonly record struct MetadataValue(MetadataState State, object? Value = null, string? Detail = null,
    MetadataObservation Observation = MetadataObservation.Current)
{
    public static readonly MetadataValue Pending = new(MetadataState.Pending);
    public static readonly MetadataValue Absent = new(MetadataState.Absent);
}

/// <summary>
/// A metadata column's descriptor: stable id, cost class, applicability, producer, formatter, and sort key. Fields read for
/// files only unless <paramref name="Folders"/>; a value that can change without a new modification time (permissions)
/// is read again after <paramref name="RefreshAfter"/>, while the old one stays on screen.
/// </summary>
public sealed record MetadataField(
    string Id,
    string Title,
    MetadataCost Cost,
    Func<string, bool> AppliesToName,
    Func<string, CancellationToken, object?> Produce,
    Func<object?, string> Format,
    bool RightAlign = false,
    Func<object?, IComparable?>? SortKey = null,
    bool Folders = false,
    TimeSpan? RefreshAfter = null,
    Func<object?, string>? Detail = null);

/// <summary>
/// Demand-driven metadata (plan §10): values are produced for rows the user can see, bounded per device,
/// coalesced, cached by path + revision + field, and requests for rows scrolled away are dropped. Zero or
/// empty text is a real value; "pending", "absent", and "unsupported" are states, never fabricated values.
/// </summary>
public sealed partial class MetadataService
{
    private const int MaxCache = 50_000;
    private const int MaxOutstandingPerDevice = 4;
    private readonly DeviceIoScheduler _io;
    private readonly Func<long> _milliseconds;
    private readonly int _maxCache;
    private readonly ConcurrentDictionary<string, MetadataValue> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _producedAt = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _outstanding = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MetadataField> _fields = new(StringComparer.Ordinal);
    private readonly object _publicationLock = new();
    private readonly HashSet<Production> _productions = [];
    private int _notifyPending;

    /// <summary>An invalidated producer may finish its current call, but must not republish its old value.</summary>
    private sealed class Production(string key, string fieldId, object? origin)
    {
        public string Key { get; } = key;
        public string FieldId { get; } = fieldId;
        public object? Origin { get; } = origin;
        public bool HadAvailableValue; // captured when production starts, under the publication lock
        public bool Current = true; // guarded with cache publication and invalidation
    }

    public MetadataService(DeviceIoScheduler io) : this(io, () => Environment.TickCount64) { }

    internal MetadataService(DeviceIoScheduler io, Func<long> milliseconds, int maxCache = MaxCache, int maxSnapshots = 64)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCache, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSnapshots, 1);
        _io = io;
        _milliseconds = milliseconds;
        _maxCache = maxCache;
        _maxSnapshots = maxSnapshots;
        foreach (var f in BuiltInFields.All) _fields[f.Id] = f;
    }

    /// <summary>Raised (throttled, on a pool thread) when new values are available.</summary>
    public event Action? ValuesChanged;

    public IReadOnlyCollection<MetadataField> Fields => _fields.Values;

    public MetadataField? Field(string id) => _fields.GetValueOrDefault(id);

    public void Register(MetadataField field) => _fields[field.Id] = field;

    private static string Key(string path, in EntryData e, string fieldId) => $"{fieldId}|{e.Modified}|{e.Size}|{path}";

    /// <summary>
    /// Returns the cached value or schedules its production. <paramref name="isStillWanted"/> lets queued work
    /// be skipped once rows scroll away.
    /// </summary>
    public MetadataValue Get(string fieldId, string path, in EntryData entry, string deviceKey, bool slowLocation, Func<bool>? isStillWanted = null)
    {
        var value = SelectCached(fieldId, path, entry, slowLocation, out var field, out var key);
        if (key is null || field is null || slowLocation && field.Cost >= MetadataCost.Expensive) return value;
        var refresh = EffectiveRefresh(field, slowLocation);
        if (value.State != MetadataState.NotRequested)
        {
            // Read again when due; the old value stays on screen until the new one arrives.
            if (AutomaticallyDue(key, value, refresh)) Schedule(key, field, path, deviceKey, refresh, isStillWanted);
            return value;
        }
        Schedule(key, field, path, deviceKey, refresh, isStillWanted);
        return MetadataValue.Pending;
    }

    /// <summary>Reads already produced metadata without requesting I/O, for bounded accessibility inspection.</summary>
    public MetadataValue Peek(string fieldId, string path, in EntryData entry) =>
        _cache.TryGetValue(Key(path, entry, fieldId), out var value) ? value : new MetadataValue(MetadataState.NotRequested);

    /// <summary>Memory-only selection with the same applicability, placeholder and slow-location gates as Get.</summary>
    public MetadataValue Peek(string fieldId, string path, in EntryData entry, bool slowLocation) =>
        SelectCached(fieldId, path, entry, slowLocation, out _, out _);

    private MetadataValue SelectCached(string fieldId, string path, in EntryData entry, bool slowLocation,
        out MetadataField? field, out string? key)
    {
        key = null;
        if (!TryEligibleField(fieldId, entry, out field, out var rejection)) return rejection;
        key = Key(path, entry, fieldId);
        if (_cache.TryGetValue(key, out var value)) return value;
        return slowLocation && field!.Cost >= MetadataCost.Expensive
            ? new MetadataValue(MetadataState.Unsupported, Detail: "Not computed automatically on slow locations.")
            : new MetadataValue(MetadataState.NotRequested);
    }

    private bool TryEligibleField(string fieldId, in EntryData entry, out MetadataField? field, out MetadataValue rejection)
    {
        rejection = MetadataValue.Absent;
        if (!_fields.TryGetValue(fieldId, out field))
            rejection = new MetadataValue(MetadataState.Unsupported, Detail: "Unknown field.");
        else if (!Applies(field, entry)) return false;
        else if (entry.Has(EntryFlags.Offline))
            rejection = new MetadataValue(MetadataState.Unsupported, Detail: "Not read for a detected cloud placeholder.");
        else return true;
        return false;
    }

    private static TimeSpan? EffectiveRefresh(MetadataField field, bool slowLocation) =>
        field.RefreshAfter is { } refresh ? TimeSpan.FromTicks(refresh.Ticks * (slowLocation ? 6 : 1)) : null;

    private bool Fresh(string key, TimeSpan? refresh) => refresh is null ||
        _producedAt.TryGetValue(key, out var at) && at != long.MinValue && _milliseconds() - at < refresh.Value.TotalMilliseconds;

    private bool AutomaticallyDue(string key, MetadataValue value, TimeSpan? refresh) =>
        refresh is not null && value.State is not (MetadataState.Denied or MetadataState.Unsupported) && !Fresh(key, refresh);

    private static bool Applies(MetadataField field, in EntryData entry) =>
        (entry.Kind == EntryKind.File || entry.Kind == EntryKind.Directory && field.Folders) && field.AppliesToName(entry.Name);

    /// <summary>Synchronous production for explicit analysis jobs (background threads only).</summary>
    public MetadataValue Compute(string fieldId, string path, in EntryData entry, CancellationToken ct,
        bool slowLocation = false, object? origin = null)
    {
        if (!TryEligibleField(fieldId, entry, out var field, out var rejection)) return rejection;
        if (ct.IsCancellationRequested) return new MetadataValue(MetadataState.NotRequested);
        var key = Key(path, entry, fieldId);
        if (_cache.TryGetValue(key, out var v) && v.State is MetadataState.Available or MetadataState.Absent &&
            Fresh(key, EffectiveRefresh(field!, slowLocation))) return v;
        var production = BeginProduction(key, fieldId, origin);
        try
        {
            var value = Produce(field!, path, ct);
            if (!Store(production, value, field!.RefreshAfter is not null, ct, out var stored, out bool changed))
                return new MetadataValue(MetadataState.NotRequested);
            if (changed) Notify();
            return stored;
        }
        finally
        {
            EndProduction(production);
            if (!IsCurrent(production)) Notify();
        }
    }

    private void Schedule(string key, MetadataField field, string path, string deviceKey, TimeSpan? refresh, Func<bool>? isStillWanted)
    {
        if (!_inFlight.TryAdd(key, 0)) return;
        // A previous producer may publish between Get's cache miss and this admission.
        // Recheck under the publication lock so a late caller cannot start a duplicate
        // producer after the first has already removed its in-flight record.
        lock (_publicationLock)
        {
            if (_cache.TryGetValue(key, out var cached) && !AutomaticallyDue(key, cached, refresh))
            {
                _inFlight.TryRemove(key, out _);
                return;
            }
        }
        int outstanding = _outstanding.AddOrUpdate(deviceKey, 1, (_, n) => n + 1);
        if (outstanding > MaxOutstandingPerDevice * 64)
        {
            // Backlog guard: drop instead of growing unbounded; the row asks again while visible.
            ReleaseOutstanding(deviceKey);
            _inFlight.TryRemove(key, out _);
            return;
        }
        var production = BeginProduction(key, field.Id);
        void Finished()
        {
            if (!EndProduction(production)) return;
            _inFlight.TryRemove(key, out _);
            ReleaseOutstanding(deviceKey);
            if (!IsCurrent(production)) Notify(); // visible rows can retry now that obsolete demand has ended
        }
        var work = _io.Run(deviceKey, IoPriority.Background, ct =>
        {
            try
            {
                if (isStillWanted is not null && !isStillWanted() || !IsCurrent(production)) return;
                var value = Produce(field, path, ct);
                if (Store(production, value, field.RefreshAfter is not null, ct, out _, out bool changed) && changed) Notify();
            }
            finally
            {
                Finished();
            }
        });
        // Shutdown cancels queued work without entering its callback; release its demand record in that case too.
        _ = work.ContinueWith(_ => Finished(), CancellationToken.None,
            TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private void ReleaseOutstanding(string deviceKey)
    {
        int remaining = _outstanding.AddOrUpdate(deviceKey, 0, (_, n) => Math.Max(0, n - 1));
        if (remaining == 0)
            // Remove only the observed zero. A concurrent admission that changed it to one must survive.
            ((ICollection<KeyValuePair<string, int>>)_outstanding).Remove(new(deviceKey, 0));
    }

    private static MetadataValue Produce(MetadataField field, string path, CancellationToken ct)
    {
        try
        {
            var value = field.Produce(path, ct);
            ct.ThrowIfCancellationRequested();
            return value switch
            {
                MetadataValue structured => structured,
                null => MetadataValue.Absent,
                _ => new MetadataValue(MetadataState.Available, value),
            };
        }
        catch (OperationCanceledException)
        {
            return new MetadataValue(MetadataState.NotRequested);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new MetadataValue(MetadataState.Denied, Detail: Sentence(ex.Message));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or ArgumentException)
        {
            return new MetadataValue(MetadataState.Failed, Detail: Sentence(ex.Message));
        }
    }

    private static string Sentence(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Could not be read just now.";
        string trimmed = text.TrimEnd();
        return trimmed[^1] is '.' or '!' or '?' ? trimmed : trimmed + ".";
    }

    private Production BeginProduction(string key, string fieldId, object? origin = null)
    {
        var production = new Production(key, fieldId, origin);
        lock (_publicationLock)
        {
            production.HadAvailableValue = _cache.TryGetValue(key, out var before) && before.State == MetadataState.Available;
            _productions.Add(production);
        }
        return production;
    }

    private bool EndProduction(Production production)
    {
        lock (_publicationLock) return _productions.Remove(production);
    }

    private bool IsCurrent(Production production)
    {
        lock (_publicationLock) return production.Current;
    }

    private bool Store(Production production, MetadataValue value, bool refreshing, CancellationToken ct,
        out MetadataValue stored, out bool changed)
    {
        lock (_publicationLock)
        {
            changed = false;
            stored = new MetadataValue(MetadataState.NotRequested);
            if (!production.Current || ct.IsCancellationRequested || value.State == MetadataState.NotRequested) return false;
            string key = production.Key;
            bool hadValue = _cache.TryGetValue(key, out var before);
            if (refreshing && production.HadAvailableValue && value.State == MetadataState.Failed && hadValue && before.State == MetadataState.Available)
                value = new MetadataValue(MetadataState.Available, before.Value,
                    Sentence(value.Detail ?? "Could not be read just now.") + " Showing the last value read.", MetadataObservation.LastKnown);
            changed = !hadValue || !before.Equals(value);
            if (changed) RetireSnapshots(production.FieldId, key, production.Origin);
            if (_cache.TryAdd(key, value)) _order.Enqueue(key); // a value read again keeps its place
            else _cache[key] = value;
            if (refreshing && value.State is not (MetadataState.Denied or MetadataState.Unsupported)) _producedAt[key] = _milliseconds();
            else _producedAt.TryRemove(key, out _);
            stored = value;
            while (_cache.Count > _maxCache && _order.TryDequeue(out var old))
            {
                _cache.TryRemove(old, out _);
                _producedAt.TryRemove(old, out _);
            }
            return true;
        }
    }

    private void Notify()
    {
        if (Interlocked.Exchange(ref _notifyPending, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(120);
            Interlocked.Exchange(ref _notifyPending, 0);
            ValuesChanged?.Invoke();
        });
    }

    /// <summary>Signals a memory-only presentation change, such as a completed principal-name lookup.</summary>
    public void NotifyValuesChanged() => Notify();

    /// <summary>Makes recorded refreshable values due and requests repaint; performs no production.</summary>
    public void ExpireRefreshable()
    {
        lock (_publicationLock)
            foreach (string key in _producedAt.Keys) _producedAt[key] = long.MinValue;
        Notify();
    }

    /// <summary>Forgets one field's values for one item (asked again when its row is drawn).</summary>
    public void Forget(string fieldId, string path)
    {
        string prefix = fieldId + "|", suffix = "|" + path;
        Forget(fieldId, key => key.StartsWith(prefix, StringComparison.Ordinal) && key.EndsWith(suffix, StringComparison.Ordinal));
    }

    /// <summary>Forgets one field's values for many items (the files a job just checked) in one pass over what is kept.</summary>
    public void Forget(string fieldId, IReadOnlyCollection<string> paths)
    {
        if (paths.Count == 0) return;
        // A checksum file may spell a name in another letter case than the folder does: forgetting a namesake too only asks again.
        var wanted = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        string prefix = fieldId + "|";
        Forget(fieldId, key => key.StartsWith(prefix, StringComparison.Ordinal) && KeyPath(key) is { } path && wanted.Contains(path));
    }

    /// <summary>Retire one folder's row claims when its listing is refreshed, even if size and mtime were restored.</summary>
    public void ForgetFolder(string fieldId, string folder)
    {
        string prefix = fieldId + "|";
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        Forget(fieldId, key => key.StartsWith(prefix, StringComparison.Ordinal) &&
            KeyPath(key) is { } path && string.Equals(Path.GetDirectoryName(path), folder, comparison));
    }

    /// <summary>Show the result of an explicit completed check on a row until its next listing refresh.</summary>
    public void PublishFreshVerification(string path, in EntryData entry, VerificationResult result)
    {
        string key = Key(path, entry, "verified");
        lock (_publicationLock)
        {
            var value = new MetadataValue(MetadataState.Available, result);
            if (!_cache.TryGetValue(key, out var before) || before != value) RetireSnapshots("verified", key, null);
            foreach (var production in _productions)
                if (production.Key == key) production.Current = false;
            if (_cache.TryAdd(key, value)) _order.Enqueue(key);
            else _cache[key] = value;
            _producedAt.TryRemove(key, out _);
            while (_cache.Count > _maxCache && _order.TryDequeue(out var old))
            {
                _cache.TryRemove(old, out _);
                _producedAt.TryRemove(old, out _);
            }
        }
        Notify();
    }

    private static string? KeyPath(string key)
    {
        int at = -1;
        for (int i = 0; i < 3; i++)
            if ((at = key.IndexOf('|', at + 1)) < 0) return null;
        return key[(at + 1)..];
    }

    private void Forget(string fieldId, Func<string, bool> matches)
    {
        lock (_publicationLock)
        {
            RetireSnapshots(fieldId, null, null);
            foreach (var production in _productions)
                if (matches(production.Key)) production.Current = false;
            foreach (string key in _cache.Keys)
                if (matches(key))
                {
                    _cache.TryRemove(key, out _);
                    _producedAt.TryRemove(key, out _);
                }
            // Retire the ordering slots too: repeatedly forgetting one field must not accumulate old path keys.
            // Publication holds this same lock, so one pass preserves the order of every unrelated cached value.
            int ordered = _order.Count;
            while (ordered-- > 0 && _order.TryDequeue(out var old))
                if (!matches(old)) _order.Enqueue(old);
        }
        Notify();
    }

    /// <summary>Forgets every value (after a refresh or a change FileCat made, such as new permissions); shown rows ask again.</summary>
    public void Invalidate()
    {
        lock (_publicationLock)
        {
            RetireSnapshots(null, null, null);
            foreach (var production in _productions) production.Current = false;
            _cache.Clear();
            _producedAt.Clear();
            _order.Clear();
        }
        Notify();
    }
}

/// <summary>First-party fields with bounded, managed parsing (no native parsers in-process; plan §6.2).</summary>
public static class BuiltInFields
{
    private static readonly HashSet<string> VersionExt = new(StringComparer.OrdinalIgnoreCase) { "exe", "dll", "sys", "ocx", "cpl", "scr", "msi", "mui", "winmd" };
    private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase) { "png", "jpg", "jpeg", "gif", "bmp", "webp", "ico" };

    public static readonly MetadataField Version = new("version", "Version", MetadataCost.Cheap,
        n => VersionExt.Contains(NameParts.GetExtension(n)),
        (p, _) =>
        {
            if (!OperatingSystem.IsWindows()) return null;
            var vi = FileVersionInfo.GetVersionInfo(p);
            var v = vi.FileVersion?.Split(' ')[0];
            return string.IsNullOrWhiteSpace(v) ? null : v;
        },
        v => v as string ?? string.Empty,
        SortKey: v => v is string s && System.Version.TryParse(s, out var parsed) ? parsed : null);

    public static readonly MetadataField Dimensions = new("dimensions", "Dimensions", MetadataCost.Expensive,
        n => ImageExt.Contains(NameParts.GetExtension(n)),
        (p, ct) => ImageHeader.TryRead(p, ct),
        v => v is (int w, int h) ? $"{w} × {h}" : string.Empty,
        RightAlign: true,
        SortKey: v => v is (int w, int h) ? (long)w * h : null);

    public static readonly MetadataField LinkTarget = new("linkTarget", "Link target", MetadataCost.Cheap,
        _ => true,
        (p, _) => new FileInfo(p).LinkTarget,
        v => v as string ?? string.Empty,
        Folders: true);

    /// <summary>Linux and macOS: permissions as ls prints them ("rwxr-xr-x"), for files and folders.</summary>
    public static readonly MetadataField Permissions = new("permissions", "Permissions", MetadataCost.Cheap,
        _ => true,
        (p, _) => UnixPermissions.Stat(p)?.Mode,
        v => v is UnixFileMode m ? UnixPermissions.Format(m) : string.Empty,
        SortKey: v => v is UnixFileMode m ? (int)m : null,
        Folders: true, RefreshAfter: TimeSpan.FromSeconds(5));

    public static readonly MetadataField Owner = new("owner", "Owner", MetadataCost.Cheap,
        _ => true,
        (p, _) => UnixPermissions.Stat(p) is { } s ? UnixPermissions.UserName(s.Uid) : null,
        v => v as string ?? string.Empty,
        Folders: true, RefreshAfter: TimeSpan.FromSeconds(5));

    public static readonly MetadataField Group = new("group", "Group", MetadataCost.Cheap,
        _ => true,
        (p, _) => UnixPermissions.Stat(p) is { } s ? UnixPermissions.GroupName(s.Gid) : null,
        v => v as string ?? string.Empty,
        Folders: true, RefreshAfter: TimeSpan.FromSeconds(5));

    /// <summary>Where a file came from: Windows zones; the quarantine mark on macOS; a browser's origin URL on Linux.</summary>
    public static readonly MetadataField Zone = new("zone", "Origin", MetadataCost.Cheap,
        _ => true,
        (p, ct) =>
        {
            if (OperatingSystem.IsMacOS()) return Xattr.Get(p, UnixFileOperations.QuarantineAttribute) is { Length: > 0 } ? "Quarantined" : null;
            if (OperatingSystem.IsLinux()) return Xattr.Get(p, UnixFileOperations.OriginAttribute) is { Length: > 0 } ? "Internet" : null;
            var ads = p + ":Zone.Identifier";
            const int maxMarkBytes = 64 * 1024;
            FileStream stream;
            try { stream = new FileStream(ads, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
            catch (FileNotFoundException) { return null; }
            using (stream)
            {
                // Origin is automatic visible-row metadata. A single newline-free ADS must not allocate
                // in proportion to its size, and a growing stream must not bypass the size precheck.
                long length = stream.Length;
                if (length > maxMarkBytes) return "Marked";
                // Ordinary marks are tiny; reserve only their observed size plus a growth sentinel.
                // A concurrent writer can still grow the stream, so keep the actual read bounded.
                var bytes = new byte[(int)Math.Min(maxMarkBytes + 1L, Math.Max(256L, length + 1))];
                int read = 0;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count == 0) break;
                    read += count;
                    if (read == bytes.Length)
                    {
                        if (bytes.Length == maxMarkBytes + 1) return "Marked";
                        Array.Resize(ref bytes, maxMarkBytes + 1);
                    }
                }
                using var reader = new StreamReader(new MemoryStream(bytes, 0, read, writable: false),
                    System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    ct.ThrowIfCancellationRequested();
                    if (line.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.AsSpan(7), out var zone))
                        return zone switch { 3 => "Internet", 4 => "Restricted", 2 => "Trusted", 1 => "Intranet", 0 => "Local", _ => $"Zone {zone}" };
                }
            }
            return "Marked";
        },
        v => v as string ?? string.Empty);

    /// <summary>Where the column reads streams and attributes from: the platform's (set at start).</summary>
    public static HiddenData.IHiddenData? HiddenDataSource { get; set; }

    /// <summary>
    /// How many streams and attributes a file or folder carries beside its contents (D-55): alternate data streams and
    /// NTFS attributes on Windows, extended attributes on Linux and macOS. Alt+Shift+Enter lists them.
    /// </summary>
    public static readonly MetadataField Hidden = new("hidden", OperatingSystem.IsWindows() ? "Streams" : "Attributes", MetadataCost.Cheap,
        _ => true,
        (p, _) => HiddenDataSource is { IsSupported: true } source ? source.List(p).Count : null,
        v => v is int n && n > 0 ? n.ToString(System.Globalization.CultureInfo.CurrentCulture) : string.Empty,
        RightAlign: true, SortKey: v => v as int?, Folders: true);

    /// <summary>
    /// Whether the file matches the checksums and signatures beside it (D-57): empty when nothing covers it, "not checked"
    /// for large files and files on the network (checked on request), and a checksum file's own row says what it covers.
    /// </summary>
    public static readonly MetadataField Verified = new("verified", "Verified", MetadataCost.Cheap,
        _ => true,
        (p, ct) => Verification.VerificationService.Current?.Automatic(p, ct),
        v => v is Verification.VerificationResult r ? r.Text : string.Empty,
        SortKey: v => v is Verification.VerificationResult r ? (int)r.State : null);

    /// <summary>The fields this OS can fill: file versions on Windows; permissions and ownership on Linux and macOS.</summary>
    public static IReadOnlyList<MetadataField> All { get; } =
        OperatingSystem.IsWindows() ? [Version, Dimensions, LinkTarget, Zone, Hidden, Verified] : [Permissions, Owner, Group, Dimensions, LinkTarget, Zone, Hidden, Verified];
}

/// <summary>Reads image dimensions from bounded headers (never decodes pixels).</summary>
public static class ImageHeader
{
    public static (int Width, int Height)? TryRead(string path, CancellationToken ct)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.RandomAccess);
        var head = new byte[64 * 1024];
        int n = fs.Read(head, 0, head.Length);
        return Parse(head.AsSpan(0, n), ct);
    }

    public static (int Width, int Height)? Parse(ReadOnlySpan<byte> b, CancellationToken ct = default)
    {
        if (b.Length >= 24 && b[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return (BinaryPrimitives.ReadInt32BigEndian(b[16..]), BinaryPrimitives.ReadInt32BigEndian(b[20..]));
        if (b.Length >= 10 && b[0] == 'G' && b[1] == 'I' && b[2] == 'F')
            return (BinaryPrimitives.ReadUInt16LittleEndian(b[6..]), BinaryPrimitives.ReadUInt16LittleEndian(b[8..]));
        if (b.Length >= 26 && b[0] == 'B' && b[1] == 'M')
            return (BinaryPrimitives.ReadInt32LittleEndian(b[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(b[22..])));
        if (b.Length >= 30 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8))
        {
            var chunk = b[12..16];
            if (chunk.SequenceEqual("VP8 "u8) && b.Length >= 30) return (BinaryPrimitives.ReadUInt16LittleEndian(b[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(b[28..]) & 0x3FFF);
            if (chunk.SequenceEqual("VP8L"u8) && b.Length >= 25)
            {
                uint bits = BinaryPrimitives.ReadUInt32LittleEndian(b[21..]);
                return ((int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
            }
            if (chunk.SequenceEqual("VP8X"u8) && b.Length >= 30)
                return (1 + (b[24] | b[25] << 8 | b[26] << 16), 1 + (b[27] | b[28] << 8 | b[29] << 16));
        }
        if (b.Length >= 8 && b[0] == 0 && b[1] == 0 && b[2] == 1 && b[3] == 0)
            return (b[6] == 0 ? 256 : b[6], b[7] == 0 ? 256 : b[7]); // ICO: first image
        if (b.Length >= 4 && b[0] == 0xFF && b[1] == 0xD8)
        {
            int i = 2;
            while (i + 9 < b.Length)
            {
                ct.ThrowIfCancellationRequested();
                if (b[i] != 0xFF) { i++; continue; }
                byte marker = b[i + 1];
                if (marker is 0xD8 or 0x01 || marker is >= 0xD0 and <= 0xD7) { i += 2; continue; }
                int len = BinaryPrimitives.ReadUInt16BigEndian(b[(i + 2)..]);
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return (BinaryPrimitives.ReadUInt16BigEndian(b[(i + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(b[(i + 5)..]));
                i += 2 + len;
            }
            return null;
        }
        return null;
    }
}
