using FileCat.Core.Edit;

namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveFile:IEditSaveCopyPublicationStage,IEditSaveCopyReceiptStage
{
    private sealed class SaveCopyReview(UnixArchiveFile issuer,ArchiveFileObservation stage,string leaf,ArchiveUnixOuterMetadata metadata)
        :EditSaveCopyNameReview(issuer,stage,leaf)
    {internal ArchiveUnixOuterMetadata Metadata { get; }=metadata;}
    private SaveCopyReview? saveCopyReview;
    private bool saveCopyClaimAttempted;
    void IEditSaveCopyReceiptStage.FlushReceipt(ArchiveFileObservation expected,CancellationToken ct)
    {
        lock(parent.Gate)
        {
            Live();ct.ThrowIfCancellationRequested();
            if(!artifact||!createdWritable||createdKind!=UnixArchiveArtifactKind.Manifest||!evidenceClaimed||rebuildClaimed||writable||namespaceChanged)
                throw SaveCopyChanged();
            ((IArchiveRetainedFile)this).Review(expected,ct);parent.CheckPrivate(held);
            UnixArchiveNative.SyncFile(held);ct.ThrowIfCancellationRequested();
            // Persist the newly created receipt's name before a later namespace mutation can occur.
            UnixArchiveNative.SyncDirectory(parent.Handle);
            parent.VerifyNamespace();parent.RequireName(KnownName,Identity);((IArchiveRetainedFile)this).Review(expected,ct);
        }
    }
    ArchiveFileObservation IEditSaveCopyStage.ApplyEditMetadata(ArchiveFileObservation copied,ArchiveFileObservation source,CancellationToken ct)
    {
        lock(parent.Gate)
        {
            Live();ct.ThrowIfCancellationRequested();
            if(!artifact||!createdWritable||createdKind!=UnixArchiveArtifactKind.Stage||!rebuildClaimed||evidenceClaimed||writable||namespaceChanged||
                source.MemberModified is not {FitsTicks:true} modified)
                throw new IOException("Saved-copy metadata requires this creator-owned sealed private stage and an exact timestamp.");
            ((IArchiveRetainedFile)this).Review(copied,ct);var before=CaptureOuterMetadata(ct);
            CopyOriginNoReplace(source.Origin,ct);
            File.SetLastWriteTimeUtc(held,modified.ToDateTimeOffset().UtcDateTime);UnixArchiveNative.SyncFile(held);
            var after=CaptureOuterMetadata(ct);var observed=ObserveRetained(ct);
            string attribute=UnixArchiveNative.Mac?"com.apple.quarantine":"user.xdg.origin.url";
            if(after.Native with{Changed=before.Native.Changed,Modified=before.Native.Modified}!=before.Native||
                after.AclPresent!=before.AclPresent||!after.AclBytes.SequenceEqual(before.AclBytes)||
                after.ExtendedAttributes is null||before.ExtendedAttributes is null||!after.ExtendedAttributes.MatchesExcept(before.ExtendedAttributes,attribute)||
                observed.Version!=copied.Version||observed.Origin.Digest!=source.Origin.Digest||observed.MemberModified!=modified)
                throw new IOException("The private saved-copy metadata did not survive exact native review.");
            parent.CheckPrivate(held);parent.VerifyNamespace();parent.RequireName(KnownName,Identity);return observed;
        }
    }
    EditSaveCopyNameReview IEditSaveCopyPublicationStage.ReviewSaveCopy(ArchiveFileObservation prepared,string targetLeaf,CancellationToken ct)
    {
        lock(parent.Gate)
        {
            Live();ct.ThrowIfCancellationRequested();EditSaveCopyNames.Validate(targetLeaf);
            if(!artifact||!createdWritable||createdKind!=UnixArchiveArtifactKind.Stage||!rebuildClaimed||evidenceClaimed||writable||namespaceChanged||
                saveCopyClaimAttempted||targetLeaf==KnownName)throw SaveCopyChanged();
            parent.VerifyNamespace();parent.CheckCreationNamespace();parent.CheckPrivate(held);
            ((IArchiveRetainedFile)this).Review(prepared,ct);
            var metadata=CaptureOuterMetadata(ct);
            if(metadata.Digest!=prepared.MetadataDigest||prepared.MemberModified is not {FitsTicks:true})throw SaveCopyChanged();
            metadata.RequireSupportedExtendedAttributes();
            return saveCopyReview=new(this,prepared,targetLeaf,metadata);
        }
    }
    void IEditSaveCopyPublicationStage.ClaimSaveCopy(EditSaveCopyNameReview review,CancellationToken ct)
    {
        lock(parent.Gate)
        {
            Live();review.RequireIssuer(this);
            if(review is not SaveCopyReview native||!ReferenceEquals(saveCopyReview,review)||saveCopyClaimAttempted||namespaceChanged)
                throw SaveCopyChanged();
            ct.ThrowIfCancellationRequested();parent.CheckCreationNamespace();saveCopyClaimAttempted=true;
            RenameNoReplace(native.TargetLeaf,native.Stage.Version,native.Metadata,ct);
        }
    }
    ArchiveFileObservation IEditSaveCopyPublicationStage.ReviewSavedCopy(EditSaveCopyNameReview review,CancellationToken ct)
    {
        lock(parent.Gate)
        {
            Live();review.RequireIssuer(this);ct.ThrowIfCancellationRequested();
            if(review is not SaveCopyReview native||!ReferenceEquals(saveCopyReview,review)||!saveCopyClaimAttempted||!namespaceChanged||
                writable||KnownName!=review.TargetLeaf)throw SaveCopyChanged();
            parent.VerifyNamespace();parent.CheckCreationNamespace();parent.RequireName(KnownName,Identity);parent.CheckPrivate(held);
            var actual=CaptureOuterMetadata(ct);var before=native.Metadata;
            if(actual.Native with{Changed=before.Native.Changed}!=before.Native||actual.AclPresent!=before.AclPresent||
                !actual.AclBytes.SequenceEqual(before.AclBytes)||actual.ExtendedAttributes?.Digest!=before.ExtendedAttributes?.Digest||
                actual.Origin.Digest!=before.Origin.Digest)throw SaveCopyChanged();
            var observed=ObserveRetained(ct);if(observed.Version!=review.Stage.Version)throw SaveCopyChanged();return observed;
        }
    }
    private static IOException SaveCopyChanged()=>new("The saved copy's native name, bytes or metadata changed; retain its files and receipts for review.");
}
