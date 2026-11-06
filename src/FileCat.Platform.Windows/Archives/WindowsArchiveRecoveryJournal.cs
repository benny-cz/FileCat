using FileCat.Core.Archives;
using FileCat.Core.Jobs;
using FileCat.Platform.Windows.Elevation;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Retains the journal's actual NTFS parent/file through review and append. No pathname reopening.</summary>
internal sealed class WindowsArchiveRecoveryJournal : IDisposable
{
    private readonly WindowsArchiveDirectory parent;
    private readonly SafeFileHandle handle;
    private readonly string leaf;
    private readonly FileIdentity identity;
    internal JobJournal Journal { get; }

    private WindowsArchiveRecoveryJournal(string path, string expectedSha256, CancellationToken ct)
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
                    Journal = JobJournal.ReopenArchiveRecovery(full, new FileStream(handle, FileAccess.ReadWrite, 64 * 1024, isAsync: false), expectedSha256, Review, ct);
                }
                catch { handle.Dispose(); throw; }
            }
        }
        catch { parent.Dispose(); throw; }
    }
    internal static WindowsArchiveRecoveryJournal Open(string path, string expectedSha256, CancellationToken ct) => new(path, expectedSha256, ct);
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
