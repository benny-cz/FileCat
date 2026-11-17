using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text;
using FileCat.Core.FileSystem;
using FileCat.Platform.Windows.Elevation;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

/// <summary>64 KiB cursor over the admitted directory handle. It neither opens another handle nor materializes a tree.</summary>
internal sealed unsafe partial class WindowsStorageDirectoryCursor(SafeFileHandle directory) : IDisposable
{
    internal const int BufferBytes = 64 * 1024;
    private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);
    private byte[]? _buffer = new byte[BufferBytes];
    private bool _first = true, _complete;
    internal bool IsComplete => _complete;
    internal sealed record Entry(string Name, uint Attributes, uint ReparseTag, string? ObjectId)
    {
        internal bool IsDirectory => (Attributes & (uint)FileAttributes.Directory) != 0;
        internal long ByteLength { get; init; }
    }

    internal ImmutableArray<Entry> ReadBatch(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_buffer is null, this);
        if (_complete) return [];
        ct.ThrowIfCancellationRequested(); _buffer.AsSpan().Clear();
        IoStatus io;
        fixed (byte* bytes = _buffer)
        {
            int status = NtQueryDirectoryFile(directory, 0, 0, 0, &io, bytes, BufferBytes,
                60 /* FileIdExtdDirectoryInformation */, 0, 0, _first ? (byte)1 : (byte)0);
            _first = false;
            if (status == unchecked((int)0x80000006) /* STATUS_NO_MORE_FILES */) { _complete = true; return []; }
            if (status < 0) throw NtFile.Error(status, "Enumerate the held storage directory");
        }
        ct.ThrowIfCancellationRequested();
        if ((nuint)io.Information is 0 or > BufferBytes) throw new IOException("The storage cursor returned an invalid byte count or made no progress.");
        return Parse(_buffer.AsSpan(0, (int)io.Information));
    }

    internal static ImmutableArray<Entry> Parse(ReadOnlySpan<byte> bytes)
    {
        // FILE_ID_EXTD_DIR_INFORMATION: next 0, attrs 56, name bytes 60, tag 68, 128-bit ID 72, name 88.
        const int Header = 88;
        if (bytes.Length is < Header or > BufferBytes) throw new IOException("The storage cursor batch length is invalid.");
        var result = ImmutableArray.CreateBuilder<Entry>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        int offset = 0;
        while (true)
        {
            var entry = bytes[offset..];
            if (entry.Length < Header) throw new IOException("The storage cursor entry is truncated.");
            uint next = BinaryPrimitives.ReadUInt32LittleEndian(entry), nameBytes = BinaryPrimitives.ReadUInt32LittleEndian(entry[60..]);
            if (nameBytes == 0 || (nameBytes & 1) != 0 || nameBytes > entry.Length - Header) throw new IOException("The storage cursor name is invalid.");
            int used = Header + (int)nameBytes;
            if (next != 0 && (next < used || (next & 7) != 0 || next > entry.Length - Header)) throw new IOException("The storage cursor offset is invalid.");
            if (next == 0 && entry.Length - used > 7) throw new IOException("The storage cursor has an unbounded terminal tail.");
            string name;
            try { name = Utf16.GetString(entry.Slice(Header, (int)nameBytes)); }
            catch (DecoderFallbackException error) { throw new IOException("The storage cursor name is malformed UTF-16.", error); }
            if (name is not ("." or ".."))
            {
                if (!StoragePaths.IsChildName(name) || !names.Add(name)) throw new IOException("The storage cursor name is not one unique exact component.");
                uint attributes = BinaryPrimitives.ReadUInt32LittleEndian(entry[56..]);
                var id = entry.Slice(72, 16);
                long length = BinaryPrimitives.ReadInt64LittleEndian(entry[40..]);
                if (length < 0) throw new IOException("The storage cursor returned a negative file length.");
                result.Add(new(name, attributes, (attributes & (uint)FileAttributes.ReparsePoint) != 0 ? BinaryPrimitives.ReadUInt32LittleEndian(entry[68..]) : 0,
                    id.IndexOfAnyExcept((byte)0) < 0 ? null : Convert.ToHexString(id)) { ByteLength = length });
            }
            if (next == 0) return result.ToImmutable();
            offset = checked(offset + (int)next);
        }
    }

    public void Dispose() => _buffer = null; // the surrounding object lease owns the same admitted handle
    [StructLayout(LayoutKind.Sequential)] private struct IoStatus { public nint Status, Information; }
    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryDirectoryFile(SafeFileHandle file, nint @event, nint apc, nint apcContext,
        IoStatus* io, void* output, uint length, int infoClass, byte single, nint pattern, byte restart);
}
