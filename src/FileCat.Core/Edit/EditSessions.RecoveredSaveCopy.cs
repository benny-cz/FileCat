using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    /// <summary>
    /// Make a new ordinary file from one complete retained capture after restart. The captured file and every
    /// old record remain; a persisted intent precedes any destination artifact or public name claim.
    /// </summary>
    internal EditSaveCopyPublicationResult SaveRecoveredNativeCopy(EditSessionRecord record,string captureDirectory,string path,
        CancellationToken ct=default,Action? check=null)
    {
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();check?.Invoke();
        if(nativeOpen is null||nativeCopyOpen is null||!record.IsSupported||record.StorageRootIdentity.Length==0)
            throw new NotSupportedException("This edit cannot save a recovered native copy. All retained files are kept.");
        using var root=OpenNativeRoot(ct,false);
        if(root.Identity!=record.StorageRootIdentity)throw RecoveredChanged();
        using var allocation=EditStorageAdmission.Acquire(root,ct);
        using var session=root.OpenDirectory(record.Id,record.SessionDirectoryIdentity,ct);
        RequireStorage(record,root,session);
        byte[] anchorBytes=ReadRecordBytes(session,RecordFile,ct);var anchor=ParseRecord(anchorBytes,session.Path);
        if(!anchor.IsSupported)throw RecoveredChanged();
        var history=new Dictionary<string,EditSessionRecord>(StringComparer.Ordinal);
        var current=ReadNativeLatest(anchor,anchorBytes,root,session,ct,history);
        if(!current.IsSupported||current.StoredRecordSha256!=record.StoredRecordSha256||current.StorageRevision!=record.StorageRevision)
            throw RecoveredChanged();
        string selected=Path.GetFullPath(captureDirectory);
        string name=Path.GetFileName(Path.TrimEndingDirectorySeparator(selected));
        if(name.Length!=39||!name.StartsWith("commit-",StringComparison.Ordinal)||!Guid.TryParseExact(name.AsSpan(7),"N",out _)||
            selected!=Path.Combine(session.Path,name))throw RecoveredChanged();
        using var capture=session.OpenDirectory(name,null,ct);
        if(!capture.Names(ct).Order(StringComparer.Ordinal).SequenceEqual(new[]{"capture.json","captured.json","files"}))
            throw new IOException("This retained copy is incomplete or has a prior attempt. All files are kept.");
        using var header=new CaptureRecord(capture,"capture.json",ct);
        var start=ParseCaptureRecord(header.Bytes,false);
        if(!history.TryGetValue(start.SessionRecord,out var originating))throw RecoveredChanged();
        RequireCaptureBinding(start,capture,originating);
        using var files=capture.OpenDirectory("files",start.Files,ct);
        if(!files.Names(ct).SequenceEqual([start.Leaf]))throw RecoveredChanged();
        using var receipt=new CaptureRecord(capture,"captured.json",ct);
        var end=ParseCaptureRecord(receipt.Bytes,true);
        RequireCaptureBinding(end,capture,originating);
        if(start.Session!=end.Session||start.SessionRecord!=end.SessionRecord||start.Directory!=end.Directory||
            start.Files!=end.Files||start.Leaf!=end.Leaf)throw RecoveredChanged();
        using var payload=files.OpenFile(start.Leaf,end.Identity,ct);
        var observed=files.ObserveSnapshot(payload,start.Leaf,ct);
        if(observed.Version.Length!=end.Length||observed.Version.Sha256!=end.Sha256||
            observed.MetadataDigest!=end.Metadata||observed.Origin.Digest!=end.Origin)throw RecoveredChanged();
        void CheckRecord(CancellationToken token)
        {
            if(!ReadRecordBytes(session,RecordFile,token).AsSpan().SequenceEqual(anchorBytes)||
                ReadNativeLatest(anchor,anchorBytes,root,session,token).StoredRecordSha256!=current.StoredRecordSha256)
                throw RecoveredChanged();
        }
        var source=new RecoveredEditFile(capture,files,payload,header,receipt,observed,CheckRecord);
        source.Review(observed,ct);check?.Invoke();ct.ThrowIfCancellationRequested();
        path=Path.GetFullPath(path);string leaf=Path.GetFileName(path);EditSaveCopyNames.Validate(leaf);
        string parent=Path.GetDirectoryName(path)!;EditSaveCopyIntent.ValidateDirectory(parent);
        string storage=Path.TrimEndingDirectorySeparator(root.Path);
        if(string.Equals(parent,storage,StringComparison.OrdinalIgnoreCase)||
            parent.StartsWith(storage+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a saved-copy destination outside the private edit-session store.");
        using var destination=nativeCopyOpen(parent,ct);destination.Review(ct);
        if(destination.Path!=parent)throw RecoveredChanged();
        Guid nonce=Guid.NewGuid();
        var stage=destination.Reserve(nonce,ArchiveArtifactKind.Stage,observed.Version.Length,ct);
        var prepared=destination.Reserve(nonce,ArchiveArtifactKind.Manifest,EditSaveCopyRecord.MaximumBytes,ct);
        var outcome=destination.Reserve(nonce,ArchiveArtifactKind.Manifest,EditSaveCopyRecord.MaximumBytes,ct);
        foreach(var reservation in new[]{stage,prepared,outcome})
            if(reservation.Nonce!=nonce||reservation.ParentIdentity!=destination.Identity||
                Path.GetDirectoryName(reservation.Path)!=destination.Path)throw RecoveredChanged();
        var modified=observed.MemberModified??throw RecoveredChanged();
        var intent=new EditSaveCopyIntent(1,nonce,originating.Id,originating.StoredRecordSha256,capture.Identity,
            observed.Version.Identity,observed.Version.Length,observed.Version.Sha256,observed.MetadataDigest,observed.Origin.Digest,
            modified.Seconds,modified.Nanoseconds,destination.Path,destination.Identity,leaf,stage.LeafName,prepared.LeafName,outcome.LeafName);
        intent.RequireSource(originating,capture.Identity,observed);
        byte[] intentBytes=intent.Encode();var usage=MeasureNativeStorageUsage(root,storageLimits,ct);
        allocation.Require(usage,storageLimits,0,0,2,intentBytes.Length,2*EditStorageAdmission.MetadataPerEntry,0,intentBytes.Length,ct);
        source.Review(observed,ct);check?.Invoke();ct.ThrowIfCancellationRequested();
        using var link=CreateRecordBytes(capture,EditSaveCopyIntent.FileName,intentBytes,ct);
        source.Link(link);
        source.Review(observed,ct);allocation.Review(ct);check?.Invoke();ct.ThrowIfCancellationRequested();
        using var output=destination.Create(stage,ct);
        if(output is not IEditSaveCopyStage copyStage)throw RecoveredChanged();
        var ready=EditSaveCopyPreparation.Prepare(source,copyStage,stage,destination.Space,ct,check);
        return EditSaveCopyPublication.Publish(ready,leaf,destination.Space,(operation,sequence)=>
        {
            if(operation!=nonce||sequence is not(1 or 2))throw RecoveredChanged();
            return sequence==1?prepared:outcome;
        },reservation=>destination.Create(reservation,CancellationToken.None),ct,check,nonce);
    }

    private sealed class RecoveredEditFile(EditDirectoryLease capture,EditDirectoryLease files,FileStream payload,
        CaptureRecord header,CaptureRecord receipt,ArchiveFileObservation expected,Action<CancellationToken> checkRecord):IArchiveRetainedFile
    {
        private EditPublishedRecord? link;
        public string ObjectIdentity=>expected.Version.Identity;
        public ArchiveFileObservation Observe(CancellationToken ct){Review(expected,ct);return expected;}
        public void Review(ArchiveFileObservation reviewed,CancellationToken ct)
        {
            if(!expected.Matches(reviewed))throw RecoveredChanged();
            checkRecord(ct);header.Review(ct);receipt.Review(ct);
            string[] names=link is null?["capture.json","captured.json","files"]:
                ["capture.json","captured.json","files",EditSaveCopyIntent.FileName];
            if(!capture.Names(ct).Order(StringComparer.Ordinal).SequenceEqual(names.Order(StringComparer.Ordinal))||
                !files.Names(ct).SequenceEqual([expected.LeafName])||
                !files.ObserveSnapshot(payload,expected.LeafName,ct).Matches(expected))throw RecoveredChanged();
            link?.Review(ct);capture.Review(ct);
        }
        public Stream OpenReadView(ArchiveFileObservation reviewed,CancellationToken ct)
        {
            Review(reviewed,ct);
            return new ArchiveRetainedReadStream(expected.Version.Length,
                (offset,buffer)=>RandomAccess.Read(payload.SafeFileHandle,buffer,offset),
                ()=>!payload.SafeFileHandle.IsClosed);
        }
        internal void Link(EditPublishedRecord record)
        {if(link is not null)throw RecoveredChanged();link=record;}
    }

    private static IOException RecoveredChanged()=>new("The retained edit capture or its native ownership changed. All files are kept.");
}
