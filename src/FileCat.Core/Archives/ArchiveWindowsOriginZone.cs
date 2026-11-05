using System.Text;

namespace FileCat.Core.Archives;

/// <summary>
/// A bounded, deliberately closed interpretation of a captured Zone.Identifier declaration. This is not an effective
/// Windows trust/policy decision and never maps URLs, consults policy, or performs IO. Unsupported/ambiguous marks
/// refuse; absence exists only when the native capture positively reported it. Unknown fields remain opaque evidence.
/// </summary>
public sealed class ArchiveWindowsOriginZone
{
    private static readonly UnicodeEncoding Utf16Le = new(false, false, true);
    private readonly int zoneByteOffset;
    public ArchiveOriginSnapshot Snapshot { get; }
    public int? DeclaredZone { get; }
    public bool IsAbsent => Snapshot.State == ArchiveOriginState.Absent;

    private ArchiveWindowsOriginZone(ArchiveOriginSnapshot snapshot, int? zone, int offset = 0)
    { Snapshot = snapshot; DeclaredZone = zone; zoneByteOffset = offset; }

    public static ArchiveWindowsOriginZone Parse(ArchiveOriginSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Format != ArchiveOriginFormat.WindowsZoneIdentifier) throw Unsupported();
        if (snapshot.State == ArchiveOriginState.Unavailable)
            throw new ArchiveWriteException(ArchiveWriteReason.PublicationUnavailable, "The source origin mark could not be read; it cannot be treated as unmarked.");
        if (snapshot.State == ArchiveOriginState.Absent) return new(snapshot, null);
        ReadOnlySpan<byte> bytes = snapshot.Bytes;
        Encoding encoding = ArchiveWritePolicy.Utf8; int preamble = 0, width = 1;
        // Windows' own PersistentZoneIdentifier rejected these candidates in native qualification. A successful
        // general-purpose text decode is not evidence that Windows will recognize the resulting origin declaration.
        if (bytes.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) || bytes.StartsWith(new byte[] { 0xfe, 0xff })) throw Unsupported();
        if (bytes.StartsWith(new byte[] { 0xff, 0xfe })) { encoding = Utf16Le; preamble = 2; width = 2; }
        string text;
        try { text = encoding.GetString(bytes[preamble..]); }
        catch (DecoderFallbackException) { throw Unsupported(); }
        foreach (char c in text)
            if (char.IsControl(c) && c is not ('\r' or '\n' or '\t')) throw Unsupported();
        bool seenSection = false, inSection = false; int zone = -1, digitIndex = -1;
        for (int start = 0; start < text.Length;)
        {
            int end = start;
            while (end < text.Length && text[end] is not ('\r' or '\n')) end++;
            int lineStart = start, lineEnd = end;
            while (lineStart < lineEnd && Space(text[lineStart])) lineStart++;
            while (lineEnd > lineStart && Space(text[lineEnd - 1])) lineEnd--;
            var line = text.AsSpan(lineStart, lineEnd - lineStart);
            if (!line.IsEmpty && line[0] != ';')
            {
                if (line[0] == '[')
                {
                    if (line.Length < 3 || line[^1] != ']' || line[1..^1].IndexOfAny('[', ']') >= 0) throw Unsupported();
                    inSection = line[1..^1].Equals("ZoneTransfer", StringComparison.OrdinalIgnoreCase);
                    if (!inSection || seenSection) throw Unsupported();
                    seenSection = true;
                }
                else
                {
                    int equal = line.IndexOf('=');
                    if (equal <= 0) throw Unsupported();
                    int keyEnd = equal;
                    while (keyEnd > 0 && Space(line[keyEnd - 1])) keyEnd--;
                    if (keyEnd == 0) throw Unsupported();
                    foreach (char c in line[..keyEnd])
                        if (c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-' or '.')) throw Unsupported();
                    if (inSection && line[..keyEnd].Equals("ZoneId", StringComparison.OrdinalIgnoreCase))
                    {
                        if (zone >= 0) throw Unsupported();
                        int valueStart = equal + 1;
                        while (valueStart < line.Length && Space(line[valueStart])) valueStart++;
                        if (line.Length - valueStart != 1 || line[valueStart] is < '0' or > '4') throw Unsupported();
                        zone = line[valueStart] - '0'; digitIndex = lineStart + valueStart;
                    }
                }
            }
            start = end;
            if (start < text.Length && text[start] == '\r') start++;
            if (start < text.Length && text[start] == '\n') start++;
        }
        if (zone < 0 || !seenSection) throw Unsupported();
        int offset = checked(preamble + encoding.GetByteCount(text.AsSpan(0, digitIndex)));
        if (offset < 0 || offset > bytes.Length - width) throw Unsupported();
        return new(snapshot, zone, offset);
    }

    /// <summary>Retains the exact existing mark, including URL evidence, changing only its one declared zone digit.</summary>
    public ArchiveOriginSnapshot RaiseTo(int requiredZone)
    {
        ValidateZone(requiredZone);
        if (DeclaredZone is not { } current) throw new InvalidOperationException("An absent mark has no declaration to raise.");
        if (requiredZone <= current) return Snapshot;
        byte[] bytes = Snapshot.Bytes.ToArray();
        bytes[zoneByteOffset] = (byte)('0' + requiredZone);
        return ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier, bytes);
    }

    /// <summary>A zone-only mark has no invented source URL; captured input evidence must be retained separately.</summary>
    public static ArchiveOriginSnapshot ZoneOnly(int declaredZone)
    {
        ValidateZone(declaredZone);
        return ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier,
            ArchiveWritePolicy.Utf8.GetBytes("[ZoneTransfer]\r\nZoneId=" + (char)('0' + declaredZone) + "\r\n"));
    }

    private static bool Space(char c) => c is ' ' or '\t';
    private static void ValidateZone(int zone)
    { if (zone is < 0 or > 4) throw new ArchiveWriteException(ArchiveWriteReason.InvalidRequest, "Only declared Windows zones zero through four are supported."); }
    private static ArchiveWriteException Unsupported() => new(ArchiveWriteReason.UnsupportedFeature,
        "The Windows origin mark is ambiguous or uses unsupported text/zone features; keep it for review instead of treating it as unmarked.");
}
