using FileCat.Core.Edit;

namespace FileCat.Core.Archives.Unix;

internal sealed partial class UnixArchiveFile:IEditSaveCopyStage
{
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
}
