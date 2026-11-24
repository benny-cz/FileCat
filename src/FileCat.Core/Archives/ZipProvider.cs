using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using FileCat.Core.Content;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Resources;

namespace FileCat.Core.Archives;

/// <summary>Archive member payload for listings.</summary>
public sealed record ZipMemberTag(string FullName, int EntryIndex, long CompressedSize, uint Crc, bool Encrypted, int DuplicateOrdinal, string? UnsafeReason)
    : IArchiveMemberRevision
{
    /// <summary>The listing revision a native member command checks, where a ZIP worker route is composed.</summary>
    public string? ArchiveRevision { get; init; }
    public string? ArchiveMutationProblem { get; init; }
}

/// <summary>The listing revision of a folder row (ZIP folders have no member tag of their own).</summary>
public sealed record ZipDirectoryRevision(string? ArchiveRevision, string? ArchiveMutationProblem) : IArchiveMemberRevision;

/// <summary>
/// ZIP browsing, extraction, and (with <see cref="ZipUpdateExecutor"/>) updates (plan §15, D-39) with in-box
/// System.IO.Compression, parsed in-process under enforced limits on entry count, expanded size, expansion ratio, and time.
/// Members are shown as archive members, not files; duplicate names stay distinct; encrypted entries are listed but not
/// extracted; archives inside archives open read-only from a private spool. Mark-of-the-Web propagates through
/// <see cref="StreamTransferExecutor"/> from the outermost marked file.
/// </summary>
public sealed class ZipProvider : ResourceProvider, IContainerDetector, IUnprotectedContentOutput
{
    public const int MaxEntries = 1_000_000;
    public const long MaxMemberInMemory = 32L * 1024 * 1024;
    public const long MaxExpansionRatio = 1000;
    public const long MaxSpooledMember = 8L * 1024 * 1024 * 1024;
    internal const string NestedEditUnavailable = "Archives inside archives are read-only. Extract the inner archive with F5 to change it.";
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { "zip", "jar", "nupkg", "vsix", "whl", "xpi", "snupkg", "aar" };
    // One owner builds/publishes or retires indexes; concurrent misses must not abandon a competing build.
    private readonly object _cacheLock = new();
    private readonly ConcurrentDictionary<string, ZipIndex> _cache = new(PathUtil.SafetyComparer);
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

    /// <summary>Stable identity of the archive file, following links; the platform supplies Windows IDs.</summary>
    public Func<string, string?> GetSourceIdentity { get; set; } =
        path => UnixFiles.Stat(path, followLinks: true)?.Identity.ToString();

    /// <summary>Identity of the actual opened source; required when a path identity is known.</summary>
    public Func<Microsoft.Win32.SafeHandles.SafeFileHandle, string?> GetOpenedSourceIdentity { get; set; } =
        handle => UnixFiles.Stat(handle)?.Identity.ToString();

    // Length and modification time alone cannot distinguish an independently replaced file.
    // Without an identity, retain bounded ownership but do not reuse a previous index or spool.
    private string SourceIdentityKey(string path) => GetSourceIdentity(path) is { Length: > 0 } id ? id : Guid.NewGuid().ToString("N");

    public override string Scheme => Schemes.Zip;

    /// <summary>
    /// Trusted composition's route test (ZIP worker profile design, Interface): whether a ZIP route is composed for an
    /// archive's runtime and filesystem, whether or not its worker can run now. Where one is, that archive's changes go
    /// through the bounded worker (ledger rows 6 and 13) or are refused with the worker's reason (from an AppImage, say),
    /// never through the existing route, and its listings carry the revision token native member commands resolve.
    /// Elsewhere the existing route (release R0) stays.
    /// </summary>
    internal Func<ArchivePlan, bool>? RouteComposed { get; set; }

    /// <summary>
    /// Whether the composed ZIP route can write an archive now. A listing reads its revision in the background only where
    /// it can (no read for a ZIP whose worker cannot run here); availability itself is checked again by every command.
    /// </summary>
    internal Func<ArchivePlan, ArchiveJobAvailability>? MutationAvailability { get; set; }

    /// <summary>Listings of archives up to this size read their revision in the background as soon as they are shown.</summary>
    internal long RevisionReadLimit { get; set; } = ArchiveListingRevision.DefaultBackgroundLimit;

    // Revisions already read, by listing token, so rows still on screen resolve after their index left the cache.
    private readonly ConcurrentDictionary<string, string> _resolvedRevisions = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _resolvedOrder = new();
    private void RememberRevision(string token, string revision)
    {
        if (!_resolvedRevisions.TryAdd(token, revision)) return;
        _resolvedOrder.Enqueue(token);
        while (_resolvedOrder.Count > 256 && _resolvedOrder.TryDequeue(out var oldest)) _resolvedRevisions.TryRemove(oldest, out _);
    }

    /// <summary>The profile a ZIP route is probed with; a command still reviews the actual archive before it is planned.</summary>
    internal static ArchiveWriteProfile WorkerProfile { get; } = new(ArchiveWriteFormat.Zip);

    /// <summary>Whether this ZIP, a file in a folder, is changed through the worker profile here (or not at all).</summary>
    public bool UsesWorkerRoute(Location location) =>
        location.Container is { IsFileSystem: true } container && Path.IsPathFullyQualified(container.Path) &&
        RouteComposed?.Invoke(new(container.Path, null, [], Profile: WorkerProfile)) == true;

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
    private ArchiveSourceLease ZipPath(Location location)
    {
        ArchiveNesting.RequireWithinLimit(location);
        return ArchiveFile(location);
    }

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
    public const int MaxNestingDepth = ArchiveNesting.MaximumDepth;
    // A nested key also contains an archive member name, which is always case-sensitive.
    private readonly ConcurrentDictionary<string, ArchiveSpool> _nested = new(StringComparer.Ordinal);
    private readonly object _nestedLock = new();

    private ArchiveSourceLease ArchiveFile(Location location)
    {
        var container = location.Container ?? throw new InvalidOperationException("ZIP location without its archive.");
        if (container.IsFileSystem) return new ArchiveSourceLease(container.Path);
        if (FileCat.Core.State.ProcessAuthority.IsElevatedOrRoot)
            throw new IOException($"Nested archives cannot be opened while FileCat runs {FileCat.Core.State.ProcessAuthority.RunningAs} because they require a user-local spool.");
        if (container.Scheme != Schemes.Zip)
        {
            if (OtherArchives is IArchiveSpoolProvider spools) return spools.OpenSpool(container);
            if (OperatingSystem.IsWindows()) throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, ArchiveOuterEfs.SpoolUnknown);
            return new ArchiveSourceLease(SpoolForeignMember?.Invoke(container) ?? throw new NotSupportedException("This archive is inside a location FileCat cannot read archives from."));
        }
        using var outerSource = ArchiveFile(container);
        string outer = outerSource.Path;
        var info = new FileInfo(outer);
        // A nested spool must not survive an in-place rewrite with restored mtime and unchanged file identity.
        string change = SourceChangeToken.FromPath(outer) ?? Guid.NewGuid().ToString("N");
        string key = $"{outer}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|{container.Path}|{SourceIdentityKey(outer)}|{change}";
        lock (_nestedLock)
        {
            int slash = container.Path.LastIndexOf('/');
            var folder = container.WithPath(slash < 0 ? string.Empty : container.Path[..slash]);
            string name = container.Path[(slash + 1)..];
            string spoolPath;
            FileStream holder;
            OutputEfsObservation.Receipt origin;
            // Nested copy admission must keep its outer index alive until every byte is spooled.
            // The resolved outer path avoids taking the nested lock again under the cache lock.
            lock (_cacheLock)
            {
                var index = GetIndex(folder, outerSource);
                index.RequireClearSpoolSource();
                if (_nested.TryGetValue(key, out var cached))
                {
                    cached.Used = DateTime.UtcNow;
                    return cached.Acquire();
                }
                var node = index.Children.GetValueOrDefault(folder.Path)?.Where(n => !n.IsDirectory && n.Name == name).ToList() ?? [];
                if (node.Count == 0) throw new FileNotFoundException($"\"{container.Path}\" is not in the archive.");
                if (node.Count > 1) throw new NotSupportedException("Several members share this name; extract the one you want with F5, then open it.");
                var tag = node[0].Tag!;
                if (tag.Encrypted) throw new NotSupportedException("The inner archive is encrypted and cannot be opened here.");
                if (tag.UnsafeReason is not null) throw new InvalidDataException("The inner archive has an unsafe name: " + tag.UnsafeReason);
                Directory.CreateDirectory(_tempDirectory);
                spoolPath = Path.Combine(_tempDirectory, $"nested-{Guid.NewGuid():N}.zip");
                holder = new FileStream(spoolPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete, 64 * 1024, FileOptions.DeleteOnClose);
                try
                {
                    index.CopyMember(tag, holder);
                    origin = index.OutputProtection.Freeze();
                    ArchiveOuterEfs.RequireClearOutput(origin.Protection, ContentOutputKind.DiskCache);
                }
                catch { holder.Dispose(); throw; }
            }
            var added = new ArchiveSpool(spoolPath, holder, origin);
            _nested[key] = added;
            while (_nested.Count > MaxNestedSpools)
            {
                var oldest = _nested.OrderBy(kv => kv.Value.Used).First();
                if (!_nested.TryRemove(oldest.Key, out var evicted)) break;
                Release(evicted.Path);
                evicted.Dispose();
            }
            return added.Acquire();
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

    /// <summary>Advisory transfer mapping; call on a device worker, just as for enumeration. Implicit folders count.</summary>
    internal bool DirectoryExists(Location folder) => GetIndex(folder).Children.ContainsKey(folder.Path);

    /// <summary>Why the archive cannot be changed here, or null when it can.</summary>
    public string? WhyReadOnly(Location location)
    {
        if (location.Container is not { IsFileSystem: true } container)
            return NestedEditUnavailable;
        var index = _cache.Where(kv => kv.Key.StartsWith(container.Path + "|", PathUtil.SafetyComparison)).Select(kv => kv.Value).FirstOrDefault();
        if (index is null) return null;
        if (index.ReadOnly) return "The archive file is read-only; clear its read-only attribute to change it.";
        if (index.HasEncrypted) return "The archive has encrypted members, which FileCat cannot re-create, so it cannot be changed here. Extract what you need with F5.";
        // Through the worker profile (a composed ZIP route), a listing that cannot identify its members keeps them read-only.
        if (index.Revision?.Problem is { } problem) return problem;
        return null;
    }

    public override string ExplainUnavailable(Location location, LocationCapabilities capability) => capability switch
    {
        LocationCapabilities.CreateDirectory or LocationCapabilities.Delete or LocationCapabilities.Rename or LocationCapabilities.TransferTarget
            or LocationCapabilities.ExternalEdit when WhyReadOnly(location) is { } reason => reason,
        LocationCapabilities.CreateFile => "New empty files cannot be created inside an archive; create the file in a folder and copy it in with F5.",
        LocationCapabilities.MoveSource => "Moving out of an archive is not supported: copy (extract) with F5, then delete the members with F8. Rename a member with F2.",
        LocationCapabilities.Recycle => $"Archive members do not go to {RecycleText.Destination}; deleting them rewrites the archive.",
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
                // Rows carry the listing's revision token; a member command resolves it (ArchiveListingRevision).
                Tag = index.Revision is not { } revision ? node.Tag
                    : node.Tag is { } member ? member with { ArchiveRevision = revision.Token, ArchiveMutationProblem = revision.Problem }
                    : new ZipDirectoryRevision(revision.Token, revision.Problem),
                Flags = (node.Tag?.Encrypted == true ? EntryFlags.Protected : EntryFlags.None) |
                        (node.Tag?.UnsafeReason is not null ? EntryFlags.Unavailable : EntryFlags.None) |
                        (!node.IsDirectory && (IsContainer(node.Name) || OtherArchives?.IsContainer(node.Name) == true) ? EntryFlags.Container : EntryFlags.None),
            };
            batch.Add(e);
        }
        if (index.Warning is not null) sink.ReportIssue(index.Warning);
        // A warning sink can run caller code after index admission. Refuse stale rows if the
        // opened ZIP changed or its path now names a different file.
        index.VerifySource();
        index.VerifyPath(GetSourceIdentity, GetOpenedSourceIdentity);
        sink.AddBatch(batch.ToArray());
        // A panel shows the rows: the revision they carry is read now, in the background (ArchiveListingRevision). A search,
        // a comparison or a transfer walking the archive reads nothing more.
        if (sink.Shown && index.Revision is { Problem: null, Known: null } shown && location.Container is { IsFileSystem: true } container &&
            MutationAvailability?.Invoke(new(container.Path, null, [], Profile: WorkerProfile)).Available == true)
            sink.WhileShown(shown.Show(RevisionReadLimit));
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
            ? new ItemRef(listing, entry.Name, entry.Kind, entry.Size, entry.Modified)
            {
                Ordinal = t.DuplicateOrdinal, Flags = entry.Flags, ArchiveRevision = t.ArchiveRevision, ArchiveMutationProblem = t.ArchiveMutationProblem,
            }
            : base.GetItemRef(listing, entry);

    // ---- Content -----------------------------------------------------------------------------------------

    public override IContentSource? OpenContent(ItemRef item) => OpenContent(item, retainedEdit: false);

    internal IContentSource? OpenEditContent(ItemRef item, Action<Microsoft.Win32.SafeHandles.SafeFileHandle>? reviewInput = null)
    {
        if (item.Parent.Container is not { IsFileSystem: true }) throw new NotSupportedException(NestedEditUnavailable);
        return OpenContent(item, retainedEdit: true, output: null, out _, reviewInput);
    }

    IContentSource? IUnprotectedContentOutput.OpenUnprotectedOutput(ItemRef item, ContentOutputKind kind) =>
        OpenContent(item, retainedEdit: false, output: kind);

    IContentSource? IUnprotectedContentOutput.OpenUnprotectedOutput(ItemRef item, ContentOutputKind kind, out OutputEfsObservation? provenance) =>
        OpenContent(item, retainedEdit: false, kind, out provenance);

    void IUnprotectedContentOutput.RequireClearOutputSource(Location location, ContentOutputKind kind)
    {
        using var source = ZipPath(location);
        lock (_cacheLock) GetIndex(location, source).RequireClearOutputSource(kind);
    }

    private IContentSource? OpenContent(ItemRef item, bool retainedEdit, ContentOutputKind? output = null) =>
        OpenContent(item, retainedEdit, output, out _);

    private IContentSource? OpenContent(ItemRef item, bool retainedEdit, ContentOutputKind? output, out OutputEfsObservation? provenance,
        Action<Microsoft.Win32.SafeHandles.SafeFileHandle>? reviewInput = null)
    {
        using var source = ZipPath(item.Parent);
        // Resolve nested paths before taking the cache lock; cross-provider spool locks stay ordered.
        // Admission owns the index until extraction has copied bytes or acquired its progressive lease.
        lock (_cacheLock)
        {
            var index = GetIndex(item.Parent, source);
            provenance = index.OutputProtection;
            var name = item.Name;
            int ordinal = item.Ordinal;
            var full = item.Parent.Path.Length == 0 ? name : item.Parent.Path + "/" + name;
            var node = index.FindFile(item.Parent.Path, name, ordinal);
            if (node?.Tag is not { } tag) throw new FileNotFoundException($"\"{full}\" is not in the archive.");
            if (tag.Encrypted) return null;
            if (tag.UnsafeReason is not null) throw new InvalidDataException("This member has an unsafe name: " + tag.UnsafeReason);
            return index.Extract(tag, _tempDirectory, retainedEdit, output, reviewInput);
        }
    }

    // ---- Native edits (a composed ZIP route) ------------------------------------------------------------------

    /// <summary>
    /// The SHA-256 of the archive a listing showed, for the native review of a member command: read now, through that
    /// listing's own handle, outside the provider's lock and cancellable (see <see cref="ArchiveListingRevision"/>).
    /// </summary>
    internal string ResolveListingRevision(string token, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        if (_resolvedRevisions.TryGetValue(token, out var known)) return known;
        ZipIndex? index; IDisposable? lease;
        lock (_cacheLock)
        {
            index = _cache.Values.FirstOrDefault(value => value.Revision?.Token == token);
            index?.Touch(); // In use by a command: the last to leave the cache.
            lease = index?.TryLease();
        }
        if (index?.Revision is not { } revision || lease is null) throw new IOException(ArchiveListingRevision.Closed);
        using (lease) return revision.Resolve(ct);
    }

    /// <summary>The revision a listing token already resolved to (in the background or for a command), or null.</summary>
    internal string? KnownListingRevision(string token)
    {
        if (_resolvedRevisions.TryGetValue(token, out var known)) return known;
        lock (_cacheLock) return _cache.Values.FirstOrDefault(value => value.Revision?.Token == token)?.Revision?.Known;
    }

    /// <summary>Whether any read of a listing token's revision began (tests and diagnostics).</summary>
    internal bool ListingRevisionReadBegan(string token)
    {
        if (_resolvedRevisions.ContainsKey(token)) return true;
        lock (_cacheLock) return _cache.Values.FirstOrDefault(value => value.Revision?.Token == token)?.Revision?.ReadBegan == true;
    }

    /// <summary>The raw name of the one regular member <paramref name="item"/> shows, checked against the edit's review.</summary>
    internal string ReviewedMemberPath(ArchiveTargetReview target, ItemRef item, CancellationToken ct)
    {
        var root = item.Parent.WithPath("");
        if (root.Container is not { IsFileSystem: true } container || Path.GetFullPath(container.Path) != target.Path)
            throw new IOException("An edit needs a member of the reviewed outer archive.");
        using var source = ZipPath(root);
        var (index, lease) = HoldEditIndex(root, source);
        using (lease)
        {
            RequireEditRevision(index, target, ct);
            var node = index.FindFile(item.Parent.Path, item.Name, item.Ordinal);
            if (node?.Tag is not { Encrypted: false, UnsafeReason: null } tag)
                throw new IOException("This archive entry is not a regular member that can be edited directly.");
            return tag.FullName;
        }
    }

    /// <summary>The content of the one regular member named <paramref name="rawPath"/> in the reviewed archive.</summary>
    internal IContentSource OpenReviewedMember(ArchiveTargetReview target, string rawPath, CancellationToken ct)
    {
        // An open listing of the reviewed archive, in whatever names encoding it was opened with, serves the edit: the
        // archive is not read again for a new index of its own.
        Location root;
        lock (_cacheLock)
            root = _cache.Values.Where(index => index.Revision?.Known == target.Assessment.SourceSha256 && index.ListingRoot is not null)
                .Select(index => index.ListingRoot!).FirstOrDefault(location => location.Container is { IsFileSystem: true, Path: var path } &&
                    PathUtil.SafetyComparer.Equals(Path.GetFullPath(path), target.Path)) ?? ForFile(target.Path);
        using var source = ZipPath(root);
        var (index, lease) = HoldEditIndex(root, source);
        using (lease)
        {
            RequireEditRevision(index, target, ct);
            var matches = index.Children.Values.SelectMany(nodes => nodes).Where(node => node.Tag?.FullName == rawPath).Take(2).ToArray();
            if (matches.Length != 1 || matches[0].Tag is not { Encrypted: false, UnsafeReason: null } tag)
                throw new IOException("Direct editing needs one exact regular member. Extract the selected occurrence with F5 instead.");
            // The lease keeps the index open through extraction, whatever the cache does meanwhile.
            ct.ThrowIfCancellationRequested();
            return index.Extract(tag, _tempDirectory, retainedEdit: true);
        }
    }

    // The archive's current index, with its revision, held open by a lease: the revision is then read outside the lock.
    private (ZipIndex Index, IDisposable Lease) HoldEditIndex(Location root, ArchiveSourceLease source)
    {
        lock (_cacheLock)
        {
            var index = GetIndex(root, source);
            index.RequireRevision(RememberRevision);
            return (index, index.TryLease() ?? throw new IOException("The archive was closed; open it again."));
        }
    }

    private static void RequireEditRevision(ZipIndex index, ArchiveTargetReview target, CancellationToken ct)
    {
        if (index.SourceIdentity != target.Native.Version.Identity || !index.CompleteListing || index.Revision is not { } revision ||
            revision.Resolve(ct) != target.Assessment.SourceSha256)
            throw new IOException("The archive member reader does not match the complete native edit assessment. Refresh and try again.");
    }

    // ---- Index ---------------------------------------------------------------------------------------------

    private ZipIndex GetIndex(Location location)
    {
        using var source = ZipPath(location);
        lock (_cacheLock) return GetIndex(location, source);
    }

    private ZipIndex GetIndex(Location location, ArchiveSourceLease source)
    {
        string path = source.Path;
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new FileNotFoundException("The archive no longer exists.", path);
        string? sourceIdentity = GetSourceIdentity(path);
        string identityKey = string.IsNullOrEmpty(sourceIdentity) ? Guid.NewGuid().ToString("N") : sourceIdentity;
        var key = $"{path}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}|{location.Session}|{identityKey}";
        if (_cache.TryGetValue(key, out var cached))
        {
            try
            {
                cached.VerifySource();
                // A route composed after this index was built: its listings carry a revision token from now on.
                if (UsesWorkerRoute(location)) cached.RequireRevision(RememberRevision);
                cached.Touch();
                return cached;
            }
            catch (IOException)
            {
                // A same-identity, same-metadata rewrite cannot reuse the old central directory.
                if (_cache.TryRemove(key, out var changed)) changed.Dispose();
            }
        }
        // The archive changed or was never opened: drop stale indexes for this path.
        foreach (var k in _cache.Keys.Where(k => k.StartsWith(path + "|", PathUtil.SafetyComparison)).ToList())
        {
            if (_cache.TryRemove(k, out var old)) old.Dispose();
        }
        var index = ZipIndex.Build(path, location.Session is { Length: > 0 } enc ? Encoding.GetEncoding(enc) : null,
            handle => OpenedSourceIdentity.RequireMatch(handle, sourceIdentity, GetOpenedSourceIdentity));
        index.SourceIdentity = sourceIdentity;
        index.ListingRoot = location.WithPath(string.Empty);
        // Only a token: a listing reads the central directory, never the whole archive (ArchiveListingRevision).
        if (UsesWorkerRoute(location)) index.RequireRevision(RememberRevision);
        index.KeepSource(source);
        _cache[key] = index;
        // Archives opened before are kept for going back into them: eight at most, and their indexes together within
        // RetainedIndexLimitBytes (I06: the index of a ZIP of a million members holds about 560 MiB). The two used last
        // stay whatever their size, as two panels may be showing them. An index that still feeds a viewer closes when the
        // viewer is done with it.
        while (_cache.Count > 2 && (_cache.Count > 8 || RetainedIndexBytes > RetainedIndexLimitBytes))
        {
            var oldest = _cache.Where(kv => !ReferenceEquals(kv.Value, index)).MinBy(kv => kv.Value.LastUsed);
            if (oldest.Key is null) break;
            if (_cache.TryRemove(oldest.Key, out var evicted)) evicted.Dispose();
        }
        return index;
    }

    /// <summary>What the indexes of earlier archives may hold together before the oldest go (see <see cref="GetIndex"/>).</summary>
    public long RetainedIndexLimitBytes { get; set; } = 256L << 20;

    /// <summary>The estimated size of the indexes kept now, and how many.</summary>
    public long RetainedIndexBytes => _cache.Values.Sum(i => i.EstimatedBytes);
    public int RetainedIndexes => _cache.Count;

    public void Release(string zipPath)
    {
        lock (_cacheLock) ReleaseIndexes(zipPath);
    }

    private void ReleaseIndexes(string zipPath)
    {
        foreach (var k in _cache.Keys.Where(k => k.StartsWith(zipPath + "|", PathUtil.SafetyComparison)).ToList())
        {
            if (_cache.TryRemove(k, out var old)) old.Dispose();
        }
    }
}

internal sealed class ZipIndex : IDisposable
{
    private readonly OutputEfsObservation _outputProtection;
    internal OutputEfsObservation OutputProtection => _outputProtection;
    private readonly object _lock = new();
    private readonly FileStream _stream;
    // Taken once while the index is built: reading a FileStream's handle flushes its read buffer, which must never happen
    // while another thread reads members through that stream. Checks and the revision use this handle at explicit offsets.
    private readonly Microsoft.Win32.SafeHandles.SafeFileHandle _handle;
    private readonly ZipArchive _archive;
    private readonly List<ZipArchiveEntry> _entries;
    private readonly string? _openedChangeToken;

    public sealed record Node(string Name, bool IsDirectory, long Size, long Modified, ZipMemberTag? Tag);

    private ZipIndex(FileStream stream, ZipArchive archive, List<ZipArchiveEntry> entries, Dictionary<string, List<Node>> children, string? warning,
        bool readOnly, long estimatedBytes, string? openedChangeToken, bool completeListing, OutputEfsObservation protection)
    {
        CompleteListing = completeListing;
        _outputProtection = protection;
        _stream = stream;
        _handle = stream.SafeFileHandle;
        _archive = archive;
        _entries = entries;
        Children = children;
        Warning = warning;
        ReadOnly = readOnly;
        HasEncrypted = entries.Any(e => e.IsEncrypted);
        EstimatedBytes = estimatedBytes;
        _openedChangeToken = openedChangeToken;
        LastUsed = DateTime.UtcNow;
    }

    private void VerifyClearEditSource()
    {
        RequireClearOutputSource(ContentOutputKind.EditSession);
        VerifySource();
    }

    internal void RequireClearSpoolSource() => RequireClearOutputSource(ContentOutputKind.DiskCache);

    internal void RequireClearOutputSource(ContentOutputKind kind)
    {
        _outputProtection.Observe(_handle);
        ArchiveOuterEfs.RequireClearOutput(_outputProtection, kind);
    }

    /// <summary>
    /// What this index holds in memory, estimated: .NET's entry and FileCat's node per member, and their names. Measured
    /// with members of 30-character names (ArchiveIndexScaleTests): 584 bytes a member at a million, 598 at 200,000.
    /// </summary>
    public long EstimatedBytes { get; }
    public void VerifySource()
    {
        if (_stale) throw new IOException("The archive changed since it was listed.");
        if (_openedChangeToken is not null && SourceChangeToken.FromHandle(_handle) != _openedChangeToken)
            throw new IOException("The archive source changed while it was being read.");
    }

    // A revision read saw the archive change: this index is never reused (a refresh lists the archive anew).
    private volatile bool _stale;
    private void MarkStale() => _stale = true;

    public void VerifyPath(Func<string, string?> pathIdentity,
        Func<Microsoft.Win32.SafeHandles.SafeFileHandle, string?> openedIdentity)
    {
        string? opened = openedIdentity(_handle);
        string? path = pathIdentity(_stream.Name);
        if (opened is { Length: > 0 } && path is { Length: > 0 } && path != opened ||
            _openedChangeToken is not null && SourceChangeToken.FromPath(_stream.Name) != _openedChangeToken)
            throw new IOException("The archive source changed since its listing was built.");
    }

    public void Touch() => LastUsed = DateTime.UtcNow;

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

    /// <summary>Whether every central-directory entry was listed (no entry or name budget cut the listing short).</summary>
    internal bool CompleteListing { get; }
    /// <summary>The source identity the index was opened for, as its provider names it.</summary>
    internal string? SourceIdentity { get; set; }
    /// <summary>The archive's root location this index was built for (its names encoding included).</summary>
    internal Location? ListingRoot { get; set; }

    private ArchiveListingRevision? _revision;
    /// <summary>
    /// Where a ZIP route is composed: the revision token this index's listings carry, read through this index's own handle
    /// in the background once a listing is shown, or by the first command (ArchiveListingRevision). Null elsewhere.
    /// </summary>
    internal ArchiveListingRevision? Revision => Volatile.Read(ref _revision);

    /// <summary>Gives this index its listing revision (once; cheap: nothing is read yet).</summary>
    internal ArchiveListingRevision RequireRevision(Action<string, string>? resolved = null)
    {
        if (Volatile.Read(ref _revision) is { } existing) return existing;
        string? problem = _openedChangeToken is null ? "This location cannot identify a stable archive listing for member changes."
            : !CompleteListing ? "This listing is incomplete and cannot safely identify members for changes." : null;
        var created = new ArchiveListingRevision(_handle,
            () => SourceChangeToken.Holds(SourceChangeToken.FromHandle(_handle), _openedChangeToken),
            TryLease, problem, resolved, MarkStale);
        var kept = Interlocked.CompareExchange(ref _revision, created, null) ?? created;
        if (_closing) kept.Close();
        return kept;
    }

    /// <summary>Keeps this index's archive open until disposed, even if the cache drops the index meanwhile; null once closing.</summary>
    internal IDisposable? TryLease()
    {
        lock (_lock)
        {
            if (_closing) return null;
            _leases++;
            return new IndexLease(this);
        }
    }

    private sealed class IndexLease(ZipIndex owner) : IDisposable
    {
        private ZipIndex? _owner = owner;
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Returned();
    }

    public static ZipIndex Build(string path, Encoding? nameEncoding) => Build(path, nameEncoding, null);

    /// <summary>The characters of member names an index holds; past them, only the first entries are listed.</summary>
    internal const long MaxNameChars = 64L * 1024 * 1024;

    public static ZipIndex Build(string path, Encoding? nameEncoding, Action<Microsoft.Win32.SafeHandles.SafeFileHandle>? verifyOpened = null,
        long maxNameChars = MaxNameChars)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.RandomAccess);
        ZipArchive? archive = null;
        try
        {
            verifyOpened?.Invoke(fs.SafeFileHandle);
            string? changeToken = SourceChangeToken.FromHandle(fs.SafeFileHandle);
            var protection = new OutputEfsObservation();
            protection.ObserveOpened(fs.SafeFileHandle, changeToken);
            archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: nameEncoding);
            var index = Build(path, fs, archive, changeToken, maxNameChars, protection);
            index.VerifySource();
            return index;
        }
        catch
        {
            // Entries are read lazily: malformed central directories can fail after construction.
            // The index takes ownership only after the entire build succeeds.
            try { archive?.Dispose(); }
            catch (Exception ex) { Diagnostics.AppLog.Warn("ZIP archive cleanup after index failure: " + ex.GetType().Name); }
            try { fs.Dispose(); }
            catch (Exception ex) { Diagnostics.AppLog.Warn("ZIP source cleanup after index failure: " + ex.GetType().Name); }
            throw;
        }
    }

    private static ZipIndex Build(string path, FileStream fs, ZipArchive archive, string? changeToken, long maxNameChars, OutputEfsObservation protection)
    {
        const long maxDirectoryChars = 32L * 1024 * 1024;
        var entries = new List<ZipArchiveEntry>();
        long nameChars = 0;
        long directoryChars = 0;
        long directoryCount = 0;
        var children = new Dictionary<string, List<Node>>(StringComparer.Ordinal) { [string.Empty] = [] };
        var dirs = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        long declaredTotal = 0;
        string? warning = null;
        bool complete = true;
        void EnsureDir(string dir)
        {
            // Archive names are untrusted. Walk missing ancestors without using one stack frame per segment.
            var missing = new Stack<string>();
            for (string current = dir; !dirs.Contains(current);)
            {
                if (current.Length > maxDirectoryChars - directoryChars)
                    throw new IOException("The ZIP's generated directory paths exceed FileCat's index budget.");
                dirs.Add(current);
                directoryChars += current.Length;
                directoryCount++;
                missing.Push(current);
                int slash = current.LastIndexOf('/');
                current = slash < 0 ? string.Empty : current[..slash];
            }
            while (missing.TryPop(out var current))
            {
                int slash = current.LastIndexOf('/');
                var parent = slash < 0 ? string.Empty : current[..slash];
                if (!children.TryGetValue(parent, out var list)) children[parent] = list = [];
                list.Add(new Node(slash < 0 ? current : current[(slash + 1)..], true, -1, 0, null));
                children.TryAdd(current, []);
            }
        }
        int i = 0;
        foreach (var entry in archive.Entries)
        {
            if (i >= ZipProvider.MaxEntries)
            {
                warning = $"The archive has more than {ZipProvider.MaxEntries:N0} entries; only the first ones are listed.";
                complete = false;
                break;
            }
            if (entry.FullName.Length > maxNameChars - nameChars)
            {
                // As at the entry limit: the members up to here are listed, with a warning, where the whole archive was
                // refused (review finding P6 of f134ba37).
                warning = "The archive's member names exceed what FileCat lists; only the first entries are listed.";
                complete = false;
                break;
            }
            nameChars += entry.FullName.Length;
            var raw = entry.FullName.Replace('\\', '/');
            string? unsafeReason = null;
            if (raw.StartsWith('/') || raw.Length > 1 && raw[1] == ':') unsafeReason = "absolute path";
            var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count > ArchivePaths.MaxMemberSegments)
                throw new IOException("A ZIP member has too many path components to index safely.");
            entries.Add(entry);
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
        // .NET keeps every entry of the central directory, also past the ones listed.
        // Generated ancestor paths are retained as separate strings and directory nodes. A single
        // deeply nested member can own much more index memory than its one central-directory name.
        long estimate = archive.Entries.Count * 460L + nameChars * 4 + directoryChars * 2 + directoryCount * 160;
        return new ZipIndex(fs, archive, entries, children, warning, readOnly, estimate, changeToken, complete, protection);
    }

    /// <summary>
    /// Opens one member. Small ones are extracted into memory at once; larger ones are decompressed as they are read
    /// (<see cref="ProgressiveContent"/>), so the first bytes are there at once. Either way the declared size is not
    /// trusted and the member's checksum is verified (.NET does not check it).
    /// </summary>
    public IContentSource Extract(ZipMemberTag tag, string tempDirectory, bool retainedEdit = false, ContentOutputKind? output = null,
        Action<Microsoft.Win32.SafeHandles.SafeFileHandle>? reviewInput = null)
    {
        LastUsed = DateTime.UtcNow;
        lock (_lock)
        {
            void VerifyInput()
            {
                if (retainedEdit) VerifyClearEditSource();
                else VerifySource();
                if (output is { } purpose) RequireClearOutputSource(purpose);
            }
            VerifyInput();
            reviewInput?.Invoke(_handle);
            if (_closing && _leases == 0) throw new IOException("The archive was closed; open it again.");
            var entry = _entries[tag.EntryIndex];
            var limits = Limits(entry);
            if (entry.Length > ZipProvider.MaxMemberInMemory)
            {
                _leases++;
                return new ProgressiveContent(tag.FullName, _lock, entry.Open, limits, ZipProvider.MaxSpooledMember,
                    tempDirectory, Returned, VerifyInput, RequireClearSpoolSource);
            }
            var memory = new MemoryStream((int)Math.Max(0, entry.Length));
            using (var source = entry.Open()) limits.CopyAll(source, memory, default, retainedEdit || output is not null ? VerifyInput : null);
            VerifyInput();
            // Ordinary members already have an exact-sized backing array. Keep its logical length instead of
            // copying the whole payload just before a viewer or nested reader allocates again. A malformed
            // member may expand within the allowed slack and force geometric over-allocation; trim that case.
            if (memory.Capacity - memory.Length > MemberLimits.Slack)
                return new MemoryContentSource(tag.FullName, memory.ToArray());
            return new MemoryContentSource(tag.FullName,
                new ArraySegment<byte>(memory.GetBuffer(), 0, checked((int)memory.Length)));
        }
    }

    /// <summary>Writes one member into <paramref name="destination"/> under the same limits (a nested archive's spool).</summary>
    public void CopyMember(ZipMemberTag tag, Stream destination)
    {
        LastUsed = DateTime.UtcNow;
        lock (_lock)
        {
            VerifySource();
            var entry = _entries[tag.EntryIndex];
            if (entry.Length > ZipProvider.MaxSpooledMember) throw new InvalidDataException("The inner archive is larger than FileCat opens in place; extract it with F5.");
            using (var source = entry.Open()) Limits(entry).CopyAll(source, destination, default, RequireClearSpoolSource);
            VerifySource();
            destination.Flush();
        }
    }

    private static MemberLimits Limits(ZipArchiveEntry entry) =>
        MemberLimits.Of(Math.Max(0, entry.Length), entry.CompressedLength, ZipProvider.MaxSpooledMember, ZipProvider.MaxExpansionRatio, entry.Crc32);

    // Members being read keep the archive open: an index dropped from the cache closes when the last one is disposed.
    private ArchiveSourceLease? _source;
    public void KeepSource(ArchiveSourceLease source)
    {
        if (source.Origin is { } origin) _outputProtection.Include(origin);
        _source = source.Retain();
    }
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
        ArchiveListingRevision? revision;
        lock (_lock)
        {
            if (_closing) return;
            _closing = true;
            revision = Volatile.Read(ref _revision);
            if (_leases == 0) Close();
        }
        // Outside the index lock (the revision takes its own lock, then this one for a lease): a revision read nothing waits
        // for stops at its next block and returns its lease, and the archive then closes.
        revision?.Close();
    }

    private void Close()
    {
        Exception? failure = null;
        try { _archive.Dispose(); } catch (Exception ex) { failure = ex; }
        try { _stream.Dispose(); }
        catch (Exception ex) { if (failure is null) failure = ex; else Diagnostics.AppLog.Warn("Secondary ZIP stream cleanup failure: " + ex.GetType().Name); }
        try { Interlocked.Exchange(ref _source, null)?.Dispose(); }
        catch (Exception ex) when (failure is not null) { Diagnostics.AppLog.Warn("Secondary ZIP source cleanup failure: " + ex.GetType().Name); }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
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
