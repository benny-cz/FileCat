using System.Buffers.Binary;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Recovery;

/// <summary>
/// Read-only random access to a disk image or a device (plan §17.2). There is no write member: a source cannot be
/// changed through this interface.
/// </summary>
public interface IBlockSource : IDisposable
{
    /// <summary>What the user knows the source as (an image path, a drive).</summary>
    string Description { get; }

    long Length { get; }

    /// <summary>
    /// Reads up to <paramref name="buffer"/>.Length bytes at <paramref name="offset"/>; fewer only at the end. Unreadable
    /// ranges throw <see cref="IOException"/>.
    /// </summary>
    int Read(long offset, Span<byte> buffer);
}

/// <summary>A native device reader that can check whether a path still names its held source entry.</summary>
public interface IDevicePathGuard
{
    /// <summary>False when the path changed, disappeared, or its descriptor identity cannot be verified.</summary>
    bool IsCurrentDevicePath(string path);
}

public static class BlockSourceExtensions
{
    /// <summary>Reads exactly <paramref name="length"/> bytes, or throws <see cref="InvalidDataException"/> past the end.</summary>
    public static byte[] ReadExactly(this IBlockSource source, long offset, int length)
    {
        var buffer = new byte[length];
        int done = 0;
        while (done < length)
        {
            int n = source.Read(offset + done, buffer.AsSpan(done));
            if (n <= 0) throw new InvalidDataException($"The structure at byte {offset:N0} runs past the end of {source.Description}.");
            done += n;
        }
        return buffer;
    }
}

/// <summary>
/// A disk image file, opened for reading only and shared with every other program (it may still be in use). Fixed VHD
/// files are raw disks followed by a 512-byte footer, which is left out.
/// </summary>
public sealed class ImageFileSource : IBlockSource
{
    private readonly SafeFileHandle _handle;

    public ImageFileSource(string path)
    {
        Description = path;
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
        try
        {
            long length = RandomAccess.GetLength(_handle);
            Length = length;
            if (length >= 512)
            {
                Span<byte> footer = stackalloc byte[512];
                if (RandomAccess.Read(_handle, footer, length - 512) == 512 && footer[..8].SequenceEqual("conectix"u8))
                {
                    uint type = BinaryPrimitives.ReadUInt32BigEndian(footer[60..]);
                    if (type != 2) throw new InvalidDataException("Dynamic and differencing VHD images are not supported: convert the image to a fixed VHD or a raw image first.");
                    Length = length - 512;
                }
            }
            Span<byte> head = stackalloc byte[8];
            if (length >= 8 && RandomAccess.Read(_handle, head, 0) == 8 && head.SequenceEqual("vhdxfile"u8))
                throw new InvalidDataException("VHDX images are not supported: convert the image to a raw image first.");
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    public string Description { get; }
    public long Length { get; }

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset >= Length) return 0;
        if (buffer.Length > Length - offset) buffer = buffer[..(int)(Length - offset)];
        return RandomAccess.Read(_handle, buffer, offset);
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>A window onto part of a source: a partition, with offsets relative to its start.</summary>
public sealed class WindowSource(IBlockSource inner, long start, long length, string description) : IBlockSource
{
    public string Description { get; } = description;
    public long Length { get; } = Math.Max(0, Math.Min(length, inner.Length - start));
    public long Start { get; } = start;

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset < 0 || offset >= Length) return 0;
        if (buffer.Length > Length - offset) buffer = buffer[..(int)(Length - offset)];
        return inner.Read(Start + offset, buffer);
    }

    /// <summary>The window does not own the source it looks into.</summary>
    public void Dispose() { }
}

/// <summary>
/// Caches recently read blocks of a source (directory clusters and MFT records are read in small pieces). Bounded;
/// thread-safe for the reads recovery does (a scan, then previews and copies).
/// </summary>
public sealed class CachedSource(IBlockSource inner, int blockSize = 64 * 1024, int maxBlocks = 256) : IBlockSource
{
    private readonly object _lock = new();
    private readonly Dictionary<long, byte[]> _blocks = [];
    private readonly LinkedList<long> _order = new();

    public string Description => inner.Description;
    public long Length => inner.Length;

    /// <summary>A small read that is not worth a block in the cache (a glance at many scattered places).</summary>
    public int ReadDirect(long offset, Span<byte> buffer) => offset >= Length ? 0 : inner.Read(offset, buffer);

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset >= Length) return 0;
        // Large reads (copying content) go straight through.
        if (buffer.Length >= blockSize) return inner.Read(offset, buffer);
        int done = 0;
        while (done < buffer.Length && offset + done < Length)
        {
            long block = (offset + done) / blockSize;
            var data = Block(block);
            int within = (int)(offset + done - block * blockSize);
            int n = Math.Min(buffer.Length - done, data.Length - within);
            if (n <= 0) break;
            data.AsSpan(within, n).CopyTo(buffer[done..]);
            done += n;
        }
        return done;
    }

    private byte[] Block(long block)
    {
        lock (_lock)
        {
            if (_blocks.TryGetValue(block, out var cached)) return cached;
        }
        long start = block * blockSize;
        var data = new byte[(int)Math.Min(blockSize, Length - start)];
        int done = 0;
        while (done < data.Length)
        {
            int n = inner.Read(start + done, data.AsSpan(done));
            if (n <= 0) break;
            done += n;
        }
        if (done < data.Length) Array.Resize(ref data, done);
        lock (_lock)
        {
            if (_blocks.TryAdd(block, data))
            {
                _order.AddLast(block);
                while (_order.Count > maxBlocks)
                {
                    _blocks.Remove(_order.First!.Value);
                    _order.RemoveFirst();
                }
            }
        }
        return data;
    }

    public void Dispose() => inner.Dispose();
}
