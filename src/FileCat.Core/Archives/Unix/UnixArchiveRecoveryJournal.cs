using System.Runtime.InteropServices;
using System.Security.Cryptography;
using FileCat.Core.FileSystem;
using FileCat.Core.Jobs;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Archives.Unix;

/// <summary>
/// Retains one unfinished archive journal through a pinned parent descriptor and an exclusive advisory lock for the whole
/// review and append. The held file, not its pathname, is re-observed before every recovery record.
/// </summary>
internal sealed partial class UnixArchiveRecoveryJournal : IDisposable
{
    private readonly SafeFileHandle parent;
    private readonly SafeFileHandle file;
    private readonly string parentPath, leaf;
    private readonly FileIdentity parentIdentity, identity;
    internal JobJournal Journal { get; }

    private UnixArchiveRecoveryJournal(string path, string? expectedSha256, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute recovery journal is required.");
        parentPath = Path.GetDirectoryName(path) ?? throw new ArgumentException("A journal folder is required.");
        leaf = Path.GetFileName(path);
        parent = UnixArchiveNative.PinDirectory(parentPath);
        try
        {
            parentIdentity = UnixFiles.Stat(parent)?.Identity ?? throw UnixArchiveNative.Unavailable();
            file = UnixArchiveNative.Child(parent, leaf, write: true);
            try
            {
                identity = UnixFiles.Stat(file)?.Identity ?? throw UnixArchiveNative.Unavailable();
                // Refuse a journal that a live writer (or another review) still holds.
                if (Flock(UnixArchiveNative.Fd(file), 2 | 4 /* LOCK_EX | LOCK_NB */) != 0)
                    throw new IOException("The archive journal is still in use; retain it.");
                Review();
                var stream = new FileStream(file, FileAccess.ReadWrite, 64 * 1024, isAsync: false);
                try { Journal = JobJournal.ReopenArchiveRecovery(path, stream, expectedSha256 ?? HashCurrent(stream, ct), Review, ct); }
                catch { stream.Dispose(); throw; }
            }
            catch { file.Dispose(); throw; }
        }
        catch { parent.Dispose(); throw; }
    }

    internal static UnixArchiveRecoveryJournal Open(string path, string expectedSha256, CancellationToken ct) => new(path, expectedSha256, ct);
    internal static UnixArchiveRecoveryJournal OpenCurrent(string path, CancellationToken ct) => new(path, null, ct);

    private void Review()
    {
        using var fresh = UnixArchiveNative.PinDirectory(parentPath);
        if (UnixFiles.Stat(fresh)?.Identity != parentIdentity || UnixFiles.Stat(parent)?.Identity != parentIdentity)
            throw new IOException("The archive journal folder changed during recovery.");
        using var named = UnixArchiveNative.Child(parent, leaf);
        var held = UnixFiles.Stat(file);
        if (UnixFiles.Stat(named)?.Identity != identity || held is not { IsRegular: true } || held.Value.Identity != identity ||
            held.Value.Size is <= 0 or > ArchiveWritePolicy.MaxMetadataBytes || ArchiveUnixOuterMetadata.Capture(file).Native.LinkCount != 1)
            throw new IOException("The retained recovery journal is no longer the reviewed single-link regular object.");
    }

    private static string HashCurrent(Stream stream, CancellationToken ct)
    {
        long length = stream.Length;
        if (length is <= 0 or > ArchiveWritePolicy.MaxMetadataBytes) throw new IOException("The archive journal exceeds its review bound.");
        stream.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); byte[] buffer = new byte[64 * 1024]; long consumed = 0;
        for (int read; (read = stream.Read(buffer)) != 0;)
        {
            ct.ThrowIfCancellationRequested(); consumed = checked(consumed + read);
            if (consumed > length) throw new IOException("The retained archive journal changed during review.");
            hash.AppendData(buffer, 0, read);
        }
        if (consumed != length || stream.Length != length) throw new IOException("The retained archive journal changed during review.");
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    public void Dispose() { try { Journal.Dispose(); } finally { parent.Dispose(); } }

    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static partial int Flock(int fd, int operation);
}
