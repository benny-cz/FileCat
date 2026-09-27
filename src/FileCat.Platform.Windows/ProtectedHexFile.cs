using System.ComponentModel;
using System.Runtime.InteropServices;
using FileCat.Core.Content;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows;

public readonly record struct WindowsFileIdentity(uint VolumeSerial, ulong FileIndex)
{
    public override string ToString() => $"{VolumeSerial:X8}:{FileIndex:X16}";
}

/// <summary>Local-file baseline held with write/delete sharing denied for the entire editing session.</summary>
public sealed partial class ProtectedHexFile : IContentSource
{
    private readonly SafeFileHandle _handle;
    private readonly string _path;
    private readonly long _length;
    private readonly WindowsFileIdentity _identity;

    public ProtectedHexFile(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        _path = Path.GetFullPath(path);
        if (_path.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(Path.GetPathRoot(_path)!).DriveType == DriveType.Network)
            throw new NotSupportedException("Protected hex editing is currently available for local Windows files. View or copy network files first.");
        if ((File.GetAttributes(_path) & FileAttributes.ReparsePoint) != 0)
            throw new NotSupportedException("Hex editing a file link could retarget an unexpected file. Open the resolved file explicitly.");
        _handle = File.OpenHandle(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, FileOptions.RandomAccess);
        try
        {
            _length = RandomAccess.GetLength(_handle);
            _identity = Identity(_handle);
        }
        catch { _handle.Dispose(); throw; }
    }

    public string DisplayName => _path;
    public string? LocalPath => _path;
    public long Length => _length;
    public bool CanSeek => true;
    public WindowsFileIdentity FileIdentity => _identity;

    public int Read(long offset, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        return RandomAccess.Read(_handle, buffer, offset);
    }

    public ContentRevision? GetRevision()
    {
        try { return new ContentRevision(RandomAccess.GetLength(_handle), File.GetLastWriteTimeUtc(_path).Ticks, _identity.ToString()); }
        catch (IOException) { return null; }
    }

    public void ValidateForSave(IReadOnlyList<HexPatchRange> ranges)
    {
        if (RandomAccess.GetLength(_handle) != _length) throw new IOException("The protected file length changed; save is blocked.");
        using var pathHandle = File.OpenHandle(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, FileOptions.RandomAccess);
        if (Identity(pathHandle) != _identity) throw new IOException("The path now names a different file; save is blocked.");
        foreach (var range in ranges)
        {
            var actual = new byte[range.Original.Length];
            if (RandomAccess.Read(_handle, actual, range.Offset) != actual.Length ||
                !actual.AsSpan().SequenceEqual(range.Original))
                throw new IOException($"Original bytes at 0x{range.Offset:X} changed; save is blocked.");
        }
    }

    internal void Write(long offset, ReadOnlySpan<byte> bytes) => RandomAccess.Write(_handle, bytes, offset);
    internal void Flush() => RandomAccess.FlushToDisk(_handle);
    public void Dispose() => _handle.Dispose();

    public static WindowsFileIdentity Identity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new WindowsFileIdentity(info.VolumeSerial, ((ulong)info.FileIndexHigh << 32) | info.FileIndexLow);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint Attributes;
        public long CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, FileIndexHigh, FileIndexLow;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandleFileInformation information);
}
