using System.ComponentModel;
using System.Security.Cryptography;
using FileCat.Core.Edit;
using Microsoft.Win32.SafeHandles;
using static FileCat.Platform.Windows.Elevation.NtFile;

namespace FileCat.Platform.Windows.Archives;

/// <summary>
/// Retires an adopted old edit folder on NTFS object by object, through handles opened relative to the held parent and
/// bound to the identities recorded at adoption: the old working file, its "files" folder, the old record, then the
/// session folder. A file is opened sharing only reads (an editor that has it open for writing makes that open fail, as
/// a discard refuses) and must still hold its recorded bytes, so an old working copy changed since the copy is kept and no
/// change is lost; a folder is removed only once empty. Missing objects are skipped, so an interrupted retirement
/// resumes. A probe opens and checks everything the same way without removing anything.
/// </summary>
internal static class WindowsLegacyEditRemoval
{
    private const int SharingViolation = unchecked((int)0xC0000043);

    /// <summary>Null when nothing of the old folder is left (or, for a probe, when everything can be removed).</summary>
    internal static string? Remove(EditLegacyRemoval request,bool probe,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        // A native identity or listing failure is a reason to keep everything, reported like any other.
        try{return RemoveCore(request,probe,ct);}
        catch(Win32Exception error){return Kept(Path.Combine(request.Root,request.SessionId),error.Message);}
    }

    private static string? RemoveCore(EditLegacyRemoval request,bool probe,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var link=request.Link;string folder=Path.Combine(request.Root,request.SessionId);
        using var root=WindowsArchiveDirectory.OpenLegacyReadOnly(Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Root)));
        lock(root.Gate)
        {
            if(root.Identity.ToString()!=link.RootIdentity)return Kept(folder,"the old edit root was replaced");
            if(OpenHeld(root.Handle,request.SessionId,true,out var session) is { } sessionStatus)
                return sessionStatus is NameNotFound or PathNotFound?null:Kept(folder,Describe(sessionStatus,"the old edit folder"));
            using(session)
            {
                if(Identity(session)!=link.SessionIdentity)return Kept(folder,"another folder now has the old edit's name");
                if(OpenHeld(session,"files",true,out var files) is { } filesStatus)
                {
                    if(filesStatus is not (NameNotFound or PathNotFound))return Kept(folder,Describe(filesStatus,"the old working folder"));
                }
                else
                {
                    using(files)
                    {
                        if(Identity(files)!=link.FilesIdentity)return Kept(folder,"the old working folder was replaced");
                        var names=Names(files,ct);
                        if(names.Count>1||names.Count==1&&!string.Equals(names[0],request.WorkingLeaf,StringComparison.OrdinalIgnoreCase))
                            return Kept(folder,"the old working folder holds other files");
                        if(names.Count==1)
                        {
                            if(OpenHeld(files,request.WorkingLeaf!,false,out var working) is { } workingStatus)
                                return Kept(folder,Describe(workingStatus,"the old working copy"));
                            using(working)
                            {
                                if(Review(working,link.WorkingIdentity,link.WorkingLength,link.WorkingSha256,ct) is { } changed)
                                    return Kept(folder,"the old working copy "+changed+"; it is kept so no change is lost");
                                if(!probe&&DeleteHeld(working) is { } failed)return Kept(folder,"the old working copy could not be removed: "+failed);
                            }
                        }
                        if(!probe)
                        {
                            ct.ThrowIfCancellationRequested();
                            if(Names(files,ct).Count!=0)return Kept(folder,"the old working folder holds other files");
                            if(DeleteHeld(files) is { } failed)return Kept(folder,"the old working folder could not be removed: "+failed);
                        }
                    }
                }
                if(OpenHeld(session,"session.json",false,out var record) is { } recordStatus)
                {
                    if(recordStatus is not (NameNotFound or PathNotFound))return Kept(folder,Describe(recordStatus,"the old edit record"));
                }
                else
                {
                    using(record)
                    {
                        if(Review(record,link.RecordIdentity,null,link.RecordSha256,ct) is { } changed)return Kept(folder,"the old edit record "+changed);
                        if(!probe&&DeleteHeld(record) is { } failed)return Kept(folder,"the old edit record could not be removed: "+failed);
                    }
                }
                var rest=Names(session,ct);
                if(probe?rest.Any(name=>!name.Equals("files",StringComparison.OrdinalIgnoreCase)&&!name.Equals("session.json",StringComparison.OrdinalIgnoreCase)):
                    rest.Count!=0)return Kept(folder,"the old edit folder holds other files");
                if(probe)return null;
                ct.ThrowIfCancellationRequested();
                if(DeleteHeld(session) is { } removal)return Kept(folder,"the empty old edit folder could not be removed: "+removal);
                return null;
            }
        }
    }

    // Relative to the held parent, never following a reparse point; a file shares only reads, so a writer refuses the open.
    private static int? OpenHeld(SafeFileHandle parent,string name,bool directory,out SafeFileHandle handle)
    {
        int status=TryOpen(parent,name,GenericRead|Delete|0x20000 /* READ_CONTROL */|Synchronize,directory?ShareRead|ShareWrite:ShareRead,Open,
            (directory?DirectoryFile:NonDirectoryFile)|OpenReparsePoint,out handle);
        return status<0?status:null;
    }
    private static string Describe(int status,string what)=>status==SharingViolation
        ?"another program has "+what+" open; close it, then retire the folder again"
        :what+" could not be opened (NTSTATUS 0x"+status.ToString("X8",System.Globalization.CultureInfo.InvariantCulture)+")";
    private static string Identity(SafeFileHandle handle)=>ProtectedHexFile.Identity(handle).ToString();
    private static List<string> Names(SafeFileHandle directory,CancellationToken ct)=>[..List(directory,64,ct).Select(entry=>entry.Name)];

    private static string? Review(SafeFileHandle file,string identity,long? length,string sha256,CancellationToken ct)
    {
        if(Identity(file)!=identity)return "was replaced";
        // A regular, non-reparse file with exactly one link, as at adoption: another name would keep sharing the bytes.
        try{WindowsArchiveNative.Regular(file,EditSessionStore.MaxMemberBytes,true,false);}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException){return "has another link or is no longer a regular file";}
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
    private static string? DeleteHeld(SafeFileHandle handle)
    {
        try{WindowsArchiveNative.DeleteExact(handle);return null;}
        catch(Win32Exception error){return error.Message;}
    }
    private static string Kept(string folder,string why)=>"The old edit folder was kept at "+folder+": "+why+".";
}
