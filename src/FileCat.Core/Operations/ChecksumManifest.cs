using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using FileCat.Core.Verification;

namespace FileCat.Core.Operations;

public enum ChecksumKind { Crc32, Md5, Sha1, Sha256, Sha384, Sha512 }

public static partial class Checksums
{
    public static string Name(ChecksumKind kind) => kind switch
    {
        ChecksumKind.Crc32 => "CRC-32",
        ChecksumKind.Md5 => "MD5",
        ChecksumKind.Sha1 => "SHA-1",
        ChecksumKind.Sha384 => "SHA-384",
        ChecksumKind.Sha512 => "SHA-512",
        _ => "SHA-256",
    };

    public static int HexLength(ChecksumKind kind) => kind switch
    {
        ChecksumKind.Crc32 => 8,
        ChecksumKind.Md5 => 32,
        ChecksumKind.Sha1 => 40,
        ChecksumKind.Sha384 => 96,
        ChecksumKind.Sha512 => 128,
        _ => 64,
    };

    /// <summary>The usual manifest extension: <c>.sfv</c> for CRC-32, otherwise the algorithm's name.</summary>
    public static string Extension(ChecksumKind kind) => kind switch
    {
        ChecksumKind.Crc32 => ".sfv",
        ChecksumKind.Md5 => ".md5",
        ChecksumKind.Sha1 => ".sha1",
        ChecksumKind.Sha384 => ".sha384",
        ChecksumKind.Sha512 => ".sha512",
        _ => ".sha256",
    };

    /// <summary>CRC-32, MD5, and SHA-1 detect accidental damage but are not proof against deliberate changes (plan §9.4).</summary>
    public static bool IsCompatibilityOnly(ChecksumKind kind) => kind is ChecksumKind.Crc32 or ChecksumKind.Md5 or ChecksumKind.Sha1;

    /// <summary>
    /// A checksum as a download page or a tool shows it, pasted: bare, "sha256:…", "SHA256 (name) = …", "… name", or in
    /// spaced pairs (certutil). The algorithm follows from the length (8 CRC-32, 32 MD5, 40 SHA-1, 64 SHA-256, 96 SHA-384,
    /// 128 SHA-512). Null when the text holds no such value or two of the same length, or names an algorithm FileCat does
    /// not compute (SHA-3, BLAKE), whose values have the same lengths.
    /// </summary>
    public static (ChecksumKind Kind, string Hex)? ParseExpected(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1024) return null;
        if (text.Contains("sha3", StringComparison.OrdinalIgnoreCase) || text.Contains("sha-3", StringComparison.OrdinalIgnoreCase)
            || text.Contains("blake", StringComparison.OrdinalIgnoreCase) || text.Contains("keccak", StringComparison.OrdinalIgnoreCase)) return null;
        // The longest value wins: a file name after it may hold a shorter hex run ("… deadbeef.bin").
        var runs = HexRun().Matches(text).Select(m => m.Value).Where(v => KindOfLength(v.Length) is not null).OrderByDescending(v => v.Length).ToList();
        string? hex = runs.Count == 1 || runs.Count > 1 && runs[0].Length > runs[1].Length ? runs[0] : null;
        if (runs.Count == 0)
        {
            // certutil and some pages split the value into pairs: "e3 b0 c4 42 …".
            string joined = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
            if (joined.All(char.IsAsciiHexDigit) && KindOfLength(joined.Length) is not null) hex = joined;
        }
        return hex is not null && KindOfLength(hex.Length) is { } kind ? (kind, hex.ToLowerInvariant()) : null;
    }

    private static ChecksumKind? KindOfLength(int digits) => digits switch
    {
        8 => ChecksumKind.Crc32,
        32 => ChecksumKind.Md5,
        40 => ChecksumKind.Sha1,
        64 => ChecksumKind.Sha256,
        96 => ChecksumKind.Sha384,
        128 => ChecksumKind.Sha512,
        _ => null,
    };

    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{8,128}(?![0-9A-Fa-f])")]
    private static partial Regex HexRun();

    /// <summary>Hashes a file with sequential reads; <paramref name="progress"/> receives the bytes read by each step.</summary>
    public static string Compute(string path, ChecksumKind kind, CancellationToken ct, Action<long>? progress = null)
    {
        using var stream = VerificationFileReader.OpenSequential(path);
        var buffer = new byte[1 << 20];
        int n;
        if (kind == ChecksumKind.Crc32)
        {
            uint crc = 0;
            while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                crc = Crc32.Append(crc, buffer.AsSpan(0, n));
                progress?.Invoke(n);
            }
            return crc.ToString("x8");
        }
        using var hash = IncrementalHash.CreateHash(kind switch
        {
            ChecksumKind.Md5 => HashAlgorithmName.MD5,
            ChecksumKind.Sha1 => HashAlgorithmName.SHA1,
            ChecksumKind.Sha384 => HashAlgorithmName.SHA384,
            ChecksumKind.Sha512 => HashAlgorithmName.SHA512,
            _ => HashAlgorithmName.SHA256,
        });
        while ((n = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, n);
            progress?.Invoke(n);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>One manifest line in the widely read form: GNU "hash  name" (names with a backslash or line break escaped), or SFV "name crc".</summary>
    public static string ManifestLine(ChecksumKind kind, string hash, string relativePath)
    {
        string name = kind == ChecksumKind.Crc32 || OperatingSystem.IsWindows() ? relativePath.Replace('\\', '/') : relativePath;
        if (kind == ChecksumKind.Crc32) return $"{name} {hash}";
        return name.Contains('\n') || name.Contains('\\') || name.Contains('\r')
            ? $"\\{hash}  {name.Replace("\\", "\\\\").Replace("\n", "\\n").Replace("\r", "\\r")}"
            : $"{hash}  {name}";
    }
}

/// <summary>One line of a manifest; entries with a <see cref="Problem"/> are reported and not verified.</summary>
public sealed record ManifestEntry(int Line, string Name, string? Path, ChecksumKind Kind, string Expected, string? Problem);

public sealed record ChecksumManifest(string ManifestPath, string Root, IReadOnlyList<ManifestEntry> Entries)
{
    public int Verifiable => Entries.Count(e => e.Problem is null);

    /// <summary>
    /// It carries its own OpenPGP signature (clearsigned, as Fedora's CHECKSUM files are): its armor is not read as checksum
    /// lines, and the signature is not checked.
    /// </summary>
    public bool ClearSigned { get; init; }
}

/// <summary>
/// Checksum manifests (plan §9.4): GNU/coreutils (<c>hash  name</c>, <c>hash *name</c>), BSD tagged
/// (<c>SHA256 (name) = hash</c>), and SFV (<c>name crc32</c>). Names resolve inside the manifest's folder; absolute
/// paths and <c>..</c> are refused rather than followed.
/// </summary>
public static partial class ChecksumManifests
{
    public const long MaxManifestBytes = 64L << 20;
    public const int MaxEntries = 1_000_000;
    private const int MaxLineChars = 65_536;

    /// <summary>The algorithm a manifest's file name announces, or null when the name is not a known manifest name.</summary>
    public static ChecksumKind? KindFromName(string fileName)
    {
        string name = fileName.ToLowerInvariant();
        if (name.EndsWith(".txt", StringComparison.Ordinal)) name = name[..^4];
        string ext = System.IO.Path.GetExtension(name);
        return ext switch
        {
            ".sfv" => ChecksumKind.Crc32,
            ".md5" or ".md5sum" => ChecksumKind.Md5,
            ".sha1" or ".sha1sum" => ChecksumKind.Sha1,
            ".sha256" or ".sha256sum" => ChecksumKind.Sha256,
            ".sha384" or ".sha384sum" => ChecksumKind.Sha384,
            ".sha512" or ".sha512sum" => ChecksumKind.Sha512,
            _ => System.IO.Path.GetFileName(name) switch
            {
                "md5sums" or "md5sum" => ChecksumKind.Md5,
                "sha1sums" or "sha1sum" => ChecksumKind.Sha1,
                "sha256sums" or "sha256sum" => ChecksumKind.Sha256,
                "sha384sums" or "sha384sum" => ChecksumKind.Sha384,
                "sha512sums" or "sha512sum" => ChecksumKind.Sha512,
                _ => null,
            },
        };
    }

    public static bool IsManifestName(string fileName) => KindFromName(fileName) is not null;

    /// <summary>Reads a manifest (UTF-8, or Latin-1 when it is not valid UTF-8) and parses it.</summary>
    public static ChecksumManifest Load(string manifestPath, long maxBytes = MaxManifestBytes) => Load(manifestPath, maxBytes, keep: false, out _);

    /// <summary>
    /// Reads and parses a manifest, giving the bytes read as well: a signature over it is checked over exactly what was
    /// parsed, never over the file read again (it may have changed since, or a server may answer differently).
    /// </summary>
    internal static ChecksumManifest Load(string manifestPath, long maxBytes, out byte[] content) => Load(manifestPath, maxBytes, keep: true, out content);

    private static ChecksumManifest Load(string manifestPath, long maxBytes, bool keep, out byte[] content)
    {
        if (maxBytes is <= 0 or > MaxManifestBytes) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        // Opened as a regular file (a pipe or a device planted under its name would never end), and read once, bounded: a
        // file that grows while it is read must not make the read wait or keep arbitrary input.
        byte[] bytes = VerificationFileReader.ReadAll(manifestPath, maxBytes)
            ?? throw new InvalidDataException($"It is larger than {Size(maxBytes)}, more than a checksum file holds.");
        content = keep ? bytes : [];
        string text;
        try { text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        return Parse(manifestPath, text);
    }

    private static string Size(long bytes) => bytes >= 1 << 20 ? $"{bytes >> 20} MiB" : $"{bytes:N0} bytes";

    public static ChecksumManifest Parse(string manifestPath, string text)
    {
        string root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(manifestPath))!;
        var announced = KindFromName(System.IO.Path.GetFileName(manifestPath));
        var entries = new List<ManifestEntry>();
        int lineNumber = 0;
        ReadOnlySpan<char> remaining = text;
        // A clearsigned list: its header ("Hash: SHA256", up to a blank line) and its signature's armor are not checksum lines.
        bool clearSigned = false, header = false, armor = false;
        while (true)
        {
            int newline = remaining.IndexOf('\n');
            ReadOnlySpan<char> raw = newline < 0 ? remaining : remaining[..newline];
            lineNumber++;
            ReadOnlySpan<char> line = raw.TrimEnd('\r');
            if (!clearSigned && entries.Count == 0 && line.SequenceEqual("-----BEGIN PGP SIGNED MESSAGE-----")) clearSigned = header = true;
            else if (header) header = !line.Trim().IsEmpty;
            else if (clearSigned && !armor && line.SequenceEqual("-----BEGIN PGP SIGNATURE-----")) armor = true;
            else if (armor) armor = !line.SequenceEqual("-----END PGP SIGNATURE-----");
            else if (!line.Trim().IsEmpty && line[0] is not ('#' or ';'))
            {
                if (entries.Count >= MaxEntries) throw new InvalidDataException($"The manifest lists more than {MaxEntries:N0} files.");
                if (line.Length > MaxLineChars)
                    entries.Add(new ManifestEntry(lineNumber, line[..120].ToString() + "…", null,
                        announced ?? ChecksumKind.Sha256, "", "The checksum line is too long."));
                else
                    entries.Add(ParseLine(root, line.ToString(), lineNumber, announced));
            }
            if (newline < 0) break;
            remaining = remaining[(newline + 1)..];
        }
        return new ChecksumManifest(manifestPath, root, entries) { ClearSigned = clearSigned };
    }

    private static ManifestEntry ParseLine(string root, string line, int number, ChecksumKind? announced)
    {
        ManifestEntry Bad(string problem) => new(number, line.Length > 120 ? line[..120] + "…" : line, null, announced ?? ChecksumKind.Sha256, "", problem);
        string name, hex;
        ChecksumKind? kind;
        bool escapedGnuName = false;
        if (BsdLine().Match(line) is { Success: true } bsd)
        {
            kind = FromTag(bsd.Groups["tag"].Value);
            if (kind is null) return Bad($"\"{bsd.Groups["tag"].Value}\" is not an algorithm FileCat verifies.");
            name = bsd.Groups["name"].Value;
            hex = bsd.Groups["hex"].Value;
        }
        else if (announced != ChecksumKind.Crc32 && GnuLine().Match(line.StartsWith('\\') ? line[1..] : line) is { Success: true } gnu)
        {
            hex = gnu.Groups["hex"].Value;
            name = gnu.Groups["name"].Value;
            if (line.StartsWith('\\'))
            {
                escapedGnuName = true;
                if (Unescape(name) is not { } unescaped) return Bad("The file name has an unknown escape sequence.");
                name = unescaped;
            }
            kind = announced ?? FromLength(hex.Length);
        }
        else if (SfvLine().Match(line) is { Success: true } sfv && announced is null or ChecksumKind.Crc32)
        {
            name = sfv.Groups["name"].Value.TrimEnd();
            hex = sfv.Groups["hex"].Value;
            kind = ChecksumKind.Crc32;
        }
        else return Bad("This line is not a checksum line.");
        if (kind is not { } k) return Bad($"A checksum with {hex.Length} digits matches no algorithm FileCat verifies.");
        if (hex.Length != Checksums.HexLength(k)) return Bad($"The checksum has {hex.Length} digits; {Checksums.Name(k)} has {Checksums.HexLength(k)}.");
        string? problem = Resolve(root, name, out var full, literalBackslash: escapedGnuName && !OperatingSystem.IsWindows());
        return new ManifestEntry(number, name, full, k, hex.ToLowerInvariant(), problem);
    }

    /// <summary>Resolves a listed name inside <paramref name="root"/>; absolute names and <c>..</c> are refused.</summary>
    internal static string? Resolve(string root, string name, out string? full, bool literalBackslash = false)
    {
        full = null;
        string normalized = literalBackslash ? name : name.Replace('\\', '/');
        if (normalized.Length == 0) return "The line has no file name.";
        if (normalized.StartsWith('/') || normalized.Length >= 2 && normalized[1] == ':' || System.IO.Path.IsPathRooted(normalized))
            return "Not verified: absolute paths are refused; a manifest lists files in and below its own folder.";
        var parts = new List<string> { root };
        foreach (var part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            if (part == "..") return "Not verified: the path leads out of the manifest's folder (\"..\").";
            if (PathUtil.ValidateNewName(part) is { } bad) return $"Not verified: {bad}";
            parts.Add(part);
        }
        if (parts.Count == 1) return "The line has no file name.";
        full = System.IO.Path.GetFullPath(System.IO.Path.Combine([.. parts]));
        if (!PathUtil.IsSameOrUnder(full, root))
        {
            full = null;
            return "Not verified: the path leads out of the manifest's folder.";
        }
        return null;
    }

    private static string? Unescape(string name)
    {
        var sb = new StringBuilder(name.Length);
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] != '\\') { sb.Append(name[i]); continue; }
            if (++i >= name.Length) return null;
            switch (name[i])
            {
                case '\\': sb.Append('\\'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                default: return null;
            }
        }
        return sb.ToString();
    }

    private static ChecksumKind? FromTag(string tag) => tag.Replace("-", "").ToUpperInvariant() switch
    {
        "MD5" => ChecksumKind.Md5,
        "SHA1" => ChecksumKind.Sha1,
        "SHA256" => ChecksumKind.Sha256,
        "SHA384" => ChecksumKind.Sha384,
        "SHA512" => ChecksumKind.Sha512,
        "CRC32" => ChecksumKind.Crc32,
        _ => null,
    };

    private static ChecksumKind? FromLength(int digits) => digits switch
    {
        8 => ChecksumKind.Crc32,
        32 => ChecksumKind.Md5,
        40 => ChecksumKind.Sha1,
        64 => ChecksumKind.Sha256,
        96 => ChecksumKind.Sha384,
        128 => ChecksumKind.Sha512,
        _ => null,
    };

    [GeneratedRegex(@"^(?<tag>[A-Za-z0-9-]+) ?\((?<name>.*)\) ?= ?(?<hex>[0-9A-Fa-f]+)$")]
    private static partial Regex BsdLine();

    [GeneratedRegex(@"^(?<hex>[0-9A-Fa-f]{8,128})(?: [ *]| )(?<name>.+)$")]
    private static partial Regex GnuLine();

    [GeneratedRegex(@"^(?<name>.+?)\s+(?<hex>[0-9A-Fa-f]{8})$")]
    private static partial Regex SfvLine();
}

/// <summary>
/// Verifies checksum manifests (<see cref="JobRequest.Sources"/>): an explicit, cancellable read-only job with byte
/// progress and a result per listed file. Mismatches and missing files are errors; unverifiable lines are reported. A
/// signature over a manifest (<c>SHA256SUMS.asc</c>, <c>.minisig</c>) vouches for what it lists, as beside files (D-57):
/// it is checked once, and a bad one fails every file the manifest lists. Lines the check beside the files compared
/// already (<see cref="JobRequest.ComparedBeside"/>) are left out; with <see cref="JobRequest.IgnoreMissing"/>, so are
/// lines for files that are not there.
/// </summary>
internal sealed class VerifyChecksumsExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public const string MismatchCause = "checksum-mismatch";
    public const string MissingCause = "checksum-missing";
    /// <summary>The file's checksum matched, but the signature over the manifest that lists it is bad.</summary>
    public const string SignatureCause = "checksum-signature-bad";

    public override void Execute()
    {
        int verified = 0, mismatched = 0, missing = 0, unreadable = 0, skipped = 0, planned = 0, unsure = 0, badSignature = 0, leftBeside = 0, absent = 0;
        var compatibilityOnly = new SortedSet<string>(StringComparer.Ordinal);
        var plan = new List<(ManifestEntry Entry, string Manifest, IReadOnlyList<SignatureCheck> Signatures, string? Unvouched)>();
        var signed = new List<string>();
        string first = Job.Request.Sources.FirstOrDefault()?.FileSystemPath ?? "";
        var report = Job.VerificationReport = new VerificationReport(System.IO.Path.GetDirectoryName(first) ?? "");
        var compared = Job.Request.ComparedBeside;
        // Named as listed when the manifest is in the folder the check started from; from that folder otherwise.
        string Shown(string path) => System.IO.Path.GetDirectoryName(path) is { } folder && !string.Equals(folder, report.Folder, PathUtil.SafetyComparison)
            ? PathUtil.IsSameOrUnder(path, report.Folder) ? System.IO.Path.GetRelativePath(report.Folder, path) : path
            : System.IO.Path.GetFileName(path);
        string NameOf(ManifestEntry e, ChecksumManifest m) => e.Path is { } p && !string.Equals(m.Root, report.Folder, PathUtil.SafetyComparison)
            ? PathUtil.IsSameOrUnder(p, report.Folder) ? System.IO.Path.GetRelativePath(report.Folder, p) : p
            : e.Name;
        void Missing(ManifestEntry e, string why, string text)
        {
            missing++;
            Job.ItemFailed();
            Issue(IssueSeverity.Error, e.Path!, why, StepOutcome.Failed, MissingCause);
            report.Add(new VerifiedFile(e.Path, e.Name, VerifiedOutcome.Missing, text) { Notes = [why] });
        }
        long bytes = 0;
        bool completed = false;
        try
        {
            for (int i = 0; i < Job.Request.Sources.Count; i++)
            {
                Job.Checkpoint();
                var source = Job.Request.Sources[i];
                string manifestPath = source.FileSystemPath ?? throw new NotSupportedException("Checksum manifests are verified only on disk.");
                string manifestName = Shown(manifestPath);
                // What signs it is found first: then a signature is checked over the very bytes whose lines are verified.
                var claims = FolderSidecars.SignaturesOver(manifestPath);
                ChecksumManifest manifest;
                byte[] content = [];
                try
                {
                    manifest = claims is { Count: > 0 }
                        ? ChecksumManifests.Load(manifestPath, ChecksumManifests.MaxManifestBytes, out content)
                        : ChecksumManifests.Load(manifestPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    string why = $"{manifestName} could not be read: {ErrorText.Describe(ex)}";
                    Issue(IssueSeverity.Error, manifestPath, why, StepOutcome.Failed);
                    // Listed with the files: a checksum file chosen and not read is a problem, not a footnote.
                    report.Add(new VerifiedFile(manifestPath, manifestName, VerifiedOutcome.Unreadable, "could not be read") { Notes = [why] });
                    Job.RootFailed(i);
                    continue;
                }
                if (manifest.Entries.Count == 0)
                {
                    Issue(IssueSeverity.Error, manifestPath, "No checksum lines were found in this file.", StepOutcome.Failed);
                    report.Add(new VerifiedFile(manifestPath, manifestName, VerifiedOutcome.NotChecked, "lists no checksums")
                    {
                        Notes = [$"{manifestName} has no checksum lines: there is nothing in it to check files against."],
                    });
                    Job.RootFailed(i);
                    continue;
                }
                // Checked once, before the files it lists are read: what the signature says holds for each of them.
                var signatures = SignaturesOver(manifestPath, manifestName, claims, content, report, signed, out string? unvouched);
                if (manifest.ClearSigned)
                {
                    string inline = $"{manifestName} carries its own OpenPGP signature (it is clearsigned), which FileCat does not check: what it lists is " +
                                    $"at most intact, not proven authentic. gpg --verify {manifestName} checks that signature.";
                    Issue(IssueSeverity.Info, manifestPath, inline, StepOutcome.Committed);
                    report.Note(inline);
                }
                // The lines the check beside the files compared are keyed by the checksum file as that check found it.
                string listed = System.IO.Path.GetFullPath(manifestPath);
                int notHere = 0;
                foreach (var e in manifest.Entries)
                {
                    // Compared beside its file already (by the check this one followed), exactly as it reads here: that file is
                    // shown there, with this line among its claims.
                    if (compared is not null && e.Problem is null && compared.Contains(new ChecksumClaim(e.Kind, e.Expected, listed, e.Line)))
                    {
                        leftBeside++;
                        continue;
                    }
                    if (e.Problem is not null)
                    {
                        skipped++;
                        Job.ItemSkipped();
                        Issue(IssueSeverity.Warning, manifestPath, $"Line {e.Line}: {e.Problem}", StepOutcome.Skipped);
                        // Named by where it is: the line may hold anything (a bare value, a refused path).
                        report.Add(new VerifiedFile(e.Path, $"{manifestName}, line {e.Line}", VerifiedOutcome.NotChecked, "not checked")
                        {
                            IsLine = true,
                            // A checksum with a name it may not use, or a line that is no checksum at all.
                            Notes = [e.Problem, e.Expected.Length > 0 ? $"It names: {VerificationReport.Cut(e.Name, 260)}" : $"It reads: {e.Name}"],
                        });
                        continue;
                    }
                    var info = Fs.TryGetInfo(e.Path!);
                    if (info is null && Job.Request.IgnoreMissing)
                    {
                        notHere++;
                        continue;
                    }
                    if (info is null || info.IsDirectory)
                    {
                        var entry = e with { Name = NameOf(e, manifest) };
                        if (info is null) Missing(entry, $"Missing: {manifestName} lists it (line {e.Line}), but it is not there.", "missing");
                        else Missing(entry, $"Listed in {manifestName} (line {e.Line}) as a file, but it is a folder.", "a folder, not a file");
                        continue;
                    }
                    if (Checksums.IsCompatibilityOnly(e.Kind)) compatibilityOnly.Add(Checksums.Name(e.Kind));
                    plan.Add((e with { Name = NameOf(e, manifest) }, manifestPath, signatures, unvouched));
                    bytes += Math.Max(0, info.Size);
                }
                if (notHere > 0)
                {
                    absent += notHere;
                    report.Note($"{manifestName} also lists {(notHere == 1 ? "1 file that is not here: it was" : $"{notHere:N0} files that are not here: they were")} " +
                                "not looked for (the folder was checked, not this checksum file).");
                }
            }
            planned = plan.Count;
            Job.AddTotals(plan.Count + skipped + missing, bytes);
            foreach (var (e, manifestPath, signatures, unvouched) in plan)
            {
                Job.Checkpoint();
                Job.SetCurrent(e.Path);
                string manifestName = Shown(manifestPath);
                string actual;
                try
                {
                    var before = Verification.VerificationService.Stamp(e.Path!);
                    actual = Checksums.Compute(e.Path!, e.Kind, Job.Token, n =>
                    {
                        Job.AddBytes(n);
                        Job.AddDataProcessed(n);
                    });
                    // The value read stays for the checks beside files (D-57): a large file then need not be read again.
                    if (before is { } stamp) Verification.VerificationService.Current?.Remember(e.Path!, stamp, new Dictionary<ChecksumKind, string> { [e.Kind] = actual });
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Gone since it was planned: missing, not unreadable.
                    if (ex is FileNotFoundException or DirectoryNotFoundException || Verification.VerificationService.Stamp(e.Path!) is null)
                    {
                        Missing(e, $"Missing: it was moved or deleted while the check ran ({manifestName} lists it, line {e.Line}).", "not there any more");
                        continue;
                    }
                    unreadable++;
                    Job.ItemFailed();
                    string why = "It could not be read: " + ErrorText.Describe(ex);
                    Issue(IssueSeverity.Error, e.Path!, why, StepOutcome.Failed);
                    report.Add(new VerifiedFile(e.Path, e.Name, VerifiedOutcome.Unreadable, "could not be read") { Notes = [why] });
                    continue;
                }
                // Judged and worded as the file's row would be: its checksum, and the signatures over the manifest.
                string listName = System.IO.Path.GetFileName(manifestPath);
                var result = Verifier.Check([new ChecksumClaim(e.Kind, e.Expected, manifestPath, e.Line)], [],
                    _ => new Dictionary<ChecksumKind, string> { [e.Kind] = actual },
                    (claim, _) => signatures.First(s => s.Claim == claim).Result,
                    name => string.Equals(name, listName, StringComparison.Ordinal) ? [.. signatures.Select(s => s.Claim)] : [],
                    Job.Token);
                var entry = VerificationReport.Entry(e.Path!, e.Name, result);
                if (!string.Equals(actual, e.Expected, StringComparison.OrdinalIgnoreCase))
                {
                    mismatched++;
                    Job.ItemFailed();
                    Issue(IssueSeverity.Error, e.Path!, $"Does not match: its {Checksums.Name(e.Kind)} is {actual}; {manifestName} (line {e.Line}) says {e.Expected}.",
                        StepOutcome.Failed, MismatchCause);
                }
                else if (entry.Outcome == VerifiedOutcome.Failed)
                {
                    badSignature++;
                    Job.ItemFailed();
                    Issue(IssueSeverity.Error, e.Path!, $"Its checksum matches {manifestName} (line {e.Line}), but the signature over {manifestName} is bad: the list may have been changed.",
                        StepOutcome.Failed, SignatureCause);
                }
                else if (entry.Outcome == VerifiedOutcome.Good && unvouched is null)
                {
                    verified++;
                    Job.ItemDone();
                }
                else
                {
                    // The checksum matched, but no signature could vouch for the list, or whether one does is not known (said
                    // once, for the manifest).
                    unsure++;
                    Job.ItemDone();
                    entry = entry with
                    {
                        Outcome = VerifiedOutcome.Unsure,
                        Authentic = false,
                        Notes = unvouched is null ? entry.Notes : [unvouched, .. entry.Notes],
                    };
                }
                report.Add(entry);
            }
            if (compatibilityOnly.Count > 0)
                Issue(IssueSeverity.Info, Job.Request.Sources[0].FileSystemPath ?? "",
                    $"{string.Join(" and ", compatibilityOnly)} {(compatibilityOnly.Count == 1 ? "is a compatibility check" : "are compatibility checks")}: a match shows the files are intact, not that they are authentic.",
                    StepOutcome.Committed);
            completed = true;
            report.IsComplete = true;
        }
        finally
        {
            var parts = new List<string>();
            bool ended = completed && !Job.IsCancellationRequested;
            int matched = verified + unsure + badSignature, reached = matched + mismatched + unreadable;
            bool any = matched + mismatched + missing + unreadable > 0;
            // Never "all match" about a check that did not reach every file.
            if (!ended)
            {
                string how = Job.IsCancellationRequested ? "canceled" : "stopped by an error";
                parts.Add(planned > 0 ? $"{how} after {reached:N0} of {planned:N0} files" : $"{how} before any file was read");
                if (matched > 0) parts.Add($"{matched:N0} match");
            }
            // "All match" only about files that were compared: a manifest without a usable line verified nothing.
            else if (!any)
            {
                if (leftBeside > 0)
                {
                    parts.Add("the files it lists were checked beside them");
                    if (skipped > 0) parts.Add($"{skipped:N0} lines not verified");
                }
                else if (absent > 0) parts.Add("none of the files it lists is here");
                else parts.Add(skipped > 0 ? $"nothing verified: none of its {skipped:N0} lines is a checksum line" : "nothing verified: the manifest lists no files");
            }
            // "0 match" says nothing beside what did not.
            else if (matched > 0)
                parts.Add(mismatched == 0 && missing == 0 && unreadable == 0 && badSignature == 0 ? $"{matched:N0} verified, all match" : $"{matched:N0} match");
            if (mismatched > 0) parts.Add($"{mismatched:N0} do not match");
            if (missing > 0) parts.Add($"{missing:N0} missing");
            if (unreadable > 0) parts.Add($"{unreadable:N0} unreadable");
            if (skipped > 0 && matched + mismatched + missing + unreadable > 0) parts.Add($"{skipped:N0} lines not verified");
            if (absent > 0 && any) parts.Add($"{absent:N0} not here, not looked for");
            if (leftBeside > 0 && any) parts.Add($"{leftBeside:N0} checked beside them");
            parts.AddRange(signed);
            Job.SetSummary(string.Join(", ", parts));
        }
    }

    /// <summary>
    /// The signatures over a manifest (<paramref name="claims"/>), checked now over <paramref name="content"/>, the bytes its
    /// lines were read from, and what they say: in the results, in the job's summary, and as an issue when one cannot
    /// vouch for the list (an error when it is bad). <paramref name="unvouched"/> says why its files are only unsure when
    /// whether anything signs it could not be found out (its folder could not be listed) or the signatures not checked.
    /// </summary>
    private IReadOnlyList<SignatureCheck> SignaturesOver(string manifestPath, string name, IReadOnlyList<SignatureClaim>? claims, byte[] content,
        VerificationReport report, List<string> summary, out string? unvouched)
    {
        unvouched = null;
        if (claims is { Count: 0 }) return [];
        if (claims is null || Verification.VerificationService.Current is not { } service)
        {
            unvouched = claims is null
                ? $"Whether a signature vouches for {name} could not be found out: its folder could not be listed."
                : $"The signatures over {name} were not checked: checking signatures is not set up here.";
            Issue(IssueSeverity.Warning, manifestPath, unvouched, StepOutcome.Committed);
            report.Note(unvouched);
            summary.Add($"{name}: signatures not checked");
            return [];
        }
        var checks = service.CheckSignaturesOver(manifestPath, claims, content, Job.Token);
        foreach (var check in checks)
        {
            string text = $"{System.IO.Path.GetFileName(check.Claim.Signature)} (over {name}): {check.Result.Text}.";
            report.Note(text);
            switch (check.Result.State)
            {
                case VerificationState.SignatureGood:
                    summary.Add($"{name} signed by {check.Result.Signer}");
                    break;
                case VerificationState.SignatureBad:
                    summary.Add($"{name}: {Verifier.Short(check.Result)}");
                    Issue(IssueSeverity.Error, manifestPath, text, StepOutcome.Failed, SignatureCause);
                    break;
                default:
                    summary.Add($"{name}: {Verifier.Short(check.Result)}");
                    Issue(IssueSeverity.Warning, manifestPath, text, StepOutcome.Committed);
                    break;
            }
        }
        return checks;
    }
}
