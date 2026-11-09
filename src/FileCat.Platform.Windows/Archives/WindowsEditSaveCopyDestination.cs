using FileCat.Core.Archives;
using FileCat.Core.Edit;

namespace FileCat.Platform.Windows.Archives;

internal sealed class WindowsEditSaveCopyDestination:EditSaveCopyDestination
{
    private readonly WindowsArchiveDirectory directory;
    private WindowsEditSaveCopyDestination(WindowsArchiveDirectory directory)=>this.directory=directory;
    internal static EditSaveCopyDestination Open(string path,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();return new WindowsEditSaveCopyDestination(WindowsArchiveDirectory.Open(path));}
    internal override string Path=>directory.DisplayPath;
    internal override string Identity=>directory.Identity.ToString();
    internal override IArchiveDestinationSpace Space=>directory;
    internal override ArchiveArtifactReservation Reserve(Guid nonce,ArchiveArtifactKind kind,long maximum,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();return directory.ReserveArtifact(nonce,(WindowsArchiveArtifactKind)kind,maximum);}
    internal override IArchiveRetainedStage Create(ArchiveArtifactReservation reservation,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();return directory.CreateArtifact(reservation);}
    internal override IArchiveRetainedArtifact? OpenReadOrAbsent(string leaf,long maximum,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();return directory.OpenSavedCopyOrAbsent(leaf,maximum);}
    internal override void Review(CancellationToken ct){ct.ThrowIfCancellationRequested();directory.VerifyNamespace();}
    public override void Dispose()=>directory.Dispose();
}
