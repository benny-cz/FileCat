using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;
using FileCat.Core.Diagnostics;
using DiscUtils;
using DiscUtils.Iso9660;
using DiscUtils.Udf;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.SevenZip;

using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.Xz;
using SharpCompress.Readers;

namespace FileCat.Archives;

public enum ArchiveKind
{
    Tar, TarGzip, TarBzip2, TarXz, TarZstd,
    Gzip, Bzip2, Xz, Zstd,
    SevenZip, Rar, DiscImage,
}

public enum MemberKind { File, Directory, SymbolicLink, HardLink, Special }

/// <summary>One member as its format lists it; <see cref="Index"/> is its position for <see cref="IMemberReader.Open"/>.</summary>
public sealed record MemberInfo(int Index, string Path, MemberKind Kind, long Size, long CompressedSize, DateTime? ModifiedUtc, bool Encrypted, string? LinkTarget = null);

/// <summary>A format reader over one archive. Callers serialize access; sequential formats keep a forward cursor.</summary>
public interface IMemberReader : IDisposable
{
    string Format { get; }

    /// <summary>Members in archive order. Damage after some members ends the list with a warning.</summary>
    IEnumerable<MemberInfo> List(Action<string> warn, CancellationToken ct);

    /// <summary>The content of member <paramref name="index"/>; valid until the next call. Disposing it is allowed.</summary>
    Stream Open(int index, CancellationToken ct);

    /// <summary>Members share one forward cursor: opening one invalidates a stream opened before.</summary>
    bool SharesCursor { get; }
}

/// <summary>Formats FileCat reads (ADR-07, P8), recognized by name or by signature, all read-only.</summary>
public static class ArchiveFormats
{
    private static readonly (string Suffix, ArchiveKind Kind)[] Suffixes =
    [
        (".tar.gz", ArchiveKind.TarGzip), (".tgz", ArchiveKind.TarGzip), (".taz", ArchiveKind.TarGzip),
        (".tar.bz2", ArchiveKind.TarBzip2), (".tbz2", ArchiveKind.TarBzip2), (".tbz", ArchiveKind.TarBzip2), (".tb2", ArchiveKind.TarBzip2),
        (".tar.xz", ArchiveKind.TarXz), (".txz", ArchiveKind.TarXz),
        (".tar.zst", ArchiveKind.TarZstd), (".tzst", ArchiveKind.TarZstd),
        (".tar", ArchiveKind.Tar), (".cbt", ArchiveKind.Tar),
        (".gz", ArchiveKind.Gzip), (".bz2", ArchiveKind.Bzip2), (".xz", ArchiveKind.Xz), (".zst", ArchiveKind.Zstd),
        (".7z", ArchiveKind.SevenZip), (".cb7", ArchiveKind.SevenZip),
        (".rar", ArchiveKind.Rar), (".cbr", ArchiveKind.Rar),
        (".iso", ArchiveKind.DiscImage), (".udf", ArchiveKind.DiscImage),
    ];

    public static ArchiveKind? ByName(string fileName)
    {
        foreach (var (suffix, kind) in Suffixes)
            if (fileName.Length > suffix.Length && fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return kind;
        return null;
    }

    /// <summary>The format from the file's first bytes (Ctrl+PgDn on a file without a known extension).</summary>
    public static ArchiveKind? BySignature(Stream s)
    {
        Span<byte> head = stackalloc byte[512];
        s.Position = 0;
        int n = s.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        head = head[..n];
        if (n >= 6 && head[..6].SequenceEqual((ReadOnlySpan<byte>)[0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C])) return ArchiveKind.SevenZip;
        if (n >= 7 && head[..6].SequenceEqual("Rar!\x1A\x07"u8) &&
            (head[6] == 0 || n >= 8 && head[6] == 1 && head[7] == 0)) return ArchiveKind.Rar;
        if (n >= 2 && head[0] == 0x1F && head[1] == 0x8B) return ArchiveKind.Gzip;
        if (n >= 3 && head[..3].SequenceEqual("BZh"u8)) return ArchiveKind.Bzip2;
        if (n >= 6 && head[..6].SequenceEqual((ReadOnlySpan<byte>)[0xFD, 0x37, 0x7A, 0x58, 0x5A, 0x00])) return ArchiveKind.Xz;
        if (n >= 4 && head[..4].SequenceEqual((ReadOnlySpan<byte>)[0x28, 0xB5, 0x2F, 0xFD])) return ArchiveKind.Zstd;
        if (n >= 262 && head.Slice(257, 5).SequenceEqual("ustar"u8)) return ArchiveKind.Tar;
        if (s.Length > 0x8006)
        {
            Span<byte> cd = stackalloc byte[5];
            s.Position = 0x8001;
            if (s.ReadAtLeast(cd, 5, throwOnEndOfStream: false) == 5 && (cd.SequenceEqual("CD001"u8) || cd.SequenceEqual("BEA01"u8))) return ArchiveKind.DiscImage;
        }
        return null;
    }

    public static string Describe(ArchiveKind kind) => kind switch
    {
        ArchiveKind.Tar => "TAR",
        ArchiveKind.TarGzip => "TAR, gzip-compressed",
        ArchiveKind.TarBzip2 => "TAR, bzip2-compressed",
        ArchiveKind.TarXz => "TAR, xz-compressed",
        ArchiveKind.TarZstd => "TAR, zstd-compressed",
        ArchiveKind.Gzip => "gzip",
        ArchiveKind.Bzip2 => "bzip2",
        ArchiveKind.Xz => "xz",
        ArchiveKind.Zstd => "zstd",
        ArchiveKind.SevenZip => "7z",
        ArchiveKind.Rar => "RAR",
        _ => "disc image",
    };

    /// <summary>Opens a reader; the archive file is read-shared so others may keep using it.</summary>
    public static IMemberReader Open(string path, ArchiveKind kind, string displayName) => kind switch
    {
        ArchiveKind.Tar => new TarMemberReader(path, null, "TAR"),
        ArchiveKind.TarGzip => new TarMemberReader(path, s => new GZipStream(s, System.IO.Compression.CompressionMode.Decompress), "TAR (gzip)"),
        ArchiveKind.TarBzip2 => new TarMemberReader(path, s => BZip2Stream.Create(s, SharpCompress.Compressors.CompressionMode.Decompress, false, false, true), "TAR (bzip2)"),
        ArchiveKind.TarXz => new TarMemberReader(path, s => new XZStream(s), "TAR (xz)"),
        ArchiveKind.TarZstd => new TarMemberReader(path, s => new SharpCompress.Compressors.ZStandard.DecompressionStream(s, 0, false, false), "TAR (zstd)"),
        ArchiveKind.Gzip => new SingleStreamReader(path, s => new GZipStream(s, System.IO.Compression.CompressionMode.Decompress), "gzip", displayName, ".gz"),
        ArchiveKind.Bzip2 => new SingleStreamReader(path, s => BZip2Stream.Create(s, SharpCompress.Compressors.CompressionMode.Decompress, false, false, true), "bzip2", displayName, ".bz2"),
        ArchiveKind.Xz => new SingleStreamReader(path, s => new XZStream(s), "xz", displayName, ".xz"),
        ArchiveKind.Zstd => new SingleStreamReader(path, s => new SharpCompress.Compressors.ZStandard.DecompressionStream(s, 0, false, false), "zstd", displayName, ".zst"),
        ArchiveKind.SevenZip => SharpArchiveReader.Open(path, rar: false),
        ArchiveKind.Rar => SharpArchiveReader.Open(path, rar: true),
        _ => new DiscImageReader(path),
    };

    internal static FileStream OpenShared(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);

    internal static DateTime? Utc(DateTime? time) => time switch
    {
        null => null,
        { Kind: DateTimeKind.Local } t => t.ToUniversalTime(),
        { Kind: DateTimeKind.Unspecified } t => DateTime.SpecifyKind(t, DateTimeKind.Utc),
        var t => t,
    };
}

/// <summary>A read-only view of part of a stream (a plain TAR member), without copying.</summary>
internal sealed class SliceStream(Stream inner, long start, long length) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        long left = length - _position;
        if (left <= 0) return 0;
        if (buffer.Length > left) buffer = buffer[..(int)left];
        inner.Position = start + _position;
        int n = inner.Read(buffer);
        _position += n;
        return n;
    }

    public override void Flush() { }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => length + offset,
    };

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>A stream the caller may dispose without closing the reader's cursor underneath.</summary>
internal sealed class BorrowedStream(Stream inner) : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => inner.Read(buffer);
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Counts what a decompressor produced (for the expansion-ratio limit while listing).</summary>
internal sealed class CountingStream(Stream inner) : Stream
{
    public long Count { get; private set; }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => Count; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int n = inner.Read(buffer, offset, count);
        Count += n;
        return n;
    }

    public override int Read(Span<byte> buffer)
    {
        int n = inner.Read(buffer);
        Count += n;
        return n;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Stands between a TAR stream and .NET's <see cref="TarReader"/> (trust boundary B02). The reader takes a PAX extended
/// header or a GNU long name ('x', 'g', 'L', 'K') whole, into an array as long as its header says, up to 2 GiB, before it
/// finds out whether that much data follows: one damaged size field in a 31 KiB archive made it take 512 MiB. Each such
/// header is checked here as it passes, before the reader acts on it: its data may not exceed <see cref="MaxMetadata"/>,
/// nor run past the end of a plain archive. Headers are where the TAR framing puts them: each one's data follows it, and
/// after each member the reader returns it is told so (<see cref="Returned"/>).
/// </summary>
internal sealed class TarHeaderGuard(Stream inner) : Stream
{
    /// <summary>The most metadata one member may carry (its PAX attributes, its GNU long name): far beyond real archives.</summary>
    public const int MaxMetadata = 16 << 20;

    private const int Block = 512;
    private readonly byte[] _header = new byte[Block];
    private long _read;
    // Where the next header starts (-1: within a member, until the reader returns it) and how much of it has passed.
    private long _next;
    private int _filled;
    // Once a header is refused, the reader is left mid-header: nothing more is read through it.
    private string? _refusal;

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.CanSeek ? inner.Position : _read; set => inner.Position = value; }

    /// <summary>The reader returned <paramref name="entry"/>: the next header follows its data.</summary>
    public void Returned(TarEntry entry)
    {
        // A seekable stream is left at the next header; otherwise at the member's data, which the reader skips later. A
        // global PAX header's data was taken whole (it has no data stream).
        _next = inner.CanSeek ? inner.Position : _read + (entry.DataStream is null ? 0 : Blocks(entry.Length));
        _filled = 0;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_refusal is not null) throw new InvalidDataException(_refusal);
        long at = Position;
        int n = inner.Read(buffer);
        _read = at + n;
        Inspect(at, buffer[..n]);
        return n;
    }

    private void Inspect(long at, ReadOnlySpan<byte> read)
    {
        while (_next >= 0)
        {
            long want = _next + _filled;
            if (want < at)
            {
                // The header was passed by without being read: nothing to check until the next member.
                _next = -1;
                return;
            }
            if (want - at >= read.Length) return;
            int offset = (int)(want - at), take = Math.Min(Block - _filled, read.Length - offset);
            read.Slice(offset, take).CopyTo(_header.AsSpan(_filled));
            _filled += take;
            if (_filled < Block) return;
            Check();
        }
    }

    private void Check()
    {
        long start = _next;
        _next = -1;
        _filled = 0;
        if (_header[156] is not ((byte)'x' or (byte)'g' or (byte)'L' or (byte)'K')) return;
        if (Size(_header.AsSpan(124, 12)) is not long size) return;
        if (size > MaxMetadata)
            _refusal = $"A member's header gives it {size:N0} bytes of metadata, more than FileCat reads ({MaxMetadata >> 20} MiB): the archive is damaged.";
        else if (inner.CanSeek && size > inner.Length - start - Block)
            _refusal = "A member's metadata runs past the end of the archive: it is damaged or cut short.";
        if (_refusal is not null) throw new InvalidDataException(_refusal);
        _next = start + Block + Blocks(size);
    }

    private static long Blocks(long size) => (size + Block - 1) / Block * Block;

    /// <summary>A header's size field as <see cref="TarReader"/> reads it; null when it refuses the field itself.</summary>
    private static long? Size(ReadOnlySpan<byte> field)
    {
        if (field[0] == 0xFF) return null; // base-256, negative
        if (field[0] == 0x80)
        {
            ulong big = 0;
            foreach (byte b in field[1..])
            {
                if (big > ulong.MaxValue >> 8) return long.MaxValue;
                big = big << 8 | b;
            }
            return big > long.MaxValue ? long.MaxValue : (long)big;
        }
        field = field.Trim((ReadOnlySpan<byte>)[0, (byte)' ']);
        long value = 0;
        foreach (byte b in field)
        {
            uint digit = (uint)(b - '0');
            if (digit >= 8) return null;
            if (value > long.MaxValue >> 3) return long.MaxValue;
            value = value << 3 | digit;
        }
        return value;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>
/// TAR through .NET's <see cref="TarReader"/>: a plain file reads members in place; a compressed one keeps one forward
/// cursor, so extracting members in archive order decompresses the stream once.
/// </summary>
internal sealed class TarMemberReader(string path, Func<Stream, Stream>? decompress, string format) : IMemberReader
{
    /// <summary>Beyond this expansion (after 1 GiB), listing stops: a likely decompression bomb.</summary>
    public const long MaxListingRatio = 1000;
    private readonly List<(long Offset, long Length)> _plainData = [];
    private FileStream? _plain;
    private FileStream? _cursorFile;
    private Stream? _cursorStream;
    private TarHeaderGuard? _cursorGuard;
    private TarReader? _cursorReader;
    private TarEntry? _cursorEntry;
    private int _cursorIndex = -1;

    public string Format => format;

    public bool SharesCursor => decompress is not null;

    private (FileStream File, Stream Data) OpenStream()
    {
        var file = ArchiveFormats.OpenShared(path);
        return (file, decompress is null ? file : new CountingStream(decompress(file)));
    }

    public IEnumerable<MemberInfo> List(Action<string> warn, CancellationToken ct)
    {
        var (file, data) = OpenStream();
        var guard = new TarHeaderGuard(data);
        using (file)
        using (data)
        using (var reader = new TarReader(guard, leaveOpen: true))
        {
            int index = -1;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                TarEntry? entry;
                try
                {
                    entry = reader.GetNextEntry(copyData: false);
                    if (entry is not null) guard.Returned(entry);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Any damage, including a decompressor's own error types, ends the list here.
                    warn(index < 0 ? "This is not a TAR archive: " + ex.Message : "The archive is damaged after the members listed: " + ex.Message);
                    if (index < 0) throw new InvalidDataException("Not a TAR archive.", ex);
                    yield break;
                }
                if (entry is null) yield break;
                index++;
                if (decompress is null) _plainData.Add((entry.DataOffset, entry.Length));
                if (data is CountingStream counting && counting.Count > (1L << 30) && counting.Count / Math.Max(1, file.Position) > MaxListingRatio)
                {
                    warn("The archive expands more than 1000:1 (possibly a decompression bomb); listing stopped.");
                    yield break;
                }
                var kind = entry.EntryType switch
                {
                    TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile => MemberKind.File,
                    TarEntryType.Directory => MemberKind.Directory,
                    TarEntryType.SymbolicLink => MemberKind.SymbolicLink,
                    TarEntryType.HardLink => MemberKind.HardLink,
                    TarEntryType.GlobalExtendedAttributes or TarEntryType.ExtendedAttributes or TarEntryType.LongLink or TarEntryType.LongPath => (MemberKind?)null,
                    _ => MemberKind.Special,
                };
                if (kind is null) continue;
                DateTime? modified;
                try { modified = entry.ModificationTime.UtcDateTime; }
                catch (ArgumentOutOfRangeException) { modified = null; }
                yield return new MemberInfo(index, entry.Name, kind.Value, kind == MemberKind.File ? entry.Length : 0, -1, modified, false,
                    kind is MemberKind.SymbolicLink or MemberKind.HardLink ? entry.LinkName : null);
            }
        }
    }

    public Stream Open(int index, CancellationToken ct)
    {
        if (decompress is null)
        {
            _plain ??= ArchiveFormats.OpenShared(path);
            var (offset, length) = _plainData[index];
            if (offset < 0) throw new InvalidDataException("The member's data cannot be located.");
            return new SliceStream(_plain, offset, length);
        }
        if (_cursorReader is null || index <= _cursorIndex) Restart();
        while (_cursorIndex < index)
        {
            ct.ThrowIfCancellationRequested();
            _cursorEntry = _cursorReader!.GetNextEntry(copyData: false) ?? throw new FileNotFoundException("The member is no longer in the archive.");
            _cursorGuard!.Returned(_cursorEntry);
            _cursorIndex++;
        }
        return new BorrowedStream(_cursorEntry!.DataStream ?? Stream.Null);
    }

    private void Restart()
    {
        CloseCursor();
        (_cursorFile, _cursorStream) = OpenStream();
        _cursorGuard = new TarHeaderGuard(_cursorStream);
        _cursorReader = new TarReader(_cursorGuard, leaveOpen: true);
        _cursorIndex = -1;
    }

    private void CloseCursor()
    {
        _cursorReader?.Dispose();
        _cursorStream?.Dispose();
        _cursorFile?.Dispose();
        _cursorReader = null;
        _cursorGuard = null;
        _cursorStream = null;
        _cursorFile = null;
        _cursorEntry = null;
    }

    public void Dispose()
    {
        CloseCursor();
        _plain?.Dispose();
    }
}

/// <summary>A single compressed file (.gz, .bz2, .xz, .zst): one member named after the file.</summary>
internal sealed class SingleStreamReader(string path, Func<Stream, Stream> decompress, string format, string displayName, string suffix) : IMemberReader
{
    private FileStream? _file;
    private Stream? _data;

    public string Format => format;

    public bool SharesCursor => true;

    public IEnumerable<MemberInfo> List(Action<string> warn, CancellationToken ct)
    {
        // The stream must at least start like its format; the content itself is only read when opened.
        using (var file = ArchiveFormats.OpenShared(path))
        {
            var kind = ArchiveFormats.BySignature(file);
            if (kind is not (ArchiveKind.Gzip or ArchiveKind.Bzip2 or ArchiveKind.Xz or ArchiveKind.Zstd))
                throw new InvalidDataException($"This is not a {format} file.");
        }
        string name = displayName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && displayName.Length > suffix.Length
            ? displayName[..^suffix.Length]
            : displayName + ".data";
        yield return new MemberInfo(0, name, MemberKind.File, -1, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), false);
    }

    public Stream Open(int index, CancellationToken ct)
    {
        CloseMember();
        try
        {
            _file = ArchiveFormats.OpenShared(path);
            _data = decompress(_file);
            return new BorrowedStream(_data);
        }
        catch
        {
            try { CloseMember(); }
            catch (Exception ex) { AppLog.Warn("Archive member close failed after opening failed", ex); }
            throw;
        }
    }

    private void CloseMember()
    {
        var data = _data;
        var file = _file;
        _data = null;
        _file = null;
        Exception? failure = null;
        try { data?.Dispose(); }
        catch (Exception ex) { failure = ex; }
        try { file?.Dispose(); }
        catch (Exception ex)
        {
            if (failure is null) failure = ex;
            else AppLog.Warn("Archive source close failed after member close", ex);
        }
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public void Dispose() => CloseMember();
}

/// <summary>
/// 7z and RAR through SharpCompress. Non-solid RAR archives open members directly. Solid RAR and every 7z keep a forward
/// reader instead: 7z packs several members into one compressed block, and opening one member out of order yields an
/// empty stream in SharpCompress. Extracting in archive order decompresses each block once. Multi-volume RAR sets are
/// opened whole.
/// </summary>
internal sealed class SharpArchiveReader : IMemberReader
{
    // SharpCompress 0.50.4 reports a missing 7z compression folder as encrypted. Its public entry API does
    // not expose HasStream. Read that pinned header metadata only; missing metadata keeps the original refusal.
    private static readonly PropertyInfo? s_sevenZipFilePart = typeof(SevenZipArchiveEntry).BaseType?
        .GetProperty("FilePart", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo? s_sevenZipHeader = s_sevenZipFilePart?.PropertyType
        .GetProperty("Header", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly PropertyInfo? s_sevenZipHasStream = s_sevenZipHeader?.PropertyType
        .GetProperty("HasStream", BindingFlags.Instance | BindingFlags.Public);

    private static bool HasNoSevenZipStream(IArchiveEntry entry)
    {
        if (entry is not SevenZipArchiveEntry { IsDirectory: false, Size: 0 } sevenZip) return false;
        object? part = s_sevenZipFilePart?.GetValue(sevenZip);
        object? header = part is null ? null : s_sevenZipHeader?.GetValue(part);
        return header is not null && s_sevenZipHasStream?.GetValue(header) is false;
    }

    private readonly IArchive _archive;
    private readonly List<IArchiveEntry> _entries;
    private readonly List<FileStream> _volumes;
    private readonly bool _sequential;
    private readonly bool _numberedVolumeGap;
    // The reader visits entries in its own order (7z puts empty entries elsewhere than its entry list), so members are
    // matched by name and occurrence, never by position.
    private readonly int[] _ordinals;
    private readonly Dictionary<string, int> _seen = new(StringComparer.Ordinal);
    private IReader? _reader;

    private SharpArchiveReader(IArchive archive, List<FileStream> volumes, string format, bool sequential, bool numberedVolumeGap)
    {
        _sequential = sequential;
        _numberedVolumeGap = numberedVolumeGap;
        _archive = archive;
        _volumes = volumes;
        Format = format;
        _entries = archive.Entries.ToList();
        _ordinals = new int[_entries.Count];
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < _entries.Count; i++)
        {
            var key = _entries[i].Key ?? string.Empty;
            _ordinals[i] = counts.TryGetValue(key, out var n) ? n : 0;
            counts[key] = _ordinals[i] + 1;
        }
    }

    public string Format { get; }

    public bool SharesCursor => _sequential;

    public static SharpArchiveReader Open(string path, bool rar)
    {
        bool numberedVolumeGap = false;
        var volumes = rar ? RarVolumes(path, out numberedVolumeGap).Select(ArchiveFormats.OpenShared).ToList() : [ArchiveFormats.OpenShared(path)];
        try
        {
            var options = new ReaderOptions { LeaveStreamOpen = true, LookForHeader = false };
            IArchive archive = rar
                ? volumes.Count > 1 ? RarArchive.OpenArchive(volumes.Cast<Stream>().ToList(), options) : RarArchive.OpenArchive(volumes[0], options)
                : SevenZipArchive.OpenArchive(volumes[0], options);
            return new SharpArchiveReader(archive, volumes, rar ? volumes.Count > 1 ? $"RAR ({volumes.Count} volumes)" : "RAR" : "7z", sequential: !rar || archive.IsSolid, numberedVolumeGap);
        }
        catch (Exception ex)
        {
            foreach (var v in volumes) v.Dispose();
            if (ex is IOException or UnauthorizedAccessException) throw;
            throw new InvalidDataException(Describe(ex), ex);
        }
    }

    /// <summary>"x.part1.rar" (or any part) → all parts in order; "x.rar" with "x.r00"… → the old naming.</summary>
    internal static List<string> RarVolumes(string path) => RarVolumes(path, out _);

    private static List<string> RarVolumes(string path, out bool numberedVolumeGap)
    {
        numberedVolumeGap = false;
        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileName(path);
        var modern = System.Text.RegularExpressions.Regex.Match(name, @"^(.*)\.part(\d+)\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (modern.Success)
        {
            var stem = modern.Groups[1].Value;
            var parts = Directory.EnumerateFiles(dir, stem + ".part*.rar")
                .Select(p => (Path: p, M: System.Text.RegularExpressions.Regex.Match(Path.GetFileName(p), @"\.part(\d+)\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)))
                .Where(x => x.M.Success && Path.GetFileName(x.Path).StartsWith(stem + ".part", StringComparison.OrdinalIgnoreCase))
                .Select(x => (x.Path, Number: int.Parse(x.M.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)))
                .OrderBy(x => x.Number).ToList();
            // Split-before/after flags describe each member's ends, so the dependency can call a member complete
            // even when an interior numbered volume is absent. Keep that discovery warning independently.
            for (int i = 1; i < parts.Count; i++)
                if (parts[i].Number - parts[i - 1].Number > 1) numberedVolumeGap = true;
            return parts.Count > 0 ? parts.Select(x => x.Path).ToList() : [path];
        }
        var extension = Path.GetExtension(name);
        bool secondary = System.Text.RegularExpressions.Regex.IsMatch(extension, @"^\.[r-z]\d{2}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var baseName = Path.GetFileNameWithoutExtension(name);
        var old = new List<(string Path, int Number)>();
        if (!secondary) old.Add((path, 0));
        foreach (string candidate in Directory.EnumerateFiles(dir))
        {
            if (!string.Equals(Path.GetFileNameWithoutExtension(candidate), baseName, StringComparison.OrdinalIgnoreCase)) continue;
            string suffix = Path.GetExtension(candidate).ToLowerInvariant();
            if (secondary && suffix == ".rar") old.Add((candidate, 0));
            else if (System.Text.RegularExpressions.Regex.IsMatch(suffix, @"^\.[r-z]\d{2}$"))
                old.Add((candidate, 1 + (suffix[1] - 'r') * 100 + (suffix[2] - '0') * 10 + suffix[3] - '0'));
        }
        old.Sort((a, b) => a.Number.CompareTo(b.Number));
        // A secondary entry point needs the primary volume first, once. Retain later volumes after a gap,
        // with the same explicit discovery warning as numbered sets; stopping at a gap loses intact members.
        if (old.Count > 0 && old[0].Number != 0) numberedVolumeGap = true;
        for (int i = 1; i < old.Count; i++)
            if (old[i].Number - old[i - 1].Number > 1) numberedVolumeGap = true;
        return old.Count > 0 ? old.Select(x => x.Path).ToList() : [path];
    }

    private static string Describe(Exception ex) =>
        ex.Message.Contains("password", StringComparison.OrdinalIgnoreCase) || ex is System.Security.Cryptography.CryptographicException
            ? "The archive's file list is encrypted; FileCat does not ask for archive passwords."
            : "The archive cannot be read: " + ex.Message;

    public IEnumerable<MemberInfo> List(Action<string> warn, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_numberedVolumeGap || !_archive.IsComplete)
            warn("Some volumes of this archive are missing; members that continue in them cannot be extracted.");
        for (int i = 0; i < _entries.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var e = _entries[i];
            if (e is SevenZipArchiveEntry { IsAnti: true }) continue; // deletion markers of an update, not content
            var kind = e.IsDirectory ? MemberKind.Directory : e.LinkTarget is { Length: > 0 } ? MemberKind.SymbolicLink : MemberKind.File;
            yield return new MemberInfo(i, e.Key ?? string.Empty, kind, e.IsDirectory ? 0 : e.Size, e.CompressedSize, ArchiveFormats.Utc(e.LastModifiedTime),
                e.IsEncrypted && !HasNoSevenZipStream(e), e.LinkTarget);
        }
    }

    public Stream Open(int index, CancellationToken ct)
    {
        try
        {
            if (HasNoSevenZipStream(_entries[index])) return Stream.Null;
            if (!_sequential) return _entries[index].OpenEntryStream();
            string key = _entries[index].Key ?? string.Empty;
            int ordinal = _ordinals[index];
            // Already passed (or current, its stream consumed): start over; otherwise continue forward.
            if (_reader is null || _seen.GetValueOrDefault(key) > ordinal)
            {
                _reader?.Dispose();
                _reader = _archive.ExtractAllEntries();
                _seen.Clear();
            }
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!_reader.MoveToNextEntry()) throw new FileNotFoundException("The member is no longer in the archive.");
                var current = _reader.Entry.Key ?? string.Empty;
                int seen = _seen[current] = _seen.GetValueOrDefault(current) + 1;
                if (current == key && seen - 1 == ordinal) return new BorrowedStream(_reader.OpenEntryStream());
            }
        }
        catch (Exception ex) when (ex is not (IOException or OperationCanceledException or InvalidDataException))
        {
            throw new InvalidDataException(Describe(ex), ex);
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
        _archive.Dispose();
        foreach (var v in _volumes) v.Dispose();
    }
}

/// <summary>ISO 9660 (with Joliet names) and UDF images through DiscUtils; UDF is preferred when both are present.</summary>
internal sealed class DiscImageReader : IMemberReader
{
    private const int MaxDepth = 64;
    private readonly FileStream _file;
    private readonly DiscFileSystem _fs;
    private readonly List<string> _paths = [];

    public DiscImageReader(string path)
    {
        _file = ArchiveFormats.OpenShared(path);
        try
        {
            if (UdfReader.Detect(_file))
            {
                _fs = new UdfReader(_file);
                Format = "UDF image";
            }
            else if (CDReader.Detect(_file))
            {
                _fs = new CDReader(_file, joliet: true, hideVersions: true);
                Format = "ISO 9660 image";
            }
            else throw new InvalidDataException("This is not an ISO 9660 or UDF disc image.");
        }
        catch
        {
            _file.Dispose();
            throw;
        }
    }

    public string Format { get; }

    public bool SharesCursor => false;

    public IEnumerable<MemberInfo> List(Action<string> warn, CancellationToken ct)
    {
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((string.Empty, 0));
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (dir, depth) = pending.Dequeue();
            string[] dirs, files;
            try
            {
                dirs = _fs.GetDirectories(dir).ToArray();
                files = _fs.GetFiles(dir).ToArray();
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or ArgumentException or NotSupportedException)
            {
                warn($"The folder \"{dir}\" could not be read: {ex.Message}");
                continue;
            }
            foreach (var d in dirs)
            {
                if (depth + 1 > MaxDepth)
                {
                    warn($"Folders nested deeper than {MaxDepth} levels are not listed (a damaged or looping image).");
                    continue;
                }
                _paths.Add(d);
                yield return new MemberInfo(_paths.Count - 1, Clean(d), MemberKind.Directory, 0, -1, Time(d), false);
                pending.Enqueue((d, depth + 1));
            }
            foreach (var f in files)
            {
                long length;
                try { length = _fs.GetFileLength(f); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { length = -1; }
                _paths.Add(f);
                yield return new MemberInfo(_paths.Count - 1, Clean(f), MemberKind.File, length, -1, Time(f), false);
            }
        }
    }

    private DateTime? Time(string path)
    {
        try { return _fs.GetLastWriteTimeUtc(path); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or NotSupportedException or ArgumentOutOfRangeException) { return null; }
    }

    private static string Clean(string path) => path.Replace('\\', '/').Trim('/');

    public Stream Open(int index, CancellationToken ct) => _fs.OpenFile(_paths[index], FileMode.Open, FileAccess.Read);

    public void Dispose()
    {
        _fs.Dispose();
        _file.Dispose();
    }
}
