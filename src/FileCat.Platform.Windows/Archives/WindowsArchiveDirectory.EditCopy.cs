using FileCat.Platform.Windows.Elevation;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsArchiveDirectory
{
    internal WindowsArchiveFile? OpenSavedCopyOrAbsent(string leaf,long maximumLength)
    {
        ValidateLeaf(leaf);ValidateLimit(WindowsArchiveArtifactKind.Stage,maximumLength);
        lock(Gate)
        {
            EnsureOpen();RequireCapacity();VerifyNamespace();
            int status=TryOpen(Handle,leaf,GenericRead|Synchronize|0x20000,ShareRead,NtFile.Open,
                NonDirectoryFile|OpenReparsePoint,out var handle);
            if(status==NameNotFound){handle.Dispose();VerifyNamespace();return null;}
            if(status<0){handle.Dispose();throw Error(status,"Read the saved-copy name or confirm its absence");}
            return Adopt(handle,leaf,maximumLength,artifact:false,writable:false,mayRename:false);
        }
    }
}
