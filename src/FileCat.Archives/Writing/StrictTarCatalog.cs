using System.Buffers;
using System.Formats.Tar;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FileCat.Core.Archives;

namespace FileCat.Archives.Writing;

internal sealed record TarWriteMember(string Name, ArchiveMemberType Type, long Offset, long Length,
    int Mode, int Uid, int Gid, string UserName, string GroupName, decimal Modified,
    decimal? Accessed, decimal? Changed, string Sha256, int Occurrence);

internal sealed record TarWriteCatalog(TarEntryFormat Format, IReadOnlyList<TarWriteMember> Members, string Revision, long Length);

/// <summary>
/// Raw framing/feature observer for writing, separate from tolerant browsing. All lengths and PAX record counts
/// are bounded before decoding strings or constructing TarReader entries. Every payload and all padding are read.
/// This runs in the archive worker; successful scanning alone never authorizes publication.
/// </summary>
internal static class StrictTarCatalog
{
    private const int Block = 512;
    private const int MaximumZeroPadding = 1 << 20;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
        { "path", "size", "mtime", "atime", "ctime", "uid", "gid", "uname", "gname" };

    internal static TarWriteCatalog Read(Stream input, CancellationToken ct)
    {
        using var reader = new RawReader(input, ct);
        var budget = new ArchiveWriteBudget();
        var members = new List<TarWriteMember>();
        var occurrences = new Dictionary<(string, ArchiveMemberType), int>();
        Dictionary<string, string>? pax = null;
        long paxBytes = 0;
        bool sawPax = false;
        byte[] header = new byte[Block];
        while (true)
        {
            reader.Exact(header);
            if (header.AsSpan().IndexOfAnyExcept((byte)0) < 0)
            {
                if (pax is not null) throw Invalid("The TAR ends with metadata that has no member.");
                reader.Exact(header);
                if (header.AsSpan().IndexOfAnyExcept((byte)0) >= 0) throw Invalid("The TAR is missing its second end marker.");
                int padding = 0, read;
                while ((read = reader.Some(header)) != 0)
                {
                    if (read > MaximumZeroPadding - padding || header.AsSpan(0, read).IndexOfAnyExcept((byte)0) >= 0)
                        throw Invalid("The TAR contains unsupported trailing data or excessive padding.");
                    padding += read;
                }
                if (padding % Block != 0) throw Invalid("The TAR ends with a partial padding block.");
                return new TarWriteCatalog(sawPax ? TarEntryFormat.Pax : TarEntryFormat.Ustar,
                    members.AsReadOnly(), reader.Digest(), reader.Position);
            }
            ValidateHeader(header);
            long size = Octal(header.AsSpan(124, 12));
            if (header[156] == (byte)'x')
            {
                if (pax is not null) throw Unsupported("Repeated TAR extension headers are not writable.");
                if (size > ArchiveWritePolicy.MaxEntryMetadataBytes) throw Limit();
                // Reserve the byte buffer before allocation. Pair/string/object retention is charged by ParsePax.
                budget.AddMetadata(size, size);
                byte[] record = new byte[(int)size];
                reader.Exact(record);
                reader.Padding(size);
                pax = ParsePax(record, budget);
                paxBytes = size;
                sawPax = true;
                continue;
            }
            ArchiveMemberType type = header[156] switch
            {
                (byte)'0' => ArchiveMemberType.File,
                (byte)'5' => ArchiveMemberType.Directory,
                _ => throw Unsupported("This TAR contains links, special entries, or an unsupported dialect."),
            };
            budget.AddMember();
            string name = Text(header.AsSpan(0, 100));
            string prefix = Text(header.AsSpan(345, 155));
            if (prefix.Length > 0) name = prefix + "/" + name;
            string user = Text(header.AsSpan(265, 32)), group = Text(header.AsSpan(297, 32));
            int mode = Integer(Octal(header.AsSpan(100, 8))), uid = Integer(Octal(header.AsSpan(108, 8))), gid = Integer(Octal(header.AsSpan(116, 8)));
            if ((mode & ~0xfff) != 0) throw Unsupported("This TAR records unsupported permission bits.");
            decimal modified = Octal(header.AsSpan(136, 12));
            decimal? accessed = null, changed = null;
            if (pax is not null)
            {
                if (pax.TryGetValue("path", out string? path)) name = path;
                if (pax.TryGetValue("size", out string? length))
                {
                    long effective = UnsignedDecimal(length);
                    if (effective != size && !(size == 0 && effective > 0x1ffffffffL))
                        throw Unsupported("The TAR's stored size and extended size disagree.");
                    size = effective;
                }
                if (pax.TryGetValue("uid", out string? owner)) uid = Integer(UnsignedDecimal(owner));
                if (pax.TryGetValue("gid", out string? ownerGroup)) gid = Integer(UnsignedDecimal(ownerGroup));
                if (pax.TryGetValue("uname", out string? ownerName)) user = ownerName;
                if (pax.TryGetValue("gname", out string? groupName)) group = groupName;
                if (pax.TryGetValue("mtime", out string? mtime)) modified = Timestamp(mtime);
                if (pax.TryGetValue("atime", out string? atime)) accessed = Timestamp(atime);
                if (pax.TryGetValue("ctime", out string? ctime)) changed = Timestamp(ctime);
            }
            CheckTime(modified);
            if (size > ArchiveWritePolicy.MaxPayloadBytes || type == ArchiveMemberType.Directory && size != 0) throw Limit();
            ArchiveWritePolicy.ValidateName(name, type);
            if (type == ArchiveMemberType.File && name.EndsWith('/')) throw Invalid("A regular TAR file has a directory name.");
            // Strings, dictionary keys and fixed per-member objects are included in the retained-heap charge.
            long chars = (long)name.Length + user.Length + group.Length;
            if (paxBytes + chars * 4 + 256 > ArchiveWritePolicy.MaxEntryMetadataBytes) throw Limit();
            budget.AddMetadata(checked(chars * 4 + 256), checked(chars * 2 + 1024));
            budget.AddLogicalBytes(size);
            long offset = reader.Position;
            string digest = reader.Payload(size);
            reader.Padding(size);
            var occurrenceKey = (name, type);
            int occurrence = occurrences.GetValueOrDefault(occurrenceKey);
            occurrences[occurrenceKey] = occurrence + 1;
            members.Add(new TarWriteMember(name, type, offset, size, mode, uid, gid, user, group, modified,
                accessed, changed, digest, occurrence));
            pax = null;
            paxBytes = 0;
        }
    }

    /// <summary>Use the runtime decoder independently of raw framing; disagreement is refusal, never a tolerant catalog.</summary>
    internal static void CompareDecoder(Stream input, TarWriteCatalog catalog, CancellationToken ct)
    {
        using var guard = new TarHeaderGuard(input, ArchiveWritePolicy.MaxEntryMetadataBytes);
        using var reader = new TarReader(guard, leaveOpen: true);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 << 10);
        try
        {
            foreach (var expected in catalog.Members)
            {
                ct.ThrowIfCancellationRequested();
                var actual = reader.GetNextEntry(copyData: false) ?? throw Invalid("The TAR decoder returned an incomplete catalog.");
                guard.Returned(actual);
                if (actual is not PosixTarEntry posix || actual.Name != expected.Name || actual.Length != expected.Length ||
                    (actual.EntryType == TarEntryType.Directory) != (expected.Type == ArchiveMemberType.Directory) ||
                    actual.EntryType is not (TarEntryType.Directory or TarEntryType.RegularFile) ||
                    (int)actual.Mode != expected.Mode || actual.Uid != expected.Uid || actual.Gid != expected.Gid ||
                    posix.UserName != expected.UserName || posix.GroupName != expected.GroupName ||
                    actual.ModificationTime.UtcTicks != ToTime(expected.Modified).UtcTicks)
                    throw Invalid("The TAR header and decoded member metadata disagree.");
                var attributes = (actual as PaxTarEntry)?.ExtendedAttributes;
                if (ReadTime(attributes, "atime") != expected.Accessed || ReadTime(attributes, "ctime") != expected.Changed)
                    throw Invalid("The TAR header and decoded timestamp metadata disagree.");
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long total = 0;
                int n;
                while (actual.DataStream is { } data && (n = data.Read(buffer)) != 0)
                {
                    ct.ThrowIfCancellationRequested();
                    if (n > expected.Length - total) throw Invalid("The TAR decoder returned excess member data.");
                    total += n;
                    hash.AppendData(buffer, 0, n);
                }
                if (total != expected.Length || Convert.ToHexString(hash.GetHashAndReset()) != expected.Sha256)
                    throw Invalid("The TAR decoder and raw payload disagree.");
            }
            if (reader.GetNextEntry() is not null) throw Invalid("The TAR decoder returned unaccounted members.");
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static decimal? ReadTime(IReadOnlyDictionary<string, string>? attributes, string key) =>
        attributes is not null && attributes.TryGetValue(key, out string? value) ? Timestamp(value) : null;

    private static void ValidateHeader(ReadOnlySpan<byte> header)
    {
        long checksum = Octal(header.Slice(148, 8));
        long sum = 8 * (byte)' ';
        for (int i = 0; i < Block; i++) if (i is < 148 or >= 156) sum += header[i];
        if (sum != checksum) throw Invalid("A TAR header checksum is invalid.");
        if (!header.Slice(257, 6).SequenceEqual("ustar\0"u8) || !header.Slice(263, 2).SequenceEqual("00"u8))
            throw Unsupported("Only USTAR and PAX TAR archives are writable.");
        // Extension headers also pass through the runtime decoder: validate their otherwise hidden text and
        // numeric fields before it can decode or replace anything in them.
        _ = Text(header[..100]);
        _ = Text(header.Slice(265, 32));
        _ = Text(header.Slice(297, 32));
        _ = Text(header.Slice(345, 155));
        _ = Octal(header.Slice(100, 8));
        _ = Octal(header.Slice(108, 8));
        _ = Octal(header.Slice(116, 8));
        _ = Octal(header.Slice(124, 12));
        _ = Octal(header.Slice(136, 12));
        if (header.Slice(157, 100).IndexOfAnyExcept((byte)0) >= 0 ||
            Octal(header.Slice(329, 8)) != 0 || Octal(header.Slice(337, 8)) != 0 ||
            header[500..].IndexOfAnyExcept((byte)0) >= 0)
            throw Unsupported("This TAR contains unsupported link, device, or header metadata.");
    }

    private static Dictionary<string, string> ParsePax(ReadOnlySpan<byte> bytes, ArchiveWriteBudget budget)
    {
        // First pass counts records and rejects framing before constructing even one string or dictionary entry.
        int count = 0, offset = 0;
        while (offset < bytes.Length)
        {
            var (length, start, equal) = PaxRecord(bytes[offset..]);
            int keyBytes = equal - start, valueBytes = length - equal - 2;
            if (++count > ArchiveWritePolicy.MaxPaxPairsPerEntry || keyBytes > 256 || valueBytes > 256 << 10) throw Limit();
            offset += length;
        }
        budget.AddMetadata(0, checked(bytes.Length * 2L + count * 256L), count);
        var result = new Dictionary<string, string>(count, StringComparer.Ordinal);
        offset = 0;
        while (offset < bytes.Length)
        {
            var (length, start, equal) = PaxRecord(bytes[offset..]);
            string key = Decode(bytes.Slice(offset + start, equal - start));
            if (!AllowedKeys.Contains(key)) throw Unsupported("This TAR has extended metadata that cannot be preserved.");
            string value = Decode(bytes.Slice(offset + equal + 1, length - equal - 2));
            if (value.Length == 0 || value.Any(c => c == '\0' || c is '\r' or '\n') || !result.TryAdd(key, value))
                throw Invalid("The TAR has empty, duplicate, or conflicting extended metadata.");
            offset += length;
        }
        return result;
    }

    private static (int Length, int Start, int Equal) PaxRecord(ReadOnlySpan<byte> bytes)
    {
        int space = bytes.IndexOf((byte)' ');
        if (space < 1 || space > 7 || bytes[0] == (byte)'0') throw Invalid("A PAX record length is invalid.");
        int length = 0;
        foreach (byte b in bytes[..space])
        {
            if (b is < (byte)'0' or > (byte)'9') throw Invalid("A PAX record length is invalid.");
            length = checked(length * 10 + b - '0');
        }
        if (length > bytes.Length || length <= space + 3 || bytes[length - 1] != (byte)'\n') throw Invalid("A PAX record is truncated or invalid.");
        int equal = bytes.Slice(space + 1, length - space - 2).IndexOf((byte)'=');
        if (equal < 1) throw Invalid("A PAX record has no metadata key.");
        return (length, space + 1, space + 1 + equal);
    }

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        int end = bytes.IndexOf((byte)0);
        if (end < 0) end = bytes.Length;
        else if (bytes[end..].IndexOfAnyExcept((byte)0) >= 0) throw Unsupported("A TAR text field contains hidden trailing data.");
        return Decode(bytes[..end]);
    }

    private static string Decode(ReadOnlySpan<byte> bytes)
    {
        try { return Utf8.GetString(bytes); }
        catch (DecoderFallbackException) { throw Invalid("The TAR contains invalid UTF-8 text."); }
    }

    private static long Octal(ReadOnlySpan<byte> bytes)
    {
        if ((bytes[0] & 0x80) != 0) throw Unsupported("This TAR uses unsupported binary numeric fields.");
        bytes = bytes.Trim((ReadOnlySpan<byte>)[0, (byte)' ']);
        long value = 0;
        foreach (byte b in bytes)
        {
            if (b is < (byte)'0' or > (byte)'7' || value > (long.MaxValue >> 3)) throw Invalid("A TAR numeric field is invalid.");
            value = value * 8 + b - '0';
        }
        return value;
    }

    private static long UnsignedDecimal(string text)
    {
        if (text.Length == 0 || text.Any(c => c is < '0' or > '9') ||
            !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long value)) throw Invalid("A PAX integer is invalid.");
        return value;
    }

    private static int Integer(long value) => value <= int.MaxValue ? (int)value : throw Unsupported("A TAR identity or mode is outside the writable range.");
    private static decimal Timestamp(string text)
    {
        if (text.Length > 40 || !decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal value)) throw Invalid("A PAX timestamp is invalid.");
        CheckTime(value);
        return value;
    }

    private static void CheckTime(decimal value)
    {
        if (value < -62135596800m || value > 253402300799.9999999m || decimal.Truncate(value * TimeSpan.TicksPerSecond) != value * TimeSpan.TicksPerSecond)
            throw Unsupported("A TAR timestamp cannot be preserved at its recorded precision.");
    }

    internal static DateTimeOffset ToTime(decimal value)
    {
        CheckTime(value);
        return DateTimeOffset.UnixEpoch.AddTicks((long)(value * TimeSpan.TicksPerSecond));
    }

    private static ArchiveWriteException Invalid(string message) => new(ArchiveWriteReason.InvalidArchive, message);
    private static ArchiveWriteException Unsupported(string message) => new(ArchiveWriteReason.UnsupportedFeature, message);
    private static ArchiveWriteException Limit() => new(ArchiveWriteReason.BudgetExceeded, "The TAR exceeds the current writing resource limits.");

    private sealed class RawReader(Stream input, CancellationToken ct) : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(64 << 10);
        public long Position { get; private set; }

        public int Some(Span<byte> buffer)
        {
            ct.ThrowIfCancellationRequested();
            int n = input.Read(buffer);
            if (n > ArchiveWritePolicy.MaxPhysicalBytes - Position) throw Limit();
            Position += n;
            _hash.AppendData(buffer[..n]);
            return n;
        }

        public void Exact(Span<byte> buffer)
        {
            while (buffer.Length > 0)
            {
                int n = Some(buffer);
                if (n == 0) throw Invalid("The TAR is truncated.");
                buffer = buffer[n..];
            }
        }

        public string Payload(long length)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            while (length > 0)
            {
                var chunk = _buffer.AsSpan(0, (int)Math.Min(length, _buffer.Length));
                Exact(chunk);
                hash.AppendData(chunk);
                length -= chunk.Length;
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        public void Padding(long length)
        {
            int padding = (int)((Block - length % Block) % Block);
            var bytes = _buffer.AsSpan(0, padding);
            Exact(bytes);
            if (bytes.IndexOfAnyExcept((byte)0) >= 0) throw Invalid("The TAR contains nonzero member padding.");
        }

        public string Digest() => Convert.ToHexString(_hash.GetHashAndReset());
        public void Dispose() { _hash.Dispose(); ArrayPool<byte>.Shared.Return(_buffer); }
    }
}
