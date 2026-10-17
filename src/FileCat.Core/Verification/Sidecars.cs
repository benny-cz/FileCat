using System.Security.Cryptography;
using FileCat.Core.Diagnostics;
using FileCat.Core.FileSystem;
using FileCat.Core.Operations;

namespace FileCat.Core.Verification;

/// <summary>What a checksum file says about one file: the algorithm, the expected value, and where it says so.</summary>
public sealed record ChecksumClaim(ChecksumKind Kind, string Expected, string Source, int Line);

public enum SignatureKind { Minisign, OpenPgp }

/// <summary>A detached signature over a file (or over a checksum manifest, which then vouches for what it lists).</summary>
public sealed record SignatureClaim(SignatureKind Kind, string Signature, string Signed);

/// <summary>The names that carry verification beside files (D-57): checksum files and manifests, and detached signatures.</summary>
public static class SidecarNames
{
    /// <summary>
    /// A checksum file or manifest: <c>X.sha256</c>, <c>SHA256SUMS</c>, <c>X.sfv</c>, <c>CHECKSUMS.txt</c>, and the lists Fedora and
    /// CentOS publish (<c>Fedora-Workstation-40-1.14-x86_64-CHECKSUM</c>, <c>CHECKSUM</c>), whose lines name their algorithm.
    /// </summary>
    public static bool IsChecksumFile(string name) =>
        ChecksumManifests.IsManifestName(name)
        || (name.StartsWith("CHECKSUMS", StringComparison.OrdinalIgnoreCase) || name.Equals("CHECKSUM", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-CHECKSUM", StringComparison.OrdinalIgnoreCase)) && !IsSignatureFile(name);

    /// <summary>
    /// A detached signature: minisign's <c>.minisig</c>, OpenPGP's <c>.sig</c> and <c>.asc</c>, and OpenPGP's <c>.gpg</c> over a
    /// checksum file (<c>SHA256SUMS.gpg</c>); another <c>.gpg</c> is more likely an encrypted file than a signature.
    /// </summary>
    public static bool IsSignatureFile(string name) =>
        name.EndsWith(".minisig", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".asc", StringComparison.OrdinalIgnoreCase)
        || name.Length > 4 && name.EndsWith(".gpg", StringComparison.OrdinalIgnoreCase) && IsChecksumFile(name[..^4]);

    public static bool IsSidecar(string name) => IsChecksumFile(name) || IsSignatureFile(name);

    /// <summary>The name a signature file signs ("X.minisig" signs "X").</summary>
    public static string Signed(string signatureName) => signatureName[..signatureName.LastIndexOf('.')];
}

/// <summary>
/// A folder's checksum files and signatures, read once (D-57): which of its files each covers, and what it claims. Files
/// named after one file (<c>X.iso.sha256</c>, <c>X.sha256</c> for <c>X.iso</c>, with or without the name inside, or a
/// bare checksum) and manifests listing several (<c>SHA256SUMS</c>, <c>X.sfv</c>) are read by the manifest parser, which
/// refuses names outside the folder. Checksum files larger than 4 MiB are left out.
/// </summary>
public sealed class FolderSidecars
{
    public const long MaxChecksumFileBytes = 4L << 20;
    private readonly Dictionary<string, List<ChecksumClaim>> _checksums;
    private readonly Dictionary<string, List<SignatureClaim>> _signatures;
    private readonly Dictionary<string, List<string>> _covers;
    private readonly Dictionary<string, HashSet<string>> _coveredNames;
    private readonly HashSet<string> _bare;
    private readonly StringComparer _names;
    private long _retainedClaims;
    private long _retainedCoveredNames;

    public string Folder { get; }

    /// <summary>The checksum and signature files, with their size and time.</summary>
    public IReadOnlyDictionary<string, (long Size, long Modified)> Sources { get; }

    /// <summary>Content identity for bounded sidecars; a native change token for larger signatures.</summary>
    public IReadOnlyDictionary<string, string> SourceFingerprints { get; }

    // Count parsed objects retained by a cached scan, independently of its source-file byte size.
    internal long RetainedUnits => Sources.Count + _retainedClaims + _retainedCoveredNames;

    public bool IsEmpty => _checksums.Count == 0 && _signatures.Count == 0;

    private FolderSidecars(string folder, Dictionary<string, (long, long)> sources, Dictionary<string, string> fingerprints, StringComparer names)
    {
        Folder = folder;
        Sources = sources;
        SourceFingerprints = fingerprints;
        _names = names;
        _checksums = new(names);
        _signatures = new(names);
        _covers = new(names);
        _coveredNames = new(names);
        _bare = new(names);
    }

    /// <summary>Whether a checksum file holds a bare checksum (no list form) for the file it is named after, read as such.</summary>
    public bool IsBare(string sidecar) => _bare.Contains(sidecar);

    public IReadOnlyList<ChecksumClaim> Checksums(string name) => _checksums.TryGetValue(name, out var list) ? list : [];

    public IReadOnlyList<SignatureClaim> Signatures(string name) => _signatures.TryGetValue(name, out var list) ? list : [];

    /// <summary>The names a checksum file or signature covers (its own row says how many match).</summary>
    public IReadOnlyList<string> Covered(string sidecar) => _covers.TryGetValue(sidecar, out var list) ? list : [];

    public bool Covers(string name) => _checksums.ContainsKey(name) || _signatures.ContainsKey(name);

    /// <summary>Reads the folder's checksum files and signatures (not the files they cover).</summary>
    public static FolderSidecars Scan(string folder, CancellationToken ct)
    {
        var names = WindowsCaseSensitivity.NamesIn(folder);
        var files = new Dictionary<string, FileInfo>(names);
        foreach (var info in new DirectoryInfo(folder).EnumerateFiles())
        {
            ct.ThrowIfCancellationRequested();
            files[info.Name] = info;
        }
        var sources = new Dictionary<string, (long, long)>(names);
        var fingerprints = new Dictionary<string, string>(names);
        var result = new FolderSidecars(folder, sources, fingerprints, names);
        foreach (var (name, info) in files)
        {
            if (!SidecarNames.IsSidecar(name)) continue;
            // A cloud placeholder is not read (reading it would download it): its claims wait until it is on this computer.
            if (IsPlaceholder(info)) continue;
            ct.ThrowIfCancellationRequested();
            sources[name] = (info.Length, info.LastWriteTimeUtc.Ticks);
            string? parsed = null;
            try
            {
                if (SidecarNames.IsSignatureFile(name)) result.AddSignature(name, info, files);
                else if (info.Length <= MaxChecksumFileBytes) parsed = result.AddChecksums(name, info, files);
            }
            // One sidecar FileCat cannot make sense of leaves the folder's others standing (and is still fingerprinted: a change is seen).
            catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
            {
                AppLog.Warn($"A checksum file or signature could not be read: {Path.Combine(folder, name)}", ex);
            }
            // A checksum file's fingerprint is that of the very bytes its claims were read from: a signature over it is
            // checked over bytes with this fingerprint, so a file changed between the two reads cannot pass for the other.
            fingerprints[name] = parsed ?? Fingerprint(info, ct);
        }
        return result;
    }

    /// <summary>The fingerprint of bounded content, as <see cref="SourceFingerprints"/> keeps it.</summary>
    internal static string ContentFingerprint(ReadOnlySpan<byte> content) => "sha256:" + Convert.ToHexString(SHA256.HashData(content));

    /// <summary>A folder's detached signatures by the name each signs, in any letter case, and the folder's own rule for names.</summary>
    internal sealed record SignatureIndex(string Folder, StringComparer Names, Dictionary<string, List<FileInfo>> BySigned);

    /// <summary>
    /// The folder's detached signatures, listed once for many of its files (a check of a folder's checksum files); null when
    /// the folder could not be listed. A cloud placeholder is left out, as <see cref="Scan"/> leaves it out.
    /// </summary>
    internal static SignatureIndex? IndexSignatures(string folder)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0 };
            var bySigned = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
            foreach (var info in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                if (!SidecarNames.IsSignatureFile(info.Name) || IsPlaceholder(info)) continue;
                string signed = SidecarNames.Signed(info.Name);
                if (!bySigned.TryGetValue(signed, out var found)) bySigned[signed] = found = [];
                found.Add(info);
            }
            return new SignatureIndex(folder, WindowsCaseSensitivity.NamesIn(folder), bySigned);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// The detached signatures over one file (<c>X.minisig</c>, <c>X.sig</c>, <c>X.asc</c> beside <c>X</c>, the extension in
    /// any letter case), by the rules <see cref="Scan"/> follows: the folder's own rule for letter case, and a Mac volume's
    /// alias only when both spellings name the same file. From <paramref name="index"/> when the folder was listed already.
    /// Null when the folder could not be listed: then it is not known whether anything signs it.
    /// </summary>
    internal static IReadOnlyList<SignatureClaim>? SignaturesOver(string path, SignatureIndex? index = null)
    {
        string? folder = Path.GetDirectoryName(path);
        if (folder is null) return [];
        index ??= IndexSignatures(folder);
        if (index is null) return null;
        string name = Path.GetFileName(path);
        var claims = new List<SignatureClaim>();
        if (!index.BySigned.TryGetValue(name, out var candidates)) return claims;
        foreach (var info in candidates)
        {
            string signed = SidecarNames.Signed(info.Name);
            bool same = index.Names.Equals(signed, name) || OperatingSystem.IsMacOS() && string.Equals(signed, name, StringComparison.OrdinalIgnoreCase)
                && UnixFiles.Stat(Path.Combine(folder, signed)) is { } a && UnixFiles.Stat(path) is { } b && a.Identity == b.Identity && a.IsLink == b.IsLink;
            if (!same) continue;
            var kind = info.Name.EndsWith(".minisig", StringComparison.OrdinalIgnoreCase) ? SignatureKind.Minisign : SignatureKind.OpenPgp;
            claims.Add(new SignatureClaim(kind, info.FullName, path));
        }
        return claims;
    }

    /// <summary>
    /// The file a checksum file holding only a bare value checks, by its name, there or not: "X.iso.sha256" (or
    /// "X.iso.sha256.txt") checks "X.iso"; "X.sha256" checks "X", or the one "X.something" there is. Null when its name
    /// names no file ("SHA256SUMS") or several fit.
    /// </summary>
    internal static string? BareTarget(string sidecarPath)
    {
        string folder = Path.GetDirectoryName(sidecarPath) ?? "", name = Path.GetFileName(sidecarPath);
        string stem = name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        int dot = stem.LastIndexOf('.');
        if (dot <= 0) return null;
        stem = stem[..dot];
        string exact = Path.Combine(folder, stem);
        if (stem.Contains('.') || File.Exists(exact)) return exact;
        try
        {
            var namesakes = new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = false })
                .Where(f => !SidecarNames.IsSidecar(f.Name) && string.Equals(Path.GetFileNameWithoutExtension(f.Name), stem, StringComparison.OrdinalIgnoreCase))
                .Take(2).ToList();
            return namesakes.Count == 1 ? namesakes[0].FullName : namesakes.Count == 0 ? exact : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool IsPlaceholder(FileInfo info) => (info.Attributes & FileAttributes.Offline) != 0 || ((uint)info.Attributes & 0x440000u) != 0;

    internal static string Fingerprint(FileInfo info, CancellationToken ct)
    {
        // A writer can restore size and mtime. Hash only bounded sidecars; an arbitrarily large
        // signature uses its native change token, or a one-scan nonce when no token is available.
        if (info.Length <= MaxChecksumFileBytes)
        {
            try
            {
                using var stream = VerificationFileReader.OpenRead(info.FullName, FileShare.ReadWrite | FileShare.Delete);
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[64 * 1024];
                long total = 0;
                int read;
                while ((read = stream.Read(buffer.AsSpan(0, (int)Math.Min(buffer.Length, MaxChecksumFileBytes + 1 - total)))) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    total += read;
                    if (total > MaxChecksumFileBytes) break;
                    digest.AppendData(buffer.AsSpan(0, read));
                }
                if (total <= MaxChecksumFileBytes)
                    return "sha256:" + Convert.ToHexString(digest.GetHashAndReset());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return SourceChangeToken.FromPath(info.FullName) is { } token ? "native:" + token : "scan:" + Guid.NewGuid().ToString("N");
    }

    private void AddSignature(string name, FileInfo signature, Dictionary<string, FileInfo> files)
    {
        string signed = SidecarNames.Signed(name);
        if (FindFile(files, signed, signature.DirectoryName!) is not { } target) return;
        var kind = name.EndsWith(".minisig", StringComparison.OrdinalIgnoreCase) ? SignatureKind.Minisign : SignatureKind.OpenPgp;
        var claim = new SignatureClaim(kind, signature.FullName, target.FullName);
        AddClaim(_signatures, target.Name, claim);
        AddCovered(name, target.Name);
    }

    /// <summary>Reads a checksum file's claims; the fingerprint of the bytes they were read from, or null when it could not be read.</summary>
    private string? AddChecksums(string name, FileInfo sidecar, Dictionary<string, FileInfo> files)
    {
        ChecksumManifest manifest;
        byte[] content;
        try { manifest = ChecksumManifests.Load(sidecar.FullName, MaxChecksumFileBytes, out content); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return null; }
        string fingerprint = ContentFingerprint(content);
        bool any = false;
        foreach (var entry in manifest.Entries)
        {
            if (entry.Problem is not null || entry.Path is null) continue;
            if (!string.Equals(Path.GetDirectoryName(entry.Path), sidecar.DirectoryName, StringComparison.OrdinalIgnoreCase)) continue;
            string covered = Path.GetFileName(entry.Path);
            if (FindFile(files, covered, sidecar.DirectoryName!) is not { } coveredFile) continue;
            AddClaim(_checksums, coveredFile.Name, new ChecksumClaim(entry.Kind, entry.Expected, sidecar.FullName, entry.Line));
            AddCovered(name, coveredFile.Name);
            any = true;
        }
        if (any) return fingerprint;
        // A bare checksum (with nothing after it, or only a name) in a file named after the one it checks: from the same bytes.
        if (ChecksumManifests.KindFromName(name) is not { } kind || BareChecksum(content, kind) is not { } expected) return fingerprint;
        string? target = TargetOf(name, files, sidecar.DirectoryName!);
        if (target is null) return fingerprint;
        AddClaim(_checksums, target, new ChecksumClaim(kind, expected, sidecar.FullName, 1));
        AddCovered(name, target);
        _bare.Add(name);
        return fingerprint;
    }

    /// <summary>"X.iso.sha256" checks "X.iso"; "X.sha256" checks the one file named "X.something" there is; "SHA256SUMS" none.</summary>
    private static string? TargetOf(string sidecar, Dictionary<string, FileInfo> files, string folder)
    {
        // "X.iso.sha256.txt" is read as "X.iso.sha256" (as ChecksumManifests.KindFromName reads it).
        string stem = sidecar.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? sidecar[..^4] : sidecar;
        int dot = stem.LastIndexOf('.');
        if (dot <= 0) return null;
        stem = stem[..dot];
        if (FindFile(files, stem, folder) is { } exactOrAlias) return exactOrAlias.Name;
        var namesakes = files.Keys.Where(n => !SidecarNames.IsSidecar(n) && string.Equals(Path.GetFileNameWithoutExtension(n), stem, StringComparison.OrdinalIgnoreCase)).ToList();
        return namesakes.Count == 1 ? namesakes[0] : null;
    }

    private static FileInfo? FindFile(Dictionary<string, FileInfo> files, string name, string folder)
    {
        if (files.TryGetValue(name, out var exact)) return exact;
        if (!OperatingSystem.IsMacOS()) return null;
        // Preserve a case-insensitive Mac volume's existing spelling alias without merging distinct names on a
        // case-sensitive volume. A single candidate is not enough: the requested spelling must name that inode.
        var candidates = files.Values.Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (candidates.Length != 1) return null;
        var requested = UnixFiles.Stat(Path.Combine(folder, name));
        var listed = UnixFiles.Stat(candidates[0].FullName);
        return requested is { } a && listed is { } b && a.Identity == b.Identity && a.IsLink == b.IsLink ? candidates[0] : null;
    }

    /// <summary>A bare checksum (the value alone, or followed by a name) in bounded content, or null.</summary>
    internal static string? BareChecksum(ReadOnlySpan<byte> content, ChecksumKind kind)
    {
        if (content.Length > MaxChecksumFileBytes) return null;
        string text = System.Text.Encoding.UTF8.GetString(content);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        text = text.Trim();
        if (text.Contains('\n')) return null;
        string first = text.Split((char[])[' ', '\t'], 2)[0].TrimStart('\\');
        return first.Length == Operations.Checksums.HexLength(kind) && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    private void AddClaim<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        // Each parsed manifest line has a distinct source/line pair, and each signature file is scanned once.
        // Checking the growing list for a duplicate on every line makes a legal manifest quadratic to scan.
        list.Add(value);
        _retainedClaims++;
    }

    private void AddCovered(string source, string file)
    {
        if (!_covers.TryGetValue(source, out var list))
        {
            _covers[source] = [file];
            _retainedCoveredNames++;
            return;
        }
        if (_coveredNames.TryGetValue(source, out var seen))
        {
            if (seen.Add(file)) { list.Add(file); _retainedCoveredNames++; }
            return;
        }
        // Most sidecars cover one file. Keep that case small, but stop linear duplicate searches
        // once a manifest covers many distinct files.
        if (list.Any(name => _names.Equals(name, file))) return;
        list.Add(file);
        _retainedCoveredNames++;
        if (list.Count == 16) _coveredNames[source] = new HashSet<string>(list, _names);
    }
}
