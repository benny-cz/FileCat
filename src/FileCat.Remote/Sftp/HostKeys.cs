using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

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
}

/// <param name="Fingerprint">"SHA256:…" as OpenSSH prints it.</param>
/// <param name="KnownFingerprint">For <see cref="HostKeyStatus.Changed"/>: the fingerprint that was expected.</param>
/// <param name="Source">Where the matching or conflicting entry came from.</param>
/// <param name="OtherTypesKnown">For <see cref="HostKeyStatus.Unknown"/>: keys of other types are known for the server.</param>
public sealed record HostKeyCheck(HostKeyStatus Status, string Host, int Port, string KeyType, string Fingerprint, string? KnownFingerprint,
    string? Source, bool OtherTypesKnown = false);

/// <summary>
/// Host-key trust (plan §14.1): FileCat's own known_hosts file (read and written), seeded read-only by the user's
/// OpenSSH known_hosts (plain and hashed hosts, [host]:port, wildcards, negation, @revoked). A changed key is a
/// security decision the user makes explicitly; FileCat never edits OpenSSH's files.
/// </summary>
public sealed class HostKeyTrust
{
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
        // FileCat's own decisions come first: a key the user replaced here overrides an old OpenSSH entry.
        foreach (var file in new[] { _ownFile }.Concat(_seedFiles))
        {
            var lines = ReadEntries(file).Where(e => e.Matches(host, port)).ToList();
            if (lines.Any(e => e.Revoked && e.Key.AsSpan().SequenceEqual(keyBlob)))
                return new HostKeyCheck(HostKeyStatus.Revoked, host, port, type, fingerprint, null, file);
            var sameType = lines.Where(e => !e.Revoked && e.KeyType == type).ToList();
            if (sameType.Any(e => e.Key.AsSpan().SequenceEqual(keyBlob)))
                return new HostKeyCheck(HostKeyStatus.Trusted, host, port, type, fingerprint, null, file);
            if (sameType.Count > 0)
                return new HostKeyCheck(HostKeyStatus.Changed, host, port, type, fingerprint, Fingerprint(sameType[0].Key), file);
        }
        bool others = new[] { _ownFile }.Concat(_seedFiles).SelectMany(ReadEntries).Any(e => !e.Revoked && e.Matches(host, port));
        return new HostKeyCheck(HostKeyStatus.Unknown, host, port, type, fingerprint, null, null, others);
    }

    /// <summary>Remembers the key in FileCat's own file, replacing any earlier key of the same type for the server.</summary>
    public void Remember(string host, int port, byte[] keyBlob)
    {
        string type = KeyTypeOf(keyBlob);
        string pattern = port == 22 ? host : $"[{host}]:{port}";
        lock (_lock)
        {
            var keep = new List<string>();
            if (File.Exists(_ownFile))
            {
                foreach (var line in File.ReadAllLines(_ownFile))
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

    private static IEnumerable<KnownHostEntry> ReadEntries(string file)
    {
        string[] lines;
        try
        {
            if (!File.Exists(file)) yield break;
            lines = File.ReadAllLines(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (var line in lines)
        {
            if (KnownHostEntry.Parse(line) is { } entry) yield return entry;
        }
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
