using System.Globalization;

namespace FileCat.Remote.Ftp;

internal sealed record FtpPermissionFacts(string Source, ushort? Mode = null, bool InvalidMode = false,
    string? TransferRights = null, string? Unique = null, uint? Owner = null, uint? Group = null,
    string? OwnerName = null, string? GroupName = null)
{
    /// <summary>A Unix LIST mode followed by '+': the server keeps access rules beyond the mode that FTP does not describe.</summary>
    internal bool ListedAccessRules { get; init; }

    internal static FtpPermissionFacts? FromMlsd(string? line, string name, bool directory)
    {
        if (line is null) return null;
        int separator = line.IndexOf(' ');
        if (separator < 0 ||
            !line.AsSpan(separator + 1).SequenceEqual(name.AsSpan()))
            return null;

        var fields = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var facts = line.AsSpan(0, separator);
        while (!facts.IsEmpty)
        {
            int semicolon = facts.IndexOf(';');
            if (semicolon < 0) return null;
            var fact = facts[..semicolon];
            int equals = fact.IndexOf('=');
            if (equals <= 0) return null;
            string key = fact[..equals].ToString();
            if (!fields.TryAdd(key, fact[(equals + 1)..].ToString())) fields[key] = null;
            facts = facts[(semicolon + 1)..];
        }

        if (fields.TryGetValue("type", out string? type) &&
            !string.Equals(type, directory ? "dir" : "file", StringComparison.OrdinalIgnoreCase))
            return null;
        var mode = Octal(fields.GetValueOrDefault("unix.mode"));
        string? rights = fields.GetValueOrDefault("perm");
        if (rights is not null && (rights.Length > 26 || !rights.All(char.IsAsciiLetter))) rights = null;
        return new("MLSD", mode, mode is null && fields.ContainsKey("unix.mode"), rights,
            Text("unique"), Number("unix.owner"), Number("unix.group"), Text("unix.ownername"), Text("unix.groupname"));

        uint? Number(string key) => uint.TryParse(fields.GetValueOrDefault(key), NumberStyles.None, CultureInfo.InvariantCulture, out uint value) ? value : null;
        string? Text(string key) => fields.GetValueOrDefault(key) is { Length: > 0 and <= 256 } value && !value.Any(char.IsControl) ? value : null;
    }

    private static ushort? Octal(string? text)
    {
        if (text is not { Length: >= 1 and <= 4 }) return null;
        ushort value = 0;
        foreach (char digit in text)
        {
            if (digit is < '0' or > '7') return null;
            value = (ushort)(value * 8 + digit - '0');
        }
        return value;
    }

    internal static ushort? ListedMode(ReadOnlySpan<char> mode)
    {
        if (mode.Length != 9) return null;
        ushort value = 0;
        for (int row = 0; row < 3; row++)
        {
            var bits = mode.Slice(row * 3, 3);
            int shift = (2 - row) * 3;
            char special = row == 2 ? 't' : 's';
            if (bits[0] is not ('r' or '-') || bits[1] is not ('w' or '-') ||
                bits[2] != 'x' && bits[2] != '-' && bits[2] != special && bits[2] != char.ToUpperInvariant(special))
                return null;
            if (bits[0] == 'r') value |= (ushort)(4 << shift);
            if (bits[1] == 'w') value |= (ushort)(2 << shift);
            if (bits[2] == 'x' || bits[2] == special) value |= (ushort)(1 << shift);
            if (char.ToLowerInvariant(bits[2]) == special) value |= (ushort)(1 << (11 - row));
        }
        return value;
    }
}
