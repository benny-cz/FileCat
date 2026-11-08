using FileCat.Core.Archives;
using FileCat.Core.Edit;

namespace FileCat.Platform.Windows.Archives;

internal sealed partial class WindowsArchiveFile:IEditSaveCopyStage
{
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
}
