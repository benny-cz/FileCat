using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;

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
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
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
    public static ChecksumManifest Load(string manifestPath)
    {
        var info = new FileInfo(manifestPath);
        if (info.Length > MaxManifestBytes) throw new InvalidDataException($"The manifest is larger than {MaxManifestBytes >> 20} MiB.");
        var bytes = File.ReadAllBytes(manifestPath);
        string text;
        try { text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { text = Encoding.Latin1.GetString(bytes); }
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        return Parse(manifestPath, text);
    }

    public static ChecksumManifest Parse(string manifestPath, string text)
    {
        string root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(manifestPath))!;
        var announced = KindFromName(System.IO.Path.GetFileName(manifestPath));
        var entries = new List<ManifestEntry>();
        int lineNumber = 0;
        foreach (var raw in text.Split('\n'))
        {
            lineNumber++;
            string line = raw.TrimEnd('\r');
            if (line.Trim().Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (entries.Count >= MaxEntries) throw new InvalidDataException($"The manifest lists more than {MaxEntries:N0} files.");
            entries.Add(ParseLine(root, line, lineNumber, announced));
        }
        return new ChecksumManifest(manifestPath, root, entries);
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
/// progress and a result per listed file. Mismatches and missing files are errors; unverifiable lines are reported.
/// </summary>
internal sealed class VerifyChecksumsExecutor(Job job, IFileSystemOperations fs, JobJournal journal) : ExecutorBase(job, fs, journal)
{
    public const string MismatchCause = "checksum-mismatch";
    public const string MissingCause = "checksum-missing";

    public override void Execute()
    {
        int verified = 0, mismatched = 0, missing = 0, unreadable = 0, skipped = 0, planned = 0;
        var compatibilityOnly = new SortedSet<string>(StringComparer.Ordinal);
        var plan = new List<(ManifestEntry Entry, string Manifest)>();
        long bytes = 0;
        try
        {
            for (int i = 0; i < Job.Request.Sources.Count; i++)
            {
                Job.Checkpoint();
                var source = Job.Request.Sources[i];
                string manifestPath = source.FileSystemPath ?? throw new NotSupportedException("Checksum manifests are verified only on disk.");
                ChecksumManifest manifest;
                try { manifest = ChecksumManifests.Load(manifestPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    Issue(IssueSeverity.Error, manifestPath, "The manifest could not be read: " + ErrorText.Describe(ex), StepOutcome.Failed);
                    Job.RootFailed(i);
                    continue;
                }
                if (manifest.Entries.Count == 0)
                {
                    Issue(IssueSeverity.Error, manifestPath, "No checksum lines were found in this file.", StepOutcome.Failed);
                    Job.RootFailed(i);
                    continue;
                }
                foreach (var e in manifest.Entries)
                {
                    if (e.Problem is not null)
                    {
                        skipped++;
                        Job.ItemSkipped();
                        Issue(IssueSeverity.Warning, manifestPath, $"Line {e.Line}: {e.Problem}", StepOutcome.Skipped);
                        continue;
                    }
                    var info = Fs.TryGetInfo(e.Path!);
                    if (info is null || info.IsDirectory)
                    {
                        missing++;
                        Job.ItemFailed();
                        Issue(IssueSeverity.Error, e.Path!, info is null
                            ? $"Missing: {System.IO.Path.GetFileName(manifestPath)} lists it (line {e.Line}), but it does not exist."
                            : $"Listed in {System.IO.Path.GetFileName(manifestPath)} (line {e.Line}) as a file, but it is a folder.", StepOutcome.Failed, MissingCause);
                        continue;
                    }
                    if (Checksums.IsCompatibilityOnly(e.Kind)) compatibilityOnly.Add(Checksums.Name(e.Kind));
                    plan.Add((e, manifestPath));
                    bytes += Math.Max(0, info.Size);
                }
            }
            planned = plan.Count;
            Job.AddTotals(plan.Count + skipped + missing, bytes);
            foreach (var (e, manifestPath) in plan)
            {
                Job.Checkpoint();
                Job.SetCurrent(e.Path);
                try
                {
                    var before = Verification.VerificationService.Stamp(e.Path!);
                    string actual = Checksums.Compute(e.Path!, e.Kind, Job.Token, Job.AddBytes);
                    // The value read stays for the checks beside files (D-57): a large file then need not be read again.
                    if (before is { } stamp) Verification.VerificationService.Current?.Remember(e.Path!, stamp, new Dictionary<ChecksumKind, string> { [e.Kind] = actual });
                    if (string.Equals(actual, e.Expected, StringComparison.OrdinalIgnoreCase))
                    {
                        verified++;
                        Job.ItemDone();
                    }
                    else
                    {
                        mismatched++;
                        Job.ItemFailed();
                        Issue(IssueSeverity.Error, e.Path!, $"Does not match: its {Checksums.Name(e.Kind)} is {actual}; {System.IO.Path.GetFileName(manifestPath)} (line {e.Line}) says {e.Expected}.",
                            StepOutcome.Failed, MismatchCause);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    unreadable++;
                    Job.ItemFailed();
                    Issue(IssueSeverity.Error, e.Path!, "Could not be read: " + ErrorText.Describe(ex), StepOutcome.Failed);
                }
            }
            if (compatibilityOnly.Count > 0)
                Issue(IssueSeverity.Info, Job.Request.Sources[0].FileSystemPath ?? "",
                    $"{string.Join(" and ", compatibilityOnly)} {(compatibilityOnly.Count == 1 ? "is a compatibility check" : "are compatibility checks")}: a match shows the files are intact, not that they are authentic.",
                    StepOutcome.Committed);
        }
        finally
        {
            var parts = new List<string>();
            bool canceled = Job.IsCancellationRequested;
            if (canceled) parts.Add($"canceled after {verified + mismatched + unreadable:N0} of {planned:N0} files");
            // "All match" only about files that were compared: a manifest without a usable line verified nothing.
            if (verified + mismatched + missing + unreadable == 0 && !canceled)
                parts.Add(skipped > 0 ? $"nothing verified: none of its {skipped:N0} lines is a checksum line" : "nothing verified: the manifest lists no files");
            else
                parts.Add(mismatched == 0 && missing == 0 && unreadable == 0 && !canceled ? $"{verified:N0} verified, all match" : $"{verified:N0} match");
            if (mismatched > 0) parts.Add($"{mismatched:N0} do not match");
            if (missing > 0) parts.Add($"{missing:N0} missing");
            if (unreadable > 0) parts.Add($"{unreadable:N0} unreadable");
            if (skipped > 0 && verified + mismatched + missing + unreadable > 0) parts.Add($"{skipped:N0} lines not verified");
            Job.SetSummary(string.Join(", ", parts));
        }
    }
}
