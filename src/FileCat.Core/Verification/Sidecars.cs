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
    /// <summary>A checksum file or manifest: <c>X.sha256</c>, <c>SHA256SUMS</c>, <c>X.sfv</c>, <c>CHECKSUMS.txt</c>, and the like.</summary>
    public static bool IsChecksumFile(string name) =>
        ChecksumManifests.IsManifestName(name) || name.StartsWith("CHECKSUMS", StringComparison.OrdinalIgnoreCase) && !IsSignatureFile(name);

    /// <summary>A detached signature: minisign's <c>.minisig</c>, OpenPGP's <c>.sig</c> and <c>.asc</c>.</summary>
    public static bool IsSignatureFile(string name) =>
        name.EndsWith(".minisig", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".asc", StringComparison.OrdinalIgnoreCase);

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
    private static readonly StringComparer Names = OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
    private readonly Dictionary<string, List<ChecksumClaim>> _checksums = new(Names);
    private readonly Dictionary<string, List<SignatureClaim>> _signatures = new(Names);
    private readonly Dictionary<string, List<string>> _covers = new(Names);

    public string Folder { get; }

    /// <summary>The checksum and signature files, with their size and time (a result stays valid while they are unchanged).</summary>
    public IReadOnlyDictionary<string, (long Size, long Modified)> Sources { get; }

    public bool IsEmpty => _checksums.Count == 0 && _signatures.Count == 0;

    private FolderSidecars(string folder, Dictionary<string, (long, long)> sources)
    {
        Folder = folder;
        Sources = sources;
    }

    public IReadOnlyList<ChecksumClaim> Checksums(string name) => _checksums.TryGetValue(name, out var list) ? list : [];

    public IReadOnlyList<SignatureClaim> Signatures(string name) => _signatures.TryGetValue(name, out var list) ? list : [];

    /// <summary>The names a checksum file or signature covers (its own row says how many match).</summary>
    public IReadOnlyList<string> Covered(string sidecar) => _covers.TryGetValue(sidecar, out var list) ? list : [];

    public bool Covers(string name) => _checksums.ContainsKey(name) || _signatures.ContainsKey(name);

    /// <summary>Reads the folder's checksum files and signatures (not the files they cover).</summary>
    public static FolderSidecars Scan(string folder, CancellationToken ct)
    {
        var files = new Dictionary<string, FileInfo>(Names);
        foreach (var info in new DirectoryInfo(folder).EnumerateFiles())
        {
            ct.ThrowIfCancellationRequested();
            files[info.Name] = info;
        }
        var sources = new Dictionary<string, (long, long)>(Names);
        var result = new FolderSidecars(folder, sources);
        foreach (var (name, info) in files)
        {
            if (!SidecarNames.IsSidecar(name)) continue;
            ct.ThrowIfCancellationRequested();
            sources[name] = (info.Length, info.LastWriteTimeUtc.Ticks);
            if (SidecarNames.IsSignatureFile(name)) result.AddSignature(name, info, files);
            else if (info.Length <= MaxChecksumFileBytes) result.AddChecksums(name, info, files);
        }
        return result;
    }

    private void AddSignature(string name, FileInfo signature, Dictionary<string, FileInfo> files)
    {
        string signed = SidecarNames.Signed(name);
        if (!files.TryGetValue(signed, out var target)) return;
        var kind = name.EndsWith(".minisig", StringComparison.OrdinalIgnoreCase) ? SignatureKind.Minisign : SignatureKind.OpenPgp;
        var claim = new SignatureClaim(kind, signature.FullName, target.FullName);
        Add(_signatures, target.Name, claim);
        Add(_covers, name, target.Name);
    }

    private void AddChecksums(string name, FileInfo sidecar, Dictionary<string, FileInfo> files)
    {
        ChecksumManifest manifest;
        try { manifest = ChecksumManifests.Load(sidecar.FullName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return; }
        bool any = false;
        foreach (var entry in manifest.Entries)
        {
            if (entry.Problem is not null || entry.Path is null) continue;
            if (!string.Equals(Path.GetDirectoryName(entry.Path), sidecar.DirectoryName, StringComparison.OrdinalIgnoreCase)) continue;
            string covered = Path.GetFileName(entry.Path);
            if (!files.ContainsKey(covered)) continue;
            Add(_checksums, covered, new ChecksumClaim(entry.Kind, entry.Expected, sidecar.FullName, entry.Line));
            Add(_covers, name, covered);
            any = true;
        }
        if (any) return;
        // A bare checksum (with nothing after it, or only a name) in a file named after the one it checks.
        if (ChecksumManifests.KindFromName(name) is not { } kind || BareChecksum(sidecar.FullName, kind) is not { } expected) return;
        string? target = TargetOf(name, files);
        if (target is null) return;
        Add(_checksums, target, new ChecksumClaim(kind, expected, sidecar.FullName, 1));
        Add(_covers, name, target);
    }

    /// <summary>"X.iso.sha256" checks "X.iso"; "X.sha256" checks the one file named "X.something" there is.</summary>
    private static string? TargetOf(string sidecar, Dictionary<string, FileInfo> files)
    {
        string stem = sidecar[..sidecar.LastIndexOf('.')];
        if (stem.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) stem = stem[..^4];
        if (files.ContainsKey(stem)) return files[stem].Name;
        var namesakes = files.Keys.Where(n => !SidecarNames.IsSidecar(n) && string.Equals(Path.GetFileNameWithoutExtension(n), stem, StringComparison.OrdinalIgnoreCase)).ToList();
        return namesakes.Count == 1 ? namesakes[0] : null;
    }

    private static string? BareChecksum(string path, ChecksumKind kind)
    {
        string text;
        try { text = File.ReadAllText(path).Trim(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        if (text.Contains('\n')) return null;
        string first = text.Split((char[])[' ', '\t'], 2)[0].TrimStart('\\');
        return first.Length == Operations.Checksums.HexLength(kind) && first.All(Uri.IsHexDigit) ? first.ToLowerInvariant() : null;
    }

    private static void Add<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        if (!list.Contains(value)) list.Add(value);
    }
}
