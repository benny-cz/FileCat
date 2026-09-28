using System.Security.Cryptography;
using System.Text;

namespace FileCat.Core.FileSystem;

/// <summary>Return value of a copy progress callback.</summary>
public enum CopyProgressAction
{
    Continue,
    Cancel,
}

/// <summary>Called from the copying thread; may block to implement pause and rate limiting.</summary>
public delegate CopyProgressAction CopyProgressCallback(long bytesTransferred, long totalBytes);

public sealed class FileCopyOptions
{
    /// <summary>Copy a symbolic link as a link instead of its target (never follow silently, §8.1).</summary>
    public bool CopyLinkAsLink { get; init; } = true;
    /// <summary>Unbuffered I/O for very large files.</summary>
    public bool NoBuffering { get; init; }
    /// <summary>
    /// Do not pre-size the destination: an interrupted direct copy then shows a short size instead of full-size zeros,
    /// which is how recovery recognizes it (plan §9.3).
    /// </summary>
    public bool DisablePreallocation { get; init; }
    /// <summary>Allow an encrypted (EFS) source to arrive decrypted where the destination cannot encrypt (user-approved).</summary>
    public bool AllowDecryptedDestination { get; init; }
    /// <summary>
    /// Flush the copied data to the device before returning. Moves set it: their source is deleted next, and a copy
    /// still in the write cache would be lost with it on power failure.
    /// </summary>
    public bool FlushDestination { get; init; }
}

/// <summary>Native identity and metadata of one file-system item.</summary>
public sealed record FileSystemItemInfo(
    string Path,
    bool IsDirectory,
    bool IsLink,
    long Size,
    DateTime ModifiedUtc,
    DateTime CreatedUtc,
    FileAttributes Attributes,
    string? FileId = null,
    int LinkCount = 1,
    string? LinkTarget = null)
{
    public bool IsReadOnly => (Attributes & FileAttributes.ReadOnly) != 0;
}

/// <summary>Capability profile of the volume holding a path (ReFS/Dev Drive is its own profile, §8.1).</summary>
public sealed record VolumeInfo(
    string DeviceKey,
    string? FileSystem,
    bool SupportsNamedStreams,
    bool CaseSensitive,
    TimeSpan TimestampPrecision,
    bool SupportsHardLinks,
    bool SupportsSymbolicLinks,
    bool IsRemote,
    bool IsRemovable,
    bool SupportsRecycle,
    long FreeBytes = -1)
{
    public static VolumeInfo Unknown(string path) => new(PathUtil.GetDeviceKey(path), null, false, !OperatingSystem.IsWindows(),
        TimeSpan.FromSeconds(2), false, false, false, false, false);
}

public enum RecycleClassification
{
    Recyclable,
    /// <summary>Network shares and most removable media have no Recycle Bin.</summary>
    NoRecycleBin,
    /// <summary>Larger than the bin's quota: the Shell would delete it permanently.</summary>
    TooLarge,
    /// <summary>The name is too long for the bin.</summary>
    NameTooLong,
    Unknown,
}

public enum RecycleOutcome
{
    Recycled,
    /// <summary>The platform destroyed the item instead of recycling it. Reported, never hidden.</summary>
    PermanentlyDeleted,
    Failed,
    /// <summary>FileCat stopped the item because it would have been deleted permanently.</summary>
    Aborted,
    NotAttempted,
}

/// <param name="RecycledId">Platform identifier of the item in the bin, used for guarded restore.</param>
public sealed record RecycleResult(string Path, RecycleOutcome Outcome, string? RecycledId = null, string? Error = null);

/// <summary>
/// Platform file-system mutations used by the operation engine (AI-02: controls never call these).
/// Every method is synchronous and called from job threads; failures throw IOException subclasses.
/// </summary>
public interface IFileSystemOperations
{
    FileSystemItemInfo? TryGetInfo(string path);
    VolumeInfo GetVolumeInfo(string path);

    /// <summary>Copies one file to a destination that must not exist (staged names only).</summary>
    void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct);

    /// <summary>
    /// Renames/moves within a volume. Replacing requires <paramref name="replaceExisting"/>. Replacing always writes
    /// through; <paramref name="writeThrough"/> makes a rename to a new name durable before returning as well.
    /// </summary>
    void Move(string source, string destination, bool replaceExisting, bool writeThrough = false);

    void CreateDirectory(string path);

    /// <summary>Permanently deletes one file or link (never follows it).</summary>
    void DeleteFile(string path);

    /// <summary>Permanently deletes an empty directory or a directory link (never its target's content).</summary>
    void DeleteDirectory(string path);

    void SetAttributes(string path, FileAttributes attributes);

    void SetTimes(string path, DateTime? createdUtc, DateTime? modifiedUtc);

    /// <summary>Copies a directory link/junction as a link. Returns false when the platform cannot.</summary>
    bool TryCopyLink(string source, string destination, bool isDirectory, out string? error);

    RecycleClassification ClassifyRecycle(string path, long size);

    /// <summary>Recycles items, reporting a verified per-item outcome (plan §9.2).</summary>
    IReadOnlyList<RecycleResult> Recycle(IReadOnlyList<string> paths, Action<string>? itemStarted, CancellationToken ct);

    /// <summary>Restores a recycled item to its original location when it is still in the bin.</summary>
    bool TryRestoreRecycled(string recycledId, string originalPath, out string? error);

    /// <summary>Mark-of-the-Web / quarantine origin data, or null.</summary>
    string? ReadOriginMark(string path);

    /// <summary>Writes origin data; returns false when the destination cannot store it (reported as a security loss).</summary>
    bool WriteOriginMark(string path, string mark);

    /// <summary>Names of alternate data streams (without the default stream); empty where the platform has none.</summary>
    IReadOnlyList<string> GetAlternateStreams(string path);

    /// <summary>True when <see cref="CopyFile"/> itself keeps attributes, times, and streams (native copy engines).</summary>
    bool CopyPreservesMetadata { get; }

    /// <summary>
    /// Root of the volume holding <paramref name="path"/> (a mount point for mounted folders), used to tell whether a
    /// rename can stay on one volume.
    /// </summary>
    string GetVolumeRoot(string path);

    /// <summary>
    /// Creates a link at <paramref name="linkPath"/>, which must not exist. A symbolic link's target may be relative to
    /// the link's folder; junction and hard link targets are absolute. Junctions exist only on Windows.
    /// </summary>
    void CreateLink(string linkPath, string target, LinkKind kind, bool isDirectory);

    /// <summary>
    /// Whether this process may create symbolic links (on Windows: Developer Mode or the privilege), found by a probe
    /// in the temporary folder; null when the probe could not tell.
    /// </summary>
    bool? CanCreateSymbolicLinks { get; }

    /// <summary>A stable identity of the file (volume and file ID), without following links; null where unavailable.</summary>
    string? GetFileIdentity(string path);
}

public enum LinkKind
{
    Symbolic,
    /// <summary>A Windows mount-point reparse point to a local folder; needs no privilege.</summary>
    Junction,
    /// <summary>Another name of the same file on the same volume.</summary>
    Hard,
}

/// <summary>
/// Portable implementation over System.IO: streamed copy with progress, links via .NET link APIs, and the
/// freedesktop.org trash (Linux) or ~/.Trash (macOS).
/// </summary>
public class PortableFileOperations : IFileSystemOperations
{
    private const int BufferSize = 1024 * 1024;

    public virtual FileSystemItemInfo? TryGetInfo(string path)
    {
        try
        {
            FileSystemInfo fi = new FileInfo(path);
            if (!fi.Exists)
            {
                fi = new DirectoryInfo(path);
                if (!fi.Exists)
                {
                    // A dangling link still exists as an item.
                    var link = new FileInfo(path);
                    if (link.LinkTarget is null) return null;
                    fi = link;
                }
            }
            return FromInfo(fi, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Item information from data an enumeration or stat already returned. Only reparse points are asked for their
    /// link target: reading it opens a handle, which would double the cost of every ordinary file.
    /// </summary>
    public static FileSystemItemInfo FromInfo(FileSystemInfo fi, string? path = null)
    {
        bool isDir = fi is DirectoryInfo;
        string? linkTarget = (fi.Attributes & FileAttributes.ReparsePoint) != 0 ? fi.LinkTarget : null;
        return new FileSystemItemInfo(path ?? fi.FullName, isDir, linkTarget is not null, isDir ? -1 : ((FileInfo)fi).Length,
            fi.LastWriteTimeUtc, fi.CreationTimeUtc, fi.Attributes, null, 1, linkTarget);
    }

    public virtual bool CopyPreservesMetadata => false;

    public virtual string GetVolumeRoot(string path)
    {
        var full = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows()) return Path.GetPathRoot(full) ?? path;
        return UnixVolumeRoot(full);
    }

    /// <summary>
    /// Unix: one root holds every path, so the volume is the deepest mount point above the path. A rename across mount
    /// points would otherwise become the runtime's unguarded copy-and-delete.
    /// </summary>
    public static string UnixVolumeRoot(string fullPath)
    {
        var full = fullPath;
        string best = "/";
        foreach (var mount in MountPoints())
        {
            if (mount.Length > best.Length && (full == mount || full.StartsWith(mount.EndsWith('/') ? mount : mount + "/", StringComparison.Ordinal)))
                best = mount;
        }
        return best;
    }

    private static string[] _mounts = [];
    private static long _mountsRead;

    private static string[] MountPoints()
    {
        if (Environment.TickCount64 - Volatile.Read(ref _mountsRead) < 5000 && _mounts.Length > 0) return _mounts;
        try
        {
            _mounts = DriveInfo.GetDrives().Select(d => d.Name).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _mounts = ["/"];
        }
        Volatile.Write(ref _mountsRead, Environment.TickCount64);
        return _mounts;
    }

    public virtual VolumeInfo GetVolumeInfo(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "/";
            DriveInfo? best = null;
            foreach (var d in DriveInfo.GetDrives())
            {
                if (path.StartsWith(d.Name, StringComparison.Ordinal) && (best is null || d.Name.Length > best.Name.Length)) best = d;
            }
            var fs = best?.IsReady == true ? best.DriveFormat : null;
            bool fat = fs is not null && (fs.Contains("fat", StringComparison.OrdinalIgnoreCase) || fs.Contains("msdos", StringComparison.OrdinalIgnoreCase));
            return new VolumeInfo(best?.Name ?? root, fs, false, !OperatingSystem.IsMacOS() && !fat,
                fat ? TimeSpan.FromSeconds(2) : TimeSpan.FromTicks(1), !fat, !fat,
                best?.DriveType == DriveType.Network, best?.DriveType == DriveType.Removable,
                TrashDirectoryFor(path) is not null, best?.IsReady == true ? best.AvailableFreeSpace : -1);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return VolumeInfo.Unknown(path);
        }
    }

    public virtual void CopyFile(string source, string destination, FileCopyOptions options, CopyProgressCallback? progress, CancellationToken ct)
    {
        var info = new FileInfo(source);
        if (options.CopyLinkAsLink && info.LinkTarget is not null)
        {
            File.CreateSymbolicLink(destination, info.LinkTarget);
            return;
        }
        long total = info.Length;
        bool created = false;
        // Unix: while it is written, the copy is readable by no one the source excludes (a private key stays private).
        var create = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, BufferSize = 1, Options = FileOptions.SequentialScan };
        UnixFileMode mode = 0;
        if (!OperatingSystem.IsWindows())
        {
            mode = File.GetUnixFileMode(source);
            create.UnixCreateMode = UnixPermissions.WhileCopying(mode, isDirectory: false);
        }
        try
        {
            using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan))
            using (var dst = new FileStream(destination, create))
            {
                created = true;
                if (total > 0 && !options.DisablePreallocation) dst.SetLength(total);
                var buffer = new byte[BufferSize];
                long done = 0;
                int n;
                while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    dst.Write(buffer, 0, n);
                    done += n;
                    if (progress?.Invoke(done, total) == CopyProgressAction.Cancel) throw new OperationCanceledException(ct);
                }
                if (dst.Length != done) dst.SetLength(done);
                dst.Flush(flushToDisk: options.FlushDestination);
            }
            File.SetLastWriteTimeUtc(destination, info.LastWriteTimeUtc);
            File.SetCreationTimeUtc(destination, info.CreationTimeUtc);
            if (!OperatingSystem.IsWindows())
            {
                // Drives without permissions (FAT, some network shares) refuse or ignore this; the copy itself is complete.
                try { File.SetUnixFileMode(destination, UnixPermissions.ForCopy(mode, isDirectory: false)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch
        {
            // Only a destination this call created is removed: an item that already existed is never touched.
            if (created) TryDelete(destination);
            throw;
        }
    }

    protected static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public virtual void Move(string source, string destination, bool replaceExisting, bool writeThrough = false)
    {
        if (Directory.Exists(source) && new DirectoryInfo(source).LinkTarget is null)
        {
            if (replaceExisting) throw new IOException("Directories are merged item by item, never replaced wholesale.");
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination, replaceExisting);
        }
    }

    public virtual void CreateDirectory(string path)
    {
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException($"An item named \"{Path.GetFileName(path)}\" already exists.");
        Directory.CreateDirectory(path);
    }

    public virtual void DeleteFile(string path) => File.Delete(path);

    public virtual void DeleteDirectory(string path)
    {
        var di = new DirectoryInfo(path);
        if (di.LinkTarget is not null)
        {
            // Removing a link removes only the link.
            di.Delete(recursive: false);
            return;
        }
        Directory.Delete(path, recursive: false);
    }

    public virtual void SetAttributes(string path, FileAttributes attributes) => File.SetAttributes(path, attributes);

    public virtual void SetTimes(string path, DateTime? createdUtc, DateTime? modifiedUtc)
    {
        bool dir = Directory.Exists(path);
        if (createdUtc is { } c)
        {
            if (dir) Directory.SetCreationTimeUtc(path, c);
            else File.SetCreationTimeUtc(path, c);
        }
        if (modifiedUtc is { } m)
        {
            if (dir) Directory.SetLastWriteTimeUtc(path, m);
            else File.SetLastWriteTimeUtc(path, m);
        }
    }

    public virtual bool TryCopyLink(string source, string destination, bool isDirectory, out string? error)
    {
        error = null;
        try
        {
            var target = isDirectory ? new DirectoryInfo(source).LinkTarget : new FileInfo(source).LinkTarget;
            if (target is null)
            {
                error = "The item is not a link.";
                return false;
            }
            if (isDirectory) Directory.CreateSymbolicLink(destination, target);
            else File.CreateSymbolicLink(destination, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    public virtual RecycleClassification ClassifyRecycle(string path, long size) =>
        TrashDirectoryFor(path) is null ? RecycleClassification.NoRecycleBin : RecycleClassification.Recyclable;

    /// <summary>The trash on the path's own volume (see <see cref="UnixTrash"/>), or null where there is none.</summary>
    public static string? TrashDirectoryFor(string path) => UnixTrash.For(path)?.Root;

    public virtual IReadOnlyList<RecycleResult> Recycle(IReadOnlyList<string> paths, Action<string>? itemStarted, CancellationToken ct)
    {
        var results = new List<RecycleResult>();
        foreach (var p in paths)
        {
            if (ct.IsCancellationRequested)
            {
                results.Add(new RecycleResult(p, RecycleOutcome.NotAttempted));
                continue;
            }
            itemStarted?.Invoke(p);
            var trash = OperatingSystem.IsWindows() ? null : UnixTrash.For(p);
            if (trash is null || OperatingSystem.IsWindows())
            {
                results.Add(new RecycleResult(p, RecycleOutcome.Aborted, null, "No trash is available for this location."));
                continue;
            }
            try
            {
                results.Add(new RecycleResult(p, RecycleOutcome.Recycled, UnixTrash.Put(p, trash)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(new RecycleResult(p, RecycleOutcome.Failed, null, ex.Message));
            }
        }
        return results;
    }

    public virtual bool TryRestoreRecycled(string recycledId, string originalPath, out string? error)
    {
        error = null;
        try
        {
            if (File.Exists(originalPath) || Directory.Exists(originalPath))
            {
                error = "An item already exists at the original location.";
                return false;
            }
            if (Directory.Exists(recycledId)) Directory.Move(recycledId, originalPath);
            else if (File.Exists(recycledId)) File.Move(recycledId, originalPath);
            else
            {
                error = "The item is no longer in the trash.";
                return false;
            }
            if (UnixTrash.InfoFileFor(recycledId) is { } info) TryDelete(info);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    public virtual string? ReadOriginMark(string path) => null;

    public virtual bool WriteOriginMark(string path, string mark) => false;

    public virtual IReadOnlyList<string> GetAlternateStreams(string path) => [];

    public virtual void CreateLink(string linkPath, string target, LinkKind kind, bool isDirectory)
    {
        if (File.Exists(linkPath) || Directory.Exists(linkPath) || new FileInfo(linkPath).LinkTarget is not null)
            throw new IOException($"An item named \"{Path.GetFileName(linkPath)}\" already exists.");
        switch (kind)
        {
            case LinkKind.Symbolic when isDirectory:
                Directory.CreateSymbolicLink(linkPath, target);
                break;
            case LinkKind.Symbolic:
                File.CreateSymbolicLink(linkPath, target);
                break;
            case LinkKind.Hard:
                if (!(OperatingSystem.IsWindows() ? NativeLinks.CreateHardLinkW(linkPath, target, 0) : NativeLinks.Link(target, linkPath) == 0))
                    throw new IOException(OperatingSystem.IsWindows()
                        ? new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastPInvokeError()).Message
                        : $"The hard link could not be created (error {System.Runtime.InteropServices.Marshal.GetLastPInvokeError()}).");
                break;
            default:
                throw new PlatformNotSupportedException("Junctions exist only on Windows; create a symbolic link instead.");
        }
    }

    private bool? _canCreateSymbolicLinks;
    private bool _probed;

    public virtual bool? CanCreateSymbolicLinks
    {
        get
        {
            if (_probed) return _canCreateSymbolicLinks;
            string probe = Path.Combine(Path.GetTempPath(), $"filecat-link-probe-{Guid.NewGuid():N}");
            try
            {
                File.CreateSymbolicLink(probe, "target-that-does-not-exist");
                _canCreateSymbolicLinks = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // ERROR_PRIVILEGE_NOT_HELD: no Developer Mode and no privilege. Anything else says nothing about links.
                _canCreateSymbolicLinks = (ex.HResult & 0xFFFF) == 1314 ? false : null;
            }
            finally
            {
                try { File.Delete(probe); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            _probed = true;
            return _canCreateSymbolicLinks;
        }
    }

    public virtual string? GetFileIdentity(string path) => null;

    /// <summary>Streaming content hash for verification and checksum features.</summary>
    public static byte[] HashFile(string path, HashAlgorithmName algorithm, CancellationToken ct, Action<long>? progress = null)
    {
        using var hash = IncrementalHash.CreateHash(algorithm);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
        var buffer = new byte[BufferSize];
        long done = 0;
        int n;
        while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, n);
            done += n;
            progress?.Invoke(done);
        }
        return hash.GetHashAndReset();
    }
}

internal static partial class NativeLinks
{
    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static partial bool CreateHardLinkW(string linkPath, string existing, nint security);

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "link", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf8, SetLastError = true)]
    internal static partial int Link(string existing, string linkPath);
}
