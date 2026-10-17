using System.Buffers;
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
    private const int MaxCacheLineBytes = 1024 * 1024;
    private const int MaxCacheFileBytes = 64 * 1024 * 1024;
    private readonly Dictionary<string, VerificationCacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private bool _loaded;
    private int _lines;

    /// <summary>
    /// A result's key: the file, the sidecars it depends on, and what vouches for its signers (key files, gpg's keyring).
    /// The leading version drops results worked out by an earlier model (keys beside a file once counted as trusted; the
    /// states were once ordered otherwise), version 3's ambiguous delimiter-based source names, version 4's
    /// metadata-only sidecars (an in-place rewrite can restore both size and mtime), and version 5's authenticity through
    /// a forgeable hash (a signed list naming the file only by MD5, SHA-1, or CRC-32).
    /// </summary>
    public static string KeyOf(string path, long size, long modified, IEnumerable<(string Name, long Size, long Modified)> sources) =>
        KeyOf(path, size, modified, sources.Select(s => (s.Name, s.Size, s.Modified, "")));

    public static string KeyOf(string path, long size, long modified, IEnumerable<(string Name, long Size, long Modified, string Fingerprint)> sources)
    {
        // Names on Unix may contain the separators used by earlier cache keys. Length-prefix every untrusted
        // string so a single sidecar cannot masquerade as two different sidecars with the same metadata.
        var key = new StringBuilder("6|");
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
            foreach (string line in ReadBoundedLines(file))
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // The persisted cache is disposable. Replace an oversized file at the next append.
            _lines = 2 * MaxEntries;
        }
    }

    /// <summary>Read an owned cache with finite byte budgets; one damaged line does not hide later valid results.</summary>
    private static IEnumerable<string> ReadBoundedLines(string path)
    {
        using var input = OperatingSystem.IsWindows()
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024)
            : new FileStream(UnixFiles.OpenRegularFileForRead(path), FileAccess.Read, 64 * 1024);
        if (input.Length > MaxCacheFileBytes) throw new InvalidDataException("Verification cache exceeds its input limit.");
        var chunk = new byte[64 * 1024];
        var line = new ArrayBufferWriter<byte>(4096);
        bool discard = false, first = true;
        long total = 0;
        int read;
        while ((read = input.Read(chunk)) != 0)
        {
            total += read;
            if (total > MaxCacheFileBytes) throw new InvalidDataException("Verification cache grew beyond its input limit.");
            for (int i = 0; i < read; i++)
            {
                byte value = chunk[i];
                if (value == (byte)'\n')
                {
                    if (!discard) yield return DecodeLine(line, first);
                    first = false;
                    discard = false;
                    line.Clear();
                }
                else if (!discard)
                {
                    if (line.WrittenCount == MaxCacheLineBytes)
                    {
                        discard = true;
                        line.Clear();
                    }
                    else
                    {
                        line.GetSpan(1)[0] = value;
                        line.Advance(1);
                    }
                }
            }
        }
        if (!discard && line.WrittenCount != 0) yield return DecodeLine(line, first);
    }

    private static string DecodeLine(ArrayBufferWriter<byte> line, bool first)
    {
        ReadOnlySpan<byte> bytes = line.WrittenSpan;
        if (bytes.Length > 0 && bytes[^1] == (byte)'\r') bytes = bytes[..^1];
        if (first && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes[3..];
        return Encoding.UTF8.GetString(bytes);
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
    /// the new result and hashes are kept. Within a <paramref name="scope"/> (one job), each folder's checksum files and
    /// signatures are read once, when the first of its files is checked.
    /// </summary>
    internal VerificationResult? OnRequest(string path, CancellationToken ct, Action<long>? progress = null, RequestScope? scope = null) =>
        Verify(path, automatic: false, ct, progress, scope);

    /// <summary>
    /// One explicit check of many files (a job): each folder's checksum files and signatures are read once (a job goes folder
    /// by folder, and the last few folders are kept), what vouches for signers at most every few seconds, and each signature
    /// over a checksum file once, not for every file the list names.
    /// </summary>
    internal sealed class RequestScope
    {
        internal const int MaxFolders = 16;
        private readonly Dictionary<string, FolderSidecars> _folders = new(StringComparer.Ordinal);
        private readonly Queue<string> _order = new();
        internal readonly Dictionary<(string Folder, bool Minisign, bool OpenPgp), (IReadOnlyList<(string Name, long Size, long Modified, string Fingerprint)> Trust, DateTime ReadAt)> Trust = [];
        internal readonly Dictionary<ListSignature, (SignatureResult Result, bool HeldOff)> Signatures = [];
        internal readonly Dictionary<(string Folder, string Trust), (IReadOnlyList<Minisign.PublicKey> Keys, DateTime ReadAt)> Keys = [];

        internal FolderSidecars SidecarsOf(VerificationService service, string folder, CancellationToken ct)
        {
            if (_folders.TryGetValue(folder, out var known)) return known;
            var sidecars = service.SidecarsOf(folder, ct, forceRefresh: true);
            while (_folders.Count >= MaxFolders && _order.TryDequeue(out string? oldest))
            {
                _folders.Remove(oldest);
                foreach (var key in Trust.Keys.Where(k => k.Folder == oldest).ToList()) Trust.Remove(key);
                foreach (var key in Keys.Keys.Where(k => k.Folder == oldest).ToList()) Keys.Remove(key);
            }
            _folders[folder] = sidecars;
            _order.Enqueue(folder);
            return sidecars;
        }
    }

    /// <summary>
    /// A signature over a checksum file as checked: the signature's and the list's fingerprints (the list's as its lines were
    /// read), and what vouched for signers then. The same again gives the same answer.
    /// </summary>
    internal readonly record struct ListSignature(string Signature, string SignatureFingerprint, string Signed, string SignedFingerprint, string Trust);

    private const int MaxListSignatures = 256;
    private readonly object _listSignatureLock = new();
    private readonly Dictionary<ListSignature, (SignatureResult Result, long Access)> _listSignatures = [];
    private long _listSignatureAccess;

    /// <summary>A list signature's answer for the rows shown: each file a signed list names would otherwise check it again.</summary>
    private SignatureResult? Recall(ListSignature signature)
    {
        lock (_listSignatureLock)
        {
            if (!_listSignatures.TryGetValue(signature, out var known)) return null;
            _listSignatures[signature] = known with { Access = ++_listSignatureAccess };
            return known.Result;
        }
    }

    /// <summary>Keeps a list signature's answer when it is one (good, bad, or by an unknown key), not one that could not be had.</summary>
    private void Keep(ListSignature signature, SignatureResult result)
    {
        if (result.State is not (VerificationState.SignatureGood or VerificationState.SignatureBad or VerificationState.SignatureUnknownKey)) return;
        lock (_listSignatureLock)
        {
            _listSignatures[signature] = (result, ++_listSignatureAccess);
            if (_listSignatures.Count > MaxListSignatures) _listSignatures.Remove(_listSignatures.MinBy(pair => pair.Value.Access).Key);
        }
    }

    private VerificationResult? Verify(string path, bool automatic, CancellationToken ct, Action<long>? progress, RequestScope? scope = null)
    {
        string? folder = Path.GetDirectoryName(path);
        if (folder is null) return null;
        string name = Path.GetFileName(path);
        // An explicit check promises to read the current claims. A writer can rewrite a checksum file
        // and restore both its own and its directory's timestamps inside the short scan-cache window.
        var sidecars = scope is null ? SidecarsOf(folder, ct, forceRefresh: !automatic) : scope.SidecarsOf(this, folder, ct);
        if (!sidecars.Covers(name))
            return SidecarNames.IsSidecar(name) && sidecars.Covered(name) is { Count: > 0 } covered ? Coverage(name, covered) : null;
        if (Stamp(path) is not { } stamp) return null;
        var checksums = sidecars.Checksums(name);
        var signatures = sidecars.Signatures(name);
        var trust = TrustOf(sidecars, name, ct, scope);
        string key = VerificationCache.KeyOf(path, stamp.Size, stamp.Modified, SourcesOf(sidecars, name).Concat(trust));
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
        var checker = new SignatureChecker(this, folder, progress, sidecars, scope, trust, recall: automatic);
        var result = Verifier.Check(checksums, signatures, Hash, checker.Check, sidecars.Signatures, ct);
        if (result.State == VerificationState.Unreadable) return result;
        // Shown from now on without reading the file again: the row says when it was checked. A check held off is not kept.
        result = result with { CheckedUtc = DateTime.UtcNow };
        if (!checker.HeldOff) cache.Put(key, result);
        return result;
    }

    /// <summary>
    /// Checks signatures over a checksum file now (<paramref name="claims"/>, from <see cref="FolderSidecars.SignaturesOver"/>;
    /// a signature over it vouches for the files it lists), over <paramref name="content"/>, the bytes its lines were read
    /// from, in memory: never over the file read again. Nothing is kept.
    /// </summary>
    /// <param name="scope">One job's: the minisign keys of a folder are read once for all its checksum files.</param>
    internal IReadOnlyList<SignatureCheck> CheckSignaturesOver(string path, IReadOnlyList<SignatureClaim> claims, byte[] content, CancellationToken ct,
        RequestScope? scope = null)
    {
        string? folder = Path.GetDirectoryName(path);
        if (folder is null || claims.Count == 0) return [];
        string name = Path.GetFileName(path);
        var checker = new SignatureChecker(this, folder, null, null, scope, null, recall: false);
        return [.. claims.Select(claim => new SignatureCheck(claim, checker.CheckContent(claim, content, ct), name))];
    }

    /// <summary>
    /// Checks the signatures of one file's check: minisign against the keys the user keeps in FileCat's keys folder (a key
    /// beside the file came from the same place, so its good signature is only "unsure"), OpenPGP through gpg unless held
    /// off (<see cref="SignatureToolsPaused"/>; <see cref="HeldOff"/> then says the result must not be kept). A signature
    /// over one of <c>sidecars</c>' checksum files is checked over the bytes its claims were read from.
    /// </summary>
    /// <param name="trust">What vouched for signers when the check began (<see cref="TrustOf"/>): a list signature's answer is kept with it.</param>
    /// <param name="recall">A row's check: a list signature's answer had already is used (an explicit check checks it again).</param>
    private sealed class SignatureChecker(VerificationService service, string folder, Action<long>? progress, FolderSidecars? sidecars, RequestScope? scope,
        IReadOnlyList<(string Name, long Size, long Modified, string Fingerprint)>? trust, bool recall)
    {
        private readonly IReadOnlyList<string> _trusted = service.TrustedKeyFolders;
        private IReadOnlyList<Minisign.PublicKey>? _keys;
        private string? _trustKey;

        public bool HeldOff { get; private set; }

        /// <summary>What vouched for signers, as one value (each name length-prefixed, as cache keys are).</summary>
        private string TrustKey => _trustKey ??= VerificationCache.KeyOf("", 0, 0, trust ?? []);

        public SignatureResult Check(SignatureClaim claim, CancellationToken ct)
        {
            string signed = Path.GetFileName(claim.Signed);
            if (sidecars is null || !SidecarNames.IsChecksumFile(signed) || !sidecars.SourceFingerprints.TryGetValue(signed, out var parsed)
                || !parsed.StartsWith("sha256:", StringComparison.Ordinal))
                return CheckFile(claim, ct);
            // A list's signature is checked once for all the files it names (the bytes are the same): within one job, and for
            // the rows shown until the list, its signature, or what vouches for signers changes. A job checks it again.
            var memo = new ListSignature(claim.Signature, sidecars.SourceFingerprints.GetValueOrDefault(Path.GetFileName(claim.Signature), ""),
                claim.Signed, parsed, TrustKey);
            if (scope is not null && scope.Signatures.TryGetValue(memo, out var known))
            {
                HeldOff |= known.HeldOff;
                return known.Result;
            }
            if (recall && service.Recall(memo) is { } recalled) return recalled;
            bool heldBefore = HeldOff;
            HeldOff = false;
            var result = CheckList(claim, signed, parsed, ct);
            scope?.Signatures.TryAdd(memo, (result, HeldOff));
            if (!HeldOff) service.Keep(memo, result);
            HeldOff |= heldBefore;
            return result;
        }

        /// <summary>A signature over one of the folder's checksum files, checked over the bytes its claims were read from.</summary>
        private SignatureResult CheckList(SignatureClaim claim, string signed, string parsed, CancellationToken ct)
        {
            // Read again, the checksum file must still be the bytes its claims came from (their fingerprint), and the
            // signature is checked over those bytes, in memory: a file changed meanwhile, or a server that answers each read
            // differently, cannot have one list's claims vouched for by another list's signature.
            byte[]? bytes;
            try { bytes = VerificationFileReader.ReadAll(claim.Signed, FolderSidecars.MaxChecksumFileBytes); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new SignatureResult(VerificationState.SignatureUnchecked, $"not checked: {signed} could not be read again: {ex.Message}");
            }
            if (bytes is null || !string.Equals(FolderSidecars.ContentFingerprint(bytes), parsed, StringComparison.Ordinal))
                return new SignatureResult(VerificationState.SignatureUnchecked, $"not checked: {signed} changed after its lines were read; verify again",
                    Short: "signature not checked: the list changed");
            return CheckContent(claim, bytes, ct);
        }

        /// <summary>Checks a signature over the file as it is read now.</summary>
        public SignatureResult CheckFile(SignatureClaim claim, CancellationToken ct)
        {
            if (claim.Kind == SignatureKind.Minisign) return Minisign.Verify(claim.Signed, claim.Signature, Keys(), ct, progress, _trusted.FirstOrDefault());
            if (HoldOff() is { } held) return held;
            var result = OpenPgp.Verify(claim.Signature, claim.Signed, ct);
            // gpg reads the file itself and says nothing while it does: once it has an answer about it, the file was read.
            if (result.State is VerificationState.SignatureGood or VerificationState.SignatureBad or VerificationState.SignatureUnknownKey
                && Stamp(claim.Signed) is { } read)
                progress?.Invoke(read.Size);
            return result;
        }

        /// <summary>Checks a signature over <paramref name="content"/>, the bytes a checksum file's lines were read from.</summary>
        public SignatureResult CheckContent(SignatureClaim claim, ReadOnlyMemory<byte> content, CancellationToken ct) =>
            claim.Kind == SignatureKind.Minisign
                ? Minisign.Verify(content, claim.Signature, Keys(), ct, _trusted.FirstOrDefault())
                : HoldOff() ?? OpenPgp.Verify(claim.Signature, content, ct);

        /// <summary>Null while gpg may run; while it is held off, the result that says so (one not to be kept).</summary>
        private SignatureResult? HoldOff()
        {
            if (!service.SignatureToolsPaused) return null;
            HeldOff = true;
            return new SignatureResult(VerificationState.SignatureUnchecked, "not checked while FileCat recovers deleted files from the disk that holds GnuPG's folder (gpg writes there)");
        }

        /// <summary>
        /// The minisign keys the user trusts and those beside the file; within a job, read once per folder while what vouches
        /// for signers is unchanged, and again after a few seconds.
        /// </summary>
        private IReadOnlyList<Minisign.PublicKey> Keys()
        {
            if (_keys is not null) return _keys;
            var memo = (folder, TrustKey);
            if (scope is not null && scope.Keys.TryGetValue(memo, out var kept) && DateTime.UtcNow - kept.ReadAt < Fresh) return _keys = kept.Keys;
            _keys = [.. Minisign.KeysIn(_trusted, trusted: true), .. Minisign.KeysIn([folder])];
            if (scope is not null) scope.Keys[memo] = (_keys, DateTime.UtcNow);
            return _keys;
        }
    }

    private IReadOnlyList<string> TrustedKeyFolders => keyFolders();

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
            var result = cache.Get(VerificationCache.KeyOf(path, stamp.Size, stamp.Modified, SourcesOf(sidecars, name).Concat(TrustOf(sidecars, name, CancellationToken.None))));
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
    /// Within a job it is read again after a few seconds, so a result is kept with what vouched for it when it was checked.
    /// </summary>
    private IReadOnlyList<(string Name, long Size, long Modified, string Fingerprint)> TrustOf(FolderSidecars sidecars, string name, CancellationToken ct, RequestScope? scope = null)
    {
        var signatures = sidecars.Signatures(name)
            .Concat(sidecars.Checksums(name).SelectMany(c => sidecars.Signatures(Path.GetFileName(c.Source))))
            .Select(s => s.Kind).ToHashSet();
        bool minisign = signatures.Contains(SignatureKind.Minisign), openPgp = signatures.Contains(SignatureKind.OpenPgp);
        if (!minisign && !openPgp) return [];
        // Within one job, what vouches for signers is not read for every file (a keyring can be megabytes, and a folder thousands of files).
        var known = (sidecars.Folder, minisign, openPgp);
        if (scope is not null && scope.Trust.TryGetValue(known, out var kept) && DateTime.UtcNow - kept.ReadAt < Fresh) return kept.Trust;
        var files = new List<FileInfo>();
        if (minisign)
            foreach (string folder in keyFolders().Append(sidecars.Folder))
            {
                // Minisign.KeysIn ignores larger public-key files. Do not hash unrelated multi-MiB
                // *.pub files for every visible signed row.
                try { files.AddRange(new DirectoryInfo(folder).EnumerateFiles("*.pub").Where(f => f.Length <= 4096)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        if (openPgp) files.AddRange(OpenPgp.KeyringFiles());
        var trust = files.Select(f => ("trust:" + f.FullName, f.Length, f.LastWriteTimeUtc.Ticks, FolderSidecars.Fingerprint(f, ct))).ToList();
        if (scope is not null) scope.Trust[known] = (trust, DateTime.UtcNow);
        return trust;
    }

    private static string Size(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.#} GiB" : $"{bytes / (double)(1 << 20):0.#} MiB";
}
