using System.Security.AccessControl;
using System.Security.Principal;
using FileCat.Core.Edit;
using FileCat.Core.State;
using FileCat.Core.Resources;
using FileCat.Core.Archives;
using FileCat.Platform.Windows.Elevation;
using FileCat.Platform.Windows.Shell;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

/// <summary>Unregistered edit namespace primitives. Created children have explicit protected ACLs before working bytes exist.</summary>
internal sealed class WindowsEditDirectoryLease : EditDirectoryLease
{
    private readonly SafeFileHandle directory;
    private readonly LocalIconFileLease? rootPins;
    private readonly WindowsEditDirectoryLease? parent;
    private readonly IDisposable? parentOwner;
    private readonly string? leaf;
    private readonly string owner;
    private readonly byte[] security;
    private readonly bool isPrivate;
    internal override string Path { get; }
    internal override string Identity { get; }
    internal override SafeFileHandle Handle => directory;

    private WindowsEditDirectoryLease(string path,SafeFileHandle directory,string owner,bool isPrivate,
        LocalIconFileLease? rootPins=null,WindowsEditDirectoryLease? parent=null,string? leaf=null)
    {
        Path=path;this.directory=directory;this.owner=owner;this.isPrivate=isPrivate;this.rootPins=rootPins;this.parent=parent;this.leaf=leaf;
        try
        {
            parentOwner=parent?.RetainForChild();Identity=ProtectedHexFile.Identity(directory).ToString();
            var descriptor=new RawSecurityDescriptor($"O:{owner}D:P(A;OICI;FA;;;{owner})(A;OICI;FA;;;SY)");
            security=new byte[descriptor.BinaryLength];descriptor.GetBinaryForm(security,0);
            ReviewCore(default);
        }
        catch { directory.Dispose();rootPins?.Dispose();parentOwner?.Dispose();throw; }
    }
    internal static WindowsEditDirectoryLease OpenRoot(string path,bool requirePrivate,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Native Windows edit storage is required.");
        if(WindowsTokenAuthority.IsHighIntegrity)throw new UnauthorizedAccessException("Edit storage requires an unelevated FileCat process.");
        string full=System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(path));
        var pins=LocalIconFileLease.TryOpenDirectory(full,LeaseLinks.Refuse,removable:true)
            ??throw new IOException("The edit root cannot be pinned without following links.");
        SafeFileHandle? handle=null;
        try
        {
            handle=OpenOrThrow(null,@"\??\"+full,GenericRead|0x20000|Synchronize,ShareRead|ShareWrite,Open,
                DirectoryFile|OpenReparsePoint,"Open native edit root");
            using var identity=WindowsIdentity.GetCurrent();string owner=identity.User?.Value??throw new UnauthorizedAccessException("The edit owner is unavailable.");
            var result=new WindowsEditDirectoryLease(full,handle,owner,requirePrivate,pins);handle=null;return result;
        }
        catch { handle?.Dispose();pins.Dispose();throw; }
    }
    protected override void ReviewCore(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();WindowsArchiveNative.SourceDirectory(directory);
        if(ProtectedHexFile.Identity(directory).ToString()!=Identity)throw new IOException("The held edit directory changed.");
        if(parent is not null)
        {
            parent.ReviewRetained(ct);
            using var named=OpenOrThrow(parent.Handle,leaf!,ReadAttributes|Synchronize,ShareAll,Open,DirectoryFile|OpenReparsePoint,"Review native edit directory");
            WindowsArchiveNative.SourceDirectory(named);
            if(ProtectedHexFile.Identity(named).ToString()!=Identity)throw new IOException("The edit directory name identifies a different object.");
        }
        else if(rootPins is null||ProtectedHexFile.Identity(rootPins.LastHandle).ToString()!=Identity)
            throw new IOException("The pinned edit root changed.");
        if(isPrivate)WindowsArchiveNative.RequirePrivateEditSecurity(directory,owner,true);
    }
    internal override EditDirectoryLease CreateDirectory(string name,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            int status=TryOpen(directory,name,GenericRead|GenericWrite|0x20000|Synchronize,ShareRead|ShareWrite,Create,
                DirectoryFile|OpenReparsePoint,out var child,(uint)FileAttributes.Directory,security);
            if(status<0){child.Dispose();throw Error(status,"Create private edit directory");}
            var result=new WindowsEditDirectoryLease(System.IO.Path.Combine(Path,name),child,owner,true,parent:this,leaf:name);
            try{ReviewCore(ct);return result;}catch{result.Dispose();throw;}
        }
    }
    internal override EditDirectoryLease OpenDirectory(string name,string? expected,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            var child=OpenOrThrow(directory,name,GenericRead|0x20000|Synchronize,ShareRead|ShareWrite,Open,
                DirectoryFile|OpenReparsePoint,"Open private edit directory");
            var result=new WindowsEditDirectoryLease(System.IO.Path.Combine(Path,name),child,owner,true,parent:this,leaf:name);
            try
            {
                if(expected is not null&&result.Identity!=expected)throw new IOException("The saved edit directory identity changed.");
                ReviewCore(ct);return result;
            }
            catch{result.Dispose();throw;}
        }
    }
    internal override FileStream CreateFile(string name,CancellationToken ct)=>OpenFileCore(name,null,true,ct);
    internal override FileStream OpenFile(string name,string? expected,CancellationToken ct)=>OpenFileCore(name,expected,false,ct);
    internal override FileStream OpenWorkingFile(string name,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            if(!isPrivate)throw new UnauthorizedAccessException("Working bytes need a private edit directory.");
            var handle=OpenOrThrow(directory,name,GenericRead|0x20000|Synchronize,ShareRead,Open,NonDirectoryFile|OpenReparsePoint,"Open private editor working file");
            var file=new FileStream(handle,FileAccess.Read);
            try{ReviewWorkingFile(file,name,ct);return file;}catch{file.Dispose();throw;}
        }
    }
    internal override ContentRevision ReviewWorkingFile(FileStream file,string name,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            if(!isPrivate)throw new UnauthorizedAccessException("Working bytes need a private edit directory.");
            WindowsArchiveNative.Regular(file.SafeFileHandle,EditSessionStore.MaxMemberBytes,true,false);
            WindowsArchiveNative.RequirePrivateEditSecurity(file.SafeFileHandle,owner,false);
            var identity=ProtectedHexFile.Identity(file.SafeFileHandle);var facts=GetBasic(file.SafeFileHandle);
            using var named=OpenOrThrow(directory,name,ReadAttributes|Synchronize,ShareAll,Open,NonDirectoryFile|OpenReparsePoint,"Review editor working-file name");
            if(ProtectedHexFile.Identity(named)!=identity)throw new IOException("The editor replaced the working file during its review.");
            ReviewCore(ct);
            return new(RandomAccess.GetLength(file.SafeFileHandle),DateTime.FromFileTimeUtc(facts.LastWriteTime).Ticks,
                FormattableString.Invariant($"{identity}:{facts.CreationTime}:{facts.ChangeTime}:{facts.FileAttributes}"));
        }
    }
    internal override IReadOnlyList<string> Names(CancellationToken ct)
    {lock(Gate){RequireOpen();ReviewCore(ct);var names=List(directory);if(names.Count>100000)throw new IOException("The edit namespace exceeds its entry budget.");ReviewCore(ct);return names.Select(entry=>entry.Name).ToArray();}}
    internal override ArchiveOriginSnapshot ReadOrigin(FileStream file,string name,CancellationToken ct)
    {
        lock(Gate)
        {
            ReviewWorkingFile(file,name,ct);var identity=ProtectedHexFile.Identity(file.SafeFileHandle);
            int status=TryOpen(directory,name+":Zone.Identifier",GenericRead|Synchronize,ShareAll,Open,
                NonDirectoryFile|OpenReparsePoint,out var origin);
            using(origin)
            {
                if(status==NameNotFound)return ArchiveOriginSnapshot.Absent(ArchiveOriginFormat.WindowsZoneIdentifier);
                if(status<0)throw Error(status,"Read native edit download mark");
                WindowsArchiveNative.Regular(origin,ArchiveOriginSnapshot.MaxBytes,false,false);
                if(ProtectedHexFile.Identity(origin)!=identity)throw new IOException("The edit download mark belongs to another object.");
                byte[] bytes=new byte[checked((int)RandomAccess.GetLength(origin))];int offset=0;
                while(offset<bytes.Length)
                {
                    ct.ThrowIfCancellationRequested();int read=RandomAccess.Read(origin,bytes.AsSpan(offset),offset);
                    if(read<=0)throw new IOException("The edit download mark ended during review.");offset+=read;
                }
                if(RandomAccess.GetLength(origin)!=bytes.Length)throw new IOException("The edit download mark changed during review.");
                ReviewWorkingFile(file,name,ct);return ArchiveOriginSnapshot.Present(ArchiveOriginFormat.WindowsZoneIdentifier,bytes);
            }
        }
    }
    internal override void CopyOrigin(FileStream file,string name,ArchiveOriginSnapshot origin,CancellationToken ct)
    {lock(Gate){ReviewWorkingFile(file,name,ct);WindowsArchiveEditOrigin.Copy(file,System.IO.Path.Combine(Path,name),origin,ct);ReviewWorkingFile(file,name,ct);}}
    internal override void DeleteSnapshot(FileStream file,string name,ArchiveFileObservation expected,CancellationToken ct)
    {
        lock(Gate)
        {
            if(!ObserveSnapshot(file,name,ct).Matches(expected))throw new IOException("The retained snapshot changed; its files are kept.");
            WindowsArchiveNative.RequireCreationStreams(file.SafeFileHandle,expected.Version.Length,expected.Origin);
            try{WindowsArchiveNative.DeleteExact(file.SafeFileHandle);file.Dispose();}
            catch(System.ComponentModel.Win32Exception error){throw new IOException("The private commit snapshot could not be removed: "+Path,error);}
        }
    }
    internal override bool CanRemoveOwnedDirectories=>true;
    internal override void RemoveEmpty(CancellationToken ct)
    {
        lock(Gate)
        {
            RequireNoChildren();ReviewCore(ct);
            if(!isPrivate||parent is null||leaf is null)throw new IOException("Only an owned private child directory can be removed.");
            if(Names(ct).Count!=0)throw new IOException("The private edit directory is not empty: "+Path);
            // Preserve exact-object authority while dropping the original access/share combination. No path reopen.
            using var retained=WindowsArchiveNative.ReopenEditDirectory(directory,GenericRead|0x20000|Synchronize,ShareAll);
            if(ProtectedHexFile.Identity(retained).ToString()!=Identity)throw new IOException("The retained directory identity changed.");
            directory.Dispose();
            using var deleting=WindowsArchiveNative.ReopenEditDirectory(retained,GenericRead|Delete|0x20000|Synchronize,ShareRead|ShareWrite);
            WindowsArchiveNative.SourceDirectory(deleting);WindowsArchiveNative.RequirePrivateEditSecurity(deleting,owner,true);
            if(ProtectedHexFile.Identity(deleting).ToString()!=Identity)throw new IOException("The directory removal handle identifies another object.");
            parent.ReviewRetained(ct);
            using(var named=OpenOrThrow(parent.Handle,leaf,ReadAttributes|Synchronize,ShareAll,Open,DirectoryFile|OpenReparsePoint,"Review empty edit directory"))
                if(ProtectedHexFile.Identity(named).ToString()!=Identity)throw new IOException("The directory name changed; its files are kept.");
            if(List(deleting).Count!=0)throw new IOException("A new child appeared in the private edit directory: "+Path);
            ct.ThrowIfCancellationRequested();
            try{WindowsArchiveNative.DeleteExact(deleting);deleting.Dispose();}
            catch(System.ComponentModel.Win32Exception error){throw new IOException("The empty private edit directory could not be removed: "+Path,error);}
            Dispose();
        }
    }
    internal override string Identify(SafeFileHandle file)
    {WindowsArchiveNative.Regular(file,FileCat.Core.Edit.EditSessionStore.MaxMemberBytes,true,false);WindowsArchiveNative.RequirePrivateSecurity(file,owner);return ProtectedHexFile.Identity(file).ToString();}
    internal override void PublishNewFile(FileStream file,string source,string target,EditFileVersion expected,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(source);Leaf(target);ReviewCore(ct);
            if(!isPrivate||source==target||Inspect(file,ct)!=expected)throw new IOException("The new edit record does not match its owned artifact.");
            using(var named=OpenOrThrow(directory,source,ReadAttributes|Synchronize,ShareAll,Open,NonDirectoryFile|OpenReparsePoint,"Review new edit record"))
                if(ProtectedHexFile.Identity(named).ToString()!=expected.Identity)throw new IOException("The edit record's source name changed.");
            file.Flush(true);WindowsArchiveNative.Flush(file.SafeFileHandle);ct.ThrowIfCancellationRequested();
            Rename(file.SafeFileHandle,directory,target,replace:false);
            ReviewCore(CancellationToken.None);
            using(var named=OpenOrThrow(directory,target,ReadAttributes|Synchronize,ShareAll,Open,NonDirectoryFile|OpenReparsePoint,"Review published edit record"))
                if(ProtectedHexFile.Identity(named).ToString()!=expected.Identity)throw new IOException("The published edit record needs review.");
            if(Inspect(file,CancellationToken.None)!=expected)throw new IOException("The published edit record bytes changed.");
        }
    }
    private FileStream OpenFileCore(string name,string? expected,bool create,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            if(!isPrivate)throw new UnauthorizedAccessException("Working bytes need a private edit directory.");
            byte[]? fileSecurity=null;
            if(create){var descriptor=new RawSecurityDescriptor($"O:{owner}D:P(A;;FA;;;{owner})(A;;FA;;;SY)");fileSecurity=new byte[descriptor.BinaryLength];descriptor.GetBinaryForm(fileSecurity,0);}
            int status=TryOpen(directory,name,GenericRead|0x20000|Synchronize|(create?GenericWrite|Delete:0),ShareRead,create?Create:Open,
                NonDirectoryFile|OpenReparsePoint,out var file,(uint)FileAttributes.Normal,fileSecurity);
            if(status<0){file.Dispose();throw Error(status,"Open private edit file");}
            try
            {
                WindowsArchiveNative.Regular(file,FileCat.Core.Edit.EditSessionStore.MaxMemberBytes,artifact:true,mayRename:false);
                WindowsArchiveNative.RequirePrivateSecurity(file,owner);
                if(expected is not null&&ProtectedHexFile.Identity(file).ToString()!=expected)throw new IOException("The private edit file identity changed.");
                ReviewCore(ct);return new(file,create?FileAccess.ReadWrite:FileAccess.Read);
            }
            catch{file.Dispose();throw;}
        }
    }
    protected override void CloseCore()
    {try{directory.Dispose();}finally{try{rootPins?.Dispose();}finally{parentOwner?.Dispose();}}}
}
