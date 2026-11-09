using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    private const string CreationMarker="creation.json";
    private const string RevisionPrefix="session-state-";
    private static string RevisionName(long revision)=>RevisionPrefix+revision.ToString("D19",CultureInfo.InvariantCulture)+".json";

    private EditDirectoryLease OpenNativeRoot(CancellationToken ct,bool create)
    {
        if(nativeOpen is null)throw new NotSupportedException("Native edit storage is unavailable.");
        string path=nativeRoot;
        if(!create||Directory.Exists(path))
        {
            var root=nativeOpen(path,true,ct);
            if(root.Path==path)return root;root.Dispose();throw new IOException("The native edit root differs from the configured store.");
        }
        string parentPath=Path.GetDirectoryName(path)!;
        using var parent=nativeOpen(parentPath,false,ct);
        if(parent.Path!=parentPath)throw new IOException("The native edit parent differs from the configured store.");
        var created=parent.CreateDirectory(Path.GetFileName(path),ct);
        if(created.Path==path)return created;
        created.Dispose();throw new IOException("The created native edit root differs from the configured store.");
    }
    private EditSessionRecord CreateNativeSession(string fileName,
        Func<string,Func<long,FileStream>?,Func<EditDirectoryLease>,WorkingCopy> write,
        Func<string,WorkingCopy,EditSessionRecord> describe,CancellationToken ct,Action? reviewOldSource=null)
    {
        using var root=OpenNativeRoot(ct,true);using var allocation=EditStorageAdmission.Acquire(root,ct);
        string id=Guid.NewGuid().ToString("N");EditDirectoryLease? session=null,files=null;
        string fallback="file"+Path.GetExtension(fileName);
        string name=FileCat.Core.Jobs.SafeNames.Validate(fileName) is null?fileName:FileCat.Core.Jobs.SafeNames.Validate(fallback) is null?fallback:"file";
        string working=Path.Combine(root.Path,id,"files",name);string workingIdentity="";
        FileStream Open(long length)
        {
            ValidateLength(length);if(session is not null)throw new IOException("The working file was requested more than once.");
            var usage=MeasureNativeStorageUsage(root,storageLimits,ct);
            allocation.Require(usage,storageLimits,1,0,5,checked(length+2L*MaxRecordBytes),5*EditStorageAdmission.MetadataPerEntry,0,
                checked(length+2L*MaxRecordBytes),ct);
            session=root.CreateDirectory(id,ct);files=session.CreateDirectory("files",ct);
            byte[] marker=JsonSerializer.SerializeToUtf8Bytes(new{Id=id,Version=int.MaxValue,WorkingPath=working},Json);
            PublishRecordBytes(session,CreationMarker,marker,ct);allocation.Review(ct);
            var stream=files.CreateFile(name,ct);
            try{workingIdentity=files.Identify(stream.SafeFileHandle);return stream;}catch{stream.Dispose();throw;}
        }
        try
        {
            var copy=write(working,Open,()=>files??throw new IOException("The native working directory is unavailable."));
            ct.ThrowIfCancellationRequested();
            if(files is null||session is null)throw new IOException("The native working file was not created.");
            reviewOldSource?.Invoke();
            allocation.Review(ct);files.Review(ct);session.Review(ct);root.Review(ct);
            var record=describe(working,copy)with{Id=id,Version=EditSessionRecord.NativeStorageVersion,CreatedUtc=DateTime.UtcNow,StorageRootIdentity=root.Identity,
                SessionDirectoryIdentity=session.Identity,WorkingDirectoryIdentity=files.Identity,WorkingIdentity=workingIdentity};
            if(!record.IsSupported&&!record.IsPendingAdoption)throw new IOException("The created edit record is incomplete; its private working folder is retained.");
            ct.ThrowIfCancellationRequested();byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(record,Json);
            PublishRecordBytes(session,RecordFile,bytes,ct);allocation.Review(ct);return record with{StoredRecordSha256=Digest(bytes)};
        }
        finally{try{files?.Dispose();}finally{session?.Dispose();}}
    }
    private static string Digest(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    private static void PublishRecordBytes(EditDirectoryLease session,string name,byte[] bytes,CancellationToken ct)
    {using var record=CreateRecordBytes(session,name,bytes,ct);}
    internal static EditPublishedRecord CreateRecordBytes(EditDirectoryLease session,string name,byte[] bytes,CancellationToken ct)
    {
        if(bytes.Length>MaxRecordBytes)throw new IOException("The edit record exceeds its metadata budget.");
        string temporary=".record-"+Guid.NewGuid().ToString("N")+".tmp";
        var stream=session.CreateFile(temporary,ct);
        try
        {
            stream.Write(bytes);stream.Flush(true);
            var expected=new EditFileVersion(session.Identify(stream.SafeFileHandle),bytes.Length,Digest(bytes));
            session.PublishNewFile(stream,temporary,name,expected,ct);
            return new(session,stream,session.ObserveSnapshot(stream,name,ct,MaxRecordBytes));
        }
        catch{stream.Dispose();throw;}
        // Failure retains uncertain artifacts. Neither a GUID pathname nor a prior identity observation authorizes deletion.
    }
    private static byte[] ReadRecordBytes(EditDirectoryLease session,string name,CancellationToken ct)
    {
        using var stream=session.OpenFile(name,null,ct);
        long length=stream.Length;if(length>MaxRecordBytes)throw new IOException("The edit record exceeds its metadata budget.");
        byte[] bytes=new byte[checked((int)length)];stream.ReadExactly(bytes);
        if(stream.ReadByte()!=-1)throw new IOException("The edit record changed during its bounded read.");
        ct.ThrowIfCancellationRequested();session.Review(ct);return bytes;
    }
    private EditSessionRecord ReadStoredRecord(byte[] bytes,string directory)
    {
        var record=ParseRecord(bytes,directory,allowPendingAdoption:true);
        if(!record.IsSupported&&!record.IsPendingAdoption)return record;
        if(record.StorageRootIdentity.Length==0)return record;
        record=record with{StoredRecordSha256=Digest(bytes)};
        if(nativeOpen is null)return UnsupportedSession(directory,"This edit needs native storage support. Its working folder is kept.");
        using var root=OpenNativeRoot(default,false);
        using var session=root.OpenDirectory(record.Id,record.SessionDirectoryIdentity,default);
        RequireStorage(record,root,session);
        if(!ReadRecordBytes(session,RecordFile,default).AsSpan().SequenceEqual(bytes))
            throw new IOException("The initial edit record changed while its native namespace was reviewed.");
        var latest=ReadNativeLatest(record,bytes,root,session);
        return latest.IsPendingAdoption?UnsupportedSession(directory,"This copied old edit is awaiting durable adoption review. Its files are kept."):latest;
    }
    private static void RequireStorage(EditSessionRecord record,EditDirectoryLease root,EditDirectoryLease session)
    {
        if(string.IsNullOrEmpty(record.StorageRootIdentity)||string.IsNullOrEmpty(record.SessionDirectoryIdentity)||
            string.IsNullOrEmpty(record.WorkingDirectoryIdentity)||root.Identity!=record.StorageRootIdentity||session.Identity!=record.SessionDirectoryIdentity)
            throw new IOException("The edit namespace no longer matches its saved native identities.");
        using var files=session.OpenDirectory("files",record.WorkingDirectoryIdentity,default);
        if(Path.GetFullPath(record.WorkingPath)!=Path.Combine(files.Path,Path.GetFileName(record.WorkingPath)))
            throw new IOException("The edit working path is outside its owned namespace.");
    }
    private static bool TryRevision(string name,out long revision)
    {
        revision=0;return name.Length==RevisionPrefix.Length+19+5&&name.StartsWith(RevisionPrefix,StringComparison.Ordinal)&&name.EndsWith(".json",StringComparison.Ordinal)&&
            long.TryParse(name.AsSpan(RevisionPrefix.Length,19),NumberStyles.None,CultureInfo.InvariantCulture,out revision)&&revision>0;
    }
    private static EditSessionRecord ReadNativeLatest(EditSessionRecord anchor,byte[] anchorBytes,EditDirectoryLease root,EditDirectoryLease session,
        CancellationToken ct=default,Dictionary<string,EditSessionRecord>? history=null)
    {
        long revision=0;
        foreach(string name in session.Names(ct))
        {
            if(!name.StartsWith(RevisionPrefix,StringComparison.Ordinal))continue;
            if(!TryRevision(name,out long value))return UnsupportedSession(session.Path,"This edit has an unknown state-record name. Its working folder is kept.");
            revision=Math.Max(revision,value);
        }
        if(anchor.StorageRevision!=0||anchor.PreviousRecordSha256.Length!=0||
            anchor.LegacyAdoption is not null&&anchor.LegacyAdoptionReady)
            return UnsupportedSession(session.Path,"This edit's initial state record has an invalid revision. Its working folder is kept.");
        // Review the complete chain, with an aggregate budget. A valid tail cannot hide a damaged ancestor.
        if(revision>4096)return UnsupportedSession(session.Path,"This edit's revision history exceeds the review budget. Its working folder is kept.");
        var latest=anchor;bool wasReady=anchor.LegacyAdoptionReady;
        string previousHash=Digest(anchorBytes);long reviewedBytes=anchorBytes.Length;
        history?.Add(previousHash,anchor with{StoredRecordSha256=previousHash});
        for(long current=1;current<=revision;current++)
        {
            ct.ThrowIfCancellationRequested();byte[] bytes=ReadRecordBytes(session,RevisionName(current),ct);reviewedBytes+=bytes.Length;
            if(reviewedBytes>32L*1024*1024)return UnsupportedSession(session.Path,"This edit's revision history exceeds the review budget. Its working folder is kept.");
            latest=ParseRecord(bytes,session.Path,allowPendingAdoption:true);
            if(!latest.IsSupported&&!latest.IsPendingAdoption)return latest;
            if(latest.StorageRevision!=current||latest.PreviousRecordSha256!=previousHash||
                latest.Id!=anchor.Id||latest.Kind!=anchor.Kind||latest.WorkingPath!=anchor.WorkingPath||latest.LegacyAdoption!=anchor.LegacyAdoption||
                wasReady&&!latest.LegacyAdoptionReady||latest.StorageRootIdentity!=anchor.StorageRootIdentity||
                latest.SessionDirectoryIdentity!=anchor.SessionDirectoryIdentity||latest.WorkingDirectoryIdentity!=anchor.WorkingDirectoryIdentity)
                return UnsupportedSession(session.Path,"This edit's state record has a broken revision link. Its working folder is kept.");
            wasReady=latest.LegacyAdoptionReady;previousHash=Digest(bytes);
            history?.Add(previousHash,latest with{StoredRecordSha256=previousHash});
        }
        RequireStorage(latest,root,session);return latest with{StoredRecordSha256=previousHash};
    }
    private EditSessionRecord SaveNativeRevision(EditSessionRecord record)
    {
        using var root=OpenNativeRoot(default,false);using var allocation=EditStorageAdmission.Acquire(root,default);
        using var session=root.OpenDirectory(record.Id,record.SessionDirectoryIdentity,default);
        RequireStorage(record,root,session);byte[] anchorBytes=ReadRecordBytes(session,RecordFile,default);
        var anchor=ParseRecord(anchorBytes,session.Path,allowPendingAdoption:true);
        if(!anchor.IsSupported&&!anchor.IsPendingAdoption)throw new IOException("The initial edit record is no longer supported; the working copy is kept.");
        var current=ReadNativeLatest(anchor,anchorBytes,root,session);
        if(!current.IsSupported&&!current.IsPendingAdoption||string.IsNullOrEmpty(record.StoredRecordSha256)||
            current.StoredRecordSha256!=record.StoredRecordSha256||current.StorageRevision!=record.StorageRevision||
            current.LegacyAdoption!=record.LegacyAdoption||current.LegacyAdoptionReady&&!record.LegacyAdoptionReady)
            throw new IOException("This edit record changed since it was reviewed. Reload it before saving a new state.");
        var next=record with{StorageRevision=checked(current.StorageRevision+1),PreviousRecordSha256=current.StoredRecordSha256};
        byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(next,Json);
        var usage=MeasureNativeStorageUsage(root,storageLimits,default);
        allocation.Require(usage,storageLimits,0,0,1,bytes.Length,EditStorageAdmission.MetadataPerEntry,0,bytes.Length,default);
        PublishRecordBytes(session,RevisionName(next.StorageRevision),bytes,default);
        allocation.Review(default);
        return next with{StoredRecordSha256=Digest(bytes)};
    }
}
