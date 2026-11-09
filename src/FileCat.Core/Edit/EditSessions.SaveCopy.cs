using FileCat.Core.Archives;

namespace FileCat.Core.Edit;

public sealed partial class EditSessionStore
{
    /// <summary>Explicit test composition only until the complete saved-copy workflow is qualified.</summary>
    internal EditSaveCopyPublicationResult SaveNewNativeCopy(EditSessionRecord record,EditCommitCopy copy,string path,
        CancellationToken ct=default,Action? check=null)
    {
        RefuseElevatedSession();ct.ThrowIfCancellationRequested();check?.Invoke();
        if(nativeCopyOpen is null||copy.Native is not { } source)throw new NotSupportedException("Native saved-copy destination support is unavailable. The edit is kept.");
        source.RequireSession(record);path=Path.GetFullPath(path);string leaf=Path.GetFileName(path);EditSaveCopyNames.Validate(leaf);
        string parent=Path.GetDirectoryName(path)!;EditSaveCopyIntent.ValidateDirectory(parent);
        using var destination=nativeCopyOpen(parent,ct);destination.Review(ct);
        if(destination.Path!=parent)throw new IOException("The native saved-copy directory differs from the selected destination.");
        Guid nonce=Guid.NewGuid();
        var stage=destination.Reserve(nonce,ArchiveArtifactKind.Stage,source.Expected.Version.Length,ct);
        var prepared=destination.Reserve(nonce,ArchiveArtifactKind.Manifest,EditSaveCopyRecord.MaximumBytes,ct);
        var outcome=destination.Reserve(nonce,ArchiveArtifactKind.Manifest,EditSaveCopyRecord.MaximumBytes,ct);
        foreach(var reservation in new[]{stage,prepared,outcome})
            if(reservation.Nonce!=nonce||reservation.ParentIdentity!=destination.Identity||Path.GetDirectoryName(reservation.Path)!=destination.Path)
                throw new IOException("The saved-copy reservations belong to another native destination.");
        var observed=source.Review(ct);var modified=observed.MemberModified??throw new IOException("The saved-copy time is unavailable.");
        var intent=new EditSaveCopyIntent(1,nonce,record.Id,record.StoredRecordSha256,source.CaptureIdentity,
            observed.Version.Identity,observed.Version.Length,observed.Version.Sha256,observed.MetadataDigest,observed.Origin.Digest,
            modified.Seconds,modified.Nanoseconds,destination.Path,destination.Identity,leaf,stage.LeafName,prepared.LeafName,outcome.LeafName);
        source.AttachSavedCopy(intent,ct);check?.Invoke();ct.ThrowIfCancellationRequested();
        using var output=destination.Create(stage,ct);
        if(output is not IEditSaveCopyStage copyStage)throw new IOException("The native destination cannot prepare an ordinary saved file.");
        var ready=EditSaveCopyPreparation.Prepare(source,copyStage,stage,destination.Space,ct,check);
        return EditSaveCopyPublication.Publish(ready,leaf,destination.Space,(operation,sequence)=>
        {
            if(operation!=nonce||sequence is not(1 or 2))throw new IOException("The saved-copy receipt belongs to another operation.");
            return sequence==1?prepared:outcome;
        },reservation=>destination.Create(reservation,CancellationToken.None),ct,check,nonce);
    }
}
