using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;
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
}

public readonly record struct MetadataValue(MetadataState State, object? Value = null, string? Detail = null)
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
    TimeSpan? RefreshAfter = null);

/// <summary>
/// Demand-driven metadata (plan §10): values are produced for rows the user can see, bounded per device,
/// coalesced, cached by path + revision + field, and requests for rows scrolled away are dropped. Zero or
/// empty text is a real value; "pending", "absent", and "unsupported" are states, never fabricated values.
/// </summary>
public sealed class MetadataService
{
    private const int MaxCache = 50_000;
    private const int MaxOutstandingPerDevice = 4;
    private readonly DeviceIoScheduler _io;
    private readonly ConcurrentDictionary<string, MetadataValue> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _producedAt = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _outstanding = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MetadataField> _fields = new(StringComparer.Ordinal);
    private int _notifyPending;

    public MetadataService(DeviceIoScheduler io)
    {
        _io = io;
        foreach (var f in BuiltInFields.All) _fields[f.Id] = f;
    }

    /// <summary>Raised (throttled, on a pool thread) when new values are available.</summary>
    public event Action? ValuesChanged;

    public IReadOnlyCollection<MetadataField> Fields => _fields.Values;

    public MetadataField? Field(string id) => _fields.GetValueOrDefault(id);

    public void Register(MetadataField field) => _fields[field.Id] = field;

    private static string Key(string path, in EntryData e, string fieldId) => $"{fieldId}|{e.Modified}|{e.Size}|{path}";

    /// <summary>
    /// Returns the cached value or schedules its production. <paramref name="demandEpoch"/> identifies the
    /// caller's current viewport; <paramref name="isStillWanted"/> lets queued work be skipped once rows scroll away.
    /// </summary>
    public MetadataValue Get(string fieldId, string path, in EntryData entry, string deviceKey, bool slowLocation, Func<bool>? isStillWanted = null)
    {
        if (!_fields.TryGetValue(fieldId, out var field)) return new MetadataValue(MetadataState.Unsupported, null, "unknown field");
        if (!Applies(field, entry)) return MetadataValue.Absent;
        if (entry.Has(EntryFlags.Offline)) return new MetadataValue(MetadataState.Unsupported, null, "not read from cloud placeholders");
        // Expensive columns never run automatically on network or removable locations (plan §10).
        if (slowLocation && field.Cost >= MetadataCost.Expensive) return new MetadataValue(MetadataState.Unsupported, null, "not computed on slow locations");
        var key = Key(path, entry, fieldId);
        if (_cache.TryGetValue(key, out var v))
        {
            // Read again when due; the old value stays on screen until the new one arrives.
            if (field.RefreshAfter is { } refresh && _producedAt.TryGetValue(key, out var at) && Environment.TickCount64 - at > refresh.TotalMilliseconds)
                Schedule(key, field, path, deviceKey, isStillWanted);
            return v;
        }
        Schedule(key, field, path, deviceKey, isStillWanted);
        return MetadataValue.Pending;
    }

    private static bool Applies(MetadataField field, in EntryData entry) =>
        (entry.Kind == EntryKind.File || entry.Kind == EntryKind.Directory && field.Folders) && field.AppliesToName(entry.Name);

    /// <summary>Synchronous production for explicit analysis jobs (background threads only).</summary>
    public MetadataValue Compute(string fieldId, string path, in EntryData entry, CancellationToken ct)
    {
        if (!_fields.TryGetValue(fieldId, out var field) || !Applies(field, entry)) return MetadataValue.Absent;
        var key = Key(path, entry, fieldId);
        if (_cache.TryGetValue(key, out var v) && v.State is MetadataState.Available or MetadataState.Absent) return v;
        var value = Produce(field, path, ct);
        Store(key, value);
        return value;
    }

    private void Schedule(string key, MetadataField field, string path, string deviceKey, Func<bool>? isStillWanted)
    {
        if (!_inFlight.TryAdd(key, 0)) return;
        int outstanding = _outstanding.AddOrUpdate(deviceKey, 1, (_, n) => n + 1);
        if (outstanding > MaxOutstandingPerDevice * 64)
        {
            // Backlog guard: drop instead of growing unbounded; the row asks again while visible.
            _outstanding.AddOrUpdate(deviceKey, 0, (_, n) => n - 1);
            _inFlight.TryRemove(key, out _);
            return;
        }
        _ = _io.Run(deviceKey, IoPriority.Background, ct =>
        {
            try
            {
                if (isStillWanted is not null && !isStillWanted()) return;
                var value = Produce(field, path, ct);
                bool changed = !_cache.TryGetValue(key, out var old) || !old.Equals(value);
                Store(key, value);
                if (field.RefreshAfter is not null) _producedAt[key] = Environment.TickCount64;
                if (changed) Notify();
            }
            finally
            {
                _inFlight.TryRemove(key, out _);
                _outstanding.AddOrUpdate(deviceKey, 0, (_, n) => Math.Max(0, n - 1));
            }
        });
    }

    private static MetadataValue Produce(MetadataField field, string path, CancellationToken ct)
    {
        try
        {
            var value = field.Produce(path, ct);
            return value is null ? MetadataValue.Absent : new MetadataValue(MetadataState.Available, value);
        }
        catch (OperationCanceledException)
        {
            return new MetadataValue(MetadataState.NotRequested);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or FormatException or ArgumentException)
        {
            return new MetadataValue(MetadataState.Failed, null, ex.Message);
        }
    }

    private void Store(string key, MetadataValue value)
    {
        if (value.State == MetadataState.NotRequested) return;
        if (_cache.TryAdd(key, value)) _order.Enqueue(key); // a value read again keeps its place
        else _cache[key] = value;
        while (_cache.Count > MaxCache && _order.TryDequeue(out var old))
        {
            _cache.TryRemove(old, out _);
            _producedAt.TryRemove(old, out _);
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

    /// <summary>Forgets one field's values for one item (asked again when its row is drawn).</summary>
    public void Forget(string fieldId, string path)
    {
        string prefix = fieldId + "|", suffix = "|" + path;
        foreach (string key in _cache.Keys)
            if (key.StartsWith(prefix, StringComparison.Ordinal) && key.EndsWith(suffix, StringComparison.Ordinal))
            {
                _cache.TryRemove(key, out _);
                _producedAt.TryRemove(key, out _);
            }
        Notify();
    }

    /// <summary>Forgets every value (after a refresh or a change FileCat made, such as new permissions); shown rows ask again.</summary>
    public void Invalidate()
    {
        _cache.Clear();
        _producedAt.Clear();
        _order.Clear();
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
        v => v as string ?? string.Empty);

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
        (p, _) =>
        {
            if (OperatingSystem.IsMacOS()) return Xattr.Get(p, UnixFileOperations.QuarantineAttribute) is { Length: > 0 } ? "Quarantined" : null;
            if (OperatingSystem.IsLinux()) return Xattr.Get(p, UnixFileOperations.OriginAttribute) is { Length: > 0 } ? "Internet" : null;
            var ads = p + ":Zone.Identifier";
            if (!File.Exists(ads)) return null;
            foreach (var line in File.ReadLines(ads))
            {
                if (line.StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.AsSpan(7), out var zone))
                    return zone switch { 3 => "Internet", 4 => "Restricted", 2 => "Trusted", 1 => "Intranet", 0 => "Local", _ => $"Zone {zone}" };
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
