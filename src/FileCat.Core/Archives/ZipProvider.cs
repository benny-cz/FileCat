using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Archives;

/// <summary>Archive member payload for listings.</summary>
public sealed record ZipMemberTag(string FullName, int EntryIndex, long CompressedSize, uint Crc, bool Encrypted, int DuplicateOrdinal, string? UnsafeReason);

/// <summary>
/// Read-only ZIP browsing and extraction (plan §15, D-39) with in-box System.IO.Compression, parsed in-process
/// under enforced limits on entry count, expanded size, expansion ratio, and time. Members are shown as archive
/// members, not files; duplicate names stay distinct; encrypted entries are listed but not extracted; nested
/// archives open after extraction. Mark-of-the-Web propagates through <see cref="StreamTransferExecutor"/>.
/// </summary>
public sealed class ZipProvider : ResourceProvider, IContainerDetector
{
    public const int MaxEntries = 1_000_000;
    public const long MaxMemberInMemory = 32L * 1024 * 1024;
    public const long MaxExpansionRatio = 1000;
    public const long MaxSpooledMember = 8L * 1024 * 1024 * 1024;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { "zip", "jar", "nupkg", "vsix", "whl", "xpi", "snupkg", "aar" };
    private readonly ConcurrentDictionary<string, ZipIndex> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _tempDirectory;

    public ZipProvider(string tempDirectory)
    {
        _tempDirectory = tempDirectory;
        TextDecoding.EnsureCodePages();
    }

    public override string Scheme => Schemes.Zip;

    // ---- IContainerDetector ------------------------------------------------------------------------------

    public bool IsContainer(string fileName) => Extensions.Contains(NameParts.GetExtension(fileName));

    public Location? GetContainerLocation(string filePath) => new(Schemes.Zip, string.Empty, Location.FileSystem(filePath));

    /// <summary>Opens any file as ZIP (Ctrl+PgDn on e.g. .docx or .apk).</summary>
    public static Location ForFile(string filePath, string? encoding = null) => new(Schemes.Zip, string.Empty, Location.FileSystem(filePath), encoding);

    // ---- Navigation --------------------------------------------------------------------------------------

    private static string ZipPath(Location l) => l.Container?.Path ?? throw new InvalidOperationException("ZIP location without its archive.");

    public override string GetDisplayPath(Location location)
    {
        var inner = location.Path.Replace('/', Path.DirectorySeparatorChar);
        return inner.Length == 0 ? ZipPath(location) : Path.Combine(ZipPath(location), inner);
    }

    public override string GetDisplayName(Location location) =>
        location.Path.Length == 0 ? Path.GetFileName(ZipPath(location)) : location.Path[(location.Path.LastIndexOf('/') + 1)..];

    public override Location? GetParent(Location location)
    {
        if (location.Path.Length == 0) return Location.FileSystem(Path.GetDirectoryName(ZipPath(location)) ?? ZipPath(location));
        int slash = location.Path.LastIndexOf('/');
        return location.WithPath(slash < 0 ? string.Empty : location.Path[..slash]);
    }

    public override string? GetNameInParent(Location location) =>
        location.Path.Length == 0 ? Path.GetFileName(ZipPath(location)) : location.Path[(location.Path.LastIndexOf('/') + 1)..];

    public override string GetDeviceKey(Location location) => PathUtil.GetDeviceKey(ZipPath(location));

    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        LocationCapabilities.CreateDirectory or LocationCapabilities.CreateFile or LocationCapabilities.Delete or LocationCapabilities.Rename
            or LocationCapabilities.TransferTarget or LocationCapabilities.MoveSource or LocationCapabilities.Recycle =>
            "ZIP archives are read-only in this version. Extract items with F5, then change them in a folder.",
        LocationCapabilities.ExternalEdit => "Editing inside archives needs an explicit edit session (planned). Extract the item with F5 to edit it.",
        _ => base.ExplainUnavailable(location, capability),
    };

    public override bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        var t = PathUtil.ExpandUserInput(text);
        if (!Path.IsPathFullyQualified(t)) return false;
        // Walk up to an existing archive file: "C:\x\a.zip\dir\sub" opens "dir/sub" inside a.zip.
        var probe = t.TrimEnd('\\', '/');
        var rest = new Stack<string>();
        while (!string.IsNullOrEmpty(probe))
        {
            if (File.Exists(probe) && IsContainer(Path.GetFileName(probe)))
            {
                location = new Location(Schemes.Zip, string.Join('/', rest), Location.FileSystem(probe));
                return true;
            }
            if (Directory.Exists(probe)) return false;
            rest.Push(Path.GetFileName(probe));
            probe = Path.GetDirectoryName(probe) ?? string.Empty;
        }
        return false;
    }

    public override Task EnumerateAsync(Location location, IEnumerationSink sink, CancellationToken ct)
    {
        var index = GetIndex(location);
        if (!index.Children.TryGetValue(location.Path, out var children))
            throw new DirectoryNotFoundException($"\"{location.Path}\" does not exist in this archive.");
        var batch = new List<EntryData>(children.Count);
        foreach (var node in children)
        {
            ct.ThrowIfCancellationRequested();
            var e = new EntryData(node.Name, node.IsDirectory ? EntryKind.Directory : EntryKind.File, node.IsDirectory ? -1 : node.Size, node.Modified)
            {
                Tag = node.Tag,
                Flags = (node.Tag?.Encrypted == true ? EntryFlags.Protected : EntryFlags.None) |
                        (node.Tag?.UnsafeReason is not null ? EntryFlags.Unavailable : EntryFlags.None) |
                        (!node.IsDirectory && IsContainer(node.Name) ? EntryFlags.Container : EntryFlags.None),
            };
            batch.Add(e);
        }
        if (index.Warning is not null) sink.ReportIssue(index.Warning);
        sink.AddBatch(batch.ToArray());
        return Task.CompletedTask;
    }

    public override Location? GetChildLocation(Location parent, in EntryData entry)
    {
        if (entry.Kind == EntryKind.Parent) return GetParent(parent);
        if (entry.Kind == EntryKind.Directory) return parent.WithPath(parent.Path.Length == 0 ? entry.Name : parent.Path + "/" + entry.Name);
        return null; // nested archives open after extraction (plan §15)
    }

    public override ItemRef GetItemRef(Location listing, in EntryData entry) =>
        entry.Tag is ZipMemberTag { DuplicateOrdinal: > 0 } t
            ? new ItemRef(listing, entry.Name, entry.Kind, entry.Size, entry.Modified) { Ordinal = t.DuplicateOrdinal }
            : base.GetItemRef(listing, entry);

    // ---- Content -----------------------------------------------------------------------------------------

    public override IContentSource? OpenContent(ItemRef item)
    {
        var index = GetIndex(item.Parent);
        var name = item.Name;
        int ordinal = item.Ordinal;
        var full = item.Parent.Path.Length == 0 ? name : item.Parent.Path + "/" + name;
        var node = index.Children.GetValueOrDefault(item.Parent.Path)?.FirstOrDefault(n => n.Name == name && !n.IsDirectory && (n.Tag?.DuplicateOrdinal ?? 0) == ordinal);
        if (node?.Tag is not { } tag) throw new FileNotFoundException($"\"{full}\" is not in the archive.");
        if (tag.Encrypted) return null;
        if (tag.UnsafeReason is not null) throw new InvalidDataException("This member has an unsafe name: " + tag.UnsafeReason);
        return index.Extract(tag, _tempDirectory);
    }

    // ---- Index ---------------------------------------------------------------------------------------------

    private ZipIndex GetIndex(Location location)
    {
        var path = ZipPath(location);
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException("The archive no longer exists.", path);
        var key = $"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{location.Session}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        // The archive changed or was never opened: drop stale indexes for this path.
        foreach (var k in _cache.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (_cache.TryRemove(k, out var old)) old.Dispose();
        }
        var index = ZipIndex.Build(path, location.Session is { Length: > 0 } enc ? Encoding.GetEncoding(enc) : null);
        _cache[key] = index;
        while (_cache.Count > 8)
        {
            var oldest = _cache.OrderBy(kv => kv.Value.LastUsed).First();
            if (_cache.TryRemove(oldest.Key, out var evicted)) evicted.Dispose();
        }
        return index;
    }

    public void Release(string zipPath)
    {
        foreach (var k in _cache.Keys.Where(k => k.StartsWith(zipPath + "|", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            if (_cache.TryRemove(k, out var old)) old.Dispose();
        }
    }
}

internal sealed class ZipIndex : IDisposable
{
    private readonly object _lock = new();
    private readonly FileStream _stream;
    private readonly ZipArchive _archive;
    private readonly List<ZipArchiveEntry> _entries;

    public sealed record Node(string Name, bool IsDirectory, long Size, long Modified, ZipMemberTag? Tag);

    private ZipIndex(FileStream stream, ZipArchive archive, List<ZipArchiveEntry> entries, Dictionary<string, List<Node>> children, string? warning)
    {
        _stream = stream;
        _archive = archive;
        _entries = entries;
        Children = children;
        Warning = warning;
        LastUsed = DateTime.UtcNow;
    }

    public Dictionary<string, List<Node>> Children { get; }
    public string? Warning { get; }
    public DateTime LastUsed { get; private set; }

    public static ZipIndex Build(string path, Encoding? nameEncoding)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: nameEncoding);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
        var entries = new List<ZipArchiveEntry>();
        var children = new Dictionary<string, List<Node>>(StringComparer.Ordinal) { [string.Empty] = [] };
        var dirs = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        long declaredTotal = 0;
        string? warning = null;
        void EnsureDir(string dir)
        {
            if (!dirs.Add(dir)) return;
            int slash = dir.LastIndexOf('/');
            var parent = slash < 0 ? string.Empty : dir[..slash];
            EnsureDir(parent);
            if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
            list.Add(new Node(slash < 0 ? dir : dir[(slash + 1)..], true, -1, 0, null));
            children.TryAdd(dir, []);
        }
        int i = 0;
        foreach (var entry in archive.Entries)
        {
            if (i >= ZipProvider.MaxEntries)
            {
                warning = $"The archive has more than {ZipProvider.MaxEntries:N0} entries; only the first ones are listed.";
                break;
            }
            entries.Add(entry);
            var raw = entry.FullName.Replace('\\', '/');
            string? unsafeReason = null;
            if (raw.StartsWith('/') || raw.Length > 1 && raw[1] == ':') unsafeReason = "absolute path";
            var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Any(p => p is "." or "..")) unsafeReason = "\"..\" path traversal";
            parts.RemoveAll(p => p is "." or "..");
            if (parts.Count == 0)
            {
                i++;
                continue;
            }
            bool isDir = raw.EndsWith('/');
            var parent = string.Join('/', parts.Take(parts.Count - 1));
            EnsureDir(parent);
            if (isDir)
            {
                EnsureDir(string.Join('/', parts));
            }
            else
            {
                var name = parts[^1];
                var key = parent + "/" + name;
                int ordinal = seen.TryGetValue(key, out var n) ? n + 1 : 0;
                seen[key] = ordinal;
                declaredTotal += Math.Max(0, entry.Length);
                var tag = new ZipMemberTag(entry.FullName, i, entry.CompressedLength, entry.Crc32, entry.IsEncrypted, ordinal, unsafeReason);
                long modified;
                try { modified = entry.LastWriteTime.UtcDateTime.Ticks; }
                catch (ArgumentOutOfRangeException) { modified = 0; }
                children[parent].Add(new Node(name, false, entry.Length, modified, tag));
            }
            i++;
        }
        if (seen.Values.Any(v => v > 0))
            warning ??= "The archive contains duplicate names; each copy is listed separately.";
        return new ZipIndex(fs, archive, entries, children, warning);
    }

    /// <summary>Extracts one member with expansion limits; small members stay in memory, larger ones spool privately.</summary>
    public IContentSource Extract(ZipMemberTag tag, string tempDirectory)
    {
        LastUsed = DateTime.UtcNow;
        lock (_lock)
        {
            var entry = _entries[tag.EntryIndex];
            long declared = Math.Max(0, entry.Length);
            long compressed = Math.Max(1, entry.CompressedLength);
            // Declared sizes are untrusted: enforce the ratio and a hard cap on what is actually produced.
            long cap = Math.Min(ZipProvider.MaxSpooledMember, Math.Max(declared + 1024 * 1024, 0));
            using var src = entry.Open();
            Stream dst;
            string? spoolPath = null;
            if (declared <= ZipProvider.MaxMemberInMemory) dst = new MemoryStream((int)declared);
            else
            {
                Directory.CreateDirectory(tempDirectory);
                spoolPath = Path.Combine(tempDirectory, $"zipmember-{Guid.NewGuid():N}.tmp");
                dst = new FileStream(spoolPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete, 1, FileOptions.DeleteOnClose);
            }
            var buffer = new byte[256 * 1024];
            long total = 0;
            int n;
            try
            {
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += n;
                    if (total > cap) throw new InvalidDataException("The member expands beyond its declared size; extraction was stopped.");
                    if (total > 64L * 1024 * 1024 && total / compressed > ZipProvider.MaxExpansionRatio)
                        throw new InvalidDataException("The member exceeds the expansion-ratio limit (possible decompression bomb); extraction was stopped.");
                    dst.Write(buffer, 0, n);
                }
            }
            catch
            {
                dst.Dispose();
                throw;
            }
            if (dst is MemoryStream ms) return new MemoryContentSource(tag.FullName, ms.ToArray());
            dst.Position = 0;
            return new StreamContentSource(tag.FullName, (FileStream)dst);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _archive.Dispose();
            _stream.Dispose();
        }
    }
}

/// <summary>Random access over a private spool file that is deleted when disposed.</summary>
public sealed class StreamContentSource(string displayName, FileStream stream) : IContentSource
{
    private readonly object _lock = new();

    public string DisplayName { get; } = displayName;
    public long Length => stream.Length;
    public bool CanSeek => true;
    public string? LocalPath => null;

    public int Read(long offset, Span<byte> buffer)
    {
        lock (_lock)
        {
            if (offset >= stream.Length) return 0;
            stream.Position = offset;
            return stream.Read(buffer);
        }
    }

    public ContentRevision? GetRevision() => new ContentRevision(stream.Length, 0);

    public void Dispose() => stream.Dispose();
}
