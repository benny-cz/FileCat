using System.Security.Cryptography;
using FileCat.Core.Archives;
using FileCat.Core.Jobs;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    /// <summary>
    /// Test-only bridge from a pinned old edit into a separate private v3 root. The old folder is never
    /// mutated. A pending anchor is kept as recovery evidence until a complete native copy and origin
    /// readback, another source review, and a linked ready revision have all been published.
    /// The application factory does not expose this path until retained-session routing is qualified.
    /// </summary>
    internal EditSessionRecord CopyLegacyForAdoption(ILegacyEditSource source,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(source);
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();
        if(nativeOpen is null||nativeRoot==Root)throw new NotSupportedException("Legacy adoption needs separate private native storage.");
        var comparison=OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
        if(!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(source.RootPath)),Root,comparison))
            throw new IOException("The old edit source belongs to a different configured root.");
        var old=source.Session;var working=source.Working;var origin=source.Origin;
        if(!old.IsSupported||old.Version is not (1 or EditSessionRecord.CurrentVersion)||
            old.StorageRootIdentity.Length!=0||working.Length is <0 or >MaxMemberBytes||
            origin.State==ArchiveOriginState.Unavailable)
            throw new IOException("Only a complete supported old edit can be copied for native adoption.");
        var relationship=new EditLegacyAdoption
        {
            SourceVersion=old.Version,SourceId=old.Id,RootIdentity=source.RootIdentity,
            SessionIdentity=source.SessionIdentity,FilesIdentity=source.FilesIdentity,
            RecordIdentity=source.RecordIdentity,RecordSha256=source.RecordSha256,
            WorkingIdentity=working.Identity,WorkingLength=working.Length,
            WorkingSha256=working.Sha256,OriginDigest=origin.Digest
        };
        if(!relationship.IsValid)throw new IOException("The old edit relationship exceeds its evidence bounds.");
        source.Review(ct);
        var pending=CreateNativeSession(old.DisplayName,(path,open,lease)=>
        {
            using var output=(open??throw new IOException("Native edit output is unavailable."))(working.Length);
            source.CopyTo(output,ct);output.Flush(true);
            var copy=InspectAdoptedBytes(output,working,ct);
            var files=lease();string leaf=Path.GetFileName(path);
            files.CopyOrigin(output,leaf,origin,ct);
            if(files.ReadOrigin(output,leaf,ct).Digest!=origin.Digest)
                throw new IOException("The adopted edit origin changed before record publication.");
            output.Flush(true);source.Review(ct);
            return copy;
        },(path,copy)=>old with
        {
            WorkingPath=path,LegacyAdoption=relationship,LegacyAdoptionReady=false,
            // The base/member evidence still describes the original remote/archive revision; the copied
            // working bytes may contain unsaved editor changes and must not replace that baseline.
            BaseSha256=old.BaseSha256
        },ct,()=>source.Review(ct));
        VerifyPendingCopy(pending,working,origin,ct);
        source.Review(ct);
        return SaveNativeRevision(pending with{LegacyAdoptionReady=true});
    }

    private static WorkingCopy InspectAdoptedBytes(FileStream output,EditFileVersion expected,CancellationToken ct)
    {
        if(output.Length!=expected.Length)throw new IOException("The adopted copy length differs from its held old source.");
        output.Position=0;using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer=new byte[1024*1024];long left=expected.Length;uint crc=0;
        try
        {
            while(left>0)
            {
                ct.ThrowIfCancellationRequested();int read=output.Read(buffer,0,(int)Math.Min(left,buffer.Length));
                if(read<=0)throw new IOException("The adopted native copy ended during full review.");
                hash.AppendData(buffer,0,read);crc=Crc32.Append(crc,buffer.AsSpan(0,read));left-=read;
            }
            if(output.ReadByte()!=-1||output.Length!=expected.Length)
                throw new IOException("The adopted native copy changed during full review.");
            string digest=Convert.ToHexString(hash.GetHashAndReset());
            if(digest!=expected.Sha256)throw new IOException("The adopted copy differs from the held old edit.");
            return new(digest,crc,expected.Length);
        }
        finally{CryptographicOperations.ZeroMemory(buffer);}
    }

    private void VerifyPendingCopy(EditSessionRecord pending,EditFileVersion expected,ArchiveOriginSnapshot origin,CancellationToken ct)
    {
        if(!pending.IsPendingAdoption||pending.LegacyAdoption is null||
            pending.LegacyAdoption.WorkingSha256!=expected.Sha256||pending.LegacyAdoption.OriginDigest!=origin.Digest)
            throw new IOException("The pending adoption record differs from its copied source.");
        using var root=OpenNativeRoot(ct,false);
        using var session=root.OpenDirectory(pending.Id,pending.SessionDirectoryIdentity,ct);
        RequireStorage(pending,root,session);
        using var files=session.OpenDirectory("files",pending.WorkingDirectoryIdentity,ct);
        string leaf=Path.GetFileName(pending.WorkingPath);
        using var file=files.OpenFile(leaf,pending.WorkingIdentity,ct);
        var copied=files.Inspect(file,ct);
        if(copied.Length!=expected.Length||copied.Sha256!=expected.Sha256||
            files.ReadOrigin(file,leaf,ct).Digest!=origin.Digest)
            throw new IOException("The pending native edit copy or origin differs from the held old source.");
    }
}
