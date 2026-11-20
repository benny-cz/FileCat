using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Retains the journal's actual NTFS parent/file through review and append. No pathname reopening.</summary>
internal sealed class WindowsArchiveRecoveryJournal : IDisposable
{
    private readonly WindowsArchiveDirectory parent;
    private readonly SafeFileHandle handle;
    private readonly string leaf;
    private readonly FileIdentity identity;
    internal JobJournal Journal { get; }

    private WindowsArchiveRecoveryJournal(string path, string? expectedSha256, CancellationToken ct)
    {
        if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("An absolute recovery journal is required.");
        string full = Path.GetFullPath(path); parent = WindowsArchiveDirectory.Open(Path.GetDirectoryName(full)!); leaf = Path.GetFileName(full);
        try
        {
            lock (parent.Gate)
            {
                WindowsArchiveDirectory.ValidateLeaf(leaf); parent.VerifyNamespace();
                handle = NtFile.OpenOrThrow(parent.Handle, leaf, NtFile.GenericRead | NtFile.GenericWrite | NtFile.Synchronize | 0x20000,
                    NtFile.ShareRead, NtFile.Open, NtFile.NonDirectoryFile | NtFile.OpenReparsePoint, "Retain exclusive archive recovery journal");
                try
                {
                    identity = ProtectedHexFile.Identity(handle); Review();
                    var stream = new FileStream(handle, FileAccess.ReadWrite, 64 * 1024, isAsync: false);
                    try { Journal = JobJournal.ReopenArchiveRecovery(full, stream, expectedSha256 ?? HashCurrent(stream, ct), Review, ct); }
                    catch { stream.Dispose(); throw; }
                }
                catch { handle.Dispose(); throw; }
            }
        }
        catch { parent.Dispose(); throw; }
    }
    internal static WindowsArchiveRecoveryJournal Open(string path, string expectedSha256, CancellationToken ct) => new(path, expectedSha256, ct);
    // A new user review starts with the actual retained journal. It does not trust a pathname hash from a stale row.
    internal static WindowsArchiveRecoveryJournal OpenCurrent(string path, CancellationToken ct) => new(path, null, ct);
    private static string HashCurrent(Stream stream, CancellationToken ct)
    {
        long length = stream.Length;
        if (length is <= 0 or > ArchiveWritePolicy.MaxMetadataBytes) throw new IOException("The archive journal exceeds its review bound.");
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
    private void Review()
    {
        lock (parent.Gate)
        {
            parent.VerifyNamespace(); parent.RequireName(leaf, identity);
            if (ProtectedHexFile.Identity(handle) != identity || WindowsArchiveNative.Regular(handle, ArchiveWritePolicy.MaxMetadataBytes, false, false) != 1)
                throw new IOException("The retained recovery journal is no longer the reviewed single-link regular object.");
        }
    }
    public void Dispose() { try { Journal.Dispose(); } finally { parent.Dispose(); } }
}
