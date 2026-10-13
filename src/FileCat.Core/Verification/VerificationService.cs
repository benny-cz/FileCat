using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FileCat.Core.FileSystem;
using FileCat.Core.Operations;

namespace FileCat.Core.Verification;

/// <summary>
/// One kept line: a result (its key says what it was computed from: the file's size/time and its sidecars' fingerprints), or a
/// file's hashes (<see cref="Hashes"/>, algorithm name to value; its key is "#" and the file's path, size, and time).
/// </summary>
public sealed record VerificationCacheEntry(string Key, VerificationState State, string Text, string[] Details)
{
    public Dictionary<string, string>? Hashes { get; init; }
    public DateTime? CheckedUtc { get; init; }
}

[JsonSerializable(typeof(VerificationCacheEntry))]
internal sealed partial class VerificationJsonContext : JsonSerializerContext;

/// <summary>
/// Results and hashes kept between runs (D-57), so a 40 GB image is read once, not on every visit: one JSON line each in
/// FileCat's cache folder. A result is keyed by the file's path, size, and modification time and the metadata/content
/// fingerprints of the sidecars that cover it; a changed sidecar makes a new key after a fresh scan. Hashes are keyed by the file alone:
/// any job that reads a file (verifying a manifest, calculating checksums) leaves them, and a later check uses them
/// instead of reading the file again. The file is rewritten, newest entries kept, when it grows past twice
/// <see cref="MaxEntries"/>. The same recent-entry window bounds memory when loading an older oversized file or
/// when the cache has no persistence file.
/// </summary>
public sealed class VerificationCache(string? file)
{
    public const int MaxEntries = 20_000;
    private readonly Dictionary<string, VerificationCacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private bool _loaded;
    private int _lines;

    /// <summary>
    /// A result's key: the file, the sidecars it depends on, and what vouches for its signers (key files, gpg's keyring).
    /// The leading version drops results worked out by an earlier model (keys beside a file once counted as trusted; the
    /// states were once ordered otherwise), version 3's ambiguous delimiter-based source names, and version 4's
    /// metadata-only sidecars (an in-place rewrite can restore both size and mtime).
    /// </summary>
    public static string KeyOf(string path, long size, long modified, IEnumerable<(string Name, long Size, long Modified)> sources) =>
        KeyOf(path, size, modified, sources.Select(s => (s.Name, s.Size, s.Modified, "")));

    public static string KeyOf(string path, long size, long modified, IEnumerable<(string Name, long Size, long Modified, string Fingerprint)> sources)
    {
        // Names on Unix may contain the separators used by earlier cache keys. Length-prefix every untrusted
        // string so a single sidecar cannot masquerade as two different sidecars with the same metadata.
        var key = new StringBuilder("5|");
        static void Name(StringBuilder key, string value) => key.Append(value.Length).Append(':').Append(value);
        Name(key, path);
        key.Append('|').Append(size).Append('|').Append(modified);
        foreach (var source in sources.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            key.Append('|');
            Name(key, source.Name);
            key.Append(':').Append(source.Size).Append(':').Append(source.Modified).Append(':');
            Name(key, source.Fingerprint);
        }
        return key.ToString();
    }

    private static string HashKeyOf(string path, long size, long modified) => $"#{path}|{size}|{modified}";

    public VerificationResult? Get(string key)
    {
        lock (_lock)
        {
            Load();
            return _entries.TryGetValue(key, out var e) && e.Hashes is null ? new VerificationResult(e.State, e.Text, e.Details) { CheckedUtc = e.CheckedUtc } : null;
        }
    }

    public void Put(string key, VerificationResult result) =>
        Append(new VerificationCacheEntry(key, result.State, result.Text, [.. result.Details]) { CheckedUtc = result.CheckedUtc });

    /// <summary>The hashes known for the file as it is now (its size and modification time), by algorithm.</summary>
    public IReadOnlyDictionary<ChecksumKind, string> HashesOf(string path, long size, long modified)
    {
        lock (_lock)
        {
            Load();
            if (!_entries.TryGetValue(HashKeyOf(path, size, modified), out var e) || e.Hashes is null) return new Dictionary<ChecksumKind, string>();
            var known = new Dictionary<ChecksumKind, string>();
            foreach (var (name, value) in e.Hashes)
                if (Enum.TryParse<ChecksumKind>(name, out var kind)) known[kind] = value;
            return known;
        }
    }

    /// <summary>Adds hashes for the file as it was when they were read (the ones known already are kept).</summary>
    public void PutHashes(string path, long size, long modified, IReadOnlyDictionary<ChecksumKind, string> hashes)
    {
        if (hashes.Count == 0) return;
        string key = HashKeyOf(path, size, modified);
        lock (_lock)
        {
            Load();
            var merged = _entries.TryGetValue(key, out var old) && old.Hashes is { } known ? new Dictionary<string, string>(known) : [];
            foreach (var (kind, value) in hashes) merged[kind.ToString()] = value;
            Append(new VerificationCacheEntry(key, VerificationState.NotChecked, "", []) { Hashes = merged });
        }
    }

    private void Append(VerificationCacheEntry entry)
    {
        lock (_lock)
        {
            Load();
            _entries.Remove(entry.Key);
            _entries[entry.Key] = entry; // re-added last: the newest when the file is rewritten
            TrimRecent();
            if (file is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (++_lines > 2 * MaxEntries) Compact();
                else File.AppendAllText(file, JsonSerializer.Serialize(entry, VerificationJsonContext.Default.VerificationCacheEntry) + "\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private void Load()
    {
        if (_loaded) return;
        _loaded = true;
        if (file is null || !File.Exists(file)) return;
        try
        {
            foreach (string line in File.ReadLines(file))
            {
                _lines++;
                try
                {
                    if (JsonSerializer.Deserialize(line, VerificationJsonContext.Default.VerificationCacheEntry) is { } e)
                    {
                        _entries.Remove(e.Key);
                        _entries[e.Key] = e;
                        TrimRecent();
                    }
                }
                catch (JsonException) { } // a line cut short by a crash: the rest still counts
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void TrimRecent()
    {
        if (_entries.Count <= 2 * MaxEntries) return;
        // Dictionary insertion order tracks the same newest-wins rule used by Compact. Keep the
        // whole compaction window until it fills, then retire its oldest half in one bounded pass.
        while (_entries.Count > MaxEntries)
            _entries.Remove(_entries.Keys.First());
    }

    private void Compact()
    {
        var keep = _entries.Values.Skip(Math.Max(0, _entries.Count - MaxEntries)).ToList();
        _entries.Clear();
        foreach (var e in keep) _entries[e.Key] = e;
        string temporary = file + ".tmp";
        File.WriteAllLines(temporary, keep.Select(e => JsonSerializer.Serialize(e, VerificationJsonContext.Default.VerificationCacheEntry)));
        File.Move(temporary, file!, overwrite: true);
        _lines = keep.Count;
    }
}

/// <summary>
/// Checksums and signatures beside files, checked in the background (D-57). A row asks for its file's state: the
/// folder's sidecars are read once (and again when they change), the result is taken from the cache when the file and
/// its sidecars are unchanged, hashes some job already read are used instead of reading the file, files larger than the
/// threshold and files on the network are left for an explicit request, and everything else is hashed once for all its
/// algorithms. Runs on the caller's thread (the device's background queue); nothing here touches the UI.
/// </summary>
public sealed class VerificationService(VerificationCache cache, Func<long> threshold, Func<IReadOnlyList<string>> keyFolders)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(5);
    // This is a short-lived scan cache, not a history of every folder visited during an app session.
    private const int MaxCachedFolders = 512;
    // A folder-count limit alone can retain millions of parsed claims from legal (<4 MiB) manifests.
    private const long MaxCachedSidecarUnits = 128_000;
    /// <summary>How long a file must have been still before it is read on its own (see <see cref="VerificationResult.Settling"/>).</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(3);
    private readonly object _folderLock = new();
    // Exact keys keep names in a Windows per-directory case-sensitive parent separate. On ordinary Windows
    // folders, differently spelled aliases can occupy separate bounded entries; that is a conservative rescan.
    private readonly Dictionary<string, (FolderSidecars Sidecars, long Stamp, DateTime ReadAt, long Access)> _folders = new(StringComparer.Ordinal);
    private long _folderAccess;
    private long _folderUnits;

    /// <summary>The instance the app configured (its cache file, the size threshold setting, its key folders).</summary>
    public static VerificationService? Current { get; set; }

    /// <summary>
    /// Set while FileCat recovers deleted files from the disk that holds GnuPG's folder (release plan V09, I09): gpg may
    /// write there (its trust database, locks), so OpenPGP signatures are left unchecked, and such results are not kept.
    /// </summary>
    public bool SignatureToolsPaused { get; set; }

    /// <summary>Raised (on the calling thread) when a folder's checksum files or signatures changed: values shown for it are stale.</summary>
    public event Action<string>? SidecarsChanged;

    public VerificationCache Cache => cache;

    /// <summary>A file's size and modification time as the caches key them, or null when it is not there.</summary>
    public static (long Size, long Modified)? Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? (info.Length, info.LastWriteTimeUtc.Ticks) : null;
    }

    /// <summary>
    /// Keeps hashes a job read (<paramref name="before"/> is the file's <see cref="Stamp"/> from before the read): only
    /// when the file is still as it was, so a file written meanwhile is not remembered with a value it no longer has.
    /// </summary>
    public void Remember(string path, (long Size, long Modified) before, IReadOnlyDictionary<ChecksumKind, string> hashes)
    {
        if (Stamp(path) == before) cache.PutHashes(path, before.Size, before.Modified, hashes);
    }

    /// <summary>The folder's sidecars, read again when the folder changed, after a few seconds, or for an explicit check.</summary>
    public FolderSidecars SidecarsOf(string folder, CancellationToken ct, bool forceRefresh = false)
    {
        long stamp = Directory.GetLastWriteTimeUtc(folder).Ticks;
        lock (_folderLock)
        {
            if (!forceRefresh && _folders.TryGetValue(folder, out var known) && known.Stamp == stamp && DateTime.UtcNow - known.ReadAt < Fresh)
            {
                _folders[folder] = known with { Access = ++_folderAccess };
                return known.Sidecars;
            }
        }
        var sidecars = FolderSidecars.Scan(folder, ct);
        bool changed;
        lock (_folderLock)
        {
            _folders.TryGetValue(folder, out var known);
            changed = known.Sidecars is { } before && !SameSources(before, sidecars);
            if (known.Sidecars is { } previous)
            {
                _folders.Remove(folder);
                _folderUnits -= previous.RetainedUnits;
            }
            if (sidecars.RetainedUnits <= MaxCachedSidecarUnits)
            {
                _folders[folder] = (sidecars, stamp, DateTime.UtcNow, ++_folderAccess);
                _folderUnits += sidecars.RetainedUnits;
            }
            // Keep the folder just scanned when it fits. An exceptionally large scan is returned
            // to this request without retaining it for every later visit.
            while (_folders.Count > MaxCachedFolders || _folderUnits > MaxCachedSidecarUnits)
            {
                string oldest = _folders.Where(pair => !StringComparer.Ordinal.Equals(pair.Key, folder))
                    .MinBy(pair => pair.Value.Access).Key;
                _folderUnits -= _folders[oldest].Sidecars.RetainedUnits;
                _folders.Remove(oldest);
            }
        }
        if (changed) SidecarsChanged?.Invoke(folder);
        return sidecars;
    }

    /// <summary>After the folder's listing changed: rescan its sidecars and invalidate rows when sources changed or the prior scan was evicted.</summary>
    public void FolderChanged(string folder, CancellationToken ct)
    {
        bool evicted;
        lock (_folderLock)
        {
            evicted = !_folders.TryGetValue(folder, out var known);
            if (!evicted) _folders[folder] = known with { ReadAt = DateTime.MinValue };
        }
        SidecarsOf(folder, ct);
        // A prior result may still be on screen after its parsed folder was evicted. With no old source list
        // to compare, conservatively invalidate those rows after the fresh scan.
        if (evicted) SidecarsChanged?.Invoke(folder);
    }

    private static bool SameSources(FolderSidecars a, FolderSidecars b) =>
        a.Sources.Count == b.Sources.Count && a.Sources.All(s =>
            b.Sources.TryGetValue(s.Key, out var other) && other == s.Value &&
            a.SourceFingerprints.TryGetValue(s.Key, out var beforeFingerprint) &&
            b.SourceFingerprints.TryGetValue(s.Key, out var afterFingerprint) &&
            beforeFingerprint == afterFingerprint);

    /// <summary>
    /// What a visible row shows: null when nothing covers the file; for a sidecar, what it covers; otherwise the result,
    /// from the cache or checked now, or "not checked" when the file would have to be read and is large or on the network.
    /// </summary>
    public VerificationResult? Automatic(string path, CancellationToken ct) => Verify(path, automatic: true, ct, null);

    /// <summary>
    /// The check asked for (a command): no size limit, and nothing kept is trusted: the file is read and its signatures
    /// checked now (a file can be changed and its time set back, which a kept result keyed by that time would not see);
    /// the new result and hashes are kept.
    /// </summary>
    public VerificationResult? OnRequest(string path, CancellationToken ct, Action<long>? progress = null) => Verify(path, automatic: false, ct, progress);

    private VerificationResult? Verify(string path, bool automatic, CancellationToken ct, Action<long>? progress)
    {
        string? folder = Path.GetDirectoryName(path);
        if (folder is null) return null;
        string name = Path.GetFileName(path);
        // An explicit check promises to read the current claims. A writer can rewrite a checksum file
        // and restore both its own and its directory's timestamps inside the short scan-cache window.
        var sidecars = SidecarsOf(folder, ct, forceRefresh: !automatic);
        if (!sidecars.Covers(name))
            return SidecarNames.IsSidecar(name) && sidecars.Covered(name) is { Count: > 0 } covered ? Coverage(name, covered) : null;
        if (Stamp(path) is not { } stamp) return null;
        var checksums = sidecars.Checksums(name);
        var signatures = sidecars.Signatures(name);
        string key = VerificationCache.KeyOf(path, stamp.Size, stamp.Modified, SourcesOf(sidecars, name).Concat(TrustOf(sidecars, name)));
        if (automatic && cache.Get(key) is { } cached) return cached;
        var known = automatic ? cache.HashesOf(path, stamp.Size, stamp.Modified) : new Dictionary<ChecksumKind, string>();
        // Signatures over the file read all of it; checksums only when their hashes are not known yet.
        bool reads = signatures.Count > 0 || checksums.Any(c => !known.ContainsKey(c.Kind));
        if (automatic && reads)
        {
            if (stamp.Size > threshold())
                return new VerificationResult(VerificationState.NotChecked, $"not checked: {Size(stamp.Size)}",
                    [$"Files larger than {Size(threshold())} are checked on request: File → Verify checksums and signatures."]);
            if (PathUtil.IsOnNetwork(path))
                return new VerificationResult(VerificationState.NotChecked, "not checked: on the network",
                    ["Files on the network are checked on request: File → Verify checksums and signatures."]);
            // A file being written (a copy, a download to its final name) would be read half-done and "differ".
            if (DateTime.UtcNow - new DateTime(stamp.Modified, DateTimeKind.Utc) < Settle)
                return new VerificationResult(VerificationState.NotChecked, "being written",
                    ["It changed a moment ago: it is checked once it has been still for a few seconds."]) { Settling = true };
        }
        IReadOnlyDictionary<ChecksumKind, string> Hash(IReadOnlyCollection<ChecksumKind> kinds)
        {
            var missing = kinds.Where(k => !known.ContainsKey(k)).ToList();
            if (missing.Count == 0) return known;
            var read = Verifier.Hash(path, missing, ct, progress);
            Remember(path, stamp, read);
            var all = new Dictionary<ChecksumKind, string>(known);
            foreach (var (kind, value) in read) all[kind] = value;
            return all;
        }
        // Keys the user keeps in FileCat's keys folder are trusted; a key beside the file is not (it came from the same place).
        IReadOnlyList<Minisign.PublicKey>? keys = null;
        var trustedFolders = keyFolders();
        bool heldOff = false;
        SignatureResult Check(SignatureClaim claim, CancellationToken token)
        {
            if (claim.Kind == SignatureKind.Minisign)
                return Minisign.Verify(claim.Signed, claim.Signature, keys ??= [.. Minisign.KeysIn(trustedFolders, trusted: true), .. Minisign.KeysIn([folder])], token, progress,
                    trustedFolders.FirstOrDefault());
            if (!SignatureToolsPaused) return OpenPgp.Verify(claim.Signature, claim.Signed, token);
            heldOff = true;
            return new SignatureResult(VerificationState.SignatureUnchecked, "not checked while FileCat recovers deleted files from the disk that holds GnuPG's folder (gpg writes there)");
        }
        var result = Verifier.Check(checksums, signatures, Hash, Check, sidecars.Signatures, ct);
        if (result.State == VerificationState.Unreadable) return result;
        // Shown from now on without reading the file again: the row says when it was checked. A check held off is not kept.
        result = result with { CheckedUtc = DateTime.UtcNow };
        if (!heldOff) cache.Put(key, result);
        return result;
    }

    /// <summary>A checksum file's or signature's own row: what it covers (each covered file shows its own result).</summary>
    private static VerificationResult Coverage(string sidecar, IReadOnlyList<string> covered)
    {
        string what = SidecarNames.IsSignatureFile(sidecar) ? "signs" : "checks";
        string text = covered.Count == 1 ? $"{what} {covered[0]}" : $"{what} {covered.Count} files";
        return new VerificationResult(VerificationState.Sidecar, text, [.. covered.Take(50).Select(c => "· " + c)]);
    }

    /// <summary>The folder's results so far, from the cache only (the status line): nothing is hashed for it.</summary>
    public (int Good, int Bad, int Unknown, int NotChecked) Summary(string folder, IEnumerable<string> names)
    {
        FolderSidecars sidecars;
        lock (_folderLock)
        {
            if (!_folders.TryGetValue(folder, out var known)) return default;
            sidecars = known.Sidecars;
        }
        int good = 0, bad = 0, unknown = 0, notChecked = 0;
        foreach (string name in names)
        {
            if (!sidecars.Covers(name)) continue;
            string path = Path.Combine(folder, name);
            if (Stamp(path) is not { } stamp) continue;
            var result = cache.Get(VerificationCache.KeyOf(path, stamp.Size, stamp.Modified, SourcesOf(sidecars, name).Concat(TrustOf(sidecars, name))));
            if (result is null) notChecked++;
            else if (result.IsGood) good++;
            else if (result.IsBad) bad++;
            else unknown++;
        }
        return (good, bad, unknown, notChecked);
    }

    /// <summary>The sidecars a file's result depends on: its checksum files and signatures, and the signatures of those manifests.</summary>
    private static IEnumerable<(string Name, long Size, long Modified, string Fingerprint)> SourcesOf(FolderSidecars sidecars, string name)
    {
        var checksums = sidecars.Checksums(name);
        return checksums.Select(c => Path.GetFileName(c.Source))
            .Concat(sidecars.Signatures(name).Select(s => Path.GetFileName(s.Signature)))
            .Concat(checksums.SelectMany(c => sidecars.Signatures(Path.GetFileName(c.Source))).Select(s => Path.GetFileName(s.Signature)))
            .Distinct(StringComparer.Ordinal)
            .Select(n => sidecars.Sources.TryGetValue(n, out var s) && sidecars.SourceFingerprints.TryGetValue(n, out var fingerprint)
                ? (n, s.Size, s.Modified, fingerprint) : (n, -1L, 0L, "missing"));
    }

    /// <summary>
    /// What vouches for a file's signers, as it is now: the minisign key files (trusted and beside the file) when a
    /// minisign signature is involved, gpg's keyring and trust database when an OpenPGP one is. A change checks it again.
    /// </summary>
    private IEnumerable<(string Name, long Size, long Modified, string Fingerprint)> TrustOf(FolderSidecars sidecars, string name)
    {
        var signatures = sidecars.Signatures(name)
            .Concat(sidecars.Checksums(name).SelectMany(c => sidecars.Signatures(Path.GetFileName(c.Source))))
            .Select(s => s.Kind).ToHashSet();
        var files = new List<FileInfo>();
        if (signatures.Contains(SignatureKind.Minisign))
            foreach (string folder in keyFolders().Append(sidecars.Folder))
            {
                try { files.AddRange(new DirectoryInfo(folder).EnumerateFiles("*.pub")); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        if (signatures.Contains(SignatureKind.OpenPgp)) files.AddRange(OpenPgp.KeyringFiles());
        return files.Select(f => ("trust:" + f.FullName, f.Length, f.LastWriteTimeUtc.Ticks, ""));
    }

    private static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GiB" : $"{bytes / (double)(1 << 20):0.#} MiB";
}
