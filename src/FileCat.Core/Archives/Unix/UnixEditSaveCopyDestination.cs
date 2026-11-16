using FileCat.Core.Edit;

namespace FileCat.Core.Archives.Unix;

internal sealed class UnixEditSaveCopyDestination:EditSaveCopyDestination
{
    private readonly UnixArchiveDirectory directory;
    private UnixEditSaveCopyDestination(UnixArchiveDirectory directory)=>this.directory=directory;
    internal static EditSaveCopyDestination Open(string path,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();return new UnixEditSaveCopyDestination(UnixArchiveDirectory.Open(path));}
    internal override string Path=>directory.DisplayPath;
    internal override string Identity=>directory.Identity.ToString();
    internal override IArchiveDestinationSpace Space=>directory;
    internal override ArchiveArtifactReservation Reserve(Guid nonce,ArchiveArtifactKind kind,long maximum,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();return directory.ReserveArtifact(nonce,(UnixArchiveArtifactKind)kind,maximum);}
    internal override IArchiveRetainedStage Create(ArchiveArtifactReservation reservation,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();return directory.CreateArtifact(reservation);}
    internal override IArchiveRetainedArtifact? OpenReadOrAbsent(string leaf,long maximum,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();return directory.OpenSavedCopyOrAbsent(leaf,maximum);}
    internal override void Review(CancellationToken ct){ct.ThrowIfCancellationRequested();directory.VerifyNamespace();}
    public override void Dispose()=>directory.Dispose();
}
