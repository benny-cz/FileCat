using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using FileCat.Core.Archives;
using FileCat.Core.Resources;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Edit;

/// <summary>Unregistered native edit creation/read namespace. No pathname cleanup or publication fallback.</summary>
internal sealed partial class UnixEditDirectoryLease : EditDirectoryLease
{
    private readonly SafeFileHandle directory;
    private readonly UnixEditDirectoryLease? parent;
    private readonly IDisposable? parentOwner;
    private readonly string? leaf;
    private readonly uint user;
    private readonly bool isPrivate;
    internal override string Path { get; }
    internal override string Identity { get; }
    internal override SafeFileHandle Handle => directory;

    private UnixEditDirectoryLease(string path, SafeFileHandle directory, uint user, bool isPrivate,
        UnixEditDirectoryLease? parent=null, string? leaf=null)
    {
        Path=path;this.directory=directory;this.user=user;this.isPrivate=isPrivate;this.parent=parent;this.leaf=leaf;
        try
        {
            parentOwner=parent?.RetainForChild();
            Identity=(UnixFiles.Stat(directory)?.Identity ?? throw UnixArchiveNative.Unavailable()).ToString();
            ReviewCore(default);
        }
        catch { directory.Dispose();parentOwner?.Dispose();throw; }
    }
    internal static UnixEditDirectoryLease OpenRoot(string path, bool requirePrivate, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();uint user=UnixArchiveNative.RequireOrdinaryIdentity();
        string full=System.IO.Path.GetFullPath(path);
        return new(full,UnixArchiveNative.PinDirectory(full),user,requirePrivate);
    }
    protected override void ReviewCore(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (parent is not null)
        {
            parent.ReviewRetained(ct);
            using var named=UnixArchiveNative.ChildDirectory(parent.Handle,leaf!);
            if (UnixFiles.Stat(named)?.Identity.ToString()!=Identity) throw UnixArchiveNative.Unavailable();
        }
        else
        {
            using var named=UnixArchiveNative.PinDirectory(Path);
            if (UnixFiles.Stat(named)?.Identity.ToString()!=Identity) throw UnixArchiveNative.Unavailable();
        }
        if (UnixFiles.Stat(directory) is not { IsDirectory:true } facts || facts.Identity.ToString()!=Identity) throw UnixArchiveNative.Unavailable();
        if (isPrivate) UnixArchiveNative.RequireEditDirectory(directory,user);
    }
    internal override EditDirectoryLease CreateDirectory(string name,CancellationToken ct,bool removable=false)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            var created=UnixArchiveNative.CreateEditDirectory(directory,name,user,ct);
            // A removable folder (a commit capture) may later remove its own reviewed entries, as a discard does.
            var result=new UnixEditDirectoryLease(System.IO.Path.Combine(Path,name),created,user,true,this,name){discard=removable,capture=removable};
            try { ReviewCore(ct);return result; } catch { result.Dispose();throw; }
        }
    }
    internal override EditDirectoryLease OpenDirectory(string name,string? expected,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            var result=new UnixEditDirectoryLease(System.IO.Path.Combine(Path,name),UnixArchiveNative.ChildDirectory(directory,name),user,true,this,name);
            try
            {
                if (expected is not null && result.Identity!=expected) throw UnixArchiveNative.Unavailable();
                ReviewCore(ct);return result;
            }
            catch { result.Dispose();throw; }
        }
    }
    internal override FileStream CreateFile(string name,CancellationToken ct) => OpenFileCore(name,null,true,ct);
    internal override FileStream OpenFile(string name,string? expected,CancellationToken ct) => OpenFileCore(name,expected,false,ct);
    internal override FileStream OpenWorkingFile(string name,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            if(!isPrivate)throw new UnauthorizedAccessException("Working bytes need a private edit directory.");
            Microsoft.Win32.SafeHandles.SafeFileHandle handle;
            // A working copy that is not there is missing (its edit can then be discarded), as on Windows, not unreadable.
            try{handle=UnixArchiveNative.Child(directory,name);}
            catch(IOException error)when(error.HResult==2 /* ENOENT */){throw new FileNotFoundException("The working copy is missing.",name,error);}
            var file=new FileStream(handle,FileAccess.Read);
            try{ReviewWorkingFile(file,name,ct);return file;}catch{file.Dispose();throw;}
        }
    }
    internal override ContentRevision ReviewWorkingFile(FileStream file,string name,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            if(!isPrivate||UnixFiles.Stat(file.SafeFileHandle) is not {IsRegular:true,Owner:var owner,Mode:var mode}||owner!=user||
                (mode&~0x1ffu)!=0)throw new IOException("The working file is not an ordinary creator-owned file in its private directory.");
            // Unix traversal requires access to the retained 0700 parent. Normal editor umasks may leave group/other
            // read/write bits on a replacement, and an editor or the user may make a script being edited executable;
            // those bits confer no access through this private namespace, and FileCat never runs the copy. Set-user-ID,
            // set-group-ID and sticky bits are still refused, as is anything but an ordinary file this user owns.
            var metadata=ArchiveUnixOuterMetadata.Capture(file.SafeFileHandle,ct);var facts=metadata.Native;
            if(facts.LinkCount!=1||facts.Length>EditSessionStore.MaxMemberBytes)throw new IOException("The working file is linked or exceeds its byte budget.");
            // The name binding is read without opening the name: under a Linux removal lease another open of the file (even
            // this process's own) is refused and would start a break of that lease.
            if(UnixFiles.StatChild(directory,name)?.Identity!=facts.Identity)throw new IOException("The editor replaced the working file during its review.");
            ReviewCore(ct);
            Int128 ticks=(Int128)DateTime.UnixEpoch.Ticks+(Int128)facts.Modified.Seconds*TimeSpan.TicksPerSecond+facts.Modified.Nanoseconds/100;
            if(ticks<DateTime.MinValue.Ticks||ticks>DateTime.MaxValue.Ticks)throw new IOException("The editor's modification time is outside the supported range.");
            return new(facts.Length,(long)ticks,facts.Identity+":"+metadata.Digest);
        }
    }
    internal override IReadOnlyList<string> Names(CancellationToken ct)=>Names(ct,100000);
    internal override IReadOnlyList<string> Names(CancellationToken ct,int maximum)
    {
        lock(Gate)
        {
            if(maximum is <0 or >100000)throw new ArgumentOutOfRangeException(nameof(maximum));
            RequireOpen();ReviewCore(ct);var names=new List<string>();
            UnixArchiveNative.EnumerateSelection(directory,entry=>
            {if(names.Count>=maximum)throw new IOException("The edit namespace exceeds its entry budget.");names.Add(entry.Leaf);},ct);
            ReviewCore(ct);return names;
        }
    }
    internal override ArchiveOriginSnapshot ReadOrigin(FileStream file,string name,CancellationToken ct)
    {lock(Gate){ReviewWorkingFile(file,name,ct);return ArchiveUnixOrigin.Read(file.SafeFileHandle,ct);}}
    internal override void CopyOrigin(FileStream file,string name,ArchiveOriginSnapshot origin,CancellationToken ct)
    {lock(Gate){ReviewWorkingFile(file,name,ct);ArchiveUnixOrigin.CopyToNewWorkingFile(file.SafeFileHandle,origin,ct);ReviewWorkingFile(file,name,ct);}}
    internal override void DeleteSnapshot(FileStream file,string name,ArchiveFileObservation expected,CancellationToken ct)
    {
        lock(Gate)
        {
            // Only a lease opened for a reviewed discard or created removable (a commit capture) removes; other cleanup keeps
            // (UnixEditDirectoryLease.Discard.cs). The removal reviews the complete content itself, under the Linux lease.
            if(discard){RemoveReviewedFile(file,name,expected,ct);return;}
            ReviewCore(ct); // No unlink is attempted; retaining an artifact does not need another full payload read.
            throw new IOException("The private commit snapshot is kept for native cleanup review: "+Path);
        }
    }
    internal override void RemoveEmpty(CancellationToken ct)
    {
        lock(Gate)
        {
            if(discard){RemoveReviewedFolder(ct);return;}
            RequireNoChildren();ReviewCore(ct);throw new IOException("The private edit directory is kept for cleanup review: "+Path);
        }
    }
    internal override string Identify(SafeFileHandle file)
    {UnixArchiveNative.RequirePrivate(file,user);return UnixFiles.Stat(file)!.Value.Identity.ToString();}
    internal override void PublishNewFile(FileStream file,string source,string target,EditFileVersion expected,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(source);Leaf(target);ReviewCore(ct);
            if(!isPrivate||source==target||Inspect(file,ct)!=expected)throw new IOException("The new edit record does not match its owned artifact.");
            using(var named=UnixArchiveNative.Child(directory,source))
                if(UnixFiles.Stat(named)?.Identity.ToString()!=expected.Identity)throw new IOException("The edit record's source name changed.");
            file.Flush(true);UnixArchiveNative.SyncFile(file.SafeFileHandle);ct.ThrowIfCancellationRequested();
            UnixArchiveNative.PublishEditFile(directory,source,target);
            // The exclusive rename may already have happened. An uncertain post-review retains both names; no pathname rollback/deletion.
            ReviewCore(CancellationToken.None);
            using(var named=UnixArchiveNative.Child(directory,target))
                if(UnixFiles.Stat(named)?.Identity.ToString()!=expected.Identity)throw new IOException("The published edit record needs review.");
            if(Inspect(file,CancellationToken.None)!=expected)throw new IOException("The published edit record bytes changed.");
            UnixArchiveNative.SyncDirectory(directory);
        }
    }
    private FileStream OpenFileCore(string name,string? expected,bool create,CancellationToken ct)
    {
        lock(Gate)
        {
            RequireOpen();Leaf(name);ReviewCore(ct);
            if(!isPrivate) throw new UnauthorizedAccessException("Working bytes need a private edit directory.");
            var file=create?UnixArchiveNative.CreateEditFile(directory,name):UnixArchiveNative.Child(directory,name);
            try
            {
                UnixArchiveNative.RequirePrivate(file,user);
                if(expected is not null&&UnixFiles.Stat(file)?.Identity.ToString()!=expected) throw UnixArchiveNative.Unavailable();
                ReviewCore(ct);return new(file,create?FileAccess.ReadWrite:FileAccess.Read);
            }
            catch { file.Dispose();throw; }
        }
    }
    protected override void CloseCore() { try { directory.Dispose(); } finally { parentOwner?.Dispose(); } }
}
