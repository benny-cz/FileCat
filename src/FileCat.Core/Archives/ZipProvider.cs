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
/// ZIP browsing, extraction, and (with <see cref="ZipUpdateExecutor"/>) updates (plan §15, D-39) with in-box
/// System.IO.Compression, parsed in-process under enforced limits on entry count, expanded size, expansion ratio, and time.
/// Members are shown as archive members, not files; duplicate names stay distinct; encrypted entries are listed but not
/// extracted; archives inside archives open read-only from a private spool. Mark-of-the-Web propagates through
/// <see cref="StreamTransferExecutor"/> from the outermost marked file.
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

    /// <summary>Other archive formats found inside ZIPs (P8): flagged as containers and opened by their provider.</summary>
    public IContainerDetector? OtherArchives { get; set; }

    /// <summary>A private local copy of a member of another archive format, for a ZIP stored inside it.</summary>
    public Func<Location, string>? SpoolForeignMember { get; set; }

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

    /// <summary>
    /// The local file of the archive a location is inside. For an archive nested in an archive, the location's container
    /// is the member location in the outer archive, and the inner archive is read from a private spool (plan §15).
    /// </summary>
    private string ZipPath(Location l) => ArchiveFile(l, 0);

    /// <summary>What users see: "C:\x\outer.zip\dir\inner.zip\sub", never a spool path.</summary>
    private static string DisplayArchive(Location l)
    {
        var container = l.Container ?? throw new InvalidOperationException("ZIP location without its archive.");
        if (container.IsFileSystem) return PathUtil.WithUpperDrive(container.Path);
        return Path.Combine(DisplayArchive(container), container.Path.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>The file on disk that ultimately holds the (possibly nested) archive.</summary>
    private static string OutermostFile(Location l)
    {
        var c = l.Container;
        while (c is not null && !c.IsFileSystem) c = c.Container;
        return c?.Path ?? throw new InvalidOperationException("ZIP location without its archive.");
    }

    public override string GetDisplayPath(Location location)
    {
        var inner = location.Path.Replace('/', Path.DirectorySeparatorChar);
        return inner.Length == 0 ? DisplayArchive(location) : Path.Combine(DisplayArchive(location), inner);
    }

    public override string GetDisplayName(Location location) =>
        location.Path.Length == 0 ? Path.GetFileName(DisplayArchive(location)) : location.Path[(location.Path.LastIndexOf('/') + 1)..];

    public override Location? GetParent(Location location)
    {
        if (location.Path.Length == 0)
        {
            var container = location.Container!;
            if (container.IsFileSystem) return Location.FileSystem(Path.GetDirectoryName(container.Path) ?? container.Path);
            // The folder of the outer archive that holds this inner archive.
            int at = container.Path.LastIndexOf('/');
            return container.WithPath(at < 0 ? string.Empty : container.Path[..at]);
        }
        int slash = location.Path.LastIndexOf('/');
        return location.WithPath(slash < 0 ? string.Empty : location.Path[..slash]);
    }

    public override string? GetNameInParent(Location location) =>
        location.Path.Length == 0 ? Path.GetFileName(DisplayArchive(location)) : location.Path[(location.Path.LastIndexOf('/') + 1)..];

    public override string GetDeviceKey(Location location) => PathUtil.GetDeviceKey(OutermostFile(location));

    // ---- Nested archives ------------------------------------------------------------------------------------

    public const int MaxNestedSpools = 4;
    public const int MaxNestingDepth = 8;
    private readonly ConcurrentDictionary<string, NestedSpool> _nested = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _nestedLock = new();

    /// <summary>An inner archive spooled privately; the file is deleted when the holder closes (also after a crash).</summary>
    private sealed class NestedSpool(string path, FileStream holder) : IDisposable
    {
        public string Path { get; } = path;
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        public void Dispose() => holder.Dispose();
    }

    private string ArchiveFile(Location location, int depth)
    {
        var container = location.Container ?? throw new InvalidOperationException("ZIP location without its archive.");
        if (container.IsFileSystem) return container.Path;
        if (container.Scheme != Schemes.Zip)
            return SpoolForeignMember?.Invoke(container) ?? throw new NotSupportedException("This archive is inside a location FileCat cannot read archives from.");
        if (depth >= MaxNestingDepth) throw new InvalidDataException($"Archives are nested more than {MaxNestingDepth} levels deep.");
        string outer = ArchiveFile(container, depth + 1);
        var info = new FileInfo(outer);
        string key = $"{outer}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{container.Path}";
        lock (_nestedLock)
        {
            if (_nested.TryGetValue(key, out var cached))
            {
                cached.LastUsed = DateTime.UtcNow;
                return cached.Path;
            }
            int slash = container.Path.LastIndexOf('/');
            var folder = container.WithPath(slash < 0 ? string.Empty : container.Path[..slash]);
            string name = container.Path[(slash + 1)..];
            var index = GetIndex(folder);
            var node = index.Children.GetValueOrDefault(folder.Path)?.Where(n => !n.IsDirectory && n.Name == name).ToList() ?? [];
            if (node.Count == 0) throw new FileNotFoundException($"\"{container.Path}\" is not in the archive.");
            if (node.Count > 1) throw new NotSupportedException("Several members share this name; extract the one you want with F5, then open it.");
            var tag = node[0].Tag!;
            if (tag.Encrypted) throw new NotSupportedException("The inner archive is encrypted and cannot be opened here.");
            if (tag.UnsafeReason is not null) throw new InvalidDataException("The inner archive has an unsafe name: " + tag.UnsafeReason);
            Directory.CreateDirectory(_tempDirectory);
            string spoolPath = Path.Combine(_tempDirectory, $"nested-{Guid.NewGuid():N}.zip");
            var holder = new FileStream(spoolPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.DeleteOnClose);
            try { index.CopyMember(tag, holder); }
            catch { holder.Dispose(); throw; }
            _nested[key] = new NestedSpool(spoolPath, holder);
            while (_nested.Count > MaxNestedSpools)
            {
                var oldest = _nested.OrderBy(kv => kv.Value.LastUsed).First();
                if (!_nested.TryRemove(oldest.Key, out var evicted)) break;
                Release(evicted.Path);
                evicted.Dispose();
            }
            return spoolPath;
        }
    }

    /// <summary>
    /// What changes a location allows. The answer comes from the already opened index (no I/O on a keypress): an archive
    /// in a folder can be updated unless it is read-only or has encrypted members, which a rebuild cannot re-create.
    /// </summary>
    public override LocationCapabilities GetCapabilities(Location location)
    {
        var caps = LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;
        if (WhyReadOnly(location) is null)
            caps |= LocationCapabilities.CreateDirectory | LocationCapabilities.Delete | LocationCapabilities.Rename |
                    LocationCapabilities.TransferTarget | LocationCapabilities.ExternalEdit;
        return caps;
    }

    /// <summary>How many file members in this folder share the name (duplicates are legal in ZIP and listed separately).</summary>
    public int CopiesOf(Location folder, string name) =>
        GetIndex(folder).Children.TryGetValue(folder.Path, out var list) ? list.Count(n => !n.IsDirectory && n.Name == name) : 0;

    /// <summary>Why the archive cannot be changed here, or null when it can.</summary>
    public string? WhyReadOnly(Location location)
    {
        if (location.Container is not { IsFileSystem: true } container)
            return "Archives inside archives are read-only; extract the inner archive with F5 to change it.";
        var index = _cache.Where(kv => kv.Key.StartsWith(container.Path + "|", StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value).FirstOrDefault();
        if (index is null) return null;
        if (index.ReadOnly) return "The archive file is read-only; clear its read-only attribute to change it.";
        if (index.HasEncrypted) return "The archive has encrypted members, which FileCat cannot re-create, so it cannot be changed here. Extract what you need with F5.";
        return null;
    }

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        LocationCapabilities.CreateDirectory or LocationCapabilities.Delete or LocationCapabilities.Rename or LocationCapabilities.TransferTarget
            or LocationCapabilities.ExternalEdit when WhyReadOnly(location) is { } reason => reason,
        LocationCapabilities.CreateFile => "New empty files cannot be created inside an archive; create the file in a folder and copy it in with F5.",
        LocationCapabilities.MoveSource => "Moving out of an archive is not supported: copy (extract) with F5, then delete the members with F8. Rename a member with F2.",
        LocationCapabilities.Recycle => "Archive members do not go to the Recycle Bin; deleting them rewrites the archive.",
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
                location = Descend(new Location(Schemes.Zip, string.Empty, Location.FileSystem(probe)), rest);
                return true;
            }
            if (Directory.Exists(probe)) return false;
            rest.Push(Path.GetFileName(probe));
            probe = Path.GetDirectoryName(probe) ?? string.Empty;
        }
        return false;
    }

    /// <summary>Walks typed path parts inside an archive, stepping into archive members ("…\outer.zip\inner.zip\dir").</summary>
    private Location Descend(Location archiveRoot, IEnumerable<string> parts)
    {
        var archive = archiveRoot;
        string inner = string.Empty;
        foreach (var part in parts)
        {
            string candidate = inner.Length == 0 ? part : inner + "/" + part;
            if (IsContainer(part))
            {
                try
                {
                    var here = archive.WithPath(inner);
                    if (GetIndex(here).Children.TryGetValue(inner, out var nodes) && nodes.Count(n => !n.IsDirectory && n.Name == part) == 1)
                    {
                        archive = new Location(Schemes.Zip, string.Empty, archive.WithPath(candidate));
                        inner = string.Empty;
                        continue;
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
            }
            inner = candidate;
        }
        return archive.WithPath(inner);
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
                        (!node.IsDirectory && (IsContainer(node.Name) || OtherArchives?.IsContainer(node.Name) == true) ? EntryFlags.Container : EntryFlags.None),
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
        string member = parent.Path.Length == 0 ? entry.Name : parent.Path + "/" + entry.Name;
        if (entry.Kind == EntryKind.Directory) return parent.WithPath(member);
        // An archive inside the archive opens read-only; its container is its member location here (plan §15).
        if (entry.Has(EntryFlags.Container) && entry.Tag is ZipMemberTag { Encrypted: false, UnsafeReason: null, DuplicateOrdinal: 0 })
            return new Location(IsContainer(entry.Name) ? Schemes.Zip : Schemes.Archive, string.Empty, parent.WithPath(member));
        return null;
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
        var node = index.FindFile(item.Parent.Path, name, ordinal);
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

    private ZipIndex(FileStream stream, ZipArchive archive, List<ZipArchiveEntry> entries, Dictionary<string, List<Node>> children, string? warning,
        bool readOnly)
    {
        _stream = stream;
        _archive = archive;
        _entries = entries;
        Children = children;
        Warning = warning;
        ReadOnly = readOnly;
        HasEncrypted = entries.Any(e => e.IsEncrypted);
        LastUsed = DateTime.UtcNow;
    }

    public Dictionary<string, List<Node>> Children { get; }

    private Dictionary<(string Folder, string Name, int Ordinal), Node>? _files;

    /// <summary>
    /// A file member by folder, name, and duplicate ordinal. The lookup is built on first use, so extracting a folder of
    /// many members does not search the folder's list once per member.
    /// </summary>
    public Node? FindFile(string folder, string name, int ordinal) =>
        LazyInitializer.EnsureInitialized(ref _files, () =>
        {
            var files = new Dictionary<(string, string, int), Node>();
            foreach (var (path, nodes) in Children)
                foreach (var node in nodes)
                    if (!node.IsDirectory) files.TryAdd((path, node.Name, node.Tag?.DuplicateOrdinal ?? 0), node);
            return files;
        }).GetValueOrDefault((folder, name, ordinal));

    public string? Warning { get; }
    public DateTime LastUsed { get; private set; }
    /// <summary>The archive file had the read-only attribute when it was opened.</summary>
    public bool ReadOnly { get; }
    public bool HasEncrypted { get; }

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
        bool readOnly;
        try { readOnly = (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0; }
        catch (IOException) { readOnly = false; }
        return new ZipIndex(fs, archive, entries, children, warning, readOnly);
    }

    /// <summary>
    /// Opens one member. Small ones are extracted into memory at once; larger ones are decompressed as they are read
    /// (<see cref="ProgressiveContent"/>), so the first bytes are there at once. Either way the declared size is not
    /// trusted and the member's checksum is verified (.NET does not check it).
    /// </summary>
    public IContentSource Extract(ZipMemberTag tag, string tempDirectory)
    {
        LastUsed = DateTime.UtcNow;
        lock (_lock)
        {
            if (_closing && _leases == 0) throw new IOException("The archive was closed; open it again.");
            var entry = _entries[tag.EntryIndex];
            var limits = Limits(entry);
            if (entry.Length > ZipProvider.MaxMemberInMemory)
            {
                _leases++;
                return new ProgressiveContent(tag.FullName, _lock, entry.Open, limits, ZipProvider.MaxSpooledMember, tempDirectory, Returned);
            }
            var memory = new MemoryStream((int)Math.Max(0, entry.Length));
            using (var source = entry.Open()) limits.CopyAll(source, memory);
            return new MemoryContentSource(tag.FullName, memory.ToArray());
        }
    }

    /// <summary>Writes one member into <paramref name="destination"/> under the same limits (a nested archive's spool).</summary>
    public void CopyMember(ZipMemberTag tag, Stream destination)
    {
        LastUsed = DateTime.UtcNow;
        lock (_lock)
        {
            var entry = _entries[tag.EntryIndex];
            if (entry.Length > ZipProvider.MaxSpooledMember) throw new InvalidDataException("The inner archive is larger than FileCat opens in place; extract it with F5.");
            using (var source = entry.Open()) Limits(entry).CopyAll(source, destination);
            destination.Flush();
        }
    }

    private static MemberLimits Limits(ZipArchiveEntry entry) =>
        MemberLimits.Of(Math.Max(0, entry.Length), entry.CompressedLength, ZipProvider.MaxSpooledMember, ZipProvider.MaxExpansionRatio, entry.Crc32);

    // Members being read keep the archive open: an index dropped from the cache closes when the last one is disposed.
    private int _leases;
    private bool _closing;

    private void Returned()
    {
        lock (_lock)
        {
            if (--_leases == 0 && _closing) Close();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_closing) return;
            _closing = true;
            if (_leases == 0) Close();
        }
    }

    private void Close()
    {
        _archive.Dispose();
        _stream.Dispose();
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
