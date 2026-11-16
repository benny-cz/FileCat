using FileCat.Core.Archives;
using FileCat.Core.Edit;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsArchiveFile:IEditSaveCopyPublicationStage,IEditSaveCopyReceiptStage,IEditSaveCopyInspection
{
    ArchiveFileObservation IEditSaveCopyInspection.InspectSavedCopy(CancellationToken ct)
    {
        lock(parent.Gate)
        {
            var observed=ObserveRetained(ct);WindowsArchiveNative.RequireSnapshotStreams(handle,observed.Version.Length,observed.Origin);
            ((IArchiveRetainedFile)this).Review(observed,ct);return observed;
        }
    }
    private sealed class SaveCopyReview(WindowsArchiveFile issuer,ArchiveFileObservation stage,string leaf,WindowsArchiveOuterMetadata metadata)
        :EditSaveCopyNameReview(issuer,stage,leaf)
    {internal WindowsArchiveOuterMetadata Metadata { get; }=metadata;}
    private SaveCopyReview? saveCopyReview;
    private bool saveCopyClaimAttempted;
    void IEditSaveCopyReceiptStage.FlushReceipt(ArchiveFileObservation expected,CancellationToken ct)
    {
        lock(parent.Gate)
        {
            EnsureLive();ct.ThrowIfCancellationRequested();
            if(!artifact||!createdWritable||createdKind!=WindowsArchiveArtifactKind.Manifest||!evidenceClaimed||rebuildClaimed||writable||namespaceChanged)
                throw SaveCopyChanged();
            ((IArchiveRetainedFile)this).Review(expected,ct);parent.CheckPrivate(handle);WindowsArchiveNative.Flush(handle);
            parent.VerifyNamespace();parent.RequireName(KnownName,Identity);((IArchiveRetainedFile)this).Review(expected,ct);
        }
    }
    ArchiveFileObservation IEditSaveCopyStage.ApplyEditMetadata(ArchiveFileObservation copied,ArchiveFileObservation source,CancellationToken ct)
    {
        lock(parent.Gate)
        {
            EnsureLive();ct.ThrowIfCancellationRequested();
            if(!artifact||!createdWritable||createdKind!=WindowsArchiveArtifactKind.Stage||!rebuildClaimed||evidenceClaimed||writable||namespaceChanged||!mayRename||
                source.MemberModified is not {FitsTicks:true} modified)
                throw new IOException("Saved-copy metadata requires this creator-owned sealed private stage and an exact timestamp.");
            ((IArchiveRetainedFile)this).Review(copied,ct);var before=CaptureOuterMetadata(ct);
            WindowsArchiveNative.RequireOnlyDataStream(handle,copied.Version.Length);
            CopyOriginNoReplace(source.Origin,ct);
            File.SetLastWriteTimeUtc(handle,modified.ToDateTimeOffset().UtcDateTime);WindowsArchiveNative.Flush(handle);
            var after=CaptureOuterMetadata(ct);var observed=ObserveRetained(ct);
            if(after.Identity!=before.Identity||after.CreationTime!=before.CreationTime||after.Attributes!=before.Attributes||
                after.LinkCount!=before.LinkCount||!after.SecurityDescriptor.SequenceEqual(before.SecurityDescriptor)||
                observed.Version!=copied.Version||observed.Origin.Digest!=source.Origin.Digest||observed.MemberModified!=modified)
                throw new IOException("The private saved-copy metadata did not survive exact native review.");
            WindowsArchiveNative.RequireSnapshotStreams(handle,observed.Version.Length,source.Origin);
            parent.CheckPrivate(handle);parent.VerifyNamespace();parent.RequireName(KnownName,Identity);return observed;
        }
    }
    EditSaveCopyNameReview IEditSaveCopyPublicationStage.ReviewSaveCopy(ArchiveFileObservation prepared,string targetLeaf,CancellationToken ct)
    {
        lock(parent.Gate)
        {
            EnsureLive();ct.ThrowIfCancellationRequested();EditSaveCopyNames.Validate(targetLeaf);
            if(!artifact||!createdWritable||createdKind!=WindowsArchiveArtifactKind.Stage||!rebuildClaimed||evidenceClaimed||writable||namespaceChanged||
                !mayRename||saveCopyClaimAttempted||targetLeaf==KnownName)throw SaveCopyChanged();
            parent.VerifyNamespace();parent.CheckPrivate(handle);((IArchiveRetainedFile)this).Review(prepared,ct);
            WindowsArchiveNative.RequireSnapshotStreams(handle,prepared.Version.Length,prepared.Origin);
            var metadata=CaptureOuterMetadata(ct);
            if(metadata.Digest!=prepared.MetadataDigest||prepared.MemberModified is not {FitsTicks:true})throw SaveCopyChanged();
            return saveCopyReview=new(this,prepared,targetLeaf,metadata);
        }
    }
    void IEditSaveCopyPublicationStage.ClaimSaveCopy(EditSaveCopyNameReview review,CancellationToken ct)
    {
        lock(parent.Gate)
        {
            EnsureLive();review.RequireIssuer(this);
            if(review is not SaveCopyReview native||!ReferenceEquals(saveCopyReview,review)||saveCopyClaimAttempted||namespaceChanged)
                throw SaveCopyChanged();
            ct.ThrowIfCancellationRequested();WindowsArchiveNative.RequireSnapshotStreams(handle,review.Stage.Version.Length,review.Stage.Origin);
            saveCopyClaimAttempted=true;RenameNoReplace(native.TargetLeaf,native.Stage.Version,native.Metadata,ct);
        }
    }
    ArchiveFileObservation IEditSaveCopyPublicationStage.ReviewSavedCopy(EditSaveCopyNameReview review,CancellationToken ct)
    {
        lock(parent.Gate)
        {
            EnsureLive();review.RequireIssuer(this);ct.ThrowIfCancellationRequested();
            if(review is not SaveCopyReview native||!ReferenceEquals(saveCopyReview,review)||!saveCopyClaimAttempted||!namespaceChanged||
                writable||KnownName!=review.TargetLeaf)throw SaveCopyChanged();
            parent.VerifyNamespace();parent.RequireName(KnownName,Identity);parent.CheckPrivate(handle);
            WindowsArchiveNative.RequireSnapshotStreams(handle,review.Stage.Version.Length,review.Stage.Origin);
            var observed=ObserveRetained(ct);
            if(observed.Version!=review.Stage.Version||observed.MetadataDigest!=native.Metadata.Digest||observed.Origin.Digest!=review.Stage.Origin.Digest)
                throw SaveCopyChanged();
            return observed;
        }
    }
    private static IOException SaveCopyChanged()=>new("The saved copy's native name, bytes or metadata changed; retain its files and receipts for review.");
}
