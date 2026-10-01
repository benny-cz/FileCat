using System.Buffers.Binary;
using System.Text;

namespace FileCat.Core.FileSystem;

/// <summary>One item in Windows' Recycle Bin, as its <c>$I</c> record describes it.</summary>
/// <param name="OriginalPath">Where the item was before it was deleted.</param>
/// <param name="Size">Its size when deleted (a folder's: all it held).</param>
/// <param name="DeletedUtc">When it was deleted.</param>
/// <param name="Version">The record's format: 1 (Windows Vista to 8.1) or 2 (Windows 10 and later).</param>
public sealed record RecycleBinRecord(string OriginalPath, long Size, DateTime DeletedUtc, int Version);

/// <summary>
/// The records Windows keeps beside each item in a Recycle Bin folder (<c>$Recycle.Bin\&lt;SID&gt;</c>): <c>$Ixxxxxx</c>
/// describes the item stored as <c>$Rxxxxxx</c>. The layout: the format version, the item's size and the time it was
/// deleted (eight bytes each, little-endian), then the original path in UTF-16 — 260 characters fixed in version 1, its
/// length in characters first (four bytes) in version 2. A record is untrusted input: anything that does not hold
/// together is refused, not guessed at.
/// </summary>
public static class RecycleBinRecords
{
    private const int Header = 24;
    private const int Version1Length = Header + 260 * 2;
    private const int MaxPathCharacters = 32_767;

    /// <summary>The record's item, or null with <paramref name="problem"/> saying why the bytes are not a record.</summary>
    public static RecycleBinRecord? Parse(ReadOnlySpan<byte> data, out string? problem)
    {
        problem = null;
        if (data.Length < Header + 2)
        {
            problem = $"too short for a record ({data.Length} bytes)";
            return null;
        }
        long version = BinaryPrimitives.ReadInt64LittleEndian(data);
        long size = BinaryPrimitives.ReadInt64LittleEndian(data[8..]);
        long fileTime = BinaryPrimitives.ReadInt64LittleEndian(data[16..]);
        ReadOnlySpan<byte> pathBytes;
        switch (version)
        {
            case 1:
                if (data.Length < Version1Length)
                {
                    problem = $"a version 1 record holds {Version1Length} bytes, this one {data.Length}";
                    return null;
                }
                pathBytes = data.Slice(Header, 260 * 2);
                break;
            case 2:
                if (data.Length < Header + 4)
                {
                    problem = $"too short for a version 2 record ({data.Length} bytes)";
                    return null;
                }
                int characters = BinaryPrimitives.ReadInt32LittleEndian(data[Header..]);
                if (characters is <= 1 or > MaxPathCharacters || Header + 4 + (long)characters * 2 > data.Length)
                {
                    problem = $"its path length ({characters} characters) does not fit the record ({data.Length} bytes)";
                    return null;
                }
                pathBytes = data.Slice(Header + 4, characters * 2);
                break;
            default:
                problem = $"unknown record version {version}";
                return null;
        }
        if (size < 0)
        {
            problem = "a negative size";
            return null;
        }
        if (fileTime <= 0 || fileTime > DateTime.MaxValue.ToFileTimeUtc())
        {
            problem = "no deletion time";
            return null;
        }
        string path = Encoding.Unicode.GetString(pathBytes);
        int end = path.IndexOf('\0');
        if (end >= 0) path = path[..end];
        if (path.Length == 0 || !IsWindowsFullPath(path))
        {
            problem = "no full original path";
            return null;
        }
        return new RecycleBinRecord(path, size, DateTime.FromFileTimeUtc(fileTime), (int)version);
    }

    /// <summary>"C:\…" or a share's "\\server\share\…": what the Recycle Bin records (checked as text, on any OS).</summary>
    private static bool IsWindowsFullPath(string path) =>
        path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\' ||
        path.Length >= 5 && path.StartsWith(@"\\", StringComparison.Ordinal) && path.IndexOf('\\', 2) > 2;

    /// <summary>The <c>$R</c> name that holds the item a <c>$I</c> record describes ("$IAB12CD.txt" → "$RAB12CD.txt").</summary>
    public static string? DataName(string recordName) =>
        recordName.Length > 2 && recordName.StartsWith("$I", StringComparison.OrdinalIgnoreCase) ? "$R" + recordName[2..] : null;
}
