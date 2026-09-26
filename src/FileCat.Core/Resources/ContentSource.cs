using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Resources;

/// <summary>
/// Random-access byte content with 64-bit offsets (plan §13.1). Implementations must be safe for
/// concurrent reads from multiple threads.
/// </summary>
public interface IContentSource : IDisposable
{
    string DisplayName { get; }

    /// <summary>Current length in bytes, or -1 when unknown (sequential sources).</summary>
    long Length { get; }

    bool CanSeek { get; }

    /// <summary>Reads at <paramref name="offset"/>; returns bytes read, 0 at end of content.</summary>
    int Read(long offset, Span<byte> buffer);

    /// <summary>Revision evidence used to detect external changes; null when unavailable.</summary>
    ContentRevision? GetRevision();

    /// <summary>Local file path when the content is a plain local file, otherwise null.</summary>
    string? LocalPath { get; }
}

/// <summary>Weak revision evidence: length + modification time (+ native file id when known).</summary>
public readonly record struct ContentRevision(long Length, long ModifiedTicks, string? NativeId = null);

/// <summary>Local file content opened with full sharing so viewers never block other programs (§9.5).</summary>
public sealed class FileContentSource : IContentSource
{
    private readonly SafeFileHandle _handle;
    private readonly string _path;

    public FileContentSource(string path)
    {
        _path = path;
        _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, FileOptions.RandomAccess);
    }

    public string DisplayName => _path;

    public long Length
    {
        get
        {
            try { return RandomAccess.GetLength(_handle); }
            catch (IOException) { return -1; }
        }
    }

    public bool CanSeek => true;

    public string? LocalPath => _path;

    public int Read(long offset, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return RandomAccess.Read(_handle, buffer, offset);
    }

    public ContentRevision? GetRevision()
    {
        try
        {
            var fi = new FileInfo(_path);
            return new ContentRevision(RandomAccess.GetLength(_handle), fi.LastWriteTimeUtc.Ticks);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public void Dispose() => _handle.Dispose();
}

/// <summary>In-memory content (small archive members, tests).</summary>
public sealed class MemoryContentSource(string displayName, byte[] data) : IContentSource
{
    public string DisplayName { get; } = displayName;
    public long Length => data.Length;
    public bool CanSeek => true;
    public string? LocalPath => null;

    public int Read(long offset, Span<byte> buffer)
    {
        if (offset >= data.Length) return 0;
        int n = (int)Math.Min(buffer.Length, data.Length - offset);
        data.AsSpan((int)offset, n).CopyTo(buffer);
        return n;
    }

    public ContentRevision? GetRevision() => new ContentRevision(data.Length, 0);

    public void Dispose() { }
}
