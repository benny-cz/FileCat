using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Archives.Unix;
using FileCat.Core.FileSystem;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Edit;

/// <summary>What retiring one adopted old edit folder may remove: the recorded objects of that folder, nothing else.</summary>
/// <param name="Root">The configured legacy edit root.</param>
/// <param name="SessionId">The old session folder's name.</param>
/// <param name="WorkingLeaf">The old working file's name, or null once the old record is gone (any file left is kept).</param>
/// <param name="Link">The identities and digests recorded when the copy was adopted.</param>
internal sealed record EditLegacyRemoval(string Root,string SessionId,string? WorkingLeaf,EditLegacyAdoption Link);

/// <summary>
/// Retires an adopted old Unix edit folder object by object (UNIX_PRIVATE_CLEANUP_DECISION.md). In order: the old working
/// file, its "files" folder, the old record and the 0700 session folder. A file is removed through
/// <see cref="UnixExactRemoval"/>: on Linux under a write lease taken before its content is reviewed, and only while its
/// name binds the identity recorded at adoption and it holds the recorded bytes (an old working copy changed since the
/// copy is kept, so no change is lost). A folder goes only once empty, the session folder only from a root no other user
/// can change. Only a child that is not there (ENOENT) counts as already removed, so an interrupted retirement resumes and
/// any other failure keeps everything. A probe checks all of that without removing anything.
/// </summary>
internal static class UnixLegacyEditRemoval
{
    /// <summary>Null when nothing of the old folder is left (or, for a probe, when everything can be removed).</summary>
    internal static string? Remove(EditLegacyRemoval request,bool probe,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);ct.ThrowIfCancellationRequested();
        if(OperatingSystem.IsWindows())throw new PlatformNotSupportedException("Unix legacy retirement needs a Unix system.");
        var link=request.Link;uint user=UnixArchiveNative.RequireOrdinaryIdentity();
        string folder=Path.Combine(request.Root,request.SessionId);
        using var root=UnixArchiveNative.PinDirectory(Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Root)));
        if(Identity(root)!=link.RootIdentity)return Kept(folder,"the old edit root was replaced");
        using var session=UnixExactRemoval.TryOpen(root,request.SessionId,true);
        if(session is null)return null;
        if(Identity(session)!=link.SessionIdentity)return Kept(folder,"another folder now has the old edit's name");
        try{UnixArchiveNative.RequireEditDirectory(session,user);}
        catch(ArchiveWriteException){return Kept(folder,"the old edit folder is not private to you");}

        using(var files=UnixExactRemoval.TryOpen(session,"files",true))
        {
            if(files is not null)
            {
                if(Identity(files)!=link.FilesIdentity||UnixFiles.Stat(files) is not { IsDirectory:true,Owner:var owner }||owner!=user)
                    return Kept(folder,"the old working folder was replaced");
                var names=Names(files,ct);
                if(names.Count>1||names.Count==1&&names[0]!=request.WorkingLeaf)return Kept(folder,"the old working folder holds other files");
                if(names.Count==1&&RemoveFile(files,request.WorkingLeaf!,user,link.WorkingIdentity,link.WorkingLength,link.WorkingSha256,
                    "the old working copy",probe,ct) is { } working)
                    return Kept(folder,working);
                if(!probe)
                {
                    ct.ThrowIfCancellationRequested();
                    if(Names(files,ct).Count!=0)return Kept(folder,"the old working folder holds other files");
                    if(UnixExactRemoval.RemoveFolder(session,"files",link.FilesIdentity,"the old working folder") is { } kept)return Kept(folder,kept);
                }
            }
        }
        // The old record's exact length is not part of the evidence; its digest is, within the record budget.
        if(RemoveFile(session,"session.json",user,link.RecordIdentity,null,link.RecordSha256,"the old edit record",probe,ct) is { } record)
            return Kept(folder,record);
        var rest=Names(session,ct);
        if(probe?rest.Any(name=>name is not ("files" or "session.json")):rest.Count!=0)return Kept(folder,"the old edit folder holds other files");
        try{UnixArchiveNative.RequireCreationDirectory(root,user);}
        catch(ArchiveWriteException){return Kept(folder,"the folder that holds it can be changed by other users, so its name cannot be bound for removal");}
        if(probe)return null;
        ct.ThrowIfCancellationRequested();
        return UnixExactRemoval.RemoveFolder(root,request.SessionId,link.SessionIdentity,"the empty old edit folder") is { } left?Kept(folder,left):null;
    }

    // A recorded file: absent is done; otherwise, under the Linux lease, the recorded object with its recorded bytes, then
    // (unless probing) its exact removal. Null when removed, absent or (probe) removable.
    private static string? RemoveFile(SafeFileHandle directory,string name,uint user,string identity,long? length,string sha256,string what,
        bool probe,CancellationToken ct)
    {
        using var file=UnixExactRemoval.TryOpen(directory,name,false);
        if(file is null)return null;
        if(UnixExactRemoval.TakeLease(file,what) is { } refused)return refused;
        try
        {
            if(Review(file,user,identity,length,sha256,ct) is { } changed)
                return what+" "+changed+(length is null?"":"; it is kept so no change is lost");
            return probe?null:UnixExactRemoval.Unlink(directory,name,file,identity,what,ct);
        }
        finally{UnixExactRemoval.ReleaseLease(file);}
    }

    // The held object is the recorded regular file of this user, with one link and the recorded bytes.
    private static string? Review(SafeFileHandle file,uint user,string identity,long? length,string sha256,CancellationToken ct)
    {
        var facts=ArchiveUnixOuterMetadata.Capture(file,ct).Native;
        if(UnixFiles.Stat(file) is not { IsRegular:true } stat||stat.Owner!=user||stat.Identity.ToString()!=identity)return "was replaced";
        if(facts.LinkCount!=1)return "has another link";
        long size=RandomAccess.GetLength(file);
        if(length is { } expected&&size!=expected||size>EditSessionStore.MaxMemberBytes)return "changed after it was copied";
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer=new byte[1024*1024];long offset=0;
        try
        {
            while(offset<size)
            {
                ct.ThrowIfCancellationRequested();
                int read=RandomAccess.Read(file,buffer.AsSpan(0,(int)Math.Min(buffer.Length,size-offset)),offset);
                if(read<=0)return "changed after it was copied";
                hash.AppendData(buffer,0,read);offset+=read;
            }
            return RandomAccess.GetLength(file)==size&&Convert.ToHexString(hash.GetHashAndReset())==sha256?null:"changed after it was copied";
        }
        finally{CryptographicOperations.ZeroMemory(buffer);}
    }

    private static List<string> Names(SafeFileHandle directory,CancellationToken ct)
    {
        var names=new List<string>();
        UnixArchiveNative.EnumerateSelection(directory,entry=>{if(names.Count>=64)throw new IOException("The old edit folder holds too many entries.");names.Add(entry.Leaf);},ct);
        return names;
    }
    private static string Identity(SafeFileHandle handle)=>(UnixFiles.Stat(handle)?.Identity ?? throw new IOException("An old edit object is unavailable.")).ToString();
    private static string Kept(string folder,string why)=>"The old edit folder was kept at "+folder+": "+why+".";
}
