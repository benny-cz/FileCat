using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Platform.Windows.Shell;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsEditDirectoryLease
{
    internal override bool CanDiscardReviewedSession=>true;
    internal override EditDirectoryLease OpenDiscardDirectory(string name,string? expected,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            if(!isPrivate)throw new UnauthorizedAccessException("Discard requires a private native edit namespace.");
            var child=OpenOrThrow(directory,name,GenericRead|Delete|0x20000|Synchronize,ShareRead|ShareWrite,Open,
                DirectoryFile|OpenReparsePoint,"Pin reviewed edit directory for discard");
            var result=new WindowsEditDirectoryLease(System.IO.Path.Combine(Path,name),child,owner,true,parent:this,leaf:name,
                removal:RemovalAuthority.DiscardReviewed);
            try
            {
                if(expected is not null&&result.Identity!=expected)throw new IOException("The reviewed edit directory was replaced. Its files are kept.");
                ReviewCore(ct);return result;
            }
            catch{result.Dispose();throw;}
        }
    }
    internal override FileStream OpenDiscardFile(string name,string? expected,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            if(!isPrivate)throw new UnauthorizedAccessException("Discard requires a private native edit namespace.");
            var handle=OpenOrThrow(directory,name,GenericRead|Delete|0x20000|Synchronize,ShareRead,Open,
                NonDirectoryFile|OpenReparsePoint,"Pin reviewed edit file for discard");
            try
            {
                WindowsArchiveNative.Regular(handle,EditSessionStore.MaxMemberBytes,true,false);
                WindowsArchiveNative.RequirePrivateEditSecurity(handle,owner,false);
                if(expected is not null&&ProtectedHexFile.Identity(handle).ToString()!=expected)
                    throw new IOException("The reviewed edit file was replaced. Its files are kept.");
                ReviewCore(ct);return new FileStream(handle,FileAccess.Read);
            }
            catch{handle.Dispose();throw;}
        }
    }
    internal override void ReviewRemoval(FileStream file,string name,ArchiveFileObservation expected,CancellationToken ct,bool working=false)
    {
        lock(Gate)
        {
            if(!ObserveSnapshot(file,name,ct,expected.Version.Length,working).Matches(expected))
                throw new IOException("The reviewed edit file changed. Its files are kept.");
            WindowsArchiveNative.RequireSnapshotStreams(file.SafeFileHandle,expected.Version.Length,expected.Origin);
        }
    }
}
