using System.Collections.Concurrent;
using FileCat.Core.Archives;
using FileCat.Core.FileSystem;
using FileCat.Core.Resources;

namespace FileCat.Archives;

/// <summary>A member of a read-only archive, as listed.</summary>
public sealed record ArchiveMemberTag(string FullName, int Index, long Size, long CompressedSize, bool Encrypted, int DuplicateOrdinal, string? UnsafeReason,
    MemberKind Kind, string? LinkTarget) : IDisplayDetails
{
    public string KindText => Kind switch
    {
        MemberKind.SymbolicLink => "Link",
        MemberKind.HardLink => "Hard link",
        MemberKind.Special => "Device or pipe",
        _ => string.Empty,
    };

    public string DetailsText => LinkTarget is { Length: > 0 } target ? "→ " + target : string.Empty;
}

/// <summary>
/// TAR, 7z, RAR, single compressed files, and disc images as read-only locations (plan §15, ADR-07, P8). The same rules
/// as ZIP apply: members are parsed in-process under limits on entry count, produced size, and expansion ratio; names
/// that would escape a folder, links, and device entries are listed but never extracted; encrypted members are listed but
/// not opened; archives inside archives (of any supported format, ZIP included) open read-only from a private spool.
/// Mark-of-the-Web propagates on extraction from the outermost marked file.
/// </summary>
public sealed class ArchiveProvider : ResourceProvider, IContainerDetector
{
    public const int MaxEntries = 1_000_000;
    public const long MaxMemberInMemory = 32L * 1024 * 1024;
    public const long MaxExpansionRatio = 1000;
    public const long MaxSpooledMember = 8L * 1024 * 1024 * 1024;
    public const int MaxNestedSpools = 4;
    public const int MaxNestingDepth = 8;

    private readonly ConcurrentDictionary<string, ArchiveIndex> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (string Path, FileStream Holder, DateTime Used)> _spools = new(StringComparer.Ordinal);
    private readonly object _spoolLock = new();
    private readonly string _tempDirectory;
    private readonly ProviderRegistry _providers;

    public ArchiveProvider(string tempDirectory, ProviderRegistry providers)
    {
        _tempDirectory = tempDirectory;
        _providers = providers;
    }

    public override string Scheme => Schemes.Archive;

    // ---- Detection -------------------------------------------------------------------------------------------

    public bool IsContainer(string fileName) => ArchiveFormats.ByName(fileName) is not null;

    public Location? GetContainerLocation(string filePath) => IsContainer(Path.GetFileName(filePath)) ? new(Schemes.Archive, string.Empty, Location.FileSystem(filePath)) : null;

    /// <summary>Opens a file in a given format regardless of its name (the format travels in the location).</summary>
    public static Location ForFile(string filePath, ArchiveKind? kind = null) =>
        new(Schemes.Archive, string.Empty, Location.FileSystem(filePath), kind?.ToString());

    /// <summary>ZIP members of these archives open through the ZIP provider.</summary>
    private IContainerDetector? Zip => _providers.TryGet(Schemes.Zip, out var p) ? p as IContainerDetector : null;

    // ---- Navigation --------------------------------------------------------------------------------------

    private static string DisplayArchive(Location l)
    {
        var container = l.Container ?? throw new InvalidOperationException("Archive location without its archive.");
        if (container.IsFileSystem) return container.Path;
        return Path.Combine(DisplayArchive(container), container.Path.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string OutermostFile(Location l)
    {
        var c = l.Container;
        while (c is not null && !c.IsFileSystem) c = c.Container;
        return c?.Path ?? throw new InvalidOperationException("Archive location without its archive.");
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
            int at = container.Path.LastIndexOf('/');
            return container.WithPath(at < 0 ? string.Empty : container.Path[..at]);
        }
        int slash = location.Path.LastIndexOf('/');
        return location.WithPath(slash < 0 ? string.Empty : location.Path[..slash]);
    }

    public override string? GetNameInParent(Location location) => GetDisplayName(location);

    public override string GetDeviceKey(Location location) => PathUtil.GetDeviceKey(OutermostFile(location));

    public override LocationCapabilities GetCapabilities(Location location) => LocationCapabilities.Enumerate | LocationCapabilities.ReadContent;

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        LocationCapabilities.CreateDirectory or LocationCapabilities.CreateFile or LocationCapabilities.Delete or LocationCapabilities.Rename or
            LocationCapabilities.TransferTarget or LocationCapabilities.ExternalEdit or LocationCapabilities.Recycle =>
            "FileCat reads this archive format but does not change it: copy (extract) members with F5; only ZIP archives are updated in place.",
        LocationCapabilities.MoveSource => "Members cannot be moved out of a read-only archive: copy (extract) them with F5.",
        _ => base.ExplainUnavailable(location, capability),
    };

    public override bool TryParse(string text, Location? current, out Location? location)
    {
        location = null;
        var t = PathUtil.ExpandUserInput(text);
        if (!Path.IsPathFullyQualified(t)) return false;
        var probe = t.TrimEnd('\\', '/');
        var rest = new Stack<string>();
        while (!string.IsNullOrEmpty(probe))
        {
            if (File.Exists(probe) && IsContainer(Path.GetFileName(probe)))
            {
                location = new Location(Schemes.Archive, string.Join('/', rest), Location.FileSystem(probe));
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
        var index = GetIndex(location, ct);
        if (!index.Children.TryGetValue(location.Path, out var children))
            throw new DirectoryNotFoundException($"\"{location.Path}\" does not exist in this archive.");
        var batch = new List<EntryData>(children.Count);
        var zip = Zip;
        foreach (var node in children)
        {
            ct.ThrowIfCancellationRequested();
            bool nested = node.Tag is { Kind: MemberKind.File } && (IsContainer(node.Name) || zip?.IsContainer(node.Name) == true);
            var e = new EntryData(node.Name, node.IsDirectory ? EntryKind.Directory : EntryKind.File, node.IsDirectory ? -1 : node.Size, node.Modified)
            {
                Tag = node.Tag,
                Flags = (node.Tag?.Encrypted == true ? EntryFlags.Protected : EntryFlags.None) |
                        (node.Tag is { UnsafeReason: not null } or { Kind: MemberKind.Special } ? EntryFlags.Unavailable : EntryFlags.None) |
                        (node.Tag?.Kind is MemberKind.SymbolicLink or MemberKind.HardLink ? EntryFlags.Link : EntryFlags.None) |
                        (nested ? EntryFlags.Container : EntryFlags.None),
            };
            batch.Add(e);
        }
        foreach (var warning in index.Warnings) sink.ReportIssue(warning);
        sink.AddBatch(batch.ToArray());
        return Task.CompletedTask;
    }

    public override Location? GetChildLocation(Location parent, in EntryData entry)
    {
        if (entry.Kind == EntryKind.Parent) return GetParent(parent);
        string member = parent.Path.Length == 0 ? entry.Name : parent.Path + "/" + entry.Name;
        if (entry.Kind == EntryKind.Directory) return parent.WithPath(member);
        if (entry.Has(EntryFlags.Container) && entry.Tag is ArchiveMemberTag { Encrypted: false, UnsafeReason: null, DuplicateOrdinal: 0, Kind: MemberKind.File })
        {
            var at = parent.WithPath(member);
            if (Zip?.IsContainer(entry.Name) == true) return new Location(Schemes.Zip, string.Empty, at);
            return new Location(Schemes.Archive, string.Empty, at);
        }
        return null;
    }

    public override ItemRef GetItemRef(Location listing, in EntryData entry) =>
        entry.Tag is ArchiveMemberTag { DuplicateOrdinal: > 0 } t
            ? new ItemRef(listing, entry.Name, entry.Kind, entry.Size, entry.Modified) { Ordinal = t.DuplicateOrdinal, Flags = entry.Flags }
            : base.GetItemRef(listing, entry);

    // ---- Content -----------------------------------------------------------------------------------------

    public override IContentSource? OpenContent(ItemRef item)
    {
        var index = GetIndex(item.Parent, CancellationToken.None);
        var node = index.Children.GetValueOrDefault(item.Parent.Path)?.FirstOrDefault(n => n.Name == item.Name && !n.IsDirectory && (n.Tag?.DuplicateOrdinal ?? 0) == item.Ordinal);
        if (node?.Tag is not { } tag) throw new FileNotFoundException($"\"{item.Name}\" is not in the archive.");
        if (tag.Encrypted) return null;
        if (tag.UnsafeReason is not null) throw new InvalidDataException("This member has an unsafe name: " + tag.UnsafeReason);
        if (tag.Kind is MemberKind.SymbolicLink or MemberKind.HardLink)
            throw new InvalidDataException("Links inside archives are not followed or extracted" + (tag.LinkTarget is { } target ? $" (it points to \"{target}\")." : "."));
        if (tag.Kind == MemberKind.Special) throw new InvalidDataException("Device and pipe entries have no content to extract.");
        return index.Extract(tag, _tempDirectory);
    }

    // ---- Index ---------------------------------------------------------------------------------------------

    private ArchiveIndex GetIndex(Location location, CancellationToken ct)
    {
        var root = location.WithPath(string.Empty);
        string path = ArchiveFile(root, 0);
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException("The archive no longer exists.", path);
        string key = $"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{root.Session}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        foreach (var k in _cache.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToList())
            if (_cache.TryRemove(k, out var old)) old.Dispose();
        string displayName = Path.GetFileName(DisplayArchive(root));
        var kind = root.Session is { Length: > 0 } forced && Enum.TryParse<ArchiveKind>(forced, out var k2) ? k2
            : ArchiveFormats.ByName(displayName) ?? throw new InvalidDataException("This file is not in an archive format FileCat reads.");
        IMemberReader reader;
        try { reader = ArchiveFormats.Open(path, kind, displayName); }
        catch (Exception ex) when (ex is not (OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException))
        {
            throw new InvalidDataException("The archive cannot be read: " + ex.Message, ex);
        }
        var index = ArchiveIndex.Build(reader, fi.Length, ct);
        _cache[key] = index;
        while (_cache.Count > 8)
        {
            var oldest = _cache.OrderBy(kv => kv.Value.LastUsed).First();
            if (_cache.TryRemove(oldest.Key, out var evicted)) evicted.Dispose();
        }
        return index;
    }

    /// <summary>The local file holding the archive: the file itself, or a private spool of a member of another archive.</summary>
    private string ArchiveFile(Location location, int depth)
    {
        var container = location.Container ?? throw new InvalidOperationException("Archive location without its archive.");
        if (container.IsFileSystem) return container.Path;
        if (depth >= MaxNestingDepth) throw new InvalidDataException($"Archives are nested more than {MaxNestingDepth} levels deep.");
        return Spool(container);
    }

    /// <summary>
    /// A private copy of an archive member (of any provider) for reading an inner archive. It is deleted when evicted or
    /// when FileCat ends (the file is opened delete-on-close). Used for ZIPs inside these archives too.
    /// </summary>
    public string Spool(Location member)
    {
        int slash = member.Path.LastIndexOf('/');
        var folder = member.WithPath(slash < 0 ? string.Empty : member.Path[..slash]);
        string name = member.Path[(slash + 1)..];
        var provider = _providers.For(member);
        string outer = OutermostFile(member);
        var outerInfo = new FileInfo(outer);
        string key = $"{member}|{outerInfo.Length}|{outerInfo.LastWriteTimeUtc.Ticks}";
        lock (_spoolLock)
        {
            if (_spools.TryGetValue(key, out var existing))
            {
                _spools[key] = existing with { Used = DateTime.UtcNow };
                return existing.Path;
            }
            using var source = provider.OpenContent(new ItemRef(folder, name, EntryKind.File))
                               ?? throw new NotSupportedException("The inner archive is encrypted and cannot be opened here.");
            Directory.CreateDirectory(_tempDirectory);
            string spoolPath = Path.Combine(_tempDirectory, $"nested-{Guid.NewGuid():N}{Path.GetExtension(name)}");
            var holder = new FileStream(spoolPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.DeleteOnClose);
            try
            {
                var buffer = new byte[256 * 1024];
                long offset = 0;
                int n;
                while ((n = source.Read(offset, buffer)) > 0)
                {
                    offset += n;
                    if (offset > MaxSpooledMember) throw new InvalidDataException("The inner archive is larger than FileCat opens in place; extract it with F5.");
                    holder.Write(buffer, 0, n);
                }
                holder.Flush();
            }
            catch
            {
                holder.Dispose();
                throw;
            }
            _spools[key] = (spoolPath, holder, DateTime.UtcNow);
            while (_spools.Count > MaxNestedSpools)
            {
                var oldest = _spools.OrderBy(kv => kv.Value.Used).First();
                if (!_spools.TryRemove(oldest.Key, out var evicted)) break;
                Release(evicted.Path);
                evicted.Holder.Dispose();
            }
            return spoolPath;
        }
    }

    public void Release(string archivePath)
    {
        foreach (var k in _cache.Keys.Where(k => k.StartsWith(archivePath + "|", StringComparison.OrdinalIgnoreCase)).ToList())
            if (_cache.TryRemove(k, out var old)) old.Dispose();
    }
}

/// <summary>The listing of one archive and its reader; extraction is serialized and bounded.</summary>
internal sealed class ArchiveIndex : IDisposable
{
    private readonly object _lock = new();
    private readonly IMemberReader _reader;
    private readonly long _archiveLength;

    public sealed record Node(string Name, bool IsDirectory, long Size, long Modified, ArchiveMemberTag? Tag);

    private ArchiveIndex(IMemberReader reader, long archiveLength, Dictionary<string, List<Node>> children, List<string> warnings)
    {
        _reader = reader;
        _archiveLength = archiveLength;
        Children = children;
        Warnings = warnings;
        LastUsed = DateTime.UtcNow;
    }

    public Dictionary<string, List<Node>> Children { get; }
    public List<string> Warnings { get; }
    public DateTime LastUsed { get; private set; }
    public string Format => _reader.Format;

    public static ArchiveIndex Build(IMemberReader reader, long archiveLength, CancellationToken ct)
    {
        var warnings = new List<string>();
        var children = new Dictionary<string, List<Node>>(StringComparer.Ordinal) { [string.Empty] = [] };
        var dirs = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        void EnsureDir(string dir, long modified = 0)
        {
            if (!dirs.Add(dir)) return;
            int slash = dir.LastIndexOf('/');
            var parent = slash < 0 ? string.Empty : dir[..slash];
            EnsureDir(parent);
            children[parent].Add(new Node(slash < 0 ? dir : dir[(slash + 1)..], true, -1, modified, null));
            children.TryAdd(dir, []);
        }
        int count = 0;
        try
        {
            foreach (var m in reader.List(warnings.Add, ct))
            {
                if (++count > ArchiveProvider.MaxEntries)
                {
                    warnings.Add($"The archive has more than {ArchiveProvider.MaxEntries:N0} entries; only the first ones are listed.");
                    break;
                }
                var raw = m.Path.Replace('\\', '/');
                string? unsafeReason = null;
                if (raw.StartsWith('/') || raw.Length > 1 && raw[1] == ':') unsafeReason = "absolute path";
                var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
                if (parts.Any(p => p == "..")) unsafeReason = "\"..\" path traversal";
                parts.RemoveAll(p => p is "." or "..");
                if (parts.Count == 0) continue;
                long modified = m.ModifiedUtc?.Ticks ?? 0;
                var parent = string.Join('/', parts.Take(parts.Count - 1));
                EnsureDir(parent);
                if (m.Kind == MemberKind.Directory)
                {
                    EnsureDir(string.Join('/', parts), modified);
                    continue;
                }
                var name = parts[^1];
                if (Core.Jobs.SafeNames.Validate(name) is { } bad) unsafeReason ??= bad;
                var key = parent + "/" + name;
                int ordinal = seen.TryGetValue(key, out var n) ? n + 1 : 0;
                seen[key] = ordinal;
                var tag = new ArchiveMemberTag(m.Path, m.Index, m.Size, m.CompressedSize, m.Encrypted, ordinal, unsafeReason, m.Kind, m.LinkTarget);
                children[parent].Add(new Node(name, false, m.Size, modified, tag));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException && count > 0)
        {
            // Damage after some members (any engine's error type): what was listed stays usable.
            warnings.Add("The archive is damaged after the members listed: " + ex.Message);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or IOException or UnauthorizedAccessException or InvalidDataException))
        {
            reader.Dispose();
            throw new InvalidDataException("The archive cannot be read: " + ex.Message, ex);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
        if (seen.Values.Any(v => v > 0)) warnings.Add("The archive contains duplicate names; each copy is listed separately.");
        return new ArchiveIndex(reader, archiveLength, children, warnings);
    }

    /// <summary>Extracts one member: small ones stay in memory, larger ones spool privately; sizes are never trusted.</summary>
    public IContentSource Extract(ArchiveMemberTag tag, string tempDirectory)
    {
        LastUsed = DateTime.UtcNow;
        lock (_lock)
        {
            long declared = tag.Size;
            Stream dst;
            if (declared is >= 0 and <= ArchiveProvider.MaxMemberInMemory) dst = new MemoryStream((int)declared);
            else
            {
                Directory.CreateDirectory(tempDirectory);
                string spoolPath = Path.Combine(tempDirectory, $"member-{Guid.NewGuid():N}.tmp");
                dst = new FileStream(spoolPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete, 1, FileOptions.DeleteOnClose);
            }
            try
            {
                using var src = _reader.Open(tag.Index, CancellationToken.None);
                CopyBounded(src, dst, declared, tag.CompressedSize > 0 ? tag.CompressedSize : _archiveLength);
            }
            catch (Exception ex)
            {
                dst.Dispose();
                if (ex is IOException or InvalidDataException or UnauthorizedAccessException) throw;
                throw new InvalidDataException("The member could not be decompressed: " + ex.Message, ex);
            }
            if (dst is MemoryStream ms) return new MemoryContentSource(tag.FullName, ms.ToArray());
            dst.Position = 0;
            return new StreamContentSource(tag.FullName, (FileStream)dst);
        }
    }

    /// <summary>A declared size caps what is produced (plus slack); without one, the ratio and an absolute cap apply.</summary>
    private static void CopyBounded(Stream src, Stream dst, long declared, long compressed)
    {
        long cap = declared >= 0 ? Math.Min(ArchiveProvider.MaxSpooledMember, declared + 1024 * 1024) : ArchiveProvider.MaxSpooledMember;
        var buffer = new byte[256 * 1024];
        long total = 0;
        int n;
        while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += n;
            if (total > cap)
                throw new InvalidDataException(declared >= 0 ? "The member expands beyond its declared size; extraction was stopped." : "The member is larger than FileCat extracts in one piece.");
            if (total > 64L * 1024 * 1024 && total / Math.Max(1, compressed) > ArchiveProvider.MaxExpansionRatio)
                throw new InvalidDataException("The member exceeds the expansion-ratio limit (possible decompression bomb); extraction was stopped.");
            dst.Write(buffer, 0, n);
        }
        if (declared >= 0 && total != declared && total < declared)
            throw new InvalidDataException($"The member ended after {total:N0} of {declared:N0} bytes; the archive is damaged.");
    }

    public void Dispose()
    {
        lock (_lock) _reader.Dispose();
    }
}
