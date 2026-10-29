using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Diagnostics;
using FileCat.Core.State;

namespace FileCat.Remote.Sftp;

public enum HostKeyStatus
{
    /// <summary>The key matches a remembered or OpenSSH known_hosts entry.</summary>
    Trusted,
    /// <summary>No key of this type is known for the server: first contact, or a new key type.</summary>
    Unknown,
    /// <summary>A different key of the same type is known: possibly an attack. Never accepted automatically.</summary>
    Changed,
    /// <summary>The key is marked @revoked in known_hosts.</summary>
    Revoked,
    /// <summary>A configured trust file could not be inspected; no key may be silently trusted.</summary>
    Unavailable,
}

/// <param name="Fingerprint">"SHA256:…" as OpenSSH prints it.</param>
/// <param name="KnownFingerprint">For <see cref="HostKeyStatus.Changed"/>: the fingerprint that was expected.</param>
/// <param name="Source">Where the matching or conflicting entry came from.</param>
/// <param name="OtherTypesKnown">For <see cref="HostKeyStatus.Unknown"/>: keys of other types are known for the server.</param>
/// <param name="Problem">For <see cref="HostKeyStatus.Unavailable"/>: why, as a sentence for the user.</param>
public sealed record HostKeyCheck(HostKeyStatus Status, string Host, int Port, string KeyType, string Fingerprint, string? KnownFingerprint,
    string? Source, bool OtherTypesKnown = false, string? Problem = null);

/// <summary>
/// Host-key trust (plan §14.1): FileCat's own known_hosts file (read and written), seeded read-only by the user's
/// OpenSSH known_hosts (plain and hashed hosts, [host]:port, wildcards, negation, @revoked). A changed key is a
/// security decision the user makes explicitly; FileCat never edits OpenSSH's files.
/// </summary>
public sealed class HostKeyTrust
{
    /// <summary>FileCat's own file, which <see cref="Remember"/> rewrites whole.</summary>
    private const int MaxKnownHostsBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Checking reads each file a line at a time and keeps only the server's entries, so a long known_hosts (an
    /// organisation's) is read, not refused for every server (review finding R-M14); a runaway one still is.
    /// </summary>
    private const long MaxCheckedBytes = 64L << 20;
    private const int MaxLineChars = 64 << 10, MaxEntriesPerServer = 1024;
    private readonly string _ownFile;
    private readonly IReadOnlyList<string> _seedFiles;
    private readonly object _lock = new();

    public HostKeyTrust(string ownFile, IReadOnlyList<string>? seedFiles = null)
    {
        _ownFile = ownFile;
        _seedFiles = seedFiles ?? DefaultSeedFiles();
    }

    public static IReadOnlyList<string> DefaultSeedFiles()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return [Path.Combine(home, ".ssh", "known_hosts"), Path.Combine(home, ".ssh", "known_hosts2")];
    }

    /// <summary>The key type from the key blob ("ssh-ed25519", "ssh-rsa"…), the name known_hosts uses.</summary>
    public static string KeyTypeOf(byte[] keyBlob)
    {
        if (keyBlob.Length < 4) return "unknown";
        int length = (int)BinaryPrimitives.ReadUInt32BigEndian(keyBlob);
        return length > 0 && length <= keyBlob.Length - 4 ? Encoding.ASCII.GetString(keyBlob, 4, length) : "unknown";
    }

    public static string Fingerprint(byte[] keyBlob) => "SHA256:" + Convert.ToBase64String(SHA256.HashData(keyBlob)).TrimEnd('=');

    public HostKeyCheck Check(string host, int port, byte[] keyBlob)
    {
        string type = KeyTypeOf(keyBlob);
        string fingerprint = Fingerprint(keyBlob);
        if (WindowsTokenAuthority.IsHighIntegrity)
            return new HostKeyCheck(HostKeyStatus.Unavailable, host, port, type, fingerprint, null, _ownFile,
                Problem: "FileCat does not read user-profile host-key trust while running as administrator. Reopen it without elevation.");
        var sources = new List<(string File, List<KnownHostEntry> Matching)>();
        foreach (var file in new[] { _ownFile }.Concat(_seedFiles))
        {
            // A file that cannot be read whole might hold this key's revocation: no key is trusted past it.
            if (!TryReadEntries(file, host, port, out var matching, out string? problem))
                return new HostKeyCheck(HostKeyStatus.Unavailable, host, port, type, fingerprint, null, file,
                    Problem: $"{file} {problem}, so no key is trusted without it.");
            sources.Add((file, matching));
        }
        // An exact revocation in any configured file vetoes earlier positive pins.
        foreach (var (file, lines) in sources)
            if (lines.Any(e => e.Revoked && e.Key.AsSpan().SequenceEqual(keyBlob)))
                return new HostKeyCheck(HostKeyStatus.Revoked, host, port, type, fingerprint, null, file);

        // Without a revocation, FileCat's own decisions still take precedence over OpenSSH seeds.
        foreach (var (file, lines) in sources)
        {
            var sameType = lines.Where(e => !e.Revoked && e.KeyType == type).ToList();
            if (sameType.Any(e => e.Key.AsSpan().SequenceEqual(keyBlob)))
                return new HostKeyCheck(HostKeyStatus.Trusted, host, port, type, fingerprint, null, file);
            if (sameType.Count > 0)
                return new HostKeyCheck(HostKeyStatus.Changed, host, port, type, fingerprint, Fingerprint(sameType[0].Key), file);
        }
        bool others = sources.Any(source => source.Matching.Any(e => !e.Revoked));
        return new HostKeyCheck(HostKeyStatus.Unknown, host, port, type, fingerprint, null, null, others);
    }

    /// <summary>Remembers the key in FileCat's own file, replacing any earlier key of the same type for the server.</summary>
    public void Remember(string host, int port, byte[] keyBlob)
    {
        if (WindowsTokenAuthority.IsHighIntegrity)
            throw new IOException("FileCat does not read or save user-profile host-key trust while running as administrator. Reopen it without elevation.");
        string type = KeyTypeOf(keyBlob);
        string pattern = port == 22 ? host : $"[{host}]:{port}";
        lock (_lock)
        {
            var keep = new List<string>();
            if (File.Exists(_ownFile))
            {
                var lines = BoundedTrustText.ReadLines(_ownFile, MaxKnownHostsBytes);
                if (lines is null)
                {
                    AppLog.Warn("FileCat's known-hosts file exceeds its input budget; existing trust entries were left untouched.");
                    return;
                }
                foreach (var line in lines)
                {
                    var entry = KnownHostEntry.Parse(line);
                    if (entry is not null && !entry.Revoked && entry.KeyType == type && entry.Matches(host, port)) continue;
                    keep.Add(line);
                }
            }
            keep.Add($"{pattern} {type} {Convert.ToBase64String(keyBlob)}");
            Directory.CreateDirectory(Path.GetDirectoryName(_ownFile)!);
            string temp = _ownFile + ".tmp";
            File.WriteAllText(temp, string.Join('\n', keep) + "\n", new UTF8Encoding(false));
            File.Move(temp, _ownFile, overwrite: true);
        }
    }

    /// <summary>The server's entries in one trust file; false, with why, when the file cannot be read to its end.</summary>
    private static bool TryReadEntries(string file, string host, int port, out List<KnownHostEntry> matching, out string? problem)
    {
        var found = new List<KnownHostEntry>();
        matching = found;
        try
        {
            problem = BoundedTrustText.ForEachLine(file, MaxCheckedBytes, MaxLineChars, line =>
            {
                if (KnownHostEntry.Parse(line) is not { } entry || !entry.Matches(host, port)) return;
                if (found.Count == MaxEntriesPerServer)
                    throw new InvalidDataException($"has more than {MaxEntriesPerServer:N0} entries for this server");
                found.Add(entry);
            });
        }
        catch (InvalidDataException ex) { problem = ex.Message; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { problem = null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { problem = "could not be read (" + ex.Message.TrimEnd('.') + ")"; }
        if (problem is null) return true;
        AppLog.Warn($"Trust file {AppLog.P(file)} {problem}; host-key trust was not granted.");
        return false;
    }
}

/// <summary>One known_hosts line.</summary>
internal sealed class KnownHostEntry
{
    private KnownHostEntry(string[] patterns, string keyType, byte[] key, bool revoked)
    {
        Patterns = patterns;
        KeyType = keyType;
        Key = key;
        Revoked = revoked;
    }

    public string[] Patterns { get; }
    public string KeyType { get; }
    public byte[] Key { get; }
    public bool Revoked { get; }

    public static KnownHostEntry? Parse(string line)
    {
        var text = line.Trim();
        if (text.Length == 0 || text.StartsWith('#')) return null;
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        bool revoked = false;
        int at = 0;
        if (parts[0].StartsWith('@'))
        {
            // Certificate authorities vouch for other keys; FileCat does not use host certificates.
            if (parts[0] != "@revoked") return null;
            revoked = true;
            at = 1;
        }
        if (parts.Length < at + 3) return null;
        try
        {
            return new KnownHostEntry(parts[at].Split(','), parts[at + 1], Convert.FromBase64String(parts[at + 2]), revoked);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public bool Matches(string host, int port)
    {
        string name = port == 22 ? host : $"[{host}]:{port}";
        bool matched = false;
        foreach (var raw in Patterns)
        {
            bool negated = raw.StartsWith('!');
            string pattern = negated ? raw[1..] : raw;
            bool hit = pattern.StartsWith("|1|", StringComparison.Ordinal) ? HashedMatch(pattern, name) : WildcardMatch(pattern, name);
            if (hit && negated) return false;
            matched |= hit;
        }
        return matched;
    }

    private static bool HashedMatch(string pattern, string name)
    {
        var fields = pattern.Split('|');
        if (fields.Length != 4) return false;
        try
        {
            var salt = Convert.FromBase64String(fields[2]);
            var expected = Convert.FromBase64String(fields[3]);
            return CryptographicOperations.FixedTimeEquals(HMACSHA1.HashData(salt, Encoding.UTF8.GetBytes(name)), expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>OpenSSH patterns: '*' any run, '?' one character; host names compare case-insensitively.</summary>
    private static bool WildcardMatch(string pattern, string text)
    {
        if (!pattern.Contains('*') && !pattern.Contains('?')) return string.Equals(pattern, text, StringComparison.OrdinalIgnoreCase);
        int p = 0, t = 0, star = -1, mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToLowerInvariant(pattern[p]) == char.ToLowerInvariant(text[t]))) { p++; t++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = t; }
            else if (star >= 0) { p = star + 1; t = ++mark; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }
}
