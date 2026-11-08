using System.Text.Json;
using FileCat.Core.Archives;
using Microsoft.Win32.SafeHandles;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    private EditCommitCopy PrepareNativeSnapshot(EditSessionRecord record,CancellationToken ct,Action? check)
    {
        void Check(){ct.ThrowIfCancellationRequested();check?.Invoke();}
        Check();using var admission=EditSnapshotAdmission.Acquire(ct);using var root=OpenNativeRoot(ct,false);
        using var session=root.OpenDirectory(record.Id,record.SessionDirectoryIdentity,ct);RequireStorage(record,root,session);
        using var source=OpenNativeWorking(record,ct);
        var revision=source.GetRevision()??throw new IOException("The working copy has no native version.");
        admission.ReserveBytes(revision.Length);
        var origin=source.ReadOrigin();Check();
        var capture=session.CreateDirectory("commit-"+Guid.NewGuid().ToString("N"),ct);
        EditDirectoryLease? files=null;FileStream? output=null;var records=new List<EditPublishedRecord>(2);
        try
        {
            files=capture.CreateDirectory("files",ct);
            string leaf=Path.GetFileName(record.WorkingPath),path=Path.Combine(files.Path,leaf);
            records.Add(CreateRecordBytes(capture,"capture.json",JsonSerializer.SerializeToUtf8Bytes(new{Version=1,Session=record.Id,
                SessionRecord=record.StoredRecordSha256,Directory=capture.Identity,Files=files.Identity,Leaf=leaf},Json),ct));
            output=files.CreateFile(leaf,ct);
            FileStream Borrow()=>new(new SafeFileHandle(output.SafeFileHandle.DangerousGetHandle(),ownsHandle:false),FileAccess.ReadWrite);
            var bytes=WriteWorkingCopy(source,path,ct,revision,check:Check,outputFactory:Borrow);
            output.Flush(true);files.CopyOrigin(output,leaf,origin,ct);
            File.SetLastWriteTimeUtc(output.SafeFileHandle,new DateTime(revision.ModifiedTicks,DateTimeKind.Utc));output.Flush(true);
            var observed=files.ObserveSnapshot(output,leaf,ct);
            if(observed.Version.Sha256!=bytes.Sha256||observed.Version.Length!=bytes.Length||source.GetRevision()!=revision||source.ReadOrigin().Digest!=origin.Digest)
                throw new IOException("The working file or private snapshot changed during complete capture.");
            Check();source.Dispose();
            records.Add(CreateRecordBytes(capture,"captured.json",JsonSerializer.SerializeToUtf8Bytes(new{Version=1,Session=record.Id,
                SessionRecord=record.StoredRecordSha256,Directory=capture.Identity,Files=files.Identity,Leaf=leaf,
                Identity=observed.Version.Identity,Length=bytes.Length,Sha256=bytes.Sha256,Metadata=observed.MetadataDigest,Origin=origin.Digest},Json),ct));
            Check();var snapshot=new NativeEditSnapshot(capture,files,output,observed,records.ToArray(),record.Id,record.StoredRecordSha256,admission);
            var result=new EditCommitCopy(snapshot,bytes.Crc);admission.Transfer();return result;
        }
        catch
        {
            try{output?.Dispose();}finally{try{foreach(var item in records)item.Dispose();}finally{try{files?.Dispose();}finally{capture.Dispose();}}}
            throw; // All uncertain artifacts remain in the recorded session namespace.
        }
    }
}
