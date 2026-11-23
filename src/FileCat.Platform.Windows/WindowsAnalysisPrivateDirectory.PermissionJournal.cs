using FileCat.Core.Jobs;
using FileCat.Core.Analysis;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows;

public sealed unsafe partial class WindowsAnalysisPrivateDirectory
{
    internal override AnalysisPrivateFile OpenPermissionJournal(string name)
    {
        PermissionJournalNames.Require(name);
        Verify();
        var owned = OpenOwned(Root, name, GenericRead | ReadControl | Synchronize, ShareRead, Open, NonDirectoryFile | OpenReparsePoint);
        try { Private(owned.Handle, directory: false); return new PrivateFile(this, owned, writable: false); }
        catch { owned.Dispose(); throw; }
    }

    internal static PermissionJournalFile CreatePermissionJournal(string directory, string leaf, CancellationToken cancellation)
    {
        PermissionJournalNames.Require(leaf);
        cancellation.ThrowIfCancellationRequested();
        var root = new WindowsAnalysisPrivateDirectory(directory, PermissionJournalNames.Directory);
        try { return root.OpenNewPermissionJournal(Path.Combine(directory, PermissionJournalNames.Directory, leaf), leaf); }
        catch { root.Dispose(); throw; }
    }

    private PermissionJournalFile OpenNewPermissionJournal(string displayPath, string leaf)
    {
        Verify();
        var owned = OpenOwned(Root, leaf, GenericRead | GenericWrite | ReadControl | Synchronize, ShareRead, Create,
            NonDirectoryFile | OpenReparsePoint, _fileSecurity);
        try
        {
            Private(owned.Handle, directory: false);
            if (!FlushMaintenance(Root)) throw new IOException("The permissions journal directory entry could not be flushed.");
            var stream = owned.Handle.OpenStream(FileAccess.ReadWrite);
            return new(displayPath, stream, VerifyFile, new PermissionJournalOwner(this, owned));
        }
        catch { owned.Dispose(); throw; }

        void VerifyFile()
        {
            Verify();
            Private(owned.Handle, directory: false);
            using var current = OpenOwned(Root, leaf, ReadAttributes | ReadControl | Synchronize,
                ShareRead | ShareWrite, Open, NonDirectoryFile | OpenReparsePoint);
            if (FileIdentity(current.Handle) != FileIdentity(owned.Handle)) throw new IOException("The permissions journal name changed.");
        }
    }

    private sealed class PermissionJournalOwner(WindowsAnalysisPrivateDirectory directory, OwnedHandle handle) : IDisposable
    {
        public void Dispose() { try { handle.Dispose(); } finally { directory.Dispose(); } }
    }
}
