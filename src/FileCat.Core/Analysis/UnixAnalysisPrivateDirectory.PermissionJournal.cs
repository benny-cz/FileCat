using System.Runtime.InteropServices;
using FileCat.Core.Jobs;

namespace FileCat.Core.Analysis;

public sealed unsafe partial class UnixAnalysisPrivateDirectory
{
    internal static UnixAnalysisPrivateDirectory OpenPermissionJournalDirectory(string parent) =>
        new(parent, PermissionJournalNames.Directory, permissionJournal: true);

    internal override AnalysisPrivateFile OpenPermissionJournal(string name)
    {
        PermissionJournalNames.Require(name);
        Verify();
        var owned = OpenOwned(Root, name, FileFlags, 0);
        try
        {
            Private(owned.Handle, directory: false, links: 1);
            LockInventoryRead(owned.Handle);
            VerifyPermissionJournalName(owned.Handle, name);
            return new PrivateFile(this, owned, writable: false, permissionJournalName: name);
        }
        catch { owned.Dispose(); throw; }
    }

    internal static PermissionJournalFile CreatePermissionJournal(string directory, string leaf, CancellationToken cancellation)
    {
        PermissionJournalNames.Require(leaf);
        cancellation.ThrowIfCancellationRequested();
        var root = OpenPermissionJournalDirectory(directory);
        try { return root.OpenNewPermissionJournal(Path.Combine(directory, PermissionJournalNames.Directory, leaf), leaf); }
        catch { root.Dispose(); throw; }
    }

    private PermissionJournalFile OpenNewPermissionJournal(string displayPath, string leaf)
    {
        Verify();
        int createExclusive = _mac ? 0x200 | 0x800 : 0x40 | 0x80;
        var owned = OpenOwned(Root, leaf, FileFlags | 2 | createExclusive, 0x180); // O_RDWR, 0600
        try
        {
            Private(owned.Handle, directory: false, links: 1);
            if (Flock(owned.Handle, 2 | 4) != 0) throw Error("Private permissions journal locking is unavailable.");
            if (Fsync(Root) != 0) throw Error("The permissions journal directory entry could not be flushed.");
            // The permissions directory itself may have just been created by this or another FileCat process.
            if (!_mac && Fsync(_chain[^2].Handle.Handle) != 0) throw Error("The private permissions directory name could not be flushed.");
            var stream = owned.Handle.OpenStream(FileAccess.ReadWrite);
            return new(displayPath, stream, VerifyFile, new PermissionJournalOwner(this, owned));
        }
        catch { owned.Dispose(); throw; }

        void VerifyFile()
        {
            Verify();
            Private(owned.Handle, directory: false, links: 1);
            VerifyPermissionJournalName(owned.Handle, leaf);
            if (_mac && Fcntl(owned.Handle, 51) != 0) throw Error("Private permissions journal durability could not be confirmed.");
        }
    }

    private void VerifyPermissionJournalName(SafeHandle held, string name)
    {
        using var current = OpenOwned(Root, name, FileFlags, 0);
        if (!Same(Stat(current.Handle), Stat(held))) throw new IOException("The permissions journal name changed.");
    }

    private sealed class PermissionJournalOwner(UnixAnalysisPrivateDirectory directory, OwnedHandle handle) : IDisposable
    {
        public void Dispose() { try { handle.Dispose(); } finally { directory.Dispose(); } }
    }
}
