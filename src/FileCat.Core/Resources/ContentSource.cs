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

/// <summary>
/// Content with parts that are lost (plan §17.1: recovered files whose space was reused or could not be read). Those
/// bytes read as zeros, and whoever copies or shows the content says so instead of passing them off as real data.
/// </summary>
public interface IPartialContent
{
    /// <summary>The ranges that read as zeros only because their data is lost, in content order (may grow while reading).</summary>
    IReadOnlyList<(long Offset, long Length)> MissingRanges { get; }
}

public static class PartialContent
{
    /// <summary>"24 KiB of 40 KiB are lost (bytes 16,384–40,959) and are zeros in this copy."</summary>
    public static string Describe(IReadOnlyList<(long Offset, long Length)> missing, long total)
    {
        long lost = missing.Sum(m => m.Length);
        var ranges = string.Join(", ", missing.Take(3).Select(m => $"{m.Offset:N0}–{m.Offset + m.Length - 1:N0}")) + (missing.Count > 3 ? $", and {missing.Count - 3} more" : "");
        return $"{Size(lost)} of {Size(total)} are lost (bytes {ranges}) and are zeros here, not the file's data.";
    }

    private static string Size(long n) => n < 1024 ? $"{n} bytes" : n < 1024 * 1024 ? $"{n / 1024.0:0.#} KiB" : $"{n / (1024.0 * 1024):0.#} MiB";
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
