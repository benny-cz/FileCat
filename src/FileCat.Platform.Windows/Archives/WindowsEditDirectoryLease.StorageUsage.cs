using FileCat.Core.Archives;
using FileCat.Core.Edit;
using FileCat.Platform.Windows.Shell;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsEditDirectoryLease
{
    internal override EditDirectoryLease OpenStorageDirectory(string name,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            var handle=OpenOrThrow(directory,name,GenericRead|0x20000|Synchronize,ShareAll,Open,
                DirectoryFile|OpenReparsePoint,"Open read-only edit storage inventory");
            var child=new WindowsEditDirectoryLease(System.IO.Path.Combine(Path,name),handle,owner,true,parent:this,leaf:name);
            try{ReviewCore(ct);return child;}catch{child.Dispose();throw;}
        }
    }
    internal override EditStoredFileSize MeasureStoredFile(string name,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            if(!isPrivate)throw new UnauthorizedAccessException("Stored edit sizes need a private namespace.");
            using var file=OpenOrThrow(directory,name,ReadAttributes|0x20000|Synchronize,ShareAll,Open,
                NonDirectoryFile|OpenReparsePoint,"Measure native edit storage");
            WindowsArchiveNative.Regular(file,ArchiveWritePolicy.MaxPhysicalBytes,false,false);
            WindowsArchiveNative.RequirePrivateEditSecurity(file,owner,false);
            var identity=ProtectedHexFile.Identity(file);var before=GetBasic(file);
            long length=RandomAccess.GetLength(file);var streams=WindowsArchiveNative.MeasureStorageStreams(file,false,ct);
            using var named=OpenOrThrow(directory,name,ReadAttributes|Synchronize,ShareAll,Open,
                NonDirectoryFile|OpenReparsePoint,"Review stored edit size name");
            if(ProtectedHexFile.Identity(named)!=identity||streams.Data!=length||RandomAccess.GetLength(file)!=length||Version(GetBasic(file))!=Version(before))
                throw new IOException("The stored edit changed during size review.");
            ReviewCore(ct);return new(identity.ToString(),length,Version(before),checked(256L+WindowsArchiveOuterMetadata.MaxSecurityBytes+WindowsArchiveNative.StorageStreamCatalogueBytes+streams.Auxiliary));
            string Version(BasicInfo facts)=>FormattableString.Invariant($"{identity}:{facts.CreationTime}:{facts.LastWriteTime}:{facts.ChangeTime}:{facts.FileAttributes}");
        }
    }
    internal override EditStoredDirectorySize MeasureStoredDirectory(CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();ReviewCore(ct);
            var before=GetBasic(directory);
            var streams=WindowsArchiveNative.MeasureStorageStreams(directory,true,ct);
            var facts=GetBasic(directory);ReviewCore(ct);
            if(before.CreationTime!=facts.CreationTime||before.LastWriteTime!=facts.LastWriteTime||before.ChangeTime!=facts.ChangeTime||before.FileAttributes!=facts.FileAttributes)
                throw new IOException("The stored directory changed during size review.");
            string version=FormattableString.Invariant($"{Identity}:{facts.CreationTime}:{facts.LastWriteTime}:{facts.ChangeTime}:{facts.FileAttributes}");
            return new(Identity,version,checked(256L+WindowsArchiveOuterMetadata.MaxSecurityBytes+WindowsArchiveNative.StorageStreamCatalogueBytes+streams.Data+streams.Auxiliary));
        }
    }
}
