using FileCat.Core.Archives;
using FileCat.Core.Edit;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsEditDirectoryLease
{
    internal override EditStoredFileSize MeasureStoredFile(string name,CancellationToken ct)
    {
        lock(Gate)
        {
            using var file=OpenWorkingFile(name,ct);var before=ReviewWorkingFile(file,name,ct);
            var origin=ReadOrigin(file,name,ct);
            WindowsArchiveNative.RequireSnapshotStreams(file.SafeFileHandle,before.Length,origin);
            if(ReviewWorkingFile(file,name,ct)!=before)throw new IOException("The stored edit changed during size review.");
            return new(before,checked(256L+WindowsArchiveOuterMetadata.MaxSecurityBytes+origin.Bytes.Length));
        }
    }
    internal override EditStoredDirectorySize MeasureStoredDirectory(CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();ReviewCore(ct);
            WindowsArchiveNative.RequireSnapshotStreams(directory,0,ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier),directory:true);
            var facts=GetBasic(directory);ReviewCore(ct);
            string version=FormattableString.Invariant($"{Identity}:{facts.CreationTime}:{facts.LastWriteTime}:{facts.ChangeTime}:{facts.FileAttributes}");
            return new(Identity,version,256L+WindowsArchiveOuterMetadata.MaxSecurityBytes);
        }
    }
}
